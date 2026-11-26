using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotLiquid;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Compiles the criteria text of the <c>Filter</c> filter through an allow-list. A criterion runs outside the render's output accounting, so an
/// expression such as <c>it["k"].ToString().PadLeft(500000000).Length &gt; 0</c> allocates about 1 GB without reaching <c>MaxOutputCharacters</c>.
/// The text is parsed by System.Linq.Dynamic.Core into an expression tree (nothing runs while parsing) and the tree is walked: only the node kinds and the
/// methods in this class are accepted, string concatenation is refused, and a number given to a string method must be a constant of at most
/// <see cref="MaxCallNumber"/>, so a computed or huge size cannot reach <c>PadLeft</c>, <c>Substring</c>, an indexer or a format specifier.
/// The allow-list does not rely on a scan of the text, so spacing, casing, nesting and string literals cannot hide a call from it. The text is only bounded before parsing
/// (<see cref="MaxCriteriaCharacters"/>, nesting of parentheses, signs and <c>not</c> at <see cref="MaxNesting"/>), because the Dynamic LINQ parser and this walk recurse per nesting level;
/// parsing and walking then run on a thread with a large stack, so even input that slips past the bounds ends as an exception and not as a process crash. Search, comparison and
/// prefix methods are rewritten to ordinal comparison (culture-sensitive <c>IndexOf</c> is quadratic and cannot be interrupted), and the number of costly calls is capped.
/// </summary>
internal static class FilterCriteriaGuard
{
    /// <summary>The largest numeric constant a string method may be given.</summary>
    internal const double MaxCallNumber = 10_000;

    /// <summary>The longest criteria text accepted: it bounds literals, chains of calls and the depth of the parse.</summary>
    internal const int MaxCriteriaCharacters = 1_000;

    /// <summary>The deepest nesting of parentheses, unary signs and <c>not</c> accepted before parsing.</summary>
    internal const int MaxNesting = 64;

    /// <summary>The most string, Math and Convert calls one expression may make (indexers and <c>ToString()</c> are not counted).</summary>
    internal const int MaxCalls = 32;

    /// <summary>
    /// Criteria up to this length, <see cref="ShallowNesting"/> deep, without brackets, braces, <c>new</c> or lambdas, are parsed on the caller's thread when
    /// <see cref="StackMarginLevels"/> levels of margin remain. Measured (cold JIT, one process per shape, minimum survivable caller stack): the eligible shapes need under
    /// 100 KB; shapes with <c>[</c>, <c>{</c>, <c>new</c> or <c>=&gt;</c> need up to about 330 KB within 200 characters and are never eligible.
    /// </summary>
    internal const int ShallowCharacters = 64;

    /// <summary>The deepest parenthesis, sign and <c>not</c> nesting parsed on the caller's thread.</summary>
    internal const int ShallowNesting = 8;

    /// <summary>Probe levels of 16 KB each: 8 levels plus the 128 KB that <c>TryEnsureSufficientExecutionStack</c> guarantees leave at least 256 KB for the parse.</summary>
    internal const int StackMarginLevels = 8;

    private static readonly object CacheLock = new();

    private const int MaxCachedPredicates = 256;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    private static long _clock;

    /// <summary>A cached predicate and when it was last used (a logical clock), so the least recently used one can be evicted alone.</summary>
    private sealed class CacheEntry(Func<Hash, bool> predicate)
    {
        public Func<Hash, bool> Predicate { get; } = predicate;

        public long LastUsed;
    }

    private const int ParseStackBytes = 16 * 1024 * 1024; // reserved, committed as used; the parse and the walk recurse once per nesting level

    private static readonly ParsingConfig Config = new()
    {
        // System.Linq.Dynamic.Core 1.6+ blocks ToString()/Equals() on object by default, which existing templates rely on (ADR 0002).
        AllowEqualsAndToStringMethodsOnObject = true,
        AllowNewToEvaluateAnyType = false,
    };

    private static readonly HashSet<string> StringMethods = new(StringComparer.Ordinal)
    {
        "StartsWith", "EndsWith", "Contains", "IndexOf", "Substring", "ToLower", "ToUpper", "ToLowerInvariant", "ToUpperInvariant", "Trim", "Equals", "CompareTo", "CompareOrdinal", "Compare", "IsNullOrEmpty", "IsNullOrWhiteSpace", "get_Chars",
    };

    private static readonly HashSet<string> MathMethods = new(StringComparer.Ordinal) { "Abs", "Min", "Max", "Floor", "Ceiling", "Round", "Sign" };

    private static readonly HashSet<string> ConvertMethods = new(StringComparer.Ordinal) { "ToInt32", "ToInt64", "ToDecimal", "ToDouble", "ToBoolean" };

    private static readonly HashSet<ExpressionType> AllowedNodes = new()
    {
        ExpressionType.Parameter, ExpressionType.Constant, ExpressionType.Call, ExpressionType.MemberAccess, ExpressionType.Conditional,
        ExpressionType.Convert, ExpressionType.ConvertChecked, ExpressionType.Not, ExpressionType.Negate, ExpressionType.NegateChecked, ExpressionType.UnaryPlus,
        ExpressionType.TypeAs, ExpressionType.TypeIs, ExpressionType.NewArrayInit, ExpressionType.ArrayIndex, ExpressionType.Index,
        ExpressionType.Add, ExpressionType.AddChecked, ExpressionType.Subtract, ExpressionType.SubtractChecked, ExpressionType.Multiply, ExpressionType.MultiplyChecked,
        ExpressionType.Divide, ExpressionType.Modulo, ExpressionType.And, ExpressionType.AndAlso, ExpressionType.Or, ExpressionType.OrElse, ExpressionType.ExclusiveOr,
        ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan, ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan, ExpressionType.GreaterThanOrEqual,
        ExpressionType.Coalesce,
    };

    /// <summary>Parses <paramref name="criteria"/>, checks the expression tree against the allow-list, and returns the predicate.</summary>
    /// <param name="criteria">The expression text.</param>
    /// <exception cref="ArgumentException">The expression uses a call, member or node that is not allowed, or a number above the cap.</exception>
    public static Func<Hash, bool> Compile(string criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (Cache.TryGetValue(criteria, out var cached))
        {
            Volatile.Write(ref cached.LastUsed, Interlocked.Increment(ref _clock)); // lock-free: a hit only stamps the entry
            return cached.Predicate; // only guard-approved predicates are ever stored, and a predicate holds no per-render state
        }

        if (criteria.Length > MaxCriteriaCharacters)
        {
            throw new ArgumentException($"A Filter expression may not be longer than {MaxCriteriaCharacters} characters.");
        }

        var nesting = CheckNesting(criteria);
        var predicate = IsShallow(criteria, nesting) && HasStackMargin(StackMarginLevels)
            ? ParseOnThisThread(criteria)
            : ParseOnLargeStack(criteria);

        lock (CacheLock)
        {
            // the miss path only: reads stay lock-free. When full, only the least recently used entry goes: a flood of distinct criteria (an id inside the text) pushes out its own
            // earlier entries, not the hot ones of every other template.
            if (Cache.Count >= MaxCachedPredicates && !Cache.ContainsKey(criteria))
            {
                string? oldestText = null;
                var oldestStamp = long.MaxValue;
                foreach (var (text, entry) in Cache)
                {
                    var stamp = Volatile.Read(ref entry.LastUsed);
                    if (stamp < oldestStamp)
                    {
                        oldestStamp = stamp;
                        oldestText = text;
                    }
                }

                if (oldestText != null)
                {
                    Cache.TryRemove(oldestText, out _);
                }
            }

            Cache[criteria] = new CacheEntry(predicate) { LastUsed = Interlocked.Increment(ref _clock) };
        }

        return predicate;
    }

