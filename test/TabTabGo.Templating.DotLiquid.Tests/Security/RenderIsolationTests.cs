using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>
/// The parsed template is cached and shared, so each render must start from a fresh context: values assigned by one
/// render (or one thread) must never be visible to another, and includes/extends must still resolve per render.
/// </summary>
[Collection("liquid-static")]
public sealed class RenderIsolationTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-iso-" + Guid.NewGuid().ToString("N"));

    public RenderIsolationTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_base, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Render_AssignedValue_DoesNotLeakIntoNextSequentialRender()
    {
        var path = Write("leak.liquid", "{% if x %}leak={{ x }}{% endif %}{% assign x = 'set' %}[{{ x }}]");
        var engine = new Liquid.TemplatingEngine();

        var first = (string)engine.Render(path, new { });
        var second = (string)engine.Render(path, new { });

        Assert.Equal("[set]", first);
        Assert.Equal("[set]", second);
    }

    [Fact]
    public void Render_CapturedValue_DoesNotLeakIntoNextRender()
    {
        var path = Write("capture.liquid", "{% if c %}leak={{ c }}{% endif %}{% capture c %}{{ who }}{% endcapture %}{{ c }}");
        var engine = new Liquid.TemplatingEngine();

        Assert.Equal("alice", engine.Render(path, new { who = "alice" }));
        Assert.Equal("bob", engine.Render(path, new { who = "bob" }));
    }

    [Fact]
    public void Parallel_PerRenderAssign_NeverSeesAnotherRendersValue()
    {
        var path = Write("who.liquid", "{% if who %}LEAK{% endif %}{% assign who = n %}{{ who }}");
        new Liquid.TemplatingEngine().ParseTemplate(path);

        var wrong = 0;
        // one engine per render: sharing an instance across threads still hits the hash-cache race B-3 (fixed on v10)
        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var result = (string)new Liquid.TemplatingEngine().Render(path, new { n = i });
            if (result != i.ToString())
            {
                Interlocked.Increment(ref wrong);
            }
        });

        Assert.Equal(0, wrong);
    }

    [Fact]
    public void Render_Includes_StillResolvePerRender_WhenTemplateIsCached()
    {
        Write("_part.liquid", "PART[{{ v }}]");
        var path = Write("inc.liquid", "{% include 'part' %}");
        var engine = new Liquid.TemplatingEngine();

        Assert.Equal("PART[1]", engine.Render(path, new { v = 1 }));
        Assert.Equal("PART[2]", engine.Render(path, new { v = 2 }));
        Assert.True(engine.IsCached(path));
    }

    [Fact]
    public void Render_Extends_StillWorks_WhenTemplateIsCached()
    {
        Write("_layout.liquid", "<{% block body %}base{% endblock %}>");
        var path = Write("child.liquid", "{% extends 'layout' %}{% block body %}child-{{ v }}{% endblock %}");
        var engine = new Liquid.TemplatingEngine();

        Assert.Equal("<child-1>", engine.Render(path, new { v = 1 }));
        Assert.Equal("<child-2>", engine.Render(path, new { v = 2 }));
    }

    [Fact]
    public void Render_CustomFiltersAndIncrementTag_StillWork_WhenTemplateIsCached()
    {
        var path = Write("misc.liquid", "{{ 'AE' | CountryName }}|{% assign n = 1 %}{% increment n %}{{ n }}");
        var engine = new Liquid.TemplatingEngine();

        Assert.Equal("United Arab Emirates|2", engine.Render(path, new { }));
        Assert.Equal("United Arab Emirates|2", engine.Render(path, new { }));
    }

    [Fact]
    public void Render_BareRelativeTemplatePath_ResolvesIncludes()
    {
        Write("_part.liquid", "PART");
        Write("main.liquid", "{% include 'part' %}");
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_base);
        try
        {
            var result = (string)new Liquid.TemplatingEngine().Render("main.liquid", new { });

            Assert.Equal("PART", result);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void FileSystem_EmptyRoot_MeansCurrentDirectory()
    {
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_base);
        try
        {
            var path = new Liquid.EnhancedLocalFileSystem("").FullPath("part");

            Assert.Equal(Path.Combine(Path.GetFullPath("."), "_part.liquid"), Path.GetFullPath(path));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void RenderString_WithTemplatesPath_ResolvesIncludesFromThatFolder()
    {
        Write("_mail.liquid", "MAIL[{{ who }}]");
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { TemplatesPath = _base });
        engine.Initialize();

        Assert.Equal("Hi MAIL[Ann]", engine.RenderString("Hi {% include 'mail' %}", new { who = "Ann" }, "en-US"));
    }

    [Fact]
    public void RenderString_WithTemplatesPath_ConfinesIncludes()
    {
        Write("_x.liquid", "X");
        var sub = Path.Combine(_base, "sub");
        Directory.CreateDirectory(sub);
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { TemplatesPath = sub });
        engine.Initialize();

        var result = engine.RenderString("{% include '../x' %}", new { }, "en-US");

        Assert.Contains("Illegal template path", result);
    }

    [Fact]
    public void RenderString_WithoutTemplatesPath_UsesHostAssignedFileSystem()
    {
        Write("_host.liquid", "HOST");
        var previous = global::DotLiquid.Template.FileSystem;
        global::DotLiquid.Template.FileSystem = new Liquid.EnhancedLocalFileSystem(_base);
        try
        {
            var engine = new Liquid.TemplatingEngine();
            engine.Initialize();

            Assert.Equal("HOST", engine.RenderString("{% include 'host' %}", new { }, "en-US"));
        }
        finally
        {
            global::DotLiquid.Template.FileSystem = previous;
        }
    }

    [Fact]
    public void Constructor_InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = TimeSpan.FromMilliseconds(-5) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxXmlCharacters = -1 }));
    }

    [Fact]
    public void Constructor_InfiniteRegexTimeoutAndUnlimitedXml_AreAccepted()
    {
        _ = new Liquid.TemplatingEngine(new TemplatingOptions { RegexTimeout = System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, MaxXmlCharacters = 0 });
    }
}
