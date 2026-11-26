using System.Globalization;
using TabTabGo.Templating.Liquid.Extensions;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

public class HumanizerExtensionsTests
{
    [Theory]
    [InlineData(0, "Zero")]
    [InlineData(1, "One")]
    [InlineData(21, "Twenty-One")]
    [InlineData(100, "One Hundred")]
    [InlineData(1000, "One Thousand")]
    public void ToWords_English_Integers(int value, string expected)
    {
        Assert.Equal(expected, ((decimal)value).ToWords("en"));
    }

    [Fact]
    public void ToWords_English_Fraction_UsesPointsSeparator()
    {
        Assert.Equal("One points Fifty", 1.5m.ToWords("en"));
    }

    [Fact]
    public void ToWords_Arabic_ReturnsArabicWords()
    {
        var words = 5m.ToWords("ar");
        Assert.False(string.IsNullOrWhiteSpace(words));
        Assert.Matches("^[\\u0600-\\u06FF ]+$", words);
    }

    [Fact]
    public void GetSeparatorAsWords_PerCulture()
    {
        Assert.Equal(" points ", new CultureInfo("en-US").GetSeparatorAsWords());
        Assert.Equal(" نقطة ", new CultureInfo("ar-AE").GetSeparatorAsWords());
        Assert.Equal(", ", new CultureInfo("fr-FR").GetSeparatorAsWords());
    }

    [Fact]
    public void ToLong_Truncates()
    {
        Assert.Equal(12L, 12.9m.ToLong());
        Assert.Equal(-12L, (-12.9m).ToLong());
    }

    [Theory]
    [InlineData(1.25, 25)]
    [InlineData(1.05, 5)]
    [InlineData(3, 0)]
    [InlineData(-2.5, 50)]
    public void GetFraction_TwoDigits(double value, long expected)
    {
        Assert.Equal(expected, ((decimal)value).GetFraction());
    }

    [Fact]
    public void NumberSeparators_CoverEnglishAndArabic()
    {
        Assert.Equal("points", HumanizerExtensions.NumberSeparators["en"]);
        Assert.True(HumanizerExtensions.NumberSeparators.ContainsKey("ar"));
    }
}
