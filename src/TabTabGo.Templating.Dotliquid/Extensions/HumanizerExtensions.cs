using System.Globalization;
using System.Text;
using Humanizer;

namespace TabTabGo.Templating.Liquid.Extensions;

/// <summary>Number-to-words helpers (English and Arabic) used by the <c>ToWords</c> filter.</summary>
public static class HumanizerExtensions
{
    /// <summary>The word that joins the integer and fraction parts, per language.</summary>
    public static readonly Dictionary<string, string> NumberSeparators = new()
    {
        { "ar", "نقطة" },
        { "en", "points" },
    };

    /// <summary>
    /// Spells a number: the number is rounded to two fraction digits first (1.999 is "Two"), then split into integer and fraction words.
    /// English output is capitalised for every English culture (<c>en</c>, <c>en-US</c>, ...).
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="culture">Culture name.</param>
    /// <param name="grammaticalGender">Gender for languages that need one.</param>
    /// <exception cref="ArgumentOutOfRangeException">The integer part does not fit a 64-bit integer.</exception>
    public static string ToWords(this decimal number, string culture, GrammaticalGender grammaticalGender = GrammaticalGender.Neuter)
    {
        var cultureInfo = CultureInfo.CreateSpecificCulture(culture);
        var rounded = Math.Round(number, 2);
        var truncated = Math.Truncate(rounded);
        if (truncated > long.MaxValue || truncated < long.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "The number is too large to spell.");
        }

        var capitalise = cultureInfo.TwoLetterISOLanguageName == "en";
        var integer = truncated.ToLong().ToWords(grammaticalGender, cultureInfo);
        var fraction = rounded.GetFraction();
        if (fraction == 0)
        {
            return capitalise ? integer.Capitalize() : integer;
        }

        var fractionWords = fraction.ToWords(grammaticalGender, cultureInfo);
        return $"{(capitalise ? integer.Capitalize() : integer)}{cultureInfo.GetSeparatorAsWords()}{(capitalise ? fractionWords.Capitalize() : fractionWords)}";
    }

    /// <summary>Capitalises every word (and hyphenated part) except "and"; empty words are skipped.</summary>
    internal static string Capitalize(this string input)
    {
        var result = new StringBuilder();
        foreach (var word in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            if (word.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(word);
            }
            else
            {
                result.Append(string.Join('-', word.Split('-').Select(CapitaliseWord)));
            }
        }

        return result.ToString();
    }

    private static string CapitaliseWord(string word) => word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();

    /// <summary>The separator word between integer and fraction, surrounded by spaces (<c> points </c>, <c> dot </c>).</summary>
    /// <param name="cultureInfo">The culture.</param>
    public static string GetSeparatorAsWords(this CultureInfo cultureInfo)
    {
        if (NumberSeparators.TryGetValue(cultureInfo.TwoLetterISOLanguageName, out var numberSeparator))
        {
            return $" {numberSeparator} ";
        }

        return cultureInfo.NumberFormat.NumberDecimalSeparator == "." ? " dot " : $"{cultureInfo.NumberFormat.NumberDecimalSeparator} ";
    }

    /// <summary>Truncates a decimal to a 64-bit integer.</summary>
    /// <param name="d">The number.</param>
    public static long ToLong(this decimal d) => decimal.ToInt64(d);

    /// <summary>The fraction digits of a number as an integer (<c>12.345</c> with 2 digits is 35).</summary>
    /// <param name="d">The number.</param>
    /// <param name="digits">Number of fraction digits kept.</param>
    public static long GetFraction(this decimal d, int digits = 2)
    {
        var roundedFraction = Math.Round(Math.Abs(d) % 1, digits);
        return (long)(roundedFraction * (decimal)Math.Pow(10, digits));
    }
}
