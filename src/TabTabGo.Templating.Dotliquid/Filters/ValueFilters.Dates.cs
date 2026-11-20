using System.Collections.Concurrent;
using System.Globalization;

namespace TabTabGo.Templating.Liquid.Filters;

public static partial class ValueFilters
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> TimeZones = new(StringComparer.Ordinal);

    /// <summary>Number of resolved zone names kept in the process-wide cache.</summary>
    internal static int TimeZoneCacheCount => TimeZones.Count;

    /// <summary>Upper bound of the zone cache.</summary>
    internal const int TimeZoneCacheLimit = 256;

    /// <summary>The clock the date filters use for <c>Now</c> and <c>Today</c>; tests replace it.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// Converts a date to a time zone given by IANA id (<c>Asia/Dubai</c>), Windows id (<c>Arabian Standard Time</c>) or, as before,
    /// the zone's standard name. The zone's daylight saving rules apply. An unknown zone returns the input unchanged.
    /// </summary>
    /// <param name="input">A date, a date-time, or text that parses as one.</param>
    /// <param name="timeZoneName">The zone name.</param>
    public static object? ToTimeZone(object? input, string? timeZoneName)
    {
        if (string.IsNullOrEmpty(timeZoneName))
        {
            return input;
        }

        var timeZone = ResolveTimeZone(timeZoneName);
        if (timeZone == null)
        {
            return input;
        }

        var date = ToDateTimeOffset(input);
        return date == null ? null : TimeZoneInfo.ConvertTime(date.Value, timeZone);
    }

    private static TimeZoneInfo? ResolveTimeZone(string name)
    {
        // Names come from templates and data: only names that resolve are cached, and the cache is bounded, so an unknown or
        // endless stream of names cannot grow process memory.
        if (TimeZones.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var found = FindTimeZone(name);
        if (found != null && TimeZones.Count < TimeZoneCacheLimit)
        {
            TimeZones.TryAdd(name, found);
        }

        return found;
    }

    private static TimeZoneInfo? FindTimeZone(string name)
    {
        if (name.Length > 128)
        {
            return null; // no zone id or display name is that long
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // fall through: older templates pass the (display) standard name
        }

        return StandardNames.Value.TryGetValue(name, out var zone) ? zone : null;
    }

    // built once from the system zones (read-only afterwards), never from template input
    private static readonly Lazy<Dictionary<string, TimeZoneInfo>> StandardNames = new(() =>
    {
        var map = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            map.TryAdd(zone.StandardName, zone);
        }

        return map;
    });

    /// <summary>
    /// Moves a date to a fixed UTC offset (seconds, rounded to whole minutes). Text <c>Now</c> is the current UTC time and <c>Today</c> the
    /// current UTC date; a date-time of unspecified kind is taken as UTC.
    /// </summary>
    /// <param name="input">A date, a date-time, or text that parses as one.</param>
    /// <param name="offset">Offset from UTC in seconds.</param>
    /// <exception cref="ArgumentException">The input is not a date.</exception>
    public static DateTimeOffset? OffsetDate(object? input, int offset)
    {
        var date = ToDateTimeOffset(input);
        return date?.ToUniversalTime().ToOffset(TimeSpan.FromMinutes(Math.Round(offset / 60.0)));
    }

    private static DateTimeOffset? ToDateTimeOffset(object? input)
    {
        switch (input)
        {
            case null:
                return null;
            case DateTimeOffset offset:
                return offset;
            case DateTime dateTime:
                // Unspecified is documented as UTC; the server's time zone must not change a report (F-10)
                return dateTime.Kind == DateTimeKind.Local
                    ? new DateTimeOffset(dateTime)
                    : new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            case DateOnly dateOnly:
                return new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            case string text:
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed;
                }

                if (text.Equals("Now", StringComparison.OrdinalIgnoreCase))
                {
                    return Clock.GetUtcNow();
                }

                if (text.Equals("Today", StringComparison.OrdinalIgnoreCase))
                {
                    return new DateTimeOffset(Clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
                }

                break;
        }

        throw new ArgumentException("The value must be a DateTime, a DateTimeOffset or text that is a date.", nameof(input));
    }

    /// <summary>Formats a date with a .NET format string (or Ruby strftime tokens when enabled and no locale is given).</summary>
    /// <param name="dateObject">A date, a date-time, or the text <c>Now</c>/<c>Today</c>.</param>
    /// <param name="format">The format string.</param>
    /// <param name="locale">Culture name; the render culture when empty.</param>
    public static string? FormatDate(object? dateObject, string format, string? locale = null)
    {
        if (dateObject == null)
        {
            return null;
        }

        if (dateObject is string text && (text.Equals("Now", StringComparison.OrdinalIgnoreCase) || text.Equals("Today", StringComparison.OrdinalIgnoreCase)))
        {
            dateObject = ToDateTimeOffset(text);
        }

        if (dateObject is DateTimeOffset or DateTime or DateOnly)
        {
            var date = ToDateTimeOffset(dateObject)!.Value;
            return DotLiquid.Liquid.UseRubyDateFormat && string.IsNullOrEmpty(locale)
                ? RubyDateTimeFormatter.ToString(date, format)
                : date.ToString(format, string.IsNullOrEmpty(locale) ? CultureInfo.CurrentCulture : new CultureInfo(locale));
        }

        return dateObject.ToString();
    }

    /// <summary>Parses text into a date-time (invariant culture, or the exact <paramref name="format"/>); text that is not a date is returned unchanged.</summary>
    /// <param name="input">The text.</param>
    /// <param name="format">An exact format, or empty for general parsing.</param>
    public static object? ParseDate(string? input, string? format)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var parsed = string.IsNullOrEmpty(format)
            ? DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            : DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        return parsed ? date : input;
    }
}

