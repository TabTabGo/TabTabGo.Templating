using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

/// <summary>
/// Writes one JSON value into a cell with the type the JSON token has (X-5, X-6): numbers as numbers (invariant text), booleans as 1/0,
/// dates as Excel serial numbers, everything else as text. A cell never keeps the stale content of the template macro it replaces.
/// </summary>
internal static class CellValueWriter
{
    private const string FormatInlineString = "IS";
    private const string FormatSharedString = "SS";
    private const string FormatNumber = "N";

    /// <summary>Replaces the content of <paramref name="cell"/> with <paramref name="value"/> (null writes an empty text).</summary>
    /// <param name="cell">The cell; its style is kept.</param>
    /// <param name="value">The value, or null when the path matched nothing.</param>
    /// <param name="format">The macro suffix after <c>:#</c>: <c>IS</c>, <c>SS</c>, <c>N</c>, or a .NET date format.</param>
    /// <param name="culture">Culture of date formats.</param>
    /// <param name="sharedStrings">Shared-string table of the workbook.</param>
    public static void Write(Cell cell, JToken? value, string format, CultureInfo culture, SharedStrings sharedStrings)
    {
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;

        if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
        {
            WriteInline(cell, string.Empty);
            return;
        }

        if (format.Equals(FormatInlineString, StringComparison.Ordinal))
        {
            WriteInline(cell, ToValidXml(TextOf(value, culture)));
            return;
        }

        if (format.Equals(FormatSharedString, StringComparison.Ordinal))
        {
            cell.DataType = CellValues.SharedString;
            cell.CellValue = new CellValue(sharedStrings.GetOrAdd(ToValidXml(TextOf(value, culture))).ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (format.Equals(FormatNumber, StringComparison.Ordinal))
        {
            if (double.TryParse(TextOf(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            {
                WriteNumber(cell, parsed.ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                WriteInline(cell, ToValidXml(TextOf(value, culture)));
            }

            return;
        }

        switch (value.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float:
                WriteNumberToken(cell, ((JValue)value).Value, culture);
                break;
            case JTokenType.Boolean:
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(value.Value<bool>() ? "1" : "0"); // 1 or 0: the old code wrote an unrelated double, so true became FALSE (X-2)
                break;
            case JTokenType.Date:
                WriteDate(cell, ((JValue)value).Value, format, culture);
                break;
            default:
                WriteInline(cell, ToValidXml(TextOf(value, culture)));
                break;
        }
    }

    private static void WriteNumberToken(Cell cell, object? number, CultureInfo culture)
    {
        switch (number)
        {
            case double d when !double.IsFinite(d):
            case float f when !float.IsFinite(f):
                WriteInline(cell, Convert.ToString(number, CultureInfo.InvariantCulture) ?? string.Empty); // NaN and Infinity are not numbers in a workbook
                break;
            case double d:
                WriteNumber(cell, d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case float f:
                WriteNumber(cell, f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case IFormattable formattable:
                WriteNumber(cell, formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                WriteInline(cell, Convert.ToString(number, culture) ?? string.Empty);
                break;
        }
    }

    private static void WriteDate(Cell cell, object? date, string format, CultureInfo culture)
    {
        var moment = date switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset offset => offset.DateTime,
            _ => (DateTime?)null,
        };

        if (moment == null)
        {
            WriteInline(cell, ToValidXml(Convert.ToString(date, culture) ?? string.Empty));
        }
        else if (format.Length > 0)
        {
            WriteInline(cell, ToValidXml(moment.Value.ToString(format, culture))); // text only when the template asks for a format
        }
        else
        {
            WriteNumber(cell, moment.Value.ToOADate().ToString("R", CultureInfo.InvariantCulture)); // a serial number sorts and filters; the template cell's style formats it
        }
    }

    private static void WriteNumber(Cell cell, string text)
    {
        cell.DataType = CellValues.Number;
        cell.CellValue = new CellValue(text);
    }

    private static void WriteInline(Cell cell, string text)
    {
        cell.DataType = CellValues.InlineString;
        cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static string TextOf(JToken value, CultureInfo culture) => value switch
    {
        JValue { Value: string text } => text,
        JValue { Value: DateTime dateTime } => dateTime.ToString("G", culture),
        JValue { Value: IFormattable formattable } => formattable.ToString(null, culture),
        JValue jValue => jValue.Value?.ToString() ?? string.Empty,
        _ => value.ToString(Formatting.None),
    };

    /// <summary>Removes characters that are not valid in XML 1.0, keeping valid surrogate pairs (emoji, supplementary CJK).</summary>
    /// <param name="s">The text.</param>
    /// <param name="startIndex">Index to begin checking at.</param>
    public static string ToValidXml(string s, int startIndex = 0)
    {
        var firstInvalid = IndexOfFirstInvalid(s, startIndex);
        if (firstInvalid < 0)
        {
            return s;
        }

        var builder = new System.Text.StringBuilder(s.Length);
        builder.Append(s, 0, firstInvalid);
        for (var i = firstInvalid; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                builder.Append(s[i]).Append(s[i + 1]);
                i++;
            }
            else if (IsLegal(s[i]))
            {
                builder.Append(s[i]);
            }
        }

        return builder.ToString();
    }

    /// <summary>The index of the first character that is not valid in XML 1.0, or -1.</summary>
    /// <param name="s">The text.</param>
    /// <param name="startIndex">Index to begin checking at.</param>
    public static int IndexOfFirstInvalid(string? s, int startIndex = 0)
    {
        if (string.IsNullOrEmpty(s) || startIndex >= s.Length)
        {
            return -1;
        }

        for (var i = Math.Max(startIndex, 0); i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (!IsLegal(s[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether a single UTF-16 code unit is valid XML 1.0 (surrogates are only valid as a pair, which this check cannot see).</summary>
    public static bool IsLegal(char c)
    {
        if (c > 31 && c <= 55295)
        {
            return true;
        }

        if (c < 32)
        {
            return c == 9 || c == 10 || c == 13;
        }

        return c >= 57344 && c <= 65533;
    }
}
