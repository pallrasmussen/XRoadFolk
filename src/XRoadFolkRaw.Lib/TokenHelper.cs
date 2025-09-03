using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace XRoadFolkRaw.Lib
{
    public sealed class FolkTokenProviderRaw(Func<CancellationToken, Task<string>> loginCall, TimeSpan? refreshSkew = null) : IDisposable
    {
        private readonly TimeSpan _skew = refreshSkew ?? TimeSpan.Zero;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Func<CancellationToken, Task<string>> _loginCall = loginCall ?? throw new ArgumentNullException(nameof(loginCall));

        private string? _token;
        private DateTimeOffset _expiresUtc = DateTimeOffset.MinValue;

        /// <summary>
        /// Coalesce concurrent refreshs
        /// </summary>
        private Task<string>? _refreshTask;

        public async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            (string Token, _) = await GetTokenWithExpiryAsync(ct).ConfigureAwait(false);
            return Token;
        }

        public async Task<(string Token, DateTimeOffset ExpiresUtc)> GetTokenWithExpiryAsync(CancellationToken ct = default)
        {
            if (!NeedsRefresh())
            {
                return (_token ?? throw new InvalidOperationException("Token not initialized."), _expiresUtc);
            }

            Task<string> refresh;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!NeedsRefresh())
                {
                    return (_token ?? throw new InvalidOperationException("Token not initialized."), _expiresUtc);
                }

                _refreshTask ??= RefreshAsync(ct);
                refresh = _refreshTask;
            }
            finally
            {
                _ = _gate.Release();
            }

            string token;
            try
            {
                token = await refresh.ConfigureAwait(false);
            }
            finally
            {
                // Use non-cancellable cleanup to ensure _refreshTask is cleared even if caller cancels
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(refresh, _refreshTask))
                    {
                        _refreshTask = null;
                    }
                }
                finally
                {
                    _ = _gate.Release();
                }
            }

            return (token, _expiresUtc);
        }

        private async Task<string> RefreshAsync(CancellationToken ct)
        {
            string xml = await _loginCall(ct).ConfigureAwait(false);

            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 10 * 1024 * 1024,
                CloseInput = true,
            };

            using StringReader sr = new(xml);
            using XmlReader reader = XmlReader.Create(sr, settings);
            XDocument doc = XDocument.Load(reader, LoadOptions.None);

            string? tokenText = null;
            string? expiryText = null;

            foreach (XElement el in doc.Descendants())
            {
                string name = el.Name.LocalName;
                if (tokenText is null && name.Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    tokenText = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(tokenText) && expiryText is not null)
                    {
                        break; // found both
                    }
                    continue;
                }

                if (expiryText is null && (name.Equals("expires", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("expiry", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("expiration", StringComparison.OrdinalIgnoreCase)))
                {
                    expiryText = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(expiryText) && tokenText is not null)
                    {
                        break; // found both
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(tokenText))
            {
                throw new InvalidOperationException("Login response did not contain <token>");
            }

            _token = tokenText!;

            if (!string.IsNullOrWhiteSpace(expiryText))
            {
                string txt = expiryText!;
                if (DateTimeOffset.TryParse(txt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset exp))
                {
                    _expiresUtc = exp - _skew;
                }
                else if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
                {
                    _expiresUtc = DateTimeOffset.UtcNow.AddSeconds(seconds) - _skew;
                }
                else
                {
                    _expiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) - _skew;
                }
            }
            else
            {
                _expiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) - _skew;
            }

            return _token;
        }

        private bool NeedsRefresh()
        {
            return string.IsNullOrWhiteSpace(_token) || DateTimeOffset.UtcNow >= _expiresUtc;
        }

        public void Dispose()
        {
            _gate.Dispose();
        }
    }
}
