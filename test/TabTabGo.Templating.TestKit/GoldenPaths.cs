namespace TabTabGo.Templating.TestKit;

/// <summary>Maps a golden path in the test output folder to the same path in the test project's source folder (used by <c>UPDATE_GOLDEN=1</c>).</summary>
public static class GoldenPaths
{
    /// <summary>True when the run should rewrite goldens instead of comparing.</summary>
    public static bool Updating => Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1";

    /// <summary>The source-folder twin of <paramref name="outputPath"/> (a path below the test output directory).</summary>
    /// <param name="outputPath">A path under <see cref="AppContext.BaseDirectory"/>.</param>
    public static string ToSource(string outputPath)
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var relative = Path.GetRelativePath(baseDirectory, outputPath);
        var directory = new DirectoryInfo(baseDirectory);
        while (directory != null && directory.GetFiles("*.csproj").Length == 0)
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("The test project folder was not found above " + baseDirectory);
        }

        return Path.Combine(directory.FullName, relative);
    }
}
