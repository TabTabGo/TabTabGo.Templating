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
            var scope = RenderScope.Current;
            var fileSystem = (object?)scope?.FileSystem ?? Template.FileSystem;
            var root = Path.GetFullPath(".");
            if (fileSystem?.GetType().GetProperty("Root")?.GetValue(fileSystem) is string { Length: > 0 } configured)
            {
                root = Path.GetFullPath(configured);
            }

            var absolute = string.IsNullOrEmpty(input) ? root : Path.GetFullPath(Path.Combine(root, input));
            if (RenderScope.CurrentOptions.AllowAbsolutePaths)
            {
                return absolute;
            }

            // S-6/S-12: without AllowAbsolutePaths the output never names the server folder the templates live in.
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var relative = Path.GetRelativePath(root, absolute);
            var endsWithSeparator = input.EndsWith('/') || input.EndsWith(Path.DirectorySeparatorChar);
            return endsWithSeparator && !relative.EndsWith(Path.DirectorySeparatorChar) ? relative + Path.DirectorySeparatorChar : relative;
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
