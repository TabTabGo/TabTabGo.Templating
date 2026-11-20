# TabTabGo.Templating

Template rendering for TabTabGo products: Liquid text/HTML (DotLiquid) and Excel workbooks (Open XML).

| Package | Purpose |
|---|---|
| [`TabTabGo.Templating`](src/TabTabGo.Templating/README.md) | The abstract `TemplatingEngine` contract, `TemplatingOptions` and `AddTabTabGoTemplating`. No engine dependency. |
| [`TabTabGo.Templating.Dotliquid`](src/TabTabGo.Templating.Dotliquid/README.md) | `Liquid.TemplatingEngine`: DotLiquid rendering, 40+ filters, the `increment` tag, render limits. |
| [`TabTabGo.Templating.OpenXml`](src/TabTabGo.Templating.OpenXml/README.md) | `OpenXml.SpreadSheet.TemplatingEngine`: fills `.xlsx` templates from JSON/objects, or writes a workbook from JSON. |

Pick `.Dotliquid` for text, e-mail and HTML, and `.OpenXml` for spreadsheets. Both depend on `TabTabGo.Templating` and can be
referenced together. None of the packages depends on `TabTabGo.Core` (ADR 0002): the lines release independently of the Core lines.

## Release lines

| Line | Branch | Target | Status |
|---|---|---|---|
| v8 | `releases/v8.0.x/v8.0.N` | net8.0 | bridge for .NET 8 consumers (8.0.4 is the security backport). .NET 8 support ends 2026-11-10 |
| v10 | `releases/v10.0.x/v10.0.N` | net10.0 | LTS line (this branch): every defect of spec section 6 fixed, sandboxing, options + DI, goldens, coverage gates |
| v11 | `releases/v11.0.x/v11.0.N` | net11.0 | pre-release (`-rc.N`) until .NET 11 GA; built and verified on CI only until a .NET 11 SDK is installed |

Design and decisions: `tabtabgo/docs/templating-modernization/` in the ttg-workspace (spec and ADRs 0001-0005).

## Building and testing

The SDK is pinned by `global.json` (10.0.401, roll forward to the latest patch) and the tests run on Microsoft Testing Platform
(xunit.v3, coverlet.MTP, NSubstitute; ADR 0005). Packages are versioned centrally in `Directory.Packages.props` and restored with
`packages.lock.json` files (`--locked-mode` on CI).

```bash
dotnet restore TabTabGo.Templating.sln
dotnet build TabTabGo.Templating.sln -c Release -warnaserror
dotnet test --solution TabTabGo.Templating.sln -c Release
python3 .github/scripts/run_tests.py          # all test projects with coverage, then the gate: 85 % lines, 75 % branches per package
dotnet list TabTabGo.Templating.sln package --vulnerable --include-transitive
```

Tests are offline, pin the culture they need, run in temporary folders and never share a process-wide setting with another test.
Rendered output is pinned by **golden files**: `test/TabTabGo.Templating.DotLiquid.Tests/Golden/Liquid/<case>/` (template, data,
culture, `expected.txt`) and `test/TabTabGo.Templating.OpenXml.Tests/Golden/Xlsx/<scenario>/` (canonical XML of every workbook part). Every
workbook also passes the Open XML validator with zero errors. After an intended output change run the golden tests with
`UPDATE_GOLDEN=1`, review the diff, and commit it. `Compat` tests pin behaviour consumers rely on (the Lisa `Filter` expression corpus,
the `OpenXmlType` strings, `AllThreePackages_Compile`).

## Security model (ADR 0003)

* **Configuration is opt-in.** `AppSetting` returns only keys in `TemplatingOptions.AppSettings`; templates never read files or host settings.
* **Includes are confined** to the template folder (names, `..`, symbolic links); error messages and `FullPath` never contain server paths
  unless `AllowAbsolutePaths` is set.
* **Dynamic LINQ is restricted** (no reflection on arbitrary types); `FilterBy`/`FilterByPrefix` need no expression at all.
* **Regex and XML are bounded:** `RegexTimeout` (200 ms), DTDs rejected, `MaxXmlCharacters`.
* **Renders are bounded:** `MaxIterations`, `RenderTimeout`, `MaxOutputCharacters` (also for `capture` blocks and the growing filters),
  `MaxDepth` for the object graph. Exceeding the output limit throws `RenderLimitExceededException`.
* Every render has its own context, variables and file system: an engine instance is safe to share between threads.

## Releasing

Packages are published **only from tags**. There is no publish from `master` or from a pull request.

1. Set `<Version>` in `Directory.Build.props` on the release branch `releases/vM.m.x/vM.m.p`.
2. Merge the feature branch into the release branch through a pull request (`ci` must pass).
3. Push the tag `vM.m.p` (or `vM.m.p-rc.N`) on the release branch commit.
4. `publish.yml` checks the tag against the commit (tag major equals target framework major, tag is on its release branch, version equals the
   tag, a stable tag carries no pre-release pins, version not yet on nuget.org), builds with `-warnaserror`, tests with the coverage gate,
   scans, packs, checks the package set (names, version, target framework, bounded dependency ranges, no `-local` builds) and publishes
   through nuget.org trusted publishing. Stable tags wait for approval of the `nuget-stable` environment.

