using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.TestKit;

/// <summary>
/// Renders a golden case folder and compares it to <c>expected.txt</c>. A case folder holds <c>template.liquid</c>, optional
/// <c>data.json</c>, optional <c>culture.txt</c> (default <c>en-US</c>) and <c>expected.txt</c>. Set <c>UPDATE_GOLDEN=1</c> to rewrite the
/// expected files; the reviewer checks the diff.
/// </summary>
public static class LiquidGolden
{
    /// <summary>Names of the case folders directly below <paramref name="root"/>.</summary>
    public static IEnumerable<string> Cases(string root) =>
        Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Select(d => System.IO.Path.GetFileName(d))
                .OrderBy(n => n, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>Renders the case and compares (or rewrites) its golden output.</summary>
    /// <param name="caseDirectory">The case folder.</param>
    public static void Assert(string caseDirectory)
    {
        var template = System.IO.Path.Combine(caseDirectory, "template.liquid");
        var dataPath = System.IO.Path.Combine(caseDirectory, "data.json");
        var culturePath = System.IO.Path.Combine(caseDirectory, "culture.txt");
        var culture = File.Exists(culturePath) ? File.ReadAllText(culturePath).Trim() : "en-US";
        var data = File.Exists(dataPath) ? JObject.Parse(File.ReadAllText(dataPath)) : new JObject();

        using var scope = new CultureScope("en-US"); // the machine culture never leaks into a golden
        var actual = Normalise((string)new Liquid.TemplatingEngine().Render(template, data, culture));

        var expectedPath = System.IO.Path.Combine(caseDirectory, "expected.txt");
        if (GoldenPaths.Updating)
        {
            File.WriteAllText(GoldenPaths.ToSource(expectedPath), actual);
            return;
        }

        if (!File.Exists(expectedPath))
        {
            throw new GoldenMismatchException($"Missing golden file {expectedPath}. Run with UPDATE_GOLDEN=1 once and review it.");
        }

        var expected = Normalise(File.ReadAllText(expectedPath));
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new GoldenMismatchException($"Golden mismatch for {caseDirectory}\n--- expected ---\n{expected}\n--- actual ---\n{actual}");
        }
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n");
}
