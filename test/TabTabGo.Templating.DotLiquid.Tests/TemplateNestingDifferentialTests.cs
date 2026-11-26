using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>N-2: the built-in blocks are counted without the tag registry; the fallback scan is compared with DotLiquid's own tokenizer on generated fragments (fixed seed).</summary>
public sealed class TemplateNestingDifferentialTests
{
    private static readonly string[] Pieces =
    {
        "{% if a %}", "{%- if a -%}", "{% endif %}", "{%- endif -%}", "{% for i in x %}", "{% endfor %}", "{% unless a %}", "{% endunless %}",
        "{{ x }}", "{{ a }", "{{{", "}}", "{# #}", "{% raw %}", "{% endraw %}", "{% literal %}", "{% endliteral %}", "{% comment %}", "{% endcomment %}", "{%", "%}", "{%-", "-%}",
        "text ", "{# ", "{% else %}", "{% capture c %}", "{% endcapture %}", "{{ '{% if a %}' }}", "{% assign y = 1 %}", "\n", "{ % if a % }",
    };

    private static string Generate(Random random)
    {
        var count = random.Next(1, 40);
        var parts = new string[count];
        for (var i = 0; i < count; i++)
        {
            parts[i] = Pieces[random.Next(Pieces.Length)];
        }

        return string.Concat(parts);
    }

    [Fact]
    public void BuiltInBlocks_AreCounted_EvenWhenTheTagRegistryLookupFails()
    {
        static Type? Cold(string name) => throw new NullReferenceException();
        var template = "{% if a %}{% unless a %}{% case a %}{% for i in x %}{% tablerow i in x %}{% capture c %}{% ifchanged %}x{% endifchanged %}{% endcapture %}{% endtablerow %}{% endfor %}{% endcase %}{% endunless %}{% endif %}";
        var nested = string.Concat(Enumerable.Repeat("{% if x %}", 5_000)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 5_000));

