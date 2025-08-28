using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw.Lib
{

    public sealed partial class ConfigurationLoader
    {
        public static class Messages
        {
            public const string XRoadBaseUrlMissing = nameof(XRoadBaseUrlMissing);
            public const string XRoadBaseUrlInvalidUri = nameof(XRoadBaseUrlInvalidUri);
            public const string XRoadHeadersProtocolVersionMissing = nameof(XRoadHeadersProtocolVersionMissing);
            public const string XRoadClientSectionMissing = nameof(XRoadClientSectionMissing);
            public const string XRoadClientXRoadInstanceMissing = nameof(XRoadClientXRoadInstanceMissing);
            public const string XRoadClientMemberClassMissing = nameof(XRoadClientMemberClassMissing);
            public const string XRoadClientMemberCodeMissing = nameof(XRoadClientMemberCodeMissing);
            public const string XRoadClientSubsystemCodeMissing = nameof(XRoadClientSubsystemCodeMissing);
            public const string XRoadServiceSectionMissing = nameof(XRoadServiceSectionMissing);
            public const string XRoadServiceXRoadInstanceMissing = nameof(XRoadServiceXRoadInstanceMissing);
            public const string XRoadServiceMemberClassMissing = nameof(XRoadServiceMemberClassMissing);
            public const string XRoadServiceMemberCodeMissing = nameof(XRoadServiceMemberCodeMissing);
            public const string XRoadServiceSubsystemCodeMissing = nameof(XRoadServiceSubsystemCodeMissing);
            public const string XRoadAuthUserIdMissing = nameof(XRoadAuthUserIdMissing);
            public const string XRoadTokenInsertModeInvalid = nameof(XRoadTokenInsertModeInvalid);
            public const string OperationsGetPeoplePublicInfoXmlPathNotFound = nameof(OperationsGetPeoplePublicInfoXmlPathNotFound);
            public const string OperationsGetPersonXmlPathNotFound = nameof(OperationsGetPersonXmlPathNotFound);
            public const string ConfigureClientCertificate = nameof(ConfigureClientCertificate);
            public const string PfxFileNotFound = nameof(PfxFileNotFound);
            public const string PemModeRequiresBothPemCertPathAndPemKeyPath = nameof(PemModeRequiresBothPemCertPathAndPemKeyPath);
            public const string PemCertFileNotFound = nameof(PemCertFileNotFound);
            public const string PemKeyFileNotFound = nameof(PemKeyFileNotFound);
            public const string ConfigSanityCheckFailedLog = nameof(ConfigSanityCheckFailedLog);
            public const string ConfigSanityCheckFailedException = nameof(ConfigSanityCheckFailedException);
            public const string XRoadClientSubsystemLog = nameof(XRoadClientSubsystemLog);
            public const string XRoadServiceSubsystemLog = nameof(XRoadServiceSubsystemLog);
        }

        public (IConfigurationRoot Config, XRoadSettings Settings) Load(ILogger log, IStringLocalizer<ConfigurationLoader> loc)
        {
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(loc);
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
            void Req(bool ok, string key, params object[] args)
            {
                if (!ok)
                {
                    errs.Add(loc[key, args]);
                }
            }

            Req(!string.IsNullOrWhiteSpace(xr.BaseUrl), Messages.XRoadBaseUrlMissing);
            if (!string.IsNullOrWhiteSpace(xr.BaseUrl) && !Uri.TryCreate(xr.BaseUrl, UriKind.Absolute, out _))
            {
                errs.Add(loc[Messages.XRoadBaseUrlInvalidUri, xr.BaseUrl]);
            }

            Req(xr.Headers != null && !string.IsNullOrWhiteSpace(xr.Headers.ProtocolVersion), Messages.XRoadHeadersProtocolVersionMissing);

            Req(xr.Client != null, Messages.XRoadClientSectionMissing);
            Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.XRoadInstance), Messages.XRoadClientXRoadInstanceMissing);
            Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.MemberClass), Messages.XRoadClientMemberClassMissing);
            Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.MemberCode), Messages.XRoadClientMemberCodeMissing);
            Req(xr.Client != null && !string.IsNullOrWhiteSpace(xr.Client.SubsystemCode), Messages.XRoadClientSubsystemCodeMissing);

            Req(xr.Service != null, Messages.XRoadServiceSectionMissing);
            Req(!string.IsNullOrWhiteSpace(xr.Service.XRoadInstance), Messages.XRoadServiceXRoadInstanceMissing);
            Req(!string.IsNullOrWhiteSpace(xr.Service.MemberClass), Messages.XRoadServiceMemberClassMissing);
            Req(!string.IsNullOrWhiteSpace(xr.Service.MemberCode), Messages.XRoadServiceMemberCodeMissing);
            Req(!string.IsNullOrWhiteSpace(xr.Service.SubsystemCode), Messages.XRoadServiceSubsystemCodeMissing);

            Req(xr.Auth != null && !string.IsNullOrWhiteSpace(xr.Auth.UserId), Messages.XRoadAuthUserIdMissing);

            string tokenMode = (config.GetValue<string>("XRoad:TokenInsert:Mode") ?? "request").Trim().ToLowerInvariant();
            if (tokenMode is not "request" and not "header")
            {
                errs.Add(loc[Messages.XRoadTokenInsertModeInvalid]);
            }

            string gpPath = config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
            string personPath = config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";
            if (!File.Exists(gpPath))
            {
                errs.Add(loc[Messages.OperationsGetPeoplePublicInfoXmlPathNotFound, gpPath]);
            }

            if (!File.Exists(personPath))
            {
                errs.Add(loc[Messages.OperationsGetPersonXmlPathNotFound, personPath]);
            }

            string? pfx = xr.Certificate?.PfxPath;
            string? pemCert = xr.Certificate?.PemCertPath;
            string? pemKey = xr.Certificate?.PemKeyPath;
            bool hasPfx = !string.IsNullOrWhiteSpace(pfx);
            bool hasPem = !string.IsNullOrWhiteSpace(pemCert) || !string.IsNullOrWhiteSpace(pemKey);
            if (!hasPfx && !hasPem)
            {
                errs.Add(loc[Messages.ConfigureClientCertificate]);
            }

            if (pfx != null && !File.Exists(pfx))
            {
                errs.Add(loc[Messages.PfxFileNotFound, pfx]);
            }

            if (hasPem)
            {
                if (string.IsNullOrWhiteSpace(pemCert) || string.IsNullOrWhiteSpace(pemKey))
                {
                    errs.Add(loc[Messages.PemModeRequiresBothPemCertPathAndPemKeyPath]);
                }
                else
                {
                    if (pemCert != null && !File.Exists(pemCert))
                    {
                        errs.Add(loc[Messages.PemCertFileNotFound, pemCert]);
                    }

                    if (pemKey != null && !File.Exists(pemKey))
                    {
                        errs.Add(loc[Messages.PemKeyFileNotFound, pemKey]);
                    }
                }
            }

            if (errs.Count > 0)
            {
                ConfigSanityCheckFailed(log);
                foreach (string e in errs)
                {
                    ConfigSanityCheckError(log, e);
                }
                throw new InvalidOperationException(loc[Messages.ConfigSanityCheckFailedException]);
            }

            // After validation, ensure required sections are present and use non-null locals
            if (xr.Client is null || xr.Service is null)
            {
                throw new InvalidOperationException("XRoad configuration missing Client or Service section.");
            }
            var client = xr.Client;
            var service = xr.Service;

            ClientSubsystem(log, $"{client.XRoadInstance}/{client.MemberClass}/{client.MemberCode}/{client.SubsystemCode}");
            ServiceSubsystem(log, $"{service.XRoadInstance}/{service.MemberClass}/{service.MemberCode}/{service.SubsystemCode}");

            return (config, xr);
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Config sanity check failed:")]
        static partial void ConfigSanityCheckFailed(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = " - {Error}")]
        static partial void ConfigSanityCheckError(ILogger logger, string error);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "X-Road client:  SUBSYSTEM:{Client}")]
        static partial void ClientSubsystem(ILogger logger, string client);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "X-Road service: SUBSYSTEM:{Service}")]
        static partial void ServiceSubsystem(ILogger logger, string service);
    }
}
