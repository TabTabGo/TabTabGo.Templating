using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TabTabGo.Templating.Tests;

public class ConfigurationTests
{
    [Fact]
    public void SectionName_IsTabTabGoTemplating() => Assert.Equal("TabTabGo.Templating", TemplatingConfigurationSection.SectionName);

    [Fact]
    public void Section_BindsTemplatesPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TabTabGo.Templating:TemplatesPath"] = "Templates" })
            .Build();
        var section = new TemplatingConfigurationSection();

        configuration.GetSection(TemplatingConfigurationSection.SectionName).Bind(section);

        Assert.Equal("Templates", section.TemplatesPath);
    }

    /// <summary>
    /// Documents bug F-16 on the v8 line: the method opens a file whose name has a typo ("appsettigs"), so it throws
    /// whatever the host provides. The v10 line fixes it and replaces this test with a binding test.
    /// </summary>
    [Fact]
    public void GetTemplatingConfiguration_V8_ThrowsFileNotFound_BecauseOfTypoedFileName()
    {
        Assert.Throws<FileNotFoundException>(() => Configuration.GetTemplatingConfiguration());
    }
}
