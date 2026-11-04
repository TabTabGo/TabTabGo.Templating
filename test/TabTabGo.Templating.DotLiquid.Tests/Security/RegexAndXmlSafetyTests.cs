using System;
using System.Diagnostics;
using System.Xml;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>S-4 and S-5: regex time-out and DTD-free XML (8.0.4).</summary>
[Collection("liquid-static")]
public class RegexAndXmlSafetyTests
{
    [Fact]
    public void GetMatch_CatastrophicPattern_ReturnsEmptyWithinTimeout()
    {
        var input = new string('a', 30) + "!";
        var watch = Stopwatch.StartNew();

        var result = ValueFilters.GetMatch(input, "^(a+)+$");

        watch.Stop();
        Assert.Equal("", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public void GetMatch_NormalPattern_StillMatches()
    {
        Assert.Equal("42", ValueFilters.GetMatch("order 42 shipped", @"\d+"));
    }

    private const string Bomb =
        "<!DOCTYPE a [<!ENTITY x \"AAAAAAAAAA\"><!ENTITY y \"&x;&x;&x;&x;&x;&x;&x;&x;&x;&x;\">]><a>&y;&y;&y;&y;&y;&y;&y;&y;&y;&y;</a>";

    [Fact]
    public void XPathSelect_Dtd_Rejected()
    {
        Assert.Throws<XmlException>(() => ValueFilters.XPathSelect(Bomb, "/a"));
    }

    [Fact]
    public void XPathSelectMany_Dtd_Rejected()
    {
        Assert.Throws<XmlException>(() => CollectionFilters.XPathSelectMany(Bomb, "/a"));
    }

    [Fact]
    public void XPathSelect_Simple_Works()
    {
        Assert.Equal("v", ValueFilters.XPathSelect("<a><b>v</b></a>", "/a/b"));
        Assert.Equal(new object[] { "1", "2" }, new System.Collections.Generic.List<object>(
            System.Linq.Enumerable.Cast<object>(CollectionFilters.XPathSelectMany("<a><b>1</b><b>2</b></a>", "/a/b"))));
    }
}
