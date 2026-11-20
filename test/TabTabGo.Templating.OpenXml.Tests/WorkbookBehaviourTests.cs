using System.Diagnostics;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.TestKit;
using Engine = TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine;

namespace TabTabGo.Templating.OpenXml.Tests;

/// <summary>X-4 to X-21 and B-7: what a rendered workbook contains. Every result must also pass the Open XML validator.</summary>
public sealed class WorkbookBehaviourTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private byte[] Render(TemplateBuilder template, object data, string culture = "en-US")
    {
        var path = template.Save(_dir, Guid.NewGuid().ToString("N") + ".xlsx");
        using var result = (MemoryStream)new Engine().Render(path, data, culture);
        var bytes = result.ToArray();
        XlsxGolden.AssertValid(bytes);
        return bytes;
    }

    private static Dictionary<string, Cell> Cells(byte[] workbook, int sheet = 0)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var part = document.WorkbookPart!.WorksheetParts.ElementAt(sheet);
        return part.Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!, c => (Cell)c.CloneNode(true));
    }

    private static string Text(byte[] workbook, string cell, int sheet = 0)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var part = document.WorkbookPart!.WorksheetParts.ElementAt(sheet);
        var found = part.Worksheet.Descendants<Cell>().Single(c => c.CellReference!.Value == cell);
        if (found.DataType?.Value == CellValues.SharedString)
        {
            return document.WorkbookPart.SharedStringTablePart!.SharedStringTable.Elements<SharedStringItem>().ElementAt(int.Parse(found.CellValue!.Text, CultureInfo.InvariantCulture)).InnerText;
        }

        return found.InlineString?.Text?.Text ?? found.CellValue?.Text ?? string.Empty;
    }

    private static T WithWorksheet<T>(byte[] workbook, Func<Worksheet, WorkbookPart, T> read, int sheet = 0)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        return read(document.WorkbookPart!.WorksheetParts.ElementAt(sheet).Worksheet, document.WorkbookPart);
    }

    private static string TableRef(byte[] workbook, int table = 0) =>
        WithWorksheet(workbook, (ws, wb) => wb.WorksheetParts.First().TableDefinitionParts.ElementAt(table).Table.Reference!.Value!);

    // ---- X-5 typing

    [Fact]
    public void Render_LeadingZeroId_String()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.id%}").Text("A2", "{%$.phone%}"), new { id = "00123", phone = "+971501234567" });

        Assert.Equal("00123", Text(bytes, "A1"));
        Assert.Equal("+971501234567", Text(bytes, "A2"));
        Assert.NotEqual(CellValues.Number, Cells(bytes)["A1"].DataType?.Value);
    }

    [Fact]
    public void Render_Decimal_DeDeThread_Invariant()
    {
        using var scope = new CultureScope("de-DE");

        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.amount%}").Text("A2", "{%$.big%}"), new { amount = 12.5, big = 1234567.25m }, "de-DE");

        Assert.Equal("12.5", Cells(bytes)["A1"].CellValue!.Text);
        Assert.Equal("1234567.25", Cells(bytes)["A2"].CellValue!.Text);
    }

    [Fact]
    public void Render_NonFiniteNumbers_AreStrings()
    {
        var data = JObject.FromObject(new { nan = double.NaN, inf = double.PositiveInfinity });

        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.nan%}").Text("A2", "{%$.inf%}"), data);

        Assert.NotEqual(CellValues.Number, Cells(bytes)["A1"].DataType?.Value);
        Assert.NotEqual(CellValues.Number, Cells(bytes)["A2"].DataType?.Value);
    }

    [Fact]
    public void Render_FormatN_ParsesTextAsNumber()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.v:#N%}").Text("A2", "{%$.w:#N%}"), new { v = "12.5", w = "abc" });

        Assert.Equal("12.5", Cells(bytes)["A1"].CellValue!.Text);
        Assert.Equal("abc", Text(bytes, "A2"));
    }

    // ---- X-6 dates

    [Fact]
    public void Render_Date_Serial()
    {
        var data = JObject.Parse("""{"d":"2026-01-15T00:00:00Z","e":"2026-01-15T12:00:00Z"}""");

        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.d%}").Text("A2", "{%$.e%}"), data);

        Assert.Equal("46037", Cells(bytes)["A1"].CellValue!.Text);
        Assert.Equal("46037.5", Cells(bytes)["A2"].CellValue!.Text);
        Assert.Null(Cells(bytes)["A1"].InlineString);
    }

    [Fact]
    public void Render_DateWithExplicitFormat_IsTextInTheRenderCulture()
    {
        var data = JObject.Parse("""{"d":"2026-01-15T00:00:00Z"}""");

        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.d:#dd MMMM yyyy%}"), data, "fr-FR");

        Assert.Equal("15 janvier 2026", Text(bytes, "A1"));
    }

    // ---- X-7 null tolerance

    [Fact]
    public void Render_NoSharedStringPart_DoesNotThrow()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Inline("A1", "plain").WithoutSharedStringPart(), new { a = 1 });

        Assert.Equal("plain", Text(bytes, "A1"));
    }

    [Fact]
    public void Render_CellWithoutValue_DoesNotThrow()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").EmptySharedStringCell("A1").Text("B1", "{%$.a%}"), new { a = "x" });

        Assert.Equal("x", Text(bytes, "B1"));
    }

    [Fact]
    public void Render_NoSheetDimension_DoesNotThrow()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("A4", "{%$.items::n%}").Table("T", "A3:A4").WithoutDimension();

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" } } });

        Assert.Equal("A1:A5", WithWorksheet(bytes, (ws, _) => ws.SheetDimension!.Reference!.Value!));
    }

    [Fact]
    public void Render_NoCalculationProperties_Created()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.a%}").WithoutCalculationProperties(), new { a = 1 });

        var calc = WithWorksheet(bytes, (_, wb) => wb.Workbook.CalculationProperties);
        Assert.NotNull(calc);
        Assert.True(calc.FullCalculationOnLoad?.Value);
    }

    // ---- X-8 / X-9 tables

    [Fact]
    public void Render_TableWithEmptyRow()
    {
        // Excel does not store empty rows: footer rows 5 and 6 are not stored, row 7 is
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("A4", "{%$.items::n%}").Text("A7", "Footer")
            .Table("T", "A3:A7");

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" }, new { n = "c" } } });

        Assert.Equal("c", Text(bytes, "A6"));
        Assert.Equal("Footer", Text(bytes, "A9")); // the gap above the footer is kept: 7 + 2
        Assert.Equal("A3:A9", TableRef(bytes));
    }

    [Fact]
    public void Render_EmptyArray_ValidTable()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("B3", "Qty").Text("A4", "{%$.items::n%}").Text("B4", "{%$.items::q%}")
            .Table("T", "A3:B4", withAutoFilter: true);

        var bytes = Render(template, new { items = Array.Empty<object>() });

        Assert.Equal("A3:B4", TableRef(bytes));
        Assert.Equal("A3:B4", WithWorksheet(bytes, (ws, wb) => wb.WorksheetParts.First().TableDefinitionParts.First().Table.AutoFilter!.Reference!.Value!));
        Assert.Equal(string.Empty, Text(bytes, "A4"));
    }

    [Fact]
    public void Render_Table_AutoFilterFollowsTheDataRows()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("A4", "{%$.items::n%}").Text("A5", "Total")
            .Table("T", "A3:A5", withAutoFilter: true);

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" }, new { n = "c" } } });

        Assert.Equal("A3:A7", TableRef(bytes));
        Assert.Equal("A3:A6", WithWorksheet(bytes, (ws, wb) => wb.WorksheetParts.First().TableDefinitionParts.First().Table.AutoFilter!.Reference!.Value!));
    }

    // ---- X-10 / X-11 two tables, shifts

    [Fact]
    public void Render_TwoTablesWithFormulas_Golden()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Item").Text("B3", "Qty").Text("C3", "Total")
            .Text("A4", "{%$.items::n%}").Text("B4", "{%$.items::q%}").Formula("C4", "B4*2")
            .Text("A8", "Line").Text("B8", "Qty").Text("C8", "Total")
            .Text("A9", "{%$.lines::n%}").Text("B9", "{%$.lines::q%}").Formula("C9", "B9*3")
            .Table("Items", "A3:C4").Table("Lines", "A8:C9");
        var data = new
        {
            items = new[] { new { n = "a", q = 1 }, new { n = "b", q = 2 }, new { n = "c", q = 3 } },
            lines = new[] { new { n = "x", q = 5 }, new { n = "y", q = 6 } },
        };

        var path = template.Save(_dir);
        using var result = (MemoryStream)new Engine().Render(path, data);
        XlsxGolden.Assert(result, Path.Combine(AppContext.BaseDirectory, "Golden", "Xlsx", "two-tables-formulas"));

        var bytes = result.ToArray();
        Assert.Equal("A3:C6", TableRef(bytes, 0));
        Assert.Equal("A10:C12", TableRef(bytes, 1));
        Assert.Equal("x", Text(bytes, "A11"));
        var masters = WithWorksheet(bytes, (ws, _) => ws.Descendants<CellFormula>().Where(f => f.Reference != null).Select(f => f.SharedIndex!.Value).ToList());
        Assert.Equal(masters.Count, masters.Distinct().Count());
        Assert.Equal(2, masters.Count);
        // the second table moved down by two rows: its formula follows its own row
        Assert.Equal(new[] { "B4*2", "B11*3" }, WithWorksheet(bytes, (ws, _) => ws.Descendants<CellFormula>().Where(f => f.Reference != null).Select(f => f.Text).ToArray()));
    }

    [Fact]
    public void Render_MergedCellsBelowTable_Shifted()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("A4", "{%$.items::n%}").Text("A10", "Note").Merge("A10:B10").Merge("A1:B1")
            .Table("T", "A3:A4");

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" }, new { n = "c" } } });

        var merges = WithWorksheet(bytes, (ws, _) => ws.Descendants<MergeCell>().Select(m => m.Reference!.Value!).OrderBy(r => r).ToList());
        Assert.Equal(new[] { "A1:B1", "A12:B12" }, merges);
        Assert.Equal("Note", Text(bytes, "A12"));
    }

    [Fact]
    public void Render_RowsBelowTable_KeepTheirGaps()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("A4", "{%$.items::n%}").Text("A10", "Below").Text("A20", "Far below")
            .Table("T", "A3:A4");

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" } } });

        Assert.Equal("Below", Text(bytes, "A11"));
        Assert.Equal("Far below", Text(bytes, "A21"));
    }

    // ---- X-12 shared strings

    [Fact]
    public void Render_5000SharedStrings_UnderBudget()
    {
        var template = TemplateBuilder.Create().Sheet("S").Text("A3", "Name").Text("A4", "{%$.items::n:#SS%}").Table("T", "A3:A4");
        var items = Enumerable.Range(0, 5000).Select(i => new { n = "value " + i }).ToArray();
        var path = template.Save(_dir);
        var watch = Stopwatch.StartNew();

        using var result = (MemoryStream)new Engine().Render(path, new { items });

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"rendering took {watch.Elapsed}");
        Assert.Equal("value 4999", Text(result.ToArray(), "A5003"));
    }

    [Fact]
    public void Render_SharedStrings_ReuseEqualEntries()
    {
        var template = TemplateBuilder.Create().Sheet("S").Text("A3", "Name").Text("A4", "{%$.items::n:#SS%}").Table("T", "A3:A4");

        var bytes = Render(template, new { items = new[] { new { n = "same" }, new { n = "same" }, new { n = "other" } } });

        var indexes = Cells(bytes).Where(c => c.Key is "A4" or "A5" or "A6").OrderBy(c => c.Key).Select(c => c.Value.CellValue!.Text).ToArray();
        Assert.Equal(indexes[0], indexes[1]);
        Assert.NotEqual(indexes[0], indexes[2]);
    }

    // ---- X-13 invalid xml characters

    [Fact]
    public void ToValidXml_Emoji_Kept()
    {
        Assert.Equal("a\U0001F600b", Engine.ToValidXmlCharactersString("a\U0001F600b"));
        Assert.Equal("\U00020000", Engine.ToValidXmlCharactersString("\U00020000"));
        Assert.Equal("ab", Engine.ToValidXmlCharactersString("a\uD83Db")); // a lone surrogate is not valid XML
        Assert.Equal("ab", Engine.ToValidXmlCharactersString("a\u0001b"));
        Assert.Equal(-1, Engine.IndexOfFirstInvalidXMLChar("a\U0001F600b"));
        Assert.Equal(1, Engine.IndexOfFirstInvalidXMLChar("a\uD83Db"));
    }

    [Fact]
    public void Render_EmojiInText_Survives()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.t%}"), new { t = "ok \U0001F600" });

        Assert.Equal("ok \U0001F600", Text(bytes, "A1"));
    }

    // ---- X-14 data roots

    [Fact]
    public void Render_NullData_ArgumentNullException()
    {
        var path = TemplateBuilder.Create().Sheet("S").Text("A1", "x").Save(_dir);

        Assert.Throws<ArgumentNullException>(() => new Engine().Render(path, null!));
    }

    [Fact]
    public void Render_ArrayRoot_IsDollar()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$[0].name%}"), JArray.Parse("""[{"name":"first"}]"""));

        Assert.Equal("first", Text(bytes, "A1"));
    }

    // ---- X-19 / X-21

    [Fact]
    public void Render_FormatMarkerRightAfterAOneLetterPath_Applied()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%a:#IS%}"), new { a = "x" });

        Assert.Equal("x", Text(bytes, "A1"));
        Assert.Equal(CellValues.InlineString, Cells(bytes)["A1"].DataType!.Value);
    }

    [Fact]
    public void Render_InlineStringCell_HasNoStaleSharedStringValue()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.t%}").Text("A2", "{%$.n%}"), new { t = "text", n = 5 });

        Assert.Null(Cells(bytes)["A1"].CellValue);
        Assert.Null(Cells(bytes)["A2"].InlineString);
        Assert.Equal("5", Cells(bytes)["A2"].CellValue!.Text);
    }

    [Fact]
    public void Render_TextWithEdgeSpaces_IsPreserved()
    {
        var bytes = Render(TemplateBuilder.Create().Sheet("S").Text("A1", "{%$.t%}"), new { t = "  padded  " });

        Assert.Equal("  padded  ", Text(bytes, "A1"));
    }

    [Fact]
    public void Render_StaticTextInATemplateRow_StaysText()
    {
        var template = TemplateBuilder.Create().Sheet("S")
            .Text("A3", "Name").Text("B3", "Kind").Text("A4", "{%$.items::n%}").Text("B4", "constant")
            .Table("T", "A3:B4");

        var bytes = Render(template, new { items = new[] { new { n = "a" }, new { n = "b" } } });

        Assert.Equal("constant", Text(bytes, "B4"));
        Assert.Equal("constant", Text(bytes, "B5"));
    }

    // ---- B-7

    [Fact]
    public async Task Parallel_20Workbooks_EachValid()
    {
        var path = TemplateBuilder.Create().Sheet("S")
            .Text("A1", "{%$.title%}").Text("A3", "Name").Text("A4", "{%$.items::n:#SS%}").Table("T", "A3:A4")
            .Save(_dir);
        var engine = new Engine();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var data = new { title = "book " + i, items = Enumerable.Range(0, 30).Select(n => new { n = $"w{i}-{n}" }).ToArray() };
            using var stream = (MemoryStream)engine.Render(path, data);
            return (i, bytes: stream.ToArray());
        }))).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        foreach (var (i, bytes) in results)
        {
            XlsxGolden.AssertValid(bytes);
            Assert.Equal("book " + i, Text(bytes, "A1"));
            Assert.Equal($"w{i}-29", Text(bytes, "A33"));
        }
    }

    // ---- JSON workbook (X-4, X-17)

    private const string JsonWorkbookText = """
        {
          "Workbook": {
            "Sheets": [ { "SheetId": 1, "Name": "First" }, { "SheetId": 2, "Name": "Second" } ],
            "Worksheet": {
              "Columns": [ { "Min": 1, "Max": 3, "BestFit": true } ],
              "SheetData": { "Rows": [
                { "Collapsed": false, "Cells": [
                  { "DataType": "String", "CellValue": "Name" },
                  { "DataType": "Date",   "CellValue": "2020-03-05T00:00:00Z" },
                  { "DataType": "Number", "CellValue": 12.5 } ] }
              ] }
            }
          },
          "StyleMap": []
        }
        """;

    private static byte[] RenderJson(string json, string culture = "en-US")
    {
        using var stream = (MemoryStream)new Engine().Render("", JObject.Parse(json), culture);
        return stream.ToArray();
    }

    [Fact]
    public void JsonWorkbook_TwoSheets_Valid()
    {
        var bytes = RenderJson(JsonWorkbookText);

        XlsxGolden.AssertValid(bytes);
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        Assert.Equal(new[] { "First", "Second" }, document.WorkbookPart!.Workbook.Descendants<Sheet>().Select(s => s.Name!.Value!).ToArray());
        Assert.Equal(2, document.WorkbookPart.WorksheetParts.Count());
    }

    [Fact]
    public void JsonWorkbook_Date_IsASerialNumber_WithAnExistingDateStyle()
    {
        var bytes = RenderJson(JsonWorkbookText, "de-DE");

        var date = Cells(bytes)["B1"];
        Assert.Equal("43895", date.CellValue!.Text);
        Assert.NotEqual(CellValues.String, date.DataType?.Value);
        var styleCount = WithWorksheet(bytes, (_, wb) => wb.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().Count());
        Assert.True(date.StyleIndex!.Value < styleCount);
        Assert.Equal("12.5", Cells(bytes)["C1"].CellValue!.Text);
    }

    [Fact]
    public void JsonWorkbook_OneSheet_Valid()
    {
        var json = JsonWorkbookText.Replace("[ { \"SheetId\": 1, \"Name\": \"First\" }, { \"SheetId\": 2, \"Name\": \"Second\" } ]", "[ { \"SheetId\": 1, \"Name\": \"Only\" } ]");

        XlsxGolden.AssertValid(RenderJson(json));
    }
}
