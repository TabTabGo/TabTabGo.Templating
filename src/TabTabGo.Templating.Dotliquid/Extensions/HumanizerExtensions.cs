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
        var shouldCapitalize = culture == "en";
        var fraction = number.GetFraction();
        if (fraction == 0)
            return (shouldCapitalize ? integer.Capitalize() : integer);

        var symbol = cultureInfo.GetSeparatorAsWords();
        var fractionWords = fraction.ToWords(grammaticalGender, cultureInfo);
        fractionWords = shouldCapitalize ? fractionWords.Capitalize() : fractionWords;

        return $"{(shouldCapitalize ? integer.Capitalize() : integer)}{symbol}{fractionWords}";
    }

    private static string Capitalize(this string input)
    {
        var words = input.Split(' ');
        var result = new StringBuilder();

        foreach (var word in words)
        {
            bool isExcluded = word.Equals("and", StringComparison.OrdinalIgnoreCase);

            if (isExcluded)
            {
                result.Append(word);
            }
            else if (word.Contains('-'))
            {
                var subWords = word.Split('-');
                for (int i = 0; i < subWords.Length; i++)
                {
                    string subWord = subWords[i];
                    string capitalizedSubWord = char.ToUpper(subWord[0]) + subWord.Substring(1).ToLower();
                    result.Append(capitalizedSubWord);

                    if (i < subWords.Length - 1)
                    {
                        result.Append('-');
                    }
                }
            }
            else
            {
                string capitalizedWord = char.ToUpper(word[0]) + word.Substring(1).ToLower();
                result.Append(capitalizedWord);
            }

            result.Append(' ');
        }

        return result.ToString().TrimEnd();
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