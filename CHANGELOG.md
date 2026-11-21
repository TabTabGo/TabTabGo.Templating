# Changelog

All notable changes per release line. The line is the major version: 8 = .NET 8, 10 = .NET 10, 11 = .NET 11. Spec and decisions:
`tabtabgo/docs/templating-modernization/` in the ttg-workspace (row ids such as S-3 or X-2 refer to its section 6).

## 11.0.0

Target: net11.0 on SDK 11.0.100 (GA); Microsoft.Extensions 11.0.0 (GA). No API change compared with 10.0.0: ApiCompat in assembly mode (`.github/scripts/apicompat.py`) compares
the packed assemblies with the published 10.0.0 ones; package validation has no baseline (it cannot compare net10.0 with net11.0). `Microsoft.Extensions.Options`,
`DependencyInjection.Abstractions` and `Configuration.Abstractions` are no longer package references: they ship in the net11.0 shared framework (NU1510).
Lock files are committed and restore is always `--locked-mode`.

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
- `RenderString` refuses templates longer than `MaxTemplateCharacters`, default 262 144 (V10-17; parse cost at 1 000 000 characters is 4-11 s of CPU against 0.5-1.4 s at 262 144; raise it explicitly for larger templates).
- `Configuration.GetTemplatingConfiguration()` and `TemplatingConfigurationSection.SectionName` are obsolete; the method reads an optional `appsettings.json`.
- Humanizer 3 (`Humanizer.Core`, `.ar`), Microsoft.Extensions 10.0.14, Newtonsoft.Json 13.0.4, DotLiquid 2.3.260, Open XML SDK 3.5.1, System.Linq.Dynamic.Core 1.7.4.

### Fixed
- B-3..B-6, B-9 (engine state, null/list data, culture, object-typed members); S-6, S-10..S-12 (paths, limits, links); F-1..F-15 (filters); F-16, F-17 (configuration);
  X-4..X-21 (workbooks), B-7 (parallel workbooks); P-7 (nullability, `-warnaserror` clean).

### Fixed (fix round after the diff and security review)
- OpenXml JSON workbook: a missing `$.StyleMap` no longer writes every row empty; `InlineString` writes its `<is>` element, `SharedString` is written inline, `Error` cells accept only Excel error literals; invalid XML characters are removed from cell text;
  an empty `Workbook.Sheets`, an invalid sheet name and a duplicate or colliding sheet id throw `ArgumentException`; the date default style 5 applies only to the generated stylesheet (or a loaded one with more than five formats); the style lookup compares keys directly
  (no JSONPath filter built from data); a stylesheet reached through a symbolic link outside `StylesheetRoot` is refused.
- `Filter` (dynamic LINQ) checks the parsed expression tree against an allow-list (no `PadLeft`, `PadRight`, `Replace`, `Insert`, `Concat`, `Join`, `Format`, string `+`; constant numbers up to 10 000 for string methods); these allocated outside `MaxOutputCharacters`.
- The parse thread reserves 512 KB plus 8 KB per allowed nesting level (1 312 KB by default) instead of 256 MB; a DotLiquid without the internal tokenizer falls back to the stock parse instead of `TypeInitializationException`.
- The template cache is keyed by `MaxNestingDepth` as well and sweeps stale entries only when a new entry is added; partial names no longer accept a trailing newline.
- CI: the vulnerability gate fails closed (any `problems`, no projects, no sources, an empty `frameworks`); `NU1900` is an error; release tags accept ASCII digits without leading zeros only; `Version` and `TargetFramework` are read as XML.

### Removed
- Dependencies on `TabTabGo.IO.Utilities` and `Common.Logging.Core`; `Newtonsoft.Json` from the base package.

## 8.0.4

Security backport for the .NET 8 line: `AppSetting` opt-in, confined includes, dynamic-LINQ restrictions (`FilterBy`/`FilterByPrefix`), regex and XML bounds, per-render file system and context, safe template cache, OpenXml
JSON-workbook mode and booleans, no temp files. See the 8.0.4 release notes.

## 8.0.3

Package refresh for .NET 8 (latest minor/patch packages, vulnerable and conflicting packages removed, test project repaired).
