using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib.Options;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using Polly.Timeout;

namespace XRoadFolkRaw.Lib
{
    /// <summary>
    /// High-level service that authenticates against X-Road and executes Folk operations
    /// (GetPeoplePublicInfo, GetPerson). Handles token acquisition and caching, request validation,
    /// and error handling with localized messages.
    /// </summary>
    public sealed partial class PeopleService : IDisposable
    {
        private static string EscapePart(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            ReadOnlySpan<char> src = s.AsSpan();
            int extra = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '\\' || c == '|')
                {
                    extra++;
                }
            }

            if (extra == 0)
            {
                return s;
            }

            return string.Create(src.Length + extra, s, static (dest, state) =>
            {
                ReadOnlySpan<char> span = state.AsSpan();
                int j = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    char c = span[i];
                    if (c == '\\' || c == '|')
                    {
                        dest[j++] = '\\';
                    }
                    j++;
                    dest[j - 1] = c;
                }
            });
        }

        private static string HashSegment(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
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
        private readonly ILogger<PeopleService> _log;
        private readonly IStringLocalizer<PeopleService> _localizer;
        private readonly FolkTokenProviderRaw _tokenProvider;
        private readonly string _loginXmlPath;
        private readonly string _peopleInfoXmlPath;
        private readonly string _personXmlPath;
        private readonly IValidateOptions<GetPersonRequestOptions> _requestValidator;
        private readonly IMemoryCache _cache;
        private readonly TokenCacheOptions _cacheOptions;
        private readonly SemaphoreSlim _tokenGate = new(1, 1);

        /// <summary>
        /// Creates a new PeopleService.
        /// </summary>
        /// <param name="client">Low-level SOAP client.</param>
        /// <param name="config">App configuration source.</param>
        /// <param name="settings">Strongly typed X-Road settings (auth, client, service, headers).</param>
        /// <param name="log">Logger instance.</param>
        /// <param name="localizer">Localizer for user-facing error messages.</param>
        /// <param name="requestValidator">Validator for <see cref="GetPersonRequestOptions"/>.</param>
        /// <param name="cache">In-memory cache for token storage.</param>
        /// <param name="cacheOptions">Optional token cache options.</param>
        public PeopleService(
            FolkRawClient client,
            IConfiguration config,
            XRoadSettings settings,
            ILogger<PeopleService> log,
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

            string? loginPath = settings.Raw.LoginXmlPath;
            if (string.IsNullOrWhiteSpace(loginPath))
            {
                throw new ArgumentException("XRoadSettings.Raw.LoginXmlPath must be configured (key: 'XRoad:Raw:LoginXmlPath').", nameof(settings));
            }
            _loginXmlPath = loginPath;

            string? peopleInfoPathCfg = _config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath");
            _peopleInfoXmlPath = string.IsNullOrWhiteSpace(peopleInfoPathCfg) ? "GetPeoplePublicInfo.xml" : peopleInfoPathCfg;

            string? personPathCfg = _config.GetValue<string>("Operations:GetPerson:XmlPath");
            _personXmlPath = string.IsNullOrWhiteSpace(personPathCfg) ? "GetPerson.xml" : personPathCfg;

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

        // Helpers extracted to reduce method length and improve readability
        private XRoadHeaderOptions BuildHeader(string serviceCode, string serviceVersion = "v1") => new()
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
            ServiceCode = serviceCode,
            ServiceVersion = serviceVersion,
        };

        private GetPersonRequestOptions LoadGetPersonOptions(string? publicId)
        {
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

            return reqOptions;
        }

        private void ValidateRequestOptions(GetPersonRequestOptions options)
        {
            ValidateOptionsResult result = _requestValidator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
            if (result.Failed)
            {
                throw new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(GetPersonRequestOptions), result.Failures);
            }
        }

        private GetPersonRequest BuildGetPersonRequest(string token, GetPersonRequestOptions reqOptions) => new()
        {
            XmlPath = _personXmlPath,
            Token = token,
            Header = BuildHeader("GetPerson", "v1"),
            // identifiers
            Id = reqOptions.Id,
            PublicId = reqOptions.PublicId,
            Ssn = reqOptions.Ssn,
            ExternalId = reqOptions.ExternalId,
            // include
            Include = reqOptions.Include,
        };

        private GetPeoplePublicInfoRequest BuildPeoplePublicInfoRequest(string token, string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth) => new()
        {
            XmlPath = _peopleInfoXmlPath,
            Token = token,
            Header = BuildHeader("GetPeoplePublicInfo", "v1"),
            Ssn = ssn,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
        };

        /// <summary>
        /// Calls the GetPeoplePublicInfo operation with optional criteria and returns the raw SOAP response XML.
        /// </summary>
        public async Task<string> GetPeoplePublicInfoAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);
            try
            {
                GetPeoplePublicInfoRequest req = BuildPeoplePublicInfoRequest(token, ssn, firstName, lastName, dateOfBirth);
                return await _client.GetPeoplePublicInfoAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // preserve cancellation
            }
            catch (HttpRequestException ex)
            {
                LogPeoplePublicInfoError(_log, ex);
                throw; // preserve original HttpRequestException (StatusCode, message)
            }
            catch (IOException ex)
            {
                LogPeoplePublicInfoError(_log, ex);
                throw new IOException(_localizer["PeoplePublicInfoError"], ex);
            }
        }

        /// <summary>
        /// Calls the GetPerson operation using configuration/defaults and an optional public identifier.
        /// </summary>
        public async Task<string> GetPersonAsync(string? publicId, CancellationToken ct = default)
        {
            string token = await GetTokenAsync(ct).ConfigureAwait(false);

            GetPersonRequestOptions reqOptions = LoadGetPersonOptions(publicId);
            ValidateRequestOptions(reqOptions);

            try
            {
                GetPersonRequest req = BuildGetPersonRequest(token, reqOptions);
                return await _client.GetPersonAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // preserve cancellation
            }
            catch (HttpRequestException ex)
            {
                LogGetPersonError(_log, ex);
                throw; // preserve original HttpRequestException (StatusCode, message)
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

        /// <summary>
        /// Disposes underlying token provider and synchronization primitives.
        /// </summary>
        public void Dispose()
        {
            _tokenProvider.Dispose();
            _tokenGate.Dispose();
        }
    }
}
