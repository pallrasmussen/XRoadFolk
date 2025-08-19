using System.Xml.Linq;

public sealed class FolkTokenProviderRaw
{
    private readonly FolkRawClient _client;
    private readonly TimeSpan _skew;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<CancellationToken, Task<string>> _loginCall;

    private string? _token;
    private DateTimeOffset _expiresUtc = DateTimeOffset.MinValue;

    // Coalesce concurrent refreshes
    private Task<string>? _refreshTask;

    // Default skew set to ZERO to avoid immediate re-refresh in quick successive calls.
    public FolkTokenProviderRaw(FolkRawClient client, Func<CancellationToken, Task<string>> loginCall, TimeSpan? refreshSkew = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _loginCall = loginCall ?? throw new ArgumentNullException(nameof(loginCall));
        _skew = refreshSkew ?? TimeSpan.Zero;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (!NeedsRefresh())
        {
            return _token!;
        }

        Task<string> refresh;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!NeedsRefresh())
            {
                return _token!;
            }

            _refreshTask ??= RefreshAsync(ct);
            refresh = _refreshTask;
        }
        finally
        {
            _gate.Release();
        }

        string token = await refresh.ConfigureAwait(false);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(refresh, _refreshTask))
            {
                _refreshTask = null;
            }

            return token;
        }
        finally
        {
            _gate.Release();
        }
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

        if (expEl != null && DateTimeOffset.TryParse(expEl.Value.Trim(), out DateTimeOffset exp))
        {
            _expiresUtc = exp.ToUniversalTime();
        }
        else
        {
            _expiresUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        }

        return _token!;
    }

    private bool NeedsRefresh()
    {
        return string.IsNullOrWhiteSpace(_token) || DateTimeOffset.UtcNow.Add(_skew) >= _expiresUtc;
    }
}
