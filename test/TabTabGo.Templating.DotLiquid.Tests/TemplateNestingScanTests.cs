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
        // thousands of nested blocks overflow a 256 KB caller stack (20 000 overflow the main thread's 8 MB); the parse runs on its own large stack, whoever calls it
        var template = string.Concat(Enumerable.Repeat("{% if a %}", 5_000)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 5_000));
        Template? parsed = null;
        var thread = new Thread(() => parsed = TemplateNesting.Parse(template, int.MaxValue), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(parsed);
    }

    [Fact]
    public void AShorthandComment_IsExpandedBeforeScanning_AsDotLiquidDoes()
    {
        // DotLiquid expands a template that is one {# ... #} into a comment block first; read as plain tokens its inner tags would count
        const string whole = "{# {% if a %}{% if a %}{% if a %}{% endif %} #}";
        Assert.Equal(1, TemplateNesting.Depth(whole));
        Assert.Equal(string.Empty, new Liquid.TemplatingEngine().RenderString(whole, new { a = true }));

        // anywhere else DotLiquid tokenizes {# and #} as text, so a tag inside them is a real tag, and a fake end tag there is counted as the parser counts it
        const string inside = "{% if a %}{# {% endif %} #}{% if a %}{% endif %}{% endif %}";
        Assert.Equal(1, TemplateNesting.Depth(inside));
        Assert.Throws<SyntaxException>(() => Template.Parse(inside)); // the parser rejects what the scan saw as closing early
    }

    [Fact]
    public void TheScan_GivesTheSameDepthAtSyntaxLevel20And22()
    {
        string[] corpus =
        {
            "{% if a %}{% for i in x %}{% unless a %}{% endunless %}{% endfor %}{% endif %}",
            "{{ ' {% if a %} ' }}{% if a %}{# {% endif %} #}{% raw %}{% if a %}{% endraw %}{% endif %}",
            "{%- if a -%}{%-endif-%}{% comment %}{% if a %}{% endcomment %}{% capture c %}{% case a %}{% when 1 %}{% endcase %}{% endcapture %}",
        };
        var original = Template.DefaultSyntaxCompatibilityLevel;
        try
        {
            Template.DefaultSyntaxCompatibilityLevel = SyntaxCompatibility.DotLiquid20;
            var at20 = corpus.Select(t => TemplateNesting.Depth(t)).ToArray();
            Template.DefaultSyntaxCompatibilityLevel = SyntaxCompatibility.DotLiquid22;
            var at22 = corpus.Select(t => TemplateNesting.Depth(t)).ToArray();

            Assert.Equal(at20, at22);
            Assert.All(at20, depth => Assert.True(depth > 0));
        }
        finally
        {
            Template.DefaultSyntaxCompatibilityLevel = original;
        }
    }

    [Theory]
    [InlineData("{% else %}")]
    [InlineData("{% zzunknown %}")]
    public void ContinuationAndUnregisteredTags_CostAtMostOneRegistryLookupPerDistinctName(string tag)
    {
        // DotLiquid throws a NullReferenceException for a name it does not register; asked once per tag that was half of the scan time. Counted, not timed.
        var template = "{% if a %}" + string.Concat(Enumerable.Repeat(tag, 3000)) + "{% endif %}";
        var before = TemplateNesting.RegistryLookups;

        Assert.Equal(1, TemplateNesting.Depth(template));

        Assert.True(TemplateNesting.RegistryLookups - before <= 2, $"{TemplateNesting.RegistryLookups - before} registry lookups"); // "if" (first time only) and the tag name
    }

    private static readonly string[] ShorthandCorpus =
    {
        "{# {{ a }} {% if a %} #}",                                   // the whole template is one shorthand comment holding output and tag text
        "{#{% if a %}{% if a %}{% if a %}#}",
        "{# { } {{ #}",                                               // nested braces
        "before {# {{ a }} #} after {% if a %}x{% endif %}",           // inside other text DotLiquid leaves {# and #} as text
        "{{{ a }}}",                                                  // the literal shorthand
        "{{{ {% if a %} }}}",
        "{%- if a -%}  x  {%- endif -%}{{- a -}}",
        "{% raw %}{# {{ a }} #}{% endraw %}",
        "{% comment %}{{ a }}{% endcomment %}{{ a | Nofilter }}",       // a failing filter: the error text must be the same
        "{{ a | Append: '{# #}' }}{# unterminated",
        "{% if a %}{# x #}{% else %}{# y #}{% endif %}",
        string.Empty,
    };

    [Theory]
    [InlineData(SyntaxCompatibility.DotLiquid20)]
    [InlineData(SyntaxCompatibility.DotLiquid21)]
    [InlineData(SyntaxCompatibility.DotLiquid22)]
    public void TheSharedTokenParse_PreprocessesLikeTemplateParse_ForEveryShorthand_AtEverySyntaxLevel(SyntaxCompatibility level)
    {
        var original = Template.DefaultSyntaxCompatibilityLevel;
        var data = global::DotLiquid.Hash.FromAnonymousObject(new { a = true, x = new[] { 1, 2 } });
        try
        {
            Template.DefaultSyntaxCompatibilityLevel = level;
            foreach (var source in ShorthandCorpus)
            {
                Template stock;
                try
                {
                    stock = Template.Parse(source);
                }
                catch (SyntaxException expected)
                {
                    var actual = Assert.Throws<SyntaxException>(() => TemplateNesting.Parse(source, int.MaxValue));
                    Assert.Equal(expected.Message, actual.Message);
                    continue;
                }

                var shared = TemplateNesting.Parse(source, int.MaxValue);

                Assert.Equal(ParsedDepth(stock.Root!), ParsedDepth(shared.Root!));
                Assert.Equal(stock.Render(data), shared.Render(data));
                Assert.Equal(stock.Errors.Count, shared.Errors.Count);
                foreach (var mode in new[] { global::DotLiquid.ErrorsOutputMode.Display, global::DotLiquid.ErrorsOutputMode.Suppress })
                {
                    var parameters = () => new RenderParameters(System.Globalization.CultureInfo.InvariantCulture) { LocalVariables = data, ErrorsOutputMode = mode };
                    Assert.Equal(stock.Render(parameters()), shared.Render(parameters()));
                }

                shared.MakeThreadSafe(); // as the engine does for cached files: it must work on a tree built the shared-token way
                Assert.Equal(stock.Render(data), shared.Render(data));
            }
        }
        finally
        {
            Template.DefaultSyntaxCompatibilityLevel = original;
        }
    }

    [Fact]
    public void TemplateParseInternal_DoesExactlyWhatTheSharedTokenParseReplicates()
    {
        // Fails closed when a DotLiquid release adds a step to ParseInternal that TemplateNesting.Parse does not replicate (the package is bound to [2.3.260, 2.4.0), risk R8)
        var method = typeof(Template).GetMethod("ParseInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<string>();
        var fieldStores = 0;
        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is 0x28 or 0x6F or 0x73)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), null, null)!;
                calls.Add(called.DeclaringType!.Name + "." + called.Name);
                i += 4;
            }
            else if (il[i] is 0x7D)
            {
                fieldStores++;
            }
        }

        Assert.Equal(new[] { "Document..ctor", "Template.set_Root", "Template.get_Root", "Tokenizer.Tokenize", "Tag.Initialize" }, calls);
        Assert.Equal(0, fieldStores);
    }

    [Fact]
    public void TheSharedTokenParse_SeesTheCallersCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR", "ar-SA" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
                const string source = "{{ 1.5 | Plus: 2.25 }}{{ 'I' | Downcase }}{% assign n = 3.5 %}{{ n | Times: 2 }}";

                Assert.Equal(Template.Parse(source).Render(), TemplateNesting.Parse(source, int.MaxValue).Render());
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TheSharedTokenParse_BuildsTheSameTreeAsTemplateParse()
    {
        var random = new Random(4242);
        var data = global::DotLiquid.Hash.FromAnonymousObject(new { a = true, x = new[] { 1, 2, 3 } });
        var compared = 0;
        for (var round = 0; round < 300; round++)
        {
            var source = Structured(random, 0).Replace("{% endfoo %}", string.Empty);
            Template stock;
            try
            {
                stock = Template.Parse(source);
            }
            catch (SyntaxException)
            {
                Assert.Throws<SyntaxException>(() => TemplateNesting.Parse(source, int.MaxValue)); // refused the same way
                continue;
            }

            var shared = TemplateNesting.Parse(source, int.MaxValue);
            compared++;
            Assert.Equal(ParsedDepth(stock.Root!), ParsedDepth(shared.Root!));
            Assert.Equal(stock.Render(data), shared.Render(data));
        }

        Assert.True(compared > 100, $"only {compared} templates compared");
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

        for (var round = 0; round < 1500; round++)
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

        Assert.True(parsedCount > 10, $"only {parsedCount} random templates parsed");
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
                case 8: builder.Append(random.Next(2) == 0 ? "{% raw %}{% if a %}{% endraw %}" : "{# {{ a }} #}"); break;
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
        for (var round = 0; round < 1000; round++)
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

        Assert.True(parsedCount > 250, $"only {parsedCount} structured templates parsed");
    }

    [Fact]
    public void SharedTokenParse_IsUsedOnlyForDotLiquid2_3_260()
    {
        Assert.True(TemplateNesting.SharedTokenParseSupported(typeof(Template).Assembly.GetName().Version));
        Assert.False(TemplateNesting.SharedTokenParseSupported(new Version(2, 3, 261, 0)));
        Assert.False(TemplateNesting.SharedTokenParseSupported(new Version(2, 2, 692, 0)));
        Assert.False(TemplateNesting.SharedTokenParseSupported(null));
    }
}
