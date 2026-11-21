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

    private const string StyleXml = "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>";

    private static bool TryLink(string link, string target, bool directory)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            if (directory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false; // the platform or account cannot create symbolic links
        }
    }

    private void AssertRefused(string path)
    {
        var writer = new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") });
        var json = Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = path });

        Assert.Throws<ArgumentException>(() => writer.CreateExcelDoc(new MemoryStream(), json));
    }

    [Fact]
    public void S4_RelativeLinkChainThroughALinkBackToTheRoot_IsRefused()
    {
        // R/a/b/c/sub -> R, R/l2 -> ../secret.xml, R/l1 -> a/b/c/sub/l2: lexically inside R, physically the OS reads _base/secret.xml
        var root = Path.Combine(_base, "root");
        File.WriteAllText(Path.Combine(_base, "secret.xml"), StyleXml);
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
        if (!TryLink(Path.Combine(root, "a", "b", "c", "sub"), root, directory: true)
            || !TryLink(Path.Combine(root, "l2"), Path.Combine("..", "secret.xml"), directory: false)
            || !TryLink(Path.Combine(root, "l1"), Path.Combine("a", "b", "c", "sub", "l2"), directory: false))
        {
            return;
        }

        AssertRefused("l1");
    }

    [Fact]
    public void S4_RootLinkIntoAnotherDirectoryWhoseLinkClimbs_IsRefused()
    {
        // R/sub -> _base/outside/ct, _base/outside/ct/l2 -> ../secret.xml, R/l1 -> sub/l2
        var root = Path.Combine(_base, "root");
        Directory.CreateDirectory(Path.Combine(_base, "outside", "ct"));
        File.WriteAllText(Path.Combine(_base, "outside", "secret.xml"), StyleXml);
        if (!TryLink(Path.Combine(root, "sub"), Path.Combine(_base, "outside", "ct"), directory: true)
            || !TryLink(Path.Combine(_base, "outside", "ct", "l2"), Path.Combine("..", "secret.xml"), directory: false)
            || !TryLink(Path.Combine(root, "l1"), Path.Combine("sub", "l2"), directory: false))
        {
            return;
        }

        AssertRefused("l1");
    }

    [Fact]
    public void S4_LinksThatStayInsideTheRoot_AreStillAccepted()
    {
        var root = Path.Combine(_base, "root");
        File.WriteAllText(Path.Combine(root, "real.xml"), StyleXml);
        Directory.CreateDirectory(Path.Combine(root, "d"));
        if (!TryLink(Path.Combine(root, "d", "link.xml"), Path.Combine("..", "real.xml"), directory: false)
            || !TryLink(Path.Combine(root, "dirlink"), Path.Combine(root, "d"), directory: true))
        {
            return;
        }

        var writer = new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = root });
        foreach (var path in new[] { "d/link.xml", "dirlink/link.xml" })
        {
            using var stream = new MemoryStream();
            writer.CreateExcelDoc(stream, Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = path }));
            Assert.True(stream.Length > 0);
        }
    }

    // ---- round 2: B1 empty cells, B2 sheet names, B3 style range, B4 no partial file

    [Theory]
    [InlineData("Number", "")]
    [InlineData("Number", null)]
    [InlineData("Boolean", "")]
    [InlineData("Date", "")]
    [InlineData("Date", null)]
    [InlineData("Date", "not a date")]
    public void B1_EmptyNumberBooleanOrDate_IsAnEmptyCell_WithItsStyle(string type, string? value)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(new JObject { ["DataType"] = type, ["CellValue"] = value == null ? JValue.CreateNull() : value })))));

        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
        Assert.Equal(0u, cell.StyleIndex!.Value);
    }

    [Theory]
    [InlineData("'Report")]
    [InlineData("Report'")]
    [InlineData("History")]
    [InlineData("hIsToRy")]
    public void B2_ApostropheAndReservedNames_AreRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 1, ["Name"] = name }))));
    }

    [Fact]
    public void B2_NamesDifferingOnlyByCase_AreRefused_AndAnInnerApostropheIsFine()
    {
        var clash = new JArray(new JObject { ["SheetId"] = 1, ["Name"] = "Report" }, new JObject { ["SheetId"] = 2, ["Name"] = "REPORT" });
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), clash)));

        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 1, ["Name"] = "O'Brien" })), stream);
        Assert.Equal("O'Brien", document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(99)]
    public void B3_ExplicitStyleIndexOutOfRange_IsRefused_InTheGeneratedStylesheet(uint index)
    {
        var cell = Cell(null, "x");
        cell["StyleIndex"] = index;

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(cell)))));
    }

    [Fact]
    public void B3_ExplicitStyleIndex_IsCheckedAgainstALoadedStylesheet()
    {
        var writer = WriterWithStylesheet(2, out var stylesheets);
        var inRange = Cell(null, "x");
        inRange["StyleIndex"] = 1;
        var outOfRange = Cell(null, "x");
        outOfRange["StyleIndex"] = 2;

        Assert.Equal(1u, Assert.Single(Cells(Workbook(new JArray(Row(inRange)), stylesheets: stylesheets), writer)).StyleIndex!.Value);
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(outOfRange)), stylesheets: stylesheets), writer));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("stylesheet")]
    [InlineData("sheets")]
    [InlineData("styleindex")]
    public void B4_FileOverload_LeavesNoFileBehind_WhenTheDefinitionFails(string failure)
    {
        var path = Path.Combine(_base, "out-" + failure + ".xlsx");
        var json = failure switch
        {
            "error" => Workbook(new JArray(Row(Cell("Error", "oops")))),
            "stylesheet" => Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = "../outside/none.xml" }),
            "sheets" => Workbook(new JArray(), new JArray()),
            _ => Workbook(new JArray(Row(new JObject { ["CellValue"] = "x", ["StyleIndex"] = 99 }))),
        };

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") }).CreateExcelDoc(path, json));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void B4_FileOverload_StillWritesTheFile_OnSuccess()
    {
        var path = Path.Combine(_base, "ok.xlsx");

        new JsonSpreedSheet().CreateExcelDoc(path, Workbook(new JArray(Row(Cell(null, "x")))));

        using var document = SpreadsheetDocument.Open(path, false);
        Assert.Single(document.WorkbookPart!.Workbook!.Descendants<Sheet>());
    }


}
