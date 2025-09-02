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
                Volatile.Write(ref _rateWindowStartMs, now);
                Volatile.Write(ref _rateCount, 0);
            }

            int count = Interlocked.Increment(ref _rateCount);
            return count > _maxWritesPerSecond;
        }

        public bool AlwaysAllowWarnError => _alwaysAllowWarnError;
    }
}
