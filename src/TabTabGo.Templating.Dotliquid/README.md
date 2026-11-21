# TabTabGo.Templating.Dotliquid

Liquid templates for text, e-mail and HTML, rendered with [DotLiquid](https://github.com/dotliquid/dotliquid) 2.3.

## Getting started

```csharp
var engine = new TabTabGo.Templating.Liquid.TemplatingEngine();

// from a file (parsed once and cached by full path and last write time)
string html = (string)engine.Render("Templates/invoice.liquid", new { Customer = "Acme" }, "en-US");

// from a string
string text = engine.RenderString("Hello {{ Customer }}", new { Customer = "Acme" }, "en-US");
```

Property names of plain objects keep their C# casing in templates (`{{ Customer }}`), and JSON keys keep the casing of the JSON
(`{{ data.number }}`). The engine initialises itself on first use (`Initialize()` is optional); one instance is safe to share between
threads and to register as a singleton: every render has its own context, variables, file system and culture.

## Data model

* plain objects (POCO/anonymous), dictionaries and collections;
* `Newtonsoft.Json` `JObject`/`JArray` (directly or deserialised to `dynamic`). A `JObject` property `foo` of a plain object is also available as `fooJson`
  (the original token) for the `JSelect*` filters;
* `null` renders with no variables. A list, an array or a scalar as the root is exposed as `model` (`{% for i in model %}`);
* the engine reads public instance properties only, never calls methods, stops `MaxDepth` levels (64) below the root and handles cycles. Pass DTOs, not
  entity-framework entities with lazy navigation properties.

## Culture

`Render(path, data, culture)` and `RenderString(template, data, culture)` format with that culture: the thread culture is set to it for the render and
restored afterwards, also when the render throws. Parsing is invariant (data is JSON or XML); `Number` and `DivideBy` fall back to the render culture for text
such as `1,5`.

## Includes and layouts

`{% include 'name' %}` and `{% extends 'layout' %}` read `_name.liquid` from the directory of the template that is being rendered. The partial must stay under
that directory: names that are rooted, that climb out of it with `..`, that come from a non-string variable, or that resolve through a symbolic link to a file outside
it fail with `FileSystemException`; messages never contain server paths. The file system belongs to the render, so templates in different folders resolve their own
partials concurrently, and each render starts from its own context: `{% assign %}` and `{% capture %}` values never carry over to another render of the same file.

**Known limitation: the template and stylesheet folders must not be writable by untrusted parties.** .NET cannot tell a named pipe or a device file from an empty regular file before opening it (all report `FileAttributes.Normal` and length 0), and a plain open of a named pipe blocks the render thread, so no managed check can refuse one; no native call is made because none is portable to musl, macOS and Windows alike. Someone who can create a pipe in those folders can already replace the templates themselves. Files are read through a byte-counting cap of 16 MB.

`RenderString` parses on every call. Includes inside a string template resolve (confined) against `TemplatingOptions.TemplatesPath` when it is set; otherwise
DotLiquid falls back to a file system the host assigned to `Template.FileSystem`.

## Limits

Parsing happens before `RenderTimeout` starts. DotLiquid's tokenizer is superlinear in the template size, so the cost grows faster than the size: measured on the final code (noisy laptop, indicative), 100 000 characters parse in 0.1-0.4 s, 262 144 in about 2.4-3.3 s wall for an output-dense template, 500 000 in 2.5-4.7 s and 1 000 000 in about 4-11 s of CPU (the scan and the parser share one token list, so the engine costs the same as `Template.Parse` itself; `MaxTemplateCharacters` caps a `RenderString` template at 262 144 by default). Concurrent limit-size parses are slower than serial ones, so a host that handles untrusted templates should cap how many it parses at once.
**`Render(path, ...)` parses a template file of any size**: files are trusted and deployed by the host, so `MaxTemplateCharacters` does not apply to them. A host that accepts uploaded templates must read them itself and render them with `RenderString`, which `MaxTemplateCharacters` caps.
`MaxNestingDepth` (100) refuses a template that nests blocks deeper, by a linear scan before parsing: DotLiquid's parser recurses once per level, and a few thousand nested
`{% if %}` tags overflow the stack and end the process (this was also true of 8.0.x and stock DotLiquid). The scan runs over DotLiquid's own tokens (so `{{ ... }}` output text and `{%-` tags split as the parser splits them), finds block tags through the registered tag types (custom blocks
count), lowers the depth only for an end tag that closes the innermost open block, and skips `raw` and `comment` bodies (the tokenizer returns each as one text token). As a backstop the
parse itself runs on a thread whose reserved stack is 512 KB plus 8 KB per allowed level (1 312 KB at the default 100, capped at 256 MB), so a template the scan undercounted by a small margin cannot overflow the caller's stack.
Measured on DotLiquid 2.3.260 the parser needs about 0.6 KB per nested level (100 levels fit in 64 KB, 1 000 levels need about 0.6 MB), so the reserve is about 13 times the need; it was a fixed 256 MB per render.
The backstop is not sized for a template the scan undercounts by orders of magnitude, nor for `MaxTemplateCharacters` 0 or very large template files; the scan is the guard. The scan binds to DotLiquid's internal tokenizer, so the package
depends on DotLiquid `[2.3.260, 2.4.0)`; a bump goes through the architect and the nesting fuzz tests. If that internal tokenizer cannot be bound (a changed signature), the engine does not fail: it uses the stock `Template.Parse` and an approximate linear scan, and because that scan can undercount, the parse thread then reserves the 256 MB maximum.

**`MaxNestingDepth` has no upper bound, and it trades against stacks.** The parse stack grows with the limit (8 KB per level) until it reaches the 256 MB cap, which holds to about 32 700 levels with the exact tokenizer. Parse cost grows with it: about 4 to 9 s of CPU at 20 000 nested levels. The render of a deeply nested template runs on the caller's stack, which the engine does not control: nested `capture` blocks at 3 000 levels overflowed a 1 MB caller stack, and DotLiquid's own guard does not cover `capture`. Keep the default unless the host renders on large-stack threads. A refused template throws `DotLiquid.Exceptions.SyntaxException`.

**`ParseTimeout` only reaches regular expressions DotLiquid creates after the engine has been created** (DotLiquid builds some parse expressions once, at first use, with the process-wide value current then). A host that calls `DotLiquid.Template.Parse` before it creates a `TemplatingEngine` keeps DotLiquid's 10 s default for those expressions (with `ParseTimeout` of 1 ms, a 700 000-character template still rendered in 279 ms instead of timing out). Create the engine before any other DotLiquid use.

**Hosts that render untrusted templates** should lower `RenderTimeout`, cap the number of concurrent renders and set `MaxTemplateCharacters` to what they need: one adversarial
render can hold one CPU core for up to `RenderTimeout` and produce about 100 MB of output (twice `MaxOutputCharacters` in the worst case of output plus one growing string).

`MaxIterations`, `RenderTimeout`, `MaxOutputCharacters` and `MaxDepth` (see the options table of `TabTabGo.Templating`) bound a render. A render, a `capture` block or the
result of `Append`, `Prepend`, `Join`, `Replace`, `ReplaceFirst`, `RemoveFirst`, `Plus`, `Times`, `Concat` longer than `MaxOutputCharacters` throws `RenderLimitExceededException` (a
`TimeoutException`, which DotLiquid lets escape instead of printing "Liquid error"); a timed-out render and regular-expression time-outs in `Replace` propagate the same way.
Callers should handle them like any render failure. Three exception types: `RenderLimitExceededException` (output limit; a `TimeoutException`, as are regular-expression
time-outs), `DotLiquid.Exceptions.MaximumIterationsExceededException` (`MaxIterations`, counted per `for` loop; a `RenderException`, not a `TimeoutException`) and
`OperationCanceledException` (`RenderTimeout`). Catching `TimeoutException` alone does not catch the last two.

## Filters

Filters are called with the C# member name: `{{ value | FormatNumber: 'N2', 'en-US' }}`. Each example below is pinned by a golden case under
`test/TabTabGo.Templating.DotLiquid.Tests/Golden/Liquid` or by a unit test.

