using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using TabTabGo.IO.Utilities;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    public class TemplatingEngine : TabTabGo.Templating.TemplatingEngine
    {
        private const string START_EXPR = "{%";
        private const string END_EXPR = "%}";
        private const string ARRAY_PATH = "::";
        private const string VALUE_FORMAT = ":#";

        private WorkbookPart CurrentWorkbookPart { get; set; }

        //ToDo: add implementation with cache capablities
        public override bool IsCached(string templatePath)
        {
            //no caching is implemented
            return false;
        }

        public override void ParseTemplate(string templatePath)
        {
            //no parsing impelemted 
        }

        public override void RegisterFilter(Type type)
        {
            //no filters implementation
        }

        /// <summary>
        /// Renders the specified template path.
        /// </summary>
        /// <param name="templatePath">The template path.</param>
        /// <param name="sourceData">The source data.</param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException">
        /// Data source can't be serialized to Json. Please make sure object is serilazable to json.
        /// or
        /// Generate SpreadSheet can't be renderd.
        /// </exception>
        /// <exception cref="NotImplementedException"></exception>
        public override object Render(string templatePath, object sourceData, string culture = "en-Us")
        {
            var jObject = (sourceData as JObject) ?? JObject.FromObject(sourceData);
            if (jObject == null)
            {
                throw new NotSupportedException("Data source can't be serialized to Json. Please make sure object is serilazable to json.");
            }

            string outputFilePath = Path.GetTempFileName() + Path.GetExtension(templatePath);
            if (string.IsNullOrEmpty(templatePath) && jObject.SelectToken("$.Workbook") != null)
            {
                // Generate standard SpreadSheet
                var jSpressdSheet = new JsonSpreedSheet();
                jSpressdSheet.CreateExcelDoc(outputFilePath, jObject);
            }
            else if (!string.IsNullOrEmpty(templatePath))
            {

                Dictionary<int, string> macroList = null;
                //prepare excel file
                string sourceFilePath = templatePath;

                File.Copy(sourceFilePath, outputFilePath, true);

                using (var excel = SpreadsheetDocument.Open(outputFilePath, true))
                {
                    this.CurrentWorkbookPart = excel.WorkbookPart;
                    macroList = GetListOfMacroToReplace(CurrentWorkbookPart);
                    ParseMacroCells(CurrentWorkbookPart, jObject, macroList);
                }
            }
            else
            {
                throw new NotSupportedException("Generate SpreadSheet can't be render.");
            }

            var mStream = new MemoryStream(FileUtility.GetFileContent(outputFilePath));

            try
            {
                File.Delete(outputFilePath);
            }
            catch (UnauthorizedAccessException unauthorized)
            {
                // ignored
            }
            catch (NotSupportedException nexException)
            {

            }
            catch (Exception exception)
            {

            }

            return mStream;


        }

        #region Helper function


        /// <summary>
        /// Gets the json path.
        /// </summary>
        /// <param name="macro">The macro.</param>
        /// <returns></returns>
        private string GetJsonPath(string macro)
        {
            return !string.IsNullOrEmpty(macro) ? macro.Replace(START_EXPR, String.Empty).Replace(END_EXPR, string.Empty) : string.Empty;
        }

        /// <summary>
        /// Parses the macro cells.
        /// </summary>
        /// <param name="workBookPart">The work book part.</param>
        /// <param name="data">The data.</param>
        /// <param name="macroList">The macro list.</param>
        private void ParseMacroCells(WorkbookPart workBookPart, JObject data, Dictionary<int, string> macroList)
        {
            var currWorkBookPart = workBookPart;
            var sheets = currWorkBookPart.Workbook.GetFirstChild<Sheets>().Elements<Sheet>();

            foreach (var currSheet in sheets)
            {
                var currWorksheetPart = (WorksheetPart)currWorkBookPart.GetPartById(currSheet.Id);
                var currSheetData = currWorksheetPart.Worksheet.GetFirstChild<SheetData>();
                var sharedStringTable = currWorkBookPart.SharedStringTablePart.SharedStringTable;

                //write data to Fix cell Not Table
                var currCells = currSheetData.Descendants<Cell>().Where(c => c.DataType != null
                                                                             && c.DataType.HasValue
                                                                             && c.DataType.Value == CellValues.SharedString
                                                                             && macroList.ContainsKey(Convert.ToInt32(c.CellValue.InnerText)));

                foreach (var cell in currCells)
                {
                    var jsonPath = macroList[Convert.ToInt32(cell.CellValue.InnerText)];
                    if (!jsonPath.Contains(ARRAY_PATH))
                    {
                        UpdateCellValue(cell, null, data, jsonPath, 0);
                    }
                }

                var tableIndex = 0;
                //write data to templated cells for vendor details
                foreach (var part in currWorksheetPart.TableDefinitionParts)
                {

                    var table = part.Table;
                    var newTablePartId = currWorksheetPart.GetIdOfPart(part);
                    var range = table.Reference.Value.Split(':');
                    var startColumn = Utility.GetColumnName(range[0]);
                    var endColumn = Utility.GetColumnName(range[1]);
                    var startRowTableRange = Utility.GetRowIndex(range[0]);
                    var endRowTableRange = Utility.GetRowIndex(range[1]);
                    var templateRowRange = endRowTableRange;
                    var rowsToAdd = new List<Row>();
                    var sharedFormulaCells = new Dictionary<int, Tuple<int, Cell>>();
                    //TODO template row will always assume the 2nd row after column row of table
                    if ((endRowTableRange - startRowTableRange) > 1)
                    {
                        templateRowRange = startRowTableRange + 1;
                        for (int i = startRowTableRange + 2; i <= endRowTableRange; i++)
                        {
                            rowsToAdd.Add(currSheetData.Elements<Row>().First(row => row.RowIndex == i));
                        }
                    }
                    //Add all rows after table to update the index
                    var rowsAfterTable = currSheetData.Elements<Row>().Where(row => row.RowIndex > endRowTableRange).ToList();

                    var anchorRow = currSheetData.Elements<Row>().First(row => row.RowIndex == templateRowRange);
                    var templateCells = anchorRow.Elements<Cell>().ToList();

                    var rowIndex = templateRowRange;

                    var jsonPath =
                        templateCells.Where(templateCell => templateCell.DataType != null
                                                            && templateCell.DataType.HasValue
                                                            && templateCell.DataType.Value == CellValues.SharedString
                                                            &&
                                                            macroList.ContainsKey(
                                                                Convert.ToInt32(templateCell.CellValue.InnerText)))
                            .Select(templateCell => macroList[Convert.ToInt32(templateCell.CellValue.InnerText)])
                            .FirstOrDefault(jPath => jPath.Contains(ARRAY_PATH));

                    if (!string.IsNullOrEmpty(jsonPath))
                    {
                        var jsonArrayPath = jsonPath.Substring(0,
                            jsonPath.IndexOf(ARRAY_PATH, StringComparison.CurrentCultureIgnoreCase));

                        var jArray = data.SelectTokens(jsonArrayPath).Children();
                        var aIndex = 0;
                        foreach (var jToken in jArray)
                        {
                            var nRow = new Row() { RowIndex = (uint)rowIndex };
                            var colIndex = 0;
                            foreach (var oCell in templateCells)
                            {
                                var columnjPath = string.Empty;

                                if (oCell.DataType != null && oCell.DataType.HasValue &&
                                    oCell.DataType.Value == CellValues.SharedString &&
                                    macroList.ContainsKey(Convert.ToInt32(oCell.CellValue.InnerText)))
                                {
                                    var jPath = macroList[Convert.ToInt32(oCell.CellValue.InnerText)];
                                    if (jPath.Contains(ARRAY_PATH))
                                    {
                                        columnjPath = jPath.Substring(jPath.IndexOf(ARRAY_PATH, StringComparison.CurrentCultureIgnoreCase) + ARRAY_PATH.Length);
                                    }
                                }

                                var nCell = new Cell()
                                {
                                    StyleIndex = oCell.StyleIndex,
                                    CellReference = Utility.GetColumnName(oCell.CellReference) + rowIndex
                                };

                                if (aIndex == 0 && oCell.CellFormula != null)
                                {
                                    nCell.CellFormula = new CellFormula()
                                    {
                                        FormulaType = new EnumValue<CellFormulaValues>(CellFormulaValues.Shared),
                                        SharedIndex = (uint)sharedFormulaCells.Count,
                                        Text = oCell.CellFormula.Text

                                    };
                                    sharedFormulaCells.Add(colIndex,
                                        new Tuple<int, Cell>(sharedFormulaCells.Count, nCell));
                                }
                                else if (oCell.CellFormula != null)
                                {
                                    nCell.CellFormula = new CellFormula()
                                    {
                                        SharedIndex = (uint)(sharedFormulaCells.ContainsKey(colIndex) ? sharedFormulaCells[colIndex].Item1 : 0),
                                        FormulaType = new EnumValue<CellFormulaValues>(CellFormulaValues.Shared)
                                    };
                                }
                                else
                                {
                                    UpdateCellValue(nCell, oCell, jToken, columnjPath, rowIndex);
                                }


                                nRow.Append(nCell);
                                colIndex++;
                            }

                            currSheetData.InsertBefore(nRow, anchorRow);
                            rowIndex++;
                            aIndex++;
                        }
                    }

                    //Modify the shared cell formula
                    foreach (var formulaCell in sharedFormulaCells)
                    {
                        formulaCell.Value.Item2.CellFormula.Reference = $"{formulaCell.Value.Item2.CellReference}:{Utility.GetColumnName(formulaCell.Value.Item2.CellReference)}{rowIndex}";
                    }

                    rowIndex = UpdateRowsIndex(rowsToAdd, rowIndex);
                    //remove anchor row
                    currSheetData.RemoveChild(anchorRow);
                    table.Reference.Value = $"{startColumn}{startRowTableRange}:{endColumn}{rowIndex - 1}";
                    rowIndex = UpdateRowsIndex(rowsAfterTable, rowIndex);
                    currWorksheetPart.Worksheet.SheetDimension.Reference = $"A1:{endColumn}{rowIndex - 1}";
                    tableIndex++;
                }

                currWorkBookPart.Workbook.CalculationProperties.ForceFullCalculation = true;
                currWorkBookPart.Workbook.CalculationProperties.FullCalculationOnLoad = true;
                currWorksheetPart.Worksheet.Save();

            }

        }

        /// <summary>
        /// Updates the index of the rows.
        /// </summary>
        /// <param name="rows">The rows.</param>
        /// <param name="rowIndex">Index of the row.</param>
        /// <returns></returns>
        private int UpdateRowsIndex(List<Row> rows, int rowIndex)
        {
            foreach (var cloneRow in rows)
            {
                cloneRow.RowIndex.Value = (uint)rowIndex;
                foreach (var cloneCell in cloneRow.Elements<Cell>())
                {
                    cloneCell.CellReference = Utility.GetColumnName(cloneCell.CellReference) + rowIndex;
                }
                //currSheetData.InsertBefore(cloneRow, anchorRow);
                rowIndex++;
            }


            return rowIndex;
        }

        /// <summary>
        /// Gets the list of macro to replace.
        /// </summary>
        /// <param name="workbookPart">The workbook part.</param>
        /// <returns></returns>
        private Dictionary<int, string> GetListOfMacroToReplace(WorkbookPart workbookPart)
        {
            var sst = workbookPart.SharedStringTablePart.SharedStringTable;
            var index = 0;
            var ssDictionary = sst.Elements<SharedStringItem>().ToDictionary(ssValue => index++, ssValue => ssValue.InnerText);
            return
                ssDictionary.Where(ss => ss.Value.StartsWith(START_EXPR) && ss.Value.EndsWith(END_EXPR))
                    .ToDictionary(ss => ss.Key, ss => GetJsonPath(ss.Value));

        }
        /// <summary>
        /// Create new columns and add it to the column collection
        /// </summary>
        /// <param name="endColumn"></param>
        /// <param name="startRowTableRange"></param>
        /// <param name="endRowTableRange"></param>
        /// <param name="workbookPart"></param>
        /// <param name="anchorRow"></param>
        /// <param name="anchorCell"></param>
        /// <param name="blankanchorRow"></param>
        /// <param name="blankanchorCell"></param>
        /// <param name="totalColumns"></param>
        /// <param name="lastcolumn"></param>
        /// <param name="newColumnList"></param>
        /// <param name="i"></param>
        /// <returns>string</returns>
        private string AddNewCol(string endColumn, int startRowTableRange, int endRowTableRange, WorkbookPart workbookPart, Row anchorRow, Cell anchorCell, Row blankanchorRow, Cell blankanchorCell, UInt32Value totalColumns, TableColumn lastcolumn, List<TableColumn> newColumnList, int i)
        {
            var colName = string.Empty;
            var newColumn = new TableColumn();

            switch (i)
            {
                case 0:
                    colName = "Actual Price";
                    break;
                case 1:
                    colName = "Tax";
                    break;
                case 2:
                    colName = "Total Price";
                    break;
                default:
                    colName = string.Empty;
                    break;
            }

            newColumn.Id = (uint)(totalColumns + i);
            newColumn.DataFormatId = lastcolumn.DataFormatId;
            newColumn.Name = colName;
            newColumn.DataCellStyle = lastcolumn.DataCellStyle;
            newColumnList.Add(newColumn);

            endColumn = Utility.GetNextColumnName(endColumn); //Get the next column

            var index = InsertSharedStringItem(workbookPart, colName);//insert shared string item

            var newCell = new Cell();
            newCell.CellReference = string.Format("{0}{1}", endColumn, startRowTableRange);
            newCell.StyleIndex = anchorCell.StyleIndex;
            newCell.CellValue = new CellValue(index.ToString());
            newCell.DataType = new EnumValue<CellValues>(CellValues.SharedString);

            anchorRow.Append(newCell);

            //add blank row           
            index = InsertSharedStringItem(workbookPart, colName.Replace(" ", ""));
            newCell = new Cell();
            newCell.CellReference = string.Format("{0}{1}", endColumn, endRowTableRange); ;
            newCell.StyleIndex = blankanchorCell.StyleIndex;
            newCell.CellValue = new CellValue(index.ToString());
            newCell.DataType = new EnumValue<CellValues>(CellValues.SharedString);

            blankanchorRow.Append(newCell);
            return endColumn;

        }

        /// <summary>
        /// Updates the cell value.
        /// </summary>
        /// <param name="nCell">The n cell.</param>
        /// <param name="templateCell">The template cell.</param>
        /// <param name="data">The data.</param>
        /// <param name="jsonValuePath">The json value path.</param>
        /// <param name="rowIndex">Index of the row.</param>
        private void UpdateCellValue(Cell nCell, Cell templateCell, JToken data, string jsonValuePath, int rowIndex)
        {
            if (string.IsNullOrEmpty(jsonValuePath))
            {
                nCell.InnerXml = templateCell.InnerXml;
            }
            else
            {
                var jsonValueWithoutFormat = jsonValuePath;
                var valueFormat = string.Empty;
                var iFormatKey = jsonValuePath.IndexOf(VALUE_FORMAT, StringComparison.CurrentCultureIgnoreCase);
                if (iFormatKey > 1)
                {
                    jsonValueWithoutFormat = jsonValuePath.Substring(0, iFormatKey);
                    valueFormat = jsonValuePath.Substring(iFormatKey + VALUE_FORMAT.Length);
                }
                var value = data.SelectToken(jsonValueWithoutFormat);
                if (value != null)
                {
                    var valueString = value.ToString();
                    double dblValue = 0.0;
                    bool blValue = false;
                    DateTime dtValue = DateTime.UtcNow;


                    if (!string.IsNullOrEmpty(valueFormat) && valueFormat.Equals("IS", StringComparison.CurrentCulture))
                    {
                        valueString = ToValidXmlCharactersString(valueString);
                        //valueString = ReplaceHexadecimalSymbols(valueString);
                        UpdateInlineCellValue(nCell, valueString);
                    }
                    else if (!string.IsNullOrEmpty(valueFormat) && valueFormat.Equals("SS", StringComparison.CurrentCulture))
                    {
                        valueString = ToValidXmlCharactersString(valueString);
                        //valueString = ReplaceHexadecimalSymbols(valueString);
                        UpdateSharedStringCellValue(CurrentWorkbookPart, nCell, valueString);
                    }
                    else if (double.TryParse(valueString, out dblValue))
                    {
                        nCell.DataType = new EnumValue<CellValues>(CellValues.Number);
                        nCell.CellValue = new CellValue(valueString);
                    }
                    else if (bool.TryParse(valueString, out blValue))
                    {
                        nCell.DataType = new EnumValue<CellValues>(CellValues.Boolean);
                        nCell.CellValue = new CellValue(dblValue.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (DateTime.TryParse(valueString, out dtValue))
                    {
                        UpdateInlineCellValue(nCell,
                            string.IsNullOrEmpty(valueFormat) ? dtValue.ToString("G") : dtValue.ToString(valueFormat));
                    }
                    else
                    {
                        valueString = ToValidXmlCharactersString(valueString);
                        //valueString = ReplaceHexadecimalSymbols(valueString);
                        UpdateInlineCellValue(nCell, valueString);
                    }
                }
                else
                {
                    UpdateInlineCellValue(nCell, string.Empty);
                }
            }


        }

        /// <summary>
        /// Updates the cell formula row index
        /// </summary>
        /// <param name="innerText">formula text.</param>
        /// <param name="rowIndex">Index of the row.</param>
        /// <returns>Updated Formula withe new row Index</returns>
        private string UpdateCellFormula(string innerText, int rowIndex)
        {
            var cellExpression = new CellExpression();
            cellExpression.TokenizeExpression(innerText);
            foreach (var cell in cellExpression.ExpressionCells)
            {
                cell.RowIndex = rowIndex;
            }

            var node = cellExpression.Nodes.Dequeue();
            return node.ToString();
        }

        /// <summary>
        /// Update Cell inline text
        /// </summary>
        /// <param name="nCell">The nnew cell need to update the inline text.</param>
        /// <param name="text">The text.</param>
        /// <returns></returns>
        private Cell UpdateInlineCellValue(Cell nCell, string text)
        {
            nCell.DataType = new EnumValue<CellValues>(CellValues.InlineString);
            nCell.InlineString = new InlineString { Text = new Text { Text = text } };
            return nCell;
        }

        private Cell UpdateSharedStringCellValue(WorkbookPart workbookPart, Cell nCell, string text)
        {
            var sstIndex = InsertSharedStringItem(workbookPart, text);
            nCell.DataType = new EnumValue<CellValues>(CellValues.SharedString);
            nCell.CellValue = new CellValue(sstIndex.ToString());
            return nCell;
        }

        /// <summary>
        /// Insert data in Shared string collection for newly added columns
        /// </summary>
        /// <param name="workbookPart"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private int InsertSharedStringItem(WorkbookPart workbookPart, string value)
        {
            var index = 0;
            var found = false;
            var stringTablePart = workbookPart
                .GetPartsOfType<SharedStringTablePart>().FirstOrDefault();

            // If the shared string table is missing, something's wrong.
            // Just return the index that you found in the cell.
            // Otherwise, look up the correct text in the table.
            if (stringTablePart == null)
            {
                // Create it.
                stringTablePart = workbookPart.AddNewPart<SharedStringTablePart>();
            }

            var stringTable = stringTablePart.SharedStringTable;
            if (stringTable == null)
            {
                stringTable = new SharedStringTable();
            }

            // Iterate through all the items in the SharedStringTable. 
            // If the text already exists, return its index.
            foreach (SharedStringItem item in stringTable.Elements<SharedStringItem>())
            {
                if (item.InnerText == value)
                {
                    found = true;
                    break;
                }
                index += 1;
            }

            if (!found)
            {
                stringTable.AppendChild(new SharedStringItem(new Text(value)));
                stringTable.Save();
            }

            return index;
        }

        #region Handle Invalid XML Characters

        /// <summary>
        /// Determines if any invalid XML 1.0 characters exist within the string,
        /// and if so it returns a new string with the invalid chars removed, else 
        /// the same string is returned (with no wasted StringBuilder allocated, etc).
        /// </summary>
        /// <param name="s">Xml string.</param>
        /// <param name="startIndex">The index to begin checking at.</param>
        public static string ToValidXmlCharactersString(string s, int startIndex = 0)
        {
            int firstInvalidChar = IndexOfFirstInvalidXMLChar(s, startIndex);
            if (firstInvalidChar < 0)
                return s;

            startIndex = firstInvalidChar;

            int len = s.Length;
            var sb = new StringBuilder(len);

            if (startIndex > 0)
                sb.Append(s, 0, startIndex);

            for (int i = startIndex; i < len; i++)
                if (IsLegalXmlChar(s[i]))
                    sb.Append(s[i]);

            return sb.ToString();
        }

        /// <summary>
        /// Gets the index of the first invalid XML 1.0 character in this string, else returns -1.
        /// </summary>
        /// <param name="s">Xml string.</param>
        /// <param name="startIndex">Start index.</param>
        public static int IndexOfFirstInvalidXMLChar(string s, int startIndex = 0)
        {
            if (s != null && s.Length > 0 && startIndex < s.Length)
            {

                if (startIndex < 0) startIndex = 0;
                int len = s.Length;

                for (int i = startIndex; i < len; i++)
                    if (!IsLegalXmlChar(s[i]))
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// Indicates whether a given character is valid according to the XML 1.0 spec.
        /// This code represents an optimized version of Tom Bogle's on SO: 
        /// https://stackoverflow.com/a/13039301/264031.
        /// </summary>
        public static bool IsLegalXmlChar(char c)
        {
            if (c > 31 && c <= 55295)
                return true;
            if (c < 32)
                return c == 9 || c == 10 || c == 13;
            return (c >= 57344 && c <= 65533) || c > 65535;
            // final comparison is useful only for integral comparison, if char c -> int c, useful for utf-32 I suppose
            //c <= 1114111 */ // impossible to get a code point bigger than 1114111 because Char.ConvertToUtf32 would have thrown an exception
        }

        static Regex regexReplaceHex = new Regex("[\x00-\x08\x0B\x0C\x0E-\x1F\x26]", RegexOptions.Compiled);
        static string ReplaceHexadecimalSymbols(string txt)
        {
            //string r = "[\x00-\x08\x0B\x0C\x0E-\x1F\x26]";
            //return Regex.Replace(txt, r, "", RegexOptions.Compiled);

            if (string.IsNullOrEmpty(txt))
            {
                return txt;
            }
                
            return regexReplaceHex.Replace(txt, string.Empty);
        }

        #endregion

        #endregion
    }
}
