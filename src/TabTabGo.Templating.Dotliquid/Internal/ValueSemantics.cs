using System.Collections;
using DotLiquid;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>Value equality and ordering for template values (boxed numbers, strings, hashes, lists) shared by the collection filters.</summary>
internal static class ValueSemantics
{
    /// <summary>Equal when the values are equal by content: numbers by value, hashes by keys and values, lists by items.</summary>
    public static new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        if (IsNumber(x) && IsNumber(y))
        {
            return CompareNumbers(x, y) == 0;
        }

        if (x is Hash hx && y is Hash hy)
        {
            return HashesEqual(hx, hy);
        }

        if (x is IEnumerable ex && y is IEnumerable ey && x is not string && y is not string)
        {
            var left = ex.Cast<object?>().ToList();
            var right = ey.Cast<object?>().ToList();
            return left.Count == right.Count && left.Zip(right).All(pair => Equals(pair.First, pair.Second));
        }

        return x.Equals(y);
    }

    /// <summary>A hash code consistent with <see cref="Equals(object?, object?)"/>.</summary>
    public static int GetHashCode(object? value)
    {
        switch (value)
        {
            case null:
                return 0;
            case Hash hash:
                // order independent: two hashes with the same pairs hash alike
                return hash.Aggregate(17, (sum, pair) => unchecked(sum + ((StringComparer.Ordinal.GetHashCode(pair.Key) * 31) ^ GetHashCode(pair.Value))));
            case string text:
                return StringComparer.Ordinal.GetHashCode(text);
            case IEnumerable list:
                return list.Cast<object?>().Aggregate(19, (sum, item) => unchecked((sum * 31) + GetHashCode(item)));
            default:
                return IsNumber(value) ? ToDecimalOrDouble(value).GetHashCode() : value.GetHashCode();
        }
    }

    /// <summary>
    /// Orders null first, numbers by value (mixed integer and floating types included), same-typed comparable values naturally; values
    /// without a natural order compare equal so a stable sort keeps their order.
    /// </summary>
    public static int Compare(object? x, object? y)
    {
        if (x == null || y == null)
        {
            return (x == null ? 0 : 1) - (y == null ? 0 : 1);
        }

        if (IsNumber(x) && IsNumber(y))
        {
            return CompareNumbers(x, y);
        }

        if (x.GetType() == y.GetType() && x is IComparable comparable)
        {
            return comparable.CompareTo(y);
        }

        return 0;
    }

    private static bool HashesEqual(Hash x, Hash y)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        foreach (var key in x.Keys)
        {
            if (!y.ContainsKey(key) || !Equals(x[key], y[key]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static int CompareNumbers(object x, object y)
    {
        if (x is float or double || y is float or double)
        {
            return Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToDecimal(x, System.Globalization.CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(y, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static decimal ToDecimalOrDouble(object value)
    {
        // equal numbers must hash alike across types: 1, 1L and 1.0 all go through decimal when they fit
        try
        {
            return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return decimal.MaxValue;
        }
    }

    /// <summary>An <see cref="IEqualityComparer{T}"/> over template values using <see cref="Equals(object?, object?)"/>.</summary>
    public sealed class Comparer : IEqualityComparer<object?>, IComparer<object?>
    {
        public static readonly Comparer Instance = new();

        bool IEqualityComparer<object?>.Equals(object? x, object? y) => ValueSemantics.Equals(x, y);

        int IEqualityComparer<object?>.GetHashCode(object? obj) => ValueSemantics.GetHashCode(obj);

        int IComparer<object?>.Compare(object? x, object? y) => ValueSemantics.Compare(x, y);
    }
}
