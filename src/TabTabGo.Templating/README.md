# TabTabGo.Templating

The abstract contract shared by the TabTabGo templating engines, the options they read, and the dependency-injection entry point. It has no
template-engine dependency.

```csharp
public abstract class TemplatingEngine
{
    public abstract string EngineName { get; }
    public abstract bool IsCached(string templatePath);
    public abstract void ParseTemplate(string templatePath);
    public abstract object Render(string templatePath, object? sourceData, string culture = "en-Us");
    public abstract string RenderString(string templateString, object? sourceData, string culture = "en-Us");
    public abstract void RegisterFilter(Type type);
}
```

Engines: [`TabTabGo.Templating.Dotliquid`](../TabTabGo.Templating.Dotliquid/README.md) and
[`TabTabGo.Templating.OpenXml`](../TabTabGo.Templating.OpenXml/README.md).

## TemplatingOptions

| Option | Default | Meaning |
|---|---|---|
| `TemplatesPath` | `null` | base path for the includes of `RenderString` templates (confined to it) |
| `CacheTemplates` | `true` | cache parsed templates by full path and last write time; an edited file is parsed again |
| `AppSettings` | empty | the only values the `AppSetting` filter can return (keys are case-insensitive) |
| `StylesheetRoot` | `null` | OpenXml JSON-workbook stylesheets are loaded only from here (relative: against the application base directory; default: the base directory) |
| `RegexTimeout` | 200 ms | time-out of regular expressions run by template filters (`GetMatch`, JSONPath, `Replace`); keep it at 50 ms or more |
| `MaxXmlCharacters` | 10 000 000 | size cap of XML parsed by `XPathSelect`/`XPathSelectMany` and stylesheets (DTDs are always rejected); 0 = unlimited |
| `MaxIterations` | 100 000 | items one `for` loop may iterate (counted per loop, not per render); 0 = unlimited |
| `RenderTimeout` | 30 s | wall-clock limit of one render; `Timeout.InfiniteTimeSpan` = none |
| `MaxOutputCharacters` | 50 000 000 | the longest string a render may produce (output, `capture` block, result of a growing filter); more throws `RenderLimitExceededException`; 0 = unlimited |
| `MaxDepth` | 64 | nesting depth when a data object becomes template variables (deeper members are null); at least 1 |
| `AllowAbsolutePaths` | `false` | `FullPath` returns absolute paths and messages may name them |

```csharp
services.AddTabTabGoTemplating(configuration);                       // binds the "TabTabGo.Templating" section
services.AddTabTabGoTemplating(o => o.AppSettings["Brand"] = "Acme"); // or in code
```

```json
{ "TabTabGo.Templating": { "CacheTemplates": true, "AppSettings": { "Brand": "Acme" } } }
```

Pass the options to an engine with `new Liquid.TemplatingEngine(options)` or `new Liquid.TemplatingEngine(IOptions<TemplatingOptions>)`; the OpenXml
engine has the same constructors.

The engine constructors validate every numeric option once and throw `ArgumentOutOfRangeException` (`RegexTimeout` and `RenderTimeout` must be
positive or infinite; the counts and sizes must not be negative; `MaxDepth` at least 1). Changing the options object afterwards is not re-validated.

**Dependency injection.** The engines have three constructors (`()`, `(TemplatingOptions)`, `(IOptions<TemplatingOptions>)`). `AddTabTabGoTemplating`
registers `IOptions<TemplatingOptions>`. If a host also registers `TemplatingOptions` itself, Microsoft DI reports an ambiguous constructor when it builds an
engine: register the engine with a factory (`sp => new Liquid.TemplatingEngine(sp.GetRequiredService<IOptions<TemplatingOptions>>())`).

## Configuration section (obsolete)

`TemplatingConfigurationSection` and `Configuration.GetTemplatingConfiguration()` (which reads an optional `appsettings.json` from the application base
directory) are kept for compatibility and marked `[Obsolete]`; use `AddTabTabGoTemplating`.
