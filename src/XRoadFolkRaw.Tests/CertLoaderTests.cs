using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XRoad.Config;
using XRoadFolkRaw.Lib;
using Xunit;

public class CertLoaderTests
{
    private static (string pfx, string cert, string key, string subject) CreateTestCertificate(string subjectCn)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest req = new($"CN={subjectCn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        string pfxPath = Path.GetTempFileName();
        string certPath = Path.GetTempFileName();
        string keyPath = Path.GetTempFileName();

        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pkcs12));

        string certPem = PemEncoding.Write("CERTIFICATE", cert.Export(X509ContentType.Cert));
        string keyPem = PemEncoding.Write("PRIVATE KEY", cert.GetRSAPrivateKey()!.ExportPkcs8PrivateKey());
        File.WriteAllText(certPath, certPem);
        File.WriteAllText(keyPath, keyPem);

        return (pfxPath, certPath, keyPath, cert.Subject);
    }

    [Fact]
    public void EnvironmentPfxOverridesConfig()
    {
        var cfgCert = CreateTestCertificate("ConfigPfx");
        var envCert = CreateTestCertificate("EnvPfx");

        CertificateSettings cfg = new() { PfxPath = cfgCert.pfx };

        try
        {
            Environment.SetEnvironmentVariable("XR_PFX_PATH", envCert.pfx);
            X509Certificate2 loaded = CertLoader.LoadFromConfig(cfg);
            Assert.Equal(envCert.subject, loaded.Subject);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XR_PFX_PATH", null);
            Environment.SetEnvironmentVariable("XR_PFX_PASSWORD", null);
        }
    }

    [Fact]
    public void EnvironmentPemOverridesConfig()
    {
        var cfgCert = CreateTestCertificate("ConfigPfx");
        var envCert = CreateTestCertificate("EnvPem");

        CertificateSettings cfg = new() { PfxPath = cfgCert.pfx };

        try
        {
            Environment.SetEnvironmentVariable("XR_PEM_CERT_PATH", envCert.cert);
            Environment.SetEnvironmentVariable("XR_PEM_KEY_PATH", envCert.key);
            X509Certificate2 loaded = CertLoader.LoadFromConfig(cfg);
            Assert.Equal(envCert.subject, loaded.Subject);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XR_PEM_CERT_PATH", null);
            Environment.SetEnvironmentVariable("XR_PEM_KEY_PATH", null);
        }
    }

    [Fact]
    public void LoadFromPfxThrowsWhenMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<FileNotFoundException>(() => CertLoader.LoadFromPfx(missing));
    }

    [Fact]
    public void LoadFromPemThrowsWhenCertMissing()
    {
        string certPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string keyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<FileNotFoundException>(() => CertLoader.LoadFromPem(certPath, keyPath));
    }

    [Fact]
    public void LoadFromPemThrowsWhenKeyMissing()
    {
        string certPath = Path.GetTempFileName();
        string missingKey = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<FileNotFoundException>(() => CertLoader.LoadFromPem(certPath, missingKey));
    }

    [Fact]
    public void LoadFromConfigThrowsWhenPemPairIncomplete()
    {
        string certPath = Path.GetTempFileName();
        CertificateSettings cfg = new() { PemCertPath = certPath };
        Assert.Throws<InvalidOperationException>(() => CertLoader.LoadFromConfig(cfg));
    }
}

