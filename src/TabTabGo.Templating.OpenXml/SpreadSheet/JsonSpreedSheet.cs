using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Xml.Linq;
using TabTabGo.IO.Utilities;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    public class JsonSpreedSheet
    {
        public void CreateExcelDoc(string fileName, JObject json)
        {
            using SpreadsheetDocument
                document = SpreadsheetDocument.Create(fileName, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet();

            // Adding style
            WorkbookStylesPart stylePart = workbookPart.AddNewPart<WorkbookStylesPart>();
            var jToken = json.SelectToken("$.Workbook.Stylesheets");
            if (jToken?["TemplatePath"] != null)
            {
                var xStyle = XDocument.Load(PathUtility.ResolvePath(jToken["TemplatePath"]?.ToString()!));
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
            if (!Enum.TryParse(jCell["DataType"]?.ToString(), out CellValues dataType))
            {
                dataType = CellValues.String;
            }

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
                cell.CellValue = new CellValue(jCell["CellValue"]!.ToString());
                cell.DataType = new EnumValue<CellValues>(dataType);
                cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0));
            }

            return cell;
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