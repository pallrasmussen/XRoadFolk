using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Options;

namespace XRoadFolkRaw.Lib
{

    public sealed partial class PeopleService : IDisposable
    {
        private readonly FolkRawClient _client;
        private readonly IConfiguration _config;
        private readonly XRoadSettings _settings;
        private readonly ILogger _log;
        private readonly IStringLocalizer<PeopleService> _localizer;
        private readonly FolkTokenProviderRaw _tokenProvider;
        private readonly string _loginXmlPath;
        private readonly string _peopleInfoXmlPath;
        private readonly string _personXmlPath;
        private readonly IValidateOptions<GetPersonRequestOptions> _requestValidator;

        public PeopleService(
            FolkRawClient client,
            IConfiguration config,
            XRoadSettings settings,
            ILogger log,
            IStringLocalizer<PeopleService> localizer,
            IValidateOptions<GetPersonRequestOptions> requestValidator)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(localizer);

            _client = client;
            _config = config;
            _settings = settings;
            _log = log;
            _localizer = localizer;
            _requestValidator = requestValidator;

            _loginXmlPath = settings.Raw.LoginXmlPath ?? throw new ArgumentNullException(nameof(settings));
            _peopleInfoXmlPath = _config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
            _personXmlPath = _config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";

            // Preload all XML templates
            _client.PreloadTemplates([_loginXmlPath, _peopleInfoXmlPath, _personXmlPath]);

            _tokenProvider = new FolkTokenProviderRaw(client, ct => client.LoginAsync(
                loginXmlPath: _loginXmlPath,
                xId: Guid.NewGuid().ToString("N"),
                userId: settings.Auth.UserId,
                username: settings.Auth.Username ?? string.Empty,
                password: settings.Auth.Password ?? string.Empty,
                protocolVersion: settings.Headers.ProtocolVersion,
                clientXRoadInstance: settings.Client.XRoadInstance,
                clientMemberClass: settings.Client.MemberClass,
                clientMemberCode: settings.Client.MemberCode,
                clientSubsystemCode: settings.Client.SubsystemCode,
                serviceXRoadInstance: settings.Service.XRoadInstance,
                serviceMemberClass: settings.Service.MemberClass,
                serviceMemberCode: settings.Service.MemberCode,
                serviceSubsystemCode: settings.Service.SubsystemCode,
                serviceCode: settings.Service.ServiceCode,
                serviceVersion: settings.Service.ServiceVersion ?? "v1",
                ct: ct
            ), refreshSkew: TimeSpan.FromSeconds(60));
        }

        private async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            string token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(_localizer["TokenMissing"]);
            }
            LogTokenAcquired(_log, token.Length);
            return token;
        }

        public async Task<string> GetPeoplePublicInfoAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);
            try
            {
                return await _client.GetPeoplePublicInfoAsync(
                    xmlPath: _peopleInfoXmlPath,
                    xId: Guid.NewGuid().ToString("N"),
                    userId: _settings.Auth.UserId,
                    token: token,
                    protocolVersion: _settings.Headers.ProtocolVersion,
                    clientXRoadInstance: _settings.Client.XRoadInstance,
                    clientMemberClass: _settings.Client.MemberClass,
                    clientMemberCode: _settings.Client.MemberCode,
                    clientSubsystemCode: _settings.Client.SubsystemCode,
                    serviceXRoadInstance: _settings.Service.XRoadInstance,
                    serviceMemberClass: _settings.Service.MemberClass,
                    serviceMemberCode: _settings.Service.MemberCode,
                    serviceSubsystemCode: _settings.Service.SubsystemCode,
                    serviceCode: "GetPeoplePublicInfo",
                    serviceVersion: "v1",
                    ssn: ssn,
                    firstName: firstName,
                    lastName: lastName,
                    dateOfBirth: dateOfBirth,
                    ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPeoplePublicInfoError(_log, ex);
                throw new InvalidOperationException(_localizer["PeoplePublicInfoError"], ex);
            }
        }

        public async Task<string> GetPersonAsync(string? publicId, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);

            GetPersonRequestOptions req =
                _config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>()
                ?? new GetPersonRequestOptions();

            // runtime override from caller
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                req.PublicId = publicId;
                req.Ssn = null;
                req.Id = null;
                req.ExternalId = null;
            }

            // Validate after overrides (qualify Options.DefaultName to avoid namespace collision)
            ValidateOptionsResult result = _requestValidator.Validate(Microsoft.Extensions.Options.Options.DefaultName, req);
            if (result.Failed)
            {
                throw new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(GetPersonRequestOptions), result.Failures);
            }

            try
            {
                return await _client.GetPersonAsync(
                    xmlPath: _personXmlPath,
                    xId: Guid.NewGuid().ToString("N"),
                    userId: _settings.Auth.UserId,
                    token: token,
                    protocolVersion: _settings.Headers.ProtocolVersion,
                    clientXRoadInstance: _settings.Client.XRoadInstance,
                    clientMemberClass: _settings.Client.MemberClass,
                    clientMemberCode: _settings.Client.MemberCode,
                    clientSubsystemCode: _settings.Client.SubsystemCode,
                    serviceXRoadInstance: _settings.Service.XRoadInstance,
                    serviceMemberClass: _settings.Service.MemberClass,
                    serviceMemberCode: _settings.Service.MemberCode,
                    serviceSubsystemCode: _settings.Service.SubsystemCode,
                    serviceCode: "GetPerson",
                    serviceVersion: "v1",
                    options: req,
                    ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogGetPersonError(_log, ex);
                throw new InvalidOperationException(_localizer["GetPersonError"], ex);
            }
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Token acquired (len={TokenLength})")]
        static partial void LogTokenAcquired(ILogger logger, int tokenLength);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Error fetching people public info")]
        static partial void LogPeoplePublicInfoError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Error fetching person")]
        static partial void LogGetPersonError(ILogger logger, Exception ex);

        public void Dispose()
        {
            _tokenProvider.Dispose();
        }
    }
}
