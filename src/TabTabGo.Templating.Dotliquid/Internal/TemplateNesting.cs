using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.Exceptions;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Guards the recursion of <c>Template.Parse</c> (S-13): DotLiquid's parser recurses once per nested block, so a few thousand nested <c>{% if %}</c> tags overflow the
/// stack of the parsing thread and end the process. Two layers: a scan that counts block nesting over the very tokens DotLiquid parses (its own tokenizer, so
/// <c>{{ ... }}</c> output tags, whitespace-control tags and shorthands split exactly as they do for the parser), and the parse itself on a thread whose stack is sized from
/// MaxNestingDepth (<see cref="ParseStackBytes"/>), which absorbs a small undercount only; the scan is the guard.
/// </summary>
internal static class TemplateNesting
{
    // The parse thread's reserved stack. Measured (DotLiquid 2.3.260, Release and Debug, if/for/capture/case): the recursive parse needs about 0.6 KB per nested level
    // (529-609 KB at 1,000 levels, 1.6-1.9 MB at 3,000) and fits 100 levels in 64 KB. The stack is 512 KB plus 8 KB per allowed level, about 13 times the measured need
    // (default MaxNestingDepth 100 reserves 1,312 KB), because the scan is the guard and the stack only backs it up. It was a fixed 256 MB per render.
    private const int ParseStackBaseBytes = 512 * 1024;
    private const int ParseStackBytesPerLevel = 8 * 1024;
    private const int ParseStackMaxBytes = 256 * 1024 * 1024;

    // tags that continue a block instead of opening one; answered without asking DotLiquid (it throws for a name it does not register, which is slow in a loop)
    private static readonly HashSet<string> Continuations = new(StringComparer.Ordinal) { "else", "elsif", "elseif", "when", "break", "continue" };

    // DotLiquid's built-in block tags; they open a block level even when the tag registry is cold
    private static readonly HashSet<string> BuiltInBlocks = new(StringComparer.Ordinal) { "if", "unless", "case", "for", "tablerow", "capture", "raw", "comment", "ifchanged" };

    // registered names are stable for the life of the process (a name may be registered later, never removed), so only positive answers are kept
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> KnownBlocks = new(StringComparer.Ordinal);

