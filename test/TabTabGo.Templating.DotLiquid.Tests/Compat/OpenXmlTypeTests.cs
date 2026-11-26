using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Compat;

/// <summary>Pins the strings <c>OpenXmlType</c> returned on 6.0.5 (spec C8-2, FR-4).</summary>
public class OpenXmlTypeTests
{
    [Theory]
    [InlineData("System.Int32", "Number")]
    [InlineData("Int16", "Number")]
    [InlineData("System.Int64", "Number")]
    [InlineData("Float", "Number")]
    [InlineData("System.Decimal", "Number")]
    [InlineData("System.Double", "Number")]
    [InlineData("System.Boolean", "Boolean")]
    [InlineData("Date", "Date")]
    [InlineData("System.DateTime", "Date")]
    [InlineData("System.DateTimeOffset", "Date")]
    [InlineData("System.String", "String")]
    [InlineData("Something.Else", "String")]
    public void OpenXmlType_SameStrings(string clrName, string expected)
    {
        Assert.Equal(expected, OpenXmlFilters.OpenXmlType(clrName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OpenXmlType_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(OpenXmlFilters.OpenXmlType(input!));
    }
}
