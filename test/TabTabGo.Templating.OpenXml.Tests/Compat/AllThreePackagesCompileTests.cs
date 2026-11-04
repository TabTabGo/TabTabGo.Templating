using DocumentFormat.OpenXml.Spreadsheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.Compat;

/// <summary>
/// FR-3: a project referencing all three packages compiles and can use Open XML SDK types. On 8.0.2 this failed with
/// CS0433 because the Dotliquid package carried the Joelharkes Open XML fork.
/// </summary>
public class AllThreePackagesCompileTests
{
    [Fact]
    public void AllThreePackages_Compile()
    {
        var cell = new Cell { CellReference = "A1", CellValue = new CellValue("1") };
        var liquid = new Templating.Liquid.TemplatingEngine();
        var xlsx = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
        Templating.TemplatingEngine baseType = liquid;

        Assert.Equal("A1", cell.CellReference?.Value);
        Assert.Equal("Liquid", baseType.EngineName);
        Assert.NotNull(xlsx.EngineName);
    }
}
