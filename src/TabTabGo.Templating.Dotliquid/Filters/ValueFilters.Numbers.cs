using System.Globalization;
using System.Text.RegularExpressions;
using TabTabGo.Templating.Liquid.Extensions;

namespace TabTabGo.Templating.Liquid.Filters;

public static partial class ValueFilters
{
    // Currency and punctuation prefixes stay attached to their number ("(12", "$5", "+971"); the old class "+-_" was a range that also
    // swallowed digits and letters (F-1). A number with separators ("1,234.50") is one unit.
    private static readonly Regex NumberPattern = new(@"[\.\$£€,+\-_\(\)]*\d+(?:[.,]\d+)*", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Divides <paramref name="input"/> by <paramref name="operand"/>; anything that is not a number, and division by zero, give 0.</summary>
    /// <param name="input">The dividend.</param>
    /// <param name="operand">The divisor.</param>
    public static double DivideBy(object? input, object? operand)
    {
        if (input == null || operand == null)
        {
            return 0;
        }

        if (TryParseDouble(input.ToString(), out var leftSide) && TryParseDouble(operand.ToString(), out var rightSide) && rightSide != 0)
        {
            return leftSide / rightSide;
        }

        return 0;
    }

    /// <summary>Parses a number from text; text that is not a number gives 0.</summary>
    /// <param name="input">The text.</param>
    public static double Number(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return 0.0;
        }

        if (input.StartsWith('.'))
        {
            return Number("0" + input);
        }

        if (input.StartsWith("-."))
        {
            return Number("-0" + input.Replace("-", string.Empty));
        }

        return TryParseDouble(input.Trim(), out var value) ? value : 0.0;
    }

    /// <summary>
    /// Data is JSON or XML, so numbers are parsed invariantly (F-11). Only when that fails does the render culture (the thread
    /// culture during a render) get a chance, for text such as <c>1,5</c> typed by a user.
    /// </summary>
    private static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>Formats an exact decimal with the render culture (no binary rounding, F-15).</summary>
    /// <param name="input">The number.</param>
    /// <param name="format">A .NET numeric format string.</param>
    public static string FormatNumber(decimal input, string format) => input.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Formats an exact decimal with the given culture (no binary rounding, F-15).</summary>
    /// <param name="input">The number.</param>
    /// <param name="format">A .NET numeric format string.</param>
    /// <param name="culture">Culture name, for example <c>ar-AE</c>.</param>
    public static string FormatNumber(decimal input, string format, string culture) =>
        input.ToString(format, CultureInfo.CreateSpecificCulture(culture));

    /// <summary>Formats a number with the render culture.</summary>
    /// <param name="input">The number.</param>
    /// <param name="format">A .NET numeric format string.</param>
    public static string FormatNumber(double input, string format) => input.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Formats a number with the given culture.</summary>
    /// <param name="input">The number.</param>
    /// <param name="format">A .NET numeric format string.</param>
    /// <param name="culture">Culture name, for example <c>ar-AE</c>.</param>
    public static string FormatNumber(double input, string format, string culture) =>
        input.ToString(format, CultureInfo.CreateSpecificCulture(culture));

    /// <summary>Formats a number as currency in the given culture.</summary>
    /// <param name="input">The amount.</param>
    /// <param name="culture">Culture name.</param>
    public static string FormatCurrency(double input, string culture) =>
        input.ToString("C", CultureInfo.CreateSpecificCulture(culture));

    /// <summary>Formats a number as currency in the given culture, replacing the currency symbol with <paramref name="symbol"/> after the amount.</summary>
    /// <param name="input">The amount.</param>
    /// <param name="culture">Culture name.</param>
    /// <param name="symbol">Text placed after the amount (for example a currency code).</param>
    public static string FormatCurrencyWithCode(double input, string culture, string symbol)
    {
        var cultureInfo = CultureInfo.CreateSpecificCulture(culture);
        cultureInfo.NumberFormat.CurrencySymbol = string.Empty;
        return input.ToString("C", cultureInfo) + " " + symbol;
    }

    /// <summary>Puts a left-to-right mark before every number so bidirectional text keeps numbers in order.</summary>
    /// <param name="input">The text.</param>
    public static string? MarkNumbers(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return NumberPattern.Replace(input, "&lrm;$0");
    }

    /// <summary>Spells a number in words in the given language (<c>en</c> or <c>ar</c>).</summary>
    /// <param name="input">The number; its fraction is rounded to two digits.</param>
    /// <param name="culture">Culture name, default <c>en</c>.</param>
    public static string ToWords(decimal input, string culture = "en") => input.ToWords(culture: culture);
}
