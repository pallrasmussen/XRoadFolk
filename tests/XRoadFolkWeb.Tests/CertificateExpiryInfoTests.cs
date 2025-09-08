using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using XRoadFolkRaw.Lib.Extensions;

namespace XRoadFolkWeb.Tests
{
    public class CertificateExpiryInfoTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CertificateExpiryInfoTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        private static string CreateTempPfx(DateTimeOffset notAfter)
        {
            using RSA rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=TestClient", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            // Start slightly in the past to avoid NotBefore being in the future
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var cert = req.CreateSelfSigned(notBefore, notAfter);
            byte[] pfx = cert.Export(X509ContentType.Pkcs12, string.Empty);
            string path = Path.Combine(Path.GetTempPath(), "xr_cert_" + Guid.NewGuid().ToString("N") + ".pfx");
            File.WriteAllBytes(path, pfx);
            return path;
        }

        private WebApplicationFactory<Program> BuildFactoryWithConfig(params (string Key, string? Value)[] pairs)
        {
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["XRoad:BaseUrl"] = "https://example.test/xroad" // ensure AddXRoadHttpClient executes
                    };
                    foreach (var (k,v) in pairs)
                    {
                        dict[k] = v;
                    }
                    cfg.AddInMemoryCollection(dict);
                });
            });
        }

        [Fact]
        public void Missing_Certificate_Info_Reports_No_Certificate()
        {
            var fac = BuildFactoryWithConfig();
            using var scope = fac.Services.CreateScope();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            // Trigger handler construction (will attempt to load cert -> warning -> Clear())
            _ = httpFactory.CreateClient("XRoadFolk");
            var info = scope.ServiceProvider.GetRequiredService<ICertificateExpiryInfo>();
            info.HasCertificate.Should().BeFalse();
            info.IsExpired.Should().BeFalse();
            info.IsExpiringSoon.Should().BeFalse();
        }

        [Fact]
        public void Expired_Certificate_Sets_IsExpired_True()
        {
            string pfxPath = CreateTempPfx(DateTimeOffset.UtcNow.AddDays(-1));
            var fac = BuildFactoryWithConfig(("XRoad:Certificate:PfxPath", pfxPath));
            using var scope = fac.Services.CreateScope();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            _ = httpFactory.CreateClient("XRoadFolk");
            var info = scope.ServiceProvider.GetRequiredService<ICertificateExpiryInfo>();
            info.HasCertificate.Should().BeTrue();
            info.IsExpired.Should().BeTrue();
            info.IsExpiringSoon.Should().BeFalse();
            info.Subject.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void Expiring_Soon_Certificate_Sets_IsExpiringSoon_True()
        {
            // within default 30-day threshold
            string pfxPath = CreateTempPfx(DateTimeOffset.UtcNow.AddDays(5));
            var fac = BuildFactoryWithConfig(("XRoad:Certificate:PfxPath", pfxPath));
            using var scope = fac.Services.CreateScope();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            _ = httpFactory.CreateClient("XRoadFolk");
            var info = scope.ServiceProvider.GetRequiredService<ICertificateExpiryInfo>();
            info.HasCertificate.Should().BeTrue();
            info.IsExpired.Should().BeFalse();
            info.IsExpiringSoon.Should().BeTrue();
            info.DaysRemaining.Should().BeGreaterThan(0);
            info.DaysRemaining.Should().BeLessThanOrEqualTo(30);
        }
    }
}
