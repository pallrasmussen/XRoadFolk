using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Polly;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using XRoadFolkRaw.Lib.Metrics;

namespace XRoadFolkRaw.Lib
{
    public sealed partial class FolkRawClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _disposeHttpClient;
        private readonly ILogger? _log;
        private readonly bool _verbose;
        private readonly int _retryAttempts;
        private readonly int _retryBaseDelayMs;
        private readonly int _retryJitterMs;
        private readonly Polly.Retry.AsyncRetryPolicy _retryPolicy;
        private static readonly Random JitterRandom = Random.Shared;
        private static readonly HashSet<string> SourceHeaders =
            new(["service", "client", "id", "protocolVersion", "userId"], StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, XDocument> _templateCache = new(StringComparer.OrdinalIgnoreCase);
        // Missing template negative cache with TTL to allow later detection of newly added files/resources
        private readonly ConcurrentDictionary<string, DateTimeOffset> _missingTemplates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan MissingTemplateTtl = TimeSpan.FromSeconds(30);

        private FolkRawClient(
            HttpClient httpClient,
            bool disposeHttpClient,
            ILogger? logger,
            bool verbose,
            int retryAttempts,
            int retryBaseDelayMs,
            int retryJitterMs)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeHttpClient = disposeHttpClient;
            _log = logger;
            _verbose = verbose;
            _retryAttempts = retryAttempts;
            _retryBaseDelayMs = retryBaseDelayMs;
            _retryJitterMs = retryJitterMs;
            _retryPolicy = BuildRetryPolicy();
        }

        /// <summary>
        /// Preferred when using IHttpClientFactory (this instance does NOT own the HttpClient).
        /// </summary>
        public FolkRawClient(
            HttpClient httpClient,
            ILogger? logger = null,
            bool verbose = false,
            int retryAttempts = 3,
            int retryBaseDelayMs = 200,
            int retryJitterMs = 250)
            : this(httpClient, disposeHttpClient: false, logger, verbose, retryAttempts, retryBaseDelayMs, retryJitterMs)
        { }

        /// <summary>
        /// Owning constructor. Prefer the IHttpClientFactory-based ctor in ASP.NET Core.
        /// </summary>
        public FolkRawClient(
            string serviceUrl,
            X509Certificate2? clientCertificate = null,
            TimeSpan? timeout = null,
            ILogger? logger = null,
            bool verbose = false,
            int retryAttempts = 3,
            int retryBaseDelayMs = 200,
            int retryJitterMs = 250,
            bool bypassServerCertificateValidation = false)
            : this(CreateOwnedHttpClient(serviceUrl, clientCertificate, timeout, bypassServerCertificateValidation),
                   disposeHttpClient: true,
                   logger,
                   verbose,
                   retryAttempts,
                   retryBaseDelayMs,
                   retryJitterMs)
        { }

        private static HttpClient CreateOwnedHttpClient(string serviceUrl, X509Certificate2? clientCertificate, TimeSpan? timeout, bool bypassServerCertificateValidation)
        {
            ArgumentNullException.ThrowIfNull(serviceUrl);

            HttpClientHandler? handler = null;
            try
            {
                handler = new HttpClientHandler();

                if (bypassServerCertificateValidation)
                {
#pragma warning disable MA0039 // Do not write your own certificate validation method
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#pragma warning restore MA0039
                }

                if (clientCertificate is not null)
                {
                    handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                    _ = handler.ClientCertificates.Add(clientCertificate);
                }

                HttpClient http = new(handler)
                {
                    BaseAddress = new Uri(serviceUrl, UriKind.Absolute),
                    Timeout = timeout ?? TimeSpan.FromSeconds(60),
                };

                handler = null; // ownership transferred to HttpClient
                return http;
            }
            finally
            {
                handler?.Dispose();
            }
        }

        private Polly.Retry.AsyncRetryPolicy BuildRetryPolicy()
        {
            return Policy.Handle<HttpRequestException>()
                         .Or<TaskCanceledException>()
                         .WaitAndRetryAsync(
                             _retryAttempts,
                             attempt => TimeSpan.FromMilliseconds((_retryBaseDelayMs * (1 << (attempt - 1))) + JitterRandom.Next(0, _retryJitterMs)),
                             (ex, ts, attempt, ctx) =>
                             {
                                 if (_log is not null)
                                 {
                                     LogHttpRetryWarning(_log, ex, attempt, ts.TotalMilliseconds);
                                 }
                                 string op = ctx.ContainsKey("op") ? (ctx["op"]?.ToString() ?? "unknown") : "unknown";
                                 XRoadRawMetrics.HttpRetries.Add(1, new KeyValuePair<string, object?>("op", op));
                             });
        }

