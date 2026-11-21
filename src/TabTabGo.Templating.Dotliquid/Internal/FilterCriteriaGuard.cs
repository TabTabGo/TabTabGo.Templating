using System.Globalization;
using System.Text.RegularExpressions;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Checks the criteria text of the <c>Filter</c> filter before System.Linq.Dynamic.Core parses it. A criterion runs outside the render's
/// output accounting, so an expression such as <c>it["k"].ToString().PadLeft(500000000).Length &gt; 0</c> allocates about 1 GB without ever
/// reaching <c>MaxOutputCharacters</c>. The guard rejects calls of the methods that grow a string from a number or a template, and any call
/// whose numeric argument (a number, or a format specifier such as <c>"D500000000"</c>) is larger than <see cref="MaxCallNumber"/>.
/// </summary>
internal static class FilterCriteriaGuard
{
    /// <summary>The largest number a method call may be given.</summary>
    internal const double MaxCallNumber = 10_000;

    private static readonly HashSet<string> BlockedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "PadLeft", "PadRight", "Replace", "Repeat", "Insert", "Concat", "Join", "Format",
    };

    // keywords that may be followed by "(" without being a method call
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "mod", "in", "eq", "ne", "lt", "gt", "le", "ge", "if", "as", "is",
    };

    private static readonly Regex FormatSpecifier = new(@"^\s*[A-Za-z]\s*(\d+)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Throws when <paramref name="criteria"/> calls a blocked method or passes a number above the cap to a method.</summary>
    /// <param name="criteria">The expression text.</param>
    /// <exception cref="ArgumentException">The expression is not allowed.</exception>
    public static void Validate(string? criteria)
    {
        if (string.IsNullOrEmpty(criteria))
        {
            return;
        }

        var frames = new Stack<bool>(); // true: the parenthesis opened a method call
        var callDepth = 0;
        string? previousIdentifier = null;
        var i = 0;
        while (i < criteria.Length)
        {
            var c = criteria[i];
            if (c == '"' || c == '\'')
            {
                var literal = ReadString(criteria, ref i, c);
                if (callDepth > 0 && FormatSpecifier.Match(literal) is { Success: true } match && ExceedsCap(match.Groups[1].Value))
                {
                    throw new ArgumentException("A Filter expression may not pass a number above " + MaxCallNumber.ToString(CultureInfo.InvariantCulture) + " to a method.");
                }

                previousIdentifier = null;
            }
            else if (char.IsLetter(c) || c == '_' || c == '@' || c == '$')
            {
                var start = i;
                while (i < criteria.Length && (char.IsLetterOrDigit(criteria[i]) || criteria[i] == '_'))
                {
                    i++;
                }

                previousIdentifier = criteria[start..i];
            }
            else if (char.IsDigit(c))
            {
                var start = i;
                while (i < criteria.Length && (char.IsLetterOrDigit(criteria[i]) || criteria[i] == '_' || criteria[i] == '.'
                    || ((criteria[i] == '+' || criteria[i] == '-') && char.ToLowerInvariant(criteria[i - 1]) == 'e' && !criteria[start..i].StartsWith("0x", StringComparison.OrdinalIgnoreCase))))
                {
                    i++;
                }

                if (callDepth > 0 && ExceedsCap(criteria[start..i]))
                {
                    throw new ArgumentException("A Filter expression may not pass a number above " + MaxCallNumber.ToString(CultureInfo.InvariantCulture) + " to a method.");
                }

                previousIdentifier = null;
            }
            else
            {
                if (c == '(')
                {
                    var isCall = previousIdentifier != null && !Keywords.Contains(previousIdentifier);
                    if (isCall && BlockedMethods.Contains(previousIdentifier!))
                    {
                        throw new ArgumentException($"A Filter expression may not call {previousIdentifier}.");
                    }

                    frames.Push(isCall);
                    if (isCall)
                    {
                        callDepth++;
                    }
                }
                else if (c == ')' && frames.Count > 0 && frames.Pop())
                {
                    callDepth--;
                }

                if (!char.IsWhiteSpace(c))
                {
                    previousIdentifier = null;
                }

                i++;
            }
        }
    }

    private static string ReadString(string text, ref int index, char quote)
    {
        var builder = new System.Text.StringBuilder();
        index++;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\\' && index + 1 < text.Length)
            {
                builder.Append(text[index + 1]);
                index += 2;
            }
            else if (c == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    builder.Append(quote); // a doubled quote is a quote inside the literal
                    index += 2;
                }
                else
                {
                    index++;
                    break;
                }
            }
            else
            {
                builder.Append(c);
                index++;
            }
        }

        return builder.ToString();
    }

    private static bool ExceedsCap(string number)
    {
        if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return !long.TryParse(number.AsSpan(2).TrimEnd("lLuU"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) || hex > MaxCallNumber;
        }

        var digits = number.TrimEnd('f', 'F', 'd', 'D', 'm', 'M', 'l', 'L', 'u', 'U');
        return !double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || Math.Abs(value) > MaxCallNumber;
    }
}
