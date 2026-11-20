namespace TabTabGo.Templating;

/// <summary>Configuration section of the engines. Prefer <see cref="TemplatingOptions"/> with <c>AddTabTabGoTemplating</c>.</summary>
public class TemplatingConfigurationSection
{
    /// <summary>Name of the section.</summary>
    [Obsolete("Use TemplatingOptions.SectionName.")]
    public static string SectionName = TemplatingOptions.SectionName;

    /// <summary>Base path of the templates.</summary>
    public string? TemplatesPath { get; set; }
}
