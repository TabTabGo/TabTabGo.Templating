# TabTabGo.Templating.OpenXml

Fills Excel (`.xlsx`) templates with data, using the Open XML SDK 3.x. No Excel installation is needed.

```csharp
var engine = new TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine();
var stream = (MemoryStream)engine.Render("Template/report.xlsx", new { reportResult = jobject });
File.WriteAllBytes("report.xlsx", stream.ToArray());
```

`Render(templatePath, data, culture)` returns a `MemoryStream`. `RenderString` is not supported and `RegisterFilter` is a
no-op for this engine.

## Template syntax

Place expressions in cells of the template workbook:

| Syntax | Meaning |
|---|---|
| `{%path%}` | value at `path` in the data (for example `{%reportResult.title%}`) |
| `{%array::item.property%}` | the `::` marker repeats the template row for every item of `array` |
| `{%path:#IS%}` / `{%path:#SS%}` | write as inline string / shared string |
| `{%path:#<format>%}` | write as text using a .NET format string |
| formulas in the template row | copied per row; shared formulas are re-created for the new rows |

Cell expressions understand binary and `SUM` expressions (`CellExpression` family).

## JSON workbook mode

`Render(null, data)` with `data.Workbook` describing sheets, columns and styles generates a workbook from scratch
(`JsonSpreedSheet`). This mode could not work with Open XML SDK 3.x before 8.0.4 (bug X-1: `Enum.TryParse<CellValues>`
throws because `CellValues` is a struct).

## Known issues on the v8 line

Booleans are written as 0 (X-2) and a 0-byte temp file is left per render (X-3) until 8.0.4. Typing, date and table
fixes (X-4..X-19) ship on v10. See `tabtabgo/docs/templating-modernization/spec.md` section 6.4.
