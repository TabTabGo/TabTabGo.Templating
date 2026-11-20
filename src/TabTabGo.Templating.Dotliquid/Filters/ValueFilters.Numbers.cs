using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TabTabGo.Templating.Liquid.Extensions;

namespace TabTabGo.Templating.Liquid.Filters
{
    public partial class ValueFilters
    {
        public static double DivideBy(object input, object operand)
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

        public static double Number(string input)
        {
            if (string.IsNullOrEmpty(input))
                return 0.0;

            if (input.StartsWith("."))
            {
                return Number("0" + input);
            }
            else if (input.StartsWith("-."))
            {
                return Number("-0" + input.Replace("-",""));
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

        public static string FormatNumber(double input, string format)
        {
            return input.ToString(format);
        }

        public static string FormatNumber(double input, string format, string culture)
        {
            return input.ToString(format, CultureInfo.CreateSpecificCulture(culture));
        }

        public static string FormatCurrency(double input, string culture)
        {
            return input.ToString("C", CultureInfo.CreateSpecificCulture(culture));
        }

        public static string FormatCurrencyWithCode(double input, string culture, string symbol)
        {
            var cultureInfo = CultureInfo.CreateSpecificCulture(culture);
            cultureInfo.NumberFormat.CurrencySymbol = "";
            return input.ToString("C", cultureInfo) + " " + symbol;
        }

        public static string MarkNumbers(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string pattern = @"([\.\$£€,+-_\(\)]*\d+)";
            string output = input;
            int offset = 0;

            foreach (Match m in Regex.Matches(input, pattern))
            {
                output = output.Insert(m.Index + offset, "&lrm;");
                offset += "&lrm;".Length;
            }

            return output;
        }
        public static string ToWords(decimal input, string culture = "en")
        {
            return input.ToWords(culture: culture);
        }
    }
}
