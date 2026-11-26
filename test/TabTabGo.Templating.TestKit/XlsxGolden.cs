using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace TabTabGo.Templating.TestKit;

/// <summary>
/// Compares a rendered workbook to a golden folder part by part (canonical XML: attributes sorted, insignificant whitespace removed,
/// document timestamps dropped) and then validates it with <see cref="OpenXmlValidator"/> (Office 2019): zero errors are allowed.
/// <c>UPDATE_GOLDEN=1</c> rewrites the folder.
/// </summary>
public static class XlsxGolden
{
    private static readonly System.Text.RegularExpressions.Regex RelationshipId = new("\\bR[0-9a-f]{16}\\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly string[] IgnoredParts = { "docProps/core.xml", "docProps/app.xml" };

    /// <summary>Compares <paramref name="xlsx"/> with <paramref name="goldenDirectory"/> and validates it.</summary>
    /// <param name="xlsx">The rendered workbook.</param>
    /// <param name="goldenDirectory">Folder holding one canonical file per package part.</param>
    /// <param name="validate">False only for fixtures that are known to be invalid on purpose.</param>
    public static void Assert(Stream xlsx, string goldenDirectory, bool validate = true)
    {
        var bytes = ReadAll(xlsx);
        var actual = Canonicalise(bytes).ToDictionary(p => FileName(p.Key), p => p.Value);

        if (GoldenPaths.Updating)
        {
            var target = GoldenPaths.ToSource(goldenDirectory);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(target);
            foreach (var (name, text) in actual)
            {
                File.WriteAllText(System.IO.Path.Combine(target, name), text, new UTF8Encoding(false));
            }
        }
        else
        {
            var expected = Directory.Exists(goldenDirectory)
                ? Directory.GetFiles(goldenDirectory).ToDictionary(f => System.IO.Path.GetFileName(f), f => File.ReadAllText(f).Replace("\r\n", "\n"))
                : new Dictionary<string, string>();

            var problems = new List<string>();
            problems.AddRange(expected.Keys.Except(actual.Keys).Select(n => $"missing part {n}"));
            problems.AddRange(actual.Keys.Except(expected.Keys).Select(n => $"unexpected part {n}"));
            problems.AddRange(expected.Keys.Intersect(actual.Keys).Where(n => expected[n] != actual[n])
                .Select(n => $"part {n} differs\n  expected: {expected[n]}\n  actual:   {actual[n]}"));

            if (problems.Count > 0)
            {
                throw new GoldenMismatchException($"Workbook golden mismatch for {goldenDirectory}\n" + string.Join("\n", problems));
            }
        }

        if (validate)
        {
            AssertValid(bytes);
        }
    }

    /// <summary>Fails when <see cref="OpenXmlValidator"/> (Office 2019) reports any error.</summary>
    /// <param name="workbook">The workbook package bytes.</param>
    public static void AssertValid(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(document).ToList();
        if (errors.Count > 0)
        {
            throw new GoldenMismatchException("The workbook is not valid:\n" + string.Join("\n", errors.Take(10).Select(e => $"{e.Path?.XPath}: {e.Description}")));
        }
    }

    /// <summary>Canonical XML of every part of the package (excluding the ignored ones), keyed by part name.</summary>
    /// <param name="workbook">The workbook package bytes.</param>
    public static IReadOnlyDictionary<string, string> Canonicalise(byte[] workbook)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            if (IgnoredParts.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            result[entry.FullName] = entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                    ? CanonicalXml(text)
                    : text;
        }

        // the SDK names relationships with random GUID-like ids; number them in order of first appearance so runs compare equal
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in result.Keys.ToList())
        {
            result[name] = RelationshipId.Replace(result[name], match =>
            {
                if (!ids.TryGetValue(match.Value, out var number))
                {
                    number = "rId" + (ids.Count + 1);
                    ids[match.Value] = number;
                }

                return number;
            });
        }

        return result;
    }

    private static string CanonicalXml(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        Sort(document.Root!);
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.None };
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, settings))
        {
            document.Root!.WriteTo(writer);
        }

        return builder.ToString();
    }

    private static void Sort(XElement element)
    {
        var attributes = element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToList();
        element.RemoveAttributes();
        element.Add(attributes);
        if (element.Elements().Any())
        {
            foreach (var node in element.Nodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList())
            {
                node.Remove();
            }
        }

        foreach (var child in element.Elements())
        {
            Sort(child);
        }
    }

    private static string FileName(string part) => part.Replace('/', '_') + ".txt";

    private static byte[] ReadAll(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