        public void PreloadTemplates(IEnumerable<string> paths)
        {
            ArgumentNullException.ThrowIfNull(paths);

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    _ = TryLoadTemplate(path, out _);
                }
                catch (Exception ex)
                {
                    if (_log != null)
                    {
                        LogTemplatePreloadFailed(_log, ex, path);
                    }
                }
            }
        }

        private XDocument LoadTemplate(string path)
        {
            if (TryLoadTemplate(path, out XDocument? doc) && doc is not null)
            {
                return doc;
            }
            throw new FileNotFoundException($"{path} not found", path);
        }

        private bool TryLoadTemplate(string path, out XDocument? doc)
        {
            if (_templateCache.TryGetValue(path, out doc))
            {
                return true;
            }

            if (!ShouldProbePath(path))
            {
                doc = null;
                return false;
            }

            if (TryLoadFromFile(path, out doc))
            {
                return true;
            }

            if (TryLoadFromResource(path, out doc))
            {
                return true;
            }

            MarkMissing(path);
            doc = null;
            return false;
        }

        private bool ShouldProbePath(string path)
        {
            if (_missingTemplates.TryGetValue(path, out DateTimeOffset until))
            {
                if (DateTimeOffset.UtcNow < until)
                {
                    return false;
                }
                _ = _missingTemplates.TryRemove(path, out _);
            }
            return true;
        }

        private bool TryLoadFromFile(string path, out XDocument? doc)
        {
            doc = null;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                XDocument loaded = XDocument.Load(path);
                doc = _templateCache.GetOrAdd(path, loaded);
                _ = _missingTemplates.TryRemove(path, out _);
                return true;
            }
            catch (Exception ex)
            {
                DateTimeOffset next = DateTimeOffset.UtcNow + MissingTemplateTtl;
                if (_missingTemplates.TryAdd(path, next) && _log != null)
                {
                    LogTemplatePreloadFailed(_log, ex, path);
                }
                return false;
            }
        }

        private bool TryLoadFromResource(string path, out XDocument? doc)
        {
            doc = null;
            string fileName = Path.GetFileName(path);
            Assembly asm = typeof(FolkRawClient).Assembly;
            string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Resources.{fileName}", StringComparison.OrdinalIgnoreCase));
            if (res is null)
            {
                return false;
            }
            using Stream? s = asm.GetManifestResourceStream(res);
            if (s is null)
            {
                return false;
            }
            XDocument loaded = XDocument.Load(s);
            doc = _templateCache.GetOrAdd(path, loaded);
            _ = _missingTemplates.TryRemove(path, out _);
            return true;
        }

        private void MarkMissing(string path)
        {
            DateTimeOffset untilNext = DateTimeOffset.UtcNow + MissingTemplateTtl;
            if (_missingTemplates.TryAdd(path, untilNext) && _log != null)
            {
                LogTemplatePreloadMissing(_log, path);
            }
        }

        public async Task<string> LoginAsync(
            string loginXmlPath,
            string xId,
            string userId,
            string username,
            string password,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(loginXmlPath);
            ArgumentNullException.ThrowIfNull(xId);
            ArgumentNullException.ThrowIfNull(userId);
            ArgumentNullException.ThrowIfNull(username);
            ArgumentNullException.ThrowIfNull(password);
            ArgumentNullException.ThrowIfNull(protocolVersion);
            ArgumentNullException.ThrowIfNull(clientXRoadInstance);
            ArgumentNullException.ThrowIfNull(clientMemberClass);
            ArgumentNullException.ThrowIfNull(clientMemberCode);
            ArgumentNullException.ThrowIfNull(clientSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceXRoadInstance);
            ArgumentNullException.ThrowIfNull(serviceMemberClass);
            ArgumentNullException.ThrowIfNull(serviceMemberCode);
            ArgumentNullException.ThrowIfNull(serviceSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceCode);
            ArgumentNullException.ThrowIfNull(serviceVersion);

            XDocument doc = new(LoadTemplate(loginXmlPath));
            XElement requestEl = PrepareLoginDocument(doc,
                xId, userId, protocolVersion,
                clientXRoadInstance, clientMemberClass, clientMemberCode, clientSubsystemCode,
                serviceXRoadInstance, serviceMemberClass, serviceMemberCode, serviceSubsystemCode,
                serviceCode, serviceVersion);

            SetChildValue(requestEl, "username", username);
            SetChildValue(requestEl, "password", password);

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "Login", ct).ConfigureAwait(false);
        }

        public async Task<string> GetPeoplePublicInfoAsync(
            string xmlPath,
            string xId,
            string userId,
            string token,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            string? ssn = null,
            string? firstName = null,
            string? lastName = null,
            DateTimeOffset? dateOfBirth = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(xmlPath);
            ArgumentNullException.ThrowIfNull(xId);
            ArgumentNullException.ThrowIfNull(userId);
            ArgumentNullException.ThrowIfNull(token);
            ArgumentNullException.ThrowIfNull(protocolVersion);
            ArgumentNullException.ThrowIfNull(clientXRoadInstance);
            ArgumentNullException.ThrowIfNull(clientMemberClass);
            ArgumentNullException.ThrowIfNull(clientMemberCode);
            ArgumentNullException.ThrowIfNull(clientSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceXRoadInstance);
            ArgumentNullException.ThrowIfNull(serviceMemberClass);
            ArgumentNullException.ThrowIfNull(serviceMemberCode);
            ArgumentNullException.ThrowIfNull(serviceSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceCode);
            ArgumentNullException.ThrowIfNull(serviceVersion);

            XDocument doc = new(LoadTemplate(xmlPath));
            XElement requestBodyEl = PrepareGetPeoplePublicInfoDocument(doc,
                xId, userId, protocolVersion,
                clientXRoadInstance, clientMemberClass, clientMemberCode, clientSubsystemCode,
                serviceXRoadInstance, serviceMemberClass, serviceMemberCode, serviceSubsystemCode,
                serviceCode, serviceVersion,
                token);

            if (!string.IsNullOrWhiteSpace(ssn))
            {
                SetChildValue(requestBodyEl, "SSN", ssn);
            }
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                SetChildValue(requestBodyEl, "FirstName", firstName);
            }
            if (!string.IsNullOrWhiteSpace(lastName))
            {
                SetChildValue(requestBodyEl, "LastName", lastName);
            }
            if (dateOfBirth.HasValue)
            {
                SetChildValue(requestBodyEl, "DateOfBirth", value: dateOfBirth.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "GetPeoplePublicInfo", ct).ConfigureAwait(false);
        }

        private static (XElement Header, XElement Body) RequireHeaderAndBody(XDocument doc)
        {
            XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
            XElement header = doc.Root?.Element(soapenv + "Header") ?? throw new InvalidOperationException("Missing SOAP Header");
            XElement body = doc.Root?.Element(soapenv + "Body") ?? throw new InvalidOperationException("Missing SOAP Body");
            return (header, body);
        }

        private static void CleanTemplateHeader(XElement header)
        {
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace x = "http://x-road.eu/xsd/x-road.xsd";
            header.Elements().Where(e => e.Name.Namespace == x).Remove();
            header.Elements().Where(e => e.Name.Namespace == xro && SourceHeaders.Contains(e.Name.LocalName)).Remove();
        }

        private static void SetCoreHeaderFields(XElement header, string xId, string protocolVersion, string userId)
        {
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace x = "http://x-road.eu/xsd/x-road.xsd";
            SetChildValue(header, xro + "id", xId);
            SetChildValue(header, xro + "protocolVersion", protocolVersion);
            SetChildValue(header, x + "userId", userId);
        }

        private static void EnsureClientHeader(XElement header,
                                               string clientXRoadInstance,
                                               string clientMemberClass,
                                               string clientMemberCode,
                                               string clientSubsystemCode)
        {
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace iden = "http://x-road.eu/xsd/identifiers";
            XElement? clientEl = header.Element(xro + "client");
            if (clientEl == null) { clientEl = new XElement(xro + "client"); header.Add(clientEl); }
            clientEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SUBSYSTEM");
            SetChildValue(clientEl, iden + "xRoadInstance", clientXRoadInstance);
            SetChildValue(clientEl, iden + "memberClass", clientMemberClass);
            SetChildValue(clientEl, iden + "memberCode", clientMemberCode);
            SetChildValue(clientEl, iden + "subsystemCode", clientSubsystemCode);
        }

        private static void EnsureServiceHeader(XElement header,
                                                string serviceXRoadInstance,
                                                string serviceMemberClass,
                                                string serviceMemberCode,
                                                string serviceSubsystemCode,
                                                string? serviceCode,
                                                string? serviceVersion)
        {
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace iden = "http://x-road.eu/xsd/identifiers";
            XElement? serviceEl = header.Element(xro + "service");
            if (serviceEl == null) { serviceEl = new XElement(xro + "service"); header.Add(serviceEl); }
            serviceEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SERVICE");
            SetChildValue(serviceEl, iden + "xRoadInstance", serviceXRoadInstance);
            SetChildValue(serviceEl, iden + "memberClass", serviceMemberClass);
            SetChildValue(serviceEl, iden + "memberCode", serviceMemberCode);
            SetChildValue(serviceEl, iden + "subsystemCode", serviceSubsystemCode);
            if (!string.IsNullOrWhiteSpace(serviceCode))
            {
                SetChildValue(serviceEl, iden + "serviceCode", serviceCode!);
            }
            if (!string.IsNullOrWhiteSpace(serviceVersion))
            {
                SetChildValue(serviceEl, iden + "serviceVersion", serviceVersion!);
            }
        }

        private static XElement PrepareLoginDocument(
            XDocument doc,
            string xId,
            string userId,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion)
        {
            (XElement header, XElement body) = RequireHeaderAndBody(doc);
            CleanTemplateHeader(header);
            SetCoreHeaderFields(header, xId, protocolVersion, userId);
            EnsureClientHeader(header, clientXRoadInstance, clientMemberClass, clientMemberCode, clientSubsystemCode);
            EnsureServiceHeader(header, serviceXRoadInstance, serviceMemberClass, serviceMemberCode, serviceSubsystemCode, serviceCode, serviceVersion);

            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XElement loginReq = body.Element(prod + "Login")?.Element("request")
                ?? throw new InvalidOperationException("Cannot find prod:Login/request in Login.xml");
            return loginReq;
        }

        private static XElement PrepareGetPeoplePublicInfoDocument(
            XDocument doc,
            string xId,
            string userId,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            string token)
        {
            (XElement header, XElement body) = RequireHeaderAndBody(doc);
            CleanTemplateHeader(header);
            SetCoreHeaderFields(header, xId, protocolVersion, userId);
            EnsureClientHeader(header, clientXRoadInstance, clientMemberClass, clientMemberCode, clientSubsystemCode);
            EnsureServiceHeader(header, serviceXRoadInstance, serviceMemberClass, serviceMemberCode, serviceSubsystemCode, serviceCode, serviceVersion);

            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XElement opEl = body.Element(prod + "GetPeoplePublicInfo")
                ?? throw new InvalidOperationException("Cannot find prod:GetPeoplePublicInfo in body");
            XElement requestEl = opEl.Element("request")
                ?? throw new InvalidOperationException("Cannot find request under prod:GetPeoplePublicInfo");
            XElement requestBodyEl = requestEl.Element("requestBody")
                ?? throw new InvalidOperationException("Cannot find requestBody under request");

            XElement? criteriaList = requestBodyEl.Element("ListOfPersonPublicInfoCriteria");
            if (criteriaList == null) { criteriaList = new XElement("ListOfPersonPublicInfoCriteria"); requestBodyEl.Add(criteriaList); }
            criteriaList.RemoveNodes();
            XElement criteria = new("PersonPublicInfoCriteria"); criteriaList.Add(criteria);

            XElement requestHeader = requestEl.Element("requestHeader") ?? new XElement("requestHeader");
            if (requestHeader.Parent == null)
            {
                requestEl.Add(requestHeader);
            }
            SetChildValue(requestHeader, "token", token);

            return criteria;
        }

        private static XElement PrepareGetPersonRequestBody(
            XDocument doc,
            string xId,
            string userId,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            string token)
        {
            (XElement header, XElement body) = RequireHeaderAndBody(doc);
            CleanTemplateHeader(header);
            SetCoreHeaderFields(header, xId, protocolVersion, userId);
            EnsureClientHeader(header, clientXRoadInstance, clientMemberClass, clientMemberCode, clientSubsystemCode);
            EnsureServiceHeader(header, serviceXRoadInstance, serviceMemberClass, serviceMemberCode, serviceSubsystemCode, serviceCode, serviceVersion);

            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XElement opEl = body.Element(prod + "GetPerson")
                ?? throw new InvalidOperationException("Cannot find prod:GetPerson in body");
            XElement requestEl = opEl.Element("request")
                ?? throw new InvalidOperationException("Cannot find request under prod:GetPerson");
            XElement requestBodyEl = requestEl.Element("requestBody")
                ?? throw new InvalidOperationException("Cannot find requestBody under request");

            XElement requestHeader = requestEl.Element("requestHeader") ?? new XElement("requestHeader");
            if (requestHeader.Parent == null)
            {
                requestEl.Add(requestHeader);
            }
            SetChildValue(requestHeader, "token", token);

            return requestBodyEl;
        }

        private static void ApplyGetPersonIdentifiers(XElement requestBodyEl, string? id, string? publicId, string? ssn, string? externalId)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                SetChildValue(requestBodyEl, "Id", id!);
            }
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                SetChildValue(requestBodyEl, "PublicId", publicId!);
            }
            else if (!string.IsNullOrWhiteSpace(ssn))
            {
                SetChildValue(requestBodyEl, "SSN", ssn!);
            }
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                SetChildValue(requestBodyEl, "ExternalId", externalId!);
            }
        }

        private static void ApplyIncludeBooleans(
            XElement requestBodyEl,
            bool? includeAddress,
            bool? includeContact,
            bool? includeBirthDate,
            bool? includeDeathDate,
            bool? includeGender,
            bool? includeMaritalStatus,
            bool? includeCitizenship,
            bool? includeSsnHistory)
        {
            void SetBool(string name, bool? val)
            {
                if (val.HasValue)
                {
                    SetChildValue(requestBodyEl, name, val.Value ? "true" : "false");
                }
            }
            SetBool("IncludeAddress", includeAddress);
            SetBool("IncludeContact", includeContact);
            SetBool("IncludeBirthDate", includeBirthDate);
            SetBool("IncludeDeathDate", includeDeathDate);
            SetBool("IncludeGender", includeGender);
            SetBool("IncludeMaritalStatus", includeMaritalStatus);
            SetBool("IncludeCitizenship", includeCitizenship);
            SetBool("IncludeSsnHistory", includeSsnHistory);
        }

        private static void ApplyIncludeFlags(XElement requestBodyEl, GetPersonInclude inc)
        {
            void SetIf(GetPersonInclude flag, string element)
            {
                if ((inc & flag) == flag)
                {
                    SetChildValue(requestBodyEl, element, "true");
                }
            }

            SetIf(GetPersonInclude.Addresses, "IncludeAddresses");
            SetIf(GetPersonInclude.AddressesHistory, "IncludeAddressesHistory");
            SetIf(GetPersonInclude.BiologicalParents, "IncludeBiologicalParents");
            SetIf(GetPersonInclude.ChurchMembership, "IncludeChurchMembership");
            SetIf(GetPersonInclude.ChurchMembershipHistory, "IncludeChurchMembershipHistory");
            SetIf(GetPersonInclude.Citizenships, "IncludeCitizenships");
            SetIf(GetPersonInclude.CitizenshipsHistory, "IncludeCitizenshipsHistory");
            SetIf(GetPersonInclude.CivilStatus, "IncludeCivilStatus");
            SetIf(GetPersonInclude.CivilStatusHistory, "IncludeCivilStatusHistory");
            SetIf(GetPersonInclude.ForeignSsns, "IncludeForeignSsns");
            SetIf(GetPersonInclude.Incapacity, "IncludeIncapacity");
            SetIf(GetPersonInclude.IncapacityHistory, "IncludeIncapacityHistory");
            SetIf(GetPersonInclude.JuridicalChildren, "IncludeJuridicalChildren");
            SetIf(GetPersonInclude.JuridicalChildrenHistory, "IncludeJuridicalChildrenHistory");
            SetIf(GetPersonInclude.JuridicalParents, "IncludeJuridicalParents");
            SetIf(GetPersonInclude.JuridicalParentsHistory, "IncludeJuridicalParentsHistory");
            SetIf(GetPersonInclude.Names, "IncludeNames");
            SetIf(GetPersonInclude.NamesHistory, "IncludeNamesHistory");
            SetIf(GetPersonInclude.Notes, "IncludeNotes");
            SetIf(GetPersonInclude.NotesHistory, "IncludeNotesHistory");
            SetIf(GetPersonInclude.Postbox, "IncludePostbox");
            SetIf(GetPersonInclude.SpecialMarks, "IncludeSpecialMarks");
            SetIf(GetPersonInclude.SpecialMarksHistory, "IncludeSpecialMarksHistory");
            SetIf(GetPersonInclude.Spouse, "IncludeSpouse");
            SetIf(GetPersonInclude.SpouseHistory, "IncludeSpouseHistory");
            SetIf(GetPersonInclude.Ssn, "IncludeSsn");
            SetIf(GetPersonInclude.SsnHistory, "IncludeSsnHistory");
        }

        public async Task<string> GetPersonAsync(
            string xmlPath,
            string xId,
            string userId,
            string token,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            string? publicId = null,
            string? ssnForPerson = null,
            bool? includeAddress = null,
            bool? includeContact = null,
            bool? includeBirthDate = null,
            bool? includeDeathDate = null,
            bool? includeGender = null,
            bool? includeMaritalStatus = null,
            bool? includeCitizenship = null,
            bool? includeSsnHistory = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(xmlPath);
            ArgumentNullException.ThrowIfNull(xId);
            ArgumentNullException.ThrowIfNull(userId);
            ArgumentNullException.ThrowIfNull(token);
            ArgumentNullException.ThrowIfNull(protocolVersion);
            ArgumentNullException.ThrowIfNull(clientXRoadInstance);
            ArgumentNullException.ThrowIfNull(clientMemberClass);
            ArgumentNullException.ThrowIfNull(clientMemberCode);
            ArgumentNullException.ThrowIfNull(clientSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceXRoadInstance);
            ArgumentNullException.ThrowIfNull(serviceMemberClass);
            ArgumentNullException.ThrowIfNull(serviceMemberCode);
            ArgumentNullException.ThrowIfNull(serviceSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceCode);
            ArgumentNullException.ThrowIfNull(serviceVersion);

            XDocument doc = new(LoadTemplate(xmlPath));
            XElement requestBodyEl = PrepareGetPersonRequestBody(
                doc,
                xId,
                userId,
                protocolVersion,
                clientXRoadInstance,
                clientMemberClass,
                clientMemberCode,
                clientSubsystemCode,
                serviceXRoadInstance,
                serviceMemberClass,
                serviceMemberCode,
                serviceSubsystemCode,
                serviceCode,
                serviceVersion,
                token);

            ApplyGetPersonIdentifiers(requestBodyEl, id: null, publicId: publicId, ssn: ssnForPerson, externalId: null);
            ApplyIncludeBooleans(requestBodyEl, includeAddress, includeContact, includeBirthDate, includeDeathDate, includeGender, includeMaritalStatus, includeCitizenship, includeSsnHistory);

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "GetPerson", ct).ConfigureAwait(false);
        }

        public async Task<string> GetPersonAsync(
            string xmlPath,
            string xId,
            string userId,
            string token,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            GetPersonRequestOptions? options,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(xmlPath);
            ArgumentNullException.ThrowIfNull(xId);
            ArgumentNullException.ThrowIfNull(userId);
            ArgumentNullException.ThrowIfNull(token);
            ArgumentNullException.ThrowIfNull(protocolVersion);
            ArgumentNullException.ThrowIfNull(clientXRoadInstance);
            ArgumentNullException.ThrowIfNull(clientMemberClass);
            ArgumentNullException.ThrowIfNull(clientMemberCode);
            ArgumentNullException.ThrowIfNull(clientSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceXRoadInstance);
            ArgumentNullException.ThrowIfNull(serviceMemberClass);
            ArgumentNullException.ThrowIfNull(serviceMemberCode);
            ArgumentNullException.ThrowIfNull(serviceSubsystemCode);
            ArgumentNullException.ThrowIfNull(serviceCode);
            ArgumentNullException.ThrowIfNull(serviceVersion);

            XDocument doc = new(LoadTemplate(xmlPath));
            XElement requestBodyEl = PrepareGetPersonRequestBody(
                doc,
                xId,
                userId,
                protocolVersion,
                clientXRoadInstance,
                clientMemberClass,
                clientMemberCode,
                clientSubsystemCode,
                serviceXRoadInstance,
                serviceMemberClass,
                serviceMemberCode,
                serviceSubsystemCode,
                serviceCode,
                serviceVersion,
                token);

            if (options is not null)
            {
                ApplyGetPersonIdentifiers(requestBodyEl, options.Id, options.PublicId, options.Ssn, options.ExternalId);
                if (options.Include != GetPersonInclude.None)
                {
                    ApplyIncludeFlags(requestBodyEl, options.Include);
                }
            }

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "GetPerson", ct).ConfigureAwait(false);
        }

        private async Task<string> SendAsync(string xmlString, string opName, CancellationToken ct)
        {
            if (_verbose && _log is not null)
            {
                // Log at Information to ensure visibility with default filters
                _log.SafeSoapInfo(xmlString, $"SOAP Request [{opName}]");
            }

            var sw = ValueStopwatch.StartNew();
            var pollyContext = new Context();
            pollyContext["op"] = opName;
            string respText = await _retryPolicy.ExecuteAsync(async (ctx) =>
            {
                using HttpRequestMessage request = new(HttpMethod.Post, _http.BaseAddress);
                // Inject operation header for policy selection (ignored by server)
                request.Headers.TryAddWithoutValidation("X-XRoad-Operation", opName);
                request.Content = new StringContent(xmlString, Encoding.UTF8, "text/xml");
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return !response.IsSuccessStatusCode
                    ? throw new HttpRequestException(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} failed. HTTP {1} {2}\n{3}",
                        opName,
                        (int)response.StatusCode,
                        response.ReasonPhrase,
                        text))
                    : text;
            }, pollyContext).ConfigureAwait(false);
            var elapsedMs = sw.GetElapsedTime().TotalMilliseconds;
            XRoadRawMetrics.HttpDurationMs.Record(elapsedMs, new KeyValuePair<string, object?>("op", opName));

            if (_verbose && _log is not null)
            {
                _log.SafeSoapInfo(respText, $"SOAP Response [{opName}]");
            }

            return respText;
        }

        private readonly struct ValueStopwatch
        {
            private readonly long _start;
            private ValueStopwatch(long start) => _start = start;
            public static ValueStopwatch StartNew() => new ValueStopwatch(Stopwatch.GetTimestamp());
            public TimeSpan GetElapsedTime()
            {
                long end = Stopwatch.GetTimestamp();
                long freq = Stopwatch.Frequency;
                long ticks = (long)((end - _start) * (TimeSpan.TicksPerSecond / (double)freq));
                return new TimeSpan(ticks);
            }
        }

        private static void SetChildValue(XElement? parent, XName name, string value)
        {
            if (parent == null)
            {
                return;
            }

            XElement? el = parent.Element(name);
            if (el == null) { el = new XElement(name, value); parent.Add(el); }
            else { el.Value = value; }
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
                       Message = "HTTP retry {Attempt} after {Delay}ms")]
        static partial void LogHttpRetryWarning(ILogger logger, Exception ex, int attempt, double delay);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information,
                       Message = "[SOAP request] {Xml}")]
        static partial void LogSoapRequest(ILogger logger, string xml);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information,
                       Message = "[SOAP response] {Xml}")]
        static partial void LogSoapResponse(ILogger logger, string xml);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "SOAP template not found: '{Path}'. Skipping preload.")]
        static partial void LogTemplatePreloadMissing(ILogger logger, string Path);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Failed to preload SOAP template: '{Path}'")]
        static partial void LogTemplatePreloadFailed(ILogger logger, Exception ex, string Path);

        private static (XDocument Doc, XElement RequestBody) PrepareGetPersonDocument(
            XDocument doc,
            string xId,
            string userId,
            string protocolVersion,
            string clientXRoadInstance,
            string clientMemberClass,
            string clientMemberCode,
            string clientSubsystemCode,
            string serviceXRoadInstance,
            string serviceMemberClass,
            string serviceMemberCode,
            string serviceSubsystemCode,
            string serviceCode,
            string serviceVersion,
            string token)
        {
            XElement body = PrepareGetPersonRequestBody(
                doc,
                xId,
                userId,
                protocolVersion,
                clientXRoadInstance,
                clientMemberClass,
                clientMemberCode,
                clientSubsystemCode,
                serviceXRoadInstance,
                serviceMemberClass,
                serviceMemberCode,
                serviceSubsystemCode,
                serviceCode,
                serviceVersion,
                token);
            return (doc, body);
        }

        public async Task<string> GetPeoplePublicInfoAsync(GetPeoplePublicInfoRequest req, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(req.Header);
            string xmlPath = req.XmlPath;
            XRoadHeaderOptions h = req.Header;
            string xId = h.XId; string userId = h.UserId; string protocolVersion = h.ProtocolVersion;
            string clientXRoadInstance = h.ClientXRoadInstance; string clientMemberClass = h.ClientMemberClass; string clientMemberCode = h.ClientMemberCode; string clientSubsystemCode = h.ClientSubsystemCode;
            string serviceXRoadInstance = h.ServiceXRoadInstance; string serviceMemberClass = h.ServiceMemberClass; string serviceMemberCode = h.ServiceMemberCode; string serviceSubsystemCode = h.ServiceSubsystemCode; string serviceCode = h.ServiceCode; string serviceVersion = h.ServiceVersion;

            return await GetPeoplePublicInfoAsync(
                xmlPath: xmlPath,
                xId: xId,
                userId: userId,
                token: req.Token,
                protocolVersion: protocolVersion,
                clientXRoadInstance: clientXRoadInstance,
                clientMemberClass: clientMemberClass,
                clientMemberCode: clientMemberCode,
                clientSubsystemCode: clientSubsystemCode,
                serviceXRoadInstance: serviceXRoadInstance,
                serviceMemberClass: serviceMemberClass,
                serviceMemberCode: serviceMemberCode,
                serviceSubsystemCode: serviceSubsystemCode,
                serviceCode: serviceCode,
                serviceVersion: serviceVersion,
                ssn: req.Ssn,
                firstName: req.FirstName,
                lastName: req.LastName,
                dateOfBirth: req.DateOfBirth,
                ct: ct).ConfigureAwait(false);
        }

        public async Task<string> GetPersonAsync(GetPersonRequest req, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(req.Header);
            string xmlPath = req.XmlPath;
            XRoadHeaderOptions h = req.Header;
            string xId = h.XId; string userId = h.UserId; string protocolVersion = h.ProtocolVersion;
            string clientXRoadInstance = h.ClientXRoadInstance; string clientMemberClass = h.ClientMemberClass; string clientMemberCode = h.ClientMemberCode; string clientSubsystemCode = h.ClientSubsystemCode;
            string serviceXRoadInstance = h.ServiceXRoadInstance; string serviceMemberClass = h.ServiceMemberClass; string serviceMemberCode = h.ServiceMemberCode; string serviceSubsystemCode = h.ServiceSubsystemCode; string serviceCode = h.ServiceCode; string serviceVersion = h.ServiceVersion;

            GetPersonRequestOptions options = new()
            {
                Id = req.Id,
                PublicId = req.PublicId,
                Ssn = req.Ssn,
                ExternalId = req.ExternalId,
                Include = req.Include,
            };

            return await GetPersonAsync(
                xmlPath: xmlPath,
                xId: xId,
                userId: userId,
                token: req.Token,
                protocolVersion: protocolVersion,
                clientXRoadInstance: clientXRoadInstance,
                clientMemberClass: clientMemberClass,
                clientMemberCode: clientMemberCode,
                clientSubsystemCode: clientSubsystemCode,
                serviceXRoadInstance: serviceXRoadInstance,
                serviceMemberClass: serviceMemberClass,
                serviceMemberCode: serviceMemberCode,
                serviceSubsystemCode: serviceSubsystemCode,
                serviceCode: serviceCode,
                serviceVersion: serviceVersion,
                options: options,
                ct: ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposeHttpClient)
            {
                _http.Dispose();
            }
        }
    }
}
