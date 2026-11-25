using System;
using System.IO;
using System.Linq;
using TabTabGo.Templating.Internal;
using Xunit;

namespace TabTabGo.Templating.Tests;

/// <summary>The shared link resolution, tested directly (the loaders also reach it through a File.Exists pre-check that hides most of these cases). Tests that cannot run are skipped visibly.</summary>
public sealed class RealPathTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ttg-realpath-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public RealPathTests()
    {
        _root = Path.Combine(_base, "root");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "real.txt"), "x");
        File.WriteAllText(Path.Combine(_base, "outside.txt"), "x");
    }

    public void Dispose()
    {
        foreach (var directory in Directory.EnumerateDirectories(_base, "*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_base, recursive: true);
    }

    private static void Link(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("this platform or account cannot create symbolic links");
        }
    }

    private static void AssertPathFree(Exception ex, params string[] forbidden)
    {
        Assert.IsType<IOException>(ex);
        Assert.Null(ex.InnerException);
        var text = ex.Message + "\n" + string.Join("\n", ex.ToString().Split('\n').Where(l => !l.TrimStart().StartsWith("at ", StringComparison.Ordinal)));
        foreach (var part in forbidden)
        {
            Assert.DoesNotContain(part, text);
        }
    }

    [Fact]
    public void ALinkLoop_IsAPathFreeIOException()
    {
        Link(Path.Combine(_root, "a"), "b");
        Link(Path.Combine(_root, "b"), "a");

        var ex = Assert.ThrowsAny<Exception>(() => RealPath.ResolveInside(_root, Path.Combine(_root, "a")));

        AssertPathFree(ex, _root, _base, Path.GetFileName(_base));
    }

    [Fact]
    public void AChainOfMoreThan32Links_IsAPathFreeIOException_AndTenHopsResolve()
    {
        for (var i = 0; i < 40; i++)
        {
            Link(Path.Combine(_root, "hop" + i), i == 39 ? "real.txt" : "hop" + (i + 1));
        }

        var ex = Assert.ThrowsAny<Exception>(() => RealPath.ResolveInside(_root, Path.Combine(_root, "hop0")));
        AssertPathFree(ex, _root, _base);

        var resolved = RealPath.ResolveInside(_root, Path.Combine(_root, "hop30"));
        Assert.EndsWith("real.txt", resolved);
    }

    [Fact]
    public void ALinkOutOfTheRoot_IsNull_AndALinkInsideItResolves()
    {
        Link(Path.Combine(_root, "out"), Path.Combine("..", "outside.txt"));
        Link(Path.Combine(_root, "in"), "real.txt");

        Assert.Null(RealPath.ResolveInside(_root, Path.Combine(_root, "out")));
        Assert.EndsWith("real.txt", RealPath.ResolveInside(_root, Path.Combine(_root, "in")));
        Assert.True(RealPath.IsInside(_root, Path.Combine(_root, "in")));
        Assert.False(RealPath.IsInside(_root, Path.Combine(_root, "out")));
    }

    [Fact]
    public void ARelativeRootAndPath_AreMadeAbsoluteFirst()
    {
        var relativeRoot = Path.GetRelativePath(Environment.CurrentDirectory, _root);

        var resolved = RealPath.ResolveInside(relativeRoot, Path.Combine(relativeRoot, "real.txt"));

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith("real.txt", resolved);
        Assert.Null(RealPath.ResolveInside(relativeRoot, Path.Combine(relativeRoot, "..", "outside.txt")));
    }

    [Fact]
    public void AnUnreadableDirectoryInTheChain_NeverLeaksAPath()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permissions only");
            return;
        }

        if (Environment.UserName == "root")
        {
            Assert.Skip("the root user ignores directory permissions");
        }

        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "inner.txt"), "x");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        var failure = Record.Exception(() => RealPath.ResolveInside(_root, Path.Combine(locked, "inner.txt")));

        if (failure != null)
        {
            AssertPathFree(failure, _root, _base);
        }
    }
}
