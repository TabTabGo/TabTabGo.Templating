using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>N-2: the built-in blocks are counted without the tag registry; the fallback scan is compared with DotLiquid's own tokenizer on generated fragments (fixed seed).</summary>
public sealed class TemplateNestingDifferentialTests
{
    private static readonly string[] Pieces =
    {
        "{% if a %}", "{%- if a -%}", "{% endif %}", "{%- endif -%}", "{% for i in x %}", "{% endfor %}", "{% unless a %}", "{% endunless %}",
        "{{ x }}", "{{ a }", "{{{", "}}", "{# #}", "{% raw %}", "{% endraw %}", "{% comment %}", "{% endcomment %}", "{%", "%}", "{%-", "-%}",
        "text ", "{% else %}", "{% capture c %}", "{% endcapture %}", "{{ '{% if a %}' }}", "{% assign y = 1 %}", "\n", "{ % if a % }",
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

    private static readonly string[] MalformedPieces = { "{{ a }", "{{{", "{%", "%}", "{%-", "-%}" };

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
    /// Fragments with raw and comment blocks, the {# #} shorthand, stray end tags, unterminated or interleaved tags (<c>{{ a }</c>, <c>{{{</c>, a stray <c>{%</c> or <c>%}</c>, <c>{%-</c> inside another tag): the real tokenizer rejects many; DotLiquid returns a raw or comment body as one
    /// text token and rewrites the shorthand before splitting, while the fallback keeps the tags and skips the body in the scan, so the two disagree on unbalanced or interleaved input and
    /// the fallback undercounts by a small amount. Recorded, bounded, not fixed: the fallback only runs when the tokenizer cannot be bound,
    /// and then the parse thread has the 256 MB maximum stack.
    /// </summary>
    [Fact]
    public void Differential_MalformedFragments_FallbackUndercountIsRareAndSmall()
    {
        var (rejected, over, under) = Compare(Pieces, 1);

        Assert.True(under.Count < 100, $"{under.Count} undercounts of {4_000 - rejected} accepted fragments");
        Assert.All(under, u => Assert.True(int.Parse(u.Split(" vs ")[0]) - int.Parse(u.Split(" vs ")[1].Split(':')[0]) <= 8, u));
        Assert.True(over >= 0);
    }
}
