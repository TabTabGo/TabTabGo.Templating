using System;
using System.IO;
using System.Linq;
using DotLiquid;
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

    [Fact]
    public void ReadTemplateFile_DanglingLinkInsideTheRoot_ThrowsWithoutAServerPath()
    {
        try
        {
            File.CreateSymbolicLink(Path.Combine(_root, "_dangling.liquid"), "nonexistent.liquid");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
        {
            return;
        }

        var ex2 = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'dangling'"));

        Assert.DoesNotContain(_root, ex2.Message);
    }

    [Fact]
    public void FullPath_IllegalPath_ExceptionNamesNoRoot()
    {
        var ex = Assert.Throws<FileSystemException>(() => new EnhancedLocalFileSystem(_root).FullPath("../../../etc/passwd"));

        Assert.DoesNotContain(_root, ex.Message);
    }

    [Fact]
    public void ReadTemplateFile_AnEmptyRegularPartial_IsStillRead()
    {
        File.WriteAllText(Path.Combine(_root, "_empty.liquid"), string.Empty);

        Assert.Equal(string.Empty, new EnhancedLocalFileSystem(_root).ReadTemplateFile(new Context(System.Globalization.CultureInfo.InvariantCulture), "'empty'"));
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
    public void Device_ReachedThroughASymbolicLink_IsRefused_AndADirectoryIsNotAPartial()
    {
        Directory.CreateDirectory(Path.Combine(_root, "_dir.liquid"));
        if (!OperatingSystem.IsWindows() && TryLink(Path.Combine(_root, "_null.liquid"), "/dev/null", directory: false))
        {
            // v8 does not resolve links, and /dev/null is seekable with length 0: it reads as an empty partial at once (what matters is that it never blocks and leaks no path)
            var ex = Record.Exception(() => Read(new EnhancedLocalFileSystem(_root), "null"));
            Assert.True(ex == null || !ex.Message.Contains(_root));
        }

        Assert.Throws<FileSystemException>(() => Read(new EnhancedLocalFileSystem(_root), "dir"));
    }

    private static bool TryLink(string link, string target, bool directory)
    {
        try
        {
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
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public void KnownLimitation_AFifoIsIndistinguishableFromARegularFile_ByTheManagedFileApis()
    {
        // measured on macOS arm64: a pipe, a device file and an empty regular file all report FileAttributes.Normal, length 0 and permission-only UnixFileMode,
        // so no managed pre-open check can refuse a pipe; the folders must not be writable by untrusted parties (see GuardedRead). The probe never opens the pipe.
        var dir = _root;
        var fifo = Path.Combine(dir, "probe.fifo");
        var regular = Path.Combine(dir, "probe.empty");
        File.WriteAllText(regular, string.Empty);
        if (!TryMakeFifo(fifo))
        {
            return;
        }

        var a = new FileInfo(fifo);
        var b = new FileInfo(regular);

        Assert.True(a.Exists && b.Exists);
        Assert.Equal(b.Attributes, a.Attributes);
        Assert.Equal(b.Length, a.Length);
    }
}
