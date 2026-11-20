using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet;

/// <summary>Writes a workbook described by JSON (<c>$.Workbook</c>) with the Open XML SDK.</summary>
public class JsonSpreedSheet
{
    /// <summary>The style index the generated stylesheet gives to dates, and the index date cells use when the JSON names none.</summary>
    internal const uint DefaultDateStyleIndex = 5;

    private readonly TemplatingOptions _options;

    /// <summary>Creates a writer with default <see cref="TemplatingOptions"/>.</summary>
    public JsonSpreedSheet()
        : this(new TemplatingOptions())
    {
    }

    /// <summary>Creates a writer that loads data-referenced stylesheets only from <see cref="TemplatingOptions.StylesheetRoot"/>.</summary>
    /// <param name="options">The options.</param>
    public JsonSpreedSheet(TemplatingOptions options)
    {
        _options = TemplatingOptionsValidator.Validate(options, validateRenderLimits: false);
    }

    /// <summary>Writes the workbook described by <paramref name="json"/> into a file (replaced when it exists).</summary>
    /// <param name="fileName">The target path.</param>
    /// <param name="json">The workbook definition (<c>$.Workbook</c>).</param>
    public void CreateExcelDoc(string fileName, JObject json)
    {
        using var file = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite);
        CreateExcelDoc(file, json);
    }

    /// <summary>
    /// Writes the workbook described by <paramref name="json"/> into <paramref name="stream"/> (no temp file). Every entry of
    /// <c>Workbook.Sheets</c> becomes a sheet; the single <c>Workbook.Worksheet</c> definition is used for each of them (X-17).
    /// </summary>
    /// <param name="stream">A writable, seekable stream; it stays open.</param>
    /// <param name="json">The workbook definition (<c>$.Workbook</c>).</param>
    public void CreateExcelDoc(Stream stream, JObject json)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(json);

        using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var stylePart = workbookPart.AddNewPart<WorkbookStylesPart>();
        var styleToken = json.SelectToken("$.Workbook.Stylesheets");
        if (styleToken?["TemplatePath"] is { } templatePath)
        {
            stylePart.Stylesheet = new Stylesheet(LoadStylesheet(templatePath.ToString()).ToString());
        }
        else
        {
            stylePart.Stylesheet = GenerateStylesheet();
        }

        stylePart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        var jSheets = json.SelectToken("$.Workbook.Sheets")?.Children().ToList() ?? new List<JToken>();
        var styleMap = json.SelectToken("$.StyleMap");
        foreach (var jSheet in jSheets)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = BuildWorksheet(json, styleMap);
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = UInt32Value.FromUInt32((uint?)jSheet["SheetId"] ?? (uint)(sheets.ChildElements.Count + 1)),
                Name = StringValue.FromString((string?)jSheet["Name"] ?? "Sheet" + (sheets.ChildElements.Count + 1)),
            });
        }

        workbookPart.Workbook.Save();
    }

    private static Worksheet BuildWorksheet(JObject json, JToken? styleMap)
    {
        var worksheet = new Worksheet();

        var columns = new Columns();
        foreach (var jColumn in json.SelectToken("$.Workbook.Worksheet.Columns")?.Children() ?? Enumerable.Empty<JToken>())
        {
            var column = new Column
            {
                Min = UInt32Value.FromUInt32((uint?)jColumn["Min"] ?? 1),
                Max = UInt32Value.FromUInt32((uint?)jColumn["Max"] ?? 1),
                CustomWidth = false,
                BestFit = BooleanValue.FromBoolean((bool?)jColumn["BestFit"] ?? false),
            };

            if (jColumn["Width"] != null)
            {
                column.Width = DoubleValue.FromDouble((double)jColumn["Width"]!);
                column.CustomWidth = true;
            }

            columns.AppendChild(column);
        }

        if (columns.HasChildren)
        {
            worksheet.AppendChild(columns);
        }

        var sheetData = worksheet.AppendChild(new SheetData());
        var rowIndex = -1; // the first row is the header and does not alternate
        uint rowNumber = 0;
        foreach (var jRow in json.SelectToken("$.Workbook.Worksheet.SheetData.Rows")?.Children() ?? Enumerable.Empty<JToken>())
        {
            rowNumber++;
            var row = new Row { RowIndex = rowNumber, Collapsed = Convert.ToBoolean(jRow["Collapsed"], CultureInfo.InvariantCulture) };
            if (jRow.SelectToken("Cells") is { } cells && styleMap != null)
            {
                var column = 0;
                foreach (var jCell in cells)
                {
                    var cell = ConstructCell(jCell, styleMap, rowIndex);
                    cell.CellReference = Utility.NextColumn(++column) + rowNumber.ToString(CultureInfo.InvariantCulture);
                    row.AppendChild(cell);
                }
            }

            rowIndex++;
            sheetData.AppendChild(row);
        }

        return worksheet;
    }

    private static Cell ConstructCell(JToken jCell, JToken styleMap, int rowIndex)
    {
        var dataType = ParseDataType(jCell["DataType"]?.ToString());
        var alternative = rowIndex > 0 && rowIndex % 2 != 0;
        var value = jCell["CellValue"];
        var cell = new Cell();

        if (dataType == CellValues.Date)
        {
            // a date is stored as an Excel serial number (a number cell with a date style), never as culture text (X-4)
            cell.CellValue = new CellValue(DateSerial(value));
            cell.DataType = new EnumValue<CellValues>(CellValues.Number);
            cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative);
            if (cell.StyleIndex.Value == 0)
            {
                cell.StyleIndex = UInt32Value.FromUInt32(DefaultDateStyleIndex);
            }

            return cell;
        }

        var text = value is JValue { Value: null } or null ? string.Empty : Invariant(value);
        if (dataType == CellValues.Boolean)
        {
            text = bool.TryParse(text, out var flag) ? (flag ? "1" : "0") : text; // a boolean cell holds 1 or 0
        }

        cell.CellValue = new CellValue(text);
        cell.DataType = new EnumValue<CellValues>(dataType);
        cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative);
        return cell;
    }

    private static string DateSerial(JToken? value)
    {
        if (value == null || value.Type == JTokenType.Null || (value.Type == JTokenType.String && string.IsNullOrEmpty(value.ToString())))
        {
            return string.Empty;
        }

        if (value is JValue { Value: DateTime dateTime })
        {
            return dateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture);
        }

        if (value is JValue { Value: DateTimeOffset dateTimeOffset })
        {
            return dateTimeOffset.UtcDateTime.ToOADate().ToString("R", CultureInfo.InvariantCulture);
        }

        return DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToOADate().ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string Invariant(JToken value) => value is JValue { Value: IFormattable formattable }
        ? formattable.ToString(null, CultureInfo.InvariantCulture)
        : value.ToString();

    /// <summary>
    /// Loads a stylesheet whose path comes from the data (S-7). The path must be relative and stay inside
    /// <see cref="TemplatingOptions.StylesheetRoot"/> (a relative root is resolved against the application base directory, which is also the default); rooted, UNC and
    /// traversing paths are rejected without echoing any absolute path, and the XML may not contain a DTD.
    /// </summary>
    private XDocument LoadStylesheet(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)
            || Path.IsPathRooted(requested)
            || requested.StartsWith("\\\\", StringComparison.Ordinal)
            || requested.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("The stylesheet path must be relative to the stylesheet root.", nameof(requested));
        }

        // never TemplatesPath: the folder of liquid/xlsx templates is not automatically a folder of stylesheets
        var configured = string.IsNullOrEmpty(_options.StylesheetRoot) ? AppContext.BaseDirectory : _options.StylesheetRoot;
        var root = Path.GetFullPath(configured, AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, requested));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
        {
            throw new ArgumentException("The stylesheet path is outside the stylesheet root.", nameof(requested));
        }

        if (!File.Exists(full))
        {
            throw new FileNotFoundException("The stylesheet was not found in the stylesheet root.");
        }

        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _options.MaxXmlCharacters,
        };
        using var reader = System.Xml.XmlReader.Create(full, settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// Maps the JSON data type name to <see cref="CellValues"/>. <c>CellValues</c> is a struct in Open XML SDK 3.x, so
    /// <c>Enum.TryParse</c> throws for every cell (X-1). Unknown names fall back to <see cref="CellValues.String"/>.
    /// </summary>
    private static CellValues ParseDataType(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "number" => CellValues.Number,
        "boolean" => CellValues.Boolean,
        "date" => CellValues.Date,
        "error" => CellValues.Error,
        "sharedstring" => CellValues.SharedString,
        "inlinestring" => CellValues.InlineString,
        _ => CellValues.String,
    };

    private static UInt32Value GetStyleIndex(JToken jCell, JToken styleMap, bool useAlternative)
    {
        if (jCell["StyleIndex"] != null)
        {
            return UInt32Value.FromUInt32(Convert.ToUInt32(jCell["StyleIndex"], CultureInfo.InvariantCulture));
        }

        var format = jCell["Format"]?.ToString().Trim().Replace(" ", string.Empty);
        if (string.IsNullOrEmpty(format))
        {
            format = "Normal";
        }

        var map = styleMap.SelectToken($"[?(@.Key == '{format.Replace("'", "\\'")}')]");
        if (map != null && map["Key"]?.ToString() == format)
        {
            if (useAlternative && map["SAlternativeIndex"] != null)
            {
                return UInt32Value.FromUInt32(Convert.ToUInt32(map["SAlternativeIndex"], CultureInfo.InvariantCulture));
            }

            return UInt32Value.FromUInt32(Convert.ToUInt32(map["SIndex"], CultureInfo.InvariantCulture));
        }

        return UInt32Value.FromUInt32(0);
    }

    /// <summary>
    /// The stylesheet written when the JSON names none: one font, the two mandatory fills, one border, and six cell formats where index 0 to 4
    /// are general and index <see cref="DefaultDateStyleIndex"/> shows a date. A workbook with an empty stylesheet is not valid (X-17).
    /// </summary>
    private static Stylesheet GenerateStylesheet()
    {
        var cellFormats = new CellFormats { Count = 6 };
        for (var i = 0; i < 5; i++)
        {
            cellFormats.Append(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 });
        }

        cellFormats.Append(new CellFormat { NumberFormatId = 14, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true });

        return new Stylesheet(
            new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 1 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            cellFormats);
    }
}
