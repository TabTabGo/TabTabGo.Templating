using System;
using DotLiquid;
using TabTabGo.Templating.DotLiquid.Tests.Support;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Filters;

[Collection("liquid-static")]
public class ValueFiltersTests
{
    [Theory]
    [InlineData("abcdef", 3, "def")]
    [InlineData("abc", 10, "abc")]
    [InlineData("abc", 0, null)]
    [InlineData(null, 2, null)]
    public void ReverseSlice_Cases(string? input, int len, string? expected)
    {
        Assert.Equal(expected, ValueFilters.ReverseSlice(input!, len));
        Assert.Equal(expected, ValueFilters.SliceEnd(input!, len));
    }

    [Fact]
    public void Strip_Lstrip_Rstrip_TrimWhitespace()
    {
        Assert.Equal("a b", ValueFilters.Strip("  a b  "));
        Assert.Equal("a b  ", ValueFilters.Lstrip("  a b  "));
        Assert.Equal("  a b", ValueFilters.Rstrip("  a b  "));
        Assert.Null(ValueFilters.Strip(null!));
        Assert.Null(ValueFilters.Lstrip(null!));
        Assert.Null(ValueFilters.Rstrip(null!));
    }

    [Theory]
    [InlineData("abc123def", @"\d+", "123")]
    [InlineData("abc", @"\d+", "")]
    [InlineData("", @"\d+", "")]
    public void GetMatch_ReturnsFirstMatchValue(string input, string pattern, string expected)
    {
        Assert.Equal(expected, ValueFilters.GetMatch(input, pattern));
    }

    [Fact]
    public void GetMatch_Null_ReturnsNull() => Assert.Null(ValueFilters.GetMatch(null!, "a"));

    [Fact]
    public void Contains_Cases()
    {
        Assert.True(ValueFilters.Contains("hello world", "world"));
        Assert.False(ValueFilters.Contains("hello", "x"));
        Assert.False(ValueFilters.Contains("  ", "x"));
        Assert.False(ValueFilters.Contains(null!, "x"));
    }

    [Fact]
    public void ToString_Cases()
    {
        Assert.Equal("5", ValueFilters.ToString(5));
        Assert.Null(ValueFilters.ToString(null!));
    }

    [Fact]
    public void Select_ReturnsPropertyOrNull()
    {
        var hash = new Hash { ["a"] = 1 };
        Assert.Equal(1, ValueFilters.Select(hash, "a"));
        Assert.Null(ValueFilters.Select(hash, "missing"));
        Assert.Null(ValueFilters.Select(hash, ""));
        Assert.Null(ValueFilters.Select(null!, "a"));
    }

