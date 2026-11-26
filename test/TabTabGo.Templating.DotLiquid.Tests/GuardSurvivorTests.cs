using DotLiquid;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid;
using TabTabGo.Templating.TestKit;
using Guarded = TabTabGo.Templating.Liquid.Filters.GuardedStringFilters;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>
/// Architect round 5, M-2 and m-7 / m-9: one test per guard whose removal no earlier test noticed. Each one fails when its guard is
/// removed (the mutation run of the round is in the spec, section 12.5).
/// </summary>
public sealed class GuardSurvivorTests
{
    private static Liquid.TemplatingEngine Engine(long limit) => new(new TemplatingOptions { MaxOutputCharacters = limit });

    private static Context Literal() =>
        new(System.Globalization.CultureInfo.InvariantCulture) { SyntaxCompatibilityLevel = SyntaxCompatibility.DotLiquid22 };

    /// <summary>Runs <paramref name="action"/> and returns the bytes this thread allocated; the budget must refuse before the allocation, not after.</summary>
    private static long Allocated(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ---- literal-mode Replace / ReplaceFirst: size is checked before the string is built, and the loop observes the time-out

    [Fact]
    public void Replace_LiteralMode_IsRejectedBeforeItIsAllocated()
    {
        using var scope = RenderScope.Begin(new TemplatingOptions { MaxOutputCharacters = 500_000 }, null);
        var input = new string('a', 100_000);

        var allocated = Allocated(() => Assert.Throws<RenderLimitExceededException>(() => Guarded.Replace(Literal(), input, "a", "bbbbbbbbbb")));

        Assert.True(allocated < 1_000_000, $"allocated {allocated} bytes before refusing"); // one 1 000 000-character result is 2 MB
    }

    [Fact]
    public void ReplaceFirst_LiteralMode_IsRejectedBeforeItIsAllocated()
    {
        using var scope = RenderScope.Begin(new TemplatingOptions { MaxOutputCharacters = 500_000 }, null);
        var input = new string('a', 400_000);
        var replacement = new string('b', 400_000);

        var allocated = Allocated(() => Assert.Throws<RenderLimitExceededException>(() => Guarded.ReplaceFirst(Literal(), input, "a", replacement)));

        Assert.True(allocated < 500_000, $"allocated {allocated} bytes before refusing");
    }

    [Fact]
    public void Replace_LiteralMode_ObservesTheRenderTimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var scope = RenderScope.Begin(new TemplatingOptions { MaxOutputCharacters = 0 }, null, cts.Token);

        Assert.Throws<RenderLimitExceededException>(() => Guarded.Replace(Literal(), new string('a', 100_000), "a", "b"));
    }

    // ---- ExpansionBound: every $ token that can grow the result is counted before Match.Result allocates it

    [Theory]
    [InlineData("a+", "$&$&$&$&$&$&$&$&$&$&")]
    [InlineData("(a+)", "$1$1$1$1$1$1$1$1$1$1")]
    [InlineData("(?<n>a+)", "${n}${n}${n}${n}${n}${n}${n}${n}${n}${n}")]
    [InlineData("a+", "$+$+$+$+$+$+$+$+$+$+")]
    [InlineData("a+", "$0$0$0$0$0$0$0$0$0$0")]
    public void Replace_TokenAmplification_IsRejectedBeforeItIsAllocated(string find, string replacement)
    {
        var input = new string('a', 100_000); // one match of 100 000 characters, ten times over is 1 000 000 > the limit
        var engine = Engine(500_000);

        var allocated = Allocated(() => Assert.Throws<RenderLimitExceededException>(() =>
            engine.RenderString("{% assign s = input | Replace: find, replacement %}x", new { input, find, replacement })));

        Assert.True(allocated < 1_000_000, $"allocated {allocated} bytes before refusing");
    }

    // ---- the engine hands its render time-out to the scope, so loops inside filters stop

