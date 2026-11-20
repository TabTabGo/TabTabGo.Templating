# TabTabGo.Templating.OpenXml

Fills Excel (`.xlsx`) templates with data, using the Open XML SDK 3.x. No Excel installation is needed.

```csharp
var engine = new TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine();
var stream = (MemoryStream)engine.Render("Template/report.xlsx", new { title = "Report" });
File.WriteAllBytes("report.xlsx", stream.ToArray());
```

`Render(templatePath, data, culture)` returns a `MemoryStream` positioned at the start (the caller disposes it). `data` is an object, a `JObject` or a `JArray` (the array is `$`);
null throws `ArgumentNullException`. `RenderString` is not supported and `RegisterFilter` is a no-op. Nothing is written to disk, a render has its own state, and one engine instance
can render in parallel.

## Template syntax

Place expressions in cells of the template workbook:

| Syntax | Meaning |
|---|---|
| `{%$.path%}` | value at the JSONPath `path` (for example `{%$.title%}`) |
| `{%$.items::name%}` | in the template row of a table, `::` repeats the row for every item of `$.items` and takes `name` from the item |
| `{%$.path:#IS%}` / `{%$.path:#SS%}` | write as inline string / shared string |
| `{%$.path:#N%}` | parse text as a number (invariant); text that is not a number stays text |
| `{%$.date:#dd MMMM yyyy%}` | write a date as text with a .NET format in the render culture |
| a formula in the template row | becomes a shared formula over the new rows (each column its own `si`) |

**Table rules.** The table's range starts with the header; the template row is the row after it; rows below the template row inside the table range (a totals row) follow the
data. An empty array leaves one blank row. Rows below the table keep their distance (gaps included); merged cells, other tables, filters and shared-formula ranges below follow.

## Typing rules

| JSON | Cell |
|---|---|
| integer, float, decimal | number, written with `.` whatever the server culture (`NaN`/`Infinity` become text) |
| boolean | boolean (`1`/`0`) |
| date | Excel serial number; the template cell's style formats it. With an explicit `:#format` it is text |
| string | text, never converted (`00123` and `+971...` stay text) |
| null or missing | empty text |
| object, array | JSON text |

Characters that are not valid XML 1.0 are removed; emoji and other supplementary characters are kept.

## JSON workbook mode

`Render("", data)` with `data.Workbook` describing sheets, columns and styles generates a workbook from scratch (`JsonSpreedSheet`). Every entry of `Workbook.Sheets` becomes a sheet that
uses the single `Workbook.Worksheet` definition. Cell `DataType` is one of `String`, `Number`, `Boolean`, `Date`, `Error`, `SharedString`, `InlineString` (unknown names become `String`);
dates are serial numbers with a date style. Without `Workbook.Stylesheets` a minimal valid stylesheet is generated (style index 5 is a date).

A workbook may name a stylesheet (`Workbook.Stylesheets.TemplatePath`). The path must be relative and stay inside `TemplatingOptions.StylesheetRoot` (a relative root is resolved against the
application base directory, which is also the default; `TemplatesPath` is never used); rooted, UNC and `..` paths fail with `ArgumentException`, and the XML must not contain a DOCTYPE.

## Limitations

Tables placed side by side on the same rows are not supported. Formulas, conditional formats, data validations and defined names that point *below* an expanded table keep their old
references (a formula in the template row itself is adjusted). Only `A1+B1` and `SUM(A1:A5)` are understood by the `CellExpression` parser.
