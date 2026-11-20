using System.Collections;
using DocumentFormat.OpenXml;
using DotLiquid;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>Helpers for templates that describe an Open XML workbook as JSON.</summary>
public static class OpenXmlFilters
{
    /// <summary>Wraps a value as an Open XML string value.</summary>
    /// <param name="input">The value.</param>
    public static StringValue? OpenXmlToString(object? input) => input == null ? null : StringValue.FromString(input.ToString());

    /// <summary>
    /// Maps a CLR type name to the workbook data type name: <c>Number</c>, <c>Boolean</c>, <c>Date</c> or <c>String</c>. Open XML SDK 3.x
    /// prints <c>CellValues</c> as "CellValues { }", so the names are explicit.
    /// </summary>
    /// <param name="type">The CLR type name, with or without <c>System.</c>.</param>
    public static string? OpenXmlType(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        return type.Replace("System.", string.Empty) switch
        {
            "Int32" or "Int16" or "Int64" or "Float" or "Single" or "Decimal" or "Double"
                or "Byte" or "SByte" or "UInt16" or "UInt32" or "UInt64" => "Number",
            "Boolean" => "Boolean",
            "Date" or "DateTime" or "DateTimeOffset" or "DateOnly" => "Date",
            _ => "String",
        };
    }

    /// <summary>Removes spaces from a format name so it can be looked up in the style map.</summary>
    /// <param name="format">The format name.</param>
    public static string? OpenXmlFormat(string? format) => string.IsNullOrEmpty(format) ? null : format.Replace(" ", string.Empty);

    /// <summary>
    /// The width (in characters) of the widest value of <paramref name="propertyName"/> in the list, or of the header when that is wider.
    /// Items may be hashes (what the engine produces) or <see cref="JObject"/>s.
    /// </summary>
    /// <param name="list">The rows.</param>
    /// <param name="propertyName">The member to measure.</param>
    /// <param name="headerValue">The column header.</param>
    public static string OpenXmlMaxWidth(IEnumerable? list, string propertyName, string? headerValue)
    {
        var headerLength = string.IsNullOrEmpty(headerValue) ? 0 : headerValue.Length;
        var longest = 0;
        if (list != null)
        {
            foreach (var item in list)
            {
                var text = item switch
                {
                    JObject json => json[propertyName]?.ToString(),
                    IDictionary dictionary when dictionary.Contains(propertyName) => dictionary[propertyName]?.ToString(),
                    _ => null,
                };
                longest = Math.Max(longest, text?.Length ?? 0);
            }
        }

        return Math.Max(longest, headerLength).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
