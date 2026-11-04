using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabTabGo.Templating.Liquid;
using TabTabGo.Templating.Liquid.Filters;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

/// <summary>B-1 (per-render file system), B-2 (template cache) and S-1 (AppSetting) for 8.0.4.</summary>
[Collection("liquid-static")]
public sealed class EngineStateTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-state-" + Guid.NewGuid().ToString("N"));

    public EngineStateTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string Template(string folder, string partialText, string mainText = "{% include 'p' %}")
    {
        var dir = Path.Combine(_base, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "_p.liquid"), partialText);
        var main = Path.Combine(dir, "main.liquid");
        File.WriteAllText(main, mainText);
        return main;
    }

    [Fact]
    public void Parallel_RendersInDifferentRoots_IncludeOwnPartials()
    {
        var a = Template("a", "PARTIAL-A");
        var b = Template("b", "PARTIAL-B");
        new Liquid.TemplatingEngine().Initialize();

        var wrong = 0;
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var useA = i % 2 == 0;
            // one engine per render: sharing an instance across threads still hits the hash-cache race B-3 (fixed on v10)
            var result = (string)new Liquid.TemplatingEngine().Render(useA ? a : b, new { });
            if (result != (useA ? "PARTIAL-A" : "PARTIAL-B"))
            {
                System.Threading.Interlocked.Increment(ref wrong);
            }
        });

        Assert.Equal(0, wrong);
    }

    [Fact]
    public void Render_DoesNotAssignTheProcessWideFileSystem()
    {
        var main = Template("c", "PARTIAL-C");
        global::DotLiquid.Template.FileSystem = new global::DotLiquid.FileSystems.BlankFileSystem();

        new Liquid.TemplatingEngine().Render(main, new { });

        Assert.IsType<global::DotLiquid.FileSystems.BlankFileSystem>(global::DotLiquid.Template.FileSystem);
    }

    [Fact]
    public void Cache_FileChanged_Reparsed()
    {
        var main = Template("d", "x", "first");
        var engine = new Liquid.TemplatingEngine();
        Assert.Equal("first", engine.Render(main, new { }));

        File.WriteAllText(main, "second");
        File.SetLastWriteTimeUtc(main, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal("second", engine.Render(main, new { }));
    }

    [Fact]
    public void Cache_RelativeAndAbsolutePath_ShareOneEntry()
    {
        var main = Template("e", "x", "same");
        var engine = new Liquid.TemplatingEngine();

        engine.ParseTemplate(main);
        var before = Liquid.TemplatingEngine.ParseCount;
        engine.ParseTemplate(Path.Combine(Path.GetDirectoryName(main)!, "..", "e", "main.liquid"));

        Assert.Equal(before, Liquid.TemplatingEngine.ParseCount);
        Assert.True(engine.IsCached(Path.Combine(Path.GetDirectoryName(main)!, "..", "e", "main.liquid")));
    }

    [Fact]
    public void Parallel_SameTemplate_ParsedOnce()
    {
        var main = Template("f", "x", "{{ n }}");
        var before = Liquid.TemplatingEngine.ParseCount;

        var results = new string[64];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            results[i] = (string)new Liquid.TemplatingEngine().Render(main, new { n = i });
        });

        Assert.Equal(1, Liquid.TemplatingEngine.ParseCount - before);
        Assert.Equal(Enumerable.Range(0, 64).Select(i => i.ToString()), results);
    }

    [Fact]
    public void Cache_Disabled_ParsesEveryRender()
    {
        var main = Template("g", "x", "plain");
        var options = new TemplatingOptions { CacheTemplates = false };
        var engine = new Liquid.TemplatingEngine(options);
        var before = Liquid.TemplatingEngine.ParseCount;

        engine.Render(main, new { });
        engine.Render(main, new { });

        Assert.Equal(2, Liquid.TemplatingEngine.ParseCount - before);
        Assert.False(engine.IsCached(main));
    }

    [Fact]
    public void AppSetting_UnconfiguredKey_Empty()
    {
        var engine = new Liquid.TemplatingEngine(new TemplatingOptions());
        engine.Initialize();

        Assert.Equal("", engine.RenderString("[{{ 'ConnectionStrings:Default' | AppSetting }}]", new { }, "en-US").Replace("[", "").Replace("]", ""));
    }

    [Fact]
    public void AppSetting_ConfiguredKey_Value()
    {
        var options = new TemplatingOptions();
        options.AppSettings["Brand:Name"] = "Acme";
        var engine = new Liquid.TemplatingEngine(options);
        engine.Initialize();

        Assert.Equal("Acme", engine.RenderString("{{ 'brand:name' | AppSetting }}", new { }, "en-US"));
    }

    [Fact]
    public void AppSetting_HostSettingsFileInCurrentDirectory_IsNotRead()
    {
        // private working directory (restored afterwards): nothing is written into the test output folder
        var original = Directory.GetCurrentDirectory();
        var workDir = Path.Combine(_base, "cwd");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "appsettings.json"), "{ \"Secret\": \"s3cr3t\" }");
        Directory.SetCurrentDirectory(workDir);
        try
        {
            var engine = new Liquid.TemplatingEngine(new TemplatingOptions());
            engine.Initialize();

            var result = engine.RenderString("[{{ 'Secret' | AppSetting }}]", new { }, "en-US");

            Assert.Equal("[]", result);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void AppSetting_NoFileWatcherCreated()
    {
        var options = new TemplatingOptions();
        options.AppSettings["k"] = "v";
        using var scope = RenderScope.Begin(options, null);

        var before = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < 2000; i++)
        {
            Assert.Equal("v", ValueFilters.AppSetting("k"));
        }

        // reading from the dictionary allocates almost nothing; the old code built a configuration and a watcher per call
        Assert.True(GC.GetTotalAllocatedBytes(true) - before < 2_000_000);
    }

    [Fact]
    public void AppSetting_EmptyKey_ReturnsKey()
    {
        Assert.Equal("", ValueFilters.AppSetting(""));
        Assert.Null(ValueFilters.AppSetting(null!));
    }

    [Fact]
    public void Xml_ExceedsMaxCharacters_Throws()
    {
        using var scope = RenderScope.Begin(new TemplatingOptions { MaxXmlCharacters = 40 }, null);
        var xml = "<a>" + new string('x', 200) + "</a>";

        Assert.Throws<System.Xml.XmlException>(() => ValueFilters.XPathSelect(xml, "/a"));
    }

    [Fact]
    public void Regex_UsesConfiguredTimeout()
    {
        using var scope = RenderScope.Begin(new TemplatingOptions { RegexTimeout = TimeSpan.FromMilliseconds(50) }, null);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = ValueFilters.GetMatch(new string('a', 40) + "!", "^(a+)+$");

        Assert.Equal("", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
