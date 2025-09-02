using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Options;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace XRoadFolkRaw.Lib
{
    public sealed partial class PeopleService : IDisposable
    {
        private static string EscapePart(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Backslash-escape the separator and backslashes to ensure uniqueness
            return s.Replace("\\", "\\\\").Replace("|", "\\|");
        }

        private static string HashSegment(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            byte[] data = Encoding.UTF8.GetBytes(s);
            byte[] hash = SHA256.HashData(data);
            return Convert.ToHexString(hash); // uppercase hex, safe in keys
        }

        private static string ComputeKey(XRoadSettings s)
        {
            // Hash PII-sensitive segments (UserId, Username) to avoid leaking identifiers in cache keys
            string userIdHash = HashSegment(s.Auth.UserId);
            string usernameHash = HashSegment(s.Auth.Username);

            return string.Join('|',
                        EscapePart(s.BaseUrl),
                        EscapePart(s.Client.XRoadInstance), EscapePart(s.Client.MemberClass), EscapePart(s.Client.MemberCode), EscapePart(s.Client.SubsystemCode),
                        EscapePart(s.Service.XRoadInstance), EscapePart(s.Service.MemberClass), EscapePart(s.Service.MemberCode), EscapePart(s.Service.SubsystemCode),
                        EscapePart(s.Service.ServiceCode), EscapePart(s.Service.ServiceVersion),
                        EscapePart(userIdHash), EscapePart(usernameHash));
        }

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
        private readonly IMemoryCache _cache;
        private readonly TokenCacheOptions _cacheOptions;
        private readonly SemaphoreSlim _tokenGate = new(1, 1);

        public PeopleService(
            FolkRawClient client,
            IConfiguration config,
            XRoadSettings settings,
            ILogger log,
            IStringLocalizer<PeopleService> localizer,
            IValidateOptions<GetPersonRequestOptions> requestValidator,
            IMemoryCache cache,
            IOptions<TokenCacheOptions>? cacheOptions = null)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(log);
            ArgumentNullException.ThrowIfNull(localizer);
            ArgumentNullException.ThrowIfNull(cache);

            _client = client;
            _config = config;
            _settings = settings;
            _log = log;
            _localizer = localizer;
            _requestValidator = requestValidator;
            _cache = cache;
            _cacheOptions = cacheOptions?.Value ?? new TokenCacheOptions();

            _loginXmlPath = settings.Raw.LoginXmlPath ?? throw new ArgumentNullException(nameof(settings));
            _peopleInfoXmlPath = _config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
            _personXmlPath = _config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";

            // Preload all XML templates
            _client.PreloadTemplates([_loginXmlPath, _peopleInfoXmlPath, _personXmlPath]);

            TimeSpan refreshSkew = TimeSpan.FromSeconds(Math.Max(0, _cacheOptions.RefreshSkewSeconds));
            _tokenProvider = new FolkTokenProviderRaw(
                ct => client.LoginAsync(
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
                ),
                refreshSkew: refreshSkew);
        }

        private async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            string key = (_cacheOptions.KeyPrefix ?? "folk-token|") + ComputeKey(_settings);

            if (_cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            {
                LogTokenReused(_log);
                return cached;
            }

            await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check under lock
                if (_cache.TryGetValue(key, out cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    LogTokenReused(_log);
                    return cached;
                }

                string token = await _cache.GetOrCreateAsync(key, async entry =>
                {
                    (string Token, DateTimeOffset ExpiresUtc) = await _tokenProvider.GetTokenWithExpiryAsync(ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(Token))
                    {
                        throw new InvalidOperationException(_localizer["TokenMissing"]);
                    }

                    TimeSpan defaultTtl = TimeSpan.FromSeconds(Math.Max(1, _cacheOptions.DefaultTtlSeconds));
                    entry.AbsoluteExpirationRelativeToNow = ExpiresUtc > DateTimeOffset.UtcNow
                        ? ExpiresUtc - DateTimeOffset.UtcNow
                        : defaultTtl;
                    LogTokenAcquired(_log, Token.Length);
                    return Token;
                }).ConfigureAwait(false) ?? string.Empty;

                return string.IsNullOrWhiteSpace(token) ? throw new InvalidOperationException(_localizer["TokenMissing"]) : token;
            }
            finally
            {
                _ = _tokenGate.Release();
            }
        }

        public async Task<string> GetPeoplePublicInfoAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);
            try
            {
                // Build request object for the new overload
                GetPeoplePublicInfoRequest req = new()
                {
                    XmlPath = _peopleInfoXmlPath,
                    Token = token,
                    Header = new XRoadHeaderOptions
                    {
                        XId = Guid.NewGuid().ToString("N"),
                        UserId = _settings.Auth.UserId,
                        ProtocolVersion = _settings.Headers.ProtocolVersion,
                        ClientXRoadInstance = _settings.Client.XRoadInstance,
                        ClientMemberClass = _settings.Client.MemberClass,
                        ClientMemberCode = _settings.Client.MemberCode,
                        ClientSubsystemCode = _settings.Client.SubsystemCode,
                        ServiceXRoadInstance = _settings.Service.XRoadInstance,
                        ServiceMemberClass = _settings.Service.MemberClass,
                        ServiceMemberCode = _settings.Service.MemberCode,
                        ServiceSubsystemCode = _settings.Service.SubsystemCode,
                        ServiceCode = "GetPeoplePublicInfo",
                        ServiceVersion = "v1",
                    },
                    Ssn = ssn,
                    FirstName = firstName,
                    LastName = lastName,
                    DateOfBirth = dateOfBirth,
                };

                return await _client.GetPeoplePublicInfoAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // preserve cancellation
            }
            catch (HttpRequestException ex)
            {
                LogPeoplePublicInfoError(_log, ex);
                throw new HttpRequestException(_localizer["PeoplePublicInfoError"], ex);
            }
            catch (IOException ex)
            {
                LogPeoplePublicInfoError(_log, ex);
                throw new IOException(_localizer["PeoplePublicInfoError"], ex);
            }
        }

        public async Task<string> GetPersonAsync(string? publicId, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);

            GetPersonRequestOptions reqOptions =
                _config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>()
                ?? new GetPersonRequestOptions();

            // Map Include booleans from configuration to flags (if provided)
            GetPersonIncludeOptions? inc = _config.GetSection("Operations:GetPerson:Request:Include").Get<GetPersonIncludeOptions>();
            if (inc is not null)
            {
                reqOptions.Include = ToFlags(inc);
            }

            // runtime override from caller
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                reqOptions.PublicId = publicId;
                reqOptions.Ssn = null;
                reqOptions.Id = null;
                reqOptions.ExternalId = null;
            }

            // Validate after overrides (qualify Options.DefaultName to avoid namespace collision)
            ValidateOptionsResult result = _requestValidator.Validate(Microsoft.Extensions.Options.Options.DefaultName, reqOptions);
            if (result.Failed)
            {
                throw new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(GetPersonRequestOptions), result.Failures);
            }

            try
            {
                // Build request object for the new overload
                GetPersonRequest req = new()
                {
                    XmlPath = _personXmlPath,
                    Token = token,
                    Header = new XRoadHeaderOptions
                    {
                        XId = Guid.NewGuid().ToString("N"),
                        UserId = _settings.Auth.UserId,
                        ProtocolVersion = _settings.Headers.ProtocolVersion,
                        ClientXRoadInstance = _settings.Client.XRoadInstance,
                        ClientMemberClass = _settings.Client.MemberClass,
                        ClientMemberCode = _settings.Client.MemberCode,
                        ClientSubsystemCode = _settings.Client.SubsystemCode,
                        ServiceXRoadInstance = _settings.Service.XRoadInstance,
                        ServiceMemberClass = _settings.Service.MemberClass,
                        ServiceMemberCode = _settings.Service.MemberCode,
                        ServiceSubsystemCode = _settings.Service.SubsystemCode,
                        ServiceCode = "GetPerson",
                        ServiceVersion = "v1",
                    },
                    // identifiers
                    Id = reqOptions.Id,
                    PublicId = reqOptions.PublicId,
                    Ssn = reqOptions.Ssn,
                    ExternalId = reqOptions.ExternalId,
                    // include
                    Include = reqOptions.Include,
                };

                return await _client.GetPersonAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // preserve cancellation
            }
            catch (HttpRequestException ex)
            {
                LogGetPersonError(_log, ex);
                throw new HttpRequestException(_localizer["GetPersonError"], ex);
            }
            catch (IOException ex)
            {
                LogGetPersonError(_log, ex);
                throw new IOException(_localizer["GetPersonError"], ex);
            }
        }

        private static GetPersonInclude ToFlags(GetPersonIncludeOptions o)
        {
            GetPersonInclude inc = GetPersonInclude.None;
            void Set(bool v, GetPersonInclude f) { if (v) { inc |= f; } }
            Set(o.Addresses, GetPersonInclude.Addresses);
            Set(o.AddressesHistory, GetPersonInclude.AddressesHistory);
            Set(o.BiologicalParents, GetPersonInclude.BiologicalParents);
            Set(o.ChurchMembership, GetPersonInclude.ChurchMembership);
            Set(o.ChurchMembershipHistory, GetPersonInclude.ChurchMembershipHistory);
            Set(o.Citizenships, GetPersonInclude.Citizenships);
            Set(o.CitizenshipsHistory, GetPersonInclude.CitizenshipsHistory);
            Set(o.CivilStatus, GetPersonInclude.CivilStatus);
            Set(o.CivilStatusHistory, GetPersonInclude.CivilStatusHistory);
            Set(o.ForeignSsns, GetPersonInclude.ForeignSsns);
            Set(o.Incapacity, GetPersonInclude.Incapacity);
            Set(o.IncapacityHistory, GetPersonInclude.IncapacityHistory);
            Set(o.JuridicalChildren, GetPersonInclude.JuridicalChildren);
            Set(o.JuridicalChildrenHistory, GetPersonInclude.JuridicalChildrenHistory);
            Set(o.JuridicalParents, GetPersonInclude.JuridicalParents);
            Set(o.JuridicalParentsHistory, GetPersonInclude.JuridicalParentsHistory);
            Set(o.Names, GetPersonInclude.Names);
            Set(o.NamesHistory, GetPersonInclude.NamesHistory);
            Set(o.Notes, GetPersonInclude.Notes);
            Set(o.NotesHistory, GetPersonInclude.NotesHistory);
            Set(o.Postbox, GetPersonInclude.Postbox);
            Set(o.SpecialMarks, GetPersonInclude.SpecialMarks);
            Set(o.SpecialMarksHistory, GetPersonInclude.SpecialMarksHistory);
            Set(o.Spouse, GetPersonInclude.Spouse);
            Set(o.SpouseHistory, GetPersonInclude.SpouseHistory);
            Set(o.Ssn, GetPersonInclude.Ssn);
            Set(o.SsnHistory, GetPersonInclude.SsnHistory);
            return inc;
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Token acquired (len={TokenLength})")]
        static partial void LogTokenAcquired(ILogger logger, int tokenLength);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Error fetching people public info")]
        static partial void LogPeoplePublicInfoError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Error fetching person")]
        static partial void LogGetPersonError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Token reused")]
        static partial void LogTokenReused(ILogger logger);

        public void Dispose()
        {
            _tokenProvider.Dispose();
            _tokenGate.Dispose();
        }
    }
}
