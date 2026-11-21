using System.Collections;
using System.Linq.Dynamic.Core;
using System.Xml.XPath;
using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>Filters over lists of hashes and values.</summary>
public static class CollectionFilters
{
    /// <summary>Collects the value of <paramref name="property"/> from every hash in the list (items that are not hashes, and missing values, are skipped).</summary>
    /// <param name="array">The list.</param>
    /// <param name="property">The member name.</param>
    public static object? SelectMany(IEnumerable? array, string? property)
    {
        if (array == null || string.IsNullOrEmpty(property))
        {
            return null;
        }

        var selected = new List<object>();
        foreach (var item in array)
        {
            if (item is Hash hash && ValueFilters.Select(hash, property) is { } value)
            {
                selected.Add(value);
            }
        }

        return selected;
    }

    /// <summary>Returns the values of every node an XPath expression selects in XML text (DTDs are rejected, S-5).</summary>
    /// <param name="xmlInput">The XML text.</param>
    /// <param name="xPath">The XPath expression.</param>
    public static IEnumerable? XPathSelectMany(string? xmlInput, string xPath)
    {
        if (string.IsNullOrEmpty(xmlInput))
        {
            return xmlInput;
        }

        var navigator = SafeXml.Parse(xmlInput).CreateNavigator();
        var array = new List<object>();
        foreach (XPathNavigator node in navigator.Select(xPath))
        {
            array.Add(node.Value);
        }

        return array;
    }

    /// <summary>Drops null items and blank strings.</summary>
    /// <param name="array">The list.</param>
    public static object? RemoveEmpty(IEnumerable? array)
    {
        if (array == null)
        {
            return null;
        }

        return array.Cast<object?>().Where(item => item != null && !(item is string text && string.IsNullOrWhiteSpace(text))).ToList();
    }

    /// <summary>The distinct items of both lists, first list first. An empty second list returns the first one (deduplicated, F-3).</summary>
    /// <param name="array">The first list.</param>
    /// <param name="secondArray">The second list.</param>
    public static object? Union(IEnumerable? array, IEnumerable? secondArray)
    {
        if (array == null || !array.Cast<object>().Any())
        {
            return secondArray;
        }

        var items = array.Cast<object?>();
        if (secondArray != null)
        {
            items = items.Concat(secondArray.Cast<object?>());
        }

        return items.Where(item => item != null).Distinct(ValueSemantics.Comparer.Instance).ToList();
    }

    /// <summary>Keeps the items that satisfy a System.Linq.Dynamic.Core expression (<c>it["k"].ToString() = "v"</c>).</summary>
    /// <param name="array">The list of hashes.</param>
    /// <param name="criteria">The expression. Do not build it from untrusted data; prefer <see cref="FilterBy"/>. It is checked against an
    /// allow-list (<c>ToString()</c>, <c>Equals</c>, the comparison and search methods of string, <c>Math</c> and <c>Convert</c> conversions, indexers); anything that grows a
    /// string (<c>PadLeft</c>, <c>Replace</c>, <c>Insert</c>, <c>Concat</c>, <c>Join</c>, <c>Format</c>, <c>+</c>) is refused because it allocates outside the output limit.</param>
    /// <exception cref="ArgumentException">The expression uses a method, member or node outside the allow-list, concatenates strings, or passes a computed or huge number to a string method.</exception>
    public static object? Filter(IEnumerable? array, string criteria)
    {
        if (array == null)
        {
            return null;
        }

        return array.Cast<Hash>().Where(FilterCriteriaGuard.Compile(criteria)).ToList();
    }

    /// <summary>
    /// Keeps the items whose <paramref name="key"/> equals <paramref name="value"/> (compared as text). The LINQ-free
    /// replacement for <c>it["key"].ToString() = "value"</c> that templates should use instead of building expressions from data.
    /// </summary>
    /// <param name="array">The list of hashes.</param>
    /// <param name="key">The member to compare.</param>
    /// <param name="value">The value to match.</param>
    public static object? FilterBy(IEnumerable? array, string key, object? value)
    {
        if (array == null)
        {
            return null;
        }

