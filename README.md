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
| v8 | `releases/v8.0.x/v8.0.N` | net8.0 | bridge for .NET 8 consumers (8.0.3 package refresh, 8.0.4 security backport). .NET 8 support ends 2026-11-10 |
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

Pack locally without publishing:

```bash
dotnet pack TabTabGo.Templating.sln -c Release -p:Version=8.0.3 -o out
```

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
