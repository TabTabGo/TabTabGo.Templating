using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet.Internal;
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

        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["Name"] = name })), stream);

        Assert.Equal(name, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
    }

    [Fact]
    public void J4_GeneratedSheetId_NeverCollidesWithAnExplicitOne()
    {
        var sheets = new JArray(new JObject(), new JObject { ["SheetId"] = 1 }, new JObject { ["SheetId"] = 2 }, new JObject());
        using var stream = new MemoryStream();

        using var document = Write(Workbook(new JArray(), sheets), stream);

        var ids = document.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(s => s.SheetId!.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(new[] { 1u, 2u }, ids.Skip(1).Take(2));
    }

    [Fact]
    public void J4_DuplicateExplicitSheetId_IsRejected()
    {
        var sheets = new JArray(new JObject { ["SheetId"] = 3 }, new JObject { ["SheetId"] = 3 });

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
    public void J5_DateCell_InTheGeneratedStylesheet_UsesIndexFive()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", "2024-03-05"))))));

        Assert.Equal(5u, cell.StyleIndex!.Value);
    }

    [Fact]
    public void J6_StyleLookup_SurvivesDuplicateKeys_QuotesAndTrailingBackslash()
    {
        var styleMap = new JArray(
            new JObject { ["Key"] = "Dup", ["SIndex"] = 3 },
            new JObject { ["Key"] = "Dup", ["SIndex"] = 4 },
            new JObject { ["Key"] = "a'b", ["SIndex"] = 5 },
            new JObject { ["Key"] = "end\\", ["SIndex"] = 4 });

        var cells = Cells(Workbook(new JArray(Row(Cell(null, "x", "Dup"), Cell(null, "x", "a'b"), Cell(null, "x", "end\\"), Cell(null, "x", "missing"))), styleMap: styleMap));

        Assert.Equal(new[] { 3u, 5u, 4u, 0u }, cells.Select(c => c.StyleIndex!.Value));
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
    [InlineData("Number", "   ")]
    [InlineData("Boolean", " \t ")]
    [InlineData("Date", "  ")]
    public void B1_EmptyNumberBooleanOrDate_IsAnEmptyCell_WithItsStyle(string type, string? value)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(new JObject { ["DataType"] = type, ["CellValue"] = value == null ? JValue.CreateNull() : value, ["StyleIndex"] = 2 })))));

        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
        Assert.Equal(2u, cell.StyleIndex!.Value);
    }

    [Fact]
    public void B1_EmptyCells_PassTheOpenXmlValidator()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(Row(Cell("Number", ""), Cell("Date", null), Cell("Boolean", ""), Cell("Number", 1)))), stream);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).ToList();

        Assert.Empty(errors);
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
        var clash = new JArray(new JObject { ["Name"] = "Report" }, new JObject { ["Name"] = "REPORT" });
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), clash)));

        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["Name"] = "O'Brien" })), stream);
        Assert.Equal("O'Brien", document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
    }

    [Fact]
    public void B2_GeneratedName_SkipsAnExplicitOne()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject(), new JObject { ["Name"] = "Sheet1" })), stream);

        var names = document.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(s => s.Name!.Value).ToArray();
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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

    [Fact]
    public void M25_ExplicitSheet2_AndAGeneratedSheet2_NeverCollide()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["Name"] = "Sheet2" }, new JObject())), stream);

        var names = document.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(s => s.Name!.Value).ToArray();
        Assert.Equal("Sheet2", names[0]);
        Assert.NotEqual("Sheet2", names[1], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- round 3: Q-1 number/boolean text, Q-2 date serials, N-3 sheet names and ids, N-4 sheet count

    private static IEnumerable<DocumentFormat.OpenXml.Validation.ValidationErrorInfo> Validate(JObject workbook)
    {
        using var stream = new MemoryStream();
        using var document = Write(workbook, stream);
        return new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).ToList();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    public void Q1_NumberCell_WithNonNumericText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Number", text))))));
    }

    [Theory]
    [InlineData("12.50", "12.5")]
    [InlineData(" 7 ", "7")]
    [InlineData("1e3", "1000")]
    [InlineData("-0.1", "-0.1")]
    public void Q1_NumberCell_WritesTheCanonicalInvariantForm(string text, string expected)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Number", text))))));

        Assert.Equal(expected, cell.CellValue!.Text);
        Assert.Equal(CellValues.Number, cell.DataType!.Value);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("2")]
    [InlineData("yes")]
    public void Q1_BooleanCell_WithOtherText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Boolean", text))))));
    }

    [Fact]
    public void Q1_BooleanCell_AcceptsTrueFalseOneZero()
    {
        var cells = Cells(Workbook(new JArray(Row(Cell("Boolean", "true"), Cell("Boolean", "FALSE"), Cell("Boolean", "1"), Cell("Boolean", "0")))));

        Assert.Equal(new[] { "1", "0", "1", "0" }, cells.Select(c => c.CellValue!.Text));
    }

    [Fact]
    public void Q1_WorkbookWithWhitespaceAndCanonicalNumbers_PassesTheOpenXmlValidator()
    {
        var errors = Validate(Workbook(new JArray(Row(Cell("Number", "  "), Cell("Number", "12.50"), Cell("Boolean", " "), Cell("Boolean", "true"), Cell("Date", "  ")))));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(45000, "45000")]
    [InlineData(45000.5, "45000.5")]
    [InlineData(1, "1")]
    [InlineData(2958465, "2958465")]
    public void Q2_NumericDate_IsWrittenAsTheSerial(double value, string expected)
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", value))))));

        Assert.Equal(expected, cell.CellValue!.Text);
        Assert.Equal(CellValues.Number, cell.DataType!.Value);
    }

    [Fact]
    public void Q2_NumericTextDate_IsAlsoTheSerial()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", "45000"))))));

        Assert.Equal("45000", cell.CellValue!.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2958466)]
    [InlineData(1e12)]
    public void Q2_NumericDate_OutOfRange_Throws(double value)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", value))))));
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2024-13-45")]
    public void Q2_UnparseableDate_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", text))))));
    }

    [Theory]
    [InlineData("a\u0001b")]
    [InlineData("a\u0000b")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a{HI}b")]
    [InlineData("a{LO}")]
    [InlineData("{HI}")]
    public void N3_SheetNameWithControlCharactersOrLoneSurrogates_Throws_BeforeAnythingIsWritten(string template)
    {
        var name = template.Replace("{HI}", "\ud800").Replace("{LO}", "\udc00"); // lone surrogates do not survive theory data, so they are built here
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(stream, Workbook(new JArray(), new JArray(new JObject { ["Name"] = name }))));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void N3_SheetNameWithAValidSurrogatePair_IsAccepted()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["Name"] = "R\ud83d\ude00" })), stream);

        Assert.Equal("R\ud83d\ude00", document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("\"abc\"")]
    [InlineData("true")]
    [InlineData("65535")]
    [InlineData("4294967295")]
    [InlineData("4294967296")]
    [InlineData("9223372036854775808")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("1e300")]
    [InlineData("\"1e300\"")]
    [InlineData("\"\"")]
    [InlineData("[]")]
    public void N3_InvalidSheetId_Throws(string idJson)
    {
        var sheets = new JArray(new JObject { ["SheetId"] = JToken.Parse(idJson), ["Name"] = "A" });

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), sheets)));
    }

    [Fact]
    public void N3_IntegralFloatAndNumericTextIds_AreAccepted()
    {
        using var stream = new MemoryStream();
        var sheets = new JArray(new JObject { ["SheetId"] = 3.0, ["Name"] = "A" }, new JObject { ["SheetId"] = "4", ["Name"] = "B" });
        using var document = Write(Workbook(new JArray(), sheets), stream);

        Assert.Equal(new[] { 3u, 4u }, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Select(s => s.SheetId!.Value));
    }

    [Fact]
    public void N4_MoreThan255Sheets_Throw_And255Work()
    {
        JArray Sheets(int count) => new(Enumerable.Range(0, count).Select(i => new JObject()));
        using var stream = new MemoryStream();

        using (var document = Write(Workbook(new JArray(), Sheets(255)), stream))
        {
            Assert.Equal(255, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Count());
        }

        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(), Sheets(256))));
    }

    [Fact]
    public void N4_Generation_IsLinear_AndKeepsTheNamingRules()
    {
        // 127 unnamed sheets interleaved with 128 sheets explicitly named Sheet1.. : the generated names skip every explicit one
        var sheets = new JArray();
        for (var i = 0; i < 255; i++)
        {
            sheets.Add(i % 2 == 0 ? new JObject() : new JObject { ["Name"] = "Sheet" + (i + 1), ["SheetId"] = 1000 + i });
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), sheets), stream);
        watch.Stop();

        var all = document.WorkbookPart!.Workbook!.Descendants<Sheet>().ToArray();
        Assert.Equal(255, all.Select(s => s.Name!.Value!.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(255, all.Select(s => s.SheetId!.Value).Distinct().Count());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void B2_SheetId_65534_IsTheLargestAccepted_AndSchemaValid()
    {
        using var stream = new MemoryStream();
        using var document = Write(Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = 65534, ["Name"] = "A" })), stream);

        Assert.Equal(65534u, document.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().SheetId!.Value);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document));
    }

    private const string Big = "1000000000000000000000000000000";

    [Theory]
    [InlineData("SheetId", Big)]
    [InlineData("SheetId", "-" + Big)]
    [InlineData("StyleIndex", Big)]
    [InlineData("StyleIndex", "-" + Big)]
    [InlineData("StyleIndex", "-1")]
    [InlineData("StyleIndex", "1.5")]
    [InlineData("StyleIndex", "\"abc\"")]
    [InlineData("StyleIndex", "true")]
    [InlineData("Date", Big)]
    [InlineData("Date", "-" + Big)]
    [InlineData("Number", "\"" + Big + "x\"")]
    [InlineData("Number", "\"NaN\"")]
    [InlineData("Boolean", Big)]
    [InlineData("Boolean", "-" + Big)]
    [InlineData("Boolean", "7")]
    [InlineData("Column", "\"abc\"")]
    [InlineData("Column", Big)]
    [InlineData("Column", "-" + Big)]
    [InlineData("Collapsed", "\"abc\"")]
    [InlineData("Collapsed", Big)]
    public void B1_EveryNumericConversion_ThrowsArgumentException_AndNothingElse(string target, string valueJson)
    {
        var value = JToken.Parse(valueJson);
        var workbook = target switch
        {
            "SheetId" => Workbook(new JArray(), new JArray(new JObject { ["SheetId"] = value, ["Name"] = "A" })),
            "StyleIndex" => Workbook(new JArray(Row(new JObject { ["CellValue"] = "x", ["StyleIndex"] = value }))),
            "Date" => Workbook(new JArray(Row(new JObject { ["DataType"] = "Date", ["CellValue"] = value }))),
            "Number" => Workbook(new JArray(Row(new JObject { ["DataType"] = "Number", ["CellValue"] = value }))),
            "Boolean" => Workbook(new JArray(Row(new JObject { ["DataType"] = "Boolean", ["CellValue"] = value }))),
            "Column" => Workbook(new JArray()),
            _ => Workbook(new JArray(new JObject { ["Collapsed"] = value })),
        };
        if (target == "Column")
        {
            ((JObject)workbook["Workbook"]!["Worksheet"]!)["Columns"] = new JArray(new JObject { ["Min"] = value, ["Max"] = 2, ["Width"] = value });
        }

        using var stream = new MemoryStream();

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(stream, workbook));
    }

    // ---- round 6: B4 path-free stylesheet errors, B5 special files, B1 date range, B2 StyleMap range

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
        Assert.DoesNotContain(Path.GetFileName(_base), text);
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
    public void B4_SymbolicLinkLoop_IsPathFree()
    {
        var root = Path.Combine(_base, "root");
        if (!TryLink(Path.Combine(root, "a.xml"), "b.xml", directory: false) || !TryLink(Path.Combine(root, "b.xml"), "a.xml", directory: false))
        {
            return;
        }

        var ex = StylesheetFailure("a.xml");

        AssertPathFree(ex);
    }

    [Fact]
    public void B5_AnEmptyRegularStylesheet_StillFailsAsMalformedXml_NotAsNotFound()
    {
        File.WriteAllText(Path.Combine(_base, "root", "empty.xml"), string.Empty);

        Assert.IsType<ArgumentException>(StylesheetFailure("empty.xml"));
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("1800-01-01")]
    [InlineData("1899-12-30")]
    [InlineData("1900-01-01")]
    [InlineData("1900-02-28")]
    public void B1_DatesBefore19000301_AreRefused(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", text))))));
    }

    [Fact]
    public void B1_DateTimeValues_BeforeTheSafeRange_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", DateTime.MinValue))))));
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", DateTimeOffset.MinValue))))));
    }

    [Theory]
    [InlineData("1900-03-01", "61")]
    [InlineData("1900-03-02", "62")]
    [InlineData("9999-12-31", "2958465")]
    public void B1_DatesFrom19000301_AreExcelSerials(string text, string expected)
    {
        Assert.Equal(expected, Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", text)))))).CellValue!.Text);
    }

    [Fact]
    public void B1_TheLatestInstantOf9999_IsStillADateOnTheLastDay()
    {
        var cell = Assert.Single(Cells(Workbook(new JArray(Row(Cell("Date", DateTime.MaxValue))))));

        Assert.StartsWith("2958465.", cell.CellValue!.Text);
    }

    [Fact]
    public void B2_StyleMapIndexes_OutsideTheStylesheet_AreRefused()
    {
        var styleMap = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 999, ["SAlternativeIndex"] = 888 });

        // the first body row uses SIndex, the second uses SAlternativeIndex
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell(null, "h", "X"))), styleMap: styleMap)));
        var alternativeOnly = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 1, ["SAlternativeIndex"] = 888 });
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell(null, "h", "X")), Row(Cell(null, "a", "X")), Row(Cell(null, "b", "X"))), styleMap: alternativeOnly)));
    }

    [Fact]
    public void B2_StyleMapIndexes_AreCheckedAgainstALoadedStylesheet()
    {
        var writer = WriterWithStylesheet(2, out var stylesheets);
        var inside = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 1 });
        var outside = new JArray(new JObject { ["Key"] = "X", ["SIndex"] = 2 });

        Assert.Equal(1u, Assert.Single(Cells(Workbook(new JArray(Row(Cell(null, "h", "X"))), styleMap: inside, stylesheets: stylesheets), writer)).StyleIndex!.Value);
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell(null, "h", "X"))), styleMap: outside, stylesheets: stylesheets), writer));
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

    [Fact]
    public void G1_ARegularStylesheet_StillReadsCorrectlyAndFast()
    {
        File.WriteAllText(Path.Combine(_base, "root", "ok.xml"), StyleXml);
        var writer = new JsonSpreedSheet(new TemplatingOptions { StylesheetRoot = Path.Combine(_base, "root") });
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 50; i++)
        {
            using var stream = new MemoryStream();
            writer.CreateExcelDoc(stream, Workbook(new JArray(), stylesheets: new JObject { ["TemplatePath"] = "ok.xml" }));
            Assert.True(stream.Length > 0);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public void G1_AFileOverTheSizeCap_IsRefusedPathFree()
    {
        var path = Path.Combine(_base, "root", "big.xml");
        using (var big = new FileStream(path, FileMode.Create))
        {
            big.SetLength(GuardedRead.MaxBytes + 1);
        }

        var ex = StylesheetFailure("big.xml");

        Assert.IsType<FileNotFoundException>(ex);
        AssertPathFree(ex);
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

    [Fact]
    public void KnownLimitation_AFifoIsIndistinguishableFromARegularFile_ByTheManagedFileApis()
    {
        // measured on macOS arm64: a pipe, a socket, /dev/null, /dev/tty and an empty regular file all report FileAttributes.Normal, length 0 and permission-only UnixFileMode,
        // so no managed pre-open check can refuse a pipe; the folders must not be writable by untrusted parties (see GuardedRead). The probe never opens the pipe.
        var dir = Path.Combine(_base, "root");
        var fifo = Path.Combine(dir, "probe.fifo");
        var regular = Path.Combine(dir, "probe.empty");
        File.WriteAllText(regular, string.Empty);
        if (!TryMakeFifo(fifo))
        {
            return;
        }

        var a = new FileInfo(fifo);
        var b = new FileInfo(regular);

        Assert.True(a.Exists && b.Exists);
        Assert.Equal(b.Attributes, a.Attributes);
        Assert.Equal(b.Length, a.Length);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("-5")]
    [InlineData("-0.1")]
    [InlineData("256")]
    [InlineData("255.5")]
    [InlineData("4294967296")]
    [InlineData("1000000000000000000000000000000")]
    [InlineData("\"abc\"")]
    public void W1_ColumnWidth_OutsideZeroTo255_OrNotANumber_Throws(string widthJson)
    {
        var workbook = Workbook(new JArray());
        ((JObject)workbook["Workbook"]!["Worksheet"]!)["Columns"] = new JArray(new JObject { ["Min"] = 1, ["Max"] = 1, ["Width"] = JToken.Parse(widthJson) });

        Assert.Throws<ArgumentException>(() => Cells(workbook));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("14.5")]
    [InlineData("255")]
    public void W1_ColumnWidth_InRange_IsWritten_AndValidatorClean(string widthJson)
    {
        var workbook = Workbook(new JArray(Row(Cell(null, "x"))));
        ((JObject)workbook["Workbook"]!["Worksheet"]!)["Columns"] = new JArray(new JObject { ["Min"] = 1, ["Max"] = 1, ["Width"] = JToken.Parse(widthJson) });
        using var stream = new MemoryStream();
        using var document = Write(workbook, stream);

        var column = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Column>().Single();

        Assert.Equal(double.Parse(widthJson, System.Globalization.CultureInfo.InvariantCulture), column.Width!.Value);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("0001-06-15")]
    [InlineData("0050-06-15")]
    [InlineData("0099-12-31")]
    [InlineData("0100-01-01")]
    [InlineData("1899-12-31")]
    [InlineData("1900-02-28")]
    public void M39_DatesBefore19000301_AreAnArgumentException_NeverAnOverflow(string text)
    {
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", text))))));
    }

    [Fact]
    public void M39_DateTimeValuesInTheYears1To99_AreAnArgumentException()
    {
        foreach (var year in new[] { 1, 50, 99, 100 })
        {
            var value = new DateTime(year, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", value))))));
            Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(Row(Cell("Date", new DateTimeOffset(value)))))));
        }
    }

    private const string HugeNumber = "1000000000000000000000000000000";

    private static JObject WorkbookWithColumn(JObject column)
    {
        var workbook = Workbook(new JArray());
        ((JObject)workbook["Workbook"]!["Worksheet"]!)["Columns"] = new JArray(column);
        return workbook;
    }

    [Theory]
    [InlineData("Min", "0")]
    [InlineData("Min", "-1")]
    [InlineData("Min", "1.5")]
    [InlineData("Min", "4294967296")]
    [InlineData("Min", HugeNumber)]
    [InlineData("Min", "\"abc\"")]
    [InlineData("Max", "0")]
    [InlineData("Max", "-1")]
    [InlineData("Max", "2.5")]
    [InlineData("Max", HugeNumber)]
    [InlineData("BestFit", "\"abc\"")]
    [InlineData("BestFit", "2")]
    [InlineData("BestFit", "-1")]
    [InlineData("BestFit", "0.5")]
    [InlineData("BestFit", "1e400")]
    [InlineData("BestFit", HugeNumber)]
    [InlineData("BestFit", "\"\"")]
    public void M43_M44_InvalidColumnValues_ThrowArgumentException_AndWriteNoFile(string member, string valueJson)
    {
        var column = new JObject { ["Min"] = 1, ["Max"] = 2, ["BestFit"] = true, ["Width"] = 10 };
        column[member] = JToken.Parse(valueJson);
        var path = Path.Combine(_base, "col-" + Guid.NewGuid().ToString("N") + ".xlsx");

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(path, WorkbookWithColumn(column)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void M44_MinAndMaxAreRequired_AndMinMayNotExceedMax()
    {
        Assert.Throws<ArgumentException>(() => Cells(WorkbookWithColumn(new JObject { ["Max"] = 2 })));
        Assert.Throws<ArgumentException>(() => Cells(WorkbookWithColumn(new JObject { ["Min"] = 1 })));
        Assert.Throws<ArgumentException>(() => Cells(WorkbookWithColumn(new JObject())));
        Assert.Throws<ArgumentException>(() => Cells(WorkbookWithColumn(new JObject { ["Min"] = 3, ["Max"] = 2 })));
    }

    [Fact]
    public void M43_BestFit_TrueFalseOneZero_AreAccepted_AndAMissingOneIsFalse()
    {
        var columns = new JArray(
            new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = true },
            new JObject { ["Min"] = 2, ["Max"] = 2, ["BestFit"] = "false" },
            new JObject { ["Min"] = 3, ["Max"] = 3, ["BestFit"] = 1 },
            new JObject { ["Min"] = 4, ["Max"] = 4, ["BestFit"] = "0" },
            new JObject { ["Min"] = 5, ["Max"] = 5 });
        var workbook = Workbook(new JArray());
        ((JObject)workbook["Workbook"]!["Worksheet"]!)["Columns"] = columns;
        using var stream = new MemoryStream();
        using var document = Write(workbook, stream);

        var written = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Column>().Select(c => c.BestFit!.Value).ToArray();

        Assert.Equal(new[] { true, false, true, false, false }, written);
    }

    // ---- round 9: a JSON null is "not given" for every optional member

    private static Column WrittenColumn(JObject column)
    {
        using var stream = new MemoryStream();
        using var document = Write(WorkbookWithColumn(column), stream);
        return document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Column>().Single();
    }

    [Fact]
    public void B3_Width_Null_Absent_Valid_Invalid()
    {
        var withNull = WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["Width"] = JValue.CreateNull() });
        var absent = WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1 });
        var valid = WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["Width"] = 12.5 });

        Assert.Null(withNull.Width);
        Assert.False(withNull.CustomWidth!.Value);
        Assert.Null(absent.Width);
        Assert.Equal(12.5, valid.Width!.Value);
        Assert.Throws<ArgumentException>(() => WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["Width"] = "abc" }));
    }

    [Fact]
    public void B3_BestFit_Null_Absent_Valid_Invalid()
    {
        Assert.False(WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = JValue.CreateNull() }).BestFit!.Value);
        Assert.False(WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1 }).BestFit!.Value);
        Assert.True(WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = true }).BestFit!.Value);
        Assert.Throws<ArgumentException>(() => WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = "abc" }));
    }

    [Fact]
    public void B3_Collapsed_Null_Absent_Valid_Invalid()
    {
        using var stream = new MemoryStream();
        var rows = new JArray(new JObject { ["Collapsed"] = JValue.CreateNull() }, new JObject(), new JObject { ["Collapsed"] = true });
        using var document = Write(Workbook(rows), stream);

        var collapsed = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Row>().Select(r => r.Collapsed?.Value ?? false).ToArray();

        Assert.Equal(new[] { false, false, true }, collapsed);
        Assert.Throws<ArgumentException>(() => Cells(Workbook(new JArray(new JObject { ["Collapsed"] = "abc" }))));
    }

    [Fact]
    public void B3_ColumnMinMax_Null_IsMissing_AndStillRequired()
    {
        Assert.Throws<ArgumentException>(() => WrittenColumn(new JObject { ["Min"] = JValue.CreateNull(), ["Max"] = 2 }));
        Assert.Throws<ArgumentException>(() => WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = JValue.CreateNull() }));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" 1 ", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    public void B1b_BestFit_AsText_TrueFalseOneZero_AreAccepted_CaseInsensitively(string text, bool expected)
    {
        Assert.Equal(expected, WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = text }).BestFit!.Value);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("1.0")]
    public void B1b_BestFit_AsOtherText_IsRefused(string text)
    {
        Assert.Throws<ArgumentException>(() => WrittenColumn(new JObject { ["Min"] = 1, ["Max"] = 1, ["BestFit"] = text }));
    }

    [Fact]
    public void Q1_ColumnMinAndMax_UpTo16384_AreAccepted_AndValidatorClean()
    {
        var workbook = WorkbookWithColumn(new JObject { ["Min"] = 1, ["Max"] = 16384 });
        using var stream = new MemoryStream();
        using var document = Write(workbook, stream);

        var column = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Column>().Single();

        Assert.Equal(16384u, column.Max!.Value);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData("Min", 16385)]
    [InlineData("Max", 16385)]
    [InlineData("Min", 4294967295)]
    [InlineData("Max", 4294967295)]
    public void Q1_ColumnMinOrMax_Above16384_IsRefused_AndWritesNoFile(string member, long value)
    {
        var column = new JObject { ["Min"] = 1, ["Max"] = 2 };
        column[member] = value;
        var path = Path.Combine(_base, "q1-" + Guid.NewGuid().ToString("N") + ".xlsx");

        Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(path, WorkbookWithColumn(column)));
        Assert.False(File.Exists(path));
    }

    // ---- T-1: the stylesheet loader shares the link resolution with the include loader and fails the same way, path-free

    [Fact]
    public void T1_LinkLoop_AndAChainOfMoreThan32Links_AreAPathFreeNotFound()
    {
        var root = Path.Combine(_base, "root");
        if (!TryLink(Path.Combine(root, "loop1.xml"), "loop2.xml", directory: false) || !TryLink(Path.Combine(root, "loop2.xml"), "loop1.xml", directory: false))
        {
            return;
        }

        File.WriteAllText(Path.Combine(root, "end.xml"), StyleXml);
        for (var i = 0; i < 40; i++)
        {
            if (!TryLink(Path.Combine(root, "hop" + i + ".xml"), i == 39 ? "end.xml" : "hop" + (i + 1) + ".xml", directory: false))
            {
                return;
            }
        }

        foreach (var name in new[] { "loop1.xml", "hop0.xml" })
        {
            var ex = StylesheetFailure(name);

            Assert.IsType<FileNotFoundException>(ex);
            AssertPathFree(ex);
        }
    }

    [Fact]
    public void T1_AnUnreadableDirectoryInTheChain_IsAPathFreeNotFound()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        var locked = Path.Combine(_base, "root", "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "inner.xml"), StyleXml);
        if (!TryLink(Path.Combine(_base, "root", "viadir.xml"), Path.Combine("locked", "inner.xml"), directory: false))
        {
            return;
        }

        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var ex = StylesheetFailure("viadir.xml");

            Assert.IsType<FileNotFoundException>(ex);
            AssertPathFree(ex);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
