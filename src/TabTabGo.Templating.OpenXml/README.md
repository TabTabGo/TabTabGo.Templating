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

**Known limitation: the template and stylesheet folders must not be writable by untrusted parties.** .NET cannot tell a named pipe or a device file from an empty regular file before opening it (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks the render thread, so no managed check can refuse one; no native call is made because none is portable to musl, macOS and Windows alike. Someone who can create a pipe in those folders can already replace the templates themselves. Files are read through a byte-counting cap of 16 MB.

## Behaviour notes

* A JSON workbook may name a stylesheet (`Workbook.Stylesheets.TemplatePath`). The path must be relative and stay inside
  `TemplatingOptions.StylesheetRoot` (a relative root is resolved against the application base directory, which is also the default;
  `TemplatesPath` is never used). Rooted, UNC and `..` paths fail with `ArgumentException`, and the XML must not contain a DOCTYPE
  (S-7, 8.0.4). **Behaviour change:** 6.0.5 accepted absolute and UNC stylesheet paths; they now fail. No consumer in the workspace uses
  `Workbook.Stylesheets`.
* JSON workbook fixes (8.0.4 fix round): a missing `$.StyleMap` is an empty map (cells are still written, style 0); `InlineString` is written as an inline string and `SharedString` inline too (no shared-string table); an `Error` cell must hold an Excel error literal (`#N/A`, `#REF!`, `#VALUE!`, `#DIV/0!`, `#NAME?`, `#NUM!`, `#NULL!`) or `ArgumentException` is thrown; characters invalid in XML 1.0 are removed; `Workbook.Sheets` may not be empty, sheet names are 1 to 31 characters without `[ ] : * ? / \\`, and sheet ids must be unique; the default date style 5 applies only to a loaded stylesheet with more than five cell formats; the style lookup compares keys directly; a stylesheet reached through a symbolic link outside `StylesheetRoot` is refused. The default `StylesheetRoot` is `AppContext.BaseDirectory`: a host that renders workbook JSON built from untrusted data should set it to a dedicated folder.
* On v8 a Number cell must hold a finite number and a Boolean cell true/false/1/0 (otherwise `ArgumentException`), sheet names may not contain control characters or lone surrogates, `SheetId` must be a whole number from 1 to 65534, and at most 255 sheets are accepted.
* **On v8, Date cells in JSON workbooks are written as culture-formatted text in a numeric cell, and the date value is LOST or WRONG when the workbook is opened in a spreadsheet (a tested example: LibreOffice reads 2024-05-06 as 5 and 2000-01-02 as 1). Do not use Date cells in JSON workbooks on v8; use v10 or v11.**
* Known limitation on v8: the JSON workbook mode keeps its legacy shape in two places. The generated stylesheet is empty (style indexes above 0 need a loaded stylesheet), and dates are culture text parsed with the thread culture. Everything else described above is validated like v10 and v11, with these differences: a missing `Columns` array is an `ArgumentException` (v10 and v11 accept it as "no columns"), and every sheet needs a `SheetId` (v10 and v11 generate a missing one). Because the generated stylesheet has no cell formats, workbooks generated without a loaded stylesheet may be flagged by strict validators. The JSON number `1.0` counts as 1 for `BestFit`/`Collapsed` and is accepted, while the text `"1.0"` is refused.
* Also on v8, validated the same way as on v10 and v11: column `Min`/`Max` (required whole numbers from 1 to 16384, `Min` <= `Max`), `Width` (0 to 255), `BestFit` and row `Collapsed` (the booleans, 1/0, or the text true/false/1/0 in any case; a JSON `null` means "not given"), and `SheetId` (a whole number from 1 to 65534, unique). Empty number, boolean and date values are empty cells, sheet names are unique ignoring case (no `History`, no leading or trailing apostrophe), an explicit `StyleIndex` must be inside the stylesheet, and `CreateExcelDoc(fileName, ...)` writes the file only on success.
* `Workbook.Worksheet.SheetData.Rows` is required and must be an array (use an empty array for no rows): a missing or non-array `Rows` throws `ArgumentException`. A workbook of the wrong JSON shape (`Workbook.Sheets`, `Columns`, `Rows` or `Cells` not an array, an entry that is not an object, an object or array sheet `Name`) throws `ArgumentException` (8.0.5; before, Json.NET's `InvalidOperationException` or `InvalidCastException`); the message names the path, never a value, and nothing is written. A JSON `null` `Cells` is "not given".
* Rendering happens in memory: no temporary file is created, even when a render fails (X-3, 8.0.4).
* A boolean value is written as `1`/`0` in a boolean cell, so `true` shows as TRUE (X-2, 8.0.4).

## Known issues on the v8 line

Typing, date and table fixes (X-4..X-19) ship on v10. One engine instance keeps per-render state: do not share it across
threads (B-7). See `tabtabgo/docs/templating-modernization/spec.md` section 6.4.
