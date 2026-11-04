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
    public void NextColumn_Zero_Throws() => Assert.Throws<Exception>(() => Utility.NextColumn(0));

    [Theory]
    [InlineData("A1", "B")]
    [InlineData("Z1", "AA")]
    [InlineData("AZ5", "BA")]
    public void GetNextColumnName_ReturnsFollowingColumn(string cell, string expected) => Assert.Equal(expected, Utility.GetNextColumnName(cell));
}
