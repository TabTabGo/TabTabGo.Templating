# TabTabGo.Templating.Dotliquid

Liquid templates for text, e-mail and HTML, rendered with [DotLiquid](https://github.com/dotliquid/dotliquid) 2.3.

## Getting started

```csharp
var engine = new TabTabGo.Templating.Liquid.TemplatingEngine();

// from a file (parsed once and cached by path)
string html = (string)engine.Render("Templates/invoice.liquid", new { Customer = "Acme" }, "en-US");

// from a string
string text = engine.RenderString("Hello {{ customer }}", new { Customer = "Acme" }, "en-US");
```

Property names of plain objects keep their C# casing in templates (`{{ Customer }}`), and JSON keys keep the casing of
the JSON (`{{ data.number }}`), as in the templates of the consuming repositories.

Call `engine.Initialize()` once at start-up (`Render` does it for you). `RenderString` does **not** initialise the
engine on the v8 line (B-4): if it is the first call in the process, custom filters are missing until `Initialize()`
or `Render` has run.

## Data model

* plain objects (POCO/anonymous), dictionaries and collections;
* `Newtonsoft.Json` `JObject`/`JArray` (deserialised to `dynamic` or passed directly). A `JObject` property `foo` is
  also available as `fooJson` (the original token) for the `JSelect*` filters;
* pass DTOs, not entity-framework entities with lazy navigation properties: the engine walks public properties.

## Culture

`Render(path, data, culture)` and `RenderString(template, data, culture)` build the DotLiquid `RenderParameters` with
that culture. On the v8 line the culture-sensitive filters (`FormatNumber`, `FormatCurrency`, ...) take their culture from
the **thread** unless the template passes a culture argument (bug B-6, fixed on v10).

## Includes and layouts

`{% include 'name' %}` and `{% extends 'layout' %}` read `_name.liquid` from the directory of the template that is being
rendered. The partial must stay under that directory: names that are rooted, that climb out of it with `..`, or that come
from a non-string variable fail with `FileSystemException`. The file system belongs to the render (not to a process-wide
static), so templates in different folders can render in parallel.

## Filters

Filters are called with the C# member name: `{{ value | FormatNumber: 'C' }}`.

| Group | Filters |
|---|---|
| Collections | `SelectMany`, `XPathSelectMany`, `RemoveEmpty`, `Union`, `Filter`, `FilterBy`, `FilterByPrefix`, `Flatten`, `Distinct`, `OrderBy`, `GroupBy` |
| Values and strings | `Contains`, `ToString`, `Select`, `XPathSelect`, `SliceEnd`, `ReverseSlice`, `Strip`, `Lstrip`, `Rstrip`, `GetMatch`, `FullPath`, `AppSetting` |
| Numbers | `DivideBy`, `Number`, `FormatNumber`, `FormatCurrency`, `FormatCurrencyWithCode`, `MarkNumbers`, `ToWords` |
| Dates | `ToTimeZone`, `OffsetDate`, `FormatDate`, `ParseDate` |
| JSON | `JSelectToken`, `JSelectTokenByProperty`, `JSelectTokenValues`, `JSelectTokenValuesByProperty`, `JSelectTokenValue`, `EscapeJsonString` |
| Other | `CountryName`, and the helpers `OpenXmlToString`, `OpenXmlType`, `OpenXmlFormat`, `OpenXmlMaxWidth` |

Examples (each one is covered by a test in `test/TabTabGo.Templating.DotLiquid.Tests`):

```liquid
{{ data.number | FormatNumber: 'C' }}                {% comment %} number:$12,345.00 under en-US {% endcomment %}
{{ data.items | GroupBy: 'type' }}
{{ data.file | FullPath }}
```

### `Filter` (dynamic LINQ)

`{{ items | Filter: 'it["status"].ToString() = "active"' }}` evaluates a
[System.Linq.Dynamic.Core](https://dynamic-linq.net/) 1.7 expression over the items (`it`). The engine configures
`AllowEqualsAndToStringMethodsOnObject = true` so the existing expression shapes keep working
(`it["k"].ToString() = "v"`, `.StartsWith("v")`, `== null`, `!= null`, `.Contains(...) and ...`). Reflection over
types outside the predefined set is blocked by the library. Do not build expressions from untrusted data.

### `FilterBy` and `FilterByPrefix`

Prefer these to `Filter` when the value comes from data: `{{ items | FilterBy: 'status', 'active' }}` keeps the items whose
`status` equals `active` (compared as text), `{{ items | FilterByPrefix: 'name', 'Al' }}` keeps those starting with `Al`
(ordinal). No expression is parsed.

### `AppSetting`, `GetMatch`, `XPathSelect`

* `{{ 'Brand' | AppSetting }}` returns only keys in `TemplatingOptions.AppSettings`; anything else is an empty string.
  Host configuration files are never read.
* `GetMatch` stops after `TemplatingOptions.RegexTimeout` and returns an empty string.
* `XPathSelect`/`XPathSelectMany` reject XML with a DTD and documents larger than `MaxXmlCharacters`.

### Custom filters

`engine.RegisterFilter(typeof(MyFilters))` registers a static class of filters. Registration is process-wide: do it
once at start-up.

## Tags

`{% increment name %}` adds 1 to the integer variable `name` in the outermost scope. The variable must already exist
(a missing variable fails on the v8 line, bug F-12).

## Known issues on the v8 line

Fixed on v10, not on v8: filter, culture and typing bugs F-1..F-17, the per-instance hash cache (B-3: do not share one
engine instance across threads on v8; create one per render or serialise), and `RenderString` not initialising the engine
(B-4). The 8.0.4 release closes S-1..S-5, B-1 and B-2. See `tabtabgo/docs/templating-modernization/spec.md` section 6.
