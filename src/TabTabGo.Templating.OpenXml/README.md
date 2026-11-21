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
dates are serial numbers with a date style. Without `Workbook.Stylesheets` a minimal valid stylesheet is generated (style index 5 is a date); a date cell with no style gets index 5 only
then, or when a loaded stylesheet has more than five cell formats. A missing `$.StyleMap` is an empty map (every cell uses style 0). `InlineString` is written as an inline string; `SharedString`
is written inline too (this mode has no shared-string table); an `Error` cell must hold one of `#N/A`, `#REF!`, `#VALUE!`, `#DIV/0!`, `#NAME?`, `#NUM!`, `#NULL!`, or `ArgumentException` is thrown. Characters invalid in XML 1.0 are removed. A `Number` cell must hold a finite number (written in the canonical invariant form) and a `Boolean` cell `true`, `false`, `1` or `0`; any other text throws `ArgumentException`. A date cell without a style gets the default date style 5 only in the generated stylesheet or a loaded one with at least six cell formats; with five or fewer it is written as a plain number (style 0, no error). A `Date` cell holds a date from 1900-03-01 to 9999-12-31, or a number (or numeric text) from 1 to 2 958 465 that is taken as the Excel serial; text that is neither throws. Whitespace-only and empty `Number`, `Boolean` and `Date` values are empty cells (no type, no value; its style is kept). An explicit `StyleIndex` must be inside the stylesheet's cell formats. Sheet names are unique ignoring case, may not start or end with an apostrophe, and may not be `History`; a sheet without a name gets the first free `SheetN`. `CreateExcelDoc(fileName, ...)` builds in memory and writes the file only on success, so a failing definition leaves no file.
Row `Collapsed` and column `BestFit` accept only `true`, `false`, `1` or `0` (anything else, including a number, throws `ArgumentException`); column `Min`/`Max` are whole numbers and `Width` is a finite number from 0 to 255. `Workbook.Sheets` must not be empty, may hold at most 255 sheets (the writer's hard ceiling), a sheet name is 1 to 31 characters without `[ ] : * ? / \`, and sheet ids are unique (a generated id skips every explicit one); otherwise `ArgumentException`.

A workbook may name a stylesheet (`Workbook.Stylesheets.TemplatePath`). The path must be relative and stay inside `TemplatingOptions.StylesheetRoot` (a relative root is resolved against the
application base directory, which is also the default; `TemplatesPath` is never used); rooted, UNC and `..` paths fail with `ArgumentException`, a symbolic link that leads outside the root is refused, and the XML must not contain a DOCTYPE.

**The default `StylesheetRoot` is `AppContext.BaseDirectory`** (the application folder), so any `.xml` file deployed there can be named as a stylesheet. A host that renders workbook JSON built from untrusted data should set
`TemplatingOptions.StylesheetRoot` to a dedicated folder that holds only stylesheets.

**Known limitation: the template and stylesheet folders must not be writable by untrusted parties.** .NET cannot tell a named pipe or a device file from an empty regular file before opening it (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks the render thread, so no managed check can refuse one; no native call is made because none is portable to musl, macOS and Windows alike. Someone who can create a pipe in those folders can already replace the templates themselves. Files are read through a byte-counting cap of 16 MB.

## Limitations

Tables placed side by side on the same rows are not supported. Formulas, conditional formats, data validations and defined names that point *below* an expanded table keep their old
references (a formula in the template row itself is adjusted). Only `A1+B1` and `SUM(A1:A5)` are understood by the `CellExpression` parser.
