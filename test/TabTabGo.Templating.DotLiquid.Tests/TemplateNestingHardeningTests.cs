using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using DotLiquid;
using DotLiquid.Exceptions;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>Fix round S4 and S5: the parse thread's stack is sized from MaxNestingDepth, and a DotLiquid without the internal tokenizer degrades instead of failing.</summary>
public sealed class TemplateNestingHardeningTests
{
    private static string Nested(int levels, string open = "{% if a %}", string close = "{% endif %}") =>
        string.Concat(Enumerable.Repeat(open, levels)) + "x" + string.Concat(Enumerable.Repeat(close, levels));

    private sealed class WrongShape
    {
        public static List<string> Tokenize(string source) => new() { source };
    }

    [Fact]
    public void ParseStack_ForTheDefaultDepth_IsAboutOneAndAThirdMegabyte_NotTwoHundredFiftySix()
    {
        var bytes = TemplateNesting.ParseStackBytes(new TemplatingOptions().MaxNestingDepth);

        Assert.Equal(512 * 1024 + 100 * 8 * 1024, bytes);
        Assert.True(bytes < 2 * 1024 * 1024);
    }

    [Fact]
    public void ParseStack_GrowsWithTheDepth_AndIsCapped()
    {
        Assert.True(TemplateNesting.ParseStackBytes(1000) > TemplateNesting.ParseStackBytes(100));
        Assert.Equal(256 * 1024 * 1024, TemplateNesting.ParseStackBytes(int.MaxValue));
        Assert.True(TemplateNesting.ParseStackBytes(1) > 0);
    }

    [Theory]
    [InlineData("{% if a %}", "{% endif %}")]
    [InlineData("{% for i in a %}", "{% endfor %}")]
    [InlineData("{% capture x %}", "{% endcapture %}")]
    [InlineData("{% unless a %}", "{% endunless %}")]
    public void TheDefaultDepth_ParsesOnBothPaths_WithTheReducedStack(string open, string close)
    {
        var template = Nested(100, open, close);

        Assert.NotNull(TemplateNesting.Parse(template, 100, sharedTokens: true));
        Assert.NotNull(TemplateNesting.Parse(template, 100, sharedTokens: false));
        Assert.Throws<SyntaxException>(() => TemplateNesting.Parse(Nested(101, open, close), 100));
    }

    [Fact]
    public void StackFor_AnUnverifiedDotLiquidVersion_GetsTheMaximumStack()
    {
        var real = TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"));

        Assert.Equal(TemplateNesting.ParseStackBytes(100), TemplateNesting.StackFor(100, real, new Version(2, 3, 260)));
        Assert.Equal(256 * 1024 * 1024, TemplateNesting.StackFor(100, real, new Version(2, 4, 0)));
        Assert.Equal(256 * 1024 * 1024, TemplateNesting.StackFor(100, real, new Version(2, 3, 261)));
        Assert.Equal(256 * 1024 * 1024, TemplateNesting.StackFor(100, null, new Version(2, 3, 260)));
    }

    [Fact]
    public void CreateTokenizer_BindsDotLiquidsTokenizer()
    {
        var type = typeof(Template).Assembly.GetType("DotLiquid.Tokenizer");

        Assert.NotNull(TemplateNesting.CreateTokenizer(type));
    }

    [Fact]
    public void CreateTokenizer_MissingTypeMethodOrSignature_IsNull_NotAnException()
    {
        Assert.Null(TemplateNesting.CreateTokenizer(null));
        Assert.Null(TemplateNesting.CreateTokenizer(typeof(object)));
        Assert.Null(TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"), "NoSuchMethod"));
        Assert.Null(TemplateNesting.CreateTokenizer(typeof(WrongShape)));
    }

    [Fact]
    public void WithoutTheTokenizer_TheScanStillCountsAndRefusesDeepTemplates()
    {
        var tokens = TemplateNesting.Tokens(Nested(101), null);

        Assert.Equal(101, TemplateNesting.Depth(tokens, int.MaxValue));
        Assert.Equal(3, TemplateNesting.Depth(TemplateNesting.Tokens("{% if a %}{{ 'x' }}{% for i in a %}{% unless a %}{% endunless %}{% endfor %}{% endif %}", null), int.MaxValue));
        Assert.Equal(1, TemplateNesting.Depth(TemplateNesting.Tokens("{% raw %}{% if a %}{% if a %}{% endraw %}", null), int.MaxValue));
    }

    [Fact]
    public void FallbackTokens_AreLinear_AndIgnoreUnterminatedTags()
    {
        Assert.Empty(TemplateNesting.FallbackTokens("{% if a"));
        Assert.Empty(TemplateNesting.FallbackTokens("plain { text } {"));
        Assert.Equal(new[] { "{% if a %}", "{{ b }}" }, TemplateNesting.FallbackTokens("x{% if a %}y{{ b }}z"));
        Assert.Equal(200_000, TemplateNesting.FallbackTokens(string.Concat(Enumerable.Repeat("{%a%}", 200_000))).Count);
    }

    [Fact]
    public void FallbackScan_GetsTheMaximumStack_TheExactTokenizerGetsTheFormula()
    {
        var real = TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"));

        Assert.Equal(256 * 1024 * 1024, TemplateNesting.StackFor(100, null));
        Assert.Equal(TemplateNesting.ParseStackBytes(100), TemplateNesting.StackFor(100, real));
        Assert.True(TemplateNesting.StackFor(100, real) < TemplateNesting.StackFor(100, null));
    }

    [Fact]
    public void FallbackDepth_OfWhitespaceControlNesting_EqualsTheRealTokenizers()
    {
        var template = Nested(500, "{%- if x -%}", "{%- endif -%}");
        var real = TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"));

        Assert.Equal(500, TemplateNesting.Depth(TemplateNesting.Tokens(template, real), int.MaxValue));
        Assert.Equal(500, TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue));
    }

    [Fact]
    public void WithoutTheTokenizer_WhitespaceControlTags_AreCounted()
    {
        var template = Nested(5_000, "{%- if x -%}", "{%- endif -%}");

        Assert.Equal(5_000, TemplateNesting.Depth(TemplateNesting.Tokens(template, null), int.MaxValue));
        Assert.Equal(5_000, TemplateNesting.Depth(TemplateNesting.Tokens(template, TemplateNesting.CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer"))), int.MaxValue));
        Assert.Equal(2, TemplateNesting.Depth(TemplateNesting.Tokens("{%-if a-%}{%- for i in a -%}{%- endfor -%}{%- endif -%}", null), int.MaxValue));
    }
}
