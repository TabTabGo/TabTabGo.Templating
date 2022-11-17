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
            using (SpreadsheetDocument document = SpreadsheetDocument.Create(fileName, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet();

                // Adding style
                WorkbookStylesPart stylePart = workbookPart.AddNewPart<WorkbookStylesPart>();
                var jToken = json.SelectToken("$.Workbook.Stylesheets");
                if (jToken["TemplatePath"] != null)
                {
                    var xStyle = XDocument.Load(PathUtility.ResolvePath(jToken["TemplatePath"].ToString()));
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
                        BestFit = BooleanValue.FromBoolean((bool)jColumn["BestFit"])
                    };

                    if (jColumn["Width"] != null)
                    {
                        column.Width = DoubleValue.FromDouble((double)jColumn["Width"]);
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
                Row row = null;
                var iRow = -1; // to ignore header
                foreach (JToken jRow in jRows)
                {
                    row = new Row { Collapsed = Convert.ToBoolean(jRow["Collapsed"]) };
                    var jcells = jRow.SelectToken("Cells");

                    foreach (JToken jcell in jcells)
                    {
                        row.AppendChild(this.ConstructCell(jcell, styleMap, iRow));
                    }
                    iRow++;
                    sheetData.AppendChild(row);
                }

                worksheetPart.Worksheet.Save();
            }
        }

        /// <summary>
        /// The construct cell.
        /// </summary>
        /// <param name="jCell">
        /// The j cell.
        /// </param>
        /// <returns>
        /// The <see cref="Cell"/>.
        /// </returns>
        private Cell ConstructCell(JToken jCell, JToken styleMap, int rowIndex)
        {
            if (!Enum.TryParse(jCell["DataType"].ToString(), out CellValues dataType))
            {
                dataType = CellValues.String;
            }
            var cell = new Cell();
            switch (dataType)
            {
                case CellValues.Date:
                    if (string.IsNullOrEmpty(jCell["CellValue"].ToString()))
                    {
                        cell.CellValue = new CellValue(string.Empty);
                    }
                    else
                    {
                        var date = DateTime.Parse(jCell["CellValue"].ToString());
#if NET452
                        cell.CellValue = new CellValue(date.ToUniversalTime().ToOADate().ToString());
#else
                        cell.CellValue = new CellValue(date.ToUniversalTime().ToString());
#endif
                    }

                    cell.DataType = new EnumValue<CellValues>(CellValues.Number);
                    cell.StyleIndex = this.GetStyleIndex(jCell, styleMap, (rowIndex > 0 && (rowIndex % 2) != 0));
                    if (cell.StyleIndex.Value == 0)
                    {
                        cell.StyleIndex = UInt32Value.FromUInt32(5);
                    }
                    break;
                //TODO support inlineString and SharedString
                case CellValues.Boolean:
                case CellValues.Error:
                case CellValues.Number:
                case CellValues.InlineString:
                case CellValues.String:
                default:
                    cell.CellValue = new CellValue(jCell["CellValue"].ToString());
                    cell.DataType = new EnumValue<CellValues>(dataType);
                    cell.StyleIndex = GetStyleIndex(jCell, styleMap, (rowIndex > 0 && rowIndex % 2 != 0));
                    break;
            }

            return cell;
        }

        private UInt32Value GetStyleIndex(JToken jCell, JToken styleMap, bool useAlternative = false)
        {
            if (jCell["StyleIndex"] != null)
            {
                return UInt32Value.FromUInt32(Convert.ToUInt32(jCell["StyleIndex"]));
            }

            string format = null;
            if (jCell["Format"] != null)
            {
                format = jCell["Format"].ToString().Trim().Replace(" ", "");
            }

            if (string.IsNullOrEmpty(format))
            {
                format = "Normal";
            }

            var map = styleMap.SelectToken($"[?(@.Key == '{format}')]");
            if (map != null && map["Key"].ToString() == format)
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
            //var jFonts = stylesheets.SelectToken("Fonts");
            //Fonts fonts = new Fonts();
            //foreach (dynamic jFont in jFonts)
            //{
            //    var font = new Font();
            //    if (jFont.FontSize)
            //    {
            //        font.AppendChild(new FontSize() { Val = jFont.FontSize });
            //    }
            //    if (jFont.Bold)
            //    {
            //        font.AppendChild(new FontSize() { Val = jFont.FontSize });
            //    }
            //    fonts.AppendChild(new Font({ new FontSize() { Val = jFont.FontSize } ,  );
            //}
            //Fonts fonts = new Fonts(
            //    new Font( // Index 0 - default
            //        new FontSize() { Val = 10 }

            //    ),
            //    new Font( // Index 1 - header
            //        new FontSize() { Val = 10 },
            //        new Bold(),
            //        new Color() { Rgb = "FFFFFF" }

            //    ));

            //Fills fills = new Fills(
            //        new Fill(new PatternFill() { PatternType = PatternValues.None }), // Index 0 - default
            //        new Fill(new PatternFill() { PatternType = PatternValues.Gray125 }), // Index 1 - default
            //        new Fill(new PatternFill(new ForegroundColor { Rgb = new HexBinaryValue() { Value = "66666666" } })
            //        { PatternType = PatternValues.Solid }) // Index 2 - header
            //    );

            //Borders borders = new Borders(
            //        new Border(), // index 0 default
            //        new Border( // index 1 black border
            //            new LeftBorder(new Color() { Auto = true }) { Style = BorderStyleValues.Thin },
            //            new RightBorder(new Color() { Auto = true }) { Style = BorderStyleValues.Thin },
            //            new TopBorder(new Color() { Auto = true }) { Style = BorderStyleValues.Thin },
            //            new BottomBorder(new Color() { Auto = true }) { Style = BorderStyleValues.Thin },
            //            new DiagonalBorder())
            //    );
            //var cell = new CellFormat();

            //CellFormats cellFormats = new CellFormats(
            //        new CellFormat(), // default
            //        new CellFormat { FontId = 0, FillId = 0, BorderId = 1, ApplyBorder = true }, // body
            //        new CellFormat { FontId = 1, FillId = 2, BorderId = 1, ApplyFill = true } // header
            //    );

            //styleSheet = new Stylesheet(fonts, fills, borders, cellFormats);

            return styleSheet;
        }
    }
}
