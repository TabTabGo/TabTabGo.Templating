using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotLiquid;
using System.Globalization;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static partial class ValueFilters
    {
        public static object ToTimeZone(object input, string timeZoneName)
        {
            if (string.IsNullOrEmpty(timeZoneName))
            {
                return input;
            }

            //https://msdn.microsoft.com/en-us/library/gg154758.aspx useful list of timezone ids and names
            var timeZone = TimeZoneInfo.GetSystemTimeZones().Where(a => a.StandardName == timeZoneName).FirstOrDefault();

            if (timeZone == null)
            {
                return input;
            }

            return OffsetDate(input, (int)timeZone.BaseUtcOffset.TotalSeconds);
        }

        public static DateTimeOffset? OffsetDate(object input, int offset)
        {
            if (input is DateTimeOffset)
            {
                return ((DateTimeOffset)input).ToUniversalTime().ToOffset(TimeSpan.FromSeconds(offset));
            }

            if (input is DateTime)
            {
                return (new DateTimeOffset((DateTime)input)).ToUniversalTime().ToOffset(TimeSpan.FromSeconds(offset));
            }

            if (input is string)
            {
                DateTimeOffset date;

                if (DateTimeOffset.TryParse((string)input, out date))
                {
                    return date.ToUniversalTime().ToOffset(TimeSpan.FromSeconds(offset));
                }
                else
                {
                    if (((string)input).Equals("Now", StringComparison.CurrentCultureIgnoreCase) || ((string)input).Equals("Today", StringComparison.CurrentCultureIgnoreCase))
                    {
                        return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromSeconds(offset));
                    }
                }
            }

            if (input == null)
            {
                return null;
            }

            throw new Exception("Object must be either a DateTime or a dateTimeOffset in order to offset it");
        }

        public static string FormatDate(object dateObject, string format, string locale = null)
        {
            if (dateObject == null)
            {
                return null;
            }

            if (dateObject is string)
            {
                if (((string)dateObject).Equals("Now", StringComparison.CurrentCultureIgnoreCase) || ((string)dateObject).Equals("Today", StringComparison.CurrentCultureIgnoreCase))
                {
                    dateObject = DateTimeOffset.Now;
                }
            }

            if (dateObject is DateTimeOffset)
            {
                var date = (DateTimeOffset)dateObject;
                return DotLiquid.Liquid.UseRubyDateFormat && string.IsNullOrEmpty(locale)
                    ? RubyDateTimeFormatter.ToString(date, format)
                    : date.ToString(format, string.IsNullOrEmpty(locale) ? CultureInfo.CurrentCulture : new CultureInfo(locale));
            }

            if (dateObject is DateTime)
            {
                var date = new DateTimeOffset((DateTime)dateObject);
                return DotLiquid.Liquid.UseRubyDateFormat && string.IsNullOrEmpty(locale)
                    ? RubyDateTimeFormatter.ToString(date, format)
                    : date.ToString(format, string.IsNullOrEmpty(locale) ? CultureInfo.CurrentCulture : new CultureInfo(locale));
            }

            return dateObject.ToString();
        }

        public static object ParseDate(string input, string format)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            DateTime date;

            if (string.IsNullOrEmpty(format))
            {
                if (!DateTime.TryParse(input, out date))
                {
                    return input;
                }
                else
                {
                    return date;
                }
            }
            else
            {
                if (!DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    return input;
                }
                else
                {
                    return date;
                }
            }
        }

    }

    public static class RubyDateTimeFormatter
    {
        public delegate string DateTimeOffsetDelegate(DateTimeOffset dateTimeOffset);

        private static readonly Dictionary<string, DateTimeOffsetDelegate> Formats = new Dictionary<string, DateTimeOffsetDelegate>
        {
            { "a", (dateTime) => dateTime.ToString("ddd", CultureInfo.CurrentCulture) },
            { "A", (dateTime) => dateTime.ToString("dddd", CultureInfo.CurrentCulture) },
            { "b", (dateTime) => dateTime.ToString("MMM", CultureInfo.CurrentCulture) },
            { "B", (dateTime) => dateTime.ToString("MMMM", CultureInfo.CurrentCulture) },
            { "c", (dateTime) => dateTime.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.CurrentCulture) },
            { "d", (dateTime) => dateTime.ToString("dd", CultureInfo.CurrentCulture) },
            { "e", (dateTime) => dateTime.ToString("%d", CultureInfo.CurrentCulture).PadLeft(2, ' ') },
            { "H", (dateTime) => dateTime.ToString("HH", CultureInfo.CurrentCulture) },
            { "I", (dateTime) => dateTime.ToString("hh", CultureInfo.CurrentCulture) },
            { "j", (dateTime) => dateTime.DayOfYear.ToString().PadLeft(3, '0') },
            { "m", (dateTime) => dateTime.ToString("MM", CultureInfo.CurrentCulture) },
            { "M", (dateTime) => dateTime.Minute.ToString().PadLeft(2, '0') },
            { "p", (dateTime) => dateTime.ToString("tt", CultureInfo.CurrentCulture) },
            { "S", (dateTime) => dateTime.ToString("ss", CultureInfo.CurrentCulture) },
            { "U", (dateTime) => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(dateTime.DateTime, CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule, DayOfWeek.Sunday).ToString().PadLeft(2, '0') },
            { "W", (dateTime) => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(dateTime.DateTime, CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule, DayOfWeek.Monday).ToString().PadLeft(2, '0') },
            { "w", (dateTime) => ((int) dateTime.DayOfWeek).ToString() },
            { "x", (dateTime) => dateTime.ToString("d", CultureInfo.CurrentCulture) },
            { "X", (dateTime) => dateTime.ToString("T", CultureInfo.CurrentCulture) },
            { "y", (dateTime) => dateTime.ToString("yy", CultureInfo.CurrentCulture) },
            { "Y", (dateTime) => dateTime.ToString("yyyy", CultureInfo.CurrentCulture) },
            { "Z", (dateTime) => dateTime.ToString("zzz", CultureInfo.CurrentCulture) },
            { "%", (dateTime) => "%" }
        };

        public static string ToString(DateTimeOffset dateTime, string pattern)
        {
            string output = "";

            int n = 0;

            while (n < pattern.Length)
            {
                string s = pattern.Substring(n, 1);

                if (n + 1 >= pattern.Length)
                    output += s;
                else
                    output += s == "%"
                        ? Formats.ContainsKey(pattern.Substring(++n, 1)) ? Formats[pattern.Substring(n, 1)].Invoke(dateTime) : "%" + pattern.Substring(n, 1)
                        : s;
                n++;
            }

            return output;
        }
    }
}
