using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.Exceptions;
using TabTabGo.Templating.Liquid.Internal;

namespace TabTabGo.Templating.Liquid.Tags;

/// <summary>
/// DotLiquid's <c>capture</c> tag with a bounded buffer: the block is rendered into a writer that throws
/// <see cref="RenderLimitExceededException"/> past <see cref="TemplatingOptions.MaxOutputCharacters"/>, so a capture cannot build an
/// unbounded string before the output writer sees it. Behaviour within the limit is the stock tag's.
/// </summary>
internal class GuardedCapture : Block
{
    private static readonly Regex Syntax = new(@"(\w+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private string _to = string.Empty;

    /// <inheritdoc />
    public override void Initialize(string tagName, string markup, List<string> tokens)
    {
        var match = Syntax.Match(markup);
        if (!match.Success)
        {
            throw new SyntaxException("Syntax error in 'capture' - Valid syntax: capture [var]");
        }

        _to = match.Groups[1].Value;
        base.Initialize(tagName, markup, tokens);
    }

    /// <inheritdoc />
    public override void Render(Context context, TextWriter result)
    {
        using var buffer = new LimitedWriter(RenderBudget.Limit, result.FormatProvider);
        base.Render(context, buffer);
        context.Scopes.Last()[_to] = buffer.ToString();
    }
}
