# TabTabGo Templating helper  Library 

## Using DotLiquid library

## Using OpenXml library

## Publishing

Two publish paths, both to nuget.org:

| Trigger | Workflow | Package version |
|---|---|---|
| push to `master` | `.github/workflows/publish-to-nuget-master.yml` | `<major>.<minor>.<yyMM>.<dd><run_number>` — e.g. `8.0.2608.1100123` |
| push of a `v*` tag | `.github/workflows/publish-to-nuget.yml` | the tag, e.g. `v8.0.2` → `8.0.2` |

Every push to `master` publishes all three packages (`TabTabGo.Templating`,
`TabTabGo.Templating.Dotliquid`, `TabTabGo.Templating.OpenXml`) at the same
version. `major.minor` comes from the `<Version>` in the csproj files — bump
that when you want a new major/minor line. Only the NuGet package version is
date-stamped; `AssemblyVersion` stays as declared in the csproj.

Pack locally without pushing:

```bash
python3 tools/packnpush.py -p 1 --local
python3 tools/packnpush.py -p 1 --print-version   # just show the version
```