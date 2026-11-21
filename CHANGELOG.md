# Changelog

Release line 8 (.NET 8). Spec and decisions: `tabtabgo/docs/templating-modernization/` in the ttg-workspace.

## 8.0.4

Security backport for the .NET 8 line: `AppSetting` opt-in, confined includes, dynamic-LINQ restrictions (`FilterBy`/`FilterByPrefix`), regex and XML bounds, per-render file system and context, safe template cache, OpenXml
JSON-workbook mode and booleans, no temp files.

**The v8 line has no render limits.** No iteration cap, no render time-out, no output cap, no nesting cap and no template size cap exist on v8 (they exist on v10 and v11 and are not backported). Do not render untrusted templates with v8.

### Fixed (fix round)
- OpenXml JSON workbook: missing `$.StyleMap`, `InlineString`/`SharedString`/`Error` cells, invalid XML characters, sheet list and sheet name validation, duplicate sheet ids, default date style, style lookup by direct key comparison, stylesheet links outside `StylesheetRoot`.
- Include names no longer accept a trailing newline.
- CI: the vulnerability gate fails closed; release tags accept ASCII digits without leading zeros only; `Version` and `TargetFramework` are read as XML.

## 8.0.3

Package refresh for .NET 8 (latest minor/patch packages, vulnerable and conflicting packages removed, test project repaired).
