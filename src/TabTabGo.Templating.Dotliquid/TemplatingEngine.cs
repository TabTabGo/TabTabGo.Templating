using System.Collections.Concurrent;
using System.Globalization;
using DotLiquid;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using TabTabGo.Templating.Liquid.Internal;
using TabTabGo.Templating.Liquid.Tags;

namespace TabTabGo.Templating.Liquid;

/// <summary>
/// DotLiquid implementation of <see cref="TabTabGo.Templating.TemplatingEngine"/>. One instance is safe to share between threads:
/// every render builds its own context, variables and file system (ADR 0003).
/// </summary>
public class TemplatingEngine : TabTabGo.Templating.TemplatingEngine
{
    private static readonly object InitLock = new();
    private static volatile bool _isInitialized;
    private static readonly ConcurrentDictionary<(string Path, DateTime LastWriteUtc), Lazy<Template>> TemplateCache = new();
    private static long _parseCount;

    private readonly TemplatingOptions _options;

    /// <summary>Creates an engine with default <see cref="TemplatingOptions"/>.</summary>
    public TemplatingEngine() : this(new TemplatingOptions())
    {
    }

    /// <summary>Creates an engine that applies <paramref name="options"/> to every render.</summary>
    /// <param name="options">The options (cache switch, allowed settings, regex time-out, XML size cap, render limits).</param>
    /// <exception cref="ArgumentOutOfRangeException">A numeric option is out of range.</exception>
    public TemplatingEngine(TemplatingOptions options)
    {
        _options = TemplatingOptionsValidator.Validate(options, validateRenderLimits: true);
    }

    /// <summary>Creates an engine from the options registered in dependency injection.</summary>
    /// <param name="options">The registered options.</param>
    public TemplatingEngine(IOptions<TemplatingOptions> options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value)
    {
    }

    /// <inheritdoc />
    public override string EngineName => "Liquid";

    /// <summary>Number of template files parsed since the process started (used by tests to prove single parsing).</summary>
    internal static long ParseCount => Interlocked.Read(ref _parseCount);

    /// <summary>
    /// Registers the filters, the <c>increment</c> tag and the naming convention once per process, and lowers DotLiquid's
    /// process-wide regex time-out to this engine's <see cref="TemplatingOptions.RegexTimeout"/> ("the strictest engine wins"; it is never raised).
    /// Every entry point calls it, so calling it yourself is optional.
    /// </summary>
    public void Initialize()
    {
        lock (InitLock)
        {
            if (_options.RegexTimeout != Timeout.InfiniteTimeSpan && _options.RegexTimeout < Template.RegexTimeOut)
            {
                Template.RegexTimeOut = _options.RegexTimeout;
            }

            if (_isInitialized)
            {
                return;
            }

            Template.NamingConvention = new DotLiquid.NamingConventions.CSharpNamingConvention();
            DotLiquid.Liquid.UseRubyDateFormat = false;

            Template.RegisterFilter(typeof(CollectionFilters));
            Template.RegisterFilter(typeof(ValueFilters));
            Template.RegisterFilter(typeof(CustomFilters));
            Template.RegisterFilter(typeof(JObjectFilters));
            Template.RegisterFilter(typeof(OpenXmlFilters));
            Template.RegisterFilter(typeof(GuardedStringFilters)); // after the stock filters: the guarded versions win
            Template.RegisterTag<GuardedCapture>("capture");
            Template.RegisterSafeType(typeof(JToken), Array.Empty<string>());
            Template.RegisterTag<Increment>("increment");

            _isInitialized = true;
        }
    }

    /// <inheritdoc />
    public override bool IsCached(string templatePath)
    {
        if (!_options.CacheTemplates || string.IsNullOrEmpty(templatePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(templatePath);
        return File.Exists(fullPath)
            && TemplateCache.TryGetValue((fullPath, File.GetLastWriteTimeUtc(fullPath)), out var cached)
            && cached.IsValueCreated;
    }

    /// <inheritdoc />
    public override void ParseTemplate(string templatePath)
    {
        EnsureInitialized();
        GetTemplate(templatePath);
    }

    /// <inheritdoc />
    public override object Render(string templatePath, object? sourceData, string culture = "en-Us")
    {
        EnsureInitialized();
        var (template, fullPath) = GetTemplate(templatePath);
        var variables = new ObjectHasher(_options.MaxDepth).HashRoot(sourceData);

        // The file system is resolved from the full path (a bare "main.liquid" has an empty directory name) and travels with
        // the render: DotLiquid reads Registers["file_system"] before the process-wide static (B-1).
        var fileSystem = new EnhancedLocalFileSystem(Path.GetDirectoryName(fullPath) ?? string.Empty);
        return RenderCore(template, variables, culture, fileSystem);
    }

    /// <inheritdoc />
    public override string RenderString(string templateString, object? sourceData, string culture = "en-Us")
    {
        EnsureInitialized();
        var template = Template.Parse(templateString); // parsed per call, so nothing is shared between renders
        var variables = new ObjectHasher(_options.MaxDepth).HashRoot(sourceData);

        // With TemplatesPath set, includes resolve (confined) against it. Without it DotLiquid falls back to a file system the
        // host assigned to Template.FileSystem, as before 8.0.4.
        EnhancedLocalFileSystem? fileSystem = string.IsNullOrEmpty(_options.TemplatesPath)
            ? null
            : new EnhancedLocalFileSystem(Path.GetFullPath(_options.TemplatesPath));
        return RenderCore(template, variables, culture, fileSystem);
    }

    /// <inheritdoc />
    public override void RegisterFilter(Type type)
    {
        Template.RegisterFilter(type);
    }

    private string RenderCore(Template template, Hash variables, string culture, EnhancedLocalFileSystem? fileSystem)
    {
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        var registers = new Hash();
        if (fileSystem != null)
        {
            registers["file_system"] = fileSystem;
        }

        using var timeout = new CancellationTokenSource();
        if (_options.RenderTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout.CancelAfter(_options.RenderTimeout);
        }

        // The parsed template may be cached and shared by every render, so each render gets its own Context: rendering through the
        // template's own Registers/InstanceAssigns would let {% assign %} values of one render appear in another (B-8).
        var context = new Context(
            new List<Hash> { variables },
            new Hash(),
            registers,
            ErrorsOutputMode.Display,
            _options.MaxIterations,
            cultureInfo,
            timeout.Token);

        var writer = new LimitedWriter(_options.MaxOutputCharacters, cultureInfo);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Filters format with the thread culture; it is the render culture for the duration of the render (B-6).
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            using (RenderScope.Begin(_options, fileSystem, timeout.Token))
            {
                // A regex or render time-out propagates as an exception: a failed render must not look like a successful result.
                template.Render(writer, RenderParameters.FromContext(context, cultureInfo));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        return writer.ToString();
    }

    private void EnsureInitialized()
    {
        // Cheap on the hot path: the lock is only taken until this engine's regex time-out has been applied.
        if (!_isInitialized || (_options.RegexTimeout != Timeout.InfiniteTimeSpan && _options.RegexTimeout < Template.RegexTimeOut))
        {
            Initialize();
        }
    }

    private (Template Template, string FullPath) GetTemplate(string templatePath)
    {
        var fullPath = string.IsNullOrEmpty(templatePath) ? templatePath : Path.GetFullPath(templatePath);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File {templatePath} doesn't exist!", templatePath);
        }

        if (!_options.CacheTemplates)
        {
            return (ParseFile(fullPath), fullPath);
        }

        // Keyed by full path and last write time: an edited file is parsed again, and relative and absolute spellings of one path
        // share an entry (B-2).
        var lastWrite = File.GetLastWriteTimeUtc(fullPath);
        var key = (fullPath, lastWrite);
        var entry = TemplateCache.GetOrAdd(key, k => new Lazy<Template>(() => ParseFile(k.Path), LazyThreadSafetyMode.ExecutionAndPublication));

        foreach (var stale in TemplateCache.Keys.Where(k => k.Path == fullPath && k.LastWriteUtc != lastWrite).ToList())
        {
            TemplateCache.TryRemove(stale, out _);
        }

        try
        {
            return (entry.Value, fullPath);
        }
        catch
        {
            TemplateCache.TryRemove(key, out _); // do not cache a failed parse
            throw;
        }
    }

    private static Template ParseFile(string fullPath)
    {
        Interlocked.Increment(ref _parseCount);
        var template = Template.Parse(File.ReadAllText(fullPath));
        template.MakeThreadSafe(); // the instance is shared; renders use their own Context (see RenderCore)
        return template;
    }
}