    [Fact]
    public void XPathSelect_ReturnsFirstNodeValue()
    {
        const string xml = "<a><b>x</b><b>y</b></a>";
        Assert.Equal("x", ValueFilters.XPathSelect(xml, "/a/b"));
        Assert.Null(ValueFilters.XPathSelect(xml, "/a/missing"));
        Assert.Equal("", ValueFilters.XPathSelect("", "/a"));
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData(".5", 0.5)]
    [InlineData("-.5", -0.5)]
    [InlineData(" 7 ", 7.0)]
    [InlineData("abc", 0.0)]
    [InlineData("", 0.0)]
    public void Number_ParsesWithEnUs(string input, double expected)
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(expected, ValueFilters.Number(input));
    }

    [Fact]
    public void DivideBy_Cases()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(2.5, ValueFilters.DivideBy(10, 4));
        Assert.Equal(0, ValueFilters.DivideBy(null!, 4));
        Assert.Equal(0, ValueFilters.DivideBy(10, null!));
        Assert.Equal(0, ValueFilters.DivideBy("x", 4));
    }

    [Fact]
    public void FormatNumber_UsesThreadCultureOrExplicitCulture()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("1,234.50", ValueFilters.FormatNumber(1234.5, "N2"));
        Assert.Equal("1.234,50", ValueFilters.FormatNumber(1234.5, "N2", "de-DE"));
    }

    [Fact]
    public void FormatCurrency_UsesGivenCulture()
    {
        Assert.Equal("$5.00", ValueFilters.FormatCurrency(5, "en-US"));
    }

    [Fact]
    public void FormatCurrencyWithCode_ReplacesSymbolWithCode()
    {
        Assert.Equal("5.00 AED", ValueFilters.FormatCurrencyWithCode(5, "en-US", "AED"));
    }

    [Fact]
    public void MarkNumbers_InsertsLeftToRightMarkBeforeNumbers()
    {
        Assert.Equal("tel &lrm;+971 &lrm;4", ValueFilters.MarkNumbers("tel +971 4"));
        Assert.Equal("", ValueFilters.MarkNumbers(""));
        Assert.Null(ValueFilters.MarkNumbers(null!));
        Assert.Equal("abc", ValueFilters.MarkNumbers("abc"));
    }

    [Fact]
    public void ToWords_English()
    {
        Assert.Equal("Twenty-One", ValueFilters.ToWords(21, "en"));
        Assert.Equal("Zero", ValueFilters.ToWords(0, "en"));
    }

    [Fact]
    public void OffsetDate_AppliesOffsetToAllInputKinds()
    {
        var utc = new DateTimeOffset(2020, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var result = ValueFilters.OffsetDate(utc, 3600);
        Assert.Equal(TimeSpan.FromHours(1), result!.Value.Offset);
        Assert.Equal(11, result.Value.Hour);

        var fromString = ValueFilters.OffsetDate("2020-01-01T10:00:00+00:00", 7200);
        Assert.Equal(12, fromString!.Value.Hour);

        var fromDateTime = ValueFilters.OffsetDate(new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc), 0);
        Assert.Equal(TimeSpan.Zero, fromDateTime!.Value.Offset);

        Assert.Null(ValueFilters.OffsetDate(null!, 0));
        Assert.NotNull(ValueFilters.OffsetDate("Now", 0));
        Assert.Throws<ArgumentException>(() => ValueFilters.OffsetDate(42, 0));
    }

    [Fact]
    public void ToTimeZone_EmptyOrUnknownName_ReturnsInput()
    {
        var input = new DateTimeOffset(2020, 1, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(input, ValueFilters.ToTimeZone(input, ""));
        Assert.Equal(input, ValueFilters.ToTimeZone(input, "No Such Zone"));
    }

    [Fact]
    public void FormatDate_DotNetFormat_WithLocale()
    {
        var previous = global::DotLiquid.Liquid.UseRubyDateFormat;
        global::DotLiquid.Liquid.UseRubyDateFormat = false;
        try
        {
            var date = new DateTimeOffset(2020, 3, 5, 14, 30, 0, TimeSpan.Zero);
            Assert.Equal("05 March 2020", ValueFilters.FormatDate(date, "dd MMMM yyyy", "en-US"));
            Assert.Equal("2020-03-05", ValueFilters.FormatDate(new DateTime(2020, 3, 5, 0, 0, 0, DateTimeKind.Utc), "yyyy-MM-dd", "en-US"));
            Assert.Null(ValueFilters.FormatDate(null!, "yyyy"));
            Assert.Equal("text", ValueFilters.FormatDate("text", "yyyy"));
            Assert.NotNull(ValueFilters.FormatDate("Now", "yyyy", "en-US"));
        }
        finally
        {
            global::DotLiquid.Liquid.UseRubyDateFormat = previous;
        }
    }

    [Fact]
    public void FormatDate_RubyFormat_WhenEnabledAndNoLocale()
    {
        var previous = global::DotLiquid.Liquid.UseRubyDateFormat;
        global::DotLiquid.Liquid.UseRubyDateFormat = true;
        try
        {
            using var _ = new CultureScope("en-US");
            var date = new DateTimeOffset(2020, 3, 5, 14, 30, 0, TimeSpan.Zero);
            Assert.Equal("2020-03-05", ValueFilters.FormatDate(date, "%Y-%m-%d"));
        }
        finally
        {
            global::DotLiquid.Liquid.UseRubyDateFormat = previous;
        }
    }

    [Fact]
    public void ParseDate_Cases()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(new DateTime(2013, 11, 1), ValueFilters.ParseDate("2013-11-01", "yyyy-MM-dd"));
        Assert.Equal(new DateTime(2013, 11, 1), ValueFilters.ParseDate("2013-11-01", ""));
        Assert.Equal("nope", ValueFilters.ParseDate("nope", "yyyy-MM-dd"));
        Assert.Equal("nope", ValueFilters.ParseDate("nope", ""));
        Assert.Equal("", ValueFilters.ParseDate("", "yyyy"));
    }
}
