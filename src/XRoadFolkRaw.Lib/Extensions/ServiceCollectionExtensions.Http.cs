using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace XRoadFolkRaw.Lib.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddXRoadHttpClient(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));

            services.TryAddCertificateExpiryInfo();
            RegisterHttpClient(services);
            return services;
        }

        private static void RegisterHttpClient(IServiceCollection services)
        {
            _ = services.AddHttpClient("XRoadFolk", (sp, c) => ConfigureClient(sp, c))
                .AddHttpMessageHandler(sp => CreatePollyHandler(sp))
                .ConfigurePrimaryHttpMessageHandler(sp => CreatePrimaryHandler(sp));
        }

        private static void ConfigureClient(IServiceProvider sp, HttpClient c)
        {
            XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();
            c.BaseAddress = new Uri(xr.BaseUrl, UriKind.Absolute);
            c.Timeout = TimeSpan.FromSeconds(xr.Http.TimeoutSeconds);
        }

        private static DelegatingHandler CreatePollyHandler(IServiceProvider sp)
        {
            return new XRoadPollyHandler(
                sp.GetRequiredService<IOptions<XRoadFolkRaw.Lib.Options.HttpRetryOptions>>(),
                sp.GetRequiredService<ILoggerFactory>());
        }

        private static HttpMessageHandler CreatePrimaryHandler(IServiceProvider sp)
        {
            XRoadSettings xr = sp.GetRequiredService<XRoadSettings>();

            SocketsHttpHandler handler = CreateSocketsHandler(xr);
            TryAttachClientCertificate(sp, xr, handler);
            ConfigureServerCertificateValidation(sp, handler);
            return handler;
        }

        private static SocketsHttpHandler CreateSocketsHandler(XRoadSettings xr)
        {
            TimeSpan lifetime = xr.Http.PooledConnectionLifetimeSeconds <= 0
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(xr.Http.PooledConnectionLifetimeSeconds);
            TimeSpan idle = xr.Http.PooledConnectionIdleTimeoutSeconds <= 0
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromSeconds(xr.Http.PooledConnectionIdleTimeoutSeconds);
            int maxPerServer = xr.Http.MaxConnectionsPerServer <= 0 ? 20 : xr.Http.MaxConnectionsPerServer;

            return new SocketsHttpHandler
            {
                PooledConnectionLifetime = lifetime,
                PooledConnectionIdleTimeout = idle,
                MaxConnectionsPerServer = maxPerServer,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
        }

        private static void TryAttachClientCertificate(IServiceProvider sp, XRoadSettings xr, SocketsHttpHandler handler)
        {
            ILogger log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadCert");
            IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
            var info = sp.GetService<ICertificateExpiryInfo>() as MutableCertificateExpiryInfo; // may be null in tests

            try
            {
                X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
                handler.SslOptions.ClientCertificates ??= [];
                _ = handler.SslOptions.ClientCertificates.Add(cert);

                int warnDays = 30;
                try
                {
                    string? raw = cfg["XRoad:Certificate:WarnIfExpiresInDays"]; // optional
                    if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 0 && parsed <= 3650)
                    {
                        warnDays = parsed;
                    }
                }
                catch { }

                DateTime nowLocal = DateTime.UtcNow;
                DateTime notAfterUtc = cert.NotAfter.ToUniversalTime();
                double daysRemaining = (notAfterUtc - nowLocal).TotalDays;
                if (daysRemaining < 0)
                {
                    log.LogClientCertExpired(cert.Subject);
                }
                else if (daysRemaining <= warnDays)
                {
                    log.LogClientCertExpiringSoon(cert.Subject, daysRemaining);
                }

                if (info is not null)
                {
                    info.Set(cert.Subject, notAfterUtc, daysRemaining, warnDays);
                }
            }
            catch (Exception ex)
            {
                log.LogClientCertNotConfigured(ex);
                info?.Clear();
            }
        }

        private static void ConfigureServerCertificateValidation(IServiceProvider sp, SocketsHttpHandler handler)
        {
            IConfiguration cfg = sp.GetRequiredService<IConfiguration>();
            IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
            ILogger serverCertLog = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XRoadServerCert");

            string? cerPath = cfg["XRoad:ServerCertificate:Path"] ?? cfg["Http:ServerCertificate:Path"];            using X509Certificate2? serverCer = LoadServerCertificate(cerPath, serverCertLog);

            if (serverCer is not null)
            {
                ApplyPinning(handler, serverCer);
            }
            else
            {
                bool bypass = cfg.GetValue<bool>(key: "Http:BypassServerCertificateValidation", defaultValue: false);
                if (env.IsDevelopment() && bypass)
                {
                    handler.SslOptions.RemoteCertificateValidationCallback = DevCertificateValidation;
                }
            }
        }

        private static X509Certificate2? LoadServerCertificate(string? path, ILogger log)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string p = path;
            if (!Path.IsPathRooted(p))
            {
                string baseDir = AppContext.BaseDirectory;
                string candidate = Path.Combine(baseDir, p);
                if (File.Exists(candidate))
                {
                    p = candidate;
                }
            }
            if (File.Exists(p))
            {
                try { return new X509Certificate2(p); }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to load server certificate from '{Path}'. Certificate pinning disabled.", p);
                    return null;
                }
            }
            else
            {
                log.LogWarning("Server certificate file not found at '{Path}'. Certificate pinning disabled.", p);
                return null;
            }
        }

        private static void ApplyPinning(SocketsHttpHandler handler, X509Certificate2 serverCer)
        {
            string pinnedThumb = (serverCer.Thumbprint ?? string.Empty).Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

            handler.SslOptions.RemoteCertificateValidationCallback = (object _, X509Certificate? presented, X509Chain? chain, SslPolicyErrors errors) =>
            {
                if (presented is null)
                {
                    return false;
                }

                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                X509Certificate2 leaf = presented as X509Certificate2 ?? new X509Certificate2(presented);
                string actual = (leaf.Thumbprint ?? string.Empty).Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (actual.Equals(pinnedThumb, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                using X509Chain custom = new();
                custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                custom.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                custom.ChainPolicy.ExtraStore.Add(serverCer);
                bool ok = custom.Build(leaf);
                if (!ok)
                {
                    return false;
                }
                foreach (var el in custom.ChainElements)
                {
                    if (string.Equals(el.Certificate.Thumbprint, serverCer.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            };
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

            X509Certificate2? toDisposeCert = null;
            X509Chain? toDisposeChain = null;
            try
            {
                X509Certificate2 cert2;
                if (certificate is X509Certificate2 existing)
                {
                    cert2 = existing;
                }
                else
                {
                    toDisposeCert = new X509Certificate2(certificate);
                    cert2 = toDisposeCert;
                }

                if (chain is null)
                {
                    toDisposeChain = new X509Chain();
                    chain = toDisposeChain;
                }

                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
                                                     | X509VerificationFlags.IgnoreEndRevocationUnknown
                                                     | X509VerificationFlags.AllowUnknownCertificateAuthority;
                bool chainOk = chain.Build(cert2);

                return chainOk || errors == SslPolicyErrors.RemoteCertificateNameMismatch;
            }
            finally
            {
                toDisposeChain?.Dispose();
                toDisposeCert?.Dispose();
            }
        }
    }

    // Source-generated high-performance logging extensions
    internal static partial class CertificateLogging
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Client certificate not configured. Proceeding without certificate.")]
        public static partial void LogClientCertNotConfigured(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Client certificate '{Subject}' expires in {DaysRemaining:F1} days. Renew soon to avoid outages.")]
        public static partial void LogClientCertExpiringSoon(this ILogger logger, string Subject, double DaysRemaining);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Client certificate '{Subject}' is expired.")]
        public static partial void LogClientCertExpired(this ILogger logger, string Subject);
    }

    /// <summary>Public interface for exposing certificate expiry info to UI.</summary>
    public interface ICertificateExpiryInfo
    {
        bool HasCertificate { get; }
        string? Subject { get; }
        DateTime? NotAfterUtc { get; }
        double? DaysRemaining { get; }
        int WarnThresholdDays { get; }
        bool IsExpired { get; }
        bool IsExpiringSoon { get; }
    }

    internal sealed class MutableCertificateExpiryInfo : ICertificateExpiryInfo
    {
        private readonly object _gate = new();
        private bool _hasCert;
        private string? _subject;
        private DateTime? _notAfterUtc;
        private double? _daysRemaining;
        private int _warnThreshold;

        public bool HasCertificate => Volatile.Read(ref _hasCert);
        public string? Subject => _subject;
        public DateTime? NotAfterUtc => _notAfterUtc;
        public double? DaysRemaining => _daysRemaining;
        public int WarnThresholdDays => _warnThreshold;
        public bool IsExpired => _daysRemaining.HasValue && _daysRemaining.Value < 0;
        public bool IsExpiringSoon => _daysRemaining.HasValue && _daysRemaining.Value >= 0 && _daysRemaining.Value <= _warnThreshold;

        public void Set(string subject, DateTime notAfterUtc, double daysRemaining, int warnThreshold)
        {
            lock (_gate)
            {
                _subject = subject;
                _notAfterUtc = notAfterUtc;
                _daysRemaining = daysRemaining;
                _warnThreshold = warnThreshold;
                _hasCert = true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _hasCert = false;
                _subject = null;
                _notAfterUtc = null;
                _daysRemaining = null;
                _warnThreshold = 0;
            }
        }
    }

    internal static class CertificateExpiryInfoRegistration
    {
        public static void TryAddCertificateExpiryInfo(this IServiceCollection services)
        {
            bool already = services.Any(d => d.ServiceType == typeof(ICertificateExpiryInfo));
            if (!already)
            {
                services.AddSingleton<MutableCertificateExpiryInfo>();
                services.AddSingleton<ICertificateExpiryInfo>(sp => sp.GetRequiredService<MutableCertificateExpiryInfo>());
            }
        }
    }
}
