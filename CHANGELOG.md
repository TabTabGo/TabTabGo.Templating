# Changelog

All notable changes per release line. The line is the major version: 8 = .NET 8, 10 = .NET 10, 11 = .NET 11. Spec and decisions:
`tabtabgo/docs/templating-modernization/` in the ttg-workspace (row ids such as S-3 or X-2 refer to its section 6).

## 10.0.0

Target: net10.0. Includes every 8.0.4 fix unchanged (fresh context per render, regex time-outs that propagate, include confinement, `AppSetting` opt-in, safe XML, JSON workbook
mode, no temp files). No public API of 8.0.4 was removed or changed.

### Added
- `TemplatingOptions`: `MaxIterations`, `RenderTimeout`, `MaxOutputCharacters`, `MaxDepth`, `AllowAbsolutePaths`; every numeric option is validated by the engine constructors.
- `RenderLimitExceededException`, and internal guards (bounded `capture`, `Append`, `Prepend`, `Join`, `Replace`, `ReplaceFirst`, `RemoveFirst`, `Plus`, `Times`, `Concat`) that return what the stock DotLiquid 2.3.260 filters return within the limit.
- `CountryName(code, culture)`, exact `decimal` overloads of `FormatNumber`.
- OpenXml: valid default stylesheet, one worksheet part per JSON-workbook sheet, per-render `RenderSession`.
- Test kit (`TabTabGo.Templating.TestKit`): golden helpers for text and workbooks, workbook builder; 31 Liquid and 6 workbook goldens; coverage gate in CI.

### Changed (behaviour; see the release notes in the README)
- Output, capture blocks and growing filters are capped at `MaxOutputCharacters` and throw instead of truncating; `FullPath` is relative; filters format with the render culture and parse
  invariantly; dates default to UTC; OpenXml cells are typed by JSON token, dates are serials, leading zeros stay text, tables keep row gaps and shift merged cells, filters and other tables.
- `Configuration.GetTemplatingConfiguration()` and `TemplatingConfigurationSection.SectionName` are obsolete; the method reads an optional `appsettings.json`.
- Humanizer 3 (`Humanizer.Core`, `.ar`), Microsoft.Extensions 10.0.14, Newtonsoft.Json 13.0.4, DotLiquid 2.3.260, Open XML SDK 3.5.1, System.Linq.Dynamic.Core 1.7.4.

### Fixed
- B-3..B-6, B-9 (engine state, null/list data, culture, object-typed members); S-6, S-10..S-12 (paths, limits, links); F-1..F-15 (filters); F-16, F-17 (configuration);
  X-4..X-21 (workbooks), B-7 (parallel workbooks); P-7 (nullability, `-warnaserror` clean).

### Removed
- Dependencies on `TabTabGo.IO.Utilities` and `Common.Logging.Core`; `Newtonsoft.Json` from the base package.

## 8.0.4

Security backport for the .NET 8 line: `AppSetting` opt-in, confined includes, dynamic-LINQ restrictions (`FilterBy`/`FilterByPrefix`), regex and XML bounds, per-render file system and context, safe template cache, OpenXml
JSON-workbook mode and booleans, no temp files. See the 8.0.4 release notes.

## 8.0.3

Package refresh for .NET 8 (latest minor/patch packages, vulnerable and conflicting packages removed, test project repaired).
