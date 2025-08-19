using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XRoad.Config;

namespace XRoadFolkRaw.Lib;

public sealed class ConfigurationLoader
{
    public (IConfigurationRoot Config, XRoadSettings Settings) Load(ILogger log)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        XRoadSettings xr = config.GetSection("XRoad").Get<XRoadSettings>() ?? new();

        string? envBase = Environment.GetEnvironmentVariable("XR_BASE_URL");
        string? envUser = Environment.GetEnvironmentVariable("XR_USER");
        string? envPass = Environment.GetEnvironmentVariable("XR_PASSWORD");
        if (!string.IsNullOrWhiteSpace(envBase))
        {
            xr.BaseUrl = envBase.Trim();
        }

        if (!string.IsNullOrWhiteSpace(envUser))
        {
            xr.Auth ??= new AuthSettings();
            xr.Auth.Username = envUser.Trim();
        }

        if (!string.IsNullOrWhiteSpace(envPass))
        {
            xr.Auth ??= new AuthSettings();
            xr.Auth.Password = envPass;
        }

        List<string> errs = [];
        void Req(bool ok, string msg) { if (!ok) { errs.Add(msg); } }

        Req(!string.IsNullOrWhiteSpace(xr.BaseUrl), "XRoad.BaseUrl is missing.");
        if (!string.IsNullOrWhiteSpace(xr.BaseUrl) && !Uri.TryCreate(xr.BaseUrl, UriKind.Absolute, out _))
        {
            errs.Add($"XRoad.BaseUrl is not a valid absolute URI: {xr.BaseUrl}");
        }

        Req(xr.Headers != null && !string.IsNullOrWhiteSpace(xr.Headers.ProtocolVersion), "XRoad.Headers.ProtocolVersion is missing.");

        Req(xr.Client != null, "XRoad.Client section is missing.");
        Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.XRoadInstance), "XRoad.Client.XRoadInstance is missing.");
        Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.MemberClass), "XRoad.Client.MemberClass is missing.");
        Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.MemberCode), "XRoad.Client.MemberCode is missing.");
        Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.SubsystemCode), "XRoad.Client.SubsystemCode is missing.");

        Req(xr.Service != null, "XRoad.Service section is missing.");
        Req(!string.IsNullOrWhiteSpace(xr.Service.XRoadInstance), "XRoad.Service.XRoadInstance is missing.");
        Req(!string.IsNullOrWhiteSpace(xr.Service.MemberClass), "XRoad.Service.MemberClass is missing.");
        Req(!string.IsNullOrWhiteSpace(xr.Service.MemberCode), "XRoad.Service.MemberCode is missing.");
        Req(!string.IsNullOrWhiteSpace(xr.Service.SubsystemCode), "XRoad.Service.SubsystemCode is missing.");

        Req(xr.Auth != null && !string.IsNullOrWhiteSpace(xr.Auth.UserId), "XRoad.Auth.UserId is missing.");

        string tokenMode = (config.GetValue<string>("XRoad:TokenInsert:Mode") ?? "request").Trim().ToLowerInvariant();
        if (tokenMode is not "request" and not "header")
        {
            errs.Add("XRoad.TokenInsert.Mode must be 'request' or 'header'.");
        }

        string gpPath = config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
        string personPath = config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";
        if (!File.Exists(gpPath))
        {
            errs.Add($"Operations:GetPeoplePublicInfo:XmlPath file not found: {gpPath}");
        }

        if (!File.Exists(personPath))
        {
            errs.Add($"Operations:GetPerson:XmlPath file not found: {personPath}");
        }

        string? pfx = xr.Certificate?.PfxPath;
        string? pemCert = xr.Certificate?.PemCertPath;
        string? pemKey = xr.Certificate?.PemKeyPath;
        bool hasPfx = !string.IsNullOrWhiteSpace(pfx);
        bool hasPem = !string.IsNullOrWhiteSpace(pemCert) || !string.IsNullOrWhiteSpace(pemKey);
        if (!hasPfx && !hasPem)
        {
            errs.Add("Configure a client certificate (PFX or PEM pair).");
        }

        if (hasPfx && !File.Exists(pfx!))
        {
            errs.Add($"PFX file not found: {pfx}");
        }

        if (hasPem)
        {
            if (string.IsNullOrWhiteSpace(pemCert) || string.IsNullOrWhiteSpace(pemKey))
            {
                errs.Add("PEM mode requires both PemCertPath and PemKeyPath.");
            }
            else
            {
                if (!File.Exists(pemCert!))
                {
                    errs.Add($"PEM cert file not found: {pemCert}");
                }

                if (!File.Exists(pemKey!))
                {
                    errs.Add($"PEM key file not found: {pemKey}");
                }
            }
        }

        if (errs.Count > 0)
        {
            log.LogError("? Config sanity check failed:");
            foreach (string e in errs)
            {
                log.LogError(" - {Error}", e);
            }
            throw new InvalidOperationException("Configuration sanity check failed.");
        }

        log.LogInformation("X-Road client:  SUBSYSTEM:{Client}", $"{xr.Client.XRoadInstance}/{xr.Client.MemberClass}/{xr.Client.MemberCode}/{xr.Client.SubsystemCode}");
        log.LogInformation("X-Road service: SUBSYSTEM:{Service}", $"{xr.Service.XRoadInstance}/{xr.Service.MemberClass}/{xr.Service.MemberCode}/{xr.Service.SubsystemCode}");

        return (config, xr);
    }
}
