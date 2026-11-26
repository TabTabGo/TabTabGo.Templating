using TabTabGo.Templating.Liquid.Filters;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>Zone names come from templates and data: unknown names must not grow any process-wide cache (resource cap).</summary>
public sealed class TimeZoneCacheTests
{
    private static readonly DateTime Moment = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void BogusNames_DoNotGrowTheCache()
    {
        ValueFilters.ToTimeZone(Moment, "Asia/Dubai"); // make sure the cache is warm and the baseline is stable
        var before = ValueFilters.TimeZoneCacheCount;

        for (var i = 0; i < 10_000; i++)
        {
            Assert.Equal(Moment, ValueFilters.ToTimeZone(Moment, "Bogus/Zone" + i));
        }

        Assert.Equal(before, ValueFilters.TimeZoneCacheCount);
    }

    [Fact]
    public void ValidNames_StillResolve_AndAreCached()
    {
        var before = ValueFilters.TimeZoneCacheCount;

        var first = Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Moment, "Arabian Standard Time"));
        var second = Assert.IsType<DateTimeOffset>(ValueFilters.ToTimeZone(Moment, "Arabian Standard Time"));

        Assert.Equal(TimeSpan.FromHours(4), first.Offset);
        Assert.Equal(first, second);
        Assert.True(ValueFilters.TimeZoneCacheCount <= before + 1);
    }

    [Fact]
    public void TheCache_IsCapped_EvenForValidAliases()
    {
        Assert.True(ValueFilters.TimeZoneCacheCount <= ValueFilters.TimeZoneCacheLimit);
        Assert.Equal(Moment, ValueFilters.ToTimeZone(Moment, new string('x', 100_000)));
        Assert.True(ValueFilters.TimeZoneCacheCount <= ValueFilters.TimeZoneCacheLimit);
    }
}
