using System;
using System.IO;
using System.Linq;
using DotLiquid;
using TabTabGo.Templating.Liquid.Internal;
using DotLiquid.Exceptions;
using TabTabGo.Templating.Liquid;
using Xunit;

namespace TabTabGo.Templating.DotLiquid.Tests;

public sealed class EnhancedLocalFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ttg-fs-" + Guid.NewGuid().ToString("N"));

    public EnhancedLocalFileSystemTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "_part.liquid"), "root-part");
        File.WriteAllText(Path.Combine(_root, "sub", "_inner.liquid"), "inner-part");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Constructor_SetsRoot() => Assert.Equal(_root, new EnhancedLocalFileSystem(_root).Root);

    [Fact]
    public void FullPath_PlainName_UsesUnderscorePrefixInRoot()
    {
        var fs = new EnhancedLocalFileSystem(_root);
        Assert.Equal(Path.Combine(_root, "_part.liquid"), fs.FullPath("part"));
    }

    [Fact]
    public void FullPath_SubFolderName_ResolvesInsideRoot()
    {
        var fs = new EnhancedLocalFileSystem(_root);
        Assert.Equal(Path.Combine(_root, "sub", "_inner.liquid"), fs.FullPath("sub/inner"));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a;b")]
    [InlineData("")]
    [InlineData("part\n")]
    [InlineData("sub/inner\n")]
    [InlineData("part\r\n")]
    public void FullPath_IllegalName_Throws(string name)
    {
        Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).FullPath(name));
    }

    [Fact]
    public void FullPath_Null_Throws()
    {
        Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).FullPath(null!));
    }

    [Fact]
    public void ReadTemplateFile_ReadsPartial()
    {
        var fs = new EnhancedLocalFileSystem(_root);
        var context = new Context(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("root-part", fs.ReadTemplateFile(context, "'part'"));
        Assert.Equal("inner-part", fs.ReadTemplateFile(context, "'sub/inner'"));
    }

    [Fact]
    public void ReadTemplateFile_Missing_Throws()
    {
        var fs = new EnhancedLocalFileSystem(_root);
        Assert.Throws<FileSystemException>(() => fs.ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'nothere'"));
    }

    private static bool TryLink(string link, string target, bool directory)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            if (directory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public void ReadTemplateFile_RelativeLinkChainThroughALinkBackToTheRoot_IsRefused()
    {
        // _root/a/b/c/sub -> _root, _root/_l2.liquid -> ../secret.liquid, _root/_l1.liquid -> a/b/c/sub/_l2.liquid: lexically inside, physically outside
        var parent = Path.GetDirectoryName(_root)!;
        var secret = Path.Combine(parent, "secret-" + Guid.NewGuid().ToString("N") + ".liquid");
        File.WriteAllText(secret, "SECRET");
        try
        {
            Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c"));
            if (!TryLink(Path.Combine(_root, "a", "b", "c", "sub"), _root, directory: true)
                || !TryLink(Path.Combine(_root, "_l2.liquid"), Path.Combine("..", Path.GetFileName(secret)), directory: false)
                || !TryLink(Path.Combine(_root, "_l1.liquid"), Path.Combine("a", "b", "c", "sub", "_l2.liquid"), directory: false))
            {
                return;
            }

            var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'l1'"));
            Assert.DoesNotContain("SECRET", ex.Message);
        }
        finally
        {
            File.Delete(secret);
        }
    }

    [Fact]
    public void ReadTemplateFile_DanglingLinkInsideTheRoot_ThrowsWithoutAServerPath()
    {
        if (!TryLink(Path.Combine(_root, "_dangling.liquid"), "nonexistent.liquid", directory: false))
        {
            return;
        }

        var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'dangling'"));

        Assert.DoesNotContain(_root, ex.Message);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), ex.Message);
    }

    [Fact]
    public void ReadTemplateFile_UnreadableFile_ThrowsWithoutAServerPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_root, "_locked.liquid");
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (Environment.UserName == "root")
            {
                return; // root reads everything
            }

            var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'locked'"));
            Assert.DoesNotContain(_root, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void ReadTemplateFile_LinksThatStayInsideTheRoot_AreStillRead()
    {
        if (!TryLink(Path.Combine(_root, "sub", "_alias.liquid"), Path.Combine("..", "_part.liquid"), directory: false))
        {
            return;
        }

        Assert.Equal("root-part", new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'sub/alias'"));
    }

    [Fact]
    public void ReadTemplateFile_AFifoInTheRoot_IsRefusedWithoutBlocking_AndWithoutAServerPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fifo = Path.Combine(_root, "_pipe.liquid");
        using (var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", fifo) { RedirectStandardError = true }))
        {
            make!.WaitForExit();
            if (make.ExitCode != 0)
            {
                return; // no mkfifo on this machine
            }
        }

        var previous = TabTabGo.Templating.Liquid.Internal.GuardedRead.OpenTimeout;
        TabTabGo.Templating.Liquid.Internal.GuardedRead.OpenTimeout = TimeSpan.FromMilliseconds(1000);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'pipe'"));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
            Assert.DoesNotContain(_root, ex.ToString().Split('\n').Where(l => !l.TrimStart().StartsWith("at ", StringComparison.Ordinal)).Aggregate(string.Empty, (a, b) => a + b));
        }
        finally
        {
            TabTabGo.Templating.Liquid.Internal.GuardedRead.OpenTimeout = previous;
        }
    }

    [Fact]
    public void ReadTemplateFile_AnEmptyRegularPartial_IsStillRead()
    {
        File.WriteAllText(Path.Combine(_root, "_empty.liquid"), string.Empty);

        Assert.Equal(string.Empty, new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'empty'"));
    }

    [Fact]
    public void FullPath_IllegalPath_ExceptionNamesNoRoot()
    {
        var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).FullPath("../../../etc/passwd"));

        Assert.DoesNotContain(_root, ex.Message);
    }

    private static bool TryMakeFifo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        using var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", path) { RedirectStandardError = true });
        make!.WaitForExit();
        return make.ExitCode == 0;
    }

    private static readonly Func<EnhancedLocalFileSystem, string, string> Read = (fs, name) => fs.ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'" + name + "'");

    [Fact]
    public void G2_TwentyConcurrentReadsOfAFifo_AreAllPathFree_WithinTheTimeout_AndLeaveNoThreadsBehind()
    {
        if (!TryMakeFifo(Path.Combine(_root, "_pipe2.liquid")))
        {
            return;
        }

        var fs = new EnhancedLocalFileSystem(_root);
        var previous = GuardedRead.OpenTimeout;
        GuardedRead.OpenTimeout = TimeSpan.FromMilliseconds(1000);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var failures = new Exception[20];

            System.Threading.Tasks.Parallel.For(0, 20, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 20 }, i => failures[i] = Record.Exception(() => Read(fs, "pipe2"))!);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
            Assert.All(failures, ex =>
            {
                Assert.IsType<FileSystemException>(ex);
                Assert.DoesNotContain(_root, ex.Message);
            });
        }
        finally
        {
            GuardedRead.OpenTimeout = previous;
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((GuardedRead.StuckReaders > 0 || GuardedRead.LiveWorkers > 0) && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(50);
        }

        Assert.Equal(0, GuardedRead.StuckReaders);
        Assert.Equal(0, GuardedRead.LiveWorkers); // every worker thread has ended: none leaked per request
    }

    [Fact]
    public void G1_ARegularPartialSwappedForAFifoBetweenTheDecisionAndTheOpen_IsStillRefused()
    {
        File.WriteAllText(Path.Combine(_root, "_swap.liquid"), "regular");
        if (!TryMakeFifo(Path.Combine(_root, "swap.fifo")))
        {
            return;
        }

        var previous = GuardedRead.OpenTimeout;
        GuardedRead.OpenTimeout = TimeSpan.FromMilliseconds(1000);
        GuardedRead.BeforeOpen = p =>
        {
            if (!p.EndsWith("swap.xml", StringComparison.Ordinal) && !p.EndsWith("_swap.liquid", StringComparison.Ordinal))
            {
                return; // other tests may read files while this hook is set
            }

            File.Delete(p);
            File.Move(Path.Combine(_root, "swap.fifo"), p);
        };
        try
        {
            var ex = Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "swap"));

            Assert.DoesNotContain(_root, ex.Message);
        }
        finally
        {
            GuardedRead.BeforeOpen = null;
            GuardedRead.OpenTimeout = previous;
        }
    }

    [Fact]
    public void G1_RegularPartials_StillReadCorrectlyAndFast()
    {
        var fs = new EnhancedLocalFileSystem(_root);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 200; i++)
        {
            Assert.Equal("root-part", Read(fs, "part"));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }
}
