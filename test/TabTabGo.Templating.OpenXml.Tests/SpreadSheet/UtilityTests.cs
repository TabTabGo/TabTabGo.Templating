using System;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

public class UtilityTests
{
    [Theory]
    [InlineData("A1", "A")]
    [InlineData("AB12", "AB")]
    [InlineData("zz9", "zz")]
    public void GetColumnName_ReturnsLetters(string cell, string expected) => Assert.Equal(expected, Utility.GetColumnName(cell));

    [Theory]
    [InlineData("A1", 1)]
    [InlineData("AB12", 12)]
    [InlineData("C300", 300)]
    public void GetRowIndex_ReturnsNumber(string cell, int expected) => Assert.Equal(expected, Utility.GetRowIndex(cell));

    [Theory]
    [InlineData("A1", 1)]
    [InlineData("Z9", 26)]
    [InlineData("AA1", 27)]
    [InlineData("AZ1", 52)]
    [InlineData("BA1", 53)]
    public void GetCellColIndex_ReturnsColumnNumber(string cell, int expected) => Assert.Equal(expected, Utility.GetCellColIndex(cell));

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    public void NextColumn_ConvertsNumberToName(int column, string expected) => Assert.Equal(expected, Utility.NextColumn(column));

    [Fact]
    public void NextColumn_Zero_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => Utility.NextColumn(0));

    [Theory]
    [InlineData("A1", "B")]
    [InlineData("Z1", "AA")]
    [InlineData("AZ5", "BA")]
    public void GetNextColumnName_ReturnsFollowingColumn(string cell, string expected) => Assert.Equal(expected, Utility.GetNextColumnName(cell));

    [Theory]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    [InlineData(16384, "XFD")]
    public void NextColumn_TheColumnTheory(int column, string expected) => Assert.Equal(expected, Utility.NextColumn(column));

    [Fact]
    public void GetRowIndex_WithoutDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Utility.GetRowIndex("A"));
        Assert.Throws<ArgumentException>(() => Utility.GetRowIndex(""));
    }

    [Theory]
    [InlineData("a1", 1)]
    [InlineData("ab1", 28)]
    [InlineData("xfd1", 16384)]
    public void GetCellColIndex_LowerCase_IsCaseInsensitive(string cell, int expected) => Assert.Equal(expected, Utility.GetCellColIndex(cell));

    [Fact]
    public void Utility_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Utility.GetColumnName(null!));
        Assert.Throws<ArgumentNullException>(() => Utility.GetRowIndex(null!));
    }
}
