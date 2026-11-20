using System.Xml.XPath;
using DotLiquid;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>Value, string, number and date filters. Filters are called with the member name: <c>{{ value | FormatNumber: 'C' }}</c>.</summary>
public static partial class ValueFilters
{
    /// <summary>
    /// Returns a setting the host put into <see cref="TemplatingOptions.AppSettings"/>; unknown keys give an empty string (S-1).
    /// Templates never read the host's configuration or any file.
    /// </summary>
    /// <param name="key">The setting key (case-insensitive).</param>
    public static string? AppSetting(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        return RenderScope.CurrentOptions.AppSettings.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Resolves a path against the folder of the template being rendered. Without <see cref="TemplatingOptions.AllowAbsolutePaths"/> the
    /// result is relative to that folder, so the output never names the server's folders (S-6, S-12).
    /// </summary>
    /// <param name="input">The path as written in the template.</param>
    public static string FullPath(string? input)
    {
        var fileSystem = (object?)RenderScope.Current?.FileSystem ?? Template.FileSystem;
        var root = Path.GetFullPath(".");
        if (fileSystem.GetType().GetProperty("Root")?.GetValue(fileSystem) is string { Length: > 0 } configured)
        {
            root = Path.GetFullPath(configured);
        }

        var absolute = string.IsNullOrEmpty(input) ? root : Path.GetFullPath(Path.Combine(root, input));
        if (RenderScope.CurrentOptions.AllowAbsolutePaths)
        {
            return absolute;
        }

        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(root, absolute);
        var endsWithSeparator = input.EndsWith('/') || input.EndsWith(Path.DirectorySeparatorChar);
        return endsWithSeparator && !relative.EndsWith(Path.DirectorySeparatorChar) ? relative + Path.DirectorySeparatorChar : relative;
    }

    /// <summary>Whether the text contains the value (false for empty text).</summary>
    /// <param name="input">The text.</param>
    /// <param name="value">The value to look for.</param>
    public static bool Contains(string? input, string value) => !string.IsNullOrWhiteSpace(input) && input.Contains(value);

    /// <summary>Converts a value to text.</summary>
    /// <param name="input">The value.</param>
    public static string? ToString(object? input) => input?.ToString();

    /// <summary>Reads a member of a hash; null when it is missing.</summary>
    /// <param name="input">The hash.</param>
    /// <param name="property">The member name.</param>
    public static object? Select(Hash? input, string? property)
    {
        if (string.IsNullOrEmpty(property))
        {
            return null;
        }

        return input == null || !input.ContainsKey(property) ? null : input[property];
    }

    /// <summary>Returns the value of the first node an XPath expression selects in XML text (DTDs are rejected, S-5).</summary>
    /// <param name="xmlInput">The XML text.</param>
    /// <param name="xPath">The XPath expression.</param>
    public static string? XPathSelect(string? xmlInput, string xPath)
    {
        if (string.IsNullOrEmpty(xmlInput))
        {
            return xmlInput;
        }

        var navigator = SafeXml.Parse(xmlInput).CreateNavigator();
        return navigator.SelectSingleNode(xPath)?.Value;
    }
}