    private static readonly Regex TagName = new(@"^\{%-?\s*(\w+)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // DotLiquid's own tokenizer is internal; calling it is what keeps the scan on the parser's token boundaries. The differential tests pin the pair.
    // Looked up lazily and defensively: a DotLiquid whose internal signature changed must degrade to the stock parse, not throw TypeInitializationException at first use.
    private static readonly Lazy<Func<string, SyntaxCompatibility, List<string>>?> Tokenize = new(
        () => CreateTokenizer(typeof(Template).Assembly.GetType("DotLiquid.Tokenizer", throwOnError: false)),
        LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Binds <paramref name="tokenizerType"/>'s static <c>Tokenize(string, SyntaxCompatibility)</c>, or returns null when the type or the method has another shape.</summary>
    /// <param name="tokenizerType">DotLiquid's tokenizer type, or null when it was not found.</param>
    /// <param name="methodName">The method name.</param>
    internal static Func<string, SyntaxCompatibility, List<string>>? CreateTokenizer(Type? tokenizerType, string methodName = "Tokenize")
    {
        try
        {
            var method = tokenizerType?.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string), typeof(SyntaxCompatibility) },
                modifiers: null);
            return method == null
                ? null
                : (Func<string, SyntaxCompatibility, List<string>>?)Delegate.CreateDelegate(typeof(Func<string, SyntaxCompatibility, List<string>>), method, throwOnBindFailure: false);
        }
        catch (Exception ex) when (ex is AmbiguousMatchException or System.ArgumentException or MethodAccessException or MissingMethodException)
        {
            return null;
        }
    }

    /// <summary>Parses <paramref name="template"/> after refusing blocks nested deeper than <paramref name="maxDepth"/>, on a thread with a large stack.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="maxDepth">The limit (at least 1).</param>
    /// <exception cref="SyntaxException">The template nests blocks too deeply, or DotLiquid rejects its syntax.</exception>
    public static Template Parse(string template, int maxDepth) =>
        Parse(template, maxDepth, SharedTokenParseSupported(typeof(Template).Assembly.GetName().Version) && Tokenize.Value != null);

    /// <summary>True when the last <see cref="Parse(string, int, bool)"/> on this thread took the shared-token path (tests pin that the public overload asks the gate).</summary>
    [ThreadStatic]
    internal static bool LastParseWasShared;

    /// <summary>The parse with the path chosen by the caller: the shared-token path, or the stock <c>Template.Parse</c> after the scan (tests run both).</summary>
    /// <param name="template">The template text.</param>
    /// <param name="maxDepth">The limit (at least 1).</param>
    /// <param name="sharedTokens">True for the shared-token parse; only valid on the DotLiquid version the gate accepts.</param>
    /// <exception cref="SyntaxException">The template nests blocks too deeply, or DotLiquid rejects its syntax.</exception>
    internal static Template Parse(string template, int maxDepth, bool sharedTokens)
    {
        // DotLiquid has no public overload for pre-tokenized input (its Template constructor is internal too), but Template.Parse is exactly: new Template(), Root = new Document(), Root.Initialize(null, null, tokens)
        // over Tokenizer.Tokenize(source, level) (verified in 2.3.260's IL: ParseInternal does nothing else; Literal.FromShortHand and Comment.FromShortHand, the whitespace-control
        // rewrite and the end-of-tag checks all run inside Tokenize, so one preprocessed string feeds both the scan and the parser). Doing those steps here lets the scan and the parser share one token list (the tokenizer is superlinear, so a second
        // pass doubled the parse cost). The tests compare the resulting tree with Template.Parse.
        // only the DotLiquid the shared-token path was verified against; any other version takes the stock Template.Parse (scan, then parse) so a bumped DotLiquid cannot change the tree
        LastParseWasShared = sharedTokens;
        List<string>? tokens = null;
        if (sharedTokens)
        {
            tokens = Tokens(template);
            if (Depth(tokens, maxDepth) > maxDepth)
            {
                throw new SyntaxException($"The template nests blocks deeper than {maxDepth} levels (TemplatingOptions.MaxNestingDepth).");
            }
        }
        else
        {
            Check(template, maxDepth);
        }

        Template? parsed = null;
        Exception? failure = null;
        var culture = CultureInfo.CurrentCulture; // a new thread starts with the process culture; the parse must see the caller's, as Template.Parse does
        var uiCulture = CultureInfo.CurrentUICulture;
        var thread = new Thread(
            () =>
            {
                try
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = uiCulture;
                    if (tokens != null)
                    {
                        var result = (Template)Activator.CreateInstance(typeof(Template), nonPublic: true)!; // the constructor is internal
                        result.Root = new Document();
                        result.Root.Initialize(null, null, tokens); // consumes the list
                        parsed = result;
                    }
                    else
                    {
                        parsed = Template.Parse(template);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            StackFor(maxDepth, Tokenize.Value));
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return parsed!;
    }

    /// <summary>
    /// The stack for one parse: the depth formula while DotLiquid's own tokenizer feeds the scan (it counts exactly what the parser recurses on), the 256 MB maximum
    /// when the approximate fallback scan is in use, because that scan can undercount.
    /// </summary>
    /// <param name="maxDepth">The limit (at least 1).</param>
    /// <param name="tokenizer">The bound DotLiquid tokenizer, or null when the fallback scan runs.</param>
    internal static int StackFor(int maxDepth, Func<string, SyntaxCompatibility, List<string>>? tokenizer) =>
        tokenizer == null ? ParseStackMaxBytes : ParseStackBytes(maxDepth);

    /// <summary>The reserved stack of the parse thread for a template limited to <paramref name="maxDepth"/> nested levels (exact tokenizer).</summary>
    /// <param name="maxDepth">The limit (at least 1).</param>
    internal static int ParseStackBytes(int maxDepth) =>
        (int)Math.Min(ParseStackMaxBytes, ParseStackBaseBytes + (long)ParseStackBytesPerLevel * Math.Max(1, maxDepth));

    /// <summary>True only for DotLiquid 2.3.260, the version whose <c>Template.Parse</c> the shared-token parse was verified against.</summary>
    /// <param name="version">The loaded DotLiquid assembly version.</param>
    internal static bool SharedTokenParseSupported(Version? version) => version is { Major: 2, Minor: 3, Build: 260 };

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
    internal static int Depth(string template, int stopAfter = int.MaxValue) => Depth(Tokens(template), stopAfter);

    /// <summary>The scan over tokens already produced by <see cref="Tokens(string)"/>.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="stopAfter">Depth at which to stop counting.</param>
    internal static int Depth(List<string> tokens, int stopAfter) => Depth(tokens, stopAfter, null);

    /// <summary>The scan with the tag lookup chosen by the caller (tests pass one that fails, to prove the built-in blocks do not depend on DotLiquid's registry).</summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="stopAfter">Depth at which to stop counting.</param>
    /// <param name="tagLookup">Resolves a tag name to its type, or throws <see cref="NullReferenceException"/> when it is not registered; null uses <c>Template.GetTagType</c>.</param>
    internal static int Depth(List<string> tokens, int stopAfter, Func<string, Type?>? tagLookup)
    {
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
                if (IsBlock(name, notBlocks, tagLookup))
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
            else if (IsBlock(name, notBlocks, tagLookup))
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
    internal static List<string> Tokens(string template) => Tokens(template, Tokenize.Value);

    /// <summary>The tokens from <paramref name="tokenizer"/>, or from the built-in approximation when it is null (DotLiquid's internal tokenizer was not found).</summary>
    /// <param name="template">The template text.</param>
    /// <param name="tokenizer">DotLiquid's tokenizer, or null.</param>
    internal static List<string> Tokens(string template, Func<string, SyntaxCompatibility, List<string>>? tokenizer) =>
        tokenizer != null ? tokenizer(template, Template.DefaultSyntaxCompatibilityLevel) : FallbackTokens(template);

    /// <summary>
    /// Splits <c>{% ... %}</c> and <c>{{ ... }}</c> in one linear pass, without DotLiquid's whitespace-control and shorthand rewriting. Good enough to count block nesting
    /// (an undercounted template can still overflow the parse thread's stack, which is sized for MaxNestingDepth only); it is only used when DotLiquid's own tokenizer cannot be bound.
    /// The tag-name pattern accepts the <c>{%-</c> whitespace-control form (DotLiquid has no <c>~</c> variant).
    /// </summary>
    /// <param name="template">The template text.</param>
    internal static List<string> FallbackTokens(string template)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < template.Length - 1)
        {
            var open = template.IndexOf('{', i);
            if (open < 0 || open >= template.Length - 1)
            {
                break;
            }

            var kind = template[open + 1];
            if (kind == '#')
            {
                // the {# ... #} shorthand is a comment block for DotLiquid's parser (it rewrites it to {% comment %}...{% endcomment %})
                var close = template.IndexOf("#}", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    break;
                }

                tokens.Add("{% comment %}");
                tokens.Add("{% endcomment %}");
                i = close + 2;
                continue;
            }

            if (kind is not ('%' or '{'))
            {
                i = open + 1;
                continue;
            }

            var end = template.IndexOf(kind == '%' ? "%}" : "}}", open + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            tokens.Add(template.Substring(open, end + 2 - open));
            i = end + 2;
        }

        return tokens;
    }

    /// <summary>Calls made to DotLiquid's tag registry by the scan (tests assert it stays at one per distinct name).</summary>
    internal static long RegistryLookups => Interlocked.Read(ref _registryLookups);

    private static long _registryLookups;

    private static bool IsBlock(string name, HashSet<string> notBlocks, Func<string, Type?>? tagLookup)
    {
        if (BuiltInBlocks.Contains(name))
        {
            return true; // by name, whatever the registry holds: a cold or changed registry must never make the scan blind to the common blocks
        }

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
            Interlocked.Increment(ref _registryLookups);
            type = (tagLookup ?? Template.GetTagType)(name);
        }
        catch (NullReferenceException)
        {
            // DotLiquid 2.3 throws, instead of returning null, for a name that is not a registered tag; only this scan remembers it (never the process-wide cache),
            // and the built-in blocks never get here
            notBlocks.Add(name);
            return false;
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
