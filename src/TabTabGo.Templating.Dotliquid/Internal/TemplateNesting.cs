using System.Reflection;
using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.Exceptions;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Guards the recursion of <c>Template.Parse</c> (S-13): DotLiquid's parser recurses once per nested block, so a few thousand nested <c>{% if %}</c> tags overflow the
/// stack of the parsing thread and end the process. Two layers: a scan that counts block nesting over the very tokens DotLiquid parses (its own tokenizer, so
/// <c>{{ ... }}</c> output tags, whitespace-control tags and shorthands split exactly as they do for the parser), and the parse itself on a thread with a large fixed
/// stack, so that even a template the scan undercounts cannot take the process down.
/// </summary>
internal static class TemplateNesting
{
    private const int ParseStackBytes = 256 * 1024 * 1024; // reserved, committed only as used; room for one million characters of nested tags

    // tags that continue a block instead of opening one; answered without asking DotLiquid (it throws for a name it does not register, which is slow in a loop)
    private static readonly HashSet<string> Continuations = new(StringComparer.Ordinal) { "else", "elsif", "elseif", "when", "break", "continue" };

    // registered names are stable for the life of the process (a name may be registered later, never removed), so only positive answers are kept
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> KnownBlocks = new(StringComparer.Ordinal);

    private static readonly Regex TagName = new(@"^\{%\s*(\w+)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // DotLiquid's own tokenizer is internal; calling it is what keeps the scan on the parser's token boundaries. The differential tests pin the pair.
    private static readonly Func<string, SyntaxCompatibility, List<string>> Tokenize = (Func<string, SyntaxCompatibility, List<string>>)Delegate.CreateDelegate(
        typeof(Func<string, SyntaxCompatibility, List<string>>),
        typeof(Template).Assembly.GetType("DotLiquid.Tokenizer", throwOnError: true)!.GetMethod("Tokenize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!);

    /// <summary>Parses <paramref name="template"/> after refusing blocks nested deeper than <paramref name="maxDepth"/>, on a thread with a large stack.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="maxDepth">The limit (at least 1).</param>
    /// <exception cref="SyntaxException">The template nests blocks too deeply, or DotLiquid rejects its syntax.</exception>
    public static Template Parse(string template, int maxDepth)
    {
        Check(template, maxDepth);

        Template? parsed = null;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    parsed = Template.Parse(template);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            ParseStackBytes);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return parsed!;
    }

    /// <summary>Throws <see cref="SyntaxException"/> when <paramref name="template"/> nests blocks deeper than <paramref name="maxDepth"/>.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="maxDepth">The limit (at least 1).</param>
    public static void Check(string template, int maxDepth)
    {
        if (Depth(template, maxDepth) > maxDepth)
        {
            throw new SyntaxException($"The template nests blocks deeper than {maxDepth} levels (TemplatingOptions.MaxNestingDepth).");
        }
    }

    /// <summary>
    /// The deepest block nesting the scan counts, stopping once it passes <paramref name="stopAfter"/>. An end tag lowers the depth only when it closes the innermost
    /// open block; any other end tag changes nothing (DotLiquid rejects it as an unknown tag, but never nests deeper because of it). The bodies of <c>raw</c> and
    /// <c>comment</c> are skipped, as DotLiquid's tokenizer returns each as one text token.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="stopAfter">Depth at which to stop counting.</param>
    internal static int Depth(string template, int stopAfter = int.MaxValue)
    {
        var tokens = Tokens(template);
        var open = new Stack<string>();
        var notBlocks = new HashSet<string>(StringComparer.Ordinal); // per scan: an unregistered name is looked up once
        var deepest = 0;
        string? skipUntil = null; // raw and comment bodies: DotLiquid's tokenizer returns the body as one text token up to the end tag
        foreach (var token in tokens)
        {
            if (!token.StartsWith("{%", StringComparison.Ordinal))
            {
                continue; // text and {{ ... }} output tokens never open a block, whatever they contain
            }

            var match = TagName.Match(token);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups[1].Value;
            if (skipUntil != null)
            {
                if (name == skipUntil)
                {
                    skipUntil = null;
                    if (open.Count > 0 && "end" + open.Peek() == name)
                    {
                        open.Pop();
                    }
                }

                continue;
            }

            if (name is "raw" or "comment")
            {
                skipUntil = "end" + name;
                if (IsBlock(name, notBlocks))
                {
                    open.Push(name); // a block level for the parser even though its body is one text token
                    deepest = Math.Max(deepest, open.Count);
                }

                continue;
            }

            if (open.Count > 0 && name == "end" + open.Peek())
            {
                open.Pop();
            }
            else if (IsBlock(name, notBlocks))
            {
                open.Push(name);
                deepest = Math.Max(deepest, open.Count);
                if (deepest > stopAfter)
                {
                    return deepest;
                }
            }
        }

        return deepest;
    }

    /// <summary>The tokens <c>Template.Parse</c> would parse (its cost, superlinear in the token count, is DotLiquid's own).</summary>
    internal static List<string> Tokens(string template) => Tokenize(DotLiquid.Tags.Comment.FromShortHand(template), Template.DefaultSyntaxCompatibilityLevel);

    private static bool IsBlock(string name, HashSet<string> notBlocks)
    {
        if (Continuations.Contains(name) || notBlocks.Contains(name))
        {
            return false;
        }

        if (KnownBlocks.ContainsKey(name))
        {
            return true;
        }

        Type? type;
        try
        {
            type = Template.GetTagType(name);
        }
        catch (NullReferenceException)
        {
            notBlocks.Add(name);
            return false; // DotLiquid 2.3 throws, instead of returning null, for a name that is not a registered tag
        }

        if (type != null && typeof(Block).IsAssignableFrom(type))
        {
            KnownBlocks[name] = true;
            return true;
        }

        notBlocks.Add(name);
        return false;
    }
}
