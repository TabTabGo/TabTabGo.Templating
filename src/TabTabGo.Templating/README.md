# TabTabGo.Templating

The abstract contract shared by the TabTabGo templating engines. It has no template-engine dependency.

```csharp
public abstract class TemplatingEngine
{
    public abstract string EngineName { get; }
    public abstract bool IsCached(string templatePath);
    public abstract void ParseTemplate(string templatePath);
    public abstract object Render(string templatePath, object sourceData, string culture = "en-Us");
    public abstract string RenderString(string templateString, object sourceData, string culture = "en-Us");
    public abstract void RegisterFilter(Type type);
}
```

Engines: [`TabTabGo.Templating.Dotliquid`](../TabTabGo.Templating.Dotliquid/README.md) and
[`TabTabGo.Templating.OpenXml`](../TabTabGo.Templating.OpenXml/README.md).

## Configuration section

`TemplatingConfigurationSection` binds the `TabTabGo.Templating` section:

```json
{ "TabTabGo.Templating": { "TemplatesPath": "Templates" } }
```

`Configuration.GetTemplatingConfiguration()` is meant to read that section from `appsettings.json`. **Known issue on
the v8 line (F-16):** it opens a file named `appsettigs.json` (typo) and throws `FileNotFoundException`; it is fixed on
v10. Until then bind the section yourself with `IConfiguration.GetSection("TabTabGo.Templating").Bind(...)`.

## TemplatingOptions (8.0.4)

| Option | Default | Meaning |
|---|---|---|
| `TemplatesPath` | `null` | base path of the templates |
| `CacheTemplates` | `true` | cache parsed templates by full path and last write time; an edited file is parsed again |
| `AppSettings` | empty | the only values the `AppSetting` filter can return (keys are case-insensitive) |
| `RegexTimeout` | 200 ms | time-out of regular expressions run by template filters (`GetMatch`) |
| `StylesheetRoot` | `null` | OpenXml JSON-workbook stylesheets are loaded only from here (relative: against the application base directory; default: the base directory) |
| `MaxXmlCharacters` | 10 000 000 | size cap of XML parsed by `XPathSelect`/`XPathSelectMany` (DTDs are always rejected) |

```csharp
services.AddTabTabGoTemplating(configuration);                       // binds the "TabTabGo.Templating" section
services.AddTabTabGoTemplating(o => o.AppSettings["Brand"] = "Acme"); // or in code
```

```json
{ "TabTabGo.Templating": { "CacheTemplates": true, "AppSettings": { "Brand": "Acme" } } }
```

Pass the options to an engine with `new Liquid.TemplatingEngine(options)` or `new Liquid.TemplatingEngine(IOptions<TemplatingOptions>)`
(the OpenXml engine has the same two constructors).

`TemplatingOptions` is validated by the engine constructors (both engines check `MaxXmlCharacters`; only the Liquid engine checks `RegexTimeout`): `RegexTimeout` must be positive (or `Timeout.InfiniteTimeSpan`) and
`MaxXmlCharacters` must not be negative (0 means unlimited). Validation happens once, at construction: changing a property of the options object afterwards is not re-validated.

**Dependency injection.** The engines have three constructors (`()`, `(TemplatingOptions)`, `(IOptions<TemplatingOptions>)`).
`AddTabTabGoTemplating` registers `IOptions<TemplatingOptions>`. If a host also registers `TemplatingOptions` itself,
Microsoft DI reports an ambiguous constructor when it builds an engine: register the engine with a factory
(`sp => new Liquid.TemplatingEngine(sp.GetRequiredService<IOptions<TemplatingOptions>>())`).
