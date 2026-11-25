namespace TabTabGo.Templating.Internal;

/// <summary>
/// Resolves symbolic links so a path check compares real locations, not the names that lead to them. One source for the include loader and the stylesheet loader (this package
/// shares its internals with both). Every failure is an <see cref="IOException"/> whose message carries no path and which has no inner exception: the system's own messages name the
/// absolute path of the directory or link that could not be read.
/// </summary>
internal static class RealPath
{
    private const int MaxLinkDepth = 32;

    /// <summary>Whether <paramref name="path"/> resolves, through every link, to a location inside <paramref name="root"/>.</summary>
    /// <param name="root">An absolute folder path.</param>
    /// <param name="path">An absolute file path.</param>
    /// <exception cref="IOException">A link chain is too long, loops, or cannot be read (no path in the message).</exception>
    public static bool IsInside(string root, string path) => ResolveInside(root, path) != null;

    /// <summary>The real path of <paramref name="path"/> when it lies inside <paramref name="root"/>, otherwise null. Callers open this resolved path, not the one they were given.</summary>
    /// <param name="root">An absolute folder path.</param>
    /// <param name="path">An absolute file path.</param>
    /// <exception cref="IOException">A link chain is too long, loops, or cannot be read (no path in the message).</exception>
    public static string? ResolveInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var realRoot = Resolve(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
        var real = Resolve(path);
        return real.StartsWith(realRoot, comparison) ? real : null;
    }

    /// <summary>
    /// The physical location of <paramref name="absolutePath"/>, resolved the way the operating system does it: one link at a time, each relative target
    /// applied to the directory the link really sits in, <c>..</c> applied to the real parent. (<c>ResolveLinkTarget(returnFinalTarget: true)</c> follows a chain of
    /// relative links lexically, which disagrees with the OS as soon as a link points through another link.)
    /// </summary>
    internal static string Resolve(string absolutePath)
    {
        try
        {
            return ResolveCore(Path.GetFullPath(absolutePath)); // a relative path (a relative root such as "Templates", "./tpl" or "") is made absolute first, like the check it replaced
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new IOException("The path could not be resolved."); // not the system exception: its message names the absolute path
        }
    }

    private static string ResolveCore(string absolutePath)
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
                throw new IOException("The path contains too many links.");
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
