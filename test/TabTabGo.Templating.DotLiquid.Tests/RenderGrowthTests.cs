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
}
