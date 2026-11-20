using TabTabGo.Templating.Liquid;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>
/// S-10 (resource cap): the output limit also bounds intermediate strings, and exceeding it fails the render with a clear exception
/// instead of truncating. One test per growth path so that removing any guard fails a test.
/// </summary>
public sealed class RenderGrowthTests
{
    private static Liquid.TemplatingEngine Engine(long limit) => new(new TemplatingOptions { MaxOutputCharacters = limit });

    private static void AssertExceeds(long limit, string template, object? data = null)
    {
        var error = Assert.Throws<RenderLimitExceededException>(() => Engine(limit).RenderString(template, data ?? new { }));
        Assert.Contains("MaxOutputCharacters", error.Message);
    }

    [Fact]
    public void Output_OverTheLimit_Throws_NotTruncated() => AssertExceeds(100, "{% for a in (1..1000) %}0123456789{% endfor %}");

    [Fact]
    public void Output_ExactlyTheLimit_IsAllowed()
    {
        Assert.Equal(100, Engine(100).RenderString("{% for a in (1..10) %}0123456789{% endfor %}", new { }).Length);
        AssertExceeds(99, "{% for a in (1..10) %}0123456789{% endfor %}");
    }

    [Fact]
    public void Output_AcrossWriteOverloads_IsCounted()
    {
        // text and variables reach the writer through different overloads
        AssertExceeds(10, "{{ big }}", new { big = new string('x', 11) });
        AssertExceeds(10, "abcdefghijk");
    }

    [Fact]
    public void Capture_Block_IsBounded() =>
        AssertExceeds(100, "{% capture c %}{% for i in (1..1000) %}0123456789{% endfor %}{% endcapture %}done");

    [Fact]
    public void Capture_WithinTheLimit_StillWorks()
    {
        Assert.Equal("hello", Engine(100).RenderString("{% capture c %}hel{{ 'lo' }}{% endcapture %}{{ c }}", new { }));
    }

    [Fact]
    public void Append_Doubling_IsBounded() =>
        AssertExceeds(1000, "{% assign s = 'x' %}{% for i in (1..30) %}{% assign s = s | Append: s %}{% endfor %}");

    [Fact]
    public void Prepend_Doubling_IsBounded() =>
        AssertExceeds(1000, "{% assign s = 'x' %}{% for i in (1..30) %}{% assign s = s | Prepend: s %}{% endfor %}");

    [Fact]
    public void AppendPrepend_WithinTheLimit_KeepTheirMeaning()
    {
        Assert.Equal("a-b|b-a|", Engine(100).RenderString("{{ 'a' | Append: '-b' }}|{{ 'a' | Prepend: 'b-' }}|", new { }));
        Assert.Equal("", Engine(100).RenderString("{{ nothing | Append: 'x' }}", new { }));
    }

    [Fact]
    public void Join_IsBounded()
    {
        var items = Enumerable.Range(0, 1000).Select(_ => "0123456789").ToList();

        AssertExceeds(100, "{{ items | Join: ',' }}", new { items });
        AssertExceeds(100, "{{ items | Join: ',' | Size }}", new { items });
    }

    [Fact]
    public void Join_WithinTheLimit_KeepsItsMeaning()
    {
        Assert.Equal("1, 2, 3", Engine(100).RenderString("{{ items | Join: ', ' }}", new { items = new[] { 1, 2, 3 } }));
    }

    [Fact]
    public void Replace_Expanding_IsBounded() =>
        AssertExceeds(5000, "{% assign s = input | Replace: 'a', 'bbbbbbbbbbbbbbbbbbbb' %}x", new { input = new string('a', 1000) });

    [Fact]
    public void ReplaceFirst_Expanding_IsBounded() =>
        AssertExceeds(100, "{% assign s = 'a' | ReplaceFirst: 'a', big %}x", new { big = new string('b', 200) });

    [Fact]
    public void Replace_WithinTheLimit_KeepsItsMeaning()
    {
        Assert.Equal("xbc|xbca", Engine(100).RenderString("{{ 'abc' | Replace: 'a', 'x' }}|{{ 'abca' | ReplaceFirst: 'a', 'x' }}", new { }));
        Assert.Equal("a-c", Engine(100).RenderString("{{ 'abc' | Replace: '(b)', '-' }}", new { }));
    }

    [Fact]
    public void Times_StringRepeat_IsBounded() => AssertExceeds(1000, "{% assign s = 'ab' | Times: 1000000 %}x");

    [Fact]
    public void Times_Numbers_Unchanged() => Assert.Equal("12", Engine(100).RenderString("{{ 3 | Times: 4 }}", new { }));

    [Fact]
    public void Plus_StringConcatenation_IsBounded() =>
        AssertExceeds(10, "{% assign s = a | Plus: a %}x", new { a = new string('x', 8) });

    [Fact]
    public void Plus_Numbers_Unchanged() => Assert.Equal("7", Engine(100).RenderString("{{ 3 | Plus: 4 }}", new { }));

    [Fact]
    public void Concat_Doubling_IsBounded() =>
        AssertExceeds(1000, "{% assign a = list %}{% for i in (1..30) %}{% assign a = a | Concat: a %}{% endfor %}", new { list = new[] { 1, 2, 3 } });

    [Fact]
    public void Limit_Zero_Unlimited_ForEveryPath()
    {
        var engine = Engine(0);

        var result = engine.RenderString(
            "{% capture c %}{{ 'x' | Times: 5000 }}{% endcapture %}{% assign s = c | Append: c | Prepend: c | Replace: 'x', 'yy' %}{{ s | Size }}",
            new { });

        Assert.Equal("30000", result);
    }

