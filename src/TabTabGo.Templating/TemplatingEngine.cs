namespace TabTabGo.Templating;

/// <summary>The contract of a template engine: turn a template and data into rendered output.</summary>
public abstract class TemplatingEngine
{
    /// <summary>Short name of the engine, for example <c>Liquid</c>.</summary>
    public abstract string EngineName { get; }

    /// <summary>Whether the template at <paramref name="templatePath"/> is already parsed and cached.</summary>
    /// <param name="templatePath">Path of the template file.</param>
    public abstract bool IsCached(string templatePath);

    /// <summary>Parses the template at <paramref name="templatePath"/> ahead of time.</summary>
    /// <param name="templatePath">Path of the template file.</param>
    public abstract void ParseTemplate(string templatePath);

    /// <summary>Renders the template file with the data.</summary>
    /// <param name="templatePath">Path of the template file.</param>
    /// <param name="sourceData">The data; null renders with no variables.</param>
    /// <param name="culture">Culture used to format the output (default <c>en-Us</c>).</param>
    /// <returns>The rendered output (a string for text engines, a stream for workbook engines).</returns>
    public abstract object Render(string templatePath, object? sourceData, string culture = "en-Us");

    /// <summary>Renders a template given as text.</summary>
    /// <param name="templateString">The template text.</param>
    /// <param name="sourceData">The data; null renders with no variables.</param>
    /// <param name="culture">Culture used to format the output (default <c>en-Us</c>).</param>
    public abstract string RenderString(string templateString, object? sourceData, string culture = "en-Us");

    /// <summary>Registers a type whose static methods become template filters (process-wide; register once at start-up).</summary>
    /// <param name="type">The filter type.</param>
    public abstract void RegisterFilter(Type type);
}
