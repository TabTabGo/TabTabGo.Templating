using System.Text.RegularExpressions;

namespace TabTabGo.Templating.Liquid.Filters;

public static partial class ValueFilters
{
    /// <summary>Returns the last <paramref name="len"/> characters of the text (the whole text when it is shorter).</summary>
    /// <param name="input">The text.</param>
    /// <param name="len">Number of characters to keep.</param>
    public static string? SliceEnd(string? input, int len) => ReverseSlice(input, len);

    /// <summary>Returns the last <paramref name="len"/> characters of the text (the whole text when it is shorter); null for a length of 0 or less.</summary>
    /// <param name="input">The text.</param>
    /// <param name="len">Number of characters to keep.</param>
    public static string? ReverseSlice(string? input, int len)
    {
        if (input == null || len <= 0)
        {
            return null;
        }

        return len >= input.Length ? input : input.Substring(input.Length - len, len);
    }

    /// <summary>Strips all whitespace around the text.</summary>
    /// <param name="input">The text.</param>
    public static string? Strip(string? input) => input?.Trim();

    /// <summary>Strips all leading whitespace.</summary>
    /// <param name="input">The text.</param>
    public static string? Lstrip(string? input) => input?.TrimStart();

    /// <summary>Strips all trailing whitespace.</summary>
    /// <param name="input">The text.</param>
    public static string? Rstrip(string? input) => input?.TrimEnd();

    /// <summary>
    /// Returns the first match of a regular expression, or an empty string when nothing matches or the match runs past
    /// <see cref="TemplatingOptions.RegexTimeout"/> (S-4).
    /// </summary>
    /// <param name="input">The text to search.</param>
    /// <param name="pattern">The regular expression.</param>
    public static string? GetMatch(string? input, string pattern)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        try
        {
            // Template-supplied patterns run with a time-out so catastrophic backtracking cannot stall a render.
            return Regex.Match(input, pattern, RegexOptions.Multiline, RenderScope.CurrentOptions.RegexTimeout).Value;
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }
}
