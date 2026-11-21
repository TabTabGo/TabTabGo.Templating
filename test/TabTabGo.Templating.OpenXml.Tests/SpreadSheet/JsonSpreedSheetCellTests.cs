using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

/// <summary>The JSON workbook mode cell by cell: data types, date serials, style lookup (plain and alternating rows), defaults for missing members.</summary>
public sealed class JsonSpreedSheetCellTests
{
    private static JObject Workbook(JArray rows, JArray? sheets = null, JArray? columns = null) => new()
    {
        ["Workbook"] = new JObject
        {
            ["Sheets"] = sheets ?? new JArray(new JObject { ["SheetId"] = 7, ["Name"] = "Report" }, new JObject()),
            ["Worksheet"] = new JObject
            {
                ["Columns"] = columns ?? new JArray(new JObject(), new JObject { ["Min"] = 2, ["Max"] = 3, ["BestFit"] = true, ["Width"] = 9.5 }),
                ["SheetData"] = new JObject { ["Rows"] = rows },
            },
        },
        ["StyleMap"] = new JArray(
            new JObject { ["Key"] = "Normal", ["SIndex"] = 1 },
            new JObject { ["Key"] = "Bold", ["SIndex"] = 2, ["SAlternativeIndex"] = 3 },
            new JObject { ["Key"] = "Plain", ["SIndex"] = 4 }),
    };

    private static Cell[] Cells(JObject workbook, int row = 0)
    {
        using var stream = new MemoryStream();
        new JsonSpreedSheet().CreateExcelDoc(stream, workbook);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        return document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Row>().ElementAt(row).Elements<Cell>().ToArray();
    }

    private static JObject Row(params JObject[] cells) => new() { ["Cells"] = new JArray(cells) };

    private static JObject Cell(string? type, object? value, string? format = null, uint? style = null)
    {
        var cell = new JObject();
        if (type != null)
        {
            cell["DataType"] = type;
        }

        cell["CellValue"] = value == null ? JValue.CreateNull() : new JValue(value);
        if (format != null)
        {
            cell["Format"] = format;
        }

        if (style != null)
        {
            cell["StyleIndex"] = style;
        }

        return cell;
    }

    [Fact]
    public void Booleans_AreStoredAsOneOrZero_AndOtherTextIsKept()
    {
        var cells = Cells(Workbook(new JArray(Row(Cell("Boolean", true), Cell("boolean", "false"), Cell("Boolean", "maybe")))));

        Assert.Equal(new[] { "1", "0", "maybe" }, cells.Select(c => c.CellValue!.Text));
        Assert.All(cells, c => Assert.Equal(CellValues.Boolean, c.DataType!.Value));
    }

