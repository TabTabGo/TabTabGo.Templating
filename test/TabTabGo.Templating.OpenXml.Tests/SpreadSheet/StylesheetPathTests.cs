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

    private Engine EngineWithRoot() => new(new TemplatingOptions { StylesheetRoot = _root });

    [Fact]
    public void Stylesheet_ValidRelativePath_Loads()
    {
        using var result = Assert.IsType<MemoryStream>(EngineWithRoot().Render("", Workbook("styles.xml")));

        using var document = SpreadsheetDocument.Open(result, false);
        Assert.Single(document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.Descendants<Font>());
    }

    [Fact]
    public void Stylesheet_TemplatesPathIsNotTheStylesheetRoot()
    {
        var engine = new Engine(new TemplatingOptions { TemplatesPath = _root, StylesheetRoot = Path.Combine(_base, "other") });

        Assert.ThrowsAny<Exception>(() => engine.Render("", Workbook("styles.xml")));
    }

    [Fact]
    public void Stylesheet_RelativeStylesheetRoot_IsResolvedAgainstTheBaseDirectory()
    {
        var folder = "ttg-rel-" + Guid.NewGuid().ToString("N");
        var absolute = Path.Combine(AppContext.BaseDirectory, folder);
        Directory.CreateDirectory(absolute);
        File.WriteAllText(Path.Combine(absolute, "styles.xml"), StyleXml);
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_base); // a current-directory resolution would not find the file
            using var result = Assert.IsType<MemoryStream>(new Engine(new TemplatingOptions { StylesheetRoot = folder }).Render("", Workbook("styles.xml")));
            Assert.True(result.Length > 0);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
            Directory.Delete(absolute, recursive: true);
        }
    }

    [Fact]
    public void Constructors_NegativeMaxXmlCharacters_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Engine(new TemplatingOptions { MaxXmlCharacters = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TabTabGo.Templating.OpenXml.SpreadSheet.JsonSpreedSheet(new TemplatingOptions { MaxXmlCharacters = -1 }));
    }

    [Fact]
    public void Stylesheet_LargerThanMaxXmlCharacters_Throws()
    {
        // S-7: the stylesheet is read with the size cap; the same file loads when the cap allows it
        var tooSmall = new Engine(new TemplatingOptions { StylesheetRoot = _root, MaxXmlCharacters = 50 });
        Assert.Throws<ArgumentException>(() => tooSmall.Render("", Workbook("styles.xml"))); // the XmlException is replaced by a path-free ArgumentException

        var roomy = new Engine(new TemplatingOptions { StylesheetRoot = _root, MaxXmlCharacters = 100_000 });
        using var result = Assert.IsType<MemoryStream>(roomy.Render("", Workbook("styles.xml")));
        Assert.True(result.Length > 0);
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

        Assert.Throws<ArgumentException>(() => EngineWithRoot().Render("", Workbook("dtd.xml")));
    }

    [Fact]
    public void Stylesheet_WithDtd_Rejected()
    {
        // well-formed, DOCTYPE name equals the root element and the entity is used: only DtdProcessing.Prohibit stops it
        File.WriteAllText(
            Path.Combine(_root, "entity.xml"),
            "<!DOCTYPE styleSheet [<!ENTITY size \"11\">]><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<fonts count=\"1\"><font><sz val=\"&size;\"/></font></fonts></styleSheet>");

        var error = Assert.Throws<ArgumentException>(() => EngineWithRoot().Render("", Workbook("entity.xml")));

        // The Open XML SDK also refuses DTDs later (same message), so also require that OUR loader is the one that rejected it;
        // otherwise changing DtdProcessing.Prohibit to Parse would go unnoticed.
        Assert.Contains("LoadStylesheet", error.StackTrace);
    }

    /// <summary>
    /// The pre-check (rooted / UNC) must reject these itself: without it an absolute path inside the root would load, and the
    /// UNC spellings would only be caught later by the root or file checks, with a different message.
    /// </summary>
    [Fact]
    public void Stylesheet_AbsolutePathInsideTheRoot_RejectedByThePreCheck()
    {
        Directory.CreateDirectory(Path.Combine(_root, "styles"));
        File.WriteAllText(Path.Combine(_root, "styles", "ok.xml"), StyleXml);
        var absoluteInside = Path.Combine(_root, "styles", "ok.xml");

        var error = Assert.Throws<ArgumentException>(() => EngineWithRoot().Render("", Workbook(absoluteInside)));

        Assert.Contains("must be relative", error.Message);
    }

    [Theory]
    [InlineData("\\\\server\\share\\x.xml")]
    [InlineData("//server/share/x.xml")]
    public void Stylesheet_UncPaths_RejectedByThePreCheck(string path)
    {
        var error = Assert.Throws<ArgumentException>(() => EngineWithRoot().Render("", Workbook(path)));

        Assert.Contains("must be relative", error.Message);
    }

    [Fact]
    public void Stylesheet_MissingFile_ThrowsWithoutAbsolutePath()
    {
        var error = Assert.ThrowsAny<Exception>(() => EngineWithRoot().Render("", Workbook("nope.xml")));

        Assert.DoesNotContain(_base, error.Message);
    }
}
