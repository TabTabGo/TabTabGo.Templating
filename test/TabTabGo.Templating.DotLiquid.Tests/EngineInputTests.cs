using System.Reflection;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>B-4 (first call), B-5 (null and list data) and the depth limit of the object-to-variables conversion.</summary>
public sealed class EngineInputTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Puts the process-wide DotLiquid state back to "no engine was ever used".</summary>
    private static void ForgetEveryInitialisation()
    {
        typeof(Liquid.TemplatingEngine).GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        global::DotLiquid.Template.NamingConvention = new global::DotLiquid.NamingConventions.RubyNamingConvention();
    }

    [Fact]
    public void RenderString_FirstCall_CustomFiltersRegistered()
    {
        ForgetEveryInitialisation();
        try
        {
            var result = new Liquid.TemplatingEngine().RenderString("{{ 'AE' | CountryName }}", new { });

            Assert.Equal("United Arab Emirates", result);
        }
        finally
        {
            new Liquid.TemplatingEngine().Initialize();
        }
    }

    [Fact]
    public void ParseTemplate_FirstCall_CustomFiltersRegistered()
    {
        var path = _dir.Write("t.liquid", "{{ 'AE' | CountryName }}");
        ForgetEveryInitialisation();
        try
        {
            var engine = new Liquid.TemplatingEngine();
            engine.ParseTemplate(path);

            Assert.Equal("United Arab Emirates", engine.Render(path, new { }));
        }
        finally
        {
            new Liquid.TemplatingEngine().Initialize();
        }
    }

    [Fact]
    public void Render_NullItemInList_Renders()
    {
        var path = _dir.Write("t.liquid", "{% for i in items %}[{{ i }}]{% endfor %}");

        var result = new Liquid.TemplatingEngine().Render(path, new { items = new object?[] { "a", null, "b" } });

        Assert.Equal("[a][][b]", result);
    }

    [Fact]
    public void Render_NullData_EmptyModel()
    {
        var path = _dir.Write("t.liquid", "[{{ model }}]");

        var result = new Liquid.TemplatingEngine().Render(path, null!);

        Assert.Equal("[]", result);
    }

    [Fact]
    public void RenderString_NullData_EmptyModel()
    {
        Assert.Equal("[]", new Liquid.TemplatingEngine().RenderString("[{{ model }}]", null!));
    }

    [Fact]
    public void Render_ListRoot_AsModel()
    {
        var path = _dir.Write("t.liquid", "{% for i in model %}{{ i }},{% endfor %}");

        var fromList = new Liquid.TemplatingEngine().Render(path, new List<int> { 1, 2, 3 });
        var fromArray = new Liquid.TemplatingEngine().Render(path, JArray.Parse("[4,5]"));

        Assert.Equal("1,2,3,", fromList);
        Assert.Equal("4,5,", fromArray);
    }

    [Fact]
    public void Render_ObjectRoot_StillExposesItsMembersDirectly()
    {
        var path = _dir.Write("t.liquid", "{{ name }}");

        Assert.Equal("x", new Liquid.TemplatingEngine().Render(path, new { name = "x" }));
    }

    private sealed class Node
    {
        public string Name { get; set; } = "";

        public Node? Next { get; set; }
    }

    [Fact]
    public void Render_ObjectGraphDeeperThanMaxDepth_IsCutOffNotAStackOverflow()
    {
        var head = new Node { Name = "0" };
        var tail = head;
        for (var i = 1; i < 200; i++)
        {
            tail.Next = new Node { Name = i.ToString() };
            tail = tail.Next;
        }

        var engine = new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 5 });

        // the root is depth 0; objects up to MaxDepth levels below it are converted, deeper ones are null
        var result = engine.RenderString("{{ Next.Next.Next.Next.Next.Name }}|{{ Next.Next.Next.Next.Next.Next.Name }}", head);

        Assert.Equal("5|", result);
    }

    [Fact]
    public void Render_CyclicObjectGraph_Terminates()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b", Next = a };
        a.Next = b;

        var result = new Liquid.TemplatingEngine().RenderString("{{ Name }}{{ Next.Name }}{{ Next.Next.Name }}", a);

        Assert.Equal("aba", result);
    }

    [Fact]
    public void Constructor_InvalidLimits_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxDepth = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxIterations = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { MaxOutputCharacters = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = TimeSpan.Zero }));
        _ = new Liquid.TemplatingEngine(new TemplatingOptions { RenderTimeout = Timeout.InfiniteTimeSpan, MaxIterations = 0, MaxOutputCharacters = 0 });
    }
}