| Filter | Example | Output |
|---|---|---|
| `FormatNumber: format [, culture]` | `{{ 1234.5 \| FormatNumber: 'N2', 'en-US' }}` | `1,234.50` (exact for `decimal` data) |
| `FormatCurrency: culture` | `{{ 1234.5 \| FormatCurrency: 'en-US' }}` | `$1,234.50` |
| `FormatCurrencyWithCode: culture, code` | `{{ 1234.5 \| FormatCurrencyWithCode: 'en-US', 'AED' }}` | `1,234.50 AED` |
| `FormatDate: format [, culture]` | `{{ d \| FormatDate: 'dddd d MMMM yyyy', 'en-US' }}` (2026-01-15T10:30Z) | `Thursday 15 January 2026`; with `'ar-AE'`: `الخميس 15 يناير 2026` |
| `OffsetDate: seconds` | `{{ d \| OffsetDate: 14400 \| FormatDate: 'yyyy-MM-dd HH:mm zzz', 'en-US' }}` | `2026-01-15 14:30 +04:00` |
| `ToTimeZone: zone` | `{{ d \| ToTimeZone: 'Asia/Dubai' \| FormatDate: 'HH:mm zzz', 'en-US' }}` | `14:30 +04:00` (IANA or Windows id; daylight saving applies) |
| `ParseDate: [format]` | `{{ '15/01/2026' \| ParseDate: 'dd/MM/yyyy' }}` | a date-time |
| `ToWords: language` | `{{ 21 \| ToWords: 'en' }}` / `{{ 21 \| ToWords: 'ar' }}` | `Twenty-One` / `واحد و عشرون`; `1.999` is `Two`, `1234.56` is `One Thousand Two Hundred and Thirty-Four points Fifty-Six` |
| `MarkNumbers` | `{{ 'ABC123' \| MarkNumbers }}` | `ABC&lrm;123` |
| `Number`, `DivideBy: n` | `{{ '1.5' \| Number }}`, `{{ 10 \| DivideBy: 4 }}`, `{{ 10 \| DivideBy: 0 }}` | `1.5`, `2.5`, `0` |
| `CountryName [: culture]` | `{{ 'AE' \| CountryName }}` | `United Arab Emirates` (an unknown code is returned unchanged; `'ar'` gives the native name) |
| `Strip`, `Lstrip`, `Rstrip`, `SliceEnd: n`, `Contains: v`, `GetMatch: regex` | `{{ 'order-1234-x' \| GetMatch: '[0-9]+' }}` | `1234` |
| `Union: other` | `{{ a \| Union: b }}` with `[1,2,3]`, `[3,4]` | `1,2,3,4`; an empty second list returns the first |
| `Distinct` | `{{ ints \| Distinct }}` with `[1,1,2]` | `1,2` (hashes by content) |
| `OrderBy: key` | `{{ items \| OrderBy: 'n' }}` | stable, numbers by value |
| `GroupBy: key` | `{{ items \| GroupBy: 'k' }}` | groups with `Key` and `Items` (`Key` is empty for a missing value) |
| `Flatten: path`, `SelectMany: key`, `RemoveEmpty` | `{{ orders \| Flatten: 'lines' }}` | the nested items |
| `Filter: expr`, `FilterBy: key, value`, `FilterByPrefix: key, prefix` | `{{ items \| FilterBy: 'k', 'a' }}` | the matching items |
| `JSelectToken`, `JSelectTokenValue`, `JSelectTokenValues`, `*ByProperty`, `ConvertToJObject`, `EscapeJsonString` | `{{ j \| JSelectTokenValue: '$.s' }}` | the scalar value (objects and arrays return the token) |
| `XPathSelect: xpath`, `XPathSelectMany: xpath` | `{{ xml \| XPathSelect: '/r/a' }}` | the node value |
| `AppSetting`, `FullPath` | `{{ 'Brand' \| AppSetting }}`, `{{ 'images/logo.png' \| FullPath }}` | the configured value or empty; a path relative to the template folder |
| `OpenXmlType`, `OpenXmlFormat`, `OpenXmlMaxWidth`, `OpenXmlToString` | `{{ 'System.Int32' \| OpenXmlType }}` | `Number` (also `Boolean`, `Date`, `String`) |

