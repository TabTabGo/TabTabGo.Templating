using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

/// <summary>
/// The shared-string table of one workbook for one render: an index from text to position built once (O(1) lookups instead of a scan per
/// insert), one save at the end, and the part is created when the template has none (X-12).
/// </summary>
internal sealed class SharedStrings
{
    private readonly WorkbookPart _workbook;
    private Dictionary<string, int>? _index;
    private SharedStringTable? _table;
    private int _count;
    private bool _changed;

    public SharedStrings(WorkbookPart workbook)
    {
        _workbook = workbook;
    }

    /// <summary>The texts of the template's shared strings by position (empty when the workbook has no table).</summary>
    public IReadOnlyList<string> ReadAll() =>
        _workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(item => item.InnerText).ToList() ?? new List<string>();

    /// <summary>The position of <paramref name="text"/>, appended when it is not in the table yet.</summary>
    public int GetOrAdd(string text)
    {
        Ensure();
        if (_index!.TryGetValue(text, out var position))
        {
            return position;
        }

        position = _count++;
        _table!.Append(new SharedStringItem(new Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        _index[text] = position;
        _changed = true;
        return position;
    }

    /// <summary>Writes the table once when anything was added.</summary>
    public void Save()
    {
        if (!_changed || _table == null)
        {
            return;
        }

        _table.Count = (uint)_count;
        _table.UniqueCount = (uint)_count;
        _table.Save();
    }

    private void Ensure()
    {
        if (_index != null)
        {
            return;
        }

        var part = _workbook.SharedStringTablePart ?? _workbook.AddNewPart<SharedStringTablePart>();
        _table = part.SharedStringTable ??= new SharedStringTable();
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in _table.Elements<SharedStringItem>())
        {
            _index.TryAdd(item.InnerText, _count);
            _count++;
        }
    }
}
