using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;
using Engine = TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

/// <summary>X-1, X-2 and X-3 (8.0.4): the JSON workbook path works, booleans keep their value, no temp files leak.</summary>
public sealed class SecurityBackportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttg-x-" + Guid.NewGuid().ToString("N"));

    public SecurityBackportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateTemplate(string cellText)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".xlsx");
        using (var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook(new Sheets(), new CalculationProperties());
            var sst = workbookPart.AddNewPart<SharedStringTablePart>();
            sst.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text(cellText)));
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData(new Row(new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") })));
            workbookPart.Workbook.GetFirstChild<Sheets>()!.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Data" });
            workbookPart.Workbook.Save();
        }

        return path;
    }

    private static string[] LeakedTempFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "tmp*.tmp").Where(IsEmptyFile).ToArray();

    // other processes create and delete temp files concurrently, so a file may vanish between listing and inspection
    private static bool IsEmptyFile(string path)
    {
        try
        {
            return new FileInfo(path).Length == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Fact]
    public void Render_BoolTrue_CellTrue()
    {
        var template = CreateTemplate("{%flag%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, new { flag = true }));

        using var document = SpreadsheetDocument.Open(result, false);
        var cell = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().First();
        Assert.Equal(CellValues.Boolean, cell.DataType!.Value);
        Assert.Equal("1", cell.CellValue!.Text);
    }

    [Fact]
    public void Render_BoolFalse_CellFalse()
    {
        var template = CreateTemplate("{%flag%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, new { flag = false }));

        using var document = SpreadsheetDocument.Open(result, false);
        var cell = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().First();
        Assert.Equal("0", cell.CellValue!.Text);
    }

    [Fact]
    public void Render_NoTempFilesLeft()
    {
        var template = CreateTemplate("{%v%}");
        var before = LeakedTempFiles().ToHashSet();

        for (var i = 0; i < 5; i++)
        {
            new Engine().Render(template, new { v = i }).As<MemoryStream>().Dispose();
        }

        Assert.Empty(LeakedTempFiles().Except(before));
    }

    [Fact]
    public void Render_Throws_NoTempFilesLeft()
    {
        var before = LeakedTempFiles().ToHashSet();

        Assert.ThrowsAny<IOException>(() => new Engine().Render(Path.Combine(_dir, "missing.xlsx"), new { v = 1 }));

        Assert.Empty(LeakedTempFiles().Except(before));
    }

    [Fact]
    public void Render_ReturnedStreamIsReadyToReadFromTheStart()
    {
        var template = CreateTemplate("{%v%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, new { v = "x" }));

        Assert.Equal(0, result.Position);
        Assert.True(result.Length > 0);
    }

    private const string Workbook = """
        {
          "Workbook": {
            "Sheets": [ { "SheetId": 1, "Name": "Report" } ],
            "Worksheet": {
              "Columns": [ { "Min": 1, "Max": 4, "BestFit": true } ],
              "SheetData": { "Rows": [
                { "Collapsed": false, "Cells": [
                  { "DataType": "String",  "CellValue": "Name" },
                  { "DataType": "Number",  "CellValue": 12.5 },
                  { "DataType": "Boolean", "CellValue": true },
                  { "DataType": "Date",    "CellValue": "2020-03-05T00:00:00Z" } ] }
              ] }
            }
          },
          "StyleMap": []
        }
        """;

    [Fact]
    public void JsonWorkbook_Number_String_Bool_Date_Written()
    {
        using var result = Assert.IsType<MemoryStream>(new Engine().Render("", JObject.Parse(Workbook)));

        using var document = SpreadsheetDocument.Open(result, false);
        var cells = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().ToList();
        Assert.Equal(4, cells.Count);
        Assert.Equal(CellValues.String, cells[0].DataType!.Value);
        Assert.Equal("Name", cells[0].CellValue!.Text);
        Assert.Equal(CellValues.Number, cells[1].DataType!.Value);
        Assert.Equal("12.5", cells[1].CellValue!.Text);
        Assert.Equal(CellValues.Boolean, cells[2].DataType!.Value);
        Assert.Equal("1", cells[2].CellValue!.Text);
        Assert.Equal(CellValues.Number, cells[3].DataType!.Value);
        Assert.False(string.IsNullOrEmpty(cells[3].CellValue!.Text));
    }

    [Fact]
    public void JsonWorkbook_UnknownDataType_FallsBackToString()
    {
        var json = Workbook.Replace("\"DataType\": \"String\"", "\"DataType\": \"Nonsense\"");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render("", JObject.Parse(json)));

        using var document = SpreadsheetDocument.Open(result, false);
        var first = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().First();
        Assert.Equal(CellValues.String, first.DataType!.Value);
    }

    [Fact]
    public void JsonSpreedSheet_StreamOverload_WritesDocument()
    {
        using var stream = new MemoryStream();

        new JsonSpreedSheet().CreateExcelDoc(stream, JObject.Parse(Workbook));

        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        Assert.Single(document.WorkbookPart!.Workbook!.Descendants<Sheet>());
    }
}

internal static class StreamAsExtensions
{
    public static T As<T>(this object value) => (T)value;
}
