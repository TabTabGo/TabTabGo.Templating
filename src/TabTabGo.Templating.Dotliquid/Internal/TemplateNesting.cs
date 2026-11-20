using DotLiquid;
using DotLiquid.Exceptions;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// A linear scan of template text that refuses blocks nested deeper than a limit, before <c>Template.Parse</c> runs: the parser recurses once per
/// nested block, so a few thousand nested <c>{% if %}</c> tags overflow the stack of the parsing thread and kill the process (S-13).
/// </summary>
internal static class TemplateNesting
{
    /// <summary>Throws <see cref="SyntaxException"/> when <paramref name="template"/> nests blocks deeper than <paramref name="maxDepth"/>.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="maxDepth">The limit (at least 1).</param>
    public static void Check(string template, int maxDepth)
    {
        var depth = 0;
        var position = 0;
        while (position < template.Length)
        {
            var open = template.IndexOf("{%", position, StringComparison.Ordinal);
            if (open < 0)
            {
                return;
            }

            var close = template.IndexOf("%}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                return; // an unterminated tag is a syntax error for the parser to report
            }

            position = close + 2;
            var name = TagName(template, open + 2, close);
            if (name.Length == 0)
            {
                continue;
            }

            if (name is "raw" or "comment")
            {
                position = SkipTo(template, position, "end" + name);
                continue;
            }

            if (name.StartsWith("end", StringComparison.Ordinal))
            {
                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (IsBlock(name) && ++depth > maxDepth)
            {
                throw new SyntaxException($"The template nests blocks deeper than {maxDepth} levels (TemplatingOptions.MaxNestingDepth).");
            }
        }
    }

    private static string TagName(string template, int start, int end)
    {
        while (start < end && (template[start] == '-' || char.IsWhiteSpace(template[start])))
        {
            start++;
        }

        var stop = start;
        while (stop < end && !char.IsWhiteSpace(template[stop]) && template[stop] != '-')
        {
            stop++;
        }

        return template.Substring(start, stop - start);
    }

    /// <summary>Position after the closing tag named <paramref name="endTag"/>, or the end of the text.</summary>
    private static int SkipTo(string template, int position, string endTag)
    {
        while (position < template.Length)
        {
            var open = template.IndexOf("{%", position, StringComparison.Ordinal);
            if (open < 0)
            {
                return template.Length;
            }

            var close = template.IndexOf("%}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                return template.Length;
            }

            position = close + 2;
            if (TagName(template, open + 2, close) == endTag)
            {
                return position;
            }
        }

        return template.Length;
    }

    private static bool IsBlock(string name)
    {
        Type? type;
        try
        {
            type = Template.GetTagType(name);
        }
        catch (NullReferenceException)
        {
            return false; // DotLiquid 2.3 throws, instead of returning null, for a name that is not a registered tag (else, elsif, when ...)
        }

        return type != null && typeof(Block).IsAssignableFrom(type);
    }
}
