using System.Text;
using System.Text.RegularExpressions;

namespace TabTabGo.Templating.OpenXml.SpreadSheet;

/// <summary>A cell reference inside a formula.</summary>
/// <seealso cref="ExpressionNode" />
public class ExpressionCell : ExpressionNode
{
    /// <summary>Creates an empty reference.</summary>
    public ExpressionCell()
    {
        ExpressionType = ExpressionType.Cell;
    }

    /// <summary>Creates a reference from <c>A1</c>-style text.</summary>
    /// <param name="range">The cell reference.</param>
    public ExpressionCell(string range)
        : this()
    {
        ColumnIndex = Utility.GetColumnName(range);
        RowIndex = Utility.GetRowIndex(range);
    }

    /// <summary>The column letters.</summary>
    public string ColumnIndex { get; set; } = string.Empty;

    /// <summary>The row number.</summary>
    public int RowIndex { get; set; }

    /// <inheritdoc />
    public override string ToString() => ColumnIndex + RowIndex;
}

/// <summary>Basic node of a spreadsheet formula.</summary>
public class ExpressionNode
{
    /// <summary>The kind of node.</summary>
    public ExpressionType ExpressionType { get; set; }

    /// <inheritdoc />
    public override string ToString() => string.Empty;
}

/// <summary>A constant text value.</summary>
/// <seealso cref="ExpressionNode" />
public class ExpressionConstant : ExpressionNode
{
    /// <summary>Creates a constant node.</summary>
    public ExpressionConstant()
    {
        ExpressionType = ExpressionType.Constant;
    }

    /// <summary>The text.</summary>
    public string? Value { get; set; }

    /// <inheritdoc />
    public override string ToString() => "\"" + (Value ?? string.Empty) + "\"";
}

/// <summary>A formula between parentheses.</summary>
/// <seealso cref="ExpressionNode" />
public class ExpressionGroup : ExpressionNode
{
    /// <summary>Creates a group node.</summary>
    public ExpressionGroup()
    {
        ExpressionType = ExpressionType.Group;
    }

    /// <summary>The inner formula.</summary>
    public ExpressionNode? Expression { get; set; }

    /// <inheritdoc />
    public override string ToString() => "(" + (Expression?.ToString() ?? string.Empty) + ")";
}

/// <summary>A binary expression: left node, operation, right node.</summary>
/// <seealso cref="ExpressionNode" />
public class BinaryOperation : ExpressionNode
{
    /// <summary>Creates a binary node.</summary>
    public BinaryOperation()
    {
        ExpressionType = ExpressionType.Binary;
    }

    /// <summary>The operator, for example <c>+</c>.</summary>
    public string? Operation { get; set; }

    /// <summary>The left operand.</summary>
    public ExpressionNode? Left { get; set; }

    /// <summary>The right operand.</summary>
    public ExpressionNode? Right { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        if (string.IsNullOrEmpty(Operation) || (Right == null && Left == null))
        {
            return string.Empty;
        }

        if (Operation == "-" && Left == null && Right != null)
        {
            return Operation + Right;
        }

        if (Operation == "-" && Left != null && Right == null)
        {
            return Operation + Left;
        }

        return Left == null || Right == null ? string.Empty : Left + Operation + Right;
    }
}

/// <summary>A function call.</summary>
/// <seealso cref="ExpressionNode" />
public class ExpressionMethod : ExpressionNode
{
    /// <summary>Creates a method node.</summary>
    public ExpressionMethod()
    {
        ExpressionType = ExpressionType.Method;
    }

    /// <summary>The arguments.</summary>
    public List<ExpressionNode> Parameters { get; set; } = new();

    /// <summary>The function name.</summary>
    public string? Method { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        if (string.IsNullOrEmpty(Method))
        {
            return string.Empty;
        }

        if (Method.Equals("SUM", StringComparison.OrdinalIgnoreCase) && Parameters.Count == 2)
        {
            return $"{Method}({Parameters[0]}:{Parameters[1]})";
        }

        var builder = new StringBuilder(Method + "(");
        builder.Append(string.Join(",", Parameters.Select(parameter => parameter.ToString())));
        return builder.Append(')').ToString();
    }
}

/// <summary>The kinds of formula node.</summary>
public enum ExpressionType
{
    /// <summary>No kind.</summary>
    None,

    /// <summary>A constant.</summary>
    Constant,

    /// <summary>A binary operation.</summary>
    Binary,

    /// <summary>A function call.</summary>
    Method,

    /// <summary>A cell reference.</summary>
    Cell,

    /// <summary>A parenthesised group.</summary>
    Group,
}

/// <summary>Parses the simple formulas the engine understands (<c>A1+B1</c> and <c>SUM(A1:A5)</c>).</summary>
public class CellExpression
{
    private static readonly Regex Tokens = new(@"([\+\-\*\(\)\^\/\ ])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex SumRange = new(@"^SUM\(\s*([A-Za-z]+\d+)\s*:\s*([A-Za-z]+\d+)\s*\)$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>The cell references found in the formula.</summary>
    public List<ExpressionCell> ExpressionCells { get; set; } = new();

    /// <summary>The parsed nodes.</summary>
    public Queue<ExpressionNode> Nodes { get; set; } = new();

    /// <summary>Parses a formula into nodes. Supported: <c>A1+B2</c> (one binary operation between two cells) and <c>SUM(A1:A5)</c>.</summary>
    /// <param name="expression">The formula text without <c>=</c>.</param>
    /// <exception cref="NotImplementedException">The formula is more complex than the supported shapes.</exception>
    public void TokenizeExpression(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // the function shape first: the generic tokenizer would split SUM(A1:A5) at its parentheses
        var sum = SumRange.Match(expression.Trim());
        if (sum.Success)
        {
            var method = new ExpressionMethod
            {
                Method = "SUM",
                Parameters = new List<ExpressionNode> { new ExpressionCell(sum.Groups[1].Value), new ExpressionCell(sum.Groups[2].Value) },
            };
            ExpressionCells.AddRange(method.Parameters.Cast<ExpressionCell>());
            Nodes = new Queue<ExpressionNode>(new ExpressionNode[] { method });
            return;
        }

        var tokens = Tokens.Split(expression).Select(token => token.Trim()).Where(token => token != string.Empty).ToList();
        if (tokens.Count != 3)
        {
            throw new NotImplementedException("Advanced formulas are not implemented: only 'A1+B2' and 'SUM(A1:A5)' are supported.");
        }

        var operation = new BinaryOperation
        {
            Left = new ExpressionCell(tokens[0]),
            Operation = tokens[1],
            Right = new ExpressionCell(tokens[2]),
        };
        ExpressionCells.Add((ExpressionCell)operation.Left);
        ExpressionCells.Add((ExpressionCell)operation.Right);
        Nodes = new Queue<ExpressionNode>(new ExpressionNode[] { operation });
    }
}
