using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

public sealed class JsonSpreedSheetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttg-json-" + Guid.NewGuid().ToString("N"));

    public JsonSpreedSheetTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string EmptyWorkbook = """
        {
          "Workbook": {
            "Sheets": [ { "SheetId": 1, "Name": "Report" } ],
            "Worksheet": {
              "Columns": [ { "Min": 1, "Max": 2, "BestFit": true, "Width": 14.5 } ],
              "SheetData": { "Rows": [] }
            }
          },
          "StyleMap": []
        }
        """;

    [Fact]
    public void CreateExcelDoc_WorkbookWithoutCells_WritesSheetAndColumns()
    {
        var path = Path.Combine(_dir, "out.xlsx");

        new JsonSpreedSheet().CreateExcelDoc(path, JObject.Parse(EmptyWorkbook));

        using var document = SpreadsheetDocument.Open(path, false);
        var sheet = Assert.Single(document.WorkbookPart!.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>());
        Assert.Equal("Report", sheet.Name?.Value);
        var column = Assert.Single(document.WorkbookPart.WorksheetParts.First().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Column>());
        Assert.Equal(14.5, column.Width?.Value);
        Assert.True(column.CustomWidth?.Value);
    }

    [Fact]
    public void Engine_Render_WithoutTemplatePath_UsesWorkbookDefinition()
    {
        using var result = Assert.IsType<MemoryStream>(new Templating.OpenXml.SpreadSheet.TemplatingEngine().Render("", JObject.Parse(EmptyWorkbook)));

        using var document = SpreadsheetDocument.Open(result, false);
        Assert.Single(document.WorkbookPart!.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>());
    }
}