/// <summary>Formats dates with Ruby <c>strftime</c> tokens (<c>%Y-%m-%d</c>), used when DotLiquid's Ruby date format is switched on.</summary>
public static class RubyDateTimeFormatter
{
    /// <summary>Formats one token.</summary>
    /// <param name="dateTimeOffset">The date.</param>
    public delegate string DateTimeOffsetDelegate(DateTimeOffset dateTimeOffset);

    private static readonly Dictionary<string, DateTimeOffsetDelegate> Formats = new()
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
        { "j", (dateTime) => dateTime.DayOfYear.ToString(CultureInfo.CurrentCulture).PadLeft(3, '0') },
        { "m", (dateTime) => dateTime.ToString("MM", CultureInfo.CurrentCulture) },
        { "M", (dateTime) => dateTime.Minute.ToString(CultureInfo.CurrentCulture).PadLeft(2, '0') },
        { "p", (dateTime) => dateTime.ToString("tt", CultureInfo.CurrentCulture) },
        { "S", (dateTime) => dateTime.ToString("ss", CultureInfo.CurrentCulture) },
        { "U", (dateTime) => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(dateTime.DateTime, CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule, DayOfWeek.Sunday).ToString(CultureInfo.CurrentCulture).PadLeft(2, '0') },
        { "W", (dateTime) => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(dateTime.DateTime, CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule, DayOfWeek.Monday).ToString(CultureInfo.CurrentCulture).PadLeft(2, '0') },
        { "w", (dateTime) => ((int)dateTime.DayOfWeek).ToString(CultureInfo.CurrentCulture) },
        { "x", (dateTime) => dateTime.ToString("d", CultureInfo.CurrentCulture) },
        { "X", (dateTime) => dateTime.ToString("T", CultureInfo.CurrentCulture) },
        { "y", (dateTime) => dateTime.ToString("yy", CultureInfo.CurrentCulture) },
        { "Y", (dateTime) => dateTime.ToString("yyyy", CultureInfo.CurrentCulture) },
        { "Z", (dateTime) => dateTime.ToString("zzz", CultureInfo.CurrentCulture) },
        { "%", (dateTime) => "%" },
    };

    /// <summary>Formats <paramref name="dateTime"/> with <paramref name="pattern"/>; unknown tokens are kept as written.</summary>
    /// <param name="dateTime">The date.</param>
    /// <param name="pattern">The pattern with <c>%</c> tokens.</param>
    public static string ToString(DateTimeOffset dateTime, string pattern)
    {
        var output = new System.Text.StringBuilder();
        var n = 0;
        while (n < pattern.Length)
        {
            var s = pattern.Substring(n, 1);
            if (n + 1 >= pattern.Length)
            {
                output.Append(s);
            }
            else if (s == "%")
            {
                var token = pattern.Substring(++n, 1);
                output.Append(Formats.TryGetValue(token, out var format) ? format(dateTime) : "%" + token);
            }
            else
            {
                output.Append(s);
            }

            n++;
        }

        return output.ToString();
    }
}
