using DotLiquid.Exceptions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>S8: the template cache is keyed by MaxNestingDepth too, so a looser engine cannot hand a deep template to a stricter one.</summary>
[Collection("liquid-static")]
public sealed class TemplateCacheKeyTests
{
    private static string Nested(int levels) =>
        string.Concat(Enumerable.Repeat("{% if a %}", levels)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", levels));

    [Fact]
    public void StricterEngine_DoesNotReceiveATemplateParsedUnderALargerDepth()
    {
        using var dir = new TempDir();
        var path = dir.Write("deep.liquid", Nested(10));
        var loose = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 100 });
        var strict = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 3 });

        Assert.Equal("x", loose.Render(path, new { a = true }));

        Assert.False(strict.IsCached(path));
        Assert.Throws<SyntaxException>(() => strict.Render(path, new { a = true }));
        Assert.Equal("x", loose.Render(path, new { a = true })); // the loose entry is untouched
    }

    [Fact]
    public void SameDepth_SharesTheEntry_AndAnEditIsParsedAgain()
    {
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", "one");
        var first = new Liquid.TemplatingEngine();
        var second = new Liquid.TemplatingEngine();

        first.ParseTemplate(path);
        var afterFirst = Liquid.TemplatingEngine.ParseCount;
        second.ParseTemplate(path);
        Assert.Equal(afterFirst, Liquid.TemplatingEngine.ParseCount);

        File.WriteAllText(path, "two");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.Equal("two", first.Render(path, new { }));
        Assert.Equal(afterFirst + 1, Liquid.TemplatingEngine.ParseCount);
    }

    [Fact]
    public void CacheHit_DoesNotParse()
    {
        using var dir = new TempDir();
        var path = dir.Write("hit.liquid", "plain");
        var engine = new Liquid.TemplatingEngine();
        engine.Render(path, new { });
        var before = Liquid.TemplatingEngine.ParseCount;

        for (var i = 0; i < 20; i++)
        {
            engine.Render(path, new { });
        }

        Assert.Equal(before, Liquid.TemplatingEngine.ParseCount);
    }
}
