using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 
/// </summary>
namespace TabTabGo.Templating.OpenXml.SpreadSheet
{

    /// <summary>
    /// SpreadSheetCell Range
    /// </summary>
    /// <seealso cref="TabTabGo.Templating.OpenXml.SpreadSheet.ExpressionNode" />
    public class ExpressionCell : ExpressionNode
    {
        public string ColumnIndex { get; set; }
        public int RowIndex { get; set; }

        public ExpressionCell()
        {
            base.ExpressionType = ExpressionType.Cell;
        }

        public ExpressionCell(string range) : this()
        {
            this.ColumnIndex = Utility.GetColumnName(range);
            this.RowIndex = Utility.GetRowIndex(range);
        }
        public override string ToString()
        {
            return ColumnIndex + RowIndex;
        }


    }

    /// <summary>
    /// Basic Node for SpreadSheet Formula
    /// </summary>
    public class ExpressionNode
    {
        public ExpressionType ExpressionType { get; set; }

        public override string ToString()
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Constant Node "string" value
    /// </summary>
    /// <seealso cref="TabTabGo.Templating.OpenXml.SpreadSheet.ExpressionNode" />
    public class ExpressionConstant : ExpressionNode
    {
        public ExpressionConstant()
        {
            base.ExpressionType = ExpressionType.Constant;
        }

        public string Value { get; set; }

        public override string ToString()
        {
            return "\"" + Value ?? string.Empty + "\"";
        }
    }

    /// <summary>
    /// Formula between ( )
    /// </summary>
    /// <seealso cref="TabTabGo.Templating.OpenXml.SpreadSheet.ExpressionNode" />
    public class ExpressionGroup : ExpressionNode
    {
        public ExpressionGroup()
        {
            base.ExpressionType = ExpressionType.Group;
        }

        public ExpressionNode Expression { get; set; }

        public override string ToString()
        {
            return "(" + (Expression?.ToString() ?? string.Empty) + ")";
        }
    }

    /// <summary>
    /// Binary Expression where formule = [Left Node] [operation] [Right Node]
    /// </summary>
    /// <seealso cref="TabTabGo.Templating.OpenXml.SpreadSheet.ExpressionNode" />
    public class BinaryOperation : ExpressionNode
    {
        public BinaryOperation()
        {
            base.ExpressionType = ExpressionType.Binary;
        }
        public string Operation { get; set; }
        public ExpressionNode Left { get; set; }
        public ExpressionNode Right { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Operation)) return string.Empty;
            if (Right == null && Left == null) return string.Empty;

            if (Operation == "-" && Left == null && Right != null)
            {
                return Operation + Right.ToString();
            }
            else if (Operation == "-" && Left != null && Right == null)
            {
                return Operation + Left.ToString();
            }
            else if (Left == null || Right == null)
            {
                return string.Empty;
            }
            else
                return Left.ToString() + Operation + Right.ToString();
        }
    }

    /// <summary>
    /// Formula use Method
    /// </summary>
    /// <seealso cref="TabTabGo.Templating.OpenXml.SpreadSheet.ExpressionNode" />
    public class ExpressionMethod : ExpressionNode
    {
        public ExpressionMethod()
        {
            base.ExpressionType = ExpressionType.Method;
        }
        public List<ExpressionNode> Parameters { get; set; } = new List<ExpressionNode>();
        public string Method { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Method)) return string.Empty;
            if (Method.ToUpper() == "SUM" && Parameters.Count == 2)
            {
                return $"{Method}({Parameters[0]}:{Parameters[1]})";
            }
            else
            {
                var methodBuilder = new StringBuilder(Method + "(");
                var parametersAdded = false;
                foreach (var parameter in Parameters)
                {
                    methodBuilder.Append(parameter.ToString() + ",");
                    parametersAdded = true;
                }
                if (parametersAdded)
                {
                    methodBuilder.Remove(methodBuilder.Length - 1, 1);
                }

                methodBuilder.Append(")");
                return methodBuilder.ToString();
            }
        }
    }

    public enum ExpressionType
    {
        None,
        Constant,
        Binary,
        Method,
        Cell,
        Group
    }
    public class CellExpression
    {
        public List<ExpressionCell> ExpressionCells { get; set; } = new List<ExpressionCell>();
        public Queue<ExpressionNode> Nodes { get; set; } = new Queue<ExpressionNode>();
        /// <summary>
        /// Converts a standard infix expression to list of tokens in
        /// postfix order.
        /// TODO implement Shunting-yard algorithm or Operator-precedence parser  
        /// </summary>
        /// <param name="expression">Expression to evaluate</param>
        /// <returns></returns>
        public void TokenizeExpression(string expression)
        {
            var queue = new Queue<ExpressionNode>();
            Regex re = new Regex(@"([\+\-\*\(\)\^\/\ ])");
            List<string> tokens = re.Split(expression).Select(t => t.Trim()).Where(t => t != "").ToList();

            // var tokens = expression.Split('-', '+', '*', '/').ToList();
            if (tokens.Count == 3)
            {
                int index = 0;
                var operationExpression = new BinaryOperation()
                {
                    Left = new ExpressionCell(tokens[index++]),
                    Operation = tokens[index++],
                    Right = new ExpressionCell(tokens[index++])
                };
                ExpressionCells.Add((ExpressionCell)operationExpression.Left);
                ExpressionCells.Add((ExpressionCell)operationExpression.Right);
                queue.Enqueue(operationExpression);
            }
            else if (tokens.Count == 0)
            {
                if (expression.StartsWith("SUM"))
                {
                    tokens = expression.Split('(', ')', ':').ToList();
                    var method = new ExpressionMethod()
                    {
                        Method = tokens[0],
                        Parameters = new List<ExpressionNode>()
                        {
                            new ExpressionCell(tokens[2]),
                            new ExpressionCell(tokens[4])
                        }
                    };
                    ExpressionCells.AddRange(method.Parameters.Cast<ExpressionCell>());
                    queue.Enqueue(method);
                }
                else
                {
                    throw new NotImplementedException("Advance fourmla is not implemnted for now");
                }

            }
            else
            {
                throw new NotImplementedException("Advance fourmla is not implemnted for now");
            }


            //Stack<ExpressionNode> stack = new Stack<ExpressionNode>();
            //Regex re = new Regex(@"([\+\-\*\(\)\^\/\ ])");
            //var tokenList = re.Split(expression).Select(t => t.Trim()).Where(t => t != "").ToList();
            //var currentType = ExpressionType.None;
            //foreach (var token in tokenList)
            //{
            //    if (token == "(" && currentType != ExpressionType.Method)
            //    {
            //        currentType = ExpressionType.Group;
            //        stack.Push(new );
            //    }
            //}


            this.Nodes = queue;
        }


    }
}