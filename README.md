# TabTabGo.Templating

Template rendering for TabTabGo products: Liquid text/HTML (DotLiquid) and Excel workbooks (Open XML).

| Package | Purpose |
|---|---|
| [`TabTabGo.Templating`](src/TabTabGo.Templating/README.md) | The abstract `TemplatingEngine` contract and the configuration section. No engine dependency. |
| [`TabTabGo.Templating.Dotliquid`](src/TabTabGo.Templating.Dotliquid/README.md) | `Liquid.TemplatingEngine`: DotLiquid rendering, 40+ filters, the `increment` tag. |
| [`TabTabGo.Templating.OpenXml`](src/TabTabGo.Templating.OpenXml/README.md) | `OpenXml.SpreadSheet.TemplatingEngine`: fills `.xlsx` templates from JSON/objects. |

Pick `.Dotliquid` for text, e-mail and HTML, and `.OpenXml` for spreadsheets. Both depend on `TabTabGo.Templating`
and can be referenced together.

## Release lines

| Line | Branch | Target | Status |
|---|---|---|---|
| v8 | `releases/v8.0.x/v8.0.N` | net8.0 | bridge for .NET 8 consumers (8.0.3 package refresh, 8.0.4 security backport: AppSetting opt-in, confined includes, regex time-out, DTD-free XML, per-render file system, safe template cache, OpenXml fixes). .NET 8 support ends 2026-11-10 |
| v10 | `releases/v10.0.x/v10.0.0` | net10.0 | planned LTS line |
| v11 | `releases/v11.0.x/v11.0.0` | net11.0 | planned, pre-release until .NET 11 GA |

Design and decisions: `tabtabgo/docs/templating-modernization/` in the ttg-workspace (spec and ADRs 0001-0005).

## Building and testing

The SDK is pinned by `global.json` (8.0.100, roll forward to the latest 8.0.x feature band).

```bash
dotnet restore TabTabGo.Templating.sln
dotnet build TabTabGo.Templating.sln -c Release
dotnet test TabTabGo.Templating.sln -c Release
dotnet list TabTabGo.Templating.sln package --vulnerable --include-transitive
```

Tests are offline. Tests that format numbers or dates pin the culture they need; tests write only into a temp directory.
The `Compat` tests pin behaviour that consumers rely on: the Lisa `Filter` expression corpus
(`LisaFilterCorpusTests`), the `OpenXmlType` strings, and `AllThreePackages_Compile`.

## Releasing

Packages are published **only from tags**. There is no publish from `master` or from a pull request.

1. Set `<Version>` in `Directory.Build.props` on the release branch `releases/vM.m.x/vM.m.p`.
2. Merge the feature branch into the release branch through a pull request (`ci` must pass).
3. Push the tag `vM.m.p` (or `vM.m.p-rc.N`) on the release branch commit.
4. `publish.yml` checks the tag against the commit (tag major equals target framework major, tag is on its release
   branch, version equals the tag, version not yet on nuget.org), builds, tests, scans, packs, and publishes through
   nuget.org trusted publishing. Stable tags wait for approval of the `nuget-stable` environment.

If a publish run fails part-way (some packages pushed, others not), do not re-run it: re-runs are refused and `dotnet nuget
push` is run without `--skip-duplicate`. Fix the cause, bump the patch version in `Directory.Build.props` on the release
branch and tag the new version.

Pack locally without publishing:

```bash
dotnet pack TabTabGo.Templating.sln -c Release -p:Version=8.0.3 -o out
```

## Release notes 8.0.4

**On v8, Date cells in JSON workbooks are written as culture-formatted text in a numeric cell, and the date value is LOST or WRONG when the workbook is opened in a spreadsheet (a tested example: LibreOffice reads 2024-05-06 as 5 and 2000-01-02 as 1). Do not use Date cells in JSON workbooks on v8; use v10 or v11.** **v8 does not confine symbolic links in the include folder: do not use it with a template directory that untrusted parties can write to.** **Known limitation: the template and stylesheet folders must not be writable by untrusted parties.** .NET cannot tell a named pipe or a device file from an empty regular file before opening it (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks the render thread, so no managed check can refuse one; no native call is made because none is portable to musl, macOS and Windows alike. Someone who can create a pipe in those folders can already replace the templates themselves. Files are read through a byte-counting cap of 16 MB.

**The v8 line has no render limits.** There is no iteration cap, no render time-out, no output cap, no nesting cap and no template size cap (`MaxIterations`, `RenderTimeout`, `MaxOutputCharacters`, `MaxNestingDepth` and `MaxTemplateCharacters` exist only on v10 and v11, and are not backported). Do not render templates from untrusted sources with a v8 package: a template can loop, allocate or nest without bound. Use v10 or v11 for that.

Callers (e-mail and report services) must now catch `RegexMatchTimeoutException` from `Render`/`RenderString`: a catastrophic regex in
DotLiquid's built-in `Replace`/`ReplaceFirst` stops at `TemplatingOptions.RegexTimeout` (200 ms by default; 6.0.5 and 8.0.3 stopped after 10 s)
and the exception propagates instead of producing output.

## Upgrading from 6.0.5

| Change | Impact |
|---|---|
| net8.0 only (6.0.5: netstandard2.1 / net6.0) | consumers already on .NET 8 are fine |
| `TabTabGo.Core` is no longer a transitive dependency | reference it directly if you use its types |
| Open XML SDK 3.x (was 2.18.0 in `.OpenXml` and a 2.7.1 fork in `.Dotliquid`) | review code that uses Open XML types: SDK 3 changed enums to structs; the CS0433 conflict between the two packages is gone |
| `System.Linq.Dynamic.Core` 1.7.4 | `Filter` expressions that reflect over non-predefined types now fail (intended); `ToString()`/`Equals()` on `object` still work |
| `System.Xml.XPath` / `System.Xml.XDocument` 4.3.0 stubs removed | none on .NET 8 |

## License

MIT, see [LICENSE](LICENSE).
