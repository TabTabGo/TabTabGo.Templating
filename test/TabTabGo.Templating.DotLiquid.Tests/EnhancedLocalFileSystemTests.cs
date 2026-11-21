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

    // ---- round 6 addendum: GR-1 capped read, GR-2/3 no write-open, no thread, no release

    private sealed class EndlessStream(long reportedLength, long actualBytes) : Stream
    {
        private long _served;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => reportedLength;

        public override long Position
        {
            get => _served;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, actualBytes - _served);
            Array.Fill(buffer, (byte)'a', offset, n);
            _served += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Gr1_AFileThatGrowsAfterTheLengthWasTaken_IsRefusedAtTheCap()
    {
        GuardedRead.OpenOverride = _ => new EndlessStream(reportedLength: 10, actualBytes: GuardedRead.MaxBytes * 2);
        try
        {
            var ex = Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));

            Assert.DoesNotContain("ignored", ex.Message);
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr1_ExactlyTheCap_ReadsAndOneByteMore_IsRefused()
    {
        try
        {
            GuardedRead.OpenOverride = _ => new EndlessStream(GuardedRead.MaxBytes, GuardedRead.MaxBytes);
            Assert.Equal(GuardedRead.MaxBytes, GuardedRead.ReadText("ignored").Length);

            GuardedRead.OpenOverride = _ => new EndlessStream(10, GuardedRead.MaxBytes + 1);
            Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr1_AFileWhoseReportedLengthIsOverTheCap_IsRefusedWithoutReading()
    {
        GuardedRead.OpenOverride = _ => new EndlessStream(GuardedRead.MaxBytes + 1, 5);
        try
        {
            Assert.Throws<IOException>(() => GuardedRead.ReadText("ignored"));
        }
        finally
        {
            GuardedRead.OpenOverride = null;
        }
    }

    [Fact]
    public void Gr2_ASymbolicLinkToAFifo_IsRefusedFast()
    {
        if (!TryMakeFifo(Path.Combine(_root, "real.fifo")) || !TryLink(Path.Combine(_root, "_linked.liquid"), "real.fifo", directory: false))
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        var ex = Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "linked"));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        Assert.DoesNotContain(_root, ex.Message);
    }


    private static int ThreadCount() => System.Diagnostics.Process.GetCurrentProcess().Threads.Count;

    [Fact]
    public void Fifo_InTheTemplateRoot_IsRefusedImmediately_ByTwentyConcurrentRequests_WithNoThreadLeft()
    {
        if (!TryMakeFifo(Path.Combine(_root, "_pipe3.liquid")))
        {
            return;
        }

        var fs = new EnhancedLocalFileSystem(_root);
        var before = ThreadCount();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failures = new Exception?[20];
        var threads = Enumerable.Range(0, 20).Select(i => new System.Threading.Thread(() => failures[i] = Record.Exception(() => Read(fs, "pipe3")))).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.All(failures, ex =>
        {
            Assert.IsType<FileSystemException>(ex);
            Assert.DoesNotContain(_root, ex!.Message);
        });
        Assert.True(ThreadCount() <= before + 2, $"threads {before} -> {ThreadCount()}: a blocked reader was left behind");
    }

    [Fact]
    public void G1_ARegularPartialSwappedForAFifoBeforeTheOpen_IsStillRefusedImmediately()
    {
        File.WriteAllText(Path.Combine(_root, "_swap.liquid"), "regular");
        if (!TryMakeFifo(Path.Combine(_root, "swap.fifo")))
        {
            return;
        }

        GuardedRead.BeforeOpen = p =>
        {
            if (!p.EndsWith("_swap.liquid", StringComparison.Ordinal))
            {
                return; // other tests may read files while this hook is set
            }

            File.Delete(p);
            File.Move(Path.Combine(_root, "swap.fifo"), p);
        };
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var ex = Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "swap"));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
            Assert.DoesNotContain(_root, ex.Message);
        }
        finally
        {
            GuardedRead.BeforeOpen = null;
        }
    }

    [Fact]
    public void Device_ReachedThroughASymbolicLink_IsRefused_AndADirectoryIsNotAPartial()
    {
        Directory.CreateDirectory(Path.Combine(_root, "_dir.liquid"));
        if (!OperatingSystem.IsWindows() && TryLink(Path.Combine(_root, "_null.liquid"), "/dev/null", directory: false))
        {
            var ex = Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "null"));
            Assert.DoesNotContain(_root, ex.Message);
        }

        Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "dir"));
    }
}
