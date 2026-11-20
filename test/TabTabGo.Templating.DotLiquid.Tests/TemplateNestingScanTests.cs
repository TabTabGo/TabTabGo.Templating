using DotLiquid;
using DotLiquid.Exceptions;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>
/// S-13, security review of the nesting scan: it tokenizes as DotLiquid does, lowers the depth only for an end tag that closes the innermost open block, skips
/// raw and comment bodies, and the parse itself runs on a large stack so that even an undercounting scan cannot crash the process.
/// </summary>
public sealed class TemplateNestingScanTests
{
    private static string Decoy(int levels, string decoy) =>
        string.Concat(Enumerable.Repeat("{% if a %}" + decoy, levels)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", levels));

    [Fact]
    public void RawAndCommentBodies_AreSkipped_AsDotLiquidTokenizesThem()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 2 });
        var hidden = string.Concat(Enumerable.Repeat("{% if a %}", 50));

        Assert.Equal(hidden, engine.RenderString("{% raw %}" + hidden + "{% endraw %}", new { }));

        // the tokenizer returns a comment body as one text token, so the blocks inside it are never parsed
        Assert.Equal("ok", engine.RenderString("{% comment %}" + hidden + "{% endcomment %}ok", new { }));
        Assert.Equal(2, TemplateNesting.Depth("{% if a %}{% comment %}{% if a %}{% if a %}{% endcomment %}{% endif %}"));
        Assert.Equal(1, TemplateNesting.Depth("{% comment %}{% if a %}{% endcomment %}{% if a %}{% endif %}"));
    }

    [Fact]
    public void OutputTags_ContainingTagText_DoNotDesynchroniseTheScan()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 3 });
        var decoys = string.Concat(Enumerable.Repeat("{{ '{% if a %}' }}", 50));

        Assert.Equal(string.Concat(Enumerable.Repeat("{% if a %}", 50)), engine.RenderString(decoys, new { }));
        Assert.Equal(0, TemplateNesting.Depth(decoys));

        // an output tag that holds a tag end must not hide real blocks either
        Assert.Equal(4, TemplateNesting.Depth("{% if a %}{{ '%}' }}{% if a %}{{ '{%' }}{% if a %}{% if a %}{% endif %}{% endif %}{% endif %}{% endif %}"));
    }

    [Theory]
    [InlineData("{% endfoo %}")]
    [InlineData("{% endfor %}")]
    [InlineData("{% endunless %}{% endfor %}{% endcase %}")]
    public void MismatchedEndTags_NeverLowerTheCountedDepth(string decoy)
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 100 });

        foreach (var levels in new[] { 150, 3000 })
        {
            var error = Assert.Throws<SyntaxException>(() => engine.RenderString(Decoy(levels, decoy), new { a = true }));
            Assert.Contains("MaxNestingDepth", error.Message);
        }
    }

    [Fact]
    public void InterleavedBlocks_AreCountedByTheirOpenTags()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 100 });

        var error = Assert.Throws<SyntaxException>(() => engine.RenderString(string.Concat(Enumerable.Repeat("{% if a %}{% endfor %}", 150)), new { a = true }));

        Assert.Contains("MaxNestingDepth", error.Message);
    }

    [Fact]
    public void WhitespaceControlTags_AreCountedLikeAnyOther()
    {
        Assert.Equal(3, TemplateNesting.Depth("{%- if a -%}{%- for i in x -%}{%- unless a -%}{%- endunless -%}{%- endfor -%}{%- endif -%}"));
        Assert.Equal(1, TemplateNesting.Depth("{%-if a-%}{%-endif-%}"));
    }

    [Fact]
    public void TheParseBackstop_SurvivesATemplateTheScanDoesNotLimit()
    {
        // 20 000 nested blocks overflow the main thread's stack (8 MB); the parse runs on its own large stack, whoever calls it
        var template = string.Concat(Enumerable.Repeat("{% if a %}", 20_000)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 20_000));
        Template? parsed = null;
        var thread = new Thread(() => parsed = TemplateNesting.Parse(template, int.MaxValue), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(parsed);
    }

    /// <summary>The block nesting of a parsed template, measured on DotLiquid's own parse tree.</summary>
    private static int ParsedDepth(object node)
    {
        if (node is not Block block)
        {
            return 0;
        }

        var inner = 0;
        foreach (var child in block.NodeList ?? new List<object>())
        {
            inner = Math.Max(inner, ParsedDepth(child));
        }

        // the branches of if / unless / case keep their bodies in the conditions (a protected member of DotLiquid)
        var branches = node.GetType().GetProperty("Blocks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(node) as System.Collections.IEnumerable;
        foreach (var condition in branches?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            var attachment = condition.GetType().GetProperty("Attachment")?.GetValue(condition) as System.Collections.IEnumerable;
            foreach (var child in attachment?.Cast<object>() ?? Enumerable.Empty<object>())
            {
                inner = Math.Max(inner, ParsedDepth(child));
            }
        }

        return (node is Document ? 0 : 1) + inner;
    }

    [Fact]
    public void Fuzz_TheScanNeverUndercountsWhatDotLiquidParses()
    {
        string[] pieces =
        {
            "{% if a %}", "{% endif %}", "{% for i in x %}", "{% endfor %}", "{% unless a %}", "{% endunless %}", "{% else %}", "{% elsif a %}", "{% endfoo %}",
            "{%- if a -%}", "{%- endif -%}", "{{ '{% if a %}' }}", "{{ a }}", "{{ '%}' }}", "{% raw %}", "{% endraw %}", "{% comment %}", "{% endcomment %}",
            "{% capture c %}", "{% endcapture %}", "{% assign q = 1 %}", "text ", "{# note #}", "{% case a %}", "{% when 1 %}", "{% endcase %}",
        };
        var random = new Random(20261120);
        var parsedCount = 0;

        for (var round = 0; round < 3000; round++)
        {
            var template = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => pieces[random.Next(pieces.Length)]));
            int counted;
            Template parsed;
            try
            {
                counted = TemplateNesting.Depth(template);
                parsed = Template.Parse(template);
            }
            catch (Exception ex) when (ex is SyntaxException or System.ArgumentException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
            {
                continue; // DotLiquid refuses it; the scan only has to be safe for what parses
            }

            parsedCount++;
            var actual = ParsedDepth(parsed.Root!);
            Assert.True(counted >= actual, $"scan {counted} < parsed {actual} for: {template}");
        }

        Assert.True(parsedCount > 20, $"only {parsedCount} random templates parsed");
    }

    private static string Structured(Random random, int depth)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = random.Next(0, 4); i > 0; i--)
        {
            switch (depth > 8 ? random.Next(3) : random.Next(11))
            {
                case 0: builder.Append("text "); break;
                case 1: builder.Append("{{ '{% if a %}' }}"); break;
                case 2: builder.Append("{{ a | Append: '%}' }}"); break;
                case 3: builder.Append("{% if a %}").Append(Structured(random, depth + 1)).Append(random.Next(3) == 0 ? "{% else %}" + Structured(random, depth + 1) : string.Empty).Append("{% endif %}"); break;
                case 4: builder.Append("{%- for i in x -%}").Append(Structured(random, depth + 1)).Append("{%- endfor -%}"); break;
                case 5: builder.Append("{% unless a %}").Append(Structured(random, depth + 1)).Append("{% endunless %}"); break;
                case 6: builder.Append("{% capture c %}").Append(Structured(random, depth + 1)).Append("{% endcapture %}"); break;
                case 7: builder.Append("{% comment %}").Append(Structured(random, depth + 1)).Append("{% endcomment %}"); break;
                case 8: builder.Append("{% raw %}{% if a %}{% endraw %}"); break;
                case 9: builder.Append("{% case a %}{% when 1 %}").Append(Structured(random, depth + 1)).Append("{% endcase %}"); break;
                default: builder.Append("{% if a %}").Append(Structured(random, depth + 1)).Append("{% endfoo %}{% endif %}"); break; // a decoy end tag
            }
        }

        return builder.ToString();
    }

    [Fact]
    public void Fuzz_WellFormedTemplates_TheScanNeverUndercounts_AndMatchesTheirNestingWhenNoDecoyIsUsed()
    {
        var random = new Random(77);
        var parsedCount = 0;
        for (var round = 0; round < 2000; round++)
        {
            var template = Structured(random, 0);
            Template parsed;
            try
            {
                parsed = Template.Parse(template);
            }
            catch (SyntaxException)
            {
                continue; // the decoy end tags make some of them invalid
            }

            parsedCount++;
            var actual = ParsedDepth(parsed.Root!);
            var counted = TemplateNesting.Depth(template);
            Assert.True(counted >= actual, $"scan {counted} < parsed {actual} for: {template}");
            if (!template.Contains("endfoo"))
            {
                Assert.True(counted == actual, $"scan {counted} != parsed {actual} for: {template}"); // exact on valid templates, so nothing is over-refused
            }
        }

        Assert.True(parsedCount > 500, $"only {parsedCount} structured templates parsed");
    }
}
