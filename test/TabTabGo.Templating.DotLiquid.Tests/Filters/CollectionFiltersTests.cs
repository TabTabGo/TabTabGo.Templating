using System.Collections.Generic;
using System.Linq;
using DotLiquid;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Filters;

[Collection("liquid-static")]
public class CollectionFiltersTests
{
    private static Hash H(string key, object? value) => new() { [key] = value };

    [Fact]
    public void SelectMany_CollectsNonNullPropertyValues()
    {
        var items = new List<object> { H("a", 1), H("a", null), H("b", 2), H("a", 3) };
        var result = Assert.IsType<List<object>>(CollectionFilters.SelectMany(items, "a"));
        Assert.Equal(new object[] { 1, 3 }, result);
        Assert.Null(CollectionFilters.SelectMany(items, ""));
    }

    [Fact]
    public void XPathSelectMany_ReturnsAllNodeValues()
    {
        var result = CollectionFilters.XPathSelectMany("<a><b>x</b><b>y</b></a>", "/a/b").Cast<object>().ToList();
        Assert.Equal(new object[] { "x", "y" }, result);
        Assert.Equal("", CollectionFilters.XPathSelectMany("", "/a"));
    }

    [Fact]
    public void RemoveEmpty_DropsNullsAndBlankStrings()
    {
        var result = Assert.IsType<List<object>>(CollectionFilters.RemoveEmpty(new object?[] { "a", null!, " ", "", 5 }));
        Assert.Equal(new object[] { "a", 5 }, result);
        Assert.Null(CollectionFilters.RemoveEmpty(null!));
    }

    [Fact]
    public void Union_MergesAndDeduplicates()
    {
        var result = Assert.IsType<List<object>>(CollectionFilters.Union(new[] { "a", "b" }, new[] { "b", "c" }));
        Assert.Equal(new object[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void Union_EmptyFirst_ReturnsSecond()
    {
        var second = new[] { "x" };
        Assert.Same(second, CollectionFilters.Union(new string[0], second));
        Assert.Same(second, CollectionFilters.Union(null!, second));
    }

    [Fact]
    public void Flatten_WalksNestedPath()
    {
        var inner1 = H("v", 1);
        var inner2 = H("v", 2);
        var items = new List<object> { H("children", new List<object> { inner1, inner2 }) };
        var result = ((IEnumerable<object>)CollectionFilters.Flatten(items, "children")!).ToList();
        Assert.Equal(new object[] { inner1, inner2 }, result);
        Assert.Null(CollectionFilters.Flatten(null!, "children"));
        Assert.Null(CollectionFilters.Flatten(items, ""));
    }

    [Fact]
    public void Distinct_Strings()
    {
        var result = ((IEnumerable<string>)CollectionFilters.Distinct(new object[] { "a", "b", "a" })!).ToList();
        Assert.Equal(new[] { "a", "b" }, result);
        Assert.Null(CollectionFilters.Distinct(null!));
    }

    [Fact]
    public void OrderBy_ByHashProperty()
    {
        var items = new List<object> { H("n", 3), H("n", 1), H("n", 2) };
        var result = CollectionFilters.OrderBy(items, "n").Cast<Hash>().Select(h => h["n"]).ToList();
        Assert.Equal(new object[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void OrderBy_NoProperty_SortsScalars()
    {
        var result = CollectionFilters.OrderBy(new List<object> { "b", "c", "a" }, "").Cast<object>().ToList();
        Assert.Equal(new object[] { "a", "b", "c" }, result);
        Assert.Empty(CollectionFilters.OrderBy(new List<object>(), "x").Cast<object>());
    }

    [Fact]
    public void GroupBy_GroupsByPropertyValue()
    {
        var items = new List<object> { new Hash { ["t"] = "A", ["i"] = 1 }, new Hash { ["t"] = "B", ["i"] = 2 }, new Hash { ["t"] = "A", ["i"] = 3 } };
        var groups = CollectionFilters.GroupBy(items, "t").Cast<Hash>().ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal("A", groups[0]["Key"]);
        Assert.Equal(2, ((List<object>)groups[0]["Items"]).Count);
        Assert.Same(items, CollectionFilters.GroupBy(items, ""));
        Assert.Null(CollectionFilters.GroupBy(null!, "t"));
    }

    [Fact]
    public void Filter_ByExpression_ReturnsMatchingRows()
    {
        var items = new List<object> { new Hash { ["s"] = "x" }, new Hash { ["s"] = "y" } };
        var result = Assert.IsType<List<Hash>>(CollectionFilters.Filter(items, "it[\"s\"].ToString() = \"x\""));
        Assert.Single(result);
        Assert.Null(CollectionFilters.Filter(null!, "true"));
    }
}
