using System;
using DotLiquid.Exceptions;
using TabTabGo.Templating.TestKit;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>8.0.5: the template-file cache is keyed by MaxNestingDepth, so a stricter engine never reuses a template another engine parsed under a looser limit.</summary>
public sealed class TemplateNestingCacheTests
{
    [Fact]
    public void CachedTemplate_ParsedUnderALooserLimit_IsNotServedToAStricterEngine()
    {
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", "{% if a %}{% if a %}{% if a %}x{% endif %}{% endif %}{% endif %}");
        var loose = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 10 });
        var strict = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 2 });

        Assert.Equal("x", (string)loose.Render(path, new { a = true }));

        Assert.True(loose.IsCached(path));
        Assert.False(strict.IsCached(path));
        Assert.Throws<SyntaxException>(() => strict.Render(path, new { a = true }));
    }
}

/// <summary>8.0.5: MaxNestingDepth is validated by the engine constructor.</summary>
public sealed class TemplateNestingOptionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_MaxNestingDepthBelowOne_Throws(int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = depth }));
    }
}
