using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotLiquid;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>S1: the Filter filter cannot allocate outside MaxOutputCharacters through string-growing methods or huge numeric arguments.</summary>
[Collection("liquid-static")]
public class FilterCriteriaGuardTests
{
    private static List<object> Rows() => new()
    {
        new Hash { ["id"] = 1, ["k"] = "a", ["n"] = 5000000 },
        new Hash { ["id"] = 2, ["k"] = "bb", ["n"] = 7 },
    };

    private static string Ids(object? result) => string.Join(",", ((IEnumerable)result!).Cast<Hash>().Select(h => h["id"]));

    [Theory]
    [InlineData("it[\"k\"].ToString().PadLeft(500000000).Length > 0", "PadLeft")]
    [InlineData("it[\"k\"].ToString().PadRight(5).Length > 0", "PadRight")]
    [InlineData("it[\"k\"].ToString().Replace(\"a\", \"b\") = \"b\"", "Replace")]
    [InlineData("it[\"k\"].ToString().Insert(0, \"x\").Length > 0", "Insert")]
    [InlineData("string.Concat(it[\"k\"].ToString(), \"x\").Length > 0", "Concat")]
    [InlineData("string.Join(\",\", it[\"k\"].ToString()).Length > 0", "Join")]
    [InlineData("string.Format(\"{0}\", it[\"k\"]).Length > 0", "Format")]
    [InlineData("it[\"k\"].ToString().PadLeft (5).Length > 0", "PadLeft")]
    [InlineData("it[\"k\"].ToString()  .  PadLeft\t(5).Length > 0", "PadLeft")]
    [InlineData("(it[\"k\"].ToString()).PadLeft(500000000).Length > 0", "PadLeft")]
    [InlineData("Math.Abs(it[\"k\"].ToString().PadLeft(500000000).Length) > 0", "PadLeft")]
    [InlineData("iif(true, it[\"k\"].ToString(), \"\").PadLeft(500000000).Length > 0", "PadLeft")]
    [InlineData("it[\"k\"].ToString().Substring(0, 1).PadLeft(500000000).Length > 0", "PadLeft")]
    [InlineData("it[\"k\"].ToString().ToLower().Trim().Insert(0, \"x\") = \"\"", "Insert")]
    public void Filter_StringGrowingMethods_AreRejectedBeforeAnythingRuns(string expression, string method)
    {
        var ex = Assert.Throws<ArgumentException>(() => CollectionFilters.Filter(Rows(), expression));

        Assert.Contains("may not call " + method, ex.Message);
    }

    [Theory]
    [InlineData("it[\"k\"].ToString().padleft(5).Length > 0")]
    [InlineData("it[\"k\"].ToString().PADLEFT(5).Length > 0")]
    [InlineData("it[\"k\"].ToString().Repeat(3).Length > 0")]
    [InlineData("it[\"k\"].ToString().Pad Left(5).Length > 0")]
    public void Filter_UnknownOrMisspelledNames_AreRejected(string expression)
    {
        Assert.ThrowsAny<Exception>(() => CollectionFilters.Filter(Rows(), expression));
    }

    [Theory]
    [InlineData("it[\"k\"].ToString().Substring(0, 20000000).Length > 0")]
    [InlineData("it[\"k\"].ToString().Substring(0, 5000 * 5000).Length > 0")]
    [InlineData("it[\"k\"].ToString().Substring(0, it[\"n\"].ToString().Length * 5000000).Length > 0")]
    [InlineData("it[\"k\"].ToString().Substring(0, -20000000).Length > 0")]
    [InlineData("it[\"k\"].ToString()[99999999] = 'a'")]
    [InlineData("it[\"n\"].ToString(\"D500000000\").Length > 0")]
    [InlineData("it[\"n\"].ToString(\"n999999999\").Length > 0")]
    [InlineData("it[\"n\"].ToString(\"D\" + \"500000000\").Length > 0")]
    [InlineData("it[\"k\"].ToString() + it[\"k\"].ToString() = \"\"")]
    [InlineData("it[\"k\"].GetType().Name = \"x\"")]
    public void Filter_HugeOrComputedNumbers_FormatsAndConcatenation_AreRejected(string expression)
    {
        Assert.ThrowsAny<Exception>(() => CollectionFilters.Filter(Rows(), expression));
    }

    [Theory]
    [InlineData("it.x == @0")]
    [InlineData("it[\"k\"].ToString() = @")]
    [InlineData("@")]
    [InlineData("$")]
    [InlineData("@0")]
    [InlineData("$.x")]
    [InlineData("@ and true")]
    [InlineData("$ and true")]
    [InlineData("true and @")]
    [InlineData("true and $")]
    [InlineData("it[\"k\"].ToString() = \"a\" or @")]
    [InlineData("true or $")]
    [InlineData("@@@@")]
    [InlineData("$$$$ $ @ @0 $.x")]
    public async Task Filter_AtAndDollar_TerminateQuickly(string expression)
    {
        var task = Task.Run(() =>
        {
            try
            {
                return CollectionFilters.Filter(Rows(), expression);
            }
            catch (Exception)
            {
                return null; // rejecting the expression is fine; hanging is not
            }
        });

        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)))); // a hang fails the test instead of stalling the suite
    }

    [Fact]
    public void Filter_BlockedNameInsideAStringLiteral_IsJustText()
    {
        var rows = new List<object> { new Hash { ["id"] = 1, ["k"] = "Replace(x) PadLeft(5)" } };

        Assert.Equal("1", Ids(CollectionFilters.Filter(rows, "it[\"k\"].ToString() = \"Replace(x) PadLeft(5)\"")));
        Assert.Equal("1", Ids(CollectionFilters.Filter(rows, "it[\"k\"].ToString().Contains(\"PadLeft(5)\")")));
    }

    [Fact]
    public void Filter_LegitimateForms_StillWork()
    {
        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), "it[\"k\"].ToString() = \"a\"")));
        Assert.Equal("2", Ids(CollectionFilters.Filter(Rows(), "it[\"k\"].ToString().StartsWith(\"b\")")));
        Assert.Equal("2", Ids(CollectionFilters.Filter(Rows(), "it[\"k\"].ToString().Length = 2")));
        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), "Math.Abs(1) = 1 && it[\"k\"].ToString().Substring(0, 1) = \"a\"")));
        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), "it[\"id\"].ToString() = \"1\" and (it[\"k\"].ToString() = \"a\")")));
        Assert.Equal("2", Ids(CollectionFilters.Filter(Rows(), "it[\"n\"].ToString().ToLower() = \"7\"")));
    }

    [Fact]
    public void Filter_LargeNumberOutsideAMethodCall_IsAComparison_AndStillWorks()
    {
        Assert.Equal("1", Ids(CollectionFilters.Filter(Rows(), "it[\"id\"].ToString() = \"1\" and 5000000 > 4000000")));
    }

    [Fact]
    public void Render_FilterWithPadLeft_ReportsALiquidError_WithoutAllocating()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        var data = JObject.Parse("""{ "rows": [ { "k": "a" } ] }""");
        const string template = "{% assign r = rows | Filter: 'it[\"k\"].ToString().PadLeft(500000000).Length > 0' %}[{{ r | Size }}]";

        var before = GC.GetTotalAllocatedBytes(true);
        var result = engine.RenderString(template, data, "en-US");
        var allocated = GC.GetTotalAllocatedBytes(true) - before;

        Assert.Contains("Liquid error", result);
        Assert.True(allocated < 200_000_000, $"allocated {allocated} bytes");
    }
}
