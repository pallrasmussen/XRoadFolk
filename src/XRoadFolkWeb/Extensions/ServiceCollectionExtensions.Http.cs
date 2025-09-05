using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        private sealed class CorrelationIdHandler : DelegatingHandler
        {
            private readonly ILogger _log;
            private const string HeaderName = "X-Correlation-Id";

            public CorrelationIdHandler(ILoggerFactory lf)
            {
                _log = lf.CreateLogger("Http.CorrelationId");
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                try
                {
                    string id = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
                    if (!request.Headers.Contains(HeaderName))
                    {
                        request.Headers.TryAddWithoutValidation(HeaderName, id);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to set correlation id header");
                }
                return base.SendAsync(request, cancellationToken);
            }
        }

        public static IServiceCollection AddXRoadHttpClient(this IServiceCollection services)
        {
            // Delegate to the Lib project which owns all certificate processing
            XRoadFolkRaw.Lib.Extensions.ServiceCollectionExtensions.AddXRoadHttpClient(services);

            // Add correlation handler to named client
            services.AddTransient<CorrelationIdHandler>();
            services.AddHttpClient("XRoadFolk").AddHttpMessageHandler<CorrelationIdHandler>();
            return services;
        }
    }
}
