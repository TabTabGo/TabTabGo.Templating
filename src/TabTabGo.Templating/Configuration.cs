using Microsoft.Extensions.Configuration;

namespace TabTabGo.Templating;

/// <summary>Reads the templating section from the application settings file.</summary>
public class Configuration
{
    /// <summary>Reads the <c>TabTabGo.Templating</c> section of the settings file in the application base directory (the file is optional).</summary>
    [Obsolete("Use services.AddTabTabGoTemplating(configuration) and TemplatingOptions instead.")]
    public static TemplatingConfigurationSection GetTemplatingConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var section = new TemplatingConfigurationSection();
        configuration.GetSection(TemplatingOptions.SectionName).Bind(section);
        return section;
    }
}