    [Fact]
    public async Task RenderTimeout_StopsALoopInsideAFilter()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = TimeSpan.FromMilliseconds(200), MaxOutputCharacters = 0 });

        var render = Task.Run(() => engine.RenderString("{{ '' | Times: 2000000000 | Join: '' }}", new { }));
        var finished = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.True(ReferenceEquals(finished, render), "the filter loop did not stop within 10 seconds");
        Assert.IsType<RenderLimitExceededException>(render.Exception!.GetBaseException());
    }

    // ---- MaxDepth on every entry: JObject roots, Render(path), arrays

    private static JObject Deep(int levels)
    {
        var node = new JObject { ["v"] = "deep" };
        for (var i = 0; i < levels; i++)
        {
            node = new JObject { ["a"] = node };
        }

        return node;
    }

    [Fact]
    public void MaxDepth_AppliesToAJObjectRoot()
    {
        var data = Deep(5);

        // root = depth 0; each "a" is one level deeper, so the object holding "v" is at depth 5 and is cut with MaxDepth 3
        Assert.Equal("", new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 3 }).RenderString("{{ a.a.a.a.a.v }}", data));
        Assert.Equal("deep", new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 20 }).RenderString("{{ a.a.a.a.a.v }}", data));
    }

    [Fact]
    public void MaxDepth_AppliesToNestedArraysInAJObject()
    {
        var data = JObject.Parse("""{ "l": [ [ [ [ [ "x" ] ] ] ] ] }""");

        Assert.Equal("", new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 3 }).RenderString("{{ l[0][0][0][0][0] }}", data));
        Assert.Equal("x", new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 20 }).RenderString("{{ l[0][0][0][0][0] }}", data));
    }

    [Fact]
    public void MaxDepth_AppliesThroughRenderOfAFile()
    {
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", "{{ a.a.a.a.a.v }}");
        var data = Deep(5);

        Assert.Equal("", (string)new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 3 }).Render(path, data));
        Assert.Equal("deep", (string)new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 20 }).Render(path, data));
    }

    // ---- S-3: an include name that is a variable holding a non-string is not coerced to text

    [Fact]
    public void Include_NameFromANonStringVariable_IsRejected()
    {
        using var dir = new TempDir();
        dir.Write("_5.liquid", "SECRET-PARTIAL");
        dir.Write("_part.liquid", "partial-text");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { TemplatesPath = dir.Path });

        var number = engine.RenderString("{% include tpl %}", new { tpl = 5 });
        var text = engine.RenderString("{% include tpl %}", new { tpl = "part" });

        Assert.DoesNotContain("SECRET-PARTIAL", number);
        Assert.Contains("Liquid error", number);
        Assert.Equal("partial-text", text);
    }

    // ---- m-9: the Filter expression language stays closed beyond GetType()

    [Theory]
    [InlineData("new System.Text.StringBuilder().Length = 0")]
    [InlineData("new System.Uri(\"http://x\").Host != null")]
    [InlineData("new Uri(\"http://x\").Host != null")]
    [InlineData("Environment.MachineName != null")]
    [InlineData("System.Environment.MachineName != null")]
    [InlineData("System.IO.File.Exists(\"/etc/passwd\")")]
    public void Filter_NewOfAnArbitraryType_AndTypesBySimpleName_AreRejected(string expression)
    {
        var rows = new List<object> { new Hash { ["id"] = 1 } };

        Assert.ThrowsAny<Exception>(() => Liquid.Filters.CollectionFilters.Filter(rows, expression));
    }

    [Fact]
    public void Filter_PredefinedTypes_StillWork()
    {
        var rows = new List<object> { new Hash { ["id"] = 1 }, new Hash { ["id"] = 2 } };

        var result = (System.Collections.IEnumerable)Liquid.Filters.CollectionFilters.Filter(rows, "Math.Abs(1) = 1 && it[\"id\"].ToString() = \"2\"")!;

        Assert.Single(result.Cast<object>());
    }
}
