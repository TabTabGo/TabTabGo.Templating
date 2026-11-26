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
