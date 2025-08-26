using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure.Logging;

/// <summary>
/// Logs outgoing HTTP requests/responses. Always logs full absolute URI and method.
/// When verbose is enabled (Logging:Verbose = true), also logs sanitized headers and sizes.
/// </summary>
public sealed class OutgoingHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<OutgoingHttpLoggingHandler> _log;
    private readonly bool _verbose;

    public OutgoingHttpLoggingHandler(ILogger<OutgoingHttpLoggingHandler> log, bool verbose)
    {
        _log = log;
        _verbose = verbose;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? "(null)";
        var method = request.Method.Method;

        long reqBytes = request.Content?.Headers.ContentLength ?? 0;
        if (reqBytes == 0 && _verbose && request.Content is not null)
        {
            try
            {
                // Try to compute size without consuming stream
                var buff = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                reqBytes = buff.LongLength;
                request.Content = new ByteArrayContent(buff);
                foreach (var h in request.Content.Headers)
                {
                    // Retain original content headers if any
                    request.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }
            catch { /* ignore size calc failures */ }
        }

        if (!_verbose)
        {
            _log.LogInformation("HTTP OUT {Method} {Uri}", method, uri);
        }
        else
        {
            string Sanitize(string key, string value) =>
                (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                ? "***" : value;
            string Trunc(string s) => s.Length > 200 ? s[..200] + "…" : s;
            string reqHeaders = string.Join(
                "; ",
                request.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, string.Join(",", h.Value)))}").Concat(
                    request.Content?.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, string.Join(",", h.Value)))}") ?? Array.Empty<string>()));
            _log.LogInformation("HTTP OUT {Method} {Uri} | req={ReqBytes}B | reqHdrs: {ReqHeaders}", method, uri, reqBytes, reqHeaders);
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        long resBytes = 0;
        try
        {
            resBytes = response.Content?.Headers.ContentLength ?? 0;
        }
        catch { }

        if (!_verbose)
        {
            _log.LogInformation("HTTP IN  {Status} {Method} {Uri} in {Elapsed} ms", (int)response.StatusCode, method, uri, sw.ElapsedMilliseconds);
        }
        else
        {
            string Sanitize(string key, string value) =>
                (key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) || key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                ? "***" : value;
            string Trunc(string s) => s.Length > 200 ? s[..200] + "…" : s;
            string resHeaders = string.Join("; ", response.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, string.Join(",", h.Value)))}").Concat(
                response.Content?.Headers.Select(h => $"{h.Key}={Trunc(Sanitize(h.Key, string.Join(",", h.Value)))}") ?? Array.Empty<string>()));
            _log.LogInformation("HTTP IN  {Status} {Method} {Uri} in {Elapsed} ms | res={ResBytes}B | resHdrs: {ResHeaders}", (int)response.StatusCode, method, uri, sw.ElapsedMilliseconds, resBytes, resHeaders);
        }

        return response;
    }
}
