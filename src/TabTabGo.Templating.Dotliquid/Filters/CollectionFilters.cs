using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading.Tasks;
using DotLiquid;
using System.Xml.XPath;
using System.IO;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static class CollectionFilters
    {
        public static object SelectMany(IEnumerable array, string property)
        {
            if (string.IsNullOrEmpty(property))
            {
                return null;
            }

            var selectedArray = new List<object>();

            foreach (var item in array)
            {
                var value = ValueFilters.Select((Hash)item, property);

                if (value != null)
                {
                    selectedArray.Add(value);
                }
            }

            return selectedArray;
        }

        public static IEnumerable XPathSelectMany(string xmlInput, string xPath)
        {
            if (string.IsNullOrEmpty(xmlInput))
            {
                return xmlInput;
            }

            XPathNavigator nav;
            XPathDocument docNav;
            var reader = new StringReader(xmlInput);
            docNav = new XPathDocument(reader);
            nav = docNav.CreateNavigator();

            var nodes = nav.Select(xPath);
            var array = new List<object>();

            foreach (XPathNavigator node in nodes)
            {
                array.Add(node.Value);
            }

            return array;
        }

        public static object RemoveEmpty(IEnumerable array)
        {
            var modifiedArray = new List<object>();

            if (array == null)
            {
                return null;
            }

            foreach (var item in array)
            {
                if (item != null)
                {
                    if (item is string)
                    {
                        if (!string.IsNullOrWhiteSpace((string)item))
                        {
                            modifiedArray.Add(item);
                        }
                    }
                    else
                    {
                        modifiedArray.Add(item);
                    }
                }
            }

            return modifiedArray;
        }

        public static object Union(IEnumerable array, IEnumerable secondArray)
        {
            var modifiedArray = new List<object>();

            if (array == null || !array.Cast<object>().Any())
            {
                return secondArray;
            }

            if (secondArray == null || !secondArray.Cast<object>().Any())
            {
                return null;
            }

            foreach (var item in array)
            {
                if (item != null && !modifiedArray.Contains(item))
                {
                    modifiedArray.Add(item);
                }
            }

            foreach (var item in secondArray)
            {
                if (item != null && !modifiedArray.Contains(item))
                {
                    modifiedArray.Add(item);
                }
            }

            return modifiedArray;
        }

        public static object Filter(IEnumerable array, string criteria)
        {
            if (array == null)
                return null;
            var filteredArray = array.Cast<Hash>().AsQueryable<Hash>().Where(criteria).ToList();
            return filteredArray;
        }

        public static object Flatten(IEnumerable array, string nestedList)
        {
            if (array == null || string.IsNullOrEmpty(nestedList))
            {
                return null;
            }

            var tokens = nestedList.Split('.');
            var flattenedArray = array;

            foreach (var token in tokens)
            {
                flattenedArray = FlattenInternal(flattenedArray, token);
            }

            return flattenedArray;
        }

        public static object Distinct(IEnumerable array)
        {
            if (array == null)
            {
                return null;
            }

            var distinctHashes = array.OfType<Hash>().Distinct(new HashEqualityComparer());

            if (distinctHashes.Count() == 0)
            {
                return array.OfType<string>().Distinct();
            }

            return distinctHashes;
        }

        public static IEnumerable OrderBy(IEnumerable input, string property)
        {
            List<object> ary = new List<object>();

            ary.AddRange((IEnumerable<object>)input);

            if (!ary.Any())
                return ary;

            if (string.IsNullOrEmpty(property))
                ary.Sort();
            else
            {
                ary.Sort((a, b) => Comparer.Default.Compare(((IDictionary)a)[property], ((IDictionary)b)[property]));
            }

            return ary;
        }

        public static IEnumerable GroupBy(IEnumerable input, string property)
        {
            List<Hash> result = new List<Hash>();

            if (string.IsNullOrEmpty(property) || input == null)
            {
                return input;
            }
            else
            {

                foreach(var group in input.OfType<Hash>().GroupBy(r => r[property]))
                {
                    var item = new Hash();
                    item["Key"] = group.Key.ToString();
                    item["Items"] = new List<object>(group);
                    result.Add(item);
                }
            }

            return result;
        }


        private static IEnumerable FlattenInternal(IEnumerable array, string nestedList)
        {
            if (array == null || string.IsNullOrEmpty(nestedList))
            {
                return null;
            }

            var flattenedArray = new List<object>();

            foreach (var item in array.Cast<Hash>())
            {
                if (item.ContainsKey(nestedList))
                {
                    var nestedObject = item[nestedList];

                    if (nestedObject is Hash)
                    {
                        flattenedArray.Add(nestedObject);
                    }
                    else
                    {
                        foreach (var nestedItem in (IEnumerable)nestedObject)
                        {
                            flattenedArray.Add(nestedItem);
                        }
                    }
                }
            }

            return flattenedArray;
        }
    }
}
