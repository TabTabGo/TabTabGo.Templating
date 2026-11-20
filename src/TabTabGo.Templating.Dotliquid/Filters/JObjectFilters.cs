using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>Filters that query a JSON token with JSONPath (<c>{{ docJson | JSelectToken: '$.a.b' }}</c>).</summary>
public static class JObjectFilters
{
    // JSONPath filter expressions can contain regular expressions (@.n =~ /.../); Json.NET runs them without a time-out
    // unless told otherwise, so every path evaluation uses the configured RegexTimeout (S-8). A timed-out path matches nothing.
    private static JsonSelectSettings SelectSettings() => new()
    {
        RegexMatchTimeout = RenderScope.CurrentOptions.RegexTimeout,
    };

    /// <summary>Selects a token by property under <paramref name="rootPath"/>.</summary>
    /// <param name="jObject">The JSON.</param>
    /// <param name="property">The property path.</param>
    /// <param name="rootPath">The path prefix, default <c>$.</c>.</param>
    public static JToken? JSelectTokenByProperty(JToken jObject, string property, string rootPath = "$.") =>
        SelectTokenSafe(jObject, rootPath + property);

    /// <summary>Selects one token with a JSONPath expression.</summary>
    /// <param name="jObject">The JSON.</param>
    /// <param name="path">The JSONPath expression.</param>
    public static JToken? JSelectToken(JToken jObject, string path) => SelectTokenSafe(jObject, path);

    /// <summary>Selects the values of a property under <paramref name="rootPath"/>.</summary>
    /// <param name="jObject">The JSON.</param>
    /// <param name="rootPath">The path prefix.</param>
    /// <param name="property">The property path.</param>
    public static IEnumerable<object?> JSelectTokenValuesByProperty(JToken jObject, string rootPath, string property) =>
        SelectValuesSafe(jObject, rootPath + property);

    /// <summary>Selects the values a JSONPath expression matches (arrays themselves are skipped).</summary>
    /// <param name="jObject">The JSON.</param>
    /// <param name="path">The JSONPath expression.</param>
    public static IEnumerable<object?> JSelectTokenValues(JToken jObject, string path) => SelectValuesSafe(jObject, path);

    /// <summary>
    /// Selects one value: the CLR value of a scalar (F-8), the token for an object or array, and null when nothing matches.
    /// </summary>
    /// <param name="jObject">The JSON.</param>
    /// <param name="path">The JSONPath expression.</param>
    public static object? JSelectTokenValue(JToken jObject, string path)
    {
        var token = SelectTokenSafe(jObject, path);
        return token switch
        {
            null => null,
            JValue value => value.Value,
            _ when token.HasValues => token.Value<object>(),
            _ => null,
        };
    }

    /// <summary>Quotes and escapes text as a JSON string literal.</summary>
    /// <param name="input">The text.</param>
    public static string EscapeJsonString(string? input) => JsonConvert.ToString(input);

    /// <summary>Parses JSON text.</summary>
    /// <param name="json">The JSON text.</param>
    public static JObject ConvertToJObject(string json) => JObject.Parse(json);

    private static JToken? SelectTokenSafe(JToken jObject, string path)
    {
        try
        {
            return jObject.SelectToken(path, SelectSettings());
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static List<object?> SelectValuesSafe(JToken jObject, string path)
    {
        try
        {
            return jObject.SelectTokens(path, SelectSettings()).Where(j => j.Type != JTokenType.Array).Select(j => (object?)j.Value<object>()).ToList();
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return new List<object?>();
        }
    }
}