### `Filter` (dynamic LINQ) and `FilterBy`

`{{ items | Filter: 'it["status"].ToString() = "active"' }}` evaluates a [System.Linq.Dynamic.Core](https://dynamic-linq.net/) 1.7 expression over the items (`it`).
`ToString()`/`Equals()` on `object` are allowed so existing expression shapes keep working (`it["k"].ToString() = "v"`, `.StartsWith("v")`, `== null`, `!= null`,
`.Contains(...) and ...`); reflection over other types is blocked. Do not build expressions from untrusted data. Prefer `FilterBy: 'status', 'active'` (compared as text)
and `FilterByPrefix: 'name', 'Al'` (ordinal): no expression is parsed. A `Filter` expression runs outside the output limit, so the parsed expression tree is checked against an allow-list before it runs: `ToString()` (no arguments), `Equals`, string comparison and search methods (`StartsWith`, `EndsWith`, `Contains`, `IndexOf`, `Substring`, `ToLower`, `ToUpper`, `Trim`, `CompareTo`, the char indexer), `string.Compare` (string `<` `>` `<=` `>=`), `string.IsNullOrEmpty`, `string.IsNullOrWhiteSpace`, `Math.Abs/Min/Max/Floor/Ceiling/Round/Sign`, `Convert.ToInt32/ToInt64/ToDecimal/ToDouble/ToBoolean`, the hash indexer, the `in` operator and the usual operators. Also refused: `CompareTo` with a non-string argument and a `StringComparison` given as a number (a cast `int`). Anything else (`PadLeft`, `PadRight`, `Replace`, `Insert`, `Concat`, `Join`, `Format`, string `+`, constructors) throws `ArgumentException`, and a number given to a string method must be a constant of at most 10 000. The text is limited to 1 000 characters, 64 levels of parentheses, signs and `not`, and 32 string, `Math` and `Convert` calls; parsing runs on its own 16 MB-stack thread, except for criteria of at most 64 characters and 8 levels without `[`, `{`, `new` or `=>` when at least 256 KB of caller stack remain; compiled predicates are cached by text (256 entries), so a criteria with `it["k"]` pays the thread only on its first use. Search and comparison are ordinal (`IndexOf`, `StartsWith`, `EndsWith`, `CompareTo`, `Compare`), and the render time-out is checked between rows. `Filter`'s `.StartsWith` is ordinal, like `FilterByPrefix`.

### `AppSetting`, `GetMatch`, `XPathSelect`

* `{{ 'Brand' | AppSetting }}` returns only keys in `TemplatingOptions.AppSettings`; anything else is empty. Host configuration files are never read.
* `GetMatch` and the JSONPath filters (regex in `[?(@.n =~ /.../)]`) stop after `RegexTimeout` and degrade per expression (empty result).
* `Replace`/`ReplaceFirst`/`RemoveFirst` (regex syntax levels) time out at the engine's `RegexTimeout` and throw `RegexMatchTimeoutException` out of the render. DotLiquid's own template-parse
  expressions run under `ParseTimeout` (5 s) instead: it lowers DotLiquid's process-wide 10 s `Template.RegexTimeOut` (strictest engine wins), so a tight `RegexTimeout` cannot fail a valid parse
  and an untrusted template string cannot spend 10 s per expression; `RenderString` also refuses a template longer than `MaxTemplateCharacters`.
* `MaxIterations` counts the items of **one** `for` loop, not the iterations of a render: nested loops multiply (400 x 400 passes the default). `RenderTimeout` (30 s) is the bound
  for nested loops; size the template data, not the limit, to keep a render short.
* `XPathSelect`/`XPathSelectMany` reject XML with a `<!DOCTYPE>` (rendered as `Liquid error`) and documents larger than `MaxXmlCharacters`.

### Custom filters

`engine.RegisterFilter(typeof(MyFilters))` registers a static class of filters. Registration is process-wide: do it once at start-up.

## Tags

`{% increment name %}` adds 1 to the integer variable `name` (a missing variable starts at 1) without output. `{% capture %}` is DotLiquid's tag with a bounded buffer.
