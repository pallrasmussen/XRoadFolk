using System.Globalization;
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

        // Coalesce concurrent refreshs
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
                await _gate.WaitAsync(ct).ConfigureAwait(false);
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

            XDocument doc = XDocument.Parse(xml);
            XElement? tokenEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("token", StringComparison.OrdinalIgnoreCase));
            XElement? expEl = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("expiry", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("expiration", StringComparison.OrdinalIgnoreCase));

            if (tokenEl == null)
            {
                throw new InvalidOperationException("Login response did not contain <token>");
            }

            _token = tokenEl.Value.Trim();

            _expiresUtc =
                expEl != null &&
                DateTimeOffset.TryParse(expEl.Value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset exp)
                    ? exp.ToUniversalTime()
                    : DateTimeOffset.UtcNow.AddMinutes(5);

            string token = _token ?? throw new InvalidOperationException("Token not parsed.");
            return token;
        }

        private bool NeedsRefresh()
        {
            return string.IsNullOrWhiteSpace(_token) || DateTimeOffset.UtcNow.Add(_skew) >= _expiresUtc;
        }

        public void Dispose()
        {
            _gate.Dispose();
        }
    }
}