    [Fact]
    public void Dates_AreStoredAsSerialNumbers_FromEveryKindOfValue()
    {
        var offset = new DateTimeOffset(2024, 3, 5, 0, 0, 0, TimeSpan.FromHours(4)); // 2024-03-04 20:00 UTC
        var cells = Cells(Workbook(new JArray(Row(
            Cell("Date", new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc), format: "No Such"),
            Cell("Date", offset),
            Cell("Date", "5 March 2024"),
            Cell("Date", "not a date"),
            Cell("Date", ""),
            Cell("Date", null),
            Cell("Date", new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc), style: 4)))));

        Assert.Equal("45356", cells[0].CellValue!.Text);
        Assert.Equal("45355.833333333336", cells[1].CellValue!.Text);
        Assert.Equal("45356", cells[2].CellValue!.Text);
        Assert.All(cells.Skip(3).Take(3), c => { Assert.Null(c.CellValue); Assert.Null(c.DataType); }); // B1: an empty date is an empty cell, not <c t="n"><v/></c>
        Assert.All(cells.Take(3).Append(cells[6]), c => Assert.Equal(CellValues.Number, c.DataType!.Value));
        Assert.Equal(5u, cells[0].StyleIndex!.Value); // no style found: the built-in date style
        Assert.Equal(4u, cells[6].StyleIndex!.Value);                                    // a named style wins
    }

    [Fact]
    public void Numbers_AndText_UseTheInvariantCulture_AndMissingValuesAreEmpty()
    {
        var cells = Cells(Workbook(new JArray(Row(Cell("Number", 12.5m), Cell(null, "text"), Cell("Error", "#N/A"), Cell(null, null), Cell("unknown", 3)))));

        Assert.Equal(new[] { "12.5", "text", "#N/A", string.Empty, "3" }, cells.Select(c => c.CellValue!.Text));
        Assert.Equal(CellValues.Number, cells[0].DataType!.Value);
        Assert.Equal(CellValues.String, cells[1].DataType!.Value);
        Assert.Equal(CellValues.Error, cells[2].DataType!.Value);
        Assert.Equal(CellValues.String, cells[4].DataType!.Value); // an unknown type name is text
    }

    [Fact]
    public void Styles_AreLookedUpByFormat_AndAlternateOnOddBodyRows()
    {
        var workbook = Workbook(new JArray(
            Row(Cell("Number", 1, "Bold")),                                            // header: never alternates
            Row(Cell("Number", 1, "Bold")),                                            // first body row: normal
            Row(Cell("Number", 1, "Bold"), Cell("Number", 1, "Plain")),                // second body row: the alternative index where there is one
            Row(Cell("Number", 1), Cell("Number", 1, "No Such"), Cell("Number", 1, "O'Brien"), Cell("Number", 1, "Plain", style: 5))));

        Assert.Equal(2u, Cells(workbook, 0)[0].StyleIndex!.Value);
        Assert.Equal(2u, Cells(workbook, 1)[0].StyleIndex!.Value);
        var alternate = Cells(workbook, 2);
        Assert.Equal(3u, alternate[0].StyleIndex!.Value);
        Assert.Equal(4u, alternate[1].StyleIndex!.Value); // no alternative index: the normal one
        var last = Cells(workbook, 3);
        Assert.Equal(new[] { 1u, 0u, 0u, 5u }, last.Select(c => c.StyleIndex!.Value)); // empty format = Normal; unknown name = 0; explicit index wins
    }

    [Fact]
    public void Rows_WithoutCells_AndCollapsedRows_AreWritten()
    {
        var workbook = Workbook(new JArray(new JObject { ["Collapsed"] = true }, new JObject(), Row(Cell("Number", 1))));

        using var stream = new MemoryStream();
        new JsonSpreedSheet().CreateExcelDoc(stream, workbook);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var rows = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Row>().ToList();

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].Collapsed!.Value);
        Assert.False(rows[1].Collapsed!.Value);
        Assert.Empty(rows[0].Elements<Cell>());
        Assert.Equal("A3", rows[2].Elements<Cell>().Single().CellReference!.Value);
    }

    [Fact]
    public void Sheets_AndColumns_TakeDefaultsForMissingMembers()
    {
        using var stream = new MemoryStream();
        new JsonSpreedSheet().CreateExcelDoc(stream, Workbook(new JArray()));
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);

        var sheets = document.WorkbookPart!.Workbook!.Descendants<Sheet>().ToList();
        Assert.Equal(new[] { "Report", "Sheet2" }, sheets.Select(s => s.Name!.Value));
        Assert.Equal(new[] { 7u, 2u }, sheets.Select(s => s.SheetId!.Value));

        var columns = document.WorkbookPart.WorksheetParts.First().Worksheet!.Descendants<Column>().ToList();
        Assert.Equal(1u, columns[0].Min!.Value);
        Assert.Equal(1u, columns[0].Max!.Value);
        Assert.False(columns[0].CustomWidth!.Value);
        Assert.False(columns[0].BestFit!.Value);
        Assert.Equal(9.5, columns[1].Width!.Value);
    }

    [Fact]
    public void WorkbookWithoutColumnsOrRows_IsStillWritten_ButNeedsASheet()
    {
        using var stream = new MemoryStream();
        new JsonSpreedSheet().CreateExcelDoc(stream, JObject.Parse("""{ "Workbook": { "Sheets": [ { "Name": "S" } ] } }"""));
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);

        Assert.Single(document.WorkbookPart!.Workbook!.Descendants<Sheet>());
        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(new MemoryStream(), JObject.Parse("""{ "Workbook": { } }""")));
    }

    [Fact]
    public void CreateExcelDoc_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonSpreedSheet().CreateExcelDoc((Stream)null!, new JObject()));
        Assert.Throws<ArgumentNullException>(() => new JsonSpreedSheet().CreateExcelDoc(new MemoryStream(), null!));
    }
}