If a publish run fails part-way, do not re-run it: re-runs are refused. Fix the cause, bump the patch version and tag again.

Pack locally without publishing: `dotnet pack TabTabGo.Templating.sln -c Release -p:Version=10.0.0 -o out`. Package validation compares with 6.0.5
(`CompatibilitySuppressions.xml` lists the dropped target frameworks).

## Release notes 10.0.0

**TabTabGo.Templating 10.0.0 - the .NET 10 line.** Upgrade from 6.0.5, 8.0.3 or 8.0.4 (every 8.0.4 security fix is included, unchanged).
No public API of 8.0.4 was removed or changed (checked with ApiCompat); everything below is additive or a documented behaviour change.

Fixed:
- **Engines.** Rendering is safe on a shared engine: no shared hash cache, per-render context, culture and file system (B-3, B-5, B-6, B-9).
  `RenderString` and `ParseTemplate` initialise the engine (B-4). Data may be null, a list or any scalar (exposed as `model`).
- **Filters.** `MarkNumbers`, `ToWords` (rounding, English capitalisation, range), `Union`, `Distinct`, `OrderBy`, `GroupBy`, `SelectMany`, `Flatten`,
  `JSelectTokenValue` (scalars), `ToTimeZone` (IANA and Windows ids, daylight saving), `OffsetDate`/`FormatDate`, `Increment`, `CountryName`,
  `OpenXmlType`/`OpenXmlMaxWidth`, exact `decimal` `FormatNumber` (F-1..F-15).
- **OpenXml.** Numbers are typed by their JSON token and written invariantly, text that looks like a number stays text, dates are Excel serials,
  booleans and the JSON-workbook mode work, tables keep merged cells, other tables, filters, gaps and formulas consistent, an empty array leaves a
  valid table, shared strings are indexed (linear time instead of a scan and a save per string), emoji survive (X-4..X-21). Every workbook passes the Open XML validator.
- **Sandbox.** `FullPath` is relative, symbolic links cannot leave the include root, and renders are limited (S-6, S-10..S-12).

Behaviour changes to check before upgrading (v10.0.0 compared with 8.0.4):
1. **Output limit.** A render, a `capture` block or the result of `Append`, `Prepend`, `Join`, `Replace`, `ReplaceFirst`, `Plus`, `Times`, `Concat` longer than
   `MaxOutputCharacters` (default 50 000 000) throws `RenderLimitExceededException` (it derives from `TimeoutException`). `MaxIterations` (100 000) and
   `RenderTimeout` (30 s) apply to every render; 0 / `Timeout.InfiniteTimeSpan` disables them.
2. **`FullPath` returns a path relative to the template folder** unless `AllowAbsolutePaths` is true (hosts that give the path to a PDF renderer opt in).
3. **Culture.** Filters format with the render culture (the `culture` argument); numbers and dates in data are parsed invariantly. `Number`/`DivideBy` of
   text such as `1,5` fall back to the render culture; division by zero is 0.
4. **Dates.** A date-time of unspecified kind is UTC; `Today` is the UTC date; `FormatDate('Now')` is UTC; `ToTimeZone` resolves ids, not display names.
5. **OpenXml typing.** IDs with leading zeros and phone numbers stay text; numbers use a `.` regardless of the server culture; dates are serial numbers
   (format the cell in the template) unless the macro has an explicit `:#format`; text is written inline unless `:#SS`.
6. **OpenXml tables.** Rows below a table keep their gaps (they used to be packed together); a blank row remains for an empty array; the template row may contain
   static text (kept as text); the first sheet part is no longer shared by several `Sheets` entries (one part per entry).
7. `Distinct` keeps scalars and compares hashes by content; `Union` with an empty second list returns the first list; `GroupBy` puts missing keys in a group named "".
8. `TabTabGo.IO.Utilities`, `Common.Logging.Core` and `Newtonsoft.Json` (base package) are no longer dependencies; Humanizer 3 (`Humanizer.Core` + `.ar`) replaces the
   Humanizer 2 metapackage (the English and Arabic number-to-words output is unchanged for the golden matrix).
9. `Configuration.GetTemplatingConfiguration()` and `TemplatingConfigurationSection.SectionName` are `[Obsolete]`; the method now reads an optional `appsettings.json`.

Known limitations: tables placed side by side on the same rows are not supported; formulas below an expanded table are not re-pointed (a formula in the template row is);
conditional formats, data validations and defined names below a table keep their old references (see the OpenXml README).

## Upgrading from 6.0.5

| Change | Impact |
|---|---|
| net10.0 only (6.0.5: netstandard2.1 / net6.0) | the 8.x line covers .NET 8 |
| `TabTabGo.Core` is no longer a transitive dependency | reference it directly if you use its types |
| Open XML SDK 3.x (was 2.18.0 in `.OpenXml` and a 2.7.1 fork in `.Dotliquid`) | review code that uses Open XML types; the CS0433 conflict between the two packages is gone |
| `System.Linq.Dynamic.Core` 1.7.4 | `Filter` expressions that reflect over non-predefined types fail (intended); `ToString()`/`Equals()` on `object` still work |
| everything in the release notes above | |

## License

MIT, see [LICENSE](LICENSE).
