using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

[Collection("liquid-static")]
public sealed class TemplatingEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttg-eng-" + Guid.NewGuid().ToString("N"));

    public TemplatingEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void EngineName_IsLiquid() => Assert.Equal("Liquid", new Liquid.TemplatingEngine().EngineName);

    [Fact]
    public void Initialize_IsIdempotent()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        engine.Initialize();
    }

    [Fact]
    public void ParseTemplate_MissingFile_Throws()
    {
        var engine = new Liquid.TemplatingEngine();
        Assert.Throws<FileNotFoundException>(() => engine.ParseTemplate(Path.Combine(_dir, "none.liquid")));
    }

    [Fact]
    public void ParseTemplate_Caches_IsCachedBecomesTrue()
    {
        var engine = new Liquid.TemplatingEngine();
        var path = Write("t.liquid", "x");
        Assert.False(engine.IsCached(path));
        engine.ParseTemplate(path);
        Assert.True(engine.IsCached(path));
    }

    [Fact]
    public void Render_PocoAndJObject_RenderValues()
    {
        var engine = new Liquid.TemplatingEngine();
        var path = Write("r.liquid", "{{ Name }}-{{ obj.k }}-{% for i in list %}{{ i }}{% endfor %}");
        var fromJObject = engine.Render(path, JObject.Parse("""{"Name":"N","obj":{"k":"v"},"list":[1,2]}"""));
        Assert.Equal("N-v-12", fromJObject);

        var fromPoco = engine.Render(path, new { Name = "N", list = new[] { 1, 2 } });
        Assert.Equal("N--12", fromPoco);
    }

    [Fact]
    public void Render_ResolvesIncludesFromTemplateDirectory()
    {
        var engine = new Liquid.TemplatingEngine();
        Write("_partial.liquid", "[{{ Name }}]");
        var path = Write("inc.liquid", "{% include 'partial' %}");
        Assert.Equal("[N]", engine.Render(path, new { Name = "N" }));
    }

    [Fact]
    public void RenderString_RendersWithCustomFilters_WhenInitialised()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        Assert.Equal("United Arab Emirates", engine.RenderString("{{ 'AE' | CountryName }}", new { }, "en-US"));
    }

    [Fact]
    public void RenderString_ObjectGraphWithNestedObjects_Renders()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        var result = engine.RenderString("{{ A.B.C }}", new { A = new { B = new { C = "deep" } } }, "en-US");
        Assert.Equal("deep", result);
    }

    public static class ReverseFilter
    {
        public static string ShoutTest(string input) => input.ToUpperInvariant() + "!";
    }

    [Fact]
    public void RegisterFilter_AddsCustomFilter()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        engine.RegisterFilter(typeof(ReverseFilter));
        Assert.Equal("HI!", engine.RenderString("{{ 'hi' | ShoutTest }}", new { }, "en-US"));
    }

    [Fact]
    public void Render_DifferentCulture_PassedToDotLiquid()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        var result = engine.RenderString("{{ 1234.5 | FormatNumber: 'N2', 'de-DE' }}", new { }, "en-US");
        Assert.Equal("1.234,50", result);
    }
}
