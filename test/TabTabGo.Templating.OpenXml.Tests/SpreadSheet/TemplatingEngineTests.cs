using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using Xunit;
using Engine = TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

public sealed class TemplatingEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttg-xlsx-" + Guid.NewGuid().ToString("N"));

    public TemplatingEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Builds a one-cell template whose A1 is a shared string with the given text.</summary>
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

    private static string? FirstCellText(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var cell = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().First();
        return cell.InlineString?.Text?.Text ?? cell.CellValue?.Text;
    }

    [Fact]
    public void Contract_Members()
    {
        var engine = new Engine();
        Assert.Equal("OpenXml", engine.EngineName);
        Assert.False(engine.IsCached("any"));
        engine.ParseTemplate("any");
        engine.RegisterFilter(typeof(string));
        Assert.Throws<NotSupportedException>(() => engine.RenderString("x", new { }));
    }

    [Fact]
    public void Render_TemplateWithScalarMacro_ReplacesValue()
    {
        var template = CreateTemplate("{%title%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, new { title = "Hello" }));

        Assert.Equal("Hello", FirstCellText(result));
    }

    [Fact]
    public void Render_AcceptsJObjectSource()
    {
        var template = CreateTemplate("{%title%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, JObject.Parse("""{"title":"FromJson"}""")));

        Assert.Equal("FromJson", FirstCellText(result));
    }

    [Fact]
    public void Render_NumericValue_WrittenAsNumber()
    {
        var template = CreateTemplate("{%amount%}");

        using var result = Assert.IsType<MemoryStream>(new Engine().Render(template, new { amount = 12.5 }));

        using var document = SpreadsheetDocument.Open(result, false);
        var cell = document.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<Cell>().First();
        Assert.Equal("12.5", cell.CellValue?.Text);
    }

    [Fact]
    public void Render_NoTemplateAndNoWorkbook_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => new Engine().Render("", new { a = 1 }));
    }

    [Fact]
    public void Render_MissingTemplateFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new Engine().Render(Path.Combine(_dir, "missing.xlsx"), new { a = 1 }));
    }

    [Fact]
    public void Render_ExistingTemplates_ProduceOpenableWorkbooks()
    {
        var data = JObject.Parse(File.ReadAllText("SpreadSheet/json/test01.json"));

        using var result = Assert.IsType<MemoryStream>(new Engine().Render("SpreadSheet/Template/test01.xlsx", data));

        using var document = SpreadsheetDocument.Open(result, false);
        Assert.NotEmpty(document.WorkbookPart!.WorksheetParts);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a\u0001b", "ab")]
    [InlineData("tab\there", "tab\there")]
    [InlineData("", "")]
    public void ToValidXmlCharactersString_RemovesIllegalCharacters(string input, string expected)
    {
        Assert.Equal(expected, Engine.ToValidXmlCharactersString(input));
    }

    [Fact]
    public void IndexOfFirstInvalidXMLChar_FindsPosition()
    {
        Assert.Equal(-1, Engine.IndexOfFirstInvalidXMLChar("clean"));
        Assert.Equal(2, Engine.IndexOfFirstInvalidXMLChar("ab\u0002c"));
        Assert.Equal(-1, Engine.IndexOfFirstInvalidXMLChar(null!));
        Assert.Equal(-1, Engine.IndexOfFirstInvalidXMLChar("ab\u0002c", 3));
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('\t', true)]
    [InlineData('\n', true)]
    [InlineData('\u0001', false)]
    [InlineData('\u0000', false)]
    [InlineData('￾', false)]
    [InlineData('', true)]
    public void IsLegalXmlChar_Cases(char c, bool expected) => Assert.Equal(expected, Engine.IsLegalXmlChar(c));
}
