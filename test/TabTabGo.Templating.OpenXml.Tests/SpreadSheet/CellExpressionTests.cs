using System;
using System.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet;
using Xunit;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

public class CellExpressionTests
{
    [Fact]
    public void ExpressionCell_ParsesColumnAndRow()
    {
        var cell = new ExpressionCell("AB12");
        Assert.Equal("AB", cell.ColumnIndex);
        Assert.Equal(12, cell.RowIndex);
        Assert.Equal("AB12", cell.ToString());
        Assert.Equal(ExpressionType.Cell, cell.ExpressionType);
    }

    [Fact]
    public void TokenizeExpression_BinaryOperation_BuildsNodeAndCells()
    {
        var expression = new CellExpression();

        expression.TokenizeExpression("A1+B2");

        var node = Assert.IsType<BinaryOperation>(Assert.Single(expression.Nodes));
        Assert.Equal("A1+B2", node.ToString());
        Assert.Equal(new[] { "A1", "B2" }, expression.ExpressionCells.Select(c => c.ToString()));
    }

    [Theory]
    [InlineData("A1+B2+C3")]
    [InlineData("A1")]
    public void TokenizeExpression_Unsupported_ThrowsNotImplemented(string text)
    {
        Assert.Throws<NotImplementedException>(() => new CellExpression().TokenizeExpression(text));
    }

    [Fact]
    public void BinaryOperation_ToString_Cases()
    {
        var a = new ExpressionCell("A1");
        var b = new ExpressionCell("B1");
        Assert.Equal("A1*B1", new BinaryOperation { Left = a, Operation = "*", Right = b }.ToString());
        Assert.Equal("-B1", new BinaryOperation { Operation = "-", Right = b }.ToString());
        Assert.Equal("-A1", new BinaryOperation { Operation = "-", Left = a }.ToString());
        Assert.Equal("", new BinaryOperation { Operation = "+", Left = a }.ToString());
        Assert.Equal("", new BinaryOperation().ToString());
        Assert.Equal("", new BinaryOperation { Operation = "+" }.ToString());
    }

    [Fact]
    public void ExpressionMethod_ToString_SumRangeAndGeneric()
    {
        var sum = new ExpressionMethod { Method = "SUM", Parameters = { new ExpressionCell("A1"), new ExpressionCell("A5") } };
        Assert.Equal("SUM(A1:A5)", sum.ToString());

        var max = new ExpressionMethod { Method = "MAX", Parameters = { new ExpressionCell("A1"), new ExpressionCell("B1"), new ExpressionCell("C1") } };
        Assert.Equal("MAX(A1,B1,C1)", max.ToString());

        Assert.Equal("NOW()", new ExpressionMethod { Method = "NOW" }.ToString());
        Assert.Equal("", new ExpressionMethod().ToString());
    }

    [Fact]
    public void ExpressionGroup_ToString_WrapsInnerExpression()
    {
        Assert.Equal("(A1)", new ExpressionGroup { Expression = new ExpressionCell("A1") }.ToString());
        Assert.Equal("()", new ExpressionGroup().ToString());
        Assert.Equal(ExpressionType.Group, new ExpressionGroup().ExpressionType);
    }

    [Fact]
    public void BaseNode_AndConstant_Types()
    {
        Assert.Equal("", new ExpressionNode().ToString());
        Assert.Equal(ExpressionType.Constant, new ExpressionConstant().ExpressionType);
        Assert.Equal(ExpressionType.Binary, new BinaryOperation().ExpressionType);
        Assert.Equal(ExpressionType.Method, new ExpressionMethod().ExpressionType);
    }

    [Fact]
    public void ExpressionConstant_ToString_Quoted()
    {
        Assert.Equal("\"abc\"", new ExpressionConstant { Value = "abc" }.ToString());
        Assert.Equal("\"\"", new ExpressionConstant().ToString());
    }

    [Fact]
    public void TokenizeExpression_SumRange_BuildsMethodNode()
    {
        var expression = new CellExpression();

        expression.TokenizeExpression("SUM(A1:A5)");

        var node = Assert.IsType<ExpressionMethod>(Assert.Single(expression.Nodes));
        Assert.Equal("SUM(A1:A5)", node.ToString());
        Assert.Equal(new[] { "A1", "A5" }, expression.ExpressionCells.Select(c => c.ToString()));
    }

    [Fact]
    public void TokenizeExpression_OtherFunctions_StillNotImplemented()
    {
        Assert.Throws<NotImplementedException>(() => new CellExpression().TokenizeExpression("AVERAGE(A1:A5)"));
        Assert.Throws<NotImplementedException>(() => new CellExpression().TokenizeExpression("SUM(A1)"));
    }
}