    /// <summary>Whether the text is small and plain enough for the caller's thread: short, shallow, and none of the constructs whose parse recurses without a check (indexers, braces, <c>new</c>, lambdas).</summary>
    /// <param name="criteria">The expression text.</param>
    /// <param name="nesting">The nesting <see cref="CheckNesting"/> measured.</param>
    internal static bool IsShallow(string criteria, int nesting)
    {
        if (criteria.Length > ShallowCharacters || nesting > ShallowNesting || criteria.Contains('[') || criteria.Contains('{') || criteria.Contains("=>", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = criteria.IndexOf("new", StringComparison.OrdinalIgnoreCase); i >= 0; i = criteria.IndexOf("new", i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var before = i == 0 || !(char.IsLetterOrDigit(criteria[i - 1]) || criteria[i - 1] == '_');
            var after = i + 3 >= criteria.Length || !(char.IsLetterOrDigit(criteria[i + 3]) || criteria[i + 3] == '_');
            if (before && after)
            {
                return false; // the token new
            }
        }

        return true;
    }

    /// <summary>
    /// True when at least 256 KB of stack remain: each level takes 16 KB of real stack and asks the runtime whether 128 KB are still free behind it, so a true answer after
    /// <paramref name="levels"/> levels means <c>levels * 16 KB + 128 KB</c> were free at the start. (The runtime's own check only promises about 128 KB.)
    /// </summary>
    /// <param name="levels">How many 16 KB levels to probe.</param>
    internal static bool HasStackMargin(int levels)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            return false;
        }

        if (levels == 0)
        {
            return true;
        }

        Span<byte> pad = stackalloc byte[16 * 1024];
        pad[0] = 1;
        pad[^1] = 1;
        return HasStackMargin(levels - 1) && pad[0] == 1 && pad[^1] == 1;
    }

