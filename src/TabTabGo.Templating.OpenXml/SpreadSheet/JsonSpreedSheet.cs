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
            ValidateSheets(json.SelectToken("$.Workbook.Sheets"));

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
            var columns = new Columns();
            foreach (JToken jColumn in jColumns)
            {
                var column = new Column()
                {
                    Min = UInt32Value.FromUInt32((uint)jColumn["Min"]),
                    Max = UInt32Value.FromUInt32((uint)jColumn["Max"]),
                    CustomWidth = false,
                    BestFit = BooleanValue.FromBoolean((bool)jColumn["BestFit"]!)
                };

                if (jColumn["Width"] != null)
                {
                    column.Width = DoubleValue.FromDouble((double)jColumn["Width"]!);
                    column.CustomWidth = true;
                }

                columns.AppendChild(column);
            }

            worksheetPart.Worksheet.AppendChild(columns);

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var jSheets = json.SelectToken("$.Workbook.Sheets");
            foreach (dynamic jSheet in jSheets)
            {
                sheets.Append(
                    new Sheet()
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = UInt32Value.FromUInt32((uint)jSheet.SheetId),
                        Name = StringValue.FromString((string)jSheet.Name)
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
                var row = new Row { Collapsed = Convert.ToBoolean(jRow["Collapsed"]) };
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
                // an empty or unparseable date is an empty cell: <c t="n"><v/></c> is not valid
                if (!string.IsNullOrEmpty(jCell["CellValue"]?.ToString()) && DateTime.TryParse(jCell["CellValue"]?.ToString(), out var date))
                {
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
                var text = ToValidXml(jCell["CellValue"]?.ToString() ?? string.Empty);
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

            // a symbolic link inside the root may point outside it: compare the real paths
            if (!RealPathIsInside(root, full))
            {
                throw new ArgumentException("The stylesheet path is outside the stylesheet root.", nameof(requested));
            }

            var settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _options.MaxXmlCharacters,
            };
            using (var reader = System.Xml.XmlReader.Create(full, settings))
            {
                return XDocument.Load(reader);
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
            if (jCell["StyleIndex"] != null)
            {
                var explicitIndex = Convert.ToUInt32(jCell["StyleIndex"]);
                if (explicitIndex >= Math.Max(styleCount, 1))
                {
                    throw new ArgumentException($"StyleIndex {explicitIndex} is outside the stylesheet's {styleCount} cell formats.", "json");
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
                if (useAlternative && map["SAlternativeIndex"] != null)
                {
                    return UInt32Value.FromUInt32(Convert.ToUInt32(map["SAlternativeIndex"]));
                }

                return UInt32Value.FromUInt32(Convert.ToUInt32(map["SIndex"]));
            }

            return UInt32Value.FromUInt32(0);
        }

        private static void ValidateSheets(JToken? jSheets)
        {
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
                    throw new ArgumentException($"The sheet name '{name}' is used more than once (names are compared ignoring case).", "json");
                }

                if (ReadSheetId(sheet["SheetId"]) is { } id && !ids.Add(id))
                {
                    throw new ArgumentException($"SheetId {id} is used by more than one sheet.", "json");
                }
            }
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

        private static uint? ReadSheetId(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            double number;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                number = Convert.ToDouble(((JValue)token).Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (token.Type != JTokenType.String || !double.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                throw new ArgumentException("SheetId must be a whole number from 1.", "json");
            }

            if (number < 1 || number > uint.MaxValue || number != Math.Floor(number))
            {
                throw new ArgumentException($"SheetId must be a whole number from 1 ({token}).", "json");
            }

            return (uint)number;
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

        private static bool RealPathIsInside(string root, string path)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var realRoot = ResolveLinks(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            return ResolveLinks(path).StartsWith(realRoot, comparison);
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