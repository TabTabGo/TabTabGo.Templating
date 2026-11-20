using TabTabGo.Templating.Liquid;
using System.Text.RegularExpressions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>S-10: a template cannot run for minutes or build an unbounded string.</summary>
public sealed class RenderLimitTests
{
    private const string NestedLoops =
        "{% for a in (1..100000) %}{% for b in (1..100000) %}{% for c in (1..100000) %}x{% endfor %}{% endfor %}{% endfor %}";

    // The render runs on its own task so that a missing limit fails the test instead of hanging the run.
    private static async Task AssertStopsWithAnError(Liquid.TemplatingEngine engine)
    {
        var render = Task.Run(() => engine.RenderString(NestedLoops, new { }));

        var finished = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.True(ReferenceEquals(finished, render), "the render did not stop within 10 seconds");
        Assert.True(render.IsFaulted, "the render finished without an error");
    }

    [Fact]
    public async Task RenderTimeout_InfiniteLoopTemplate_Stops()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = TimeSpan.FromMilliseconds(300), MaxIterations = 0, MaxOutputCharacters = 0 });

        await AssertStopsWithAnError(engine);
    }

    [Fact]
    public async Task MaxIterations_NestedLoops_Stops()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxIterations = 1000, RenderTimeout = Timeout.InfiniteTimeSpan, MaxOutputCharacters = 0 });

        await AssertStopsWithAnError(engine);
    }

    [Fact]
    public void MaxIterations_WithinTheLimit_Renders()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxIterations = 1000 });

        Assert.Equal("xxx", engine.RenderString("{% for a in (1..3) %}x{% endfor %}", new { }));
    }

    [Fact]
    public void MaxOutput_Zero_Unlimited()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxOutputCharacters = 0 });

        var result = engine.RenderString("{% for a in (1..1000) %}0123456789{% endfor %}", new { });

        Assert.Equal(10_000, result.Length);
    }

    [Fact]
    public void Defaults_DoNotTruncateOrStopANormalReport()
    {
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", "{% for r in rows %}{{ r.n }}:{{ r.t }};{% endfor %}");
        var rows = Enumerable.Range(0, 20_000).Select(i => new { n = i, t = "row" }).ToList();

        var result = (string)new Liquid.TemplatingEngine().Render(path, new { rows });

        Assert.EndsWith("19999:row;", result);
    }
}

/// <summary>QA F2: the regex time-out bounds user-controlled regular expressions, never DotLiquid's own template-parse expressions.</summary>
public sealed class RegexTimeoutScopeTests
{
    [Fact]
    public void Parse_OfALargeTemplate_IsNotBoundByATinyRegexTimeout()
    {
        var template = string.Concat(Enumerable.Repeat("{% if a %}{{ b | Append: 'x' }}{% else %}{% assign c = 'q' %}{% endif %}\n", 4000));
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = TimeSpan.FromTicks(1), MaxOutputCharacters = 0 });

        var result = engine.RenderString(template, new { a = true, b = "y" });

        Assert.StartsWith("yx", result);
    }

    [Fact]
    public void ParseTimeout_IsAppliedToDotLiquidsProcessWideRegexTimeout_NeverRaised()
    {
        // DotLiquid builds some of its parse expressions once, at first use, with the value current then; what an engine controls is the process-wide
        // setting, so that is what is asserted (a catastrophic parse pattern cannot be forced deterministically in a warm process).
        var original = global::DotLiquid.Template.RegexTimeOut;
        try
        {
            global::DotLiquid.Template.RegexTimeOut = TimeSpan.FromSeconds(10);
            new Liquid.TemplatingEngine(new TemplatingOptions { ParseTimeout = TimeSpan.FromSeconds(2) }).RenderString("{{ 'a' }}", new { });
            Assert.Equal(TimeSpan.FromSeconds(2), global::DotLiquid.Template.RegexTimeOut);

            new Liquid.TemplatingEngine(new TemplatingOptions { ParseTimeout = TimeSpan.FromSeconds(8) }).RenderString("{{ 'a' }}", new { });
            Assert.Equal(TimeSpan.FromSeconds(2), global::DotLiquid.Template.RegexTimeOut); // the strictest engine wins
        }
        finally
        {
            global::DotLiquid.Template.RegexTimeOut = original;
        }
    }

    [Fact]
    public void DefaultParseTimeout_IsAFewSeconds_AndLowersDotLiquidsTenSecondDefault()
    {
        var original = global::DotLiquid.Template.RegexTimeOut;
        try
        {
            global::DotLiquid.Template.RegexTimeOut = TimeSpan.FromSeconds(10);
            var engine = new Liquid.TemplatingEngine(new TemplatingOptions());

            engine.RenderString("{{ 'a' }}", new { });

            Assert.Equal(TimeSpan.FromSeconds(5), new TemplatingOptions().ParseTimeout);
            Assert.True(global::DotLiquid.Template.RegexTimeOut <= TimeSpan.FromSeconds(5));
        }
        finally
        {
            global::DotLiquid.Template.RegexTimeOut = original;
        }
    }

    [Fact]
    public void RenderString_TemplateLongerThanMaxTemplateCharacters_IsRejectedBeforeParsing()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxTemplateCharacters = 100 });

        Assert.Equal(new string('x', 100), engine.RenderString(new string('x', 100), new { }));
        var error = Assert.Throws<RenderLimitExceededException>(() => engine.RenderString(new string('x', 101), new { }));
        Assert.Contains("MaxTemplateCharacters", error.Message);
    }

    [Fact]
    public void ParseTimeout_AndMaxTemplateCharacters_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { ParseTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { ParseTimeout = Timeout.InfiniteTimeSpan }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxTemplateCharacters = -1 }));
    }

    [Fact]
    public void FilterRegex_StillUsesTheEngineRegexTimeout()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = TimeSpan.FromMilliseconds(50) });
        var input = new string('a', 60) + "!";

        Assert.Throws<RegexMatchTimeoutException>(() => engine.RenderString("{{ input | Replace: '(a+)+$', 'x' }}", new { input }));
    }
}
