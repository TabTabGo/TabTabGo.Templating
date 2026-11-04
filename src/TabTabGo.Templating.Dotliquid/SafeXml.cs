using System.IO;
using System.Xml;
using System.Xml.XPath;

namespace TabTabGo.Templating.Liquid
{
    /// <summary>Builds XPath documents from template-supplied XML without DTDs, external entities or unbounded size (S-5).</summary>
    internal static class SafeXml
    {
        public static XPathDocument Parse(string xml)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = RenderScope.CurrentOptions.MaxXmlCharacters,
            };

            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                return new XPathDocument(reader);
            }
        }
    }
}
