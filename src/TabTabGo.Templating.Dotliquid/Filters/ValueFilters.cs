using System.IO;
using System.Xml.XPath;
using DotLiquid;
using Microsoft.Extensions.Configuration;


namespace TabTabGo.Templating.Liquid.Filters
{
    public static partial class ValueFilters
    {
        public static string AppSetting(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }
            // Only values the host put into TemplatingOptions.AppSettings are visible to templates (S-1): no file I/O,
            // no file watcher, no access to connection strings or secrets.
            return RenderScope.CurrentOptions.AppSettings.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }

        public static string FullPath(string input)
        {
            var root = Path.GetFullPath(".");

            var fileSystem = (object)RenderScope.Current?.FileSystem ?? Template.FileSystem;
            var rootProperty = fileSystem.GetType().GetProperty("Root");
            if (rootProperty != null)
            {
                root = (string)rootProperty.GetValue(fileSystem);
            }
  

            if (string.IsNullOrEmpty(input))
            {
                return root;
            }

            var rootedPath = Path.Combine(root, input);
            return Path.GetFullPath(rootedPath);
        }

        public static bool Contains(string input, string value)
        {
            return string.IsNullOrWhiteSpace(input) ? false : input.Contains(value);
        }

        public static string ToString(object input)
        {
            if (input == null)
            {
                return null;
            }

            return input.ToString();
        }

        public static object Select(Hash input, string property)
        {
            if (string.IsNullOrEmpty(property))
            {
                return null;
            }

            return input == null || !input.ContainsKey(property) ? null : input[property];
        }

        public static string XPathSelect(string xmlInput, string xPath)
        {
            if (string.IsNullOrEmpty(xmlInput))
            {
                return xmlInput;
            }

            XPathNavigator nav = SafeXml.Parse(xmlInput).CreateNavigator();

            var node = nav.SelectSingleNode(xPath);

            return node == null ? null : node.Value;
        }
    }
}