    /// <summary>Drops every cached predicate (tests).</summary>
    internal static void ClearCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }

    /// <summary>The number of cached predicates (tests).</summary>
    internal static int CachedCount => Cache.Count;

    /// <summary>Replaces the Dynamic LINQ parse (tests make it throw, to reach the real exception handling of <see cref="ParseAndCheck"/>).</summary>
    internal static Func<string, LambdaExpression>? Parser { get; set; }

    private static Func<Hash, bool> ParseAndCheck(string criteria)
    {
        try
        {
            var lambda = Parser?.Invoke(criteria) ?? DynamicExpressionParser.ParseLambda(Config, typeof(Hash), typeof(bool), criteria);
            var body = new Checker().Visit(lambda.Body)!;
            return Expression.Lambda<Func<Hash, bool>>(body, lambda.Parameters).Compile();
        }
        catch (Exception ex) when (ex is not ArgumentException and not InsufficientExecutionStackException and not OutOfMemoryException)
        {
            // an expression that parses but does not type-check ("renew == 1": Equal is not defined for Object and Int32) or compile surfaces as one catchable, short, path-free error
            var message = ex.Message.ReplaceLineEndings(" ");
            throw new ArgumentException("A Filter expression is not valid: " + (message.Length > 200 ? message[..200] : message));
        }
    }

    /// <summary>Short, shallow criteria need little stack, so they are parsed on the caller's thread (a thread per call costs ten times the parse).</summary>
    internal static Func<Hash, bool> ParseOnThisThread(string criteria, Func<string, Func<Hash, bool>>? parse = null)
    {
        try
        {
            return (parse ?? ParseAndCheck)(criteria);
        }
        catch (InsufficientExecutionStackException)
        {
            // the caller has less stack than the probe promised (it moved on, or a shape needed more): the criteria is valid, so it is retried where the stack is large
            return ParseOnLargeStack(criteria);
        }
    }

    private static Func<Hash, bool> ParseOnLargeStack(string criteria)
    {
        Func<Hash, bool>? predicate = null;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    predicate = ParseAndCheck(criteria);
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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return predicate!;
    }

    /// <summary>Walks an expression tree against the allow-list (the second half of <see cref="Compile"/>); tests call it with trees deeper than any text can produce.</summary>
    /// <param name="body">The expression.</param>
    internal static Expression CheckTree(Expression body) => new Checker().Visit(body)!;

    /// <summary>Refuses deep nesting of parentheses, unary signs and <c>not</c> before the recursive parser sees the text (string literals are skipped).</summary>
    private static int CheckNesting(string criteria)
    {
        var deepest = 0;
        var parentheses = 0;
        var run = 0; // consecutive unary operators ("- - -", "not not not")
        var i = 0;
        while (i < criteria.Length)
        {
            var c = criteria[i];
            if (c is '"' or '\'')
            {
                i++;
                while (i < criteria.Length && criteria[i] != c)
                {
                    i += criteria[i] == '\\' ? 2 : 1;
                }

                i++;
                run = 0;
                continue;
            }

            if (c is '(' or '[')
            {
                parentheses++;
                run = 0;
            }
            else if (c is ')' or ']')
            {
                parentheses = Math.Max(0, parentheses - 1);
                run = 0;
            }
            else if (c is '-' or '+' or '!' or '~')
            {
                run++;
            }
            else if (char.IsLetter(c))
            {
                var start = i;
                while (i + 1 < criteria.Length && (char.IsLetterOrDigit(criteria[i + 1]) || criteria[i + 1] == '_'))
                {
                    i++;
                }

                run = criteria.AsSpan(start, i - start + 1).Equals("not", StringComparison.OrdinalIgnoreCase) ? run + 1 : 0;
            }
            else if (!char.IsWhiteSpace(c))
            {
                run = 0;
            }

            deepest = Math.Max(deepest, parentheses + run);
            if (parentheses + run > MaxNesting)
            {
                throw new ArgumentException($"A Filter expression may not nest parentheses, signs or 'not' deeper than {MaxNesting} levels.");
            }

            i++;
        }

        return deepest;
    }

    private static bool IsNumeric(Type type) => Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) is
        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
        or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    /// <summary>The value of a constant, or of a constant under a conversion or a minus sign; null when the expression is computed.</summary>
    private static double? ConstantNumber(Expression expression) => expression switch
    {
        ConstantExpression { Value: IConvertible value } when value is not string => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.UnaryPlus } unary => ConstantNumber(unary.Operand),
        UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } unary => -ConstantNumber(unary.Operand),
        _ => null,
    };

    private sealed class Checker : ExpressionVisitor
    {
        private readonly HashSet<Expression> _generated = new(ReferenceEqualityComparer.Instance); // nodes the ordinal rewrite built: not counted again
        private int _calls;

        public override Expression? Visit(Expression? node)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack(); // a nesting the pre-parse bounds missed ends as InsufficientExecutionStackException, not as a crash
            if (node != null && !AllowedNodes.Contains(node.NodeType))
            {
                throw new ArgumentException($"A Filter expression may not use {node.NodeType} expressions.");
            }

            return base.Visit(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.Method is { Name: "Concat", DeclaringType: { } declaring } && declaring == typeof(string))
            {
                throw new ArgumentException("A Filter expression may not concatenate strings.");
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var declaring = node.Member.DeclaringType;
            var allowed = declaring == typeof(string) || declaring == typeof(DateTime) || declaring == typeof(DateTimeOffset) || declaring == typeof(TimeSpan)
                || (declaring != null && declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(Nullable<>));
            if (!allowed)
            {
                throw new ArgumentException($"A Filter expression may not read {declaring?.Name}.{node.Member.Name}.");
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;
            if (!IsAllowed(method))
            {
                throw new ArgumentException($"A Filter expression may not call {method.Name}.");
            }

            if (method.Name is not ("get_Item" or "get_Chars" or "ToString") && !_generated.Contains(node) && ++_calls > MaxCalls)
            {
                throw new ArgumentException($"A Filter expression may not make more than {MaxCalls} method calls.");
            }

            if (method.DeclaringType == typeof(string) && StringMethods.Contains(method.Name))
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.ParameterType != typeof(string) && parameter.ParameterType != typeof(char) && parameter.ParameterType != typeof(int)
                        && parameter.ParameterType != typeof(StringComparison))
                    {
                        throw new ArgumentException($"A Filter expression may not call that overload of {method.Name}.");
                    }
                }

                foreach (var argument in node.Arguments.Where(a => IsNumeric(a.Type)))
                {
                    if (ConstantNumber(argument) is not { } number || double.IsNaN(number) || Math.Abs(number) > MaxCallNumber)
                    {
                        throw new ArgumentException($"A Filter expression may only pass a constant number up to {MaxCallNumber.ToString(CultureInfo.InvariantCulture)} to {method.Name}.");
                    }
                }

                // only ordinal comparison: the culture-sensitive overloads are quadratic and cannot be interrupted
                for (var i = 0; i < node.Arguments.Count; i++)
                {
                    if (node.Arguments[i].Type == typeof(StringComparison)
                        && node.Arguments[i] is not ConstantExpression { Value: StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase })
                    {
                        throw new ArgumentException("A Filter expression may only use ordinal string comparison.");
                    }
                }

                var rewritten = Ordinal(node);
                if (rewritten != null)
                {
                    _generated.Add(rewritten);
                    return base.Visit(rewritten) ?? rewritten;
                }
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>
        /// The ordinal form of a culture-sensitive string search or comparison, or null when the call needs no rewrite (it has no string argument, already carries an
        /// ordinal <see cref="StringComparison"/>, or is not a search or comparison). Every overload of <c>IndexOf</c>, <c>LastIndexOf</c>, <c>StartsWith</c>,
        /// <c>EndsWith</c>, <c>Compare</c> and <c>CompareTo</c> that takes a string and no comparison is rewritten to its matching ordinal overload; one with no ordinal
        /// overload is refused. Culture-sensitive search is quadratic and cannot be interrupted.
        /// </summary>
        private static Expression? Ordinal(MethodCallExpression node)
        {
            var method = node.Method;
            var parameters = method.GetParameters();
            if (parameters.Any(p => p.ParameterType == typeof(StringComparison)) || !parameters.Any(p => p.ParameterType == typeof(string)))
            {
                return null; // the comparison argument was already checked to be ordinal
            }

            var types = parameters.Select(p => p.ParameterType).ToArray();
            switch (method.Name)
            {
                case "Compare" when typeof(string).GetMethod(nameof(string.CompareOrdinal), types) is { } compareOrdinal:
                    return Expression.Call(compareOrdinal, node.Arguments);
                case "CompareTo" when types.Length == 1 && types[0] == typeof(string):
                    return Expression.Call(typeof(string).GetMethod(nameof(string.CompareOrdinal), new[] { typeof(string), typeof(string) })!, node.Object!, node.Arguments[0]);
                case "IndexOf" or "LastIndexOf" or "StartsWith" or "EndsWith"
                    when typeof(string).GetMethod(method.Name, types.Append(typeof(StringComparison)).ToArray()) is { } ordinal:
                    return Expression.Call(node.Object, ordinal, node.Arguments.Append(Expression.Constant(StringComparison.Ordinal)));
                case "Compare" or "CompareTo" or "IndexOf" or "LastIndexOf" or "StartsWith" or "EndsWith":
                    throw new ArgumentException($"A Filter expression may not call that overload of {method.Name}: it has no ordinal form.");
                default:
                    return null;
            }
        }

        private static bool IsAllowed(MethodInfo method)
        {
            var declaring = method.DeclaringType;
            if (method.Name == "ToString")
            {
                return method.GetParameters().Length == 0; // a format or culture argument ("D500000000") is how a number allocates
            }

            if (declaring == typeof(string))
            {
                return StringMethods.Contains(method.Name);
            }

            if (declaring == typeof(object))
            {
                return method.Name == "Equals";
            }

            if (declaring == typeof(Math))
            {
                return MathMethods.Contains(method.Name);
            }

            if (declaring == typeof(Convert))
            {
                return ConvertMethods.Contains(method.Name);
            }

            if (declaring == typeof(Enumerable))
            {
                return method.Name == "Contains"; // the "in" operator
            }

            return declaring != null && typeof(IDictionary<string, object>).IsAssignableFrom(declaring) && method.Name is "get_Item" or "ContainsKey";
        }
    }
}
