using System;
using System.Collections.Generic;
using Xunit;

namespace TabTabGo.Templating.Tests;

/// <summary>Exercises the abstract contract through a fake engine (the base package ships no engine).</summary>
public class TemplatingEngineContractTests
{
    private sealed class FakeEngine : TemplatingEngine
    {
        private readonly HashSet<string> _parsed = new();

        public override string EngineName => "Fake";

        public List<Type> RegisteredFilters { get; } = new();

        public override bool IsCached(string templatePath) => _parsed.Contains(templatePath);

        public override void ParseTemplate(string templatePath) => _parsed.Add(templatePath);

        public override object Render(string templatePath, object sourceData, string culture = "en-Us")
        {
            ParseTemplate(templatePath);
            return $"{templatePath}|{sourceData}|{culture}";
        }

        public override string RenderString(string templateString, object sourceData, string culture = "en-Us")
            => $"{templateString}|{sourceData}|{culture}";

        public override void RegisterFilter(Type type) => RegisteredFilters.Add(type);
    }

    [Fact]
    public void Contract_DefaultCultureIsEnUs()
    {
        TemplatingEngine engine = new FakeEngine();
        Assert.Equal("t|d|en-Us", engine.RenderString("t", "d"));
        Assert.Equal("p|d|en-Us", engine.Render("p", "d"));
    }

    [Fact]
    public void Contract_ExplicitCultureIsPassedThrough()
    {
        TemplatingEngine engine = new FakeEngine();
        Assert.Equal("t|d|ar-AE", engine.RenderString("t", "d", "ar-AE"));
    }

    [Fact]
    public void Contract_CacheAndFilterRegistration()
    {
        var engine = new FakeEngine();
        Assert.Equal("Fake", engine.EngineName);
        Assert.False(engine.IsCached("p"));
        engine.Render("p", "d");
        Assert.True(engine.IsCached("p"));
        engine.RegisterFilter(typeof(string));
        Assert.Contains(typeof(string), engine.RegisteredFilters);
    }
}
