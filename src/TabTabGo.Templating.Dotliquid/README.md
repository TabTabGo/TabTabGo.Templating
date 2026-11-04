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
rendered. The partial must stay under that directory (name pattern `^[.\/]*[a-zA-Z0-9_\/]+$`). Section 8.0.4 hardens this
confinement.

## Filters

Filters are called with the C# member name: `{{ value | FormatNumber: 'C' }}`.

| Group | Filters |
|---|---|
| Collections | `SelectMany`, `XPathSelectMany`, `RemoveEmpty`, `Union`, `Filter`, `Flatten`, `Distinct`, `OrderBy`, `GroupBy` |
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

### Custom filters

`engine.RegisterFilter(typeof(MyFilters))` registers a static class of filters. Registration is process-wide: do it
once at start-up.

## Tags

`{% increment name %}` adds 1 to the integer variable `name` in the outermost scope. The variable must already exist
(a missing variable fails on the v8 line, bug F-12).

## Limits and known issues on the v8 line

* **Shared parsed templates (fixed in 8.0.4).** `Render(path, ...)` caches the parsed template and renders from that shared
  instance. A value set with `{% assign %}` can therefore survive into the next render of the same file, and concurrent
  renders can see each other's assigned values. Until you move to 8.0.4, do not read a variable before assigning it in a
  template, and do not render one template file from several threads at once.

The 8.0.4 release addresses the security rows (S-1..S-5, B-1, B-2). The remaining filter and culture fixes ship on v10.
See the specification in `tabtabgo/docs/templating-modernization/spec.md` section 6.