    [Fact]
    public void Exception_IsCatchableAsTimeoutException_AndNotSwallowedIntoTheOutput()
    {
        var error = Record.Exception(() => Engine(10).RenderString("{% capture c %}0123456789012{% endcapture %}", new { }));

        Assert.IsAssignableFrom<TimeoutException>(error);
    }

    [Fact]
    public void Limit_IsPerRender_NotPerEngine()
    {
        var engine = Engine(10);

        Assert.Equal("0123456789", engine.RenderString("0123456789", new { }));
        Assert.Equal("0123456789", engine.RenderString("0123456789", new { }));
    }

    // ---- review round: expansion is estimated before it is allocated; item counts and repeat counts are capped; loops observe the time-out

    [Fact]
    public void Replace_SubstitutionAmplification_IsRejectedBeforeItIsAllocated()
    {
        var input = new string('a', 100_000);
        var engine = Engine(500_000);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<RenderLimitExceededException>(() =>
            engine.RenderString("{% assign s = input | Replace: 'a', '$_$_$_$_$_$_$_$_$_$_' %}x", new { input }));

        // one expansion of 10 x 100 000 characters would allocate about 2 MB on its own
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000);
    }

    [Fact]
    public void Replace_LiteralReplacementAmplification_Throws()
    {
        // the first expansion (400 000) fits; the second would pass 500 000 and is refused before it is appended
        AssertExceeds(500_000, "{% assign s = 'aaa' | Replace: 'a', big %}x", new { big = new string('b', 400_000) });
    }

    [Fact]
    public void Replace_DollarTokens_StillExpandWithinTheLimit()
    {
        Assert.Equal("[b][b]", Engine(1000).RenderString("{{ 'bb' | Replace: '(b)', '[$1]' }}", new { }));
        Assert.Equal("price $5", Engine(1000).RenderString("{{ 'price 5' | Replace: ' ', ' $$' }}", new { }));
        Assert.Equal("xx", Engine(1000).RenderString("{{ 'ab' | Replace: 'a', 'x$&' | Replace: 'b', '' | Replace: 'xa', 'xx' }}", new { }));
    }

    [Fact]
    public void Join_TooManyItems_IsBounded_EvenWhenEveryItemIsEmpty()
    {
        var items = Enumerable.Repeat(string.Empty, 200).ToList();

        AssertExceeds(100, "{{ items | Join: '' }}", new { items });
    }

    [Fact]
    public void Times_HugeRepeatCount_IsRejected_EvenForAnEmptyString()
    {
        AssertExceeds(100, "{% assign s = '' | Times: 1000 %}x");
        AssertExceeds(0 + 100, "{% assign s = '' | Times: 3000000000 %}x");
        // unlimited output still refuses a count no string can hold
        Assert.Throws<RenderLimitExceededException>(() => Engine(0).RenderString("{% assign s = '' | Times: 3000000000 %}x", new { }));
    }

    [Fact]
    public void Join_Replace_Concat_ObserveTheRenderTimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var scope = TabTabGo.Templating.Liquid.RenderScope.Begin(new TemplatingOptions { MaxOutputCharacters = 0 }, null, cts.Token);
        var lazy = Enumerable.Repeat("x", 1_000_000);

        Assert.Throws<RenderLimitExceededException>(() => TabTabGo.Templating.Liquid.Filters.GuardedStringFilters.Join(lazy, ","));
        Assert.Throws<RenderLimitExceededException>(() => TabTabGo.Templating.Liquid.Filters.GuardedStringFilters.Concat(lazy, lazy));
        Assert.Throws<RenderLimitExceededException>(() =>
            TabTabGo.Templating.Liquid.Filters.GuardedStringFilters.Replace(new global::DotLiquid.Context(System.Globalization.CultureInfo.InvariantCulture), new string('a', 100_000), "a", "b"));
    }

    [Theory]
    [InlineData("Append")]
    [InlineData("Prepend")]
    [InlineData("Join")]
    [InlineData("Replace")]
    [InlineData("ReplaceFirst")]
    [InlineData("Plus")]
    [InlineData("Times")]
    [InlineData("Concat")]
    public void EveryGuardedFilter_ThrowsPastTheLimit_AndOnlyBecauseOfTheLimit(string filter)
    {
        var data = new { s = new string('x', 60), list = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" } };
        var template = filter switch
        {
            "Append" => "{% assign r = s | Append: s %}",
            "Prepend" => "{% assign r = s | Prepend: s %}",
            "Join" => "{% assign r = list | Join: s %}",
            "Replace" => "{% assign r = s | Replace: 'x', 'yy' %}",
            "ReplaceFirst" => "{% assign r = s | ReplaceFirst: 'x', s %}",
            "Plus" => "{% assign r = s | Plus: s %}",
            "Times" => "{% assign r = s | Times: 3 %}",
            _ => "{% assign r = list | Concat: list %}{% assign r = r | Concat: r %}{% assign r = r | Concat: r %}{% assign r = r | Concat: r %}{% assign r = r | Concat: r %}{% assign r = r | Concat: r %}",
        };

        AssertExceeds(100, template, data);                       // past the limit: throws
        Assert.Equal(string.Empty, Engine(100_000).RenderString(template, data)); // same template, roomy limit: renders (assign only prints nothing)
        Assert.Equal(string.Empty, Engine(0).RenderString(template, data));       // limit 0 = unlimited
    }
}
