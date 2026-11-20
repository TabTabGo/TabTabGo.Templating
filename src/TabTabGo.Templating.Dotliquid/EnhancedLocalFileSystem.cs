using System.Text.RegularExpressions;
using DotLiquid;
using DotLiquid.Exceptions;
using DotLiquid.FileSystems;

namespace TabTabGo.Templating.Liquid;

/// <summary>
/// DotLiquid file system for <c>include</c> and <c>extends</c>: partials are <c>_name.liquid</c> files that must stay inside
/// <see cref="Root"/> (ADR 0003 section 2). Names that are rooted, climb out of the root, or reach a file outside it through a
/// symbolic link are rejected; exception messages never contain server paths.
/// </summary>
public class EnhancedLocalFileSystem : IFileSystem
{
    private static readonly Regex LegalName = new(@"^[.\/]*[a-zA-Z0-9_\/]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Creates a file system rooted at <paramref name="root"/> (empty means the current directory).</summary>
    /// <param name="root">The folder the partials live in.</param>
    public EnhancedLocalFileSystem(string root)
    {
        Root = root;
    }

    /// <summary>The folder the partials live in.</summary>
    public string Root { get; set; }

    /// <inheritdoc />
    public string ReadTemplateFile(Context context, string templateName)
    {
        // The name may be a literal ('part') or a variable; a variable that is not a string must not be coerced (S-3).
        if (context[templateName] is not string templatePath)
        {
            throw new FileSystemException("Illegal template name", templateName);
        }

        var fullPath = FullPath(templatePath);
        if (!File.Exists(fullPath))
        {
            throw new FileSystemException("Template not found", templatePath);
        }

        if (!RealPathIsInsideRoot(fullPath))
        {
            throw new FileSystemException("Illegal template path", templatePath);
        }

        return File.ReadAllText(fullPath);
    }

    /// <summary>Resolves a partial name (<c>part</c> or <c>folder/part</c>) to its <c>_part.liquid</c> path inside the root.</summary>
    /// <param name="templatePath">The partial name as written in the template.</param>
    /// <exception cref="FileSystemException">The name is not legal or points outside the root.</exception>
    public string FullPath(string? templatePath)
    {
        if (templatePath == null || !LegalName.IsMatch(templatePath) || Path.IsPathRooted(templatePath))
        {
            throw new FileSystemException("Illegal template name", templatePath ?? string.Empty);
        }

        var fullPath = templatePath.Contains('/')
            ? Path.Combine(Path.Combine(Root ?? string.Empty, Path.GetDirectoryName(templatePath) ?? string.Empty), $"_{Path.GetFileName(templatePath)}.liquid")
            : Path.Combine(Root ?? string.Empty, $"_{templatePath}.liquid");

        // Compare normalised paths with a trailing separator on the root: a plain prefix test accepts "/rootEvil" for "/root",
        // and an unescaped root is a regular expression (S-3). An empty root (a template given as a bare file name) is the current directory.
        var normalizedRoot = NormalizedRoot();
        var normalizedPath = Path.GetFullPath(fullPath);
        if (!normalizedPath.StartsWith(normalizedRoot, PathComparison))
        {
            throw new FileSystemException("Illegal template path", templatePath);
        }

        return fullPath;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string NormalizedRoot() =>
        Path.GetFullPath(string.IsNullOrEmpty(Root) ? "." : Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <summary>A symbolic link inside the root may point outside it (S-11): resolve every link and compare the real paths.</summary>
    private bool RealPathIsInsideRoot(string fullPath)
    {
        var root = ResolveLinks(NormalizedRoot().TrimEnd(Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar;
        return ResolveLinks(Path.GetFullPath(fullPath)).StartsWith(root, PathComparison);
    }

    private static string ResolveLinks(string absolutePath, int depth = 0)
    {
        if (depth > 32)
        {
            throw new FileSystemException("Illegal template path", "link depth");
        }

        var rootOfPath = Path.GetPathRoot(absolutePath) ?? string.Empty;
        var current = rootOfPath;
        var segments = absolutePath[rootOfPath.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = ResolveLinks(Path.GetFullPath(target.FullName), depth + 1);
            }
        }

        return current;
    }
}
