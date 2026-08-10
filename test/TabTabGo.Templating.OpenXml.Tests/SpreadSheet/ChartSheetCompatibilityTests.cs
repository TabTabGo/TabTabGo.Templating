using System;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

public class ChartSheetCompatibilityTests
{
    [Fact]
    public void Render_WorkbookWithChartSheet_ProcessesNormalWorksheet()
    {
        var templatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        try
        {
            CreateTemplate(templatePath);

            var engine = new Templating.OpenXml.SpreadSheet.TemplatingEngine();
            using var result = Assert.IsType<MemoryStream>(
                engine.Render(templatePath, new { Result = "Processed" })
            );
            using var document = SpreadsheetDocument.Open(result, false);
            var workbookPart = Assert.IsType<WorkbookPart>(document.WorkbookPart);
            var worksheetPart = Assert.Single(workbookPart.WorksheetParts);
            var cell = Assert.Single(worksheetPart.Worksheet.Descendants<Cell>());

            Assert.Equal("Processed", cell.InlineString?.Text?.Text);
            Assert.Single(workbookPart.GetPartsOfType<ChartsheetPart>());
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    private static void CreateTemplate(string path)
    {
        using var document = SpreadsheetDocument.Create(
            path,
            DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook
        );
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook(new Sheets(), new CalculationProperties());

        var sharedStringTablePart = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStringTablePart.SharedStringTable = new SharedStringTable(
            new SharedStringItem(new Text("{%Result%}"))
        );

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(
            new SheetData(
                new Row(
                    new Cell
                    {
                        CellReference = "A1",
                        DataType = CellValues.SharedString,
                        CellValue = new CellValue("0"),
                    }
                )
            )
        );

        var chartsheetPart = workbookPart.AddNewPart<ChartsheetPart>();
        chartsheetPart.Chartsheet = new Chartsheet();

        var sheets = workbookPart.Workbook.GetFirstChild<Sheets>()!;
        sheets.Append(
            new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Data",
            },
            new Sheet
            {
                Id = workbookPart.GetIdOfPart(chartsheetPart),
                SheetId = 2,
                Name = "Chart",
            }
        );

        workbookPart.Workbook.Save();
    }
}
