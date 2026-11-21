using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

/// <summary>Fix round after the diff review: missing StyleMap, inline/shared/error cells, invalid XML text, sheet validation, date default style, style lookup, stylesheet links.</summary>
public sealed class JsonSpreedSheetFixTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-jfix-" + Guid.NewGuid().ToString("N"));

    public JsonSpreedSheetFixTests()
    {
        Directory.CreateDirectory(Path.Combine(_base, "root"));
        Directory.CreateDirectory(Path.Combine(_base, "outside"));
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private static JObject Workbook(JArray rows, JArray? sheets = null, JToken? styleMap = null, JToken? stylesheets = null)
    {
        var workbook = new JObject
        {
            ["Sheets"] = sheets ?? new JArray(new JObject { ["SheetId"] = 1, ["Name"] = "Report" }),
            ["Worksheet"] = new JObject { ["Columns"] = new JArray(), ["SheetData"] = new JObject { ["Rows"] = rows } },
        };
        if (stylesheets != null)
        {
            workbook["Stylesheets"] = stylesheets;
        }

        var root = new JObject { ["Workbook"] = workbook };
        if (styleMap != null)
        {
            root["StyleMap"] = styleMap;
        }

        return root;
    }

    private static JObject Row(params JObject[] cells) => new() { ["Cells"] = new JArray(cells) };

    private static JObject Cell(string? type, object? value, string? format = null)
    {
        var cell = new JObject { ["CellValue"] = value == null ? JValue.CreateNull() : new JValue(value) };
        if (type != null)
        {
            cell["DataType"] = type;
        }

        if (format != null)
        {
            cell["Format"] = format;
        }

        return cell;
    }

    private static SpreadsheetDocument Write(JObject workbook, MemoryStream stream, JsonSpreedSheet? writer = null)
    {
        (writer ?? new JsonSpreedSheet()).CreateExcelDoc(stream, workbook);
        stream.Position = 0;
        return SpreadsheetDocument.Open(stream, false);
    }

    private static Cell[] Cells(JObject workbook, JsonSpreedSheet? writer = null)
    {
        using var stream = new MemoryStream();
        using var document = Write(workbook, stream, writer);
        return document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Row>().First().Elements<Cell>().ToArray();
    }

    [Fact]
    public void J1_NoStyleMap_StillWritesCells_WithStyleZero()
    {
        var cells = Cells(Workbook(new JArray(Row(Cell("Number", 5), Cell(null, "x", "Bold")))));

        Assert.Equal(new[] { "5", "x" }, cells.Select(c => c.CellValue!.Text));
        Assert.All(cells, c => Assert.Equal(0u, c.StyleIndex!.Value));
    }

    [Fact]
    public void J2_InlineString_WritesAnIsElement_AndNoValue()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("InlineString", "hello"))))));

        Assert.Equal(CellValues.InlineString, cell.DataType!.Value);
        Assert.Equal("hello", cell.InlineString!.Text!.Text);
        Assert.Null(cell.CellValue);
    }

    [Fact]
    public void J2_SharedString_FallsBackToAnInlineString()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("SharedString", "shared"))))));

        Assert.Equal(CellValues.InlineString, cell.DataType!.Value);
        Assert.Equal("shared", cell.InlineString!.Text!.Text);
        Assert.Null(cell.CellValue);
    }

    [Theory]
    [InlineData("#N/A")]
    [InlineData("#REF!")]
    [InlineData("#VALUE!")]
    [InlineData("#DIV/0!")]
    [InlineData("#NAME?")]
    [InlineData("#NUM!")]
    [InlineData("#NULL!")]
    public void J2_ErrorCell_WithAnExcelErrorLiteral_IsWrittenAsAnError(string literal)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Error", literal))))));

        Assert.Equal(CellValues.Error, cell.DataType!.Value);
        Assert.Equal(literal, cell.CellValue!.Text);
    }

    [Theory]
    [InlineData("oops")]
    [InlineData("")]
    [InlineData("#n/a")]
    public void J2_ErrorCell_WithAnyOtherText_IsRejected(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Error", text))))));
    }

    [Fact]
    public void J3_CharactersInvalidInXml_AreRemoved_InEveryTextCell()
    {
        var cells = Cells(Workbook(new JArray(Row(Cell(null, "a\u0001b"), Cell("InlineString", "c\u0002d"), Cell("Number", "1\u0003")))));

        Assert.Equal("ab", cells[0].CellValue!.Text);
        Assert.Equal("cd", cells[1].InlineString!.Text!.Text);
        Assert.Equal("1", cells[2].CellValue!.Text);
    }

    [Fact]
    public void J4_NoSheets_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets: new JArray())));
        var missing = Workbook(new JArray());
        ((JObject)missing["Workbook"]!).Remove("Sheets");
        Assert.Throws<ArgumentException>(() => Cells(missing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890X")] // 32 characters
    [InlineData("a[b")]
    [InlineData("a]b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void J4_InvalidSheetName_IsRejected(string name)
    {
        var sheets = new JArray(new JObject { ["SheetId"] = 1, ["Name"] = name });

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets)));
    }

    [Fact]
    public void J4_ThirtyOneCharacterName_IsAccepted()
    {
        var name = new string('n', 31);
        using var stream = new MemoryStream();

        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 1, ["Name"] = name })), stream);

        Assert.Equal(name, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
    }

    [Fact]
    public void J4_DuplicateExplicitSheetId_IsRejected()
    {
        var sheets = new JArray(new JObject { ["SheetId"] = 3, ["Name"] = "A" }, new JObject { ["SheetId"] = 3, ["Name"] = "B" });

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets)));
    }

    private JsonSpreedSheet WriterWithStylesheet(int formats, out JToken stylesheets)
    {
        var xfs = string.Concat(Enumerable.Repeat("<xf numFmtId=\"0\"/>", formats));
        File.WriteAllText(
            Path.Combine(_base, "root", "styles.xml"),
            $"<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"{formats}\">{xfs}</cellXfs></styleSheet>");
        stylesheets = new JObject { ["TemplatePath"] = "styles.xml" };
        return new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") });
    }

    [Fact]
    public void J5_DateCell_InAShortLoadedStylesheet_KeepsStyleZero()
    {
        var writer = WriterWithStylesheet(2, out var stylesheets);

        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", "2024-03-05"))), stylesheets: stylesheets), writer));

        Assert.Equal(0u, cell.StyleIndex!.Value);
    }

    [Fact]
    public void J5_DateCell_InALoadedStylesheetWithSixFormats_UsesIndexFive()
    {
        var writer = WriterWithStylesheet(6, out var stylesheets);

        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", "2024-03-05"))), stylesheets: stylesheets), writer));

        Assert.Equal(5u, cell.StyleIndex!.Value);
    }

    [Fact]
    public void J5_DateCell_InTheGeneratedStylesheet_KeepsStyleZero_BecauseItHasNoFormats()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", "2024-03-05"))))));

        Assert.Equal(0u, cell.StyleIndex!.Value);
    }

    [Fact]
    public void J6_StyleLookup_SurvivesDuplicateKeys_QuotesAndTrailingBackslash()
    {
        var styleMap = new JArray(
            new JObject { ["Key"] = "Dup", ["SIndex"] = 3 },
            new JObject { ["Key"] = "Dup", ["SIndex"] = 4 },
            new JObject { ["Key"] = "a'b", ["SIndex"] = 5 },
            new JObject { ["Key"] = "end\\", ["SIndex"] = 6 });

        var cells = Cells(Workbook(new JArray(Row(Cell(null, "x", "Dup"), Cell(null, "x", "a'b"), Cell(null, "x", "end\\"), Cell(null, "x", "missing"))), styleMap: styleMap));

        Assert.Equal(new[] { 3u, 5u, 6u, 0u }, cells.Select(c => c.StyleIndex!.Value));
    }

    [Fact]
    public void J7_StylesheetLinkPointingOutsideTheRoot_IsRejected()
    {
        File.WriteAllText(Path.Combine(_base, "outside", "styles.xml"), "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_base, "root", "link.xml"), Path.Combine(_base, "outside", "styles.xml"));
            Directory.CreateSymbolicLink(Path.Combine(_base, "root", "dirlink"), Path.Combine(_base, "outside"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // the platform or account cannot create symbolic links
        }

        var writer = new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") });

        foreach (var path in new[] { "link.xml", "dirlink/styles.xml" })
        {
            var json = Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = path });
            Assert.Throws<ArgumentException>(() => writer.CreateExcelDoc(new MemoryStream(), json));
        }
    }
}
