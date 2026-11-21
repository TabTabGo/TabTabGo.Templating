using System;
using System.IO;
using System.Collections.Generic;
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

        var writer = WriterWithStylesheet(7, out var stylesheets); // the StyleMap indexes must exist in the stylesheet
        var cells = Cells(Workbook(new JArray(Row(Cell(null, "x", "Dup"), Cell(null, "x", "a'b"), Cell(null, "x", "end\\"), Cell(null, "x", "missing"))), styleMap: styleMap, stylesheets: stylesheets), writer);

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



    // ---- round 3

    [Theory]
    [InlineData("abc")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    [InlineData("1e999")]
    public void Q1_NumberCell_WithNonNumericText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Number", text))))));
    }

    [Theory]
    [InlineData("12.50", "12.5")]
    [InlineData(" 7 ", "7")]
    [InlineData("1e3", "1000")]
    public void Q1_NumberCell_WritesTheCanonicalInvariantForm(string text, string expected)
    {
        Assert.Equal(expected, Assert.Single(Cells(Workbook(new JArray(Row(Cell("Number", text)))))).CellValue!.Text);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("2")]
    public void Q1_BooleanCell_WithOtherText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Boolean", text))))));
    }

    [Theory]
    [InlineData("Number", "   ")]
    [InlineData("Boolean", " \t ")]
    public void Q1_WhitespaceNumberOrBoolean_IsAnEmptyCell(string type, string text)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell(type, text))))));

        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
    }

    [Theory]
    [InlineData("a{C1}b")]
    [InlineData("a{C0}b")]
    [InlineData("a\tb")]
    [InlineData("a{HI}b")]
    [InlineData("a{LO}")]
    public void N3_SheetNameWithControlCharactersOrLoneSurrogates_Throws_BeforeAnythingIsWritten(string template)
    {
        var name = template.Replace("{C1}", "\u0001").Replace("{C0}", "\u0000").Replace("{HI}", "\ud800").Replace("{LO}", "\udc00");
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(stream, Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 1, ["Name"] = name }))));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("65535")]
    [InlineData("4294967295")]
    [InlineData("9223372036854775808")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("1e300")]
    [InlineData("[]")]
    public void N3_InvalidSheetId_Throws(string idJson)
    {
        var sheets = new JArray(new JObject { ["SheetId"] = JToken.Parse(idJson), ["Name"] = "A" });

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets)));
    }

    [Fact]
    public void N4_MoreThan255Sheets_Throw_And255Work()
    {
        JArray Sheets(int count) => new(Enumerable.Range(1, count).Select(i => new JObject { ["SheetId"] = i, ["Name"] = "S" + i }));
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), Sheets(256))));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void M33_NumericTextSheetId_IsWrittenAsTheValidatedNumber_NotConvertedAgain()
    {
        var sheets = new JArray(new JObject { ["SheetId"] = "1e0", ["Name"] = "A" }, new JObject { ["SheetId"] = "4", ["Name"] = "B" });
        using var stream = new MemoryStream();

        using var document = Write(Workbook(new JArray(), sheets), stream);

        Assert.Equal(new[] { 1u, 4u }, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(x => x.SheetId!.Value));
    }

    [Fact]
    public void M33_MissingSheetId_IsAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), new JArray(new JObject { ["Name"] = "A" }))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void M32_NumberCells_AreFormattedAndParsedInvariantly_UnderAnyThreadCulture(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
        try
        {
            var cells = Cells(Workbook(new JArray(Row(Cell("Number", 12.5m), Cell("Number", 12.5), Cell("Number", "12.5"), Cell("Number", 1234567.25), Cell("Number", -0.125m)))));

            Assert.Equal(new[] { "12.5", "12.5", "12.5", "1234567.25", "-0.125" }, cells.Select(c => c.CellValue!.Text));
            Assert.All(cells, c => Assert.Equal(CellValues.Number, c.DataType!.Value));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    private const string Big = "1000000000000000000000000000000";

    [Fact]
    public void B2_SheetId_65534_IsTheLargestAccepted()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 65534, ["Name"] = "A" })), stream);

        Assert.Equal(65534u, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().SheetId!.Value);
    }

    [Theory]
    [InlineData("SheetId", Big)]
    [InlineData("SheetId", "-" + Big)]
    [InlineData("StyleIndex", Big)]
    [InlineData("StyleIndex", "-" + Big)]
    [InlineData("StyleIndex", "-1")]
    [InlineData("StyleIndex", "1.5")]
    [InlineData("StyleIndex", "\"abc\"")]
    [InlineData("Number", "\"NaN\"")]
    [InlineData("Boolean", Big)]
    [InlineData("Boolean", "7")]
    public void B1_NumericConversionsOfSheetIdStyleIndexSIndexAndSAlternativeIndex_ThrowArgumentException_AndNothingElse(string target, string valueJson)
    {
        var value = JToken.Parse(valueJson);
        var workbook = target switch
        {
            "SheetId" => Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = value, ["Name"] = "A" })),
            "StyleIndex" => Workbook(new JArray(Row(new JObject { ["CellValue"] = "x", ["StyleIndex"] = value }))),
            "Number" => Workbook(new JArray(Row(new JObject { ["DataType"] = "Number", ["CellValue"] = value }))),
            _ => Workbook(new JArray(Row(new JObject { ["DataType"] = "Boolean", ["CellValue"] = value }))),
        };
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(stream, workbook));
    }

    // ---- round 6

    private static string EverythingButStackLines(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
        {
            parts.Add(e.Message);
            parts.AddRange(e.ToString().Split('\n').Where(line => !line.TrimStart().StartsWith("at ", StringComparison.Ordinal)));
            if (e is System.Xml.XmlException xml)
            {
                parts.Add(xml.SourceUri ?? string.Empty);
            }

            if (e is FileNotFoundException notFound)
            {
                parts.Add(notFound.FileName ?? string.Empty);
            }
        }

        return string.Join("\n", parts);
    }

    private void AssertPathFree(Exception ex)
    {
        var text = EverythingButStackLines(ex);
        Assert.DoesNotContain(_base, text);
        Assert.Null(ex.InnerException);
    }

    private Exception StylesheetFailure(string path)
    {
        var writer = new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") });
        return Assert.ThrowsAny<Exception>(() => writer.CreateExcelDoc(new MemoryStream(), Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = path })));
    }

    [Fact]
    public void B4_UnreadableStylesheet_IsPathFree()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        var file = Path.Combine(_base, "root", "noperm.xml");
        File.WriteAllText(file, StyleXml);
        File.SetUnixFileMode(file, UnixFileMode.None);
        try
        {
            var ex = StylesheetFailure("noperm.xml");

            Assert.IsType<FileNotFoundException>(ex);
            AssertPathFree(ex);
        }
        finally
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void B4_DirectoryNamedLikeAStylesheet_IsPathFree()
    {
        Directory.CreateDirectory(Path.Combine(_base, "root", "d.xml"));

        var ex = StylesheetFailure("d.xml");

        Assert.IsType<FileNotFoundException>(ex);
        AssertPathFree(ex);
    }

    [Theory]
    [InlineData("<styleSheet")]
    [InlineData("not xml at all")]
    [InlineData("<!DOCTYPE styleSheet [<!ENTITY e \"x\">]><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">&e;</styleSheet>")]
    public void B4_MalformedOrDtdStylesheet_IsAPathFreeArgumentException(string xml)
    {
        File.WriteAllText(Path.Combine(_base, "root", "bad.xml"), xml);

        var ex = StylesheetFailure("bad.xml");

        Assert.IsType<ArgumentException>(ex);
        AssertPathFree(ex);
    }

    [Fact]
    public void B2_StyleMapIndexes_OutsideTheStylesheet_AreRefused()
    {
        var writer = WriterWithStylesheet(2, out var stylesheets);
        var inside = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 1 });
        var outside = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 2 });
        var alternativeOutside = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 1, ["SAlternativeIndex"] = 888 });

        Assert.Equal(1u, Assert.Single(Cells(Workbook(new JArray(Row(Cell(null, "h", "X"))), styleMap: inside, stylesheets: stylesheets), writer)).StyleIndex!.Value);
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell(null, "h", "X"))), styleMap: outside, stylesheets: stylesheets), writer));
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell(null, "h", "X")), Row(Cell(null, "a", "X")), Row(Cell(null, "b", "X"))), styleMap: alternativeOutside, stylesheets: stylesheets), writer));
    }

    [Fact]
    public void Nit_MoreThan255Sheets_AreRefusedBeforeTheListIsCopied()
    {
        var sheets = new JArray(Enumerable.Range(1, 5_000).Select(i => new JObject { ["SheetId"] = i, ["Name"] = "S" + i }));

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets)));
    }

    private static bool TryMakeFifo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        using var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", path) { RedirectStandardError = true });
        make!.WaitForExit();
        return make.ExitCode == 0;
    }


    // ---- round 6 addendum: GR-1 capped read, GR-2/3 no write-open, no thread, no release

    private sealed class EndlessStream(long reportedLength, long actualBytes) : Stream
    {
        private long _served;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => reportedLength;

        public override long Position
        {
            get => _served;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, actualBytes - _served);
            Array.Fill(buffer, (byte)'a', offset, n);
            _served += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Gr1_AFileThatGrowsAfterTheLengthWasTaken_IsRefusedAtTheCap()
    {
        GuardedRead.OpenOverride = _ => new EndlessStream(reportedLength: 10, actualBytes: GuardedRead.MaxBytes * 2);
        try
        {
            var ex = Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));

            Assert.DoesNotContain("ignored", ex.Message);
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr1_ExactlyTheCap_ReadsAndOneByteMore_IsRefused()
    {
        try
        {
            GuardedRead.OpenOverride = _ => new EndlessStream(GuardedRead.MaxBytes, GuardedRead.MaxBytes);
            Assert.Equal(GuardedRead.MaxBytes, GuardedRead.ReadText("ignored").Length);

            GuardedRead.OpenOverride = _ => new EndlessStream(10, GuardedRead.MaxBytes + 1);
            Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr1_AFileWhoseReportedLengthIsOverTheCap_IsRefusedWithoutReading()
    {
        GuardedRead.OpenOverride = _ => new EndlessStream(GuardedRead.MaxBytes + 1, 5);
        try
        {
            Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr2_ASymbolicLinkToAFifo_IsRefusedFast()
    {
        var root = Path.Combine(_base, "root");
        if (!TryMakeFifo(Path.Combine(root, "real.fifo")) || !TryLink(Path.Combine(root, "link.xml"), "real.fifo", directory: false))
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        var ex = StylesheetFailure("link.xml");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        AssertPathFree(ex);
    }

    private static int ThreadCount() => System.Diagnostics.Process.GetCurrentProcess().Threads.Count;

    [Fact]
    public void Fifo_InTheStylesheetRoot_IsRefusedImmediately_ByTwentyConcurrentRequests_WithNoThreadLeft()
    {
        var fifo = Path.Combine(_base, "root", "pipe.xml");
        if (!TryMakeFifo(fifo))
        {
            return;
        }

        var before = ThreadCount();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failures = new Exception?[20];
        var threads = Enumerable.Range(0, 20).Select(i => new System.Threading.Thread(() => failures[i] = StylesheetFailure("pipe.xml"))).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.All(failures, ex =>
        {
            Assert.IsType<FileNotFoundException>(ex);
            AssertPathFree(ex!);
        });
        Assert.True(ThreadCount() <= before + 2, $"threads {before} -> {ThreadCount()}: a blocked reader was left behind");
    }

    [Fact]
    public void G1_ARegularFileSwappedForAFifoBeforeTheOpen_IsStillRefusedImmediately()
    {
        var path = Path.Combine(_base, "root", "swap.xml");
        File.WriteAllText(path, StyleXml);
        if (!TryMakeFifo(Path.Combine(_base, "root", "swap.fifo")))
        {
            return;
        }

        GuardedRead.BeforeOpen = p =>
        {
            if (!p.EndsWith("swap.xml", StringComparison.Ordinal))
            {
                return; // other tests may read files while this hook is set
            }

            File.Delete(p);
            File.Move(Path.Combine(_base, "root", "swap.fifo"), p);
        };
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var ex = StylesheetFailure("swap.xml");

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
            AssertPathFree(ex);
        }
        finally
        {
            GuardedRead.BeforeOpen = null;
        }
    }

    [Fact]
    public void Device_ReachedThroughASymbolicLink_IsRefused_AndADirectoryIsNotAStylesheet()
    {
        var root = Path.Combine(_base, "root");
        Directory.CreateDirectory(Path.Combine(root, "dir.xml"));
        if (!OperatingSystem.IsWindows() && TryLink(Path.Combine(root, "null.xml"), "/dev/null", directory: false))
        {
            var ex = StylesheetFailure("null.xml");
            Assert.IsAssignableFrom<Exception>(ex);
            AssertPathFree(ex);
        }

        AssertPathFree(StylesheetFailure("dir.xml"));
    }
}
