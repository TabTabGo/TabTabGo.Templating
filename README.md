# TabTabGo.Templating

> **v11 line (`11.0.0`, `11.0.0-rc.N` for pre-releases).** Same code and API as 10.0.0 on net11.0, SDK 11.0.100 and Microsoft.Extensions 11.0.0 (GA).

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
| v11 | `releases/v11.0.x/v11.0.N` | net11.0 | stable (`11.0.0`; `-rc.N` tags are consumer trials on the same release branch) (this branch); built and tested on SDK 11.0.100 |

Design and decisions: `tabtabgo/docs/templating-modernization/` in the ttg-workspace (spec and ADRs 0001-0005).

## Building and testing

The SDK is pinned by `global.json` (11.0.100, `latestPatch`; Microsoft.Extensions 11.0.0) and the tests run on Microsoft Testing Platform
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
* **Known limitation: the template and stylesheet folders must not be writable by untrusted parties.** .NET cannot tell a named pipe or a device file from an empty regular file before opening it (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks the render thread, so no managed check can refuse one; no native call is made because none is portable to musl, macOS and Windows alike. Someone who can create a pipe in those folders can already replace the templates themselves. Files are read through a byte-counting cap of 16 MB.

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

Pack locally without publishing: `dotnet pack TabTabGo.Templating.sln -c Release -p:Version=11.0.0 -o out`. Package validation has no baseline on this line; the API surface is compared with the previous line by ApiCompat in assembly mode
(`python3 .github/scripts/apicompat.py out net10.0 net11.0 --baseline-version 10.0.0`, tool pinned in `.config/dotnet-tools.json`, `dotnet tool restore`). **Order of releases:** the ApiCompat step of `publish.yml` compares with the published `10.0.0` and fails the release when that version is not on nuget.org, so a v11 tag cannot be released before v10.0.0 is published (a transient nuget.org error is retried three times and then reported as such, not as a missing baseline). The reference assemblies are the declared dependencies from the committed lock files and the newest installed .NET reference pack, never the whole NuGet cache.

## Release notes 11.0.0

No API or behaviour change compared with 10.0.0: the target framework is net11.0, `Microsoft.Extensions.*` are the 11.0.0 GA builds, the SDK is
`11.0.100`, and ApiCompat compares the assemblies with 10.0.0. `Microsoft.Extensions.Options`, `DependencyInjection.Abstractions` and `Configuration.Abstractions` come from the
net11.0 shared framework instead of packages. DotLiquid, Humanizer, System.Linq.Dynamic.Core, Open XML SDK and Newtonsoft.Json have no net11 builds and resolve to
their net10/net8 assets; the test suite proves the combination (run on net11.0 with SDK 11.0.100).

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
1. **Output limit.** A render, a `capture` block or the result of `Append`, `Prepend`, `Join`, `Replace`, `ReplaceFirst`, `RemoveFirst`, `Plus`, `Times`, `Concat` longer than
   `MaxOutputCharacters` (default 50 000 000) throws `RenderLimitExceededException` (it derives from `TimeoutException`). `MaxIterations` (100 000 items in one
   `for` loop; each loop counts separately) and `RenderTimeout` (30 s) apply to every render; 0 / `Timeout.InfiniteTimeSpan` disables them.
   The three limits surface as three exception types: `RenderLimitExceededException` (output limit; regular-expression time-outs are a `RegexMatchTimeoutException`, also a
   `TimeoutException`), `DotLiquid.Exceptions.MaximumIterationsExceededException` (a DotLiquid `RenderException`, not a `TimeoutException`) and
   `OperationCanceledException` (the render time-out). A host that catches `TimeoutException` does not catch the last two.
2. **`FullPath` returns a path relative to the template folder** unless `AllowAbsolutePaths` is true (hosts that give the path to a PDF renderer opt in).
3. **Culture.** Filters format with the render culture (the `culture` argument), not the thread's ambient culture; numbers and dates in data are parsed invariantly. `Number`/`DivideBy` of
   text such as `1,5` fall back to the render culture; division by zero is 0.
4. **Dates.** A date-time of unspecified kind is UTC; `Today` is the UTC date; `FormatDate('Now')` is UTC; `ToTimeZone` resolves ids, not display names, and its result is a
   date-time with offset, so printing it without `FormatDate` ends with the offset suffix (`1/15/2026 2:30:00 PM +04:00`).
5. **OpenXml typing.** Cells are typed: JSON strings such as `"0502888414"` (IDs with leading zeros, phone numbers) stay text; numbers use a `.` regardless of the server culture; dates are serial numbers
   (format the cell in the template) unless the macro has an explicit `:#format`; text is written inline unless `:#SS`.
6. **OpenXml tables.** Rows below a table keep their gaps (they used to be packed together); a blank row remains for an empty array; the template row may contain
   static text (kept as text); the first sheet part is no longer shared by several `Sheets` entries (one part per entry).
7. `Distinct` removes duplicate hashes (compared by content) and keeps scalars; `Union` with an empty second list returns the first list; `GroupBy` puts missing keys in a group named "".
8. `TabTabGo.IO.Utilities`, `Common.Logging.Core` and `Newtonsoft.Json` (base package) are no longer dependencies; Humanizer 3 (`Humanizer.Core` + `.ar`) replaces the
   Humanizer 2 metapackage (the English and Arabic number-to-words output is unchanged for the golden matrix).
9. **Parse limits.** `RenderString` refuses a template longer than `MaxTemplateCharacters` (262 144; it was 1 000 000 in the 10.0.0 drafts, lowered because parsing 1 000 000 characters costs 4-11 s of CPU against 0.5-1.4 s at 262 144; raise it explicitly if you need larger templates). `Render(path)` parses a template file of any size (files are trusted, deployed by the host): a host that accepts uploaded templates must read them itself and render via `RenderString`, which `MaxTemplateCharacters` caps. Any template (string or file) that nests blocks deeper than
   `MaxNestingDepth` (100) is refused; DotLiquid's own parse expressions run under `ParseTimeout` (5 s). Filter regexes (`Replace`, `ReplaceFirst`, `RemoveFirst`) run under `RegexTimeout` (200 ms).
10. `Configuration.GetTemplatingConfiguration()` and `TemplatingConfigurationSection.SectionName` are `[Obsolete]`; the method now reads an optional `appsettings.json`.

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
