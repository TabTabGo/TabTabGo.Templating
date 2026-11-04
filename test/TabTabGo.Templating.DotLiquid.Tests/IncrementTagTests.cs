using DotLiquid.Exceptions;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

[Collection("liquid-static")]
public class IncrementTagTests
{
    private static Liquid.TemplatingEngine Engine()
    {
        var engine = new Liquid.TemplatingEngine();
        engine.Initialize();
        return engine;
    }

    [Fact]
    public void Increment_AddsOneToExistingVariable()
    {
        var result = Engine().RenderString("{% assign n = 1 %}{% increment n %}{% increment n %}{{ n }}", new { }, "en-US");
        Assert.Equal("3", result);
    }

    [Fact]
    public void Increment_WithoutVariable_FailsToParse()
    {
        var engine = Engine();
        Assert.Throws<SyntaxException>(() => engine.RenderString("{% increment %}", new { }, "en-US"));
    }
}
