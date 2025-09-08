using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkRaw.Lib;
using Xunit;

namespace XRoadFolkWeb.Tests.Lib;

public class ConfigurationLoaderValidationTests
{
    private sealed class DummyStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => System.Linq.Enumerable.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private static IStringLocalizer<ConfigurationLoader> Loc => new DummyStringLocalizer<ConfigurationLoader>();

    [Fact]
    public void Invalid_BaseUrl_Is_Detected()
    {
        var xr = new XRoadSettings { BaseUrl = "not-a-uri" };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();
        var errs = ConfigurationLoader.ValidateSettings(xr, cfg, Loc, requireClientCertificate: false);
        errs.Should().Contain(ConfigurationLoader.Messages.XRoadBaseUrlInvalidUri);
    }

    [Fact]
    public void Missing_TokenInsert_Mode_Uses_Default()
    {
        var xr = new XRoadSettings { BaseUrl = "https://example" };
        var dict = new Dictionary<string,string?> { { "XRoad:Headers:ProtocolVersion", "4.0" } };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var errs = ConfigurationLoader.ValidateSettings(xr, cfg, Loc, requireClientCertificate: false);
        // No error for missing TokenInsert:Mode (defaults to request)
        errs.Should().NotContain(ConfigurationLoader.Messages.XRoadTokenInsertModeInvalid);
    }

    [Fact]
    public void TokenInsert_Mode_Invalid_Is_Detected()
    {
        var xr = new XRoadSettings { BaseUrl = "https://example" };
        var dict = new Dictionary<string,string?> { { "XRoad:TokenInsert:Mode", "body" }, { "XRoad:Headers:ProtocolVersion", "4.0" } };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var errs = ConfigurationLoader.ValidateSettings(xr, cfg, Loc, requireClientCertificate: false);
        errs.Should().Contain(ConfigurationLoader.Messages.XRoadTokenInsertModeInvalid);
    }

    [Fact]
    public void Pem_Mode_Requires_Both_Paths_When_Either_Set()
    {
        var xr = new XRoadSettings { BaseUrl = "https://example", Headers = new HeaderSettings { ProtocolVersion = "4.0" } };
        xr.Certificate = new CertificateSettings { PemCertPath = "cert.pem", PemKeyPath = null };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();
        var errs = ConfigurationLoader.ValidateSettings(xr, cfg, Loc, requireClientCertificate: false);
        errs.Should().Contain(ConfigurationLoader.Messages.PemModeRequiresBothPemCertPathAndPemKeyPath);
    }

    [Fact]
    public void RequireClientCertificate_Adds_Error_When_None_Configured()
    {
        var xr = new XRoadSettings { BaseUrl = "https://example", Headers = new HeaderSettings { ProtocolVersion = "4.0" } };
        xr.Client = new ClientIdSettings{ XRoadInstance = "x", MemberClass = "mc", MemberCode = "m", SubsystemCode = "s" };
        xr.Service = new ServiceIdSettings{ XRoadInstance = "x", MemberClass = "mc", MemberCode = "m", SubsystemCode = "s" };
        xr.Auth = new AuthSettings { UserId = "u" };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();
        var errs = ConfigurationLoader.ValidateSettings(xr, cfg, Loc, requireClientCertificate: true);
        errs.Should().Contain(ConfigurationLoader.Messages.ConfigureClientCertificate);
    }
}
