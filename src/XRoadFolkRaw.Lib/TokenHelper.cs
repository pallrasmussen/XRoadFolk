using System.Globalization;
using System.Text.Json;
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
            XDocument doc = LoadXml(xml);
            (string token, string? expiryText) = ExtractTokenAndExpiry(doc);
            _token = token;

            DateTimeOffset exp = ComputeExpiryFromText(expiryText);
            if (exp == DateTimeOffset.MinValue)
            {
                // Try to infer from JWT 'exp' if possible
                if (TryGetJwtExpiryUtc(_token, out DateTimeOffset jwtExp))
                {
                    exp = jwtExp;
                }
            }

            _expiresUtc = (exp == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow.AddMinutes(30) : exp) - _skew;
            return _token;
        }

        private static XDocument LoadXml(string xml)
        {
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
            return XDocument.Load(reader, LoadOptions.None);
        }

        private static (string Token, string? Expiry) ExtractTokenAndExpiry(XDocument doc)
        {
            string? tokenText = null;
            string? expiryText = null;

            foreach (XElement el in doc.Descendants())
            {
                string name = el.Name.LocalName;

                // Token candidates
                if (tokenText is null && (name.Equals("token", StringComparison.OrdinalIgnoreCase)
                                          || name.Equals("accessToken", StringComparison.OrdinalIgnoreCase)
                                          || name.Equals("access_token", StringComparison.OrdinalIgnoreCase)
                                          || name.Equals("authToken", StringComparison.OrdinalIgnoreCase)))
                {
                    tokenText = ReadValueOrValueAttribute(el);
                    if (!string.IsNullOrEmpty(tokenText) && expiryText is not null)
                    {
                        break;
                    }
                    continue;
                }

                // Expiry candidates
                if (expiryText is null && (name.Equals("expires", StringComparison.OrdinalIgnoreCase)
                                           || name.Equals("expiry", StringComparison.OrdinalIgnoreCase)
                                           || name.Equals("expiration", StringComparison.OrdinalIgnoreCase)
                                           || name.Equals("expiresIn", StringComparison.OrdinalIgnoreCase)
                                           || name.Equals("expires_in", StringComparison.OrdinalIgnoreCase)))
                {
                    expiryText = ReadValueOrValueAttribute(el);
                    if (!string.IsNullOrEmpty(expiryText) && tokenText is not null)
                    {
                        break;
                    }
                }

                // Also look at attributes on a <token> element
                if (tokenText is not null && expiryText is null && el.Name.LocalName.Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    expiryText = el.Attribute("expires")?.Value
                              ?? el.Attribute("expiry")?.Value
                              ?? el.Attribute("expiration")?.Value
                              ?? el.Attribute("expiresIn")?.Value
                              ?? el.Attribute("expires_in")?.Value;
                    if (!string.IsNullOrWhiteSpace(expiryText))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(tokenText))
            {
                throw new InvalidOperationException("Login response did not contain <token>");
            }
            return (tokenText!, expiryText);
        }

        private static string? ReadValueOrValueAttribute(XElement el)
        {
            string? v = el.Value?.Trim();
            if (!string.IsNullOrEmpty(v))
            {
                return v;
            }
            string? attr = el.Attribute("value")?.Value?.Trim();
            return string.IsNullOrEmpty(attr) ? null : attr;
        }

        private static DateTimeOffset ComputeExpiryFromText(string? expiryText)
        {
            if (string.IsNullOrWhiteSpace(expiryText))
            {
                return DateTimeOffset.MinValue;
            }

            string txt = expiryText.Trim();

            // Try ISO / RFC date-time
            if (DateTimeOffset.TryParse(txt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset exp))
            {
                return exp;
            }

            // Try numeric epoch/relative
            if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long num))
            {
                // Heuristic: large values are likely epoch milliseconds
                if (num > 9999999999L) // > ~Sat Nov 20 2286 in seconds
                {
                    try { return DateTimeOffset.FromUnixTimeMilliseconds(num); } catch { }
                }
                else if (num > 1000000000L) // plausible epoch seconds
                {
                    try { return DateTimeOffset.FromUnixTimeSeconds(num); } catch { }
                }
                else
                {
                    // Treat as relative seconds
                    return DateTimeOffset.UtcNow.AddSeconds(num);
                }
            }

            return DateTimeOffset.MinValue;
        }

        private static bool TryGetJwtExpiryUtc(string? token, out DateTimeOffset exp)
        {
            exp = DateTimeOffset.MinValue;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            try
            {
                // Decode payload (Base64Url)
                string payload = parts[1];
                byte[] jsonBytes = Base64UrlDecode(payload);
                using JsonDocument doc = JsonDocument.Parse(jsonBytes);
                if (doc.RootElement.TryGetProperty("exp", out JsonElement expEl))
                {
                    if (expEl.ValueKind == JsonValueKind.Number && expEl.TryGetInt64(out long seconds))
                    {
                        exp = DateTimeOffset.FromUnixTimeSeconds(seconds);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
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
