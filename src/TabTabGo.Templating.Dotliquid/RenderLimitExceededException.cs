namespace TabTabGo.Templating.Liquid;

/// <summary>
/// Thrown when a render produces more than <see cref="TemplatingOptions.MaxOutputCharacters"/> characters, either as its output or as
/// one intermediate string (a <c>capture</c> block, or the result of <c>Append</c>, <c>Join</c>, <c>Replace</c> and the other growing filters).
/// </summary>
/// <remarks>
/// It derives from <see cref="TimeoutException"/> on purpose: DotLiquid lets that family of exceptions escape a render, while every other
/// exception is turned into "Liquid error" text inside the output, which would hide the limit from the caller.
/// </remarks>
public class RenderLimitExceededException : TimeoutException
{
    /// <summary>Creates the exception with the default message.</summary>
    public RenderLimitExceededException()
        : base("The render exceeded the configured output limit (TemplatingOptions.MaxOutputCharacters).")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What exceeded the limit.</param>
    public RenderLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What exceeded the limit.</param>
    /// <param name="innerException">The cause.</param>
    public RenderLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
