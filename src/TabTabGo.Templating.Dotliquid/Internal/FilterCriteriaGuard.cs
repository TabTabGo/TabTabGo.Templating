using System.Globalization;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using DotLiquid;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Compiles the criteria text of the <c>Filter</c> filter through an allow-list. A criterion runs outside the render's output accounting, so an
/// expression such as <c>it["k"].ToString().PadLeft(500000000).Length &gt; 0</c> allocates about 1 GB without reaching <c>MaxOutputCharacters</c>.
/// The text is parsed by System.Linq.Dynamic.Core into an expression tree (nothing runs while parsing) and the tree is walked: only the node kinds and the
/// methods in this class are accepted, string concatenation is refused, and a number given to a string method must be a constant of at most
/// <see cref="MaxCallNumber"/>, so a computed or huge size cannot reach <c>PadLeft</c>, <c>Substring</c>, an indexer or a format specifier.
/// There is no lexical scan of the text, so spacing, casing, nesting and string literals cannot hide a call from it.
/// </summary>
internal static class FilterCriteriaGuard
{
    /// <summary>The largest numeric constant a string method may be given.</summary>
    internal const double MaxCallNumber = 10_000;

    private static readonly ParsingConfig Config = new()
    {
        // System.Linq.Dynamic.Core 1.6+ blocks ToString()/Equals() on object by default, which existing templates rely on (ADR 0002).
        AllowEqualsAndToStringMethodsOnObject = true,
        AllowNewToEvaluateAnyType = false,
    };

    private static readonly HashSet<string> StringMethods = new(StringComparer.Ordinal)
    {
        "StartsWith", "EndsWith", "Contains", "IndexOf", "Substring", "ToLower", "ToUpper", "ToLowerInvariant", "ToUpperInvariant", "Trim", "Equals", "CompareTo", "get_Chars",
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
        var lambda = DynamicExpressionParser.ParseLambda(Config, typeof(Hash), typeof(bool), criteria);
        new Checker().Visit(lambda.Body);
        return (Func<Hash, bool>)lambda.Compile();
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
        public override Expression? Visit(Expression? node)
        {
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

            if (method.DeclaringType == typeof(string) && StringMethods.Contains(method.Name))
            {
                foreach (var argument in node.Arguments.Where(a => IsNumeric(a.Type)))
                {
                    if (ConstantNumber(argument) is not { } number || double.IsNaN(number) || Math.Abs(number) > MaxCallNumber)
                    {
                        throw new ArgumentException($"A Filter expression may only pass a constant number up to {MaxCallNumber.ToString(CultureInfo.InvariantCulture)} to {method.Name}.");
                    }
                }
            }

            return base.VisitMethodCall(node);
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
