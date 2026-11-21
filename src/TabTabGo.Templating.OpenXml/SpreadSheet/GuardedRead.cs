using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    /// <summary>
    /// Opens and reads a file from a folder the host trusts but other accounts may be able to write to. A named pipe or device placed there makes <c>File.Exists</c> true, and a plain
    /// open of it blocks forever. .NET cannot tell a regular file from a pipe without opening it, and cannot open without blocking, so on Linux and macOS the file is opened ONCE, read-only,
    /// with <c>O_NONBLOCK</c> through one small native call (<c>open(path, flags)</c>, no variadic arguments; this is the only P/Invoke in the library). That open returns at once for a pipe,
    /// a terminal or a device; the handle is then verified (a regular file is seekable and not larger than <see cref="MaxBytes"/>) and read from the SAME handle, so there is no separate
    /// type check that a swapped file could outrun, no write-open, no helper thread and no counter. The read itself is capped at <see cref="MaxBytes"/> by counting the bytes that arrive.
    /// Windows opens the file normally (a device handle is not seekable and is refused). <b>Known limitation:</b> on another Unix, or where libc cannot be loaded, a plain open is used and a pipe
    /// placed in the template or stylesheet folder would block the render; the folder must not be writable by untrusted parties there. Every refusal is an <see cref="IOException"/> without a path.
    /// </summary>
    internal static class GuardedRead
    {
        /// <summary>The largest file read (templates and stylesheets are small; anything bigger is refused).</summary>
        internal const long MaxBytes = 16 * 1024 * 1024;

        private const int ErrorNoEntry = 2;
        private const int ErrorAccess = 13;

        private static int _nativeUnavailable;

        /// <summary>Runs just before the open (tests swap a regular file for a pipe here).</summary>
        internal static Action<string> BeforeOpen { get; set; }

        /// <summary>Replaces the open (tests hand back a stream that lies about its length).</summary>
        internal static Func<string, Stream> OpenOverride { get; set; }

        /// <summary>Opens <paramref name="path"/> once, refuses anything that is not a seekable regular file, and hands a capped view of the same stream to <paramref name="consume"/>.</summary>
        public static T Read<T>(string path, Func<Stream, T> consume)
        {
            var hook = BeforeOpen;
            if (hook != null)
            {
                hook(path);
            }

            var over = OpenOverride;
            using (var opened = over != null ? over(path) : Open(path))
            {
                if (!opened.CanSeek || opened.Length > MaxBytes)
                {
                    throw new IOException("The file is not a regular file of usable size.");
                }

                return consume(new CappedStream(opened, MaxBytes));
            }
        }

        /// <summary>Reads a text file the way <c>File.ReadAllText</c> does (UTF-8, byte-order marks honoured), through <see cref="Read{T}"/>.</summary>
        public static string ReadText(string path)
        {
            return Read(path, stream =>
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            });
        }

        private static Stream Open(string path)
        {
            if (!OperatingSystem.IsWindows() && Volatile.Read(ref _nativeUnavailable) == 0)
            {
                var stream = TryOpenNonBlocking(path);
                if (stream != null)
                {
                    return stream;
                }
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        }

        private static FileStream TryOpenNonBlocking(string path)
        {
            int flags;
            if (OperatingSystem.IsMacOS())
            {
                flags = 0x0004 | 0x1000000; // O_RDONLY | O_NONBLOCK | O_CLOEXEC
            }
            else if (OperatingSystem.IsLinux())
            {
                flags = 0x0800 | 0x80000; // O_RDONLY | O_NONBLOCK | O_CLOEXEC (x86, x64, arm, arm64)
            }
            else
            {
                return null;
            }

            int fd;
            int error;
            try
            {
                fd = OperatingSystem.IsMacOS() ? Native.MacOpen(path, flags) : Native.LinuxOpen(path, flags);
                error = Marshal.GetLastPInvokeError();
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                Volatile.Write(ref _nativeUnavailable, 1);
                return null;
            }

            if (fd < 0)
            {
                if (error == ErrorNoEntry)
                {
                    throw new FileNotFoundException("The file was not found.");
                }

                if (error == ErrorAccess)
                {
                    throw new UnauthorizedAccessException("The file cannot be read.");
                }

                throw new IOException("The file cannot be opened.");
            }

            var handle = new SafeFileHandle((IntPtr)fd, true);
            try
            {
                return new FileStream(handle, FileAccess.Read, 1, false);
            }
            catch
            {
                handle.Dispose();
                throw new IOException("The file is not a regular file.");
            }
        }

        private static class Native
        {
            [DllImport("libSystem", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
            internal static extern int MacOpen(string path, int flags);

            [DllImport("libc.so.6", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
            internal static extern int LinuxOpen(string path, int flags);
        }

        /// <summary>A read-only view that throws once more than <c>limit</c> bytes have been read, whatever length the file reported when it was opened.</summary>
        private sealed class CappedStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _limit;
            private long _total;

            public CappedStream(Stream inner, long limit)
            {
                _inner = inner;
                _limit = limit;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

            public override int Read(Span<byte> buffer) => Count(_inner.Read(buffer));

            private int Count(int read)
            {
                _total += read;
                if (_total > _limit)
                {
                    throw new IOException("The file is larger than the allowed size.");
                }

                return read;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
