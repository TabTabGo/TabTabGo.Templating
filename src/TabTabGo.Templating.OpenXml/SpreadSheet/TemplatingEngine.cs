using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

namespace TabTabGo.Templating.OpenXml.SpreadSheet;

/// <summary>
/// Fills <c>.xlsx</c> templates from JSON or objects: <c>{%$.path%}</c> cells take a value, <c>{%$.items::name%}</c> cells in a table template
/// row are repeated for every item. Everything is rendered in memory and every render has its own state, so one engine can be shared.
/// </summary>
public class TemplatingEngine : TabTabGo.Templating.TemplatingEngine
{
    private readonly TemplatingOptions _options;

    /// <summary>Creates an engine with default <see cref="TemplatingOptions"/>.</summary>
    public TemplatingEngine()
        : this(new TemplatingOptions())
    {
    }

    /// <summary>Creates an engine with the given options.</summary>
    /// <param name="options">The options.</param>
    public TemplatingEngine(TemplatingOptions options)
    {
        _options = TemplatingOptionsValidator.Validate(options, validateRenderLimits: false);
    }

    /// <summary>Creates an engine from the options registered in dependency injection.</summary>
    /// <param name="options">The registered options.</param>
    public TemplatingEngine(IOptions<TemplatingOptions> options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value)
    {
    }

    /// <inheritdoc />
    public override string EngineName => "OpenXml";

    /// <inheritdoc />
    /// <remarks>Workbooks are not cached; this is always false.</remarks>
    public override bool IsCached(string templatePath) => false;

    /// <inheritdoc />
    /// <remarks>Nothing is parsed ahead of time; this does nothing.</remarks>
    public override void ParseTemplate(string templatePath)
    {
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: workbooks cannot be rendered from a text template.</exception>
    public override string RenderString(string templateString, object? sourceData, string culture = "en-Us") => throw new NotSupportedException();

    /// <inheritdoc />
    /// <remarks>This engine has no filters; this does nothing.</remarks>
    public override void RegisterFilter(Type type)
    {
    }

    /// <summary>
    /// Renders the workbook template at <paramref name="templatePath"/>, or, when the path is empty and the data holds <c>$.Workbook</c>,
    /// writes the workbook the JSON describes.
    /// </summary>
    /// <param name="templatePath">Path of the <c>.xlsx</c> template, or empty for the JSON workbook mode.</param>
    /// <param name="sourceData">The data: any object, a <see cref="JObject"/> or a <see cref="JArray"/> (the array is <c>$</c>).</param>
    /// <param name="culture">Culture of date formats (<c>:#format</c> macros); numbers are always written invariantly.</param>
    /// <returns>A <see cref="MemoryStream"/> positioned at the start; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceData"/> is null.</exception>
    /// <exception cref="NotSupportedException">There is neither a template nor a <c>$.Workbook</c> definition.</exception>
    public override object Render(string templatePath, object? sourceData, string culture = "en-Us")
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        var root = sourceData as JToken ?? JToken.FromObject(sourceData);

        // Everything is rendered in memory: no temp file is created, so nothing can leak when a render throws (X-3).
        var output = new MemoryStream();
        try
        {
            if (string.IsNullOrEmpty(templatePath) && root is JObject jObject && jObject.SelectToken("$.Workbook") != null)
            {
                new JsonSpreedSheet(_options).CreateExcelDoc(output, jObject);
            }
            else if (!string.IsNullOrEmpty(templatePath))
            {
                var template = File.ReadAllBytes(templatePath);
                output.Write(template, 0, template.Length);
                output.Position = 0;

                using var excel = SpreadsheetDocument.Open(output, true);
                new RenderSession(excel.WorkbookPart!, root, CultureInfo.GetCultureInfo(culture)).Run();
            }
            else
            {
                throw new NotSupportedException("Generate SpreadSheet can't be render.");
            }
        }
        catch
        {
            output.Dispose();
            throw;
        }

        output.Position = 0;
        return output;
    }

    /// <summary>Removes characters that are not valid in XML 1.0 (valid surrogate pairs such as emoji are kept).</summary>
    /// <param name="s">The text.</param>
    /// <param name="startIndex">Index to begin checking at.</param>
    public static string ToValidXmlCharactersString(string s, int startIndex = 0) => CellValueWriter.ToValidXml(s, startIndex);

    /// <summary>The index of the first character that is not valid in XML 1.0, or -1.</summary>
    /// <param name="s">The text.</param>
    /// <param name="startIndex">Index to begin checking at.</param>
    public static int IndexOfFirstInvalidXMLChar(string s, int startIndex = 0) => CellValueWriter.IndexOfFirstInvalid(s, startIndex);

    /// <summary>Whether one UTF-16 code unit is valid in XML 1.0 (a surrogate is only valid as part of a pair, which this overload cannot see).</summary>
    /// <param name="c">The character.</param>
    public static bool IsLegalXmlChar(char c) => CellValueWriter.IsLegal(c);
}
