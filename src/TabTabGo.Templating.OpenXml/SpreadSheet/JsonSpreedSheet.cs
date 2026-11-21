using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

namespace TabTabGo.Templating.OpenXml.SpreadSheet;

/// <summary>Writes a workbook described by JSON (<c>$.Workbook</c>) with the Open XML SDK.</summary>
public class JsonSpreedSheet
{
    /// <summary>The style index the generated stylesheet gives to dates, and the index date cells use when the JSON names none.</summary>
    internal const uint DefaultDateStyleIndex = 5;

    private const int MaxSheetNameLength = 31;

    /// <summary>The most sheets this writer accepts. Excel's practical limit is far lower (memory); 255 is the hard ceiling here and keeps the id/name generation trivial.</summary>
    internal const int MaxSheets = 255;

    private const double MaxDateSerial = 2958465; // 9999-12-31
    private static readonly char[] InvalidSheetNameChars = { '[', ']', ':', '*', '?', '/', '\\' };
    private static readonly string[] ErrorLiterals = { "#N/A", "#REF!", "#VALUE!", "#DIV/0!", "#NAME?", "#NUM!", "#NULL!" };

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
        // build in memory first: a definition that fails (bad sheet, style, error literal, stylesheet path) must not leave a half-written or empty file behind
        using var buffer = new MemoryStream();
        CreateExcelDoc(buffer, json);
        using var file = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        buffer.Position = 0;
        buffer.CopyTo(file);
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

        // validate the sheet list before the package is created; failures later on (stylesheet, cell values) can still leave a partial stream, which is why the
        // file overload builds in memory and writes the file only on success
        var jSheets = json.SelectToken("$.Workbook.Sheets")?.Children().ToList() ?? new List<JToken>();
        if (jSheets.Count == 0)
        {
            throw new ArgumentException("The workbook must define at least one sheet ($.Workbook.Sheets is missing or empty).", nameof(json));
        }

        if (jSheets.Count > MaxSheets)
        {
            throw new ArgumentException($"The workbook may define at most {MaxSheets} sheets.", nameof(json));
        }

        var sheetIds = ResolveSheetIds(jSheets);
        var sheetNames = ResolveSheetNames(jSheets);

        using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var stylePart = workbookPart.AddNewPart<WorkbookStylesPart>();
        var styleToken = json.SelectToken("$.Workbook.Stylesheets");
        uint? defaultDateStyle;
        uint styleCount;
        if (styleToken?["TemplatePath"] is { } templatePath)
        {
            stylePart.Stylesheet = new Stylesheet(LoadStylesheet(templatePath.ToString()).ToString());

            // index 5 means "date" only in the generated stylesheet; a loaded one keeps it only when it has that many formats
            styleCount = (uint)(stylePart.Stylesheet.CellFormats?.Elements<CellFormat>().Count() ?? 0);
            defaultDateStyle = styleCount > DefaultDateStyleIndex ? DefaultDateStyleIndex : null;
        }
        else
        {
            stylePart.Stylesheet = GenerateStylesheet();
            styleCount = (uint)stylePart.Stylesheet.CellFormats!.Elements<CellFormat>().Count();
            defaultDateStyle = DefaultDateStyleIndex;
        }

