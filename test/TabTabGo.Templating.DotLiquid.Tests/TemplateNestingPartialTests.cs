using System;
using System.Linq;
using Xunit;
using DotLiquid.Exceptions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>S-13 for partials: an included file that nests blocks deeper than MaxNestingDepth is refused before DotLiquid parses it.</summary>
[Collection("liquid-static")] // GuardedRead.OpenOverride is process-wide state that other tests set
public sealed class TemplateNestingPartialTests
{
    private static string Nested(int levels) =>
        string.Concat(Enumerable.Repeat("{% if a %}", levels)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", levels));

    [Fact]
    public void ReadTemplateFile_PartialDeeperThanTheLimit_Throws_AndAtTheLimitReads()
    {
        using var dir = new TempDir();
        dir.Write("_deep.liquid", Nested(5));
        dir.Write("_ok.liquid", Nested(3));
        var fileSystem = new Liquid.EnhancedLocalFileSystem(dir.Path);
        var context = new global::DotLiquid.Context(System.Globalization.CultureInfo.InvariantCulture);
        using var scope = Liquid.RenderScope.Begin(new TemplatingOptions { MaxNestingDepth = 3 }, fileSystem);

        Assert.Equal(Nested(3), fileSystem.ReadTemplateFile(context, "'ok'"));
        var error = Assert.Throws<SyntaxException>(() => fileSystem.ReadTemplateFile(context, "'deep'"));
        Assert.Contains("MaxNestingDepth", error.Message);
    }

    [Fact]
    public void Render_IncludedPartialNestedDeeperThanTheLimit_DoesNotParseIt()
    {
        using var dir = new TempDir();
        dir.Write("_deep.liquid", Nested(5000));
        var main = dir.Write("main.liquid", "{% include 'deep' %}");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { TemplatesPath = dir.Path });

        string? output = null;
        var thrown = Record.Exception(() => output = (string)engine.Render(main, new { a = true }));

        // never a stack overflow (that would end the test process); the include either throws or renders DotLiquid's inline error, and never the partial's body
        Assert.True(thrown is SyntaxException || (output ?? "").Contains("MaxNestingDepth", StringComparison.Ordinal), $"thrown={thrown?.GetType().Name} output={output}");
    }
}
