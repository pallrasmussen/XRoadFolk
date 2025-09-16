namespace XRoadFolkWeb.Infrastructure;

/// <summary>
/// Provides access to the current user's identity name in web contexts.
/// </summary>
public interface ICurrentUserAccessor
{
    string? Name { get; }
}

internal sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    public HttpContextCurrentUserAccessor(IHttpContextAccessor http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }
    public string? Name => _http.HttpContext?.User?.Identity?.Name;
}
