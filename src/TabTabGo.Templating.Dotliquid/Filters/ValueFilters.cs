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
            // Get Configuration from appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            return config[key];
        }

        public static string FullPath(string input)
        {
            var root = Path.GetFullPath(".");

            var rootProperty = Template.FileSystem.GetType().GetProperty("Root");
            if (rootProperty != null)
            {
                root = (string)rootProperty.GetValue(Template.FileSystem);
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

            XPathNavigator nav;
            XPathDocument docNav;
            var reader = new StringReader(xmlInput);
            docNav = new XPathDocument(reader);
            nav = docNav.CreateNavigator();

            var node = nav.SelectSingleNode(xPath);

            return node == null ? null : node.Value;
        }
    }
}
