namespace XRoadFolkWeb.Infrastructure;

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
