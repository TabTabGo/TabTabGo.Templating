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

    public static RenderLimitExceededException Exceeded(long limit) =>
        new($"The render produced more than {limit} characters (TemplatingOptions.MaxOutputCharacters).");
}
