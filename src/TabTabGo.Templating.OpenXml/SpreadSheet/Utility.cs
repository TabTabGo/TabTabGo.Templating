using System;
using System.Text.RegularExpressions;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    public static class Utility
    {
        public static string GetColumnName(string cellName)
        {
            Regex regex = new Regex("[A-Za-z]+");
            Match match = regex.Match(cellName);

            return match.Value;
        }

        public static int GetRowIndex(string cellName)
        {
            Regex regex = new Regex(@"\d+");
            Match match = regex.Match(cellName);

            return Convert.ToInt32(match.Value);
        }

        public static int GetCellColIndex(string name)
        {
            name = GetColumnName(name);
            int number = 0;
            int pow = 1;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                number += (name[i] - 'A' + 1) * pow;
                pow *= 26;
            }

            return number;
        }

        public static string GetNextColumnName(string cellName)
        {
            int index = GetCellColIndex(cellName);
            return NextColumn(index + 1);
        }

        public static string NextColumn(int column)
        {
            column--;
            if (column >= 0 && column < 26)
                return ((char)('A' + column)).ToString();
            else if (column > 25)
                return NextColumn(column / 26) + NextColumn(column % 26 + 1);
            else
                throw new Exception("Invalid Column #" + (column + 1).ToString());
        }
    }
}