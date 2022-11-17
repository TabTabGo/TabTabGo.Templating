using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using DotLiquid.Util;
using System.Globalization;
using DocumentFormat.OpenXml;
using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Linq.Dynamic;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static class OpenXmlFilters
    {
       

        public static StringValue OpenXmlToString(object input)
        {
            if (input == null)
            {
                return null;
            }

            return StringValue.FromString(input.ToString());
        }

        public static string OpenXmlType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return null;
            }
            var shortTypeName = type.Replace("System.", "");

            switch (shortTypeName)
            {
                case "Int32":
                case "Int16":
                case "Int64":
                case "Float":
                case "Decimal":
                case "Double":
                    return CellValues.Number.ToString();
                case "Boolean":
                    return CellValues.Boolean.ToString();
                case "Date":
                case "DateTime":
                case "DateTimeOffset":
                    return CellValues.Date.ToString();
                default:
                    return CellValues.String.ToString();
            }
        }

        public static string OpenXmlFormat(string format)
        {
            if (string.IsNullOrEmpty(format))
            {
                return null;
            }

            return format.Replace(" ", "");
        }

        public static string OpenXmlMaxWidth(IEnumerable list, string propertyName, string headerValue)
        {
            var headerLength = string.IsNullOrEmpty(headerValue) ? 0 : headerValue.Length;

            if (list != null && list.Cast<object>().Any() && list.Cast<JObject>().Any())
            {
                var propertyList = list.Cast<JObject>()
                        .Where(r => r[propertyName] != null)
                        .Select(r => r[propertyName].ToString().Length).ToList();

                if (propertyList.Any())
                {
                    var maxLength = propertyList.Max();
                    return maxLength > headerLength ? maxLength.ToString() : headerLength.ToString();
                }
            }
            return headerLength.ToString();
        }
    }
    
}
