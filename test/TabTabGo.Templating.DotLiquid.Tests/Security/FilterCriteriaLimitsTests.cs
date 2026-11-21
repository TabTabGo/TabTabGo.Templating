using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using DotLiquid;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid;
using TabTabGo.Templating.Liquid.Filters;
using TabTabGo.Templating.Liquid.Internal;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>Round 2 (S-1, S-2, S-3): the Filter guard survives deep input, is bounded in size and calls, honours the render time-out per row, and compares ordinally.</summary>
[Collection("liquid-static")]
public class FilterCriteriaLimitsTests
{
    private static List<object> Rows() => new()
    {
        new Hash { ["id"] = 1, ["k"] = "abc" },
        new Hash { ["id"] = 2, ["k"] = "xyz" },
    };

    private static string Ids(object? result) => string.Join(",", ((System.Collections.IEnumerable)result!).Cast<Hash>().Select(h => h["id"]));

    /// <summary>Runs on a caller thread with a small stack, the way a host's request thread may look.</summary>
    private static T OnSmallStack<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            },
            maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    private static string Repeat(string part, int times) => string.Concat(Enumerable.Repeat(part, times));

    // ---- S-1: the walk itself

    [Fact]
    public void Walk_OfAnAbsurdlyDeepTree_IsACatchableException_NotACrash()
    {
        Expression tree = Expression.Constant(1);
        for (var i = 0; i < 300_000; i++)
        {
            tree = Expression.Add(Expression.Constant(1), tree);
        }

        Assert.Throws<InsufficientExecutionStackException>(() => OnSmallStack(() => FilterCriteriaGuard.CheckTree(tree)));
    }

    // ---- S-2: the parser's own recursion

    public static IEnumerable<object[]> DeepInputs()
    {
        yield return new object[] { "1" + Repeat(" + 1", 7_000) + " > 0" };
        yield return new object[] { "it.k" + Repeat(".ToString().Trim()", 10_000) + " = \"a\"" };
        yield return new object[] { Repeat("(", 500) + "it.k = \"abc\"" + Repeat(")", 500) };
        yield return new object[] { Repeat("(", 2_000) + "it.k = \"abc\"" + Repeat(")", 2_000) };
        yield return new object[] { Repeat("-", 10_000) + "1 < 0" };
        yield return new object[] { Repeat("- ", 400) + "1 < 0" };
        yield return new object[] { Repeat("not ", 20_000) + "true" };
        yield return new object[] { Repeat("not ", 300) + "true" };
        yield return new object[] { Repeat("!", 300) + "true" };
        yield return new object[] { "it[\"k\"] = " + Repeat("(", 70) + "\"a\"" + Repeat(")", 70) };
    }

    [Theory]
    [MemberData(nameof(DeepInputs))]
    public void DeepInput_IsRefusedWithAnException_OnASmallStackCaller(string expression)
    {
        Assert.Throws<ArgumentException>(() => OnSmallStack(() => CollectionFilters.Filter(Rows(), expression)));
    }

    [Fact]
    public void ModerateNesting_StillWorks_OnASmallStackCaller()
    {
        var parenthesised = Repeat("(", 50) + "it[\"k\"].ToString() = \"abc\"" + Repeat(")", 50);
        var terms = "1" + Repeat(" + 1", 200) + " > 0 and it[\"k\"].ToString() = \"xyz\"";
        var nots = Repeat("not ", 40) + "(it[\"k\"].ToString() = \"abc\")";

        Assert.Equal("1", Ids(OnSmallStack(() => CollectionFilters.Filter(Rows(), parenthesised))));
        Assert.Equal("2", Ids(OnSmallStack(() => CollectionFilters.Filter(Rows(), terms))));
        Assert.Equal("1", Ids(OnSmallStack(() => CollectionFilters.Filter(Rows(), nots))));
    }

    [Fact]
    public void Parentheses_InsideStringLiterals_DoNotCountAsNesting()
    {
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = Repeat("(", 200) } };

        Assert.Equal("1", Ids(CollectionFilters.Filter(rows, "it[\"k\"].ToString() = \"" + Repeat("(", 200) + "\"")));
    }

    // ---- S-3: CPU and allocation

    [Fact]
    public void Criteria_LongerThanTheCap_IsRefused()
    {
        var atCap = "it[\"k\"].ToString() = \"" + new string('a', FilterCriteriaGuard.MaxCriteriaCharacters - 23) + "\"";
        Assert.Equal(FilterCriteriaGuard.MaxCriteriaCharacters, atCap.Length);

        Assert.Equal("", Ids(CollectionFilters.Filter(Rows(), atCap)));
        Assert.Throws<ArgumentException>(() => CollectionFilters.Filter(Rows(), atCap + " "));
    }

    [Fact]
    public void MoreThanThirtyTwoCalls_AreRefused_AndThirtyTwoWork()
    {
        var ok = "it[\"k\"].ToString()" + Repeat(".ToLower()", FilterCriteriaGuard.MaxCalls) + " = \"abc\"";
        var tooMany = "it[\"k\"].ToString()" + Repeat(".ToLower()", FilterCriteriaGuard.MaxCalls + 1) + " = \"abc\"";

        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), ok)));
        Assert.Contains("method calls", Assert.Throws<ArgumentException>(() => CollectionFilters.Filter(Rows(), tooMany)).Message);
    }

    [Fact]
    public void IndexOf_IsOrdinal_AndFast_OnAQuadraticInput()
    {
        var haystack = new string('a', 100_000);
        var needle = new string('a', 900) + "b";
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = haystack }, new Hash { ["id"] = 2, ["k"] = haystack } };

        var watch = Stopwatch.StartNew();
        var result = CollectionFilters.Filter(rows, "it[\"k\"].ToString().IndexOf(\"" + needle + "\") >= 0");
        watch.Stop();

        Assert.Equal("", Ids(result));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Theory]
    [InlineData("it[\"k\"].ToString().IndexOf(\"Al\") = 1", "1")] // soft hyphen at 0, "Al" at 1: found by ordinal comparison only when really there
    [InlineData("it[\"k\"].ToString().IndexOf(\"­\") = 0", "1")]
    [InlineData("it[\"k\"].ToString().StartsWith(\"Al\")", "2")]
    [InlineData("it[\"k\"].ToString().EndsWith(\"ce\")", "1,2")]
    [InlineData("it[\"k\"].ToString().CompareTo(\"Alice\") = 0", "2")]
    [InlineData("it[\"k\"].ToString().Contains(\"lic\")", "1,2")]
    public void SearchAndCompare_AreOrdinal(string expression, string expected)
    {
        using var _ = new TabTabGo.Templating.DotLiquid.Tests.Support.CultureScope("en-US");
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = "­Alice" }, new Hash { ["id"] = 2, ["k"] = "Alice" } };

        Assert.Equal(expected, Ids(CollectionFilters.Filter(rows, expression)));
    }

    [Fact]
    public void ExplicitComparison_OnlyOrdinal_IsAccepted()
    {
        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), "it[\"k\"].ToString().StartsWith(\"AB\", StringComparison.OrdinalIgnoreCase)")));
        Assert.ThrowsAny<Exception>(() => CollectionFilters.Filter(Rows(), "it[\"k\"].ToString().StartsWith(\"AB\", StringComparison.CurrentCultureIgnoreCase)"));
        Assert.ThrowsAny<Exception>(() => CollectionFilters.Filter(Rows(), "it[\"k\"].ToString().IndexOf(\"a\", StringComparison.InvariantCulture) >= 0"));
    }

    [Fact]
    public void Filter_ChecksTheRenderTimeoutPerRow()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var scope = RenderScope.Begin(new TemplatingOptions(), null, cancelled.Token);

        Assert.Throws<RenderLimitExceededException>(() => CollectionFilters.Filter(Rows(), "true"));
    }

    [Fact]
    public void Render_FilterInsideARenderWithATimeout_StopsAtTheTimeout()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = TimeSpan.FromMilliseconds(50) });
        var items = Enumerable.Range(0, 200_000).Select(i => new { id = i, k = "abc" }).ToArray();
        var watch = Stopwatch.StartNew();

        var ex = Record.Exception(() => engine.RenderString("{% assign r = items | Filter: 'it[\"k\"].ToString().Contains(\"b\") and it[\"id\"].ToString().Length > 0' %}{{ r | Size }}", new { items }));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        _ = ex; // a fast machine may finish within the time-out; the bound on the time is what is asserted
    }
}
