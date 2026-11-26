using System;
using TabTabGo.Templating.DotLiquid.Tests.Support;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Filters;

public class RubyDateTimeFormatterTests
{
    private static readonly DateTimeOffset Sample = new(2020, 3, 5, 14, 7, 9, TimeSpan.Zero);

    [Theory]
    [InlineData("%Y", "2020")]
    [InlineData("%y", "20")]
    [InlineData("%m", "03")]
    [InlineData("%d", "05")]
    [InlineData("%e", " 5")]
    [InlineData("%H", "14")]
    [InlineData("%I", "02")]
    [InlineData("%M", "07")]
    [InlineData("%S", "09")]
    [InlineData("%p", "PM")]
    [InlineData("%j", "065")]
    [InlineData("%w", "4")]
    [InlineData("%a", "Thu")]
    [InlineData("%A", "Thursday")]
    [InlineData("%b", "Mar")]
    [InlineData("%B", "March")]
    [InlineData("%Z", "+00:00")]
    [InlineData("%%", "%")]
    [InlineData("%Q", "%Q")]
    [InlineData("%Y-%m-%d", "2020-03-05")]
    [InlineData("literal", "literal")]
    public void ToString_Tokens(string pattern, string expected)
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal(expected, RubyDateTimeFormatter.ToString(Sample, pattern));
    }

    [Fact]
    public void ToString_WeekAndDateTokens()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("3/5/2020", RubyDateTimeFormatter.ToString(Sample, "%x"));
        // ICU versions differ in the space before AM/PM, so compare with the framework formatter
        Assert.Equal(Sample.ToString("T", System.Globalization.CultureInfo.CurrentCulture), RubyDateTimeFormatter.ToString(Sample, "%X"));
        Assert.Equal("Thu Mar 05 14:07:09 2020", RubyDateTimeFormatter.ToString(Sample, "%c"));
        Assert.Equal(2, RubyDateTimeFormatter.ToString(Sample, "%U").Length);
        Assert.Equal(2, RubyDateTimeFormatter.ToString(Sample, "%W").Length);
    }

    [Fact]
    public void ToString_TrailingPercent_KeptAsLiteral()
    {
        Assert.Equal("100%", RubyDateTimeFormatter.ToString(Sample, "100%"));
    }
}
