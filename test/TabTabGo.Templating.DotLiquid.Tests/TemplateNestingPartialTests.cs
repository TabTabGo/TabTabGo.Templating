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
    public void Render_IncludedPartialNestedDeeperThanTheLimit_IsRefusedWithTheSyntaxErrorAndNeverParsed()
    {
        using var dir = new TempDir();
        dir.Write("_deep.liquid", string.Concat(Enumerable.Repeat("{% if a %}", 5000)) + "PARTIAL-BODY" + string.Concat(Enumerable.Repeat("{% endif %}", 5000)));
        var main = dir.Write("main.liquid", "{% include 'deep' %}");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { TemplatesPath = dir.Path });

        var output = (string)engine.Render(main, new { a = true });

        // DotLiquid renders an exception of an include as inline error text (Display mode); the process never overflows its stack and the partial body is absent
        Assert.Contains("Liquid syntax error: The template nests blocks deeper than 100 levels (TemplatingOptions.MaxNestingDepth)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTIAL-BODY", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludedPartialWithinTheLimit_StillRenders()
    {
        using var dir = new TempDir();
        dir.Write("_ok.liquid", Nested(40).Replace("x", "PARTIAL-BODY"));
        var main = dir.Write("main.liquid", "{% include 'ok' %}");

        Assert.Equal("PARTIAL-BODY", (string)new Liquid.TemplatingEngine().Render(main, new { a = true }));
    }
}
