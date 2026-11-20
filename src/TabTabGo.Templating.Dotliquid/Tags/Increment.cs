using System.Globalization;
using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.Exceptions;
using DotLiquid.Util;

namespace TabTabGo.Templating.Liquid.Tags;

/// <summary>The <c>{% increment name %}</c> tag: adds one to a variable (a missing variable starts at 1) without producing output.</summary>
public class Increment : Tag
{
    private static readonly Regex Syntax = R.B(R.Q(@"({0}+)\s*"), DotLiquid.Liquid.VariableSignature);

    private string _variable = string.Empty;

    /// <inheritdoc />
    public override void Initialize(string tagName, string markup, List<string> tokens)
    {
        var syntaxMatch = Syntax.Match(markup);
        if (!syntaxMatch.Success)
        {
            throw new SyntaxException("Syntax error in Increment tag");
        }

        _variable = syntaxMatch.Groups[1].Value;
        base.Initialize(tagName, markup, tokens);
    }

    /// <inheritdoc />
    public override void Render(Context context, TextWriter result)
    {
        var current = context[_variable] switch
        {
            null => 0,
            string text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
            IConvertible convertible => Convert.ToInt32(convertible, CultureInfo.InvariantCulture),
            _ => 0,
        };

        context.Scopes.Last()[_variable] = current + 1;
    }
}
