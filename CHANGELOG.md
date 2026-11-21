# Changelog

Release line 8 (.NET 8). Spec and decisions: `tabtabgo/docs/templating-modernization/` in the ttg-workspace.

## 8.0.4

Security backport for the .NET 8 line: `AppSetting` opt-in, confined includes, dynamic-LINQ restrictions (`FilterBy`/`FilterByPrefix`), regex and XML bounds, per-render file system and context, safe template cache, OpenXml
JSON-workbook mode and booleans, no temp files.

**The v8 line has no render limits.** No iteration cap, no render time-out, no output cap, no nesting cap and no template size cap exist on v8 (they exist on v10 and v11 and are not backported). Do not render untrusted templates with v8.

### Fixed (fix round)
- OpenXml JSON workbook: missing `$.StyleMap`, `InlineString`/`SharedString`/`Error` cells, invalid XML characters, sheet list and sheet name validation, duplicate sheet ids, default date style, style lookup by direct key comparison, stylesheet links outside `StylesheetRoot`.
- Round 2: empty number/boolean/date cells are empty cells; sheet names are unique ignoring case and may not be `History` or start/end with an apostrophe; an out-of-range `StyleIndex` is refused; `CreateExcelDoc(fileName, ...)` leaves no file on failure; a stylesheet symbolic-link chain that leaves `StylesheetRoot` through another link is refused (links are resolved the way the OS does). v8's `EnhancedLocalFileSystem` does no link resolution at all: a symbolic link inside the include folder is followed wherever it points, so **v8 must not be used with a template directory that untrusted parties can write to** (not changed).
- Round 6: a stylesheet that cannot be read (no permission, a directory, a pipe) fails as "stylesheet not found", and malformed XML or a DTD as a generic `ArgumentException`, neither carrying a path, inner exception or `SourceUri`; include and stylesheet files are opened once and verified on that same handle (seekable, at most 16 MB), read through a byte-counting cap, and the stylesheet loader opens the resolved real path (a link out of the folder is refused). **Known limitation:** .NET cannot tell a named pipe or a device file from an empty regular file before opening (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks, so the template and stylesheet folders must not be writable by untrusted parties; no native call is made because none is portable to musl, macOS and Windows alike; the "Illegal template path" error names the requested name, not the absolute path; StyleMap `SIndex`/`SAlternativeIndex` must be inside the stylesheet's cell formats (the generated stylesheet of this line has none, so a StyleMap index above 0 needs a loaded stylesheet); the sheet count is checked before the list is copied. Fractional `StyleIndex`, `SIndex` and `SAlternativeIndex` are no longer rounded but refused.
- Round 5: `SheetId` is capped at 65534 (the schema maximum, uint.MaxValue before); every numeric conversion of `SheetId`, `StyleIndex`, `SIndex` and `SAlternativeIndex`, including values beyond any numeric type, is an `ArgumentException`; Number cells are formatted and parsed invariantly under any thread culture.
- Round 3: a dangling or unreadable include file throws "Template not found" without a server path; a Number cell must hold a finite number (canonical invariant form) and a Boolean cell true/false/1/0, otherwise `ArgumentException`, and whitespace-only text is an empty cell; sheet names may not contain control characters or lone surrogates; `SheetId` must be a whole number from 1 to 65534; at most 255 sheets; the source builds with SDK 10 as well (explicit `Enumerable.Reverse`).
- Known limitation, not fixed on v8: the JSON workbook mode still has the legacy shape (the generated stylesheet is empty, dates are written as culture text, and a missing `Columns`, `SheetId` or `BestFit` throws). Use v10 or v11 for a complete JSON workbook mode.
- Include names no longer accept a trailing newline.
- CI: the vulnerability gate fails closed; release tags accept ASCII digits without leading zeros only; `Version` and `TargetFramework` are read as XML.

## 8.0.3

Package refresh for .NET 8 (latest minor/patch packages, vulnerable and conflicting packages removed, test project repaired).
