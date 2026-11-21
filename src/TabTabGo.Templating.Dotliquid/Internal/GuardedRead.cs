using System.Runtime.ExceptionServices;
using System.Text;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// Opens and reads a file from a folder the host trusts but other accounts may be able to write to. A named pipe or device placed there makes <c>File.Exists</c> true and the
/// open (or the read) block forever. The open is the blocking step, so it runs on a short-lived background thread with a time-out; the handle it returns is then checked
/// (a regular file is seekable, a pipe or a terminal is not) and read from the SAME handle, bounded by <see cref="MaxBytes"/>: there is no separate check that a swapped file could outrun.
/// On a time-out the blocked open is released (Unix: by opening the pipe's write end, which pairs with the waiting reader), and a reader that cannot be released is counted;
/// new reads are refused while <see cref="MaxStuckReaders"/> are still stuck, so a stream of such files cannot exhaust threads. Every failure is an <see cref="IOException"/> without a path.
/// </summary>
internal static class GuardedRead
{
    /// <summary>The largest file read (templates and stylesheets are small; anything bigger is refused).</summary>
    internal const long MaxBytes = 16 * 1024 * 1024;

    /// <summary>The most blocked opens tolerated at once; further reads are refused until some are released.</summary>
    internal const int MaxStuckReaders = 4;

    private static int _stuck;
    private static int _live;

    /// <summary>How long the open may take before the file is treated as not usable (tests lower it).</summary>
    internal static TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Runs between the decision to read and the open, on the worker thread (tests swap a file for a pipe here).</summary>
    internal static Action<string>? BeforeOpen { get; set; }

    /// <summary>Blocked opens that were abandoned and have not finished yet.</summary>
    internal static int StuckReaders => Volatile.Read(ref _stuck);

    /// <summary>Worker threads that are still running (a blocked open keeps its thread until it is released).</summary>
    internal static int LiveWorkers => Volatile.Read(ref _live);

    /// <summary>Opens <paramref name="path"/> once, refuses anything that is not a seekable regular file of at most <see cref="MaxBytes"/>, and hands the same stream to <paramref name="consume"/>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="consume">Reads the stream.</param>
    /// <exception cref="IOException">The file cannot be opened in time, is not a regular file, or is too large (no path in the message).</exception>
    public static T Read<T>(string path, Func<Stream, T> consume)
    {
        if (StuckReaders >= MaxStuckReaders)
        {
            throw new IOException("Too many files are blocking; the file was not read.");
        }

        var state = new OpenState();
        var thread = new Thread(
            () =>
            {
                Interlocked.Increment(ref _live);
                try
                {
                    BeforeOpen?.Invoke(path);
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                    lock (state)
                    {
                        if (state.Abandoned)
                        {
                            stream.Dispose();
                        }
                        else
                        {
                            state.Stream = stream;
                        }
                    }
                }
                catch (Exception ex)
                {
                    state.Failure = ex;
                }
                finally
                {
                    lock (state)
                    {
                        state.Done = true;
                        if (state.Abandoned)
                        {
                            Interlocked.Decrement(ref _stuck);
                        }
                    }

                    Interlocked.Decrement(ref _live);
                }
            })
        {
            IsBackground = true,
        };
        thread.Start();

        if (!thread.Join(OpenTimeout))
        {
            lock (state)
            {
                if (!state.Done)
                {
                    state.Abandoned = true;
                    Interlocked.Increment(ref _stuck);
                }
            }

            ReleaseBlockedOpen(path);
            throw new IOException("The file could not be opened in time.");
        }

        if (state.Failure != null)
        {
            ExceptionDispatchInfo.Capture(state.Failure).Throw();
        }

        using var opened = state.Stream!;
        if (!opened.CanSeek || opened.Length > MaxBytes)
        {
            throw new IOException("The file is not a regular file of usable size.");
        }

        return consume(opened);
    }

    /// <summary>Reads a text file the way <c>File.ReadAllText</c> does (UTF-8, byte-order marks honoured), through <see cref="Read{T}"/>.</summary>
    /// <param name="path">The file.</param>
    public static string ReadText(string path) => Read(path, stream =>
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// Releases an open that is blocked on a named pipe by opening its write end (Unix): the waiting reader is paired with it and gets an empty stream, which the check then refuses.
    /// Best effort: on other platforms, or when the file is not a pipe, nothing happens and the counter keeps the abandoned open bounded.
    /// </summary>
    private static void ReleaseBlockedOpen(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var releaser = new Thread(
            () =>
            {
                try
                {
                    using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // not a pipe, or not writable: nothing to release
                }
            })
        {
            IsBackground = true,
        };
        releaser.Start();
        releaser.Join(TimeSpan.FromSeconds(1));
    }

    private sealed class OpenState
    {
        public FileStream? Stream;
        public Exception? Failure;
        public bool Done;
        public bool Abandoned;
    }
}
