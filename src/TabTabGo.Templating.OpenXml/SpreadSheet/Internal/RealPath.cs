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

    /// <summary>
    /// The physical location of <paramref name="absolutePath"/>, resolved the way the operating system does it: one link at a time, each relative target
    /// applied to the directory the link really sits in, <c>..</c> applied to the real parent. (<c>ResolveLinkTarget(returnFinalTarget: true)</c> follows a chain of
    /// relative links lexically, which disagrees with the OS as soon as a link points through another link.)
    /// </summary>
    private static string Resolve(string absolutePath)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var volume = Path.GetPathRoot(absolutePath) ?? string.Empty;
        var resolved = new List<string>();
        var pending = new Stack<string>(absolutePath[volume.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries).Reverse());
        var hops = 0;
        while (pending.Count > 0)
        {
            var segment = pending.Pop();
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            resolved.Add(segment);
            var current = Path.Combine(volume, Path.Combine(resolved.ToArray()));
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not { } target)
            {
                continue;
            }

            if (++hops > MaxLinkDepth)
            {
                throw new ArgumentException("The stylesheet path contains too many links.", "requested");
            }

            resolved.RemoveAt(resolved.Count - 1); // the link is replaced by its target, read from the directory it sits in
            if (Path.IsPathRooted(target))
            {
                volume = Path.GetPathRoot(target) ?? string.Empty;
                resolved.Clear();
                target = target[volume.Length..];
            }

            foreach (var part in target.Split(separators, StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                pending.Push(part);
            }
        }

        return resolved.Count == 0 ? volume : Path.Combine(volume, Path.Combine(resolved.ToArray()));
    }
}
