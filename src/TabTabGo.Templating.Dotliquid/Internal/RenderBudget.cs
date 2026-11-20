namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// The per-render size budget: no string produced while rendering (output, <c>capture</c> block, result of a growing filter) may be longer
/// than <see cref="TemplatingOptions.MaxOutputCharacters"/> of the running render (kept in <see cref="RenderScope"/>).
/// </summary>
internal static class RenderBudget
{
    /// <summary>The limit of the running render (0 = unlimited).</summary>
    public static long Limit => RenderScope.CurrentOptions.MaxOutputCharacters;

    /// <summary>Throws when a string of <paramref name="length"/> characters would exceed the limit.</summary>
    public static void Check(long length)
    {
        var limit = Limit;
        if (limit > 0 && length > limit)
        {
            throw Exceeded(limit);
        }
    }

    /// <summary>Throws when a list or loop would handle more than <see cref="Limit"/> items (every item may be an empty string, so characters alone do not bound it).</summary>
    public static void CheckItems(long count) => Check(count);

    /// <summary>Throws when a repeat count is larger than a string could ever hold, or than the limit (even for an empty string).</summary>
    public static void CheckRepeat(decimal times)
    {
        if (times > int.MaxValue)
        {
            throw new RenderLimitExceededException("The repeat count is larger than any string can hold (see TemplatingOptions.MaxOutputCharacters).");
        }

        var limit = Limit;
        if (limit > 0 && times > limit)
        {
            throw Exceeded(limit);
        }
    }

    /// <summary>Stops a long loop inside a filter when the render time-out has passed.</summary>
    public static void ThrowIfCancelled()
    {
        if (RenderScope.Current?.Cancellation.IsCancellationRequested == true)
        {
            throw new RenderLimitExceededException("The render exceeded TemplatingOptions.RenderTimeout.");
        }
    }

    /// <summary>
    /// An upper bound of the length <c>Match.Result</c> produces for <paramref name="replacement"/>: literal characters count one, a
    /// group or whole-match token at most the match, and the prefix, suffix and whole-input tokens at most the input.
    /// </summary>
    public static long ExpansionBound(string replacement, int matchLength, int inputLength)
    {
        long total = 0;
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                total++;
                continue;
            }

            var next = replacement[i + 1];
            if (next == '$')
            {
                total++;
                i++;
            }
            else if (next is '`' or '\'' or '_')
            {
                total += inputLength;
                i++;
            }
            else if (next is '&' or '+' || char.IsDigit(next))
            {
                total += matchLength;
                i++;
                while (char.IsDigit(next) && i + 1 < replacement.Length && char.IsDigit(replacement[i + 1]))
                {
                    i++;
                }
            }
            else if (next == '{')
            {
                var close = replacement.IndexOf('}', i + 2);
                total += matchLength;
                i = close < 0 ? replacement.Length : close;
            }
            else
            {
                total += 2;
                i++;
            }
        }

        return total;
    }

    public static RenderLimitExceededException Exceeded(long limit) =>
        new($"The render produced more than {limit} characters (TemplatingOptions.MaxOutputCharacters).");
}
