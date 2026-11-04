using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Filters;

public class JObjectFiltersTests
{
    private static readonly JObject Json = JObject.Parse("""{ "a": { "b": 5, "list": [1, 2] }, "items": [ { "n": "x" }, { "n": "y" } ] }""");

    [Fact]
    public void JSelectToken_ReturnsTokenOrNull()
    {
        Assert.Equal(5, JObjectFilters.JSelectToken(Json, "$.a.b")!.Value<int>());
        Assert.Null(JObjectFilters.JSelectToken(Json, "$.missing"));
    }

    [Fact]
    public void JSelectTokenByProperty_PrefixesRoot()
    {
        Assert.Equal(5, JObjectFilters.JSelectTokenByProperty(Json, "a.b")!.Value<int>());
        Assert.Equal(5, JObjectFilters.JSelectTokenByProperty(Json, "b", "$.a.")!.Value<int>());
    }

    [Fact]
    public void JSelectTokenValues_ReturnsScalarValues()
    {
        var values = JObjectFilters.JSelectTokenValues(Json, "$.items[*].n").Select(v => v.ToString());
        Assert.Equal(new[] { "x", "y" }, values);
    }

    [Fact]
    public void JSelectTokenValuesByProperty_CombinesRootAndProperty()
    {
        var values = JObjectFilters.JSelectTokenValuesByProperty(Json, "$.items[*].", "n").Select(v => v.ToString());
        Assert.Equal(new[] { "x", "y" }, values);
    }

    [Fact]
    public void EscapeJsonString_QuotesAndEscapes()
    {
        Assert.Equal("\"a\\\"b\"", JObjectFilters.EscapeJsonString("a\"b"));
    }

    [Fact]
    public void ConvertToJObject_ParsesJson()
    {
        Assert.Equal(1, JObjectFilters.ConvertToJObject("""{"x":1}""")["x"]!.Value<int>());
        Assert.ThrowsAny<System.Exception>(() => JObjectFilters.ConvertToJObject("{not json"));
    }
}

public class CustomAndOpenXmlFiltersTests
{
    [Fact]
    public void CountryName_ReturnsEnglishName()
    {
        Assert.Equal("United Arab Emirates", CustomFilters.CountryName("AE"));
        Assert.Equal("", CustomFilters.CountryName(""));
        Assert.Null(CustomFilters.CountryName(null!));
    }

    [Fact]
    public void OpenXmlToString_WrapsValue()
    {
        Assert.Equal("5", OpenXmlFilters.OpenXmlToString(5)!.Value);
        Assert.Null(OpenXmlFilters.OpenXmlToString(null!));
    }

    [Fact]
    public void OpenXmlFormat_RemovesSpaces()
    {
        Assert.Equal("dd/MM/yyyy", OpenXmlFilters.OpenXmlFormat("dd / MM / yyyy"));
        Assert.Null(OpenXmlFilters.OpenXmlFormat(""));
    }

    [Fact]
    public void OpenXmlMaxWidth_UsesLongestValueOrHeader()
    {
        var rows = new List<JObject> { JObject.Parse("""{"n":"abcd"}"""), JObject.Parse("""{"n":"ab"}"""), JObject.Parse("""{"x":1}""") };
        Assert.Equal("4", OpenXmlFilters.OpenXmlMaxWidth(rows, "n", "hd"));
        Assert.Equal("8", OpenXmlFilters.OpenXmlMaxWidth(rows, "n", "longhead"));
        Assert.Equal("2", OpenXmlFilters.OpenXmlMaxWidth(null!, "n", "hd"));
        Assert.Equal("0", OpenXmlFilters.OpenXmlMaxWidth(new List<JObject>(), "n", ""));
    }
}
