using System;
using System.Collections.Generic;
using System.Linq;
using DotLiquid;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>S-2: dynamic LINQ cannot reflect over runtime types; FilterBy/FilterByPrefix replace data-built expressions.</summary>
[Collection("liquid-static")]
public class FilterExpressionTests
{
    private static List<object> Rows() => new()
    {
        new Hash { ["id"] = 1, ["name"] = "Alice", ["status"] = "active" },
        new Hash { ["id"] = 2, ["name"] = "Albert", ["status"] = "inactive" },
        new Hash { ["id"] = 3, ["name"] = "Bob", ["status"] = "active" },
        new Hash { ["id"] = 4, ["name"] = "Carla", ["status"] = null },
    };

    private static string Ids(object? result) => string.Join(",", ((IEnumerable<object>)result!).Cast<Hash>().Select(h => h["id"]));

    [Theory]
    [InlineData("\"\".GetType().Assembly.FullName != null")]
    [InlineData("it[\"name\"].GetType().Assembly.FullName != null")]
    [InlineData("System.Environment.GetEnvironmentVariable(\"PATH\") != null")]
    public void Filter_ReflectionOnSystemType_Rejected(string expression)
    {
        Assert.ThrowsAny<Exception>(() => CollectionFilters.Filter(Rows(), expression));
    }

    [Fact]
    public void Render_DataBuiltFilterExpression_CannotReflect()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        var data = JObject.Parse("""{ "rows": [ { "id": 1, "name": "x" } ], "evil": "\") || \"\".GetType().Assembly.FullName != null || (\"" }""");
        const string template = "{% assign expr = 'it[\"name\"].ToString() = \"' | Append: evil | Append: '\"' %}{% assign r = rows | Filter: expr %}[{{ r | Size }}]";

        var result = engine.RenderString(template, data, "en-US");

        Assert.DoesNotContain("System.Private.CoreLib", result);
        Assert.DoesNotContain("[1]", result);
    }

    [Fact]
    public void FilterBy_MatchesKeyEqualityByString()
    {
        Assert.Equal("1,3", Ids(CollectionFilters.FilterBy(Rows(), "status", "active")));
        Assert.Equal("2", Ids(CollectionFilters.FilterBy(Rows(), "id", 2)));
    }

    [Fact]
    public void FilterBy_NullValue_MatchesNullKeys()
    {
        Assert.Equal("4", Ids(CollectionFilters.FilterBy(Rows(), "status", null!)));
    }

    [Fact]
    public void FilterByPrefix_MatchesStartsWith()
    {
        Assert.Equal("1,2", Ids(CollectionFilters.FilterByPrefix(Rows(), "name", "Al")));
        Assert.Equal("", Ids(CollectionFilters.FilterByPrefix(Rows(), "name", "Zed")));
    }

    [Fact]
    public void FilterBy_NullArray_ReturnsNull()
    {
        Assert.Null(CollectionFilters.FilterBy(null!, "k", "v"));
        Assert.Null(CollectionFilters.FilterByPrefix(null!, "k", "v"));
    }

    [Theory]
    [InlineData("it[\"status\"].ToString()=\"active\"", "status", "active")]
    public void FilterBy_AgreesWithFilterForTheLisaShape(string expression, string key, string value)
    {
        var rows = Rows().Take(3).ToList(); // the fourth row has a null status, which the LINQ expression cannot call ToString() on
        Assert.Equal(Ids(CollectionFilters.Filter(rows, expression)), Ids(CollectionFilters.FilterBy(rows, key, value)));
    }

    [Fact]
    public void FilterBy_RegisteredAsLiquidFilter()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        var data = JObject.Parse("""{ "rows": [ { "id": 1, "name": "Alice" }, { "id": 2, "name": "Bob" } ] }""");

        var result = engine.RenderString("{% assign r = rows | FilterBy: 'name', 'Bob' %}{% for i in r %}{{ i.id }}{% endfor %}", data, "en-US");
        var prefix = engine.RenderString("{% assign r = rows | FilterByPrefix: 'name', 'Al' %}{% for i in r %}{{ i.id }}{% endfor %}", data, "en-US");

        Assert.Equal("2", result);
        Assert.Equal("1", prefix);
    }
}
