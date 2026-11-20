using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

/// <summary>
/// The state of one render of one workbook (B-7): the workbook, the data, the culture and the shared-string table. Nothing here is kept
/// on the engine, so one engine instance can render any number of workbooks in parallel.
/// </summary>
internal sealed class RenderSession
{
    private const string StartExpression = "{%";
    private const string EndExpression = "%}";
    private const string ArrayPath = "::";
    private const string ValueFormat = ":#";

    private readonly WorkbookPart _workbook;
    private readonly JToken _data;
    private readonly CultureInfo _culture;
    private readonly SharedStrings _sharedStrings;

    public RenderSession(WorkbookPart workbook, JToken data, CultureInfo culture)
    {
        _workbook = workbook;
        _data = data;
        _culture = culture;
        _sharedStrings = new SharedStrings(workbook);
    }

    public void Run()
    {
        var macros = ReadMacros();
        var sheets = _workbook.Workbook.GetFirstChild<Sheets>()?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>();
        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not string relationshipId || _workbook.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue; // a chart sheet or a dialog sheet
            }

            ProcessSheet(worksheetPart, macros);
        }

        var calculation = _workbook.Workbook.CalculationProperties ??= new CalculationProperties();
        calculation.ForceFullCalculation = true;
        calculation.FullCalculationOnLoad = true;

        _sharedStrings.Save();
    }

    private Dictionary<int, string> ReadMacros()
    {
        var macros = new Dictionary<int, string>();
        var texts = _sharedStrings.ReadAll();
        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].StartsWith(StartExpression, StringComparison.Ordinal) && texts[i].EndsWith(EndExpression, StringComparison.Ordinal))
            {
                macros[i] = texts[i].Replace(StartExpression, string.Empty).Replace(EndExpression, string.Empty);
            }
        }

        return macros;
    }

    private static bool TryGetMacro(Cell cell, Dictionary<int, string> macros, out string path)
    {
        path = string.Empty;
        return cell.DataType?.Value == CellValues.SharedString
            && cell.CellValue is { } value
            && int.TryParse(value.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && macros.TryGetValue(index, out path!);
    }

    private void ProcessSheet(WorksheetPart part, Dictionary<int, string> macros)
    {
        var worksheet = part.Worksheet;
        var sheetData = worksheet.GetFirstChild<SheetData>();
        if (sheetData == null)
        {
            return;
        }

        // fixed cells (everything that is not a table column macro), also inside table template rows
        foreach (var cell in sheetData.Descendants<Cell>().ToList())
        {
            if (TryGetMacro(cell, macros, out var path) && !path.Contains(ArrayPath, StringComparison.Ordinal))
            {
                WriteCell(cell, path, _data);
            }
        }

        var tables = part.TableDefinitionParts.Select(p => p.Table).Where(t => t.Reference?.Value != null).OrderBy(t => StartRow(t)).ToList();
        var nextSharedIndex = NextSharedIndex(sheetData);
        foreach (var table in tables)
        {
            ExpandTable(worksheet, sheetData, table, tables, macros, ref nextSharedIndex);
        }

        UpdateDimension(worksheet, sheetData);
        worksheet.Save();
    }

    private static int StartRow(Table table) => Utility.GetRowIndex(table.Reference!.Value!.Split(':')[0]);

    private static uint NextSharedIndex(SheetData sheetData)
    {
        var highest = sheetData.Descendants<CellFormula>().Where(f => f.SharedIndex != null).Select(f => (long)f.SharedIndex!.Value).DefaultIfEmpty(-1).Max();
        return (uint)(highest + 1);
    }

    private void ExpandTable(Worksheet worksheet, SheetData sheetData, Table table, List<Table> allTables, Dictionary<int, string> macros, ref uint nextSharedIndex)
    {
        var range = table.Reference!.Value!.Split(':');
        var startColumn = Utility.GetColumnName(range[0]);
        var endColumn = Utility.GetColumnName(range[1]);
        var startRow = Utility.GetRowIndex(range[0]);
        var endRow = Utility.GetRowIndex(range[1]);
        if (endRow <= startRow)
        {
            return; // a header only: there is no template row
        }

        // The template row is always the row after the header; rows below it inside the table range (a totals row) follow the data.
        var templateRowIndex = startRow + 1;
        var templateRow = sheetData.Elements<Row>().FirstOrDefault(row => row.RowIndex?.Value == (uint)templateRowIndex);
        if (templateRow == null)
        {
            return;
        }

        var templateCells = templateRow.Elements<Cell>().ToList();
        var arrayPath = templateCells
            .Select(cell => TryGetMacro(cell, macros, out var path) ? path : null)
            .FirstOrDefault(path => path != null && path.Contains(ArrayPath, StringComparison.Ordinal));
        if (arrayPath == null)
        {
            return;
        }

        var items = _data.SelectTokens(arrayPath[..arrayPath.IndexOf(ArrayPath, StringComparison.Ordinal)]).Children().ToList();
        var dataRows = Math.Max(items.Count, 1); // an empty array keeps one blank row so the table stays valid (X-9)
        var followers = sheetData.Elements<Row>().Where(row => row.RowIndex!.Value > templateRowIndex).ToList();

        var formulaColumns = new Dictionary<int, uint>();
        for (var i = 0; i < dataRows; i++)
        {
            var rowIndex = templateRowIndex + i;
            var row = (Row)templateRow.CloneNode(false);
            row.RowIndex = (uint)rowIndex;
            row.Spans = null;
            for (var c = 0; c < templateCells.Count; c++)
            {
                row.Append(BuildCell(templateCells[c], rowIndex, items.Count > 0 ? items[i] : null, items.Count > 0, i == 0, c, formulaColumns, ref nextSharedIndex, macros, templateRowIndex, items.Count));
            }

            sheetData.InsertBefore(row, templateRow);
        }

        sheetData.RemoveChild(templateRow);

        var delta = dataRows - 1;
        if (delta != 0)
        {
            foreach (var row in followers)
            {
                ShiftRow(row, delta);
            }

            ShiftMergedCells(worksheet, templateRowIndex, delta);
            foreach (var other in allTables.Where(t => !ReferenceEquals(t, table) && StartRow(t) > templateRowIndex))
            {
                other.Reference = ShiftRange(other.Reference!.Value!, delta);
                if (other.AutoFilter?.Reference?.Value != null)
                {
                    other.AutoFilter.Reference = ShiftRange(other.AutoFilter.Reference.Value, delta);
                }
            }
        }

        var lastDataRow = templateRowIndex + dataRows - 1;
        table.Reference = $"{startColumn}{startRow}:{endColumn}{endRow + delta}";
        if (table.AutoFilter != null)
        {
            table.AutoFilter.Reference = $"{startColumn}{startRow}:{endColumn}{lastDataRow}"; // the filter covers the header and the data, not the rows below them
        }
    }

    private Cell BuildCell(
        Cell template,
        int rowIndex,
        JToken? item,
        bool hasItems,
        bool firstRow,
        int columnPosition,
        Dictionary<int, uint> formulaColumns,
        ref uint nextSharedIndex,
        Dictionary<int, string> macros,
        int templateRowIndex,
        int itemCount)
    {
        var reference = Utility.GetColumnName(template.CellReference!.Value!) + rowIndex;

        if (template.CellFormula?.Text is { Length: > 0 } formulaText)
        {
            var formulaCell = new Cell { CellReference = reference, StyleIndex = template.StyleIndex };
            if (!hasItems)
            {
                formulaCell.CellFormula = new CellFormula(formulaText); // one blank row: a plain formula, nothing to share
            }
            else if (firstRow)
            {
                formulaColumns[columnPosition] = nextSharedIndex;
                formulaCell.CellFormula = new CellFormula(formulaText)
                {
                    FormulaType = CellFormulaValues.Shared,
                    SharedIndex = nextSharedIndex++,
                    Reference = $"{reference}:{Utility.GetColumnName(reference)}{templateRowIndex + itemCount - 1}",
                };
            }
            else
            {
                formulaCell.CellFormula = new CellFormula { FormulaType = CellFormulaValues.Shared, SharedIndex = formulaColumns[columnPosition] };
            }

            return formulaCell;
        }

        if (TryGetMacro(template, macros, out var macro) && macro.Contains(ArrayPath, StringComparison.Ordinal))
        {
            var cell = new Cell { CellReference = reference, StyleIndex = template.StyleIndex };
            if (hasItems)
            {
                WriteCell(cell, macro[(macro.IndexOf(ArrayPath, StringComparison.Ordinal) + ArrayPath.Length)..], item!);
            }

            return cell;
        }

        var clone = (Cell)template.CloneNode(true); // static text, numbers and already resolved fixed macros keep their type and style
        clone.CellReference = reference;
        return clone;
    }

    private void WriteCell(Cell cell, string macroPath, JToken context)
    {
        var path = macroPath;
        var format = string.Empty;
        var formatAt = macroPath.IndexOf(ValueFormat, StringComparison.OrdinalIgnoreCase);
        if (formatAt > 0)
        {
            path = macroPath[..formatAt];
            format = macroPath[(formatAt + ValueFormat.Length)..];
        }

        CellValueWriter.Write(cell, context.SelectToken(path), format, _culture, _sharedStrings);
    }

    private static void ShiftRow(Row row, int delta)
    {
        var oldRow = (int)row.RowIndex!.Value;
        row.RowIndex = (uint)(oldRow + delta);
        foreach (var cell in row.Elements<Cell>())
        {
            cell.CellReference = Utility.GetColumnName(cell.CellReference!.Value!) + row.RowIndex.Value;
            if (cell.CellFormula?.Text is { Length: > 0 } formula)
            {
                // a formula written for its own row keeps pointing at that row (B9*3 in row 9 becomes B11*3 in row 11); references to other rows are not rewritten
                cell.CellFormula.Text = SameRowReference(oldRow).Replace(formula, match => match.Groups[1].Value + (oldRow + delta));
            }

            if (cell.CellFormula?.Reference?.Value is { } sharedRange)
            {
                cell.CellFormula.Reference = ShiftRange(sharedRange, delta);
            }
        }
    }

    private static Regex SameRowReference(int row) =>
        new($@"(?<![A-Za-z0-9_!])(\$?[A-Za-z]{{1,3}}){row}(?![0-9A-Za-z_(])", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static void ShiftMergedCells(Worksheet worksheet, int afterRow, int delta)
    {
        foreach (var merge in worksheet.Descendants<MergeCell>())
        {
            var reference = merge.Reference?.Value;
            if (reference == null)
            {
                continue;
            }

            var parts = reference.Split(':');
            var first = Utility.GetRowIndex(parts[0]);
            if (first > afterRow)
            {
                merge.Reference = ShiftRange(reference, delta);
            }
        }
    }

    private static string ShiftRange(string range, int delta) =>
        string.Join(':', range.Split(':').Select(cell => Utility.GetColumnName(cell) + (Utility.GetRowIndex(cell) + delta)));

    private static void UpdateDimension(Worksheet worksheet, SheetData sheetData)
    {
        var cells = sheetData.Descendants<Cell>().Where(c => c.CellReference?.Value != null).ToList();
        var reference = cells.Count == 0
            ? "A1"
            : $"A1:{Utility.NextColumn(cells.Max(c => Utility.GetCellColIndex(c.CellReference!.Value!)))}{cells.Max(c => Utility.GetRowIndex(c.CellReference!.Value!))}";
        if (worksheet.SheetDimension == null)
        {
            worksheet.SheetDimension = new SheetDimension { Reference = reference };
        }
        else
        {
            worksheet.SheetDimension.Reference = reference;
        }
    }
}
