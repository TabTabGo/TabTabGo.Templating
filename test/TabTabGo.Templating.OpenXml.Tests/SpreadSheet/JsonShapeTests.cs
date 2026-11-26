using System;
using System.IO;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

/// <summary>8.0.5: a workbook of the wrong JSON shape is an ArgumentException (never Json.NET's InvalidOperationException/InvalidCastException), without the value, and nothing is written.</summary>
public class JsonShapeTests
{
    private const string Sheet = "\"Sheets\": [ { \"Name\": \"S\", \"SheetId\": 1 } ]";

    [Theory]
    [InlineData("{ \"Workbook\": { \"Sheets\": { \"SECRET\": 1 }, \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { \"Sheets\": \"SECRET\", \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { \"Sheets\": [ \"SECRET\" ], \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { \"Sheets\": [ { \"Name\": { \"SECRET\": 1 }, \"SheetId\": 1 } ], \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": { \"SECRET\": 1 }, \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": [ \"SECRET\" ], \"SheetData\": { \"Rows\": [] } } } }")]
    [InlineData("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [ \"SECRET\" ] } } } }")]
    [InlineData("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [ { \"Cells\": { \"SECRET\": 1 } } ] } } } }")]
    [InlineData("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [ { \"Cells\": [ \"SECRET\" ] } ] } } } }")]
    public void WrongJsonShape_IsAnArgumentException_WithoutTheValue(string json)
    {
        var stream = new MemoryStream();

        var error = Assert.Throws<ArgumentException>(() => new JsonSpreedSheet().CreateExcelDoc(stream, JObject.Parse(json)));

        Assert.DoesNotContain("SECRET", error.Message);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void NullCells_AreNotGiven()
    {
        var stream = new MemoryStream();

        new JsonSpreedSheet().CreateExcelDoc(stream, JObject.Parse("{ \"Workbook\": { " + Sheet + ", \"Worksheet\": { \"Columns\": [], \"SheetData\": { \"Rows\": [ { \"Cells\": null } ] } } } }"));

        Assert.True(stream.Length > 0);
    }
}