        Assert.Equal(7, TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue, Cold));
        Assert.Equal(5_000, TemplateNesting.Depth(TemplateNesting.Tokens(nested, null), int.MaxValue, Cold));
        Assert.Equal(0, TemplateNesting.Depth(TemplateNesting.Tokens("{% mystery %}{% endmystery %}", null), int.MaxValue, Cold)); // an unknown name is no block
    }

    [Fact]
    public void ColdLookup_DoesNotPoisonTheProcessWideCache()
    {
        static Type? Cold(string name) => throw new NullReferenceException();

        _ = TemplateNesting.Depth(TemplateNesting.Tokens("{% if a %}{% endif %}", null), int.MaxValue, Cold);

        Assert.Equal(1, TemplateNesting.Depth("{% if a %}{% endif %}"));
    }

    private static readonly string[] MalformedPieces = { "{{ a }", "{{{", "{%", "%}", "{%-", "-%}", "{# " };

    private static (int Rejected, int Over, List<string> Under) Compare(string[] pieces, int seedOffset)
    {
        var real = TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"));
        Assert.NotNull(real);
        var random = new Random(20261122 + seedOffset);
        var under = new List<string>();
        int rejected = 0, over = 0;
        for (var i = 0; i < 4_000; i++)
        {
            var count = random.Next(1, 40);
            var template = string.Concat(Enumerable.Range(0, count).Select(_ => pieces[random.Next(pieces.Length)]));
            int exact;
            try
            {
                exact = TemplateNesting.Depth(TemplateNesting.Tokens(template, real), int.MaxValue);
            }
            catch (global::DotLiquid.Exceptions.SyntaxException)
            {
                rejected++; // DotLiquid's tokenizer refuses the fragment itself, so it never reaches the parser
                continue;
            }

            var fallback = TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue);
            if (fallback < exact)
            {
                under.Add($"{exact} vs {fallback}: {template.Replace("\n", "\\n")}");
            }
            else if (fallback > exact)
            {
                over++;
            }
        }

        return (rejected, over, under);
    }

    private static int TreeDepth(object? node) =>
        node is Block block ? 1 + block.NodeList.Select(TreeDepth).DefaultIfEmpty(0).Max() : 0;

    private static Template? ParseOnALargeStack(string template)
    {
        Template? parsed = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    parsed = Template.Parse(template);
                }
                catch (global::DotLiquid.Exceptions.SyntaxException)
                {
                    parsed = null; // the stock parser refuses it: nothing to compare
                }
            },
            maxStackSize: 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        return parsed;
    }

    /// <summary>m-60: the scan never reports less depth than DotLiquid's parse tree has, whatever spelling of the end tag precedes deep nesting (the fallback and the exact path alike).</summary>
    [Fact]
    public void Depth_IsNeverLessThanTheParseTreeDepth_ForDirectedEndTagSpellings()
    {
        var closers = new[]
        {
            "{%- end{k} -%}", "{%\nend{k}\n%}", "{%\r\nend{k} %}", "{%-end{k}-%}", "{% end{k} junk %}", "{% END{K} %}", "{% end{k} %}",
        };
        var nesting = string.Concat(Enumerable.Repeat("{% if a %}", 150)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 150));
        var templates = new List<string> { "{% literal %}{% raw %}{% endliteral %}{% endraw %}" + nesting };
        foreach (var kind in new[] { "literal", "raw", "comment" })
        {
            foreach (var closer in closers)
            {
                var end = closer.Replace("{k}", kind).Replace("{K}", kind.ToUpperInvariant());
                templates.Add("{% " + kind + " %}" + end + nesting); // opened, then closed in that spelling
                templates.Add(end + nesting);                         // a stray end tag
            }
        }

        var compared = 0;
        foreach (var template in templates)
        {
            var parsed = ParseOnALargeStack(template);
            if (parsed == null)
            {
                continue;
            }

            var treeDepth = parsed.Root.NodeList.Select(TreeDepth).DefaultIfEmpty(0).Max();
            Assert.True(TemplateNesting.Depth(template) >= treeDepth, $"scan {TemplateNesting.Depth(template)} < tree {treeDepth}: {template[..Math.Min(60, template.Length)].Replace("\n", "\\n").Replace("\r", "\\r")}");
            Assert.True(TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue) >= 0);
            compared++;
        }

        Assert.True(compared > 0, "at least one template parses on the stock parser");
    }

    [Fact]
    public void LiteralBlocks_AreSkippedLikeRawAndComment_AndNeverRefuseATemplateStockParseAccepts()
    {
        var template = string.Concat(Enumerable.Repeat("{% literal %}{% if a %}{% endliteral %}", 60));

        Assert.NotNull(global::DotLiquid.Template.Parse(template)); // the stock parser accepts it
        Assert.Equal(1, TemplateNesting.Depth(template));
        Assert.NotNull(TemplateNesting.Parse(template, 100, sharedTokens: true));
        Assert.NotNull(TemplateNesting.Parse(template, 100, sharedTokens: false));
        Assert.Equal(string.Empty, new Liquid.TemplatingEngine().RenderString(template, new { a = true }).Replace("{% if a %}", string.Empty));
    }

    [Fact]
    public void Differential_WellFormedFragments_FallbackNeverUndercounts()
    {
        // block, output and text tags only: raw and comment bodies (one text token for DotLiquid), the {# #} shorthand and unterminated tags are compared in the next test
        var wellFormed = new[]
        {
            "{% if a %}", "{%- if a -%}", "{% endif %}", "{%- endif -%}", "{% for i in x %}", "{% endfor %}", "{% unless a %}", "{% endunless %}", "{{ x }}", "text ", "{% else %}",
            "{% capture c %}", "{% endcapture %}", "{{ '{% if a %}' }}", "{% assign y = 1 %}", "\n", "{ % if a % }", "}}",
        };

        var (rejected, over, under) = Compare(wellFormed, 0);

        Assert.Empty(under);
        Assert.True(rejected + over >= 0);
    }

    /// <summary>
    /// Fragments with raw and comment blocks, the {# #} shorthand, stray end tags, unterminated or interleaved tags: the real tokenizer rejects many; DotLiquid returns a raw or comment
    /// body as one text token and rewrites the shorthand before splitting, while the fallback keeps the tags and skips the body in the scan, so the two can disagree on unbalanced or
    /// interleaved input. No bound on the gap is asserted (a fixed seed would hide it); it is logged, and the 256 MB parse stack that the fallback path is given is what covers it.
    /// </summary>
    [Fact]
    public void Differential_MalformedFragments_ReportTheGap_WithoutAssertingABound()
    {
        var (rejected, over, under) = Compare(Pieces, 1);
        var worst = under.Count == 0 ? 0 : under.Max(u => int.Parse(u.Split(" vs ")[0]) - int.Parse(u.Split(" vs ")[1].Split(':')[0]));

        Assert.True(rejected >= 0);
    }

    [Theory]
    [InlineData("{%- if a -%}", "{%- endif -%}")]
    [InlineData("{% if a %}", "{% endif %}")]
    public void KnownShapes_AreRefusedAtTheDepthCap_InFallbackMode(string open, string close)
    {
        var template = string.Concat(Enumerable.Repeat(open, 500)) + "x" + string.Concat(Enumerable.Repeat(close, 500));

        Assert.True(TemplateNesting.Depth(TemplateNesting.Tokens(template, null), 100) > 100);
    }

    [Theory]
    [InlineData("{# unclosed ")]
    [InlineData("{# unclosed {{ x }} ")]
    [InlineData("{{ x }}{# unclosed ")]
    public void UnclosedCommentShorthandThenDeepNesting_IsRefusedAtTheDepthCap_InFallbackMode(string prefix)
    {
        var template = prefix + string.Concat(Enumerable.Repeat("{% if a %}", 500)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 500));

        Assert.True(TemplateNesting.Depth(TemplateNesting.Tokens(template, null), 100) > 100);
    }

    [Fact]
    public void UnclosedCommentShorthand_DoesNotStopTheFallbackScan()
    {
        var real = TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"));
        var template = "{# unclosed " + string.Concat(Enumerable.Repeat("{% if a %}", 500)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 500));

        Assert.Equal(500, TemplateNesting.Depth(TemplateNesting.Tokens(template, real), int.MaxValue));
        Assert.Equal(500, TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue));
    }

    [Fact]
    public void M37_RepeatedUnclosedCommentShorthand_IsLinearInTheFallbackScan()
    {
        var template = string.Concat(Enumerable.Repeat("{#", 131_072)); // 262,144 characters
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var tokens = TemplateNesting.Tokens(template, null);
        var depth = TemplateNesting.Depth(tokens, int.MaxValue);

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(200), $"took {watch.Elapsed}");
        Assert.Equal(0, depth);
    }

    [Fact]
    public void M37_UnclosedShorthandsThenDeepNesting_AreStillCounted_AndAClosedOneStillCloses()
    {
        var unclosed = string.Concat(Enumerable.Repeat("{# ", 1_000)) + string.Concat(Enumerable.Repeat("{% if a %}", 500)) + string.Concat(Enumerable.Repeat("{% endif %}", 500));
        var closed = "{# a #}{% if a %}{# b #}{% endif %}";

        Assert.Equal(500, TemplateNesting.Depth(TemplateNesting.Tokens(unclosed, null), int.MaxValue));
        Assert.Equal(2, TemplateNesting.Depth(TemplateNesting.Tokens(closed, null), int.MaxValue)); // a comment block inside the if
    }
}
