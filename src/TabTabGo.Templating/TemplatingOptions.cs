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

        /// <summary>
        /// Time-out of each regular expression DotLiquid runs while it parses a template (DotLiquid's own default is 10 s and is process-wide: the strictest value
        /// of any engine wins and is never raised). Must be positive, never infinite. Default 5 seconds; separate from <see cref="RegexTimeout"/> so that a tight
        /// filter time-out cannot fail the parse of a valid template.
        /// </summary>
        public TimeSpan ParseTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Longest template string, in characters, <c>RenderString</c> parses (template files are trusted and not limited). 0 disables the limit. Default 1 000 000.</summary>
        public long MaxTemplateCharacters { get; set; } = 1_000_000;

        /// <summary>Maximum characters (after entity expansion) of an XML document parsed by template filters. Default 10 000 000.</summary>
        public long MaxXmlCharacters { get; set; } = 10_000_000;

        /// <summary>
        /// Maximum items one <c>for</c> loop may iterate (DotLiquid's render limit: it counts per loop, not per render; nested loops each have their own count). 0 disables the limit. Default 100 000.
        /// </summary>
        public int MaxIterations { get; set; } = 100_000;

        /// <summary>Wall-clock limit of one render. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables it. Default 30 seconds.</summary>
        public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Longest string, in characters, a render may produce: the output, a <c>capture</c> block, or the result of a growing filter. A render
        /// that passes it throws <c>RenderLimitExceededException</c> (a <see cref="TimeoutException"/>); it never truncates. 0 disables the limit.
        /// Default 50 000 000.
        /// </summary>
        public long MaxOutputCharacters { get; set; } = 50_000_000;

        /// <summary>Maximum nesting depth when a data object is converted to template variables. Default 64.</summary>
        public int MaxDepth { get; set; } = 64;

        /// <summary>
        /// When false (default) the <c>FullPath</c> filter returns a path relative to the template root and error messages never
        /// contain server paths. Set to true where a host needs absolute paths (for example PDF generation from local HTML).
        /// </summary>
        public bool AllowAbsolutePaths { get; set; }
    }
}
