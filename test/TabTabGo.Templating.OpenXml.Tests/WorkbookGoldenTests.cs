using Newtonsoft.Json.Linq;
using TabTabGo.Templating.TestKit;
using Engine = TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine;

namespace TabTabGo.Templating.OpenXml.Tests;

/// <summary>
/// One xlsx golden per scenario: the workbook parts are compared canonically (see <see cref="XlsxGolden"/>) and the package must
/// pass the Open XML validator. Fixtures are built in code by <see cref="TemplateBuilder"/>.
/// </summary>
public sealed class WorkbookGoldenTests : IDisposable
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Golden", "Xlsx");

    // v10: every workbook passes the Open XML validator
    private const bool Validate = true;

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private void AssertGolden(string scenario, TemplateBuilder template, JObject data)
    {
        var path = template.Save(_dir);
        using var result = (MemoryStream)new Engine().Render(path, data);
        XlsxGolden.Assert(result, Path.Combine(Root, scenario), Validate);
    }

    [Fact]
    public void SimpleCells_Golden()
    {
        var template = TemplateBuilder.Create()
            .Sheet("Data")
            .Text("A1", "{%$.title%}")
            .Text("A2", "{%$.amount%}")
            .Text("A3", "{%$.flag%}");

        AssertGolden("simple-cells", template, JObject.Parse("""{"title":"Report","amount":12.5,"flag":true}"""));
    }

    [Fact]
    public void TableWithArray_Golden()
    {
        var template = TemplateBuilder.Create()
            .Sheet("Data")
            .Text("A1", "{%$.title%}")
            .Text("A3", "Name").Text("B3", "Qty").Text("C3", "Price")
            .Text("A4", "{%$.items::name%}").Text("B4", "{%$.items::qty%}").Text("C4", "{%$.items::price%}")
            .Table("Items", "A3:C4");

        var data = JObject.Parse("""{"title":"Order","items":[{"name":"Widget","qty":2,"price":9.5},{"name":"Gadget","qty":1,"price":20}]}""");

        AssertGolden("table-array", template, data);
    }

    [Fact]
    public void TableWithFooterRow_Golden()
    {
        var template = TemplateBuilder.Create()
            .Sheet("Data")
            .Text("A3", "Name").Text("B3", "Qty")
            .Text("A4", "{%$.items::name%}").Text("B4", "{%$.items::qty%}")
            .Text("A5", "Total")
            .Table("Items", "A3:B5");

        var data = JObject.Parse("""{"items":[{"name":"Widget","qty":2},{"name":"Gadget","qty":1},{"name":"Gizmo","qty":4}]}""");

        AssertGolden("table-with-footer", template, data);
    }

    [Fact]
    public void TypedValues_Golden()
    {
        var template = TemplateBuilder.Create()
            .Sheet("Data")
            .Text("A1", "{%$.int%}")
            .Text("A2", "{%$.decimal%}")
            .Text("A3", "{%$.bool%}")
            .Text("A4", "{%$.zeroId%}")
            .Text("A5", "{%$.text%}")
            .Text("A6", "{%$.text:#IS%}")
            .Text("A7", "{%$.text:#SS%}")
            .Text("A8", "{%$.missing%}");

        var data = JObject.Parse("""{"int":42,"decimal":12.5,"bool":false,"zeroId":"00123","text":"plain"}""");

        AssertGolden("typed-values", template, data);
    }

    [Fact]
    public void JsonWorkbook_Golden()
    {
        var json = JObject.Parse("""
            {
              "Workbook": {
                "Sheets": [ { "SheetId": 1, "Name": "Report" } ],
                "Worksheet": {
                  "Columns": [ { "Min": 1, "Max": 4, "BestFit": true, "Width": 12 } ],
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
            """);

        using var result = (MemoryStream)new Engine().Render("", json);

        XlsxGolden.Assert(result, Path.Combine(Root, "json-workbook"), Validate);
    }
}
