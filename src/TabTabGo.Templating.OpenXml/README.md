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
(`JsonSpreedSheet`). Cell `DataType` is one of `String`, `Number`, `Boolean`, `Date`, `Error`, `SharedString`,
`InlineString` (unknown names become `String`). Before 8.0.4 this mode threw for every cell (bug X-1).

## Behaviour notes

* A JSON workbook may name a stylesheet (`Workbook.Stylesheets.TemplatePath`). The path must be relative and stay inside
  `TemplatingOptions.TemplatesPath` (the application base directory when unset); rooted, UNC and `..` paths fail with `ArgumentException`,
  and the XML must not contain a DOCTYPE (S-7, 8.0.4).

* Rendering happens in memory: no temporary file is created, even when a render fails (X-3, 8.0.4).
* A boolean value is written as `1`/`0` in a boolean cell, so `true` shows as TRUE (X-2, 8.0.4).

## Known issues on the v8 line

Typing, date and table fixes (X-4..X-19) ship on v10. One engine instance keeps per-render state: do not share it across
threads (B-7). See `tabtabgo/docs/templating-modernization/spec.md` section 6.4.
