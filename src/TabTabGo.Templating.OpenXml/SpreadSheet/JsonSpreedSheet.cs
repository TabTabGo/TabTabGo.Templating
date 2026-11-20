using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.IO;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    public class JsonSpreedSheet
    {
        private readonly TemplatingOptions _options;

        /// <summary>Creates a writer with default <see cref="TemplatingOptions"/>.</summary>
        public JsonSpreedSheet() : this(new TemplatingOptions())
        {
        }

        /// <summary>Creates a writer that loads data-referenced stylesheets only from <see cref="TemplatingOptions.StylesheetRoot"/>.</summary>
        /// <param name="options">The options.</param>
        public JsonSpreedSheet(TemplatingOptions options)
        {
            _options = TemplatingOptionsValidator.Validate(options, validateRenderLimits: false);
        }

        public void CreateExcelDoc(string fileName, JObject json)
        {
            using var file = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite);
            CreateExcelDoc(file, json);
        }

        /// <summary>Writes the workbook described by <paramref name="json"/> into <paramref name="stream"/> (no temp file).</summary>
        /// <param name="stream">A writable, seekable stream; it stays open.</param>
        /// <param name="json">The workbook definition (<c>$.Workbook</c>).</param>
        public void CreateExcelDoc(Stream stream, JObject json)
        {
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
            var styleMap = json.SelectToken("$.StyleMap");
            var iRow = -1; // to ignore header
            foreach (JToken jRow in jRows)
            {
                var row = new Row { Collapsed = Convert.ToBoolean(jRow["Collapsed"]) };
                var jCells = jRow.SelectToken("Cells");

                if (jCells != null && styleMap != null)
                    foreach (JToken jCell in jCells)
                    {
                        row.AppendChild(this.ConstructCell(jCell, styleMap, iRow));
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
        private Cell ConstructCell(JToken jCell, JToken styleMap, int rowIndex)
        {
            var dataType = ParseDataType(jCell["DataType"]?.ToString());

            var cell = new Cell();
            if (dataType == CellValues.Date)
            {
                if (string.IsNullOrEmpty(jCell["CellValue"]?.ToString()))
                {
                    cell.CellValue = new CellValue(string.Empty);
                }
                else if (DateTime.TryParse(jCell["CellValue"]?.ToString(), out var date))
                {
                    cell.CellValue = new CellValue(date.ToUniversalTime().ToString());
                }

                cell.DataType = new EnumValue<CellValues>(CellValues.Number);
                cell.StyleIndex = this.GetStyleIndex(jCell, styleMap, (rowIndex > 0 && (rowIndex % 2) != 0));
                if (cell.StyleIndex.Value == 0)
                {
                    cell.StyleIndex = UInt32Value.FromUInt32(5);
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
                var text = jCell["CellValue"]!.ToString();
                if (dataType == CellValues.Boolean)
                {
                    // a boolean cell holds 1 or 0
                    text = bool.TryParse(text, out var flag) ? (flag ? "1" : "0") : text;
                }

                cell.CellValue = new CellValue(text);
                cell.DataType = new EnumValue<CellValues>(dataType);
                cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0));
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

        private UInt32Value GetStyleIndex(JToken jCell, JToken styleMap, bool useAlternative = false)
        {
            if (jCell["StyleIndex"] != null)
            {
                return UInt32Value.FromUInt32(Convert.ToUInt32(jCell["StyleIndex"]));
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

            var map = styleMap.SelectToken($"[?(@.Key == '{format}')]");
            if (map != null && map["Key"]?.ToString() == format)
            {
                if (useAlternative && map["SAlternativeIndex"] != null)
                {
                    return UInt32Value.FromUInt32(Convert.ToUInt32(map["SAlternativeIndex"]));
                }

                return UInt32Value.FromUInt32(Convert.ToUInt32(map["SIndex"]));
            }

            return UInt32Value.FromUInt32(0);
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