using System.Globalization;
using System.Text.RegularExpressions;

namespace TabTabGo.Templating.OpenXml.SpreadSheet;

/// <summary>Helpers for A1-style cell references.</summary>
public static class Utility
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>The column letters of a cell reference (<c>AB12</c> gives <c>AB</c>); empty when there are none.</summary>
    /// <param name="cellName">The cell reference.</param>
    public static string GetColumnName(string cellName)
    {
        ArgumentNullException.ThrowIfNull(cellName);
        return Letters.Match(cellName).Value;
    }

    /// <summary>The row number of a cell reference (<c>AB12</c> gives 12).</summary>
    /// <param name="cellName">The cell reference.</param>
    /// <exception cref="ArgumentException">The reference has no row number.</exception>
    public static int GetRowIndex(string cellName)
    {
        ArgumentNullException.ThrowIfNull(cellName);
        var match = Digits.Match(cellName);
        if (!match.Success)
        {
            throw new ArgumentException("The cell reference has no row number.", nameof(cellName));
        }

        return int.Parse(match.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The 1-based column number of a cell reference (<c>A1</c> is 1, <c>AA1</c> is 27); letters may be lower case.</summary>
    /// <param name="name">The cell reference or column letters.</param>
    public static int GetCellColIndex(string name)
    {
        name = GetColumnName(name);
        var number = 0;
        var pow = 1;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            number += (char.ToUpperInvariant(name[i]) - 'A' + 1) * pow;
            pow *= 26;
        }

        return number;
    }

    /// <summary>The letters of the column after the one in <paramref name="cellName"/>.</summary>
    /// <param name="cellName">The cell reference or column letters.</param>
    public static string GetNextColumnName(string cellName) => NextColumn(GetCellColIndex(cellName) + 1);

    /// <summary>The letters of a 1-based column number (<c>1</c> is <c>A</c>, <c>27</c> is <c>AA</c>, <c>16384</c> is <c>XFD</c>).</summary>
    /// <param name="column">The column number.</param>
    /// <exception cref="ArgumentOutOfRangeException">The number is not positive.</exception>
    public static string NextColumn(int column)
    {
        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "The column number must be at least 1.");
        }

        var letters = string.Empty;
        while (column > 0)
        {
            column--;
            letters = (char)('A' + (column % 26)) + letters;
            column /= 26;
        }

        return letters;
    }
}
