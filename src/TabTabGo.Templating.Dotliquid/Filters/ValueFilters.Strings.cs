using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static partial class ValueFilters
    {
        public static string SliceEnd(string input, int len)
        {
            return ReverseSlice(input, len);
        }

        public static string ReverseSlice(string input, int len)
        {
            if (input == null || len <= 0)
                return null;

            if (len >= input.Length)
            {
                return input;
            }

            return input.Substring(input.Length - len, len);
        }

        /// <summary>
        /// Strip all whitespace from input
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string Strip(string input)
        {
            return input?.Trim();
        }

        /// <summary>
        /// Strip all leading whitespace from input
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string Lstrip(string input)
        {
            return input?.TrimStart();
        }

        /// <summary>
        /// Strip all trailing whitespace from input
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string Rstrip(string input)
        {
            return input?.TrimEnd();
        }

        public static string GetMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            RegexOptions options = RegexOptions.Multiline;

            Match m = Regex.Match(input, pattern, options);

            return m == null ? string.Empty : m.Value;
        }
    }
}
