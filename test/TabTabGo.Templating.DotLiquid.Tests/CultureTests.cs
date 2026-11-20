using System.Globalization;
using System.Text.RegularExpressions;
using TabTabGo.Templating.TestKit;

namespace TabTabGo.Templating.DotLiquid.Tests;

/// <summary>B-6 and F-11: formatting follows the render culture, parsing is invariant, the thread culture is restored.</summary>
public sealed class CultureTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Spaces(object value) => Regex.Replace((string)value, @"\s", " ");

    [Fact]
    public void Render_FrCulture_FormatsFr()
    {
        using var scope = new CultureScope("en-US");
        var path = _dir.Write("t.liquid", "{{ n | FormatNumber: 'N2' }}|{{ n | FormatNumber: 'C' }}");

        var result = Spaces(new Liquid.TemplatingEngine().Render(path, new { n = 1234.5 }, "fr-FR"));

        Assert.Equal("1 234,50|1 234,50 €", result);
    }

    [Fact]
    public void RenderString_FrCulture_FormatsFr()
    {
        using var scope = new CultureScope("en-US");

        var result = Spaces(new Liquid.TemplatingEngine().RenderString("{{ n | FormatNumber: 'N2' }}", new { n = 1234.5 }, "fr-FR"));

        Assert.Equal("1 234,50", result);
    }

    [Fact]
    public void Render_RestoresThreadCulture()
    {
        using var scope = new CultureScope("de-DE");
        var path = _dir.Write("t.liquid", "{{ n | FormatNumber: 'N1' }}");

        new Liquid.TemplatingEngine().Render(path, new { n = 1 }, "fr-FR");

        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Render_RestoresThreadCulture_WhenTheRenderThrows()
    {
        using var scope = new CultureScope("de-DE");

        Assert.ThrowsAny<Exception>(() => new Liquid.TemplatingEngine().Render(Path.Combine(_dir.Path, "missing.liquid"), new { }, "fr-FR"));
        Assert.ThrowsAny<Exception>(() => new Liquid.TemplatingEngine().RenderString("{% unknowntag %}", new { }, "fr-FR"));

        Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void Number_DeDeThread_Invariant()
    {
        using var scope = new CultureScope("de-DE");

        var result = new Liquid.TemplatingEngine().RenderString("{{ '1.5' | Number }}|{{ 3 | DivideBy: '1.5' }}", new { }, "en-US");

        Assert.Equal("1.5|2", result);
    }

    [Fact]
    public void Number_RenderCulture_UsedWhenInvariantParsingFails()
    {
        using var scope = new CultureScope("en-US");

        var result = new Liquid.TemplatingEngine().RenderString("{{ '1,5' | Number }}", new { }, "de-DE");

        Assert.Equal("1,5", Spaces(result)); // 1.5 formatted in the German render culture
    }

    [Fact]
    public void DivideBy_Zero_Zero()
    {
        using var scope = new CultureScope("en-US");

        var result = new Liquid.TemplatingEngine().RenderString("{{ 10 | DivideBy: 0 }}|{{ 0 | DivideBy: 0 }}", new { });

        Assert.Equal("0|0", result);
    }
}
