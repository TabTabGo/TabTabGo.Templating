using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TabTabGo.Templating.TestKit;

/// <summary>
/// Builds small <c>.xlsx</c> template fixtures in code (no binary fixtures in the repository). Cell text that is placed with
/// <see cref="Text"/> is stored as a shared string, so <c>{%$.path%}</c> macros are found by the OpenXml engine.
/// </summary>
public sealed class TemplateBuilder
{
    private readonly List<SheetSpec> _sheets = new();
    private bool _omitSharedStrings;
    private bool _omitCalculationProperties;

    private TemplateBuilder()
    {
    }

    /// <summary>Starts a builder with no sheets.</summary>
    public static TemplateBuilder Create() => new();

    /// <summary>Starts a new sheet; the following cell, table and merge calls apply to it.</summary>
    /// <param name="name">The sheet name.</param>
    public TemplateBuilder Sheet(string name)
    {
        _sheets.Add(new SheetSpec(name));
        return this;
    }

    /// <summary>Puts text (a shared string) into a cell.</summary>
    /// <param name="cell">A1-style reference.</param>
    /// <param name="text">The text or macro.</param>
    public TemplateBuilder Text(string cell, string text)
    {
        Current.Cells[cell] = new CellSpec(CellKind.Text, text);
        return this;
    }

    /// <summary>Puts an inline string (not a macro candidate) into a cell.</summary>
    /// <param name="cell">A1-style reference.</param>
    /// <param name="text">The text.</param>
    public TemplateBuilder Inline(string cell, string text)
    {
        Current.Cells[cell] = new CellSpec(CellKind.Inline, text);
        return this;
    }

    /// <summary>Puts a number into a cell.</summary>
    /// <param name="cell">A1-style reference.</param>
    /// <param name="value">The value.</param>
    public TemplateBuilder Number(string cell, double value)
    {
        Current.Cells[cell] = new CellSpec(CellKind.Number, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>Puts a formula into a cell.</summary>
    /// <param name="cell">A1-style reference.</param>
    /// <param name="formula">The formula text without <c>=</c>.</param>
    public TemplateBuilder Formula(string cell, string formula)
    {
        Current.Cells[cell] = new CellSpec(CellKind.Formula, formula);
        return this;
    }

    /// <summary>Adds a table over <paramref name="range"/>; the first row holds the header texts.</summary>
    /// <param name="name">Table name.</param>
    /// <param name="range">For example <c>A3:C5</c>.</param>
    /// <param name="withAutoFilter">Whether the table carries an auto filter.</param>
    public TemplateBuilder Table(string name, string range, bool withAutoFilter = false)
    {
        Current.Tables.Add(new TableSpec(name, range, withAutoFilter));
        return this;
    }

    /// <summary>Merges a range on the current sheet.</summary>
    /// <param name="range">For example <c>A10:C10</c>.</param>
    public TemplateBuilder Merge(string range)
    {
        Current.Merges.Add(range);
        return this;
    }

    /// <summary>Leaves the shared-string part out (only valid when no cell uses <see cref="Text"/>).</summary>
    public TemplateBuilder WithoutSharedStringPart()
    {
        _omitSharedStrings = true;
        return this;
    }

    /// <summary>Leaves the workbook calculation properties out.</summary>
    public TemplateBuilder WithoutCalculationProperties()
    {
        _omitCalculationProperties = true;
        return this;
    }

    /// <summary>Writes the workbook into memory.</summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());

            var shared = new List<string>();
            var sharedIndex = new Dictionary<string, int>();
            uint sheetId = 1;
            foreach (var spec in _sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                foreach (var row in spec.Cells.GroupBy(c => RowOf(c.Key)).OrderBy(g => g.Key))
                {
                    var rowElement = new Row { RowIndex = (uint)row.Key };
                    foreach (var cell in row.OrderBy(c => ColumnNumber(c.Key)))
                    {
                        rowElement.Append(ToCell(cell.Key, cell.Value, shared, sharedIndex));
                    }

                    sheetData.Append(rowElement);
                }

                var worksheet = new Worksheet();
                worksheet.Append(new SheetDimension { Reference = Dimension(spec) });
                worksheet.Append(sheetData);
                if (spec.Merges.Count > 0)
                {
                    var merges = new MergeCells();
                    foreach (var merge in spec.Merges)
                    {
                        merges.Append(new MergeCell { Reference = merge });
                    }

                    worksheet.Append(merges);
                }

                worksheetPart.Worksheet = worksheet;

                if (spec.Tables.Count > 0)
                {
                    var tableParts = new TableParts { Count = (uint)spec.Tables.Count };
                    uint tableId = 1;
                    foreach (var table in spec.Tables)
                    {
                        var tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
                        tablePart.Table = BuildTable(table, tableId++, spec);
                        tableParts.Append(new TablePart { Id = worksheetPart.GetIdOfPart(tablePart) });
                    }

                    worksheet.Append(tableParts);
                }

                worksheet.Save();
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId++, Name = spec.Name });
            }

            if (!_omitSharedStrings)
            {
                var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
                var table = new SharedStringTable { Count = (uint)shared.Count, UniqueCount = (uint)shared.Count };
                foreach (var text in shared)
                {
                    table.Append(new SharedStringItem(new Text(text)));
                }

                sstPart.SharedStringTable = table;
                sstPart.SharedStringTable.Save();
            }

            if (!_omitCalculationProperties)
            {
                workbookPart.Workbook.Append(new CalculationProperties { CalculationId = 191029U });
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>Writes the workbook into <paramref name="directory"/> and returns the file path.</summary>
    /// <param name="directory">Target folder.</param>
    /// <param name="fileName">File name.</param>
    public string Save(TempDir directory, string fileName = "template.xlsx")
    {
        var path = directory.Combine(fileName);
        File.WriteAllBytes(path, ToBytes());
        return path;
    }

    private SheetSpec Current => _sheets.Count > 0 ? _sheets[^1] : throw new InvalidOperationException("Call Sheet(name) first.");

    private Cell ToCell(string reference, CellSpec spec, List<string> shared, Dictionary<string, int> sharedIndex)
    {
        var cell = new Cell { CellReference = reference };
        switch (spec.Kind)
        {
            case CellKind.Text:
                if (_omitSharedStrings)
                {
                    throw new InvalidOperationException("Text() needs the shared-string part.");
                }

                if (!sharedIndex.TryGetValue(spec.Value, out var index))
                {
                    index = shared.Count;
                    shared.Add(spec.Value);
                    sharedIndex[spec.Value] = index;
                }

                cell.DataType = CellValues.SharedString;
                cell.CellValue = new CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case CellKind.Inline:
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString { Text = new Text(spec.Value) };
                break;
            case CellKind.Number:
                cell.CellValue = new CellValue(spec.Value);
                break;
            case CellKind.Formula:
                cell.CellFormula = new CellFormula(spec.Value);
                cell.CellValue = new CellValue("0");
                break;
        }

        return cell;
    }

    private static Table BuildTable(TableSpec spec, uint id, SheetSpec sheet)
    {
        var parts = spec.Range.Split(':');
        var first = ColumnNumber(parts[0]);
        var last = ColumnNumber(parts[1]);
        var headerRow = RowOf(parts[0]);
        var table = new Table
        {
            Id = id,
            Name = spec.Name,
            DisplayName = spec.Name,
            Reference = spec.Range,
            TotalsRowShown = false,
        };

        if (spec.WithAutoFilter)
        {
            table.Append(new AutoFilter { Reference = spec.Range });
        }

        var columns = new TableColumns { Count = (uint)(last - first + 1) };
        for (var column = first; column <= last; column++)
        {
            var reference = ColumnName(column) + headerRow;
            var header = sheet.Cells.TryGetValue(reference, out var cell) ? cell.Value : "Column" + column;
            columns.Append(new TableColumn { Id = (uint)(column - first + 1), Name = header });
        }

        table.Append(columns);
        return table;
    }

    private static string Dimension(SheetSpec spec)
    {
        if (spec.Cells.Count == 0)
        {
            return "A1";
        }

        var rows = spec.Cells.Keys.Select(RowOf).ToList();
        var columns = spec.Cells.Keys.Select(ColumnNumber).ToList();
        return $"A1:{ColumnName(columns.Max())}{rows.Max()}";
    }

    private static int RowOf(string reference) => int.Parse(new string(reference.Where(char.IsDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture);

    private static int ColumnNumber(string reference)
    {
        var number = 0;
        foreach (var letter in reference.TakeWhile(char.IsLetter))
        {
            number = number * 26 + (char.ToUpperInvariant(letter) - 'A' + 1);
        }

        return number;
    }

    private static string ColumnName(int number)
    {
        var name = string.Empty;
        while (number > 0)
        {
            number--;
            name = (char)('A' + number % 26) + name;
            number /= 26;
        }

        return name;
    }

    private enum CellKind
    {
        Text,
        Inline,
        Number,
        Formula,
    }

    private sealed record CellSpec(CellKind Kind, string Value);

    private sealed record TableSpec(string Name, string Range, bool WithAutoFilter);

    private sealed class SheetSpec(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, CellSpec> Cells { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<TableSpec> Tables { get; } = new();

        public List<string> Merges { get; } = new();
    }
}
