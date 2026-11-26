using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using DotLiquid;
using DotLiquid.Exceptions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>S-13 (architect round 6, M-4): blocks nested deeper than MaxNestingDepth are refused before DotLiquid's recursive parser can overflow the stack.</summary>
public sealed class TemplateNestingTests
{
    private static string Nested(int levels, string block = "if a") =>
        string.Concat(Enumerable.Repeat("{% " + block + " %}", levels)) + "x" + string.Concat(Enumerable.Repeat("{% end" + block.Split(' ')[0] + " %}", levels));

    /// <summary>Runs on a thread with a small stack, so that a missing guard overflows deterministically instead of depending on the machine.</summary>
    private static T OnSmallStack<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            },
            maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    [Fact]
    public void ExactlyTheLimit_Parses_OnASmallStack()
    {
        // parse only: DotLiquid's own render limit ("Nesting too deep", about 100 pushed scopes) is separate and not changed here
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", Nested(100));
        var engine = new Liquid.TemplatingEngine();

        OnSmallStack(() =>
        {
            engine.ParseTemplate(path);
            return true;
        });

    }

    [Fact]
    public void OneOverTheLimit_IsRefusedWithAClearException()
    {
        var engine = new Liquid.TemplatingEngine();

        var error = Assert.Throws<SyntaxException>(() => OnSmallStack(() => engine.RenderString(Nested(101), new { a = true })));

        Assert.Contains("MaxNestingDepth", error.Message);
    }

    [Fact]
    public void ThreeThousandNestedBlocks_AreRefused_NotAStackOverflow()
    {
        var engine = new Liquid.TemplatingEngine();

        Assert.Throws<SyntaxException>(() => OnSmallStack(() => engine.RenderString(Nested(3000), new { a = true })));
    }

    [Fact]
    public void TheLimitIsAnOption()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 3 });

        Assert.Equal("x", engine.RenderString(Nested(3), new { a = true }));
        Assert.Throws<SyntaxException>(() => engine.RenderString(Nested(4), new { a = true }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 0 }));
    }

    [Fact]
    public void SiblingBlocks_DoNotAddUp()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 2 });
        var template = string.Concat(Enumerable.Repeat("{% if a %}{% for i in (1..2) %}{{ i }}{% endfor %}{% endif %}", 50));

        Assert.Equal(string.Concat(Enumerable.Repeat("12", 50)), engine.RenderString(template, new { a = true }));
    }

    [Fact]
    public void CustomBlockTags_Count()
    {
        Template.RegisterTag<NestingProbeBlock>("nestingprobe");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 2 });

        Assert.Throws<SyntaxException>(() => engine.RenderString(Nested(3, "nestingprobe"), new { }));
        Assert.Equal("x", engine.RenderString(Nested(2, "nestingprobe"), new { }));
    }

    [Fact]
    public void TemplateFiles_AreChecked_Too()
    {
        using var dir = new TempDir();
        var path = dir.Write("t.liquid", Nested(5));

        Assert.Throws<SyntaxException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 4 }).Render(path, new { a = true }));
        Assert.Equal("x", (string)new Liquid.TemplatingEngine(new TemplatingOptions { MaxNestingDepth = 5 }).Render(path, new { a = true }));
    }

    /// <summary>A block tag that renders its body.</summary>
    private sealed class NestingProbeBlock : Block
    {
        public override void Render(Context context, TextWriter result) => RenderAll(NodeList, context, result);
    }
}
