namespace TabTabGo.Templating.OpenXml.SpreadSheet.Internal;

/// <summary>Resolves symbolic links so a path check compares real locations, not the names that lead to them.</summary>
internal static class RealPath
{
    private const int MaxLinkDepth = 32;

    /// <summary>Whether <paramref name="path"/> resolves, through every link, to a location inside <paramref name="root"/>.</summary>
    /// <param name="root">An absolute folder path.</param>
    /// <param name="path">An absolute file path.</param>
    public static bool IsInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var realRoot = Resolve(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
        return Resolve(path).StartsWith(realRoot, comparison);
    }

    private static string Resolve(string absolutePath, int depth = 0)
    {
        if (depth > MaxLinkDepth)
        {
            throw new ArgumentException("The stylesheet path contains too many links.", "requested");
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
                current = Resolve(Path.GetFullPath(target.FullName), depth + 1);
            }
        }

        return current;
    }
}
