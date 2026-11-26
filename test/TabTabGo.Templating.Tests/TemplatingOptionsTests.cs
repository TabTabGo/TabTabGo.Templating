using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace TabTabGo.Templating.Tests;

public class TemplatingOptionsTests
{
    [Fact]
    public void Defaults_AreSafe()
    {
        var options = new TemplatingOptions();

        Assert.Null(options.TemplatesPath);
        Assert.True(options.CacheTemplates);
        Assert.Empty(options.AppSettings);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.RegexTimeout);
        Assert.Equal(10_000_000, options.MaxXmlCharacters);
        Assert.Equal(100, options.MaxNestingDepth);
        Assert.Equal("TabTabGo.Templating", TemplatingOptions.SectionName);
    }

    [Fact]
    public void AppSettings_KeysAreCaseInsensitive()
    {
        var options = new TemplatingOptions();
        options.AppSettings["Brand:Name"] = "Acme";

        Assert.Equal("Acme", options.AppSettings["brand:name"]);
    }

    private static IConfiguration Build() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TabTabGo.Templating:TemplatesPath"] = "Templates",
            ["TabTabGo.Templating:CacheTemplates"] = "false",
            ["TabTabGo.Templating:RegexTimeout"] = "00:00:01",
            ["TabTabGo.Templating:MaxXmlCharacters"] = "1234",
            ["TabTabGo.Templating:AppSettings:Brand"] = "Acme",
        })
        .Build();

    [Fact]
    public void AddTabTabGoTemplating_Configuration_BindsSectionIncludingAppSettings()
    {
        var services = new ServiceCollection();

        services.AddTabTabGoTemplating(Build());
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<TemplatingOptions>>().Value;

        Assert.Equal("Templates", options.TemplatesPath);
        Assert.False(options.CacheTemplates);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RegexTimeout);
        Assert.Equal(1234, options.MaxXmlCharacters);
        Assert.Equal("Acme", options.AppSettings["brand"]);
    }

    [Fact]
    public void AddTabTabGoTemplating_Action_ConfiguresOptions()
    {
        var services = new ServiceCollection();

        services.AddTabTabGoTemplating(o => o.AppSettings["k"] = "v");
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<TemplatingOptions>>().Value;

        Assert.Equal("v", options.AppSettings["k"]);
    }

    [Fact]
    public void AddTabTabGoTemplating_CalledTwice_StillResolves()
    {
        var services = new ServiceCollection();

        services.AddTabTabGoTemplating(Build());
        services.AddTabTabGoTemplating(Build());
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<TemplatingOptions>>().Value;

        Assert.Equal("Templates", options.TemplatesPath);
    }

    [Fact]
    public void AddTabTabGoTemplating_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddTabTabGoTemplating(Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddTabTabGoTemplating((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddTabTabGoTemplating((Action<TemplatingOptions>)null!));
    }
}
