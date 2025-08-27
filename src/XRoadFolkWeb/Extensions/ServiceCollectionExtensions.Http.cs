using System.Net;
using System.Security.Cryptography.X509Certificates;
using XRoadFolkRaw.Lib;
//using XRoadFolkRaw.Lib.Logging;
//using XRoadFolkRaw.Lib.Options;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        // Add LoggerMessage delegate for improved performance
        private static readonly Action<ILogger, Exception?> _logCertWarning =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1, "ClientCertNotConfigured"),
                "Client certificate not configured. Proceeding without certificate."
            );

        public static IServiceCollection AddXRoadHttpClient(this IServiceCollection services)
        {
            _ = services.AddHttpClient("XRoadFolk", (sp, c) =>
            {
                XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
                c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
                c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(static sp =>
            {
                SocketsHttpHandler handler = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 20,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();

                try
                {
                    X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
                    handler.SslOptions.ClientCertificates ??= [];
                    _ = handler.SslOptions.ClientCertificates.Add(cert);
                }
                catch (Exception ex)
                {
                    ILogger log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadCert");
                    _logCertWarning(log, ex); // Use LoggerMessage delegate
                }

                IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
                bool bypass = cfg.GetValue("Http:BypassServerCertificateValidation", true);
                IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
                if (env.IsDevelopment() && bypass)
                {
                    handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                }

                return handler;
            });

            return services;
        }
    }
}
