using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    public class JsonSpreedSheet
    {
        private static readonly char[] InvalidSheetNameChars = { '[', ']', ':', '*', '?', '/', '\\' };
        private static readonly string[] ErrorLiterals = { "#N/A", "#REF!", "#VALUE!", "#DIV/0!", "#NAME?", "#NUM!", "#NULL!" };

        private readonly TemplatingOptions _options;

        /// <summary>Creates a writer with default <see cref="TemplatingOptions"/>.</summary>
        public JsonSpreedSheet() : this(new TemplatingOptions())
        {
        }

        /// <summary>Creates a writer that loads data-referenced stylesheets only from <see cref="TemplatingOptions.StylesheetRoot"/>.</summary>
        /// <param name="options">The options.</param>
        public JsonSpreedSheet(TemplatingOptions options)
        {
            _options = TemplatingOptionsValidator.Validate(options, validateRegexTimeout: false);
        }

        public void CreateExcelDoc(string fileName, JObject json)
        {
            // build in memory first: a definition that fails must not leave a half-written or empty file behind
            using var buffer = new MemoryStream();
            CreateExcelDoc(buffer, json);
            using var file = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            buffer.Position = 0;
            buffer.CopyTo(file);
        }

        /// <summary>Writes the workbook described by <paramref name="json"/> into <paramref name="stream"/> (no temp file).</summary>
        /// <param name="stream">A writable, seekable stream; it stays open.</param>
        /// <param name="json">The workbook definition (<c>$.Workbook</c>).</param>
        public void CreateExcelDoc(Stream stream, JObject json)
        {
            var sheetIds = ValidateSheets(json.SelectToken("$.Workbook.Sheets"));
            if (!(json.SelectToken("$.Workbook.Worksheet.SheetData.Rows") is JArray))
            {
                throw new ArgumentException("$.Workbook.Worksheet.SheetData.Rows is required and must be an array (use an empty array for no rows).", "json");
            }

            using SpreadsheetDocument
                document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet();

            // Adding style
            WorkbookStylesPart stylePart = workbookPart.AddNewPart<WorkbookStylesPart>();
            var jToken = json.SelectToken("$.Workbook.Stylesheets");
            if (jToken?["TemplatePath"] != null)
            {
                var xStyle = LoadStylesheet(jToken["TemplatePath"]?.ToString());
                stylePart.Stylesheet = new Stylesheet(xStyle.ToString());
            }
            else
            {
                stylePart.Stylesheet = GenerateStylesheet(jToken);
            }

            // index 5 is a date style only when the stylesheet has that many cell formats; the generated stylesheet has none
            var styleCount = (uint)(stylePart.Stylesheet.CellFormats?.Elements<CellFormat>().Count() ?? 0);
            uint? defaultDateStyle = styleCount > 5 ? 5u : (uint?)null;

            stylePart.Stylesheet.Save();


            // Setting up columns
            var jColumns = json.SelectToken("$.Workbook.Worksheet.Columns");
            if (jColumns == null)
            {
                throw new ArgumentException("$.Workbook.Worksheet.Columns is required (use an empty array for no columns).", "json");
            }

            var columns = new Columns();
            foreach (JToken jColumn in jColumns)
            {
                var min = ReadWholeNumber(jColumn["Min"], "Column Min", 1, MaxColumn) ?? throw new ArgumentException("Every column needs a Min and a Max.", "json");
                var max = ReadWholeNumber(jColumn["Max"], "Column Max", 1, MaxColumn) ?? throw new ArgumentException("Every column needs a Min and a Max.", "json");
                if (min > max)
                {
                    throw new ArgumentException("Column Min may not be greater than Max.", "json");
                }

                var column = new Column()
                {
                    Min = UInt32Value.FromUInt32(min),
                    Max = UInt32Value.FromUInt32(max),
                    CustomWidth = false,
                    BestFit = BooleanValue.FromBoolean(ReadBoolean(jColumn["BestFit"], "Column BestFit"))
                };

                if (jColumn["Width"] is JToken widthToken && widthToken.Type != JTokenType.Null && widthToken.Type != JTokenType.Undefined)
                {
                    var width = ReadDouble(widthToken, "Column Width");
                    if (!(width >= 0 && width <= 255)) // the schema allows 0 to 255; NaN, infinity, negative and huge values are refused
                    {
                        throw new ArgumentException("Column Width must be a number from 0 to 255.", "json");
                    }

                    column.Width = DoubleValue.FromDouble(width);
                    column.CustomWidth = true;
                }

                columns.AppendChild(column);
            }

            if (columns.HasChildren)
            {
                worksheetPart.Worksheet.AppendChild(columns); // an empty <cols/> is schema-invalid
            }

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var jSheets = json.SelectToken("$.Workbook.Sheets");
            var sheetIndex = 0;
            foreach (var jSheet in jSheets.Children())
            {
                sheets.Append(
                    new Sheet()
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = UInt32Value.FromUInt32(sheetIds[sheetIndex++]), // validated up front, never converted again here
                        Name = StringValue.FromString((string)jSheet["Name"]!)
                    });
            }

            workbookPart.Workbook.Save();

            SheetData sheetData = worksheetPart.Worksheet.AppendChild(new SheetData());

            // Constructing header
            var jRows = json.SelectToken("$.Workbook.Worksheet.SheetData.Rows");
            var styleMap = json.SelectToken("$.StyleMap") as JArray ?? new JArray(); // no StyleMap means style 0, not "write no cells"
            var iRow = -1; // to ignore header
            foreach (JToken jRow in jRows)
            {
                var row = new Row { Collapsed = ReadBoolean(jRow["Collapsed"], "Row Collapsed") };
                var jCells = jRow.SelectToken("Cells");

                if (jCells != null)
                    foreach (JToken jCell in jCells)
                    {
                        row.AppendChild(this.ConstructCell(jCell, styleMap, iRow, defaultDateStyle, styleCount));
                    }

                iRow++;
                sheetData.AppendChild(row);
            }

            worksheetPart.Worksheet.Save();
        }

        /// <summary>
        /// The construct cell.
        /// </summary>
        /// <param name="jCell">
        /// The j cell.
        /// </param>
        /// <param name="styleMap"></param>
        /// <param name="rowIndex"></param>
        /// <returns>
        /// The <see cref="Cell"/>.
        /// </returns>
        private Cell ConstructCell(JToken jCell, JArray styleMap, int rowIndex, uint? defaultDateStyle, uint styleCount)
        {
            var dataType = ParseDataType(jCell["DataType"]?.ToString());

            var cell = new Cell();
            if (dataType == CellValues.Date)
            {
                // an empty date is an empty cell (<c t="n"><v/></c> is not valid); text that is not a date is an error, like a bad Number or Boolean
                var dateText = jCell["CellValue"]?.ToString();
                if (!string.IsNullOrWhiteSpace(dateText))
                {
                    if (!DateTime.TryParse(dateText, out var date))
                    {
                        throw new ArgumentException("A Date cell must hold a date.", "json");
                    }

                    cell.CellValue = new CellValue(date.ToUniversalTime().ToString());
                    cell.DataType = new EnumValue<CellValues>(CellValues.Number);
                }

                cell.StyleIndex = this.GetStyleIndex(jCell, styleMap, (rowIndex > 0 && (rowIndex % 2) != 0), styleCount);
                if (cell.StyleIndex.Value == 0 && defaultDateStyle is { } dateStyle)
                {
                    cell.StyleIndex = UInt32Value.FromUInt32(dateStyle);
                }
            }
            else
            {
                //case CellValues.Boolean:
                //      case CellValues.Error:
                //    case CellValues.Number:
                //  case CellValues.InlineString:
                //  case CellValues.String:
                // default:
                var text = ToValidXml(InvariantText(jCell["CellValue"]));
                if (dataType == CellValues.Boolean && !string.IsNullOrWhiteSpace(text))
                {
                    // a boolean cell holds 1 or 0
                    var lower = text.Trim().ToLowerInvariant();
                    if (lower == "true" || lower == "1")
                    {
                        text = "1";
                    }
                    else if (lower == "false" || lower == "0")
                    {
                        text = "0";
                    }
                    else
                    {
                        throw new ArgumentException("A Boolean cell must hold true, false, 1 or 0.", "json");
                    }
                }
                else if (dataType == CellValues.Number && !string.IsNullOrWhiteSpace(text))
                {
                    // the canonical invariant round-trip form; text that is not a finite number would write an invalid workbook
                    if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new ArgumentException("A Number cell must hold a finite number.", "json");
                    }

                    text = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                }

                if (dataType == CellValues.InlineString || dataType == CellValues.SharedString)
                {
                    // there is no shared-string table in this mode, so a shared string is written inline; an inline string needs its <is> element
                    cell.DataType = new EnumValue<CellValues>(CellValues.InlineString);
                    cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                    cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0), styleCount);
                    return cell;
                }

                if (dataType == CellValues.Error && Array.IndexOf(ErrorLiterals, text) < 0)
                {
                    throw new ArgumentException("An Error cell must hold one of #N/A, #REF!, #VALUE!, #DIV/0!, #NAME?, #NUM!, #NULL!.", "json");
                }

                if (string.IsNullOrWhiteSpace(text) && (dataType == CellValues.Number || dataType == CellValues.Boolean))
                {
                    // an empty number or boolean is an empty cell (it keeps its style)
                    cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0), styleCount);
                    return cell;
                }

                cell.CellValue = new CellValue(text);
                cell.DataType = new EnumValue<CellValues>(dataType);
                cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0), styleCount);
            }

            return cell;
        }

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

            try
            {
                // a symbolic link inside the root may point outside it: compare the real paths
                var real = RealPathInside(root, full);
                if (real == null)
                {
                    throw new ArgumentException("The stylesheet path is outside the stylesheet root.", nameof(requested));
                }

                var settings = new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = _options.MaxXmlCharacters,
                };

                return GuardedRead.Read(real, stream => // the resolved path is what is opened
                {
                    using (var reader = System.Xml.XmlReader.Create(stream, settings))
                    {
                        return XDocument.Load(reader);
                    }
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // an unreadable file, a directory, a pipe: the system message carries the absolute path, ours does not
                throw new FileNotFoundException("The stylesheet was not found in the stylesheet root.");
            }
            catch (System.Xml.XmlException)
            {
                // the XmlException message and SourceUri can carry the file name
                throw new ArgumentException("The stylesheet is not well-formed XML, or it uses a DTD (not allowed).", nameof(requested));
            }
        }

        /// <summary>
        /// Maps the JSON data type name to <see cref="CellValues"/>. <c>CellValues</c> is a struct in Open XML SDK 3.x, so
        /// <c>Enum.TryParse</c> throws for every cell (X-1). Unknown names fall back to <see cref="CellValues.String"/>.
        /// </summary>
        private static CellValues ParseDataType(string? name)
        {
            switch (name?.Trim().ToLowerInvariant())
            {
                case "number": return CellValues.Number;
                case "boolean": return CellValues.Boolean;
                case "date": return CellValues.Date;
                case "error": return CellValues.Error;
                case "sharedstring": return CellValues.SharedString;
                case "inlinestring": return CellValues.InlineString;
                default: return CellValues.String;
            }
        }

        private UInt32Value GetStyleIndex(JToken jCell, JArray styleMap, bool useAlternative, uint styleCount)
        {
            var explicitToken = jCell["StyleIndex"];
            if (explicitToken != null && explicitToken.Type != JTokenType.Null && explicitToken.Type != JTokenType.Undefined) // a JSON null means "not given": the Format lookup stays active
            {
                var explicitIndex = ReadWholeNumber(explicitToken, "StyleIndex", 0, uint.MaxValue) ?? 0;
                if (explicitIndex >= Math.Max(styleCount, 1))
                {
                    throw new ArgumentException($"A StyleIndex is outside the stylesheet's {styleCount} cell formats.", "json");
                }

                return UInt32Value.FromUInt32(explicitIndex);
            }

            string? format = null;
            if (jCell["Format"] != null)
            {
                format = jCell["Format"]?.ToString().Trim().Replace(" ", "");
            }

            if (string.IsNullOrEmpty(format))
            {
                format = "Normal";
            }

            // compare the key directly: a JSONPath filter built from data breaks on a quote or a trailing backslash in the format
            var map = styleMap.OfType<JObject>().FirstOrDefault(entry => entry["Key"]?.ToString() == format);
            if (map != null)
            {
                var alternativeToken = map["SAlternativeIndex"];
                var hasAlternative = alternativeToken != null && alternativeToken.Type != JTokenType.Null && alternativeToken.Type != JTokenType.Undefined; // null means "not given"
                var mapped = useAlternative && hasAlternative
                    ? ReadWholeNumber(alternativeToken, "SAlternativeIndex", 0, uint.MaxValue) ?? 0
                    : ReadWholeNumber(map["SIndex"], "SIndex", 0, uint.MaxValue) ?? 0;
                if (mapped >= Math.Max(styleCount, 1))
                {
                    throw new ArgumentException($"A StyleMap index is outside the stylesheet's {styleCount} cell formats.", "json");
                }

                return UInt32Value.FromUInt32(mapped);
            }

            return UInt32Value.FromUInt32(0);
        }

        private static List<uint> ValidateSheets(JToken? jSheets)
        {
            if (jSheets is JContainer container && container.Count > 255)
            {
                throw new ArgumentException("The workbook may define at most 255 sheets.", "json"); // before any copy of the list
            }

            var sheets = jSheets?.Children().ToList() ?? new List<JToken>();
            if (sheets.Count == 0)
            {
                throw new ArgumentException("The workbook must define at least one sheet ($.Workbook.Sheets is missing or empty).", "json");
            }

            if (sheets.Count > 255)
            {
                throw new ArgumentException("The workbook may define at most 255 sheets.", "json");
            }

            var ids = new HashSet<uint>();
            var idList = new List<uint>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                var name = (string?)sheet["Name"];
                if (string.IsNullOrWhiteSpace(name) || name.Length > 31 || name.IndexOfAny(InvalidSheetNameChars) >= 0
                    || name[0] == '\'' || name[name.Length - 1] == '\'' || name.Equals("History", StringComparison.OrdinalIgnoreCase)
                    || HasControlOrLoneSurrogate(name))
                {
                    throw new ArgumentException("A sheet name must be 1 to 31 characters, may not contain any of [ ] : * ? / \\, may not start or end with an apostrophe, may not be 'History', and may not contain control characters or lone surrogates.", "json");
                }

                if (!names.Add(name))
                {
                    throw new ArgumentException("A sheet name is used more than once (names are compared ignoring case).", "json");
                }

                var id = ReadSheetId(sheet["SheetId"]) ?? throw new ArgumentException("Every sheet needs a SheetId (a whole number from 1).", "json");
                if (!ids.Add(id))
                {
                    throw new ArgumentException("A SheetId is used by more than one sheet.", "json");
                }

                idList.Add(id);
            }

            return idList;
        }

        /// <summary>The cell value as text in the invariant culture: <c>JValue.ToString()</c> formats numbers with the thread culture, which writes "12,5" under de-DE.</summary>
        private static string InvariantText(JToken? value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return value is JValue { Value: IFormattable formattable }
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString();
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
                    return true;
                }
            }

            return false;
        }

        /// <summary>The largest SheetId the schema allows (the validator rejects anything above 65534).</summary>
        private const uint MaxSheetId = 65534;

        /// <summary>The last Excel column (XFD): the schema rejects a column range above it.</summary>
        private const uint MaxColumn = 16384;

        private static uint? ReadSheetId(JToken? token) => ReadWholeNumber(token, "SheetId", 1, MaxSheetId);

        /// <summary>
        /// A whole number read from JSON (an integer, an integral float, or numeric text) within <paramref name="min"/> and <paramref name="max"/>; null for a missing or null value.
        /// Every failure (a BigInteger beyond any type, negative, fractional, NaN, text) is an <see cref="ArgumentException"/>, never an InvalidCast, Format or Overflow exception.
        /// </summary>
        private static uint? ReadWholeNumber(JToken? token, string what, double min, double max)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) // a JSON null means "not given"
            {
                return null;
            }

            var number = ReadDouble(token, what);
            if (!(number >= min && number <= max) || number != Math.Floor(number))
            {
                throw new ArgumentException($"{what} must be a whole number from {min.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} to {max.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}.", "json");
            }

            return (uint)number;
        }

        private static double ReadDouble(JToken token, string what)
        {
            try
            {
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                {
                    var raw = ((JValue)token).Value;
                    return raw is System.Numerics.BigInteger big ? (double)big : Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture); // above 2^63 the value is a BigInteger
                }

                if (token.Type == JTokenType.String && double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                throw new ArgumentException($"{what} must be a number.", "json"); // no inner exception: its message quotes the value
            }

            throw new ArgumentException($"{what} must be a number.", "json");
        }

        private static bool ReadBoolean(JToken? token, string what)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) // a JSON null means "not given"
            {
                return false;
            }

            if (token is JValue value && value.Value is bool flag)
            {
                return flag;
            }

            var text = token.ToString().Trim().ToLowerInvariant();
            if (text == "true" || text == "1")
            {
                return true;
            }

            if (text == "false" || text == "0")
            {
                return false;
            }

            throw new ArgumentException($"{what} must be true or false.", "json");
        }

        private static string ToValidXml(string s)
        {
            var builder = new System.Text.StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    builder.Append(c).Append(s[++i]);
                }
                else if ((c > 31 && c <= 55295) || c == 9 || c == 10 || c == 13 || (c >= 57344 && c <= 65533))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>The real path of <paramref name="path"/> when it lies inside <paramref name="root"/>, otherwise null; callers open this resolved path.</summary>
        private static string? RealPathInside(string root, string path)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var realRoot = ResolveLinks(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            var real = ResolveLinks(path);
            return real.StartsWith(realRoot, comparison) ? real : null;
        }

        /// <summary>
        /// The physical location of <paramref name="absolutePath"/>, resolved the way the operating system does it: one link at a time, each relative target applied to
        /// the directory the link really sits in, <c>..</c> applied to the real parent (<c>ResolveLinkTarget(returnFinalTarget: true)</c> follows relative link chains lexically).
        /// </summary>
        private static string ResolveLinks(string absolutePath)
        {
            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var volume = Path.GetPathRoot(absolutePath) ?? string.Empty;
            var resolved = new List<string>();
            var pending = new Stack<string>(Enumerable.Reverse(absolutePath.Substring(volume.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries)));
            var hops = 0;
            while (pending.Count > 0)
            {
                var segment = pending.Pop();
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (resolved.Count > 0)
                    {
                        resolved.RemoveAt(resolved.Count - 1);
                    }

                    continue;
                }

                resolved.Add(segment);
                var current = Path.Combine(volume, Path.Combine(resolved.ToArray()));
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                var target = info.LinkTarget;
                if (target == null)
                {
                    continue;
                }

                if (++hops > 32)
                {
                    throw new ArgumentException("The stylesheet path contains too many links.", "requested");
                }

                resolved.RemoveAt(resolved.Count - 1);
                if (Path.IsPathRooted(target))
                {
                    volume = Path.GetPathRoot(target) ?? string.Empty;
                    resolved.Clear();
                    target = target.Substring(volume.Length);
                }

                foreach (var part in Enumerable.Reverse(target.Split(separators, StringSplitOptions.RemoveEmptyEntries)))
                {
                    pending.Push(part);
                }
            }

            return resolved.Count == 0 ? volume : Path.Combine(volume, Path.Combine(resolved.ToArray()));
        }

        /// <summary>
        /// The generate stylesheet.
        /// </summary>
        /// <param name="stylesheets">
        /// The stylesheets.
        /// </param>
        /// <returns>
        /// The <see cref="Stylesheet"/>.
        /// </returns>
        private Stylesheet GenerateStylesheet(JToken stylesheets)
        {
            Stylesheet styleSheet = new Stylesheet();
            // Constructing fonts
            return styleSheet;
        }
    }
}