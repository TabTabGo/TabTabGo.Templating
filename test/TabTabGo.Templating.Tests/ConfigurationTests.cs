using Microsoft.Extensions.Configuration;

namespace TabTabGo.Templating.Tests;

public class ConfigurationTests
{
    [Fact]
    public void SectionName_IsTabTabGoTemplating() => Assert.Equal("TabTabGo.Templating", TemplatingOptions.SectionName);

    [Fact]
    public void Section_BindsTemplatesPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TabTabGo.Templating:TemplatesPath"] = "Templates" })
            .Build();
        var section = new TemplatingConfigurationSection();

        configuration.GetSection(TemplatingOptions.SectionName).Bind(section);

        Assert.Equal("Templates", section.TemplatesPath);
    }

    [Fact]
    public void GetTemplatingConfiguration_ReadsSection()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.False(File.Exists(file), "the test host must not ship a settings file");
        try
        {
            File.WriteAllText(file, """{ "TabTabGo.Templating": { "TemplatesPath": "FromFile" } }""");

#pragma warning disable CS0618 // the obsolete entry point is what F-16 repairs
            var section = Configuration.GetTemplatingConfiguration();
#pragma warning restore CS0618

            Assert.Equal("FromFile", section.TemplatesPath);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void GetTemplatingConfiguration_NoFile_ReturnsDefaults()
    {
#pragma warning disable CS0618
        var section = Configuration.GetTemplatingConfiguration();
#pragma warning restore CS0618

        Assert.Null(section.TemplatesPath);
    }

    [Fact]
    public void GetTemplatingConfiguration_IsObsolete_PointingToTheDependencyInjectionEntryPoint()
    {
        var method = typeof(Configuration).GetMethod(nameof(Configuration.GetTemplatingConfiguration))!;

        var obsolete = Assert.Single(method.GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>());
        Assert.Contains("AddTabTabGoTemplating", obsolete.Message);
    }

    [Fact]
    public void SectionNameField_IsObsolete_AndStillTheSameValue()
    {
        var field = typeof(TemplatingConfigurationSection).GetField(nameof(TemplatingConfigurationSection.SectionName))!;

        Assert.Single(field.GetCustomAttributes(typeof(ObsoleteAttribute), false));
#pragma warning disable CS0618
        Assert.Equal(TemplatingOptions.SectionName, TemplatingConfigurationSection.SectionName);
#pragma warning restore CS0618
    }
}
