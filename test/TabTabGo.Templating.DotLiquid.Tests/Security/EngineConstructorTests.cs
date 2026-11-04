using System;
using Microsoft.Extensions.Options;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests.Security;

public class EngineConstructorTests
{
    [Fact]
    public void Constructors_NullOptions_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new Liquid.TemplatingEngine((TemplatingOptions)null!));
        Assert.Throws<ArgumentNullException>(() => new Liquid.TemplatingEngine((IOptions<TemplatingOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => new Templating.OpenXml.SpreadSheet.TemplatingEngine((TemplatingOptions)null!));
    }

    [Fact]
    public void IOptionsConstructor_UsesRegisteredOptions()
    {
        var options = new TemplatingOptions();
        options.AppSettings["k"] = "from-ioptions";
        var engine = new Liquid.TemplatingEngine(Options.Create(options));
        engine.Initialize();

        Assert.Equal("from-ioptions", engine.RenderString("{{ 'k' | AppSetting }}", new { }, "en-US"));
    }
}
