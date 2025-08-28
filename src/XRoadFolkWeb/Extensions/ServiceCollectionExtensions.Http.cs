using System.Net;
using System.Net.Security;
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
                    handler.SslOptions.RemoteCertificateValidationCallback = DevCertificateValidation;
                }

                return handler;
            });

            return services;
        }

        private static bool DevCertificateValidation(object _, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (certificate is null)
            {
                return false;
            }

            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            X509Certificate2 cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            chain ??= new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
                                                 | X509VerificationFlags.IgnoreEndRevocationUnknown
                                                 | X509VerificationFlags.AllowUnknownCertificateAuthority;
            bool chainOk = chain.Build(cert2);

            // In development, allow only hostname mismatch if the chain is otherwise valid
            return chainOk || errors == SslPolicyErrors.RemoteCertificateNameMismatch;
        }
    }
}
