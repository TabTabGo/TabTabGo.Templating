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
        if (criteria.Length > MaxCriteriaCharacters)
        {
            throw new ArgumentException($"A Filter expression may not be longer than {MaxCriteriaCharacters} characters.");
        }

        CheckNesting(criteria);

        Func<Hash, bool>? predicate = null;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var lambda = DynamicExpressionParser.ParseLambda(Config, typeof(Hash), typeof(bool), criteria);
                    var checker = new Checker();
                    var body = checker.Visit(lambda.Body)!;
                    predicate = Expression.Lambda<Func<Hash, bool>>(body, lambda.Parameters).Compile();
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
    private static void CheckNesting(string criteria)
    {
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

            if (c == '(')
            {
                parentheses++;
                run = 0;
            }
            else if (c == ')')
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

            if (parentheses + run > MaxNesting)
            {
                throw new ArgumentException($"A Filter expression may not nest parentheses, signs or 'not' deeper than {MaxNesting} levels.");
            }

            i++;
        }
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

            if (method.Name is not ("get_Item" or "get_Chars" or "ToString") && ++_calls > MaxCalls)
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
                    return base.Visit(rewritten) ?? rewritten;
                }
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>The ordinal form of a culture-sensitive one-argument call, or null when the call is already ordinal.</summary>
        private static Expression? Ordinal(MethodCallExpression node)
        {
            var method = node.Method;
            var parameters = method.GetParameters();
            if (method.Name == "Compare" && parameters.Length == 2 && parameters.All(p => p.ParameterType == typeof(string)))
            {
                // string < and > become string.Compare, which is culture-sensitive; the ordinal comparison gives the same answer for the usual data
                return Expression.Call(typeof(string).GetMethod(nameof(string.CompareOrdinal), new[] { typeof(string), typeof(string) })!, node.Arguments[0], node.Arguments[1]);
            }

            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                return null;
            }

            if (method.Name == "CompareTo")
            {
                return Expression.Call(typeof(string).GetMethod(nameof(string.CompareOrdinal), new[] { typeof(string), typeof(string) })!, node.Object!, node.Arguments[0]);
            }

            if (method.Name is "IndexOf" or "StartsWith" or "EndsWith")
            {
                var ordinal = typeof(string).GetMethod(method.Name, new[] { typeof(string), typeof(StringComparison) })!;
                return Expression.Call(node.Object, ordinal, node.Arguments[0], Expression.Constant(StringComparison.Ordinal));
            }

            return null;
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
