using System.Net;
using System.Security.Cryptography.X509Certificates;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddXRoadHttpClient(this IServiceCollection services)
    {
        services.AddHttpClient("XRoadFolk", (sp, c) =>
        {
            XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
            c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
            c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
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
                handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
                _ = handler.SslOptions.ClientCertificates.Add(cert);
            }
            catch (Exception ex)
            {
                ILogger log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadCert");
                log.LogWarning(ex, "Client certificate not configured. Proceeding without certificate.");
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
