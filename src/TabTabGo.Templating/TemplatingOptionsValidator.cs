namespace TabTabGo.Templating;

/// <summary>Validation shared by the engine packages (they see it through InternalsVisibleTo).</summary>
internal static class TemplatingOptionsValidator
{
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    /// <summary>Throws when an option the engine relies on is out of range. Called once, from the engine constructors.</summary>
    /// <param name="options">The options to check.</param>
    /// <param name="validateRenderLimits">False for engines that run no regular expressions and no template renders.</param>
    public static TemplatingOptions Validate(TemplatingOptions options, bool validateRenderLimits)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxXmlCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxXmlCharacters must not be negative (0 = unlimited).");
        }

        if (!validateRenderLimits)
        {
            return options;
        }

        if (options.RegexTimeout != Timeout.InfiniteTimeSpan && (options.RegexTimeout <= TimeSpan.Zero || options.RegexTimeout > MaxTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RegexTimeout must be positive (or Timeout.InfiniteTimeSpan).");
        }

        if (options.ParseTimeout <= TimeSpan.Zero || options.ParseTimeout > MaxTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ParseTimeout must be positive and finite.");
        }

        if (options.MaxTemplateCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxTemplateCharacters must not be negative (0 = unlimited).");
        }

        if (options.RenderTimeout != Timeout.InfiniteTimeSpan && (options.RenderTimeout <= TimeSpan.Zero || options.RenderTimeout > MaxTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RenderTimeout must be positive (or Timeout.InfiniteTimeSpan).");
        }

        if (options.MaxIterations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxIterations must not be negative (0 = unlimited).");
        }

        if (options.MaxOutputCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxOutputCharacters must not be negative (0 = unlimited).");
        }

        if (options.MaxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be at least 1.");
        }

        return options;
    }
}
