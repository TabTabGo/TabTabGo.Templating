using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json.Linq;
using Xunit;
using Engine = TabTabGo.Templating.OpenXml.SpreadSheet.TemplatingEngine;

namespace TabTabGo.Templating.OpenXml.Tests.SpreadSheet;

/// <summary>M4 (8.0.4): the data-supplied stylesheet path of the JSON workbook mode is confined to the templates folder.</summary>
public sealed class StylesheetPathTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-style-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    private const string StyleXml =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/></font></fonts></styleSheet>";

    public StylesheetPathTests()
    {
        _root = Path.Combine(_base, "templates");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "styles.xml"), StyleXml);
        File.WriteAllText(Path.Combine(_base, "outside.xml"), StyleXml);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private JObject Workbook(string templatePath) => JObject.Parse("""
        {
          "Workbook": {
            "Stylesheets": { "TemplatePath": "__PATH__" },
            "Sheets": [ { "SheetId": 1, "Name": "Report" } ],
            "Worksheet": { "Columns": [ { "Min": 1, "Max": 1, "BestFit": true } ], "SheetData": { "Rows": [] } }
          },
          "StyleMap": []
        }
        """.Replace("__PATH__", templatePath.Replace("\\", "\\\\")));

    private Engine EngineWithRoot() => new(new TemplatingOptions { TemplatesPath = _root });

    [Fact]
    public void Stylesheet_ValidRelativePath_Loads()
    {
        using var result = Assert.IsType<MemoryStream>(EngineWithRoot().Render("", Workbook("styles.xml")));

        using var document = SpreadsheetDocument.Open(result, false);
        Assert.Single(document.WorkbookPart!.WorkbookStylesPart!.Stylesheet.Descendants<Font>());
    }

    [Fact]
    public void Stylesheet_AbsolutePath_Throws()
    {
        var absolute = Path.Combine(_base, "outside.xml");

        var error = Assert.ThrowsAny<ArgumentException>(() => EngineWithRoot().Render("", Workbook(absolute)));

        Assert.DoesNotContain(_base, error.Message);
    }

    [Theory]
    [InlineData("../outside.xml")]
    [InlineData("sub/../../outside.xml")]
    [InlineData("\\\\server\\share\\styles.xml")]
    [InlineData("//server/share/styles.xml")]
    public void Stylesheet_TraversalAndUncPaths_Throw(string path)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => EngineWithRoot().Render("", Workbook(path)));

        Assert.DoesNotContain(_base, error.Message);
    }

    [Fact]
    public void Stylesheet_SiblingFolderSharingTheRootPrefix_Throws()
    {
        Directory.CreateDirectory(_root + "evil");
        File.WriteAllText(Path.Combine(_root + "evil", "styles.xml"), StyleXml);

        Assert.ThrowsAny<ArgumentException>(() => EngineWithRoot().Render("", Workbook("../templatesevil/styles.xml")));
    }

    [Fact]
    public void Stylesheet_WithDoctype_IsRejected()
    {
        File.WriteAllText(Path.Combine(_root, "dtd.xml"), "<!DOCTYPE s [<!ENTITY x \"y\">]>" + StyleXml);

        Assert.ThrowsAny<System.Xml.XmlException>(() => EngineWithRoot().Render("", Workbook("dtd.xml")));
    }

    [Fact]
    public void Stylesheet_MissingFile_ThrowsWithoutAbsolutePath()
    {
        var error = Assert.ThrowsAny<Exception>(() => EngineWithRoot().Render("", Workbook("nope.xml")));

        Assert.DoesNotContain(_base, error.Message);
    }
}
