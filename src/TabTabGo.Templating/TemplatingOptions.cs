using System;
using System.Collections.Generic;

namespace TabTabGo.Templating
{
    /// <summary>
    /// Options shared by the templating engines. Bind them from the <see cref="SectionName"/> section with
    /// <see cref="TemplatingServiceCollectionExtensions.AddTabTabGoTemplating(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>
    /// or pass an instance to an engine constructor.
    /// </summary>
    public sealed class TemplatingOptions
    {
        /// <summary>Name of the section the options are bound from.</summary>
        public const string SectionName = "TabTabGo.Templating";

        /// <summary>Base path of the templates (same key as <see cref="TemplatingConfigurationSection.TemplatesPath"/>).</summary>
        public string? TemplatesPath { get; set; }

        /// <summary>
        /// When true (default) parsed templates are cached by full path and last write time, so an edited file is parsed
        /// again. When false every render parses the template.
        /// </summary>
        public bool CacheTemplates { get; set; } = true;

        /// <summary>
        /// The only values the <c>AppSetting</c> filter can return. Templates never read the host settings or any
        /// file directly. Keys are case-insensitive.
        /// </summary>
        public IDictionary<string, string?> AppSettings { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Folder the OpenXml JSON-workbook mode may load stylesheets from (<c>Workbook.Stylesheets.TemplatePath</c>). A relative
        /// value is resolved against the application base directory; when unset the base directory itself is the root.
        /// Independent of <see cref="TemplatesPath"/>.
        /// </summary>
        public string? StylesheetRoot { get; set; }

        /// <summary>Time-out of regular expressions evaluated by template filters. Default 200 ms.</summary>
        public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>Maximum characters (after entity expansion) of an XML document parsed by template filters. Default 10 000 000.</summary>
        public long MaxXmlCharacters { get; set; } = 10_000_000;
    }
}
