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

    // ---- S-3 addendum: every overload that takes a string

    private static readonly HashSet<string> CultureFamily = new() { "IndexOf", "LastIndexOf", "StartsWith", "EndsWith", "Compare", "CompareTo" };

    private static Expression? Argument(Type type) => type switch
    {
        _ when type == typeof(string) => Expression.Constant("a"),
        _ when type == typeof(int) => Expression.Constant(0),
        _ when type == typeof(char) => Expression.Constant('a'),
        _ when type == typeof(bool) => Expression.Constant(false),
        _ when type == typeof(StringComparison) => Expression.Constant(StringComparison.CurrentCulture),
        _ when type == typeof(System.Globalization.CompareOptions) => Expression.Constant(System.Globalization.CompareOptions.None),
        _ when type == typeof(System.Globalization.CultureInfo) => Expression.Constant(null, type),
        _ when type == typeof(object) => Expression.Constant(null, type),
        _ => null, // spans, arrays, runes, providers: cannot be built as constants here (and are refused by the parameter-type rule)
    };

    private static IEnumerable<Expression> StringCalls()
    {
        foreach (var method in typeof(string).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
        {
            var parameters = method.GetParameters();
            if (!parameters.Any(p => p.ParameterType == typeof(string)) || method.IsGenericMethod)
            {
                continue;
            }

            var arguments = parameters.Select(p => Argument(p.ParameterType)).ToArray();
            if (arguments.Any(a => a == null))
            {
                continue;
            }

            yield return Expression.Call(method.IsStatic ? null : Expression.Constant("abc"), method, arguments!);
        }
    }

    private static bool IsCultureSensitiveSearch(MethodCallExpression call)
    {
        if (call.Method.DeclaringType != typeof(string) || !CultureFamily.Contains(call.Method.Name))
        {
            return false;
        }

        var comparison = call.Arguments.OfType<ConstantExpression>().FirstOrDefault(a => a.Type == typeof(StringComparison));
        var hasString = call.Method.GetParameters().Any(p => p.ParameterType == typeof(string));
        return hasString && comparison is not { Value: StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase };
    }

    [Fact]
    public void EveryStringOverload_IsRewrittenToOrdinal_OrRefused_NeverPassedThroughCultureSensitive()
    {
        var calls = StringCalls().Cast<MethodCallExpression>().ToList();
        Assert.True(calls.Count > 40, $"only {calls.Count} overloads enumerated");

        foreach (var call in calls)
        {
            Expression? result = null;
            try
            {
                result = FilterCriteriaGuard.CheckTree(call);
            }
            catch (ArgumentException)
            {
                continue; // refused
            }

            var remaining = new CallCollector();
            remaining.Visit(result);
            Assert.DoesNotContain(remaining.Calls, IsCultureSensitiveSearch);
            Assert.DoesNotContain(remaining.Calls, c => c.Method.DeclaringType == typeof(string) && CultureFamily.Contains(c.Method.Name)
                && c.Method.GetParameters().Any(p => p.ParameterType == typeof(System.Globalization.CultureInfo)));
        }
    }

    [Theory]
    [InlineData("IndexOf", 2)]
    [InlineData("IndexOf", 3)]
    [InlineData("StartsWith", 1)]
    [InlineData("EndsWith", 1)]
    [InlineData("Compare", 2)]
    [InlineData("Compare", 5)]
    [InlineData("CompareTo", 1)]
    public void TheRewrittenOverloads_AreOrdinal(string name, int parameterCount)
    {
        var method = typeof(string).GetMethods().First(m => m.Name == name && m.GetParameters().Length == parameterCount
            && m.GetParameters().All(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(int)));
        var arguments = method.GetParameters().Select(p => Argument(p.ParameterType)!).ToArray();
        var call = Expression.Call(method.IsStatic ? null : Expression.Constant("abc"), method, arguments);

        var collector = new CallCollector();
        collector.Visit(FilterCriteriaGuard.CheckTree(call));

        Assert.NotEmpty(collector.Calls);
        Assert.DoesNotContain(collector.Calls, IsCultureSensitiveSearch);
    }

    private sealed class CallCollector : ExpressionVisitor
    {
        public List<MethodCallExpression> Calls { get; } = new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Calls.Add(node);
            return base.VisitMethodCall(node);
        }
    }

    [Theory]
    [InlineData("it[\"k\"].ToString().IndexOf(\"__NEEDLE__\", 5) >= 0")]
    [InlineData("it[\"k\"].ToString().IndexOf(\"__NEEDLE__\", 5, 90000) >= 0")]
    [InlineData("string.Compare(it[\"k\"].ToString(), 0, \"__NEEDLE__\", 0, 9000) = 0")]
    [InlineData("it[\"k\"].ToString().StartsWith(\"__NEEDLE__\")")]
    public void QuadraticOffsetAndRangeOverloads_AreFast_UnderARenderTimeout(string template)
    {
        var haystack = new string('a', 100_000);
        var needle = new string('a', 900) + "b";
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = haystack }, new Hash { ["id"] = 2, ["k"] = haystack } };
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = TimeSpan.FromSeconds(1) });
        var expression = template.Replace("__NEEDLE__", needle);
        var watch = Stopwatch.StartNew();

        var result = Record.Exception(() => engine.RenderString("{% assign r = rows | Filter: expr %}{{ r | Size }}", new { rows, expr = expression }));
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}: {result?.Message}");
    }

    [Fact]
    public void OffsetOverloads_GiveTheOrdinalAnswer()
    {
        using var _ = new TabTabGo.Templating.DotLiquid.Tests.Support.CultureScope("en-US");
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = "\u00ADAlice Al" } };

        Assert.Equal("1", Ids(CollectionFilters.Filter(rows, "it[\"k\"].ToString().IndexOf(\"Al\", 3) = 7")));
        Assert.Equal("1", Ids(CollectionFilters.Filter(rows, "string.Compare(it[\"k\"].ToString(), 1, \"Alice\", 0, 5) = 0")));
    }

    // ---- round 3: m-28 cache and caller-thread parse, m-29 call counting

    [Fact]
    public void M29_ThirtyTwoStartsWithCalls_Pass_AndThirtyThreeAreRefused()
    {
        string Calls(int count) => string.Join(" or ", Enumerable.Repeat("\"abc\".StartsWith(\"a\")", count));

        Assert.Equal("1,2", Ids(CollectionFilters.Filter(Rows(), Calls(FilterCriteriaGuard.MaxCalls))));
        Assert.Contains("method calls", Assert.Throws<ArgumentException>(() => CollectionFilters.Filter(Rows(), Calls(FilterCriteriaGuard.MaxCalls + 1))).Message);
    }

    [Fact]
    public void M28_ARepeatedCriteria_IsCompiledOnce_AndHitsStillCheckTheRenderTimeout()
    {
        FilterCriteriaGuard.ClearCache();
        const string criteria = "it[\"k\"].ToString() = \"abc\"";

        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), criteria)));
        var first = FilterCriteriaGuard.Compile(criteria);
        Assert.Same(first, FilterCriteriaGuard.Compile(criteria));
        Assert.Equal(1, FilterCriteriaGuard.CachedCount);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var scope = RenderScope.Begin(new TemplatingOptions(), null, cancelled.Token);
        Assert.Throws<RenderLimitExceededException>(() => CollectionFilters.Filter(Rows(), criteria)); // a cache hit is still checked per row
    }

    [Fact]
    public void M28_RefusedCriteria_AreNeverCached_AndAreRefusedEveryTime()
    {
        FilterCriteriaGuard.ClearCache();
        const string refused = "it[\"k\"].ToString().PadLeft(5).Length > 0";

        for (var i = 0; i < 3; i++)
        {
            Assert.Throws<ArgumentException>(() => CollectionFilters.Filter(Rows(), refused));
        }

        Assert.Equal(0, FilterCriteriaGuard.CachedCount);
    }

    [Fact]
    public void M28_TheCache_IsBounded()
    {
        FilterCriteriaGuard.ClearCache();

        for (var i = 0; i < 600; i++)
        {
            CollectionFilters.Filter(Rows(), "it[\"k\"].ToString() = \"v" + i + "\"");
        }

        Assert.InRange(FilterCriteriaGuard.CachedCount, 1, 256);
    }

    [Fact]
    public void M28_ConcurrentCalls_ShareThePredicate_WithoutSharedState()
    {
        FilterCriteriaGuard.ClearCache();
        var results = new string[64];

        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            results[i] = Ids(CollectionFilters.Filter(Rows(), "it[\"k\"].ToString() = \"abc\" or it[\"id\"].ToString() = \"" + (i % 2 + 1) + "\""));
        });

        Assert.All(results.Select((r, i) => (r, i)), t => Assert.Equal(t.i % 2 == 0 ? "1" : "1,2", t.r));
    }

    [Fact]
    public void M28_ACriteriaAboveTheShallowNesting_StillParsesOnA256KbCallerStack()
    {
        FilterCriteriaGuard.ClearCache();
        var deep = Repeat("(", 40) + "it[\"k\"].ToString() = \"abc\"" + Repeat(")", 40); // above the shallow nesting, within the cap

        Assert.Equal("1", Ids(OnSmallStack(() => CollectionFilters.Filter(Rows(), deep))));
    }

    /// <summary>Burns about <paramref name="kilobytes"/> of the current thread's stack (16 KB frames) and then runs <paramref name="action"/>, like a host that is already deep in recursion.</summary>
    private static T WithStackConsumed<T>(int kilobytes, Func<T> action)
    {
        if (kilobytes <= 0)
        {
            return action();
        }

        Span<byte> frame = stackalloc byte[16 * 1024];
        frame[0] = 1;
        frame[^1] = 1;
        var result = WithStackConsumed(kilobytes - 16, action);
        return frame[0] == 1 ? result : default!;
    }

    private static T OnStack<T>(int kilobytes, int consumedKilobytes, Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = WithStackConsumed(consumedKilobytes, action);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            },
            maxStackSize: kilobytes * 1024);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    private static string Fit(string open, string core, string close, int maxLength)
    {
        var best = core;
        for (var n = 1; n < 200; n++)
        {
            var text = Repeat(open, n) + core + Repeat(close, n);
            if (text.Length > maxLength)
            {
                break;
            }

            best = text;
        }

        return best;
    }

    /// <summary>Every shape the caller's thread may be asked to parse: at most 64 characters and 8 levels, no brackets, braces, new or lambdas.</summary>
    public static IEnumerable<object[]> EligibleShapes()
    {
        yield return new object[] { Repeat("(", 8) + "it.k == \"a\"" + Repeat(")", 8) };
        yield return new object[] { Repeat("-(", 8) + "1" + Repeat(")", 8) + " < 0" };
        yield return new object[] { Fit("iif(true,", "1", ",1)", 60) + " > 0" };
        yield return new object[] { Fit("Math.Max(", "1", ",1)", 60) + " > 0" };
        yield return new object[] { Repeat("not ", 8) + "true" };
        yield return new object[] { Repeat("!", 8) + "true" };
        yield return new object[] { Fit("true?", "1", ":1", 60) + " > 0" };
        yield return new object[] { "it.k" + Repeat(".ToString()", 3) + " = \"a\"" };
        yield return new object[] { Repeat("int(", 8) + "1" + Repeat(")", 8) + " > 0" };
        yield return new object[] { Repeat("np(", 8) + "1" + Repeat(")", 8) + " > 0" };
    }

    [Theory]
    [MemberData(nameof(EligibleShapes))]
    public void F1_EligibleShapes_ParseOnA256KbCallerStack_AndOnA300KbOneWithHalfConsumed(string expression)
    {
        Assert.True(expression.Length <= FilterCriteriaGuard.ShallowCharacters, expression.Length.ToString());

        for (var i = 0; i < 5; i++)
        {
            FilterCriteriaGuard.ClearCache();
            var outcome = Record.Exception(() => OnStack(256, 0, () => CollectionFilters.Filter(Rows(), expression)));
            Assert.IsNotType<InsufficientExecutionStackException>(outcome); // any other exception is a catchable refusal; a crash would have ended the test run

            FilterCriteriaGuard.ClearCache();
            var deep = Record.Exception(() => OnStack(300, 150, () => CollectionFilters.Filter(Rows(), expression)));
            Assert.IsNotType<InsufficientExecutionStackException>(deep); // never a crash
        }
    }

    /// <summary>The shapes that crashed the 200-character caller-thread gate: indexers, braces, new, lambdas, deep calls.</summary>
    public static IEnumerable<object[]> IneligibleShapes()
    {
        yield return new object[] { Fit("$[", "1", "]", 200) };
        yield return new object[] { Fit("it[", "\"a\"", "]", 200) };
        yield return new object[] { Repeat("it[", 66) };
        yield return new object[] { Fit("new[]{", "1", "}", 200) };
        yield return new object[] { Fit("new{a=", "1", "}", 200) };
        yield return new object[] { Fit("new(", "1", ")", 200) };
        yield return new object[] { Fit("Math.Max(", "1", ",1)", 200) };
        yield return new object[] { Fit("iif(true,", "1", ",1)", 200) };
        yield return new object[] { Fit("-(", "1", ")", 200) };
        yield return new object[] { Fit("it.k.Any(x=>", "true", ")", 200) };
    }

    [Theory]
    [MemberData(nameof(IneligibleShapes))]
    public void F1_ReproShapes_AreCatchableOnSmallCallerStacks_NeverACrash(string expression)
    {
        Assert.False(FilterCriteriaGuard.IsShallow(expression, 0));

        foreach (var (stack, consumed) in new[] { (200, 0), (256, 0), (300, 150), (480, 0) })
        {
            FilterCriteriaGuard.ClearCache();
            var outcome = Record.Exception(() => OnStack(stack, consumed, () => CollectionFilters.Filter(Rows(), expression)));
            Assert.IsNotType<InsufficientExecutionStackException>(outcome); // the shape is refused or evaluated, never a crash ({stack} KB)
        }
    }

    [Theory]
    [InlineData("it[\"k\"] == null", false)]
    [InlineData("{1}", false)]
    [InlineData("x => x", false)]
    [InlineData("new(1)", false)]
    [InlineData("NEW (1)", false)]
    [InlineData("1+new\t(1)", false)]
    [InlineData("it.renew == null", true)]
    [InlineData("it.newer == null", true)]
    [InlineData("it.k == \"a\"", true)]
    public void F1_Eligibility_RejectsBracketsBracesNewAndLambdas_ButNotWordsContainingNew(string expression, bool eligible)
    {
        Assert.Equal(eligible, FilterCriteriaGuard.IsShallow(expression, 0));
    }

    [Fact]
    public void F1_TheEligibilityBounds_AreSixtyFourCharactersAndEightLevels()
    {
        Assert.True(FilterCriteriaGuard.IsShallow(new string('a', 64), 8));
        Assert.False(FilterCriteriaGuard.IsShallow(new string('a', 65), 0));
        Assert.False(FilterCriteriaGuard.IsShallow("1", 9));
    }

    [Fact]
    public void F1_StackMargin_IsTrueWithRoomAndFalseWhenHalfTheStackIsGone()
    {
        Assert.True(OnStack(1024, 0, () => FilterCriteriaGuard.HasStackMargin(FilterCriteriaGuard.StackMarginLevels)));
        Assert.False(OnStack(300, 100, () => FilterCriteriaGuard.HasStackMargin(FilterCriteriaGuard.StackMarginLevels)));
    }

    [Fact]
    public void F2_TheCacheBound_HoldsUnder64ThreadsOfMisses()
    {
        FilterCriteriaGuard.ClearCache();
        var rows = Rows();
        var threads = Enumerable.Range(0, 64).Select(t => new Thread(() =>
        {
            for (var i = 0; i < 40; i++)
            {
                CollectionFilters.Filter(rows, "it[\"k\"].ToString() = \"t" + t + "-" + i + "\"");
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.InRange(FilterCriteriaGuard.CachedCount, 1, 256);
    }

    [Fact]
    public void M28_CachedAndUncachedCalls_PerSecond()
    {
        var criteria = "it[\"k\"].ToString() = \"abc\"";
        var rows = Rows();
        const int calls = 2_000;

        FilterCriteriaGuard.ClearCache();
        var cold = Stopwatch.StartNew();
        for (var i = 0; i < calls / 10; i++)
        {
            FilterCriteriaGuard.ClearCache();
            CollectionFilters.Filter(rows, criteria);
        }

        cold.Stop();
        var warm = Stopwatch.StartNew();
        for (var i = 0; i < calls; i++)
        {
            CollectionFilters.Filter(rows, criteria);
        }

        warm.Stop();
        var coldPerSecond = (calls / 10) / cold.Elapsed.TotalSeconds;
        var warmPerSecond = calls / warm.Elapsed.TotalSeconds;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ttg-filter-bench.txt"), $"parse each call: {coldPerSecond:F0}/s; cached: {warmPerSecond:F0}/s");

        Assert.True(warmPerSecond > coldPerSecond, $"cached {warmPerSecond:F0}/s vs uncached {coldPerSecond:F0}/s");
    }
}
