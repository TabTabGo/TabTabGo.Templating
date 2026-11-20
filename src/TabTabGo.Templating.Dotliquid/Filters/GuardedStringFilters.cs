using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.Liquid.Filters;

/// <summary>
/// DotLiquid's growing filters (<c>Append</c>, <c>Prepend</c>, <c>Join</c>, <c>Replace</c>, <c>ReplaceFirst</c>, <c>Plus</c>, <c>Times</c>,
/// <c>Concat</c>) with a size check against <see cref="TemplatingOptions.MaxOutputCharacters"/> before anything is allocated. Registered after
/// the stock filters so these win; within the limit they return exactly what the stock DotLiquid filters return at every syntax level
/// (the differential test <c>GuardedFiltersStockParityTests</c> pins it).
/// </summary>
internal static class GuardedStringFilters
{
    /// <summary>Appends <paramref name="operand"/> to <paramref name="input"/>.</summary>
    /// <param name="input">The text.</param>
    /// <param name="operand">The text to append.</param>
    public static string? Append(string? input, string? operand)
    {
        // stock is plain concatenation: a nil input gives the operand, two nils give an empty string
        RenderBudget.Check((long)(input?.Length ?? 0) + (operand?.Length ?? 0));
        return input + operand;
    }

    /// <summary>Puts <paramref name="operand"/> in front of <paramref name="input"/>.</summary>
    /// <param name="input">The text.</param>
    /// <param name="operand">The text to prepend.</param>
    public static string? Prepend(string? input, string? operand)
    {
        RenderBudget.Check((long)(input?.Length ?? 0) + (operand?.Length ?? 0));
        return operand + input;
    }

    /// <summary>Joins the items of a list with <paramref name="glue"/>.</summary>
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
        foreach (var item in input) // not flattened: a nested list prints as the stock filter prints it
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
        var literal = context.SyntaxCompatibilityLevel >= SyntaxCompatibility.DotLiquid21;

        if (literal && count == 1)
        {
            // the stock ReplaceFirst: a culture-sensitive search, and the length of the search text is what is removed
            var position = input.IndexOf(find);
            if (position < 0)
            {
                return input;
            }

            RenderBudget.Check((long)input.Length - find.Length + replacement.Length);
            return input.Remove(position, find.Length).Insert(position, replacement);
        }

        var builder = new StringBuilder();
        var last = 0;
        var done = 0;

        if (literal)
        {
            // the stock Replace: string.Replace, an ordinal literal replacement
            int at;
            while ((at = input.IndexOf(find, last, StringComparison.Ordinal)) >= 0)
            {
                RenderBudget.ThrowIfCancelled();
                RenderBudget.Check(builder.Length + (long)(at - last) + replacement.Length + (input.Length - at - find.Length));
                builder.Append(input, last, at - last).Append(replacement);
                last = at + find.Length;
            }
        }
        else
        {
            // the stock 2.0 behaviour: the search text is a regular expression with the process-wide time-out; Replace expands $1 in the
            // replacement, ReplaceFirst inserts it literally
            var regex = new Regex(find, RegexOptions.None, Template.RegexTimeOut);
            for (var match = regex.Match(input); match.Success && (count < 0 || done < count); match = match.NextMatch())
            {
                RenderBudget.ThrowIfCancelled();
                // the size is estimated before Match.Result allocates: a replacement such as "$_$_$_" would otherwise be built first and checked after
                var inserted = count < 0 ? RenderBudget.ExpansionBound(replacement, match.Length, input.Length) : replacement.Length;
                RenderBudget.Check(builder.Length + (long)(match.Index - last) + inserted + (input.Length - match.Index - match.Length));
                builder.Append(input, last, match.Index - last).Append(count < 0 ? match.Result(replacement) : replacement);
                last = match.Index + match.Length;
                done++;
            }
        }

        builder.Append(input, last, input.Length - last);
        RenderBudget.Check(builder.Length); // defence in depth: the pre-checks above are estimates
        return builder.ToString();
    }
}
