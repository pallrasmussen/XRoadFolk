using System;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkWeb.Infrastructure;
using Xunit;

namespace XRoadFolkWeb.Tests;

public class XRoadSettingsValidatorTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private static XRoadSettings ValidSettings() => new()
    {
        BaseUrl = "https://example.invalid/",
        Headers = new HeaderSettings { ProtocolVersion = "4.0" },
        Client = new ClientIdSettings { XRoadInstance = "X", MemberClass = "GOV", MemberCode = "123", SubsystemCode = "Sub" },
        Service = new ServiceIdSettings { XRoadInstance = "X", MemberClass = "GOV", MemberCode = "123", SubsystemCode = "S", ServiceCode = "Get", ServiceVersion = "v1" },
        Auth = new AuthSettings { UserId = "user" },
        Raw = new RawSettings { LoginXmlPath = "Resources/Login.xml" },
        Certificate = new CertificateSettings(),
    };

    [Fact]
    public void Relative_PfxPath_Is_Resolved_From_BaseDirectory()
    {
        // Arrange: create a dummy file under BaseDirectory
        string rel = "client-test.pfx";
        string full = Path.Combine(AppContext.BaseDirectory, rel);
        File.WriteAllBytes(full, new byte[] { 0 });
        try
        {
            var settings = ValidSettings();
            settings.Certificate!.PfxPath = rel; // relative
            var validator = new XRoadSettingsValidator(new Env("Production"));

            // Act
            ValidateOptionsResult res = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, settings);

            // Assert
            Assert.True(res.Succeeded, string.Join(";", res.Failures ?? Array.Empty<string>()));
        }
        finally
        {
            try { File.Delete(full); } catch { }
        }
    }

    [Fact]
    public void Missing_Relative_PfxPath_Returns_FileNotFound_Error()
    {
        var settings = ValidSettings();
        settings.Certificate!.PfxPath = "no-such.pfx";
        var validator = new XRoadSettingsValidator(new Env("Production"));

        ValidateOptionsResult res = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, settings);
        Assert.True(res.Failed);
        Assert.Contains(res.Failures!, s => s.Contains("XRoad:Certificate:PfxPath file not found: 'no-such.pfx'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_Pem_Paths_Return_FileNotFound_Errors()
    {
        var settings = ValidSettings();
        settings.Certificate!.PemCertPath = "missing-cert.pem";
        settings.Certificate!.PemKeyPath = "missing-key.pem";
        var validator = new XRoadSettingsValidator(new Env("Production"));

        ValidateOptionsResult res = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, settings);
        Assert.True(res.Failed);
        Assert.Contains(res.Failures!, s => s.Contains("XRoad:Certificate:PemCertPath file not found: 'missing-cert.pem'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Failures!, s => s.Contains("XRoad:Certificate:PemKeyPath file not found: 'missing-key.pem'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Development_Does_Not_Require_Client_Certificate()
    {
        var settings = ValidSettings();
        // No certificate provided; should be OK in Development
        var validator = new XRoadSettingsValidator(new Env("Development"));
        ValidateOptionsResult res = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, settings);
        Assert.True(res.Succeeded, string.Join(";", res.Failures ?? Array.Empty<string>()));
    }

    [Fact]
    public void Relative_Pem_Paths_Are_Resolved_From_BaseDirectory()
    {
        // Arrange: create dummy cert and key files under BaseDirectory
        string certRel = "client-test-cert.pem";
        string keyRel = "client-test-key.pem";
        string certFull = Path.Combine(AppContext.BaseDirectory, certRel);
        string keyFull = Path.Combine(AppContext.BaseDirectory, keyRel);
        File.WriteAllText(certFull, "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n");
        File.WriteAllText(keyFull, "-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----\n");
        try
        {
            var settings = ValidSettings();
            settings.Certificate!.PemCertPath = certRel;
            settings.Certificate!.PemKeyPath = keyRel;
            var validator = new XRoadSettingsValidator(new Env("Production"));

            // Act
            ValidateOptionsResult res = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, settings);

            // Assert
            Assert.True(res.Succeeded, string.Join(";", res.Failures ?? Array.Empty<string>()));
        }
        finally
        {
            try { File.Delete(certFull); } catch {}
            try { File.Delete(keyFull); } catch {}
        }
    }
}
