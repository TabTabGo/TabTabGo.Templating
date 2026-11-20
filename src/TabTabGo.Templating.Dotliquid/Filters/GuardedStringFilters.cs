using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>
/// DotLiquid's growing filters (<c>Append</c>, <c>Prepend</c>, <c>Join</c>, <c>Replace</c>, <c>ReplaceFirst</c>, <c>Plus</c>, <c>Times</c>,
/// <c>Concat</c>) with a size check against <see cref="TemplatingOptions.MaxOutputCharacters"/> before anything is allocated. Registered after
/// the stock filters so these win; within the limit they return what the stock filters return.
/// </summary>
public static class GuardedStringFilters
{
    /// <summary>Appends <paramref name="operand"/> to <paramref name="input"/>.</summary>
    /// <param name="input">The text.</param>
    /// <param name="operand">The text to append.</param>
    public static string? Append(string? input, string? operand)
    {
        if (input == null)
        {
            return input;
        }

        RenderBudget.Check((long)input.Length + (operand?.Length ?? 0));
        return input + operand;
    }

    /// <summary>Puts <paramref name="operand"/> in front of <paramref name="input"/>.</summary>
    /// <param name="input">The text.</param>
    /// <param name="operand">The text to prepend.</param>
    public static string? Prepend(string? input, string? operand)
    {
        if (input == null)
        {
            return input;
        }

        RenderBudget.Check((long)input.Length + (operand?.Length ?? 0));
        return operand + input;
    }

    /// <summary>Joins the items of a list (nested lists are flattened) with <paramref name="glue"/>.</summary>
    /// <param name="input">The list.</param>
    /// <param name="glue">The separator, default one space.</param>
    public static string? Join(IEnumerable? input, string? glue = " ")
    {
        if (input == null)
        {
            return null;
        }

        var builder = new StringBuilder();
        var first = true;
        long count = 0;
        foreach (var item in Flatten(input))
        {
            RenderBudget.CheckItems(++count);
            if ((count & 1023) == 0)
            {
                RenderBudget.ThrowIfCancelled();
            }

            if (!first)
            {
                builder.Append(glue);
            }

            builder.Append(item);
            RenderBudget.Check(builder.Length);
            first = false;
        }

        return builder.ToString();
    }

    /// <summary>Replaces matches of a regular expression (or, in DotLiquid 2.2+ syntax mode, a literal) in the text.</summary>
    /// <param name="context">The render context.</param>
    /// <param name="input">The text.</param>
    /// <param name="string">What to look for.</param>
    /// <param name="replacement">What to put in its place.</param>
    public static string? Replace(Context context, string? input, string? @string, string? replacement = "") =>
        ReplaceCore(context, input, @string, replacement, count: -1);

    /// <summary>Replaces the first match only.</summary>
    /// <param name="context">The render context.</param>
    /// <param name="input">The text.</param>
    /// <param name="string">What to look for.</param>
    /// <param name="replacement">What to put in its place.</param>
    public static string? ReplaceFirst(Context context, string? input, string? @string, string? replacement = "") =>
        ReplaceCore(context, input, @string, replacement, count: 1);

    /// <summary>Adds numbers, or concatenates two strings.</summary>
    /// <param name="context">The render context.</param>
    /// <param name="input">The left value.</param>
    /// <param name="operand">The right value.</param>
    public static object? Plus(Context context, object? input, object? operand)
    {
        if (input is string left && operand is string right)
        {
            RenderBudget.Check((long)left.Length + right.Length);
        }

        return StandardFilters.Plus(context, input, operand);
    }

    /// <summary>Multiplies numbers, or repeats a string.</summary>
    /// <param name="context">The render context.</param>
    /// <param name="input">The value.</param>
    /// <param name="operand">The factor.</param>
    public static object? Times(Context context, object? input, object? operand)
    {
        if (input is string text && operand is sbyte or byte or short or ushort or int or uint or long or ulong)
        {
            var times = Convert.ToDecimal(operand, System.Globalization.CultureInfo.InvariantCulture);
            if (times > 0)
            {
                RenderBudget.CheckRepeat(times); // independent of the character product: an empty string repeated 3 billion times is a loop too
                RenderBudget.Check((long)Math.Min((decimal)long.MaxValue, text.Length * times));
            }
        }

        return StandardFilters.Times(context, input, operand);
    }

    /// <summary>
    /// Concatenates two lists into a new list. The result may not hold more items than the output limit allows characters; it is built
    /// eagerly so that a chain of concatenations cannot nest lazy sequences that are only expanded at the end.
    /// </summary>
    /// <param name="input">The first list.</param>
    /// <param name="array">The second list.</param>
    public static IEnumerable? Concat(IEnumerable? input, IEnumerable? array)
    {
        if (input == null || array == null)
        {
            return input ?? array;
        }

        var result = new List<object?>();
        foreach (var item in input.Cast<object?>().Concat(array.Cast<object?>()))
        {
            result.Add(item);
            RenderBudget.Check(result.Count);
            if ((result.Count & 1023) == 0)
            {
                RenderBudget.ThrowIfCancelled();
            }
        }

        return result;
    }

    private static string? ReplaceCore(Context context, string? input, string? find, string? replacement, int count)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(find))
        {
            return input;
        }

        replacement ??= string.Empty;
        var builder = new StringBuilder();
        var last = 0;
        var done = 0;

        if (context.SyntaxCompatibilityLevel >= SyntaxCompatibility.DotLiquid22)
        {
            int at;
            while ((count < 0 || done < count) && (at = input.IndexOf(find, last, StringComparison.Ordinal)) >= 0)
            {
                RenderBudget.ThrowIfCancelled();
                RenderBudget.Check(builder.Length + (long)(at - last) + replacement.Length + (input.Length - at - find.Length));
                builder.Append(input, last, at - last).Append(replacement);
                last = at + find.Length;
                done++;
            }
        }
        else
        {
            // the stock 2.0 behaviour: the search text is a regular expression with the process-wide time-out, the replacement may use $1
            var regex = new Regex(find, RegexOptions.None, Template.RegexTimeOut);
            for (var match = regex.Match(input); match.Success && (count < 0 || done < count); match = match.NextMatch())
            {
                RenderBudget.ThrowIfCancelled();
                // the size is estimated before Match.Result allocates: a replacement such as "$_$_$_" would otherwise be built first and checked after
                RenderBudget.Check(builder.Length + (long)(match.Index - last) + RenderBudget.ExpansionBound(replacement, match.Length, input.Length) + (input.Length - match.Index - match.Length));
                builder.Append(input, last, match.Index - last).Append(match.Result(replacement));
                last = match.Index + match.Length;
                done++;
            }
        }

        builder.Append(input, last, input.Length - last); // within the budget: every append above was checked together with the tail
        return builder.ToString();
    }

    private static IEnumerable<object?> Flatten(IEnumerable input)
    {
        foreach (var item in input)
        {
            if (item is IEnumerable nested and not string)
            {
                foreach (var inner in Flatten(nested))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return item;
            }
        }
    }
}
