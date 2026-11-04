using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>H1 (JSONPath =~ regex) and M1 (built-in Replace/ReplaceFirst regex) for 8.0.4: regexes in templates time out.</summary>
[Collection("liquid-static")]
public class RegexInJsonPathAndReplaceTests
{
    private static readonly JObject Data = JObject.Parse("{\"d\":{\"arr\":[{\"n\":\"" + new string('a', 30) + "!\"}]}}");
    private const string EvilPath = "$.d.arr[?(@.n =~ /^(a+)+$/)]";

    /// <summary>Runs <paramref name="work"/> on another thread so a regression fails the test instead of hanging the run.</summary>
    private static void AssertFinishesWithin(TimeSpan limit, Action work)
    {
        var task = Task.Run(work);
        Assert.True(task.Wait(limit), $"did not finish within {limit}: regex without time-out");
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void JSelectToken_CatastrophicRegex_TimesOutAndReturnsNull()
    {
        AssertFinishesWithin(TimeSpan.FromSeconds(3), () => Assert.Null(JObjectFilters.JSelectToken(Data, EvilPath)));
    }

    [Fact]
    public void JSelectTokenByProperty_CatastrophicRegex_TimesOutAndReturnsNull()
    {
        AssertFinishesWithin(TimeSpan.FromSeconds(3), () => Assert.Null(JObjectFilters.JSelectTokenByProperty(Data, "d.arr[?(@.n =~ /^(a+)+$/)]")));
    }

    [Fact]
    public void JSelectTokenValues_CatastrophicRegex_TimesOutAndReturnsEmpty()
    {
        AssertFinishesWithin(TimeSpan.FromSeconds(3), () => Assert.Empty(JObjectFilters.JSelectTokenValues(Data, EvilPath)));
    }

    [Fact]
    public void JSelectTokenValuesByProperty_CatastrophicRegex_TimesOutAndReturnsEmpty()
    {
        AssertFinishesWithin(TimeSpan.FromSeconds(3), () => Assert.Empty(JObjectFilters.JSelectTokenValuesByProperty(Data, "$.d.arr[?(@.n =~ /^(a+)+$/)]", "")));
    }

    [Fact]
    public void JSelectTokenValue_CatastrophicRegex_TimesOutAndReturnsNull()
    {
        AssertFinishesWithin(TimeSpan.FromSeconds(3), () => Assert.Null(JObjectFilters.JSelectTokenValue(Data, EvilPath)));
    }

    [Fact]
    public void JSelectTokenValues_ThroughATemplate_FinishesWithinTheTimeout()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();

        AssertFinishesWithin(TimeSpan.FromSeconds(3), () =>
        {
            var result = engine.RenderString("{{ root | JSelectTokenValues: \"" + EvilPath + "\" | Size }}", new { root = Data }, "en-US");
            Assert.Equal("0", result);
        });
    }

    [Fact]
    public void JSelectTokenValues_NormalRegex_StillMatches()
    {
        var data = JObject.Parse("""{"arr":[{"n":"abc"},{"n":"xyz"}]}""");

        var values = JObjectFilters.JSelectTokenValues(data, "$.arr[?(@.n =~ /^a/)].n").Select(v => v.ToString());

        Assert.Equal(new[] { "abc" }, values);
    }

    [Theory]
    [InlineData("Replace")]
    [InlineData("ReplaceFirst")]
    public void BuiltInReplace_CatastrophicPattern_StopsAtTheConfiguredTimeoutWithoutEscaping(string filter)
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = TimeSpan.FromMilliseconds(200) });
        engine.Initialize();
        var input = new string('a', 30) + "!";
        var watch = Stopwatch.StartNew();
        string? result = null;

        var task = Task.Run(() => result = engine.RenderString("[{{ s | " + filter + ": '^(a+)+$', 'x' }}]", new { s = input }, "en-US"));

        Assert.True(task.Wait(TimeSpan.FromSeconds(4)), $"{filter} ran for {watch.Elapsed}: DotLiquid's 10 s default time-out is still in force");
        Assert.NotNull(result);
        Assert.Contains("Liquid error", result);
    }

    [Fact]
    public void BuiltInReplace_NormalUse_StillWorks()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();

        Assert.Equal("a-b-c", engine.RenderString("{{ 'a b c' | Replace: ' ', '-' }}", new { }, "en-US"));
        Assert.Equal("x b a", engine.RenderString("{{ 'a b a' | ReplaceFirst: 'a', 'x' }}", new { }, "en-US"));
    }
}
