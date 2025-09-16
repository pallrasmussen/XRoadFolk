using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Generic;

namespace XRoadFolkRaw.Lib
{
    /// <summary>
    /// Helpers for structured logging scopes and masking sensitive values.
    /// Use <see cref="BeginCorrelationScope"/> to attach a correlation id to logs, and <see cref="Mask"/>
    /// to obfuscate secrets while preserving length. These helpers never throw.
    /// </summary>
    public static class LoggingHelper
    {
        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }

        /// <summary>
        /// Minimal allocation list that carries a single key/value for a logging scope.
        /// </summary>
        private readonly struct OnePropertyList : IReadOnlyList<KeyValuePair<string, object?>>
        {
            private readonly string _key;
            private readonly object? _value;
            public OnePropertyList(string key, object? value) { _key = key; _value = value; }
            public int Count => 1;
            public KeyValuePair<string, object?> this[int index]
                => index == 0 ? new KeyValuePair<string, object?>(_key, _value) : throw new ArgumentOutOfRangeException(nameof(index));

            public Enumerator GetEnumerator() => new(_key, _value);
            IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<KeyValuePair<string, object?>>
            {
                private readonly string _key;
                private readonly object? _value;
                private bool _moved;
                public Enumerator(string key, object? value) { _key = key; _value = value; _moved = false; }
                public KeyValuePair<string, object?> Current => new(_key, _value);
                object IEnumerator.Current => Current;
                public bool MoveNext()
                {
                    if (_moved)
                    {
                        return false;
                    }
                    _moved = true;
                    return true;
                }
                public void Reset() { _moved = false; }
                public void Dispose() { }
            }
        }

        /// <summary>
        /// Creates a logging scope that carries a correlation identifier. If no
        /// identifier is supplied, the current Activity Id or a random value is used.
        /// This method never throws; if the provider doesn't support BeginScope, it returns a no-op disposable.
        /// </summary>
        /// <param name="logger">The logger to create a scope from.</param>
        /// <param name="correlationId">Optional correlation id to propagate.</param>
        /// <returns>An <see cref="IDisposable"/> scope that should be disposed to end the scope.</returns>
        public static IDisposable BeginCorrelationScope(ILogger logger, string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(logger);

            string id = !string.IsNullOrWhiteSpace(correlationId)
                ? correlationId!
                : (Activity.Current?.Id ?? Guid.NewGuid().ToString("N"));

            try
            {
                var state = new OnePropertyList("correlationId", id);
                return logger.BeginScope(state) ?? NoopDisposable.Instance;
            }
            catch
            {
                // Never throw from logging helpers
                return NoopDisposable.Instance;
            }
        }

        /// <summary>
        /// Masks a value by replacing leading characters with '*'. The last
        /// <paramref name="visible"/> characters remain visible. If visible is 0 or less,
        /// the entire value is masked. Returns empty string for null/empty inputs.
        /// </summary>
        /// <param name="value">The value to mask.</param>
        /// <param name="visible">Number of trailing characters to keep.</param>
        /// <returns>A masked string with the same length as the input.</returns>
        public static string Mask(string? value, int visible = 4)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int len = value.Length;
            if (visible <= 0)
            {
                return new string('*', len);
            }

            int vis = visible >= len ? len : visible;
            if (vis == len)
            {
                // Nothing to mask
                return value;
            }

            int masked = len - vis; // number of asterisks
            return string.Create(len, (value, masked), static (dest, state) =>
            {
                string src = state.Item1;
                int stars = state.Item2;
                dest.Slice(0, stars).Fill('*');
                src.AsSpan(stars).CopyTo(dest.Slice(stars));
            });
        }
    }
}
