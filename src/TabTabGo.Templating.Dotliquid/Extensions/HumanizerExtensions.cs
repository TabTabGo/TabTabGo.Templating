using Humanizer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Templating.Liquid.Extensions;
public static class HumanizerExtensions
{
    public static readonly Dictionary<string, string> NumberSeparators = new Dictionary<string, string>()
    {
        {"ar", "نقطة"},
        {"en", "points"}
    };

    public static string ToWords(this decimal number, string culture, GrammaticalGender grammaticalGender = GrammaticalGender.Neuter)
    {
        var cultureInfo = CultureInfo.CreateSpecificCulture(culture);
        var integer = Math.Truncate(number).ToLong().ToWords(grammaticalGender, cultureInfo);
        var fraction = number.GetFraction();
        if (fraction == 0)
            return integer;
        var symbol = cultureInfo.GetSeparatorAsWords();
        return $"{integer}{symbol}{fraction.ToWords(grammaticalGender, cultureInfo)}";
    }
    public static string GetSeparatorAsWords(this CultureInfo cultureInfo)
    {
        if (NumberSeparators.TryGetValue(cultureInfo.TwoLetterISOLanguageName, out var numberSeparator))
            return $" {numberSeparator} ";

        if (cultureInfo.NumberFormat.NumberDecimalSeparator == ".")
            return " dot ";

        return $"{cultureInfo.NumberFormat.NumberDecimalSeparator} ";
    }

    public static long ToLong(this decimal d) => Decimal.ToInt64(d);
    public static long GetFraction(this decimal d, int digits = 2)
    {
        decimal roundedFraction = Math.Round(Math.Abs(d) % 1, digits);
        long fraction = (long)(roundedFraction * (decimal)Math.Pow(10, digits));
        return fraction;
    }
}