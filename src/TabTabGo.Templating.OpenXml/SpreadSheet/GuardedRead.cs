using System;
using System.IO;
using System.Text;

namespace TabTabGo.Templating.OpenXml.SpreadSheet
{
    /// <summary>
    /// Reads a template or stylesheet file that the caller has already resolved. The file is opened once and verified on that same handle (it must be seekable and not larger than
    /// <see cref="MaxBytes"/>), and the read itself is capped by counting the bytes that arrive, never by trusting the length taken at open time, so a file that grows while it is read is
    /// refused at <see cref="MaxBytes"/> + 1 bytes. Every refusal is an <see cref="IOException"/> without a path.
    /// <para>
    /// <b>Known limitation (measured, not guessed):</b> .NET reports a named pipe, a device file and an empty regular file identically (<c>FileAttributes.Normal</c>, length 0, the same
    /// permission-only <c>UnixFileMode</c>), and a plain open of a named pipe blocks until a writer appears. There is no managed way to tell them apart before opening, and a native call is not
    /// portable to every platform this library runs on, so none is made. The template and stylesheet folders must therefore not be writable by untrusted parties: someone who can place a pipe
    /// there can already replace the templates themselves.
    /// </para>
    /// </summary>
    internal static class GuardedRead
    {
        /// <summary>The largest file read (templates and stylesheets are small; anything bigger is refused).</summary>
        internal const long MaxBytes = 16 * 1024 * 1024;

        /// <summary>Replaces the open (tests hand back a stream that lies about its length).</summary>
        internal static Func<string, Stream> OpenOverride { get; set; }

        /// <summary>Opens <paramref name="path"/> once, refuses anything that is not a seekable file of usable size, and hands a capped view of the same stream to <paramref name="consume"/>.</summary>
        public static T Read<T>(string path, Func<Stream, T> consume)
        {
            var over = OpenOverride;
            using (var opened = over != null ? over(path) : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
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