        stylePart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        var styleMap = json.SelectToken("$.StyleMap") as JArray ?? new JArray(); // no StyleMap means every cell uses style 0, not "write no cells"
        for (var i = 0; i < jSheets.Count; i++)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = BuildWorksheet(json, styleMap, defaultDateStyle, styleCount);
            worksheetPart.Worksheet.Save();
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = UInt32Value.FromUInt32(sheetIds[i]),
                Name = StringValue.FromString(sheetNames[i]),
            });
        }

        workbookPart.Workbook.Save();
    }

    /// <summary>Explicit ids are kept (and must be unique, whole numbers from 1); generated ids skip every explicit one so they never collide (linear: one running counter).</summary>
    private static List<uint> ResolveSheetIds(List<JToken> jSheets)
    {
        var used = new HashSet<uint>();
        var ids = new uint?[jSheets.Count];
        for (var i = 0; i < jSheets.Count; i++)
        {
            ids[i] = ReadSheetId(jSheets[i]["SheetId"]);
            if (ids[i] is { } explicitId && !used.Add(explicitId))
            {
                throw new ArgumentException($"SheetId {explicitId} is used by more than one sheet.", "json");
            }
        }

        var result = new List<uint>(jSheets.Count);
        uint next = 1; // never goes back: each generated id is the sheet's position, or later, past every id already taken
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] is { } known)
            {
                result.Add(known);
                continue;
            }

            next = Math.Max(next, (uint)(i + 1));
            while (used.Contains(next))
            {
                next++;
            }

            used.Add(next);
            result.Add(next++);
        }

        return result;
    }

    /// <summary>The largest SheetId the schema allows (the validator rejects anything above 65534).</summary>
    internal const uint MaxSheetId = 65534;

    private static uint? ReadSheetId(JToken? token) => ReadWholeNumber(token, "SheetId", 1, MaxSheetId);

    /// <summary>
    /// A whole number read from JSON (an integer, an integral float, or numeric text) within <paramref name="min"/> and <paramref name="max"/>; null for a missing or null value.
    /// Every failure (a BigInteger beyond any type, negative, fractional, NaN, text) is an <see cref="ArgumentException"/>, never an InvalidCast, Format or Overflow exception.
    /// </summary>
    private static uint? ReadWholeNumber(JToken? token, string what, double min, double max)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }

        var number = ReadDouble(token, what);
        if (!(number >= min && number <= max) || number != Math.Floor(number))
        {
            throw new ArgumentException($"{what} must be a whole number from {min.ToString("0", CultureInfo.InvariantCulture)} to {max.ToString("0", CultureInfo.InvariantCulture)} ({token}).", "json");
        }

        return (uint)number;
    }

    private static double ReadDouble(JToken token, string what)
    {
        try
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return ((JValue)token).Value switch
                    {
                        System.Numerics.BigInteger big => (double)big, // above 2^63 Newtonsoft keeps a BigInteger, which Convert cannot read
                        { } other => Convert.ToDouble(other, CultureInfo.InvariantCulture),
                        _ => throw new ArgumentException($"{what} must be a number.", "json"),
                    };
                case JTokenType.String when double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
                default:
                    throw new ArgumentException($"{what} must be a number ({token}).", "json");
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new ArgumentException($"{what} must be a number ({token}).", "json", ex);
        }
    }

    private static bool ReadBoolean(JToken? token, string what)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        if (token is JValue { Value: bool flag })
        {
            return flag;
        }

        return token.ToString().Trim().ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new ArgumentException($"{what} must be true or false ({token}).", "json"),
        };
    }

    /// <summary>Explicit names are validated and must be unique ignoring case; a sheet without a name gets the first free "SheetN" from its position (linear: one running counter).</summary>
    private static List<string> ResolveSheetNames(List<JToken> jSheets)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string?[jSheets.Count];
        for (var i = 0; i < jSheets.Count; i++)
        {
            names[i] = (string?)jSheets[i]["Name"];
            if (names[i] is { } explicitName && !used.Add(ValidateSheetName(explicitName)))
            {
                throw new ArgumentException($"The sheet name '{explicitName}' is used more than once (names are compared ignoring case).", "json");
            }
        }

        var result = new List<string>(jSheets.Count);
        var next = 1;
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] is { } known)
            {
                result.Add(known);
                continue;
            }

            next = Math.Max(next, i + 1);
            while (!used.Add("Sheet" + next.ToString(CultureInfo.InvariantCulture)))
            {
                next++;
            }

            result.Add("Sheet" + next.ToString(CultureInfo.InvariantCulture));
            next++;
        }

        return result;
    }

    private static string ValidateSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > MaxSheetNameLength
            || name.IndexOfAny(InvalidSheetNameChars) >= 0
            || HasControlOrLoneSurrogate(name)
            || name[0] == '\'' || name[^1] == '\''
            || name.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"A sheet name must be 1 to {MaxSheetNameLength} characters, may not contain any of [ ] : * ? / \\, may not start or end with an apostrophe, may not be 'History', and may not contain control characters or lone surrogates.",
                "json");
        }

        return name;
    }

    private static Worksheet BuildWorksheet(JObject json, JArray styleMap, uint? defaultDateStyle, uint styleCount)
    {
        var worksheet = new Worksheet();

        var columns = new Columns();
        foreach (var jColumn in json.SelectToken("$.Workbook.Worksheet.Columns")?.Children() ?? Enumerable.Empty<JToken>())
        {
            var column = new Column
            {
                Min = UInt32Value.FromUInt32(ReadWholeNumber(jColumn["Min"], "Column Min", 0, uint.MaxValue) ?? 1),
                Max = UInt32Value.FromUInt32(ReadWholeNumber(jColumn["Max"], "Column Max", 0, uint.MaxValue) ?? 1),
                CustomWidth = false,
                BestFit = BooleanValue.FromBoolean((bool?)jColumn["BestFit"] ?? false),
            };

            if (jColumn["Width"] != null)
            {
                column.Width = DoubleValue.FromDouble(ReadDouble(jColumn["Width"]!, "Column Width"));
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
            var row = new Row { RowIndex = rowNumber, Collapsed = ReadBoolean(jRow["Collapsed"], "Row Collapsed") };
            if (jRow.SelectToken("Cells") is { } cells)
            {
                var column = 0;
                foreach (var jCell in cells)
                {
                    var cell = ConstructCell(jCell, styleMap, rowIndex, defaultDateStyle, styleCount);
                    cell.CellReference = Utility.NextColumn(++column) + rowNumber.ToString(CultureInfo.InvariantCulture);
                    row.AppendChild(cell);
                }
            }

            rowIndex++;
            sheetData.AppendChild(row);
        }

        return worksheet;
    }

    private static bool HasControlOrLoneSurrogate(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsControl(name[i]))
            {
                return true;
            }

            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(name[i]))
            {
                return true; // a lone surrogate cannot be written to XML
            }
        }

        return false;
    }

    private static Cell ConstructCell(JToken jCell, JArray styleMap, int rowIndex, uint? defaultDateStyle, uint styleCount)
    {
        var dataType = ParseDataType(jCell["DataType"]?.ToString());
        var alternative = rowIndex > 0 && rowIndex % 2 != 0;
        var value = jCell["CellValue"];
        var cell = new Cell();

        if (dataType == CellValues.Date)
        {
            // a date is stored as an Excel serial number (a number cell with a date style), never as culture text (X-4)
            var serial = DateSerial(value);
            if (serial.Length > 0)
            {
                cell.CellValue = new CellValue(serial);
                cell.DataType = new EnumValue<CellValues>(CellValues.Number);
            }

            cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative, styleCount);
            if (cell.StyleIndex.Value == 0 && defaultDateStyle is { } dateStyle)
            {
                cell.StyleIndex = UInt32Value.FromUInt32(dateStyle);
            }

            return cell;
        }

        var text = CellValueWriter.ToValidXml(value is JValue { Value: null } or null ? string.Empty : Invariant(value));
        if (dataType == CellValues.Boolean && !string.IsNullOrWhiteSpace(text))
        {
            text = text.Trim().ToLowerInvariant() switch
            {
                "true" or "1" => "1",
                "false" or "0" => "0",
                _ => throw new ArgumentException("A Boolean cell must hold true, false, 1 or 0.", "json"),
            };
        }
        else if (dataType == CellValues.Number && !string.IsNullOrWhiteSpace(text))
        {
            // the canonical invariant round-trip form; text that is not a finite number would write an invalid workbook
            text = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? number.ToString("R", CultureInfo.InvariantCulture)
                : throw new ArgumentException("A Number cell must hold a finite number.", "json");
        }

        if (dataType == CellValues.InlineString || dataType == CellValues.SharedString)
        {
            // there is no shared-string table in this mode, so a shared string is written inline; an inline string needs its <is> element
            cell.DataType = new EnumValue<CellValues>(CellValues.InlineString);
            cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative, styleCount);
            return cell;
        }

        if (dataType == CellValues.Error && Array.IndexOf(ErrorLiterals, text) < 0)
        {
            throw new ArgumentException("An Error cell must hold one of #N/A, #REF!, #VALUE!, #DIV/0!, #NAME?, #NUM!, #NULL!.", "json");
        }

        if (string.IsNullOrWhiteSpace(text) && (dataType == CellValues.Number || dataType == CellValues.Boolean))
        {
            // an empty number or boolean is an empty cell: <c t="n"><v/></c> is not valid (it keeps its style)
            cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative, styleCount);
            return cell;
        }

        cell.CellValue = new CellValue(text);
        cell.DataType = new EnumValue<CellValues>(dataType);
        cell.StyleIndex = GetStyleIndex(jCell, styleMap, alternative, styleCount);
        return cell;
    }

    /// <summary>
    /// The Excel serial of a date cell: a date value, a date text, or a number (or numeric text) from 1 to 2958465 taken as the serial itself. Empty is empty;
    /// anything else throws, because dropping it would lose the data without a trace.
    /// </summary>
    private static string DateSerial(JToken? value)
    {
        if (value == null || value.Type == JTokenType.Null || (value.Type == JTokenType.String && string.IsNullOrWhiteSpace(value.ToString())))
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

        var text = value.ToString();
        if (value.Type is JTokenType.Integer or JTokenType.Float
            || (value.Type == JTokenType.String && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            var serial = ReadDouble(value, "A Date cell");
            return serial >= 1 && serial <= MaxDateSerial
                ? serial.ToString("R", CultureInfo.InvariantCulture)
                : throw new ArgumentException($"A Date cell holding a number must be an Excel serial from 1 to {MaxDateSerial.ToString("0", CultureInfo.InvariantCulture)} ({text}).", "json");
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToOADate().ToString("R", CultureInfo.InvariantCulture)
            : throw new ArgumentException($"A Date cell must hold a date, or an Excel serial number ({text}).", "json");
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

        // a symbolic link inside the root may point outside it: compare the real paths
        if (!RealPath.IsInside(root, full))
        {
            throw new ArgumentException("The stylesheet path is outside the stylesheet root.", nameof(requested));
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

    private static UInt32Value GetStyleIndex(JToken jCell, JArray styleMap, bool useAlternative, uint styleCount)
    {
        if (jCell["StyleIndex"] != null)
        {
            var explicitIndex = ReadWholeNumber(jCell["StyleIndex"], "StyleIndex", 0, uint.MaxValue) ?? 0;
            if (explicitIndex >= Math.Max(styleCount, 1))
            {
                throw new ArgumentException($"StyleIndex {explicitIndex} is outside the stylesheet's {styleCount} cell formats.", "json");
            }

            return UInt32Value.FromUInt32(explicitIndex);
        }

        var format = jCell["Format"]?.ToString().Trim().Replace(" ", string.Empty);
        if (string.IsNullOrEmpty(format))
        {
            format = "Normal";
        }

        // compare the key directly: a JSONPath filter built from data breaks on a quote or a trailing backslash in the format
        var map = styleMap.OfType<JObject>().FirstOrDefault(entry => entry["Key"]?.ToString() == format);
        if (map != null)
        {
            if (useAlternative && map["SAlternativeIndex"] != null)
            {
                return UInt32Value.FromUInt32(ReadWholeNumber(map["SAlternativeIndex"], "SAlternativeIndex", 0, uint.MaxValue) ?? 0);
            }

            return UInt32Value.FromUInt32(ReadWholeNumber(map["SIndex"], "SIndex", 0, uint.MaxValue) ?? 0);
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
