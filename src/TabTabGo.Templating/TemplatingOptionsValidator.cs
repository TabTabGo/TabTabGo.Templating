using System;
using System.Threading;

namespace TabTabGo.Templating
{
    /// <summary>Validation shared by the engine packages (they see it through InternalsVisibleTo).</summary>
    internal static class TemplatingOptionsValidator
    {
        private static readonly TimeSpan MaxRegexTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1);

        /// <summary>Throws when an option the engine relies on is out of range. Called once, from the engine constructors.</summary>
        /// <param name="options">The options to check.</param>
        /// <param name="validateRegexTimeout">False for engines that run no regular expressions.</param>
        public static TemplatingOptions Validate(TemplatingOptions options, bool validateRegexTimeout)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (validateRegexTimeout
                && options.RegexTimeout != Timeout.InfiniteTimeSpan
                && (options.RegexTimeout <= TimeSpan.Zero || options.RegexTimeout > MaxRegexTimeout))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "RegexTimeout must be positive (or Timeout.InfiniteTimeSpan).");
            }

            if (options.MaxXmlCharacters < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxXmlCharacters must not be negative (0 = unlimited).");
            }

            if (options.MaxNestingDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxNestingDepth must be at least 1.");
            }

            return options;
        }
    }
}
