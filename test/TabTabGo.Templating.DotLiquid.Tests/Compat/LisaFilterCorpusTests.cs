using System.Globalization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Compat;

/// <summary>
/// Compatibility corpus for the <c>Filter</c> filter (spec 11.4, FR-4). Each expression shape is one measured in Lisa
/// templates. The expected ids were recorded once on master (System.Linq.Dynamic.Core 1.3.14, baseline A2) and must stay
/// identical on 1.7.4.
/// </summary>
[Collection("liquid-static")]
public class LisaFilterCorpusTests
{
    private const string Items = """
        {
          "items": [
            { "id": 1, "name": "Alice",  "status": "active",   "note": "vip",  "qty": 5 },
            { "id": 2, "name": "Albert", "status": "inactive", "note": null,   "qty": 0 },
            { "id": 3, "name": "Bob",    "status": "active",   "note": null,   "qty": 7 },
            { "id": 4, "name": "Carla",  "status": "active",   "note": "late", "qty": 1 },
            { "id": 5, "name": "Dan",    "status": "pending",  "note": "late", "qty": 2 }
          ]
        }
        """;

    [Theory]
    [InlineData("it[\"status\"].ToString()=\"active\"", "1,3,4")]
    [InlineData("it[\"name\"].ToString().StartsWith(\"Al\")", "1,2")]
    [InlineData("it[\"status\"].ToString() == \"active\"", "1,3,4")]
    [InlineData("it[\"note\"] == null", "2,3")]
    [InlineData("it[\"note\"] != null", "1,4,5")]
    [InlineData("it[\"name\"].ToString().Contains(\"a\") and it[\"status\"].ToString()=\"active\"", "4")]
    [InlineData("it[\"qty\"].ToString()=\"7\"", "3")]
    public void Filter_LisaCorpus_Unchanged(string expression, string expectedIds)
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize(); // RenderString does not initialise the engine (bug B-4, fixed on v10)
        const string template =
            "{% assign r = items | Filter: expr %}{% for i in r %}{{ i.id }}{% unless forloop.last %},{% endunless %}{% endfor %}";

        var data = JObject.Parse(Items);
        data["expr"] = expression;

        var result = engine.RenderString(template, data, "en-US");

        Assert.Equal(expectedIds, result);
    }
}