        var expected = value?.ToString();
        return array.OfType<Hash>().Where(item => KeyText(item, key) == expected).ToList();
    }

    /// <summary>
    /// Keeps the items whose <paramref name="key"/> starts with <paramref name="prefix"/> (ordinal comparison).
    /// Replacement for <c>it["key"].ToString().StartsWith("prefix")</c>.
    /// </summary>
    /// <param name="array">The list of hashes.</param>
    /// <param name="key">The member to compare.</param>
    /// <param name="prefix">The prefix.</param>
    public static object? FilterByPrefix(IEnumerable? array, string key, string? prefix)
    {
        if (array == null)
        {
            return null;
        }

        return array.OfType<Hash>()
            .Where(item => KeyText(item, key) is { } text && text.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal))
            .ToList();
    }

    private static string? KeyText(Hash item, string key) => item.ContainsKey(key) ? item[key]?.ToString() : null;

    /// <summary>Follows a dotted path of list members and returns the items found at its end.</summary>
    /// <param name="array">The list of hashes.</param>
    /// <param name="nestedList">The dotted path, for example <c>lines</c> or <c>order.lines</c>.</param>
    public static object? Flatten(IEnumerable? array, string? nestedList)
    {
        if (array == null || string.IsNullOrEmpty(nestedList))
        {
            return null;
        }

        IEnumerable? flattened = array;
        foreach (var token in nestedList.Split('.'))
        {
            flattened = FlattenInternal(flattened, token);
        }

        return flattened;
    }

    /// <summary>Removes duplicates by value: hashes by content, numbers by value, strings ordinally. The first occurrence stays.</summary>
    /// <param name="array">The list.</param>
    public static object? Distinct(IEnumerable? array)
    {
        return array?.Cast<object?>().Distinct(ValueSemantics.Comparer.Instance).ToList();
    }

    /// <summary>Sorts by a member of each hash, or the values themselves when no member is given. The sort is stable; numbers sort by value.</summary>
    /// <param name="input">The list.</param>
    /// <param name="property">The member to sort by; empty to sort the values.</param>
    public static IEnumerable OrderBy(IEnumerable? input, string? property)
    {
        if (input == null)
        {
            return new List<object?>();
        }

        var items = input.Cast<object?>().ToList();
        if (items.Count == 0)
        {
            return items;
        }

        Func<object?, object?> key = string.IsNullOrEmpty(property)
            ? item => item
            : item => item is IDictionary dictionary && dictionary.Contains(property) ? dictionary[property] : null;
        return items.OrderBy(key, ValueSemantics.Comparer.Instance).ToList();
    }

    /// <summary>Groups hashes by a member; each group is a hash with <c>Key</c> (text; empty for a missing value) and <c>Items</c>.</summary>
    /// <param name="input">The list of hashes.</param>
    /// <param name="property">The member to group by.</param>
    public static IEnumerable? GroupBy(IEnumerable? input, string? property)
    {
        if (string.IsNullOrEmpty(property) || input == null)
        {
            return input;
        }

        var result = new List<Hash>();
        foreach (var group in input.OfType<Hash>().GroupBy(row => row.ContainsKey(property) ? row[property] ?? string.Empty : string.Empty))
        {
            var item = new Hash
            {
                ["Key"] = group.Key.ToString(),
                ["Items"] = new List<object>(group),
            };
            result.Add(item);
        }

        return result;
    }

    private static IEnumerable? FlattenInternal(IEnumerable? array, string nestedList)
    {
        if (array == null || string.IsNullOrEmpty(nestedList))
        {
            return null;
        }

        var flattened = new List<object>();
        foreach (var item in array.OfType<Hash>())
        {
            if (!item.ContainsKey(nestedList))
            {
                continue;
            }

            switch (item[nestedList])
            {
                case null:
                    break;
                case Hash or string:
                    flattened.Add(item[nestedList]);
                    break;
                case IEnumerable nested:
                    flattened.AddRange(nested.Cast<object>());
                    break;
                default:
                    flattened.Add(item[nestedList]);
                    break;
            }
        }

        return flattened;
    }
}
