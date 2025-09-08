using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;
using XRoadFolkRaw.Lib.Extensions;

namespace XRoadFolkWeb.Tests
{
    public class CertificateExpiryThresholdTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        public CertificateExpiryThresholdTests(WebApplicationFactory<Program> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        private static string CreatePfx(DateTimeOffset notAfter)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=ThresholdTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), notAfter);
            var pfx = cert.Export(X509ContentType.Pkcs12, string.Empty);
            string path = Path.Combine(Path.GetTempPath(), "xrf_thr_" + Guid.NewGuid().ToString("N") + ".pfx");
            File.WriteAllBytes(path, pfx);
            return path;
        }

        [Fact]
        public void Custom_Threshold_Respected()
        {
            string pfx = CreatePfx(DateTimeOffset.UtcNow.AddDays(40));
            var fac = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string,string?>
                    {
                        ["XRoad:BaseUrl"] = "https://example/xroad",
                        ["XRoad:Certificate:PfxPath"] = pfx,
                        ["XRoad:Certificate:WarnIfExpiresInDays"] = "60"
                    });
                });
            });
            using var scope = fac.Services.CreateScope();
            var httpFactory = scope.ServiceProvider.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            _ = httpFactory.CreateClient("XRoadFolk");
            var info = scope.ServiceProvider.GetRequiredService<ICertificateExpiryInfo>();
            info.HasCertificate.Should().BeTrue();
            info.IsExpiringSoon.Should().BeTrue(); // 40 < 60
            info.DaysRemaining.Should().BeGreaterThan(30);
        }
    }
}
