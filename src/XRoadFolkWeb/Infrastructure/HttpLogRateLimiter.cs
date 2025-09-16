using System.Threading;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    /// <summary>
    /// Thread-safe, per-second rate limiter used by HTTP logging stores.
    /// </summary>
    internal sealed class HttpLogRateLimiter
    {
        private readonly int _maxWritesPerSecond;
        private readonly bool _alwaysAllowWarnError;
        private long _rateWindowStartMs = Environment.TickCount64;
        private int _rateCount;
        private readonly object _gate = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpLogRateLimiter"/> class.
        /// </summary>
        /// <param name="maxWritesPerSecond">The maximum number of writes allowed per second.</param>
        /// <param name="alwaysAllowWarnError">
        /// A value indicating whether warnings and errors should always be allowed,
        /// bypassing the rate limit.
        /// </param>
        public HttpLogRateLimiter(int maxWritesPerSecond, bool alwaysAllowWarnError)
        {
            _maxWritesPerSecond = Math.Max(0, maxWritesPerSecond);
            _alwaysAllowWarnError = alwaysAllowWarnError;
        }

        /// <summary>
        /// Returns true if the event should be dropped due to rate limiting.
        /// Warnings and errors can bypass if configured.
        /// </summary>
        public bool ShouldDrop(LogLevel level)
        {
            if (_maxWritesPerSecond <= 0)
            {
                return false;
            }

            if (_alwaysAllowWarnError && level >= LogLevel.Warning)
            {
                return false;
            }

            long now = Environment.TickCount64;
            long start = Volatile.Read(ref _rateWindowStartMs);
            long elapsed = unchecked(now - start);
            if (elapsed is >= 1000 or < 0)
            {
                lock (_gate)
                {
                    // Re-read under lock to avoid redundant resets
                    start = _rateWindowStartMs;
                    elapsed = unchecked(now - start);
                    if (elapsed is >= 1000 or < 0)
                    {
                        _rateWindowStartMs = now; // protected by lock
                        _rateCount = 0;           // protected by lock
                    }
                }
            }

            int count = Interlocked.Increment(ref _rateCount);
            return count > _maxWritesPerSecond;
        }

        /// <summary>
        /// Gets a value indicating whether warnings and errors are always allowed,
        /// bypassing the rate limit.
        /// </summary>
        public bool AlwaysAllowWarnError => _alwaysAllowWarnError;
    }
}
