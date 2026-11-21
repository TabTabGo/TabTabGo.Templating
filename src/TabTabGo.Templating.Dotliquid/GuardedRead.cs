using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TabTabGo.Templating.Liquid
{
    /// <summary>
    /// Reads a file that lives in a folder the host trusts but other accounts may be able to write to. A FIFO or device placed there makes <c>File.Exists</c> true and the read block
    /// forever, and there is no portable way to ask for the file type without a native call. Every regular file with content has a length, so only a zero-length file (an empty file, or any
    /// special file) is read on a background thread with a time-out; a regular non-empty file is read directly, with no thread.
    /// </summary>
    internal static class GuardedRead
    {
        /// <summary>How long a zero-length file may take to read before it is treated as not usable (tests lower it).</summary>
        internal static TimeSpan SpecialFileTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Runs <paramref name="read"/> for <paramref name="path"/>; throws <see cref="TimeoutException"/> when a zero-length file does not finish in time.</summary>
        public static T Run<T>(string path, Func<T> read)
        {
            if (new FileInfo(path).Length > 0)
            {
                return read();
            }

            T result = default;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = read();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            if (!thread.Join(SpecialFileTimeout))
            {
                throw new TimeoutException();
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result;
        }
    }
}
