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

        public static (IConfigurationRoot Config, XRoadSettings Settings) Load(ILogger log, IStringLocalizer<ConfigurationLoader> loc)
        {
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(loc);
            IConfigurationRoot config = BuildConfiguration();

            XRoadSettings xr = config.GetSection("XRoad").Get<XRoadSettings>() ?? new();
            ApplyEnvOverrides(xr);

            List<string> errs = ValidateSettings(xr, config, loc, requireClientCertificate: true);
            ValidateTemplates(config, log);

            if (errs.Count > 0)
            {
                ReportErrorsAndThrow(log, loc, errs);
            }

            EnsureRequiredSections(xr);
            LogSubsystems(log, xr);
            return (config, xr);
        }

        public static List<string> ValidateSettings(XRoadSettings xr, IConfiguration config, IStringLocalizer<ConfigurationLoader> loc, bool requireClientCertificate)
        {
            List<string> errs = new(capacity: 16);
            ValidateBaseUrl(xr, loc, errs);
            ValidateHeaders(xr, loc, errs);
            ValidateClient(xr, loc, errs);
            ValidateService(xr, loc, errs);
            ValidateAuth(xr, loc, errs);
            ValidateTokenInsert(config, loc, errs);
            ValidateCertificates(xr, loc, errs, requireClientCertificate);
            return errs;
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
        }

        private static void ApplyEnvOverrides(XRoadSettings xr)
        {
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
        }

        private static void ValidateBaseUrl(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            if (string.IsNullOrWhiteSpace(xr.BaseUrl))
            {
                errs.Add(loc[Messages.XRoadBaseUrlMissing]);
            }
            else if (!Uri.TryCreate(xr.BaseUrl, UriKind.Absolute, out _))
            {
                errs.Add(loc[Messages.XRoadBaseUrlInvalidUri, xr.BaseUrl]);
            }
        }

        private static void ValidateHeaders(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            HeaderSettings headers = xr.Headers;
            if (headers == null || string.IsNullOrWhiteSpace(headers.ProtocolVersion))
            {
                errs.Add(loc[Messages.XRoadHeadersProtocolVersionMissing]);
            }
        }

        private static void ValidateClient(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            ClientIdSettings? cli = xr.Client;
            if (cli == null)
            {
                errs.Add(loc[Messages.XRoadClientSectionMissing]);
            }
            if (cli == null || string.IsNullOrWhiteSpace(cli.XRoadInstance))
            {
                errs.Add(loc[Messages.XRoadClientXRoadInstanceMissing]);
            }
            if (cli == null || string.IsNullOrWhiteSpace(cli.MemberClass))
            {
                errs.Add(loc[Messages.XRoadClientMemberClassMissing]);
            }
            if (cli == null || string.IsNullOrWhiteSpace(cli.MemberCode))
            {
                errs.Add(loc[Messages.XRoadClientMemberCodeMissing]);
            }
            if (cli == null || string.IsNullOrWhiteSpace(cli.SubsystemCode))
            {
                errs.Add(loc[Messages.XRoadClientSubsystemCodeMissing]);
            }
        }

        private static void ValidateService(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            ServiceIdSettings? svc = xr.Service;
            if (svc == null)
            {
                errs.Add(loc[Messages.XRoadServiceSectionMissing]);
            }
            if (svc == null || string.IsNullOrWhiteSpace(svc.XRoadInstance))
            {
                errs.Add(loc[Messages.XRoadServiceXRoadInstanceMissing]);
            }
            if (svc == null || string.IsNullOrWhiteSpace(svc.MemberClass))
            {
                errs.Add(loc[Messages.XRoadServiceMemberClassMissing]);
            }
            if (svc == null || string.IsNullOrWhiteSpace(svc.MemberCode))
            {
                errs.Add(loc[Messages.XRoadServiceMemberCodeMissing]);
            }
            if (svc == null || string.IsNullOrWhiteSpace(svc.SubsystemCode))
            {
                errs.Add(loc[Messages.XRoadServiceSubsystemCodeMissing]);
            }
        }

        private static void ValidateAuth(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            AuthSettings auth = xr.Auth;
            if (auth == null || string.IsNullOrWhiteSpace(auth.UserId))
            {
                errs.Add(loc[Messages.XRoadAuthUserIdMissing]);
            }
        }

        private static void ValidateTokenInsert(IConfiguration config, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            string tokenModeRaw = (config.GetValue<string>("XRoad:TokenInsert:Mode") ?? "request").Trim();
            if (!string.Equals(tokenModeRaw, "request", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tokenModeRaw, "header", StringComparison.OrdinalIgnoreCase))
            {
                errs.Add(loc[Messages.XRoadTokenInsertModeInvalid]);
            }
        }

        private static void ValidateTemplates(IConfiguration config, ILogger log)
        {
            string gpPath = config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
            string personPath = config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";
            if (!File.Exists(gpPath))
            {
                MissingPeopleInfoTemplate(log, gpPath);
            }
            if (!File.Exists(personPath))
            {
                MissingPersonTemplate(log, personPath);
            }
        }

        private static void ValidateCertificates(XRoadSettings xr, IStringLocalizer<ConfigurationLoader> loc, List<string> errs, bool requireClientCertificate)
        {
            string? pfx = xr.Certificate?.PfxPath;
            string? pemCert = xr.Certificate?.PemCertPath;
            string? pemKey = xr.Certificate?.PemKeyPath;
            bool hasPfx = !string.IsNullOrWhiteSpace(pfx);
            bool hasPemCert = !string.IsNullOrWhiteSpace(pemCert);
            bool hasPemKey = !string.IsNullOrWhiteSpace(pemKey);
            bool hasPem = hasPemCert || hasPemKey;

            if (requireClientCertificate && !hasPfx && !(hasPemCert && hasPemKey))
            {
                errs.Add(loc[Messages.ConfigureClientCertificate]);
            }

            // Structural validation for PEM mode: both required if any specified
            if (hasPem && !(hasPemCert && hasPemKey))
            {
                errs.Add(loc[Messages.PemModeRequiresBothPemCertPathAndPemKeyPath]);
            }

            // Note: File existence checks are host-specific and resolved by the web validator (which knows ContentRootPath).
        }

        private static void ReportErrorsAndThrow(ILogger log, IStringLocalizer<ConfigurationLoader> loc, List<string> errs)
        {
            ConfigSanityCheckFailed(log);
            foreach (string e in errs)
            {
                ConfigSanityCheckError(log, e);
            }
            string header = loc[Messages.ConfigSanityCheckFailedException];
            string details = string.Join(Environment.NewLine + " - ", errs);
            string message = header + Environment.NewLine + " - " + details;
            throw new InvalidOperationException(message);
        }

        private static void EnsureRequiredSections(XRoadSettings xr)
        {
            if (xr.Client is null || xr.Service is null)
            {
                throw new InvalidOperationException("XRoad configuration missing Client or Service section.");
            }
        }

        private static void LogSubsystems(ILogger log, XRoadSettings xr)
        {
            ClientIdSettings client = xr.Client!;
            ServiceIdSettings service = xr.Service!;
            ClientSubsystem(log, $"{client.XRoadInstance}/{client.MemberClass}/{client.MemberCode}/{client.SubsystemCode}");
            ServiceSubsystem(log, $"{service.XRoadInstance}/{service.MemberClass}/{service.MemberCode}/{service.SubsystemCode}");
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Config sanity check failed:")]
        static partial void ConfigSanityCheckFailed(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = " - {Error}")]
        static partial void ConfigSanityCheckError(ILogger logger, string error);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "X-Road client:  SUBSYSTEM:{Client}")]
        static partial void ClientSubsystem(ILogger logger, string client);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "X-Road service: SUBSYSTEM:{Service}")]
        static partial void ServiceSubsystem(ILogger logger, string service);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "GetPeoplePublicInfo XML path not found: '{Path}'. Using fallback template.")]
        static partial void MissingPeopleInfoTemplate(ILogger logger, string path);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "GetPerson XML path not found: '{Path}'. Using fallback template.")]
        static partial void MissingPersonTemplate(ILogger logger, string path);
    }
}
