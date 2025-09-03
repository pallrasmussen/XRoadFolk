using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Polly;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using System.Reflection;
using System.Text;

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
        private readonly ConcurrentDictionary<string, byte> _missingTemplates = new(StringComparer.OrdinalIgnoreCase);

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
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
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
                             (ex, ts, attempt, _) =>
                             {
                                 if (_log is not null)
                                 {
                                     LogHttpRetryWarning(_log, ex, attempt, ts.TotalMilliseconds);
                                 }
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
                    // Only unexpected failures reach here; missing files are handled in TryLoadTemplate with one-time log
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
            if (_missingTemplates.ContainsKey(path))
            {
                doc = null;
                return false;
            }

            // Try filesystem
            if (File.Exists(path))
            {
                try
                {
                    XDocument loaded = XDocument.Load(path);
                    doc = _templateCache.GetOrAdd(path, loaded);
                    return true;
                }
                catch (Exception ex)
                {
                    // Cache negative to avoid repeated IO on corrupted files too
                    _missingTemplates.TryAdd(path, 0);
                    if (_log != null)
                    {
                        LogTemplatePreloadFailed(_log, ex, path);
                    }
                    doc = null;
                    return false;
                }
            }

            // Try embedded resource
            string fileName = Path.GetFileName(path);
            Assembly asm = typeof(FolkRawClient).Assembly;
            string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Resources.{fileName}", StringComparison.OrdinalIgnoreCase));
            if (res is not null)
            {
                using Stream? s = asm.GetManifestResourceStream(res);
                if (s is not null)
                {
                    XDocument loaded = XDocument.Load(s);
                    doc = _templateCache.GetOrAdd(path, loaded);
                    return true;
                }
            }

            // Record missing once and log; subsequent calls hit the cache and skip IO
            if (_missingTemplates.TryAdd(path, 0) && _log != null)
            {
                LogTemplatePreloadMissing(_log, path);
            }
            doc = null;
            return false;
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

            XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XNamespace iden = "http://x-road.eu/xsd/identifiers";
            XNamespace x = "http://x-road.eu/xsd/x-road.xsd";

            XElement? header = doc.Root?.Element(soapenv + "Header");
            XElement? body = doc.Root?.Element(soapenv + "Body");

            SetChildValue(header, xro + "id", xId);
            SetChildValue(header, xro + "protocolVersion", protocolVersion);
            SetChildValue(header, x + "userId", userId);

            XElement? clientEl = header?.Element(xro + "client");
            if (clientEl == null) { clientEl = new XElement(xro + "client"); header?.Add(clientEl); }
            clientEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SUBSYSTEM");
            SetChildValue(clientEl, iden + "xRoadInstance", clientXRoadInstance);
            SetChildValue(clientEl, iden + "memberClass", clientMemberClass);
            SetChildValue(clientEl, iden + "memberCode", clientMemberCode);
            SetChildValue(clientEl, iden + "subsystemCode", clientSubsystemCode);

            XElement? serviceEl = header?.Element(xro + "service");
            if (serviceEl == null) { serviceEl = new XElement(xro + "service"); header?.Add(serviceEl); }
            serviceEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SERVICE");
            SetChildValue(serviceEl, iden + "xRoadInstance", serviceXRoadInstance);
            SetChildValue(serviceEl, iden + "memberClass", serviceMemberClass);
            SetChildValue(serviceEl, iden + "memberCode", serviceMemberCode);
            SetChildValue(serviceEl, iden + "subsystemCode", serviceSubsystemCode);

            XElement loginReq = body?.Element(prod + "Login")?.Element("request")
                ?? throw new InvalidOperationException("Cannot find prod:Login/request in Login.xml");
            SetChildValue(loginReq, "username", username);
            SetChildValue(loginReq, "password", password);

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

            XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XNamespace iden = "http://x-road.eu/xsd/identifiers";
            XNamespace x = "http://x-road.eu/xsd/x-road.xsd";

            XElement? header = doc.Root?.Element(soapenv + "Header");
            XElement? body = doc.Root?.Element(soapenv + "Body");

            SetChildValue(header, xro + "id", xId);
            SetChildValue(header, xro + "protocolVersion", protocolVersion);
            SetChildValue(header, x + "userId", userId);

            XElement? clientEl = header?.Element(xro + "client");
            if (clientEl == null) { clientEl = new XElement(xro + "client"); header?.Add(clientEl); }
            clientEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SUBSYSTEM");
            SetChildValue(clientEl, iden + "xRoadInstance", clientXRoadInstance);
            SetChildValue(clientEl, iden + "memberClass", clientMemberClass);
            SetChildValue(clientEl, iden + "memberCode", clientMemberCode);
            SetChildValue(clientEl, iden + "subsystemCode", clientSubsystemCode);

            XElement? serviceEl = header?.Element(xro + "service");
            if (serviceEl == null) { serviceEl = new XElement(xro + "service"); header?.Add(serviceEl); }
            serviceEl.SetAttributeValue(XName.Get("objectType", iden.NamespaceName), "SERVICE");
            SetChildValue(serviceEl, iden + "xRoadInstance", serviceXRoadInstance);
            SetChildValue(serviceEl, iden + "memberClass", serviceMemberClass);
            SetChildValue(serviceEl, iden + "memberCode", serviceMemberCode);
            SetChildValue(serviceEl, iden + "subsystemCode", serviceSubsystemCode);
            SetChildValue(serviceEl, iden + "serviceCode", serviceCode);
            SetChildValue(serviceEl, iden + "serviceVersion", serviceVersion);

            XElement opEl = body?.Element(prod + "GetPeoplePublicInfo")
                ?? throw new InvalidOperationException("Cannot find prod:GetPeoplePublicInfo in body");
            XElement requestEl = opEl.Element("request")
                ?? throw new InvalidOperationException("Cannot find request under prod:GetPeoplePublicInfo");
            XElement requestBodyEl = requestEl.Element("requestBody")
                ?? throw new InvalidOperationException("Cannot find requestBody under request");
            XElement? criteriaList = requestBodyEl.Element("ListOfPersonPublicInfoCriteria");
            if (criteriaList == null) { criteriaList = new XElement("ListOfPersonPublicInfoCriteria"); requestBodyEl.Add(criteriaList); }
            criteriaList.RemoveNodes();

            XElement criteria = new("PersonPublicInfoCriteria"); criteriaList.Add(criteria);
            if (!string.IsNullOrWhiteSpace(ssn))
            {
                SetChildValue(criteria, "SSN", ssn);
            }
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                SetChildValue(criteria, "FirstName", firstName);
            }
            if (!string.IsNullOrWhiteSpace(lastName))
            {
                SetChildValue(criteria, "LastName", lastName);
            }
            if (dateOfBirth.HasValue)
            {
                SetChildValue(criteria, "DateOfBirth", value: dateOfBirth.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }

            XElement? requestHeader = requestEl.Element("requestHeader");
            if (requestHeader == null) { requestHeader = new XElement("requestHeader"); requestEl.Add(requestHeader); }
            SetChildValue(requestHeader, "token", token);

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "GetPeoplePublicInfo", ct).ConfigureAwait(false);
        }

        private async Task<string> SendAsync(string xmlString, string opName, CancellationToken ct)
        {
            if (_verbose && _log is not null)
            {
                // Log at Information to ensure visibility with default filters
                _log.SafeSoapInfo(xmlString, $"SOAP Request [{opName}]");
            }

            string respText = await _retryPolicy.ExecuteAsync(async () =>
            {
                using HttpRequestMessage request = new(HttpMethod.Post, _http.BaseAddress);
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
            }).ConfigureAwait(false);

            if (_verbose && _log is not null)
            {
                _log.SafeSoapInfo(respText, $"SOAP Response [{opName}]");
            }

            return respText;
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
            XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
            XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
            XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
            XNamespace iden = "http://x-road.eu/xsd/identifiers";
            XNamespace x = "http://x-road.eu/xsd/x-road.xsd";

            XElement header = doc.Root?.Element(soapenv + "Header") ?? throw new InvalidOperationException("Missing SOAP Header");
            XElement body = doc.Root?.Element(soapenv + "Body") ?? throw new InvalidOperationException("Missing SOAP Body");

            // Clean templated service/client entries in template header namespaces
            header.Elements().Where(e => e.Name.Namespace == x).Remove();
            header.Elements().Where(e => e.Name.Namespace == xro && SourceHeaders.Contains(e.Name.LocalName)).Remove();

            XElement serviceEl = new(xro + "service", new XAttribute(XName.Get("objectType", iden.NamespaceName), "SERVICE"));
            header.Add(serviceEl);
            SetChildValue(serviceEl, iden + "xRoadInstance", serviceXRoadInstance);
            SetChildValue(serviceEl, iden + "memberClass", serviceMemberClass);
            SetChildValue(serviceEl, iden + "memberCode", serviceMemberCode);
            SetChildValue(serviceEl, iden + "subsystemCode", serviceSubsystemCode);
            SetChildValue(serviceEl, iden + "serviceCode", serviceCode);
            SetChildValue(serviceEl, iden + "serviceVersion", serviceVersion);

            XElement clientEl = new(xro + "client", new XAttribute(XName.Get("objectType", iden.NamespaceName), "SUBSYSTEM"));
            header.Add(clientEl);
            SetChildValue(clientEl, iden + "xRoadInstance", clientXRoadInstance);
            SetChildValue(clientEl, iden + "memberClass", clientMemberClass);
            SetChildValue(clientEl, iden + "memberCode", clientMemberCode);
            SetChildValue(clientEl, iden + "subsystemCode", clientSubsystemCode);

            SetChildValue(header, xro + "id", xId);
            SetChildValue(header, xro + "protocolVersion", protocolVersion);
            SetChildValue(header, x + "userId", userId);

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

            return (doc, requestBodyEl);
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
            (_, XElement RequestBody) = PrepareGetPersonDocument(
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

            XElement requestBodyEl = RequestBody;

            if (!string.IsNullOrWhiteSpace(publicId))
            {
                SetChildValue(requestBodyEl, "PublicId", publicId);
            }
            else if (!string.IsNullOrWhiteSpace(ssnForPerson))
            {
                SetChildValue(requestBodyEl, "SSN", ssnForPerson);
            }

            void SetBool(string name, bool? val) { if (val.HasValue) { SetChildValue(requestBodyEl, name, val.Value ? "true" : "false"); } }
            SetBool("IncludeAddress", includeAddress);
            SetBool("IncludeContact", includeContact);
            SetBool("IncludeBirthDate", includeBirthDate);
            SetBool("IncludeDeathDate", includeDeathDate);
            SetBool("IncludeGender", includeGender);
            SetBool("IncludeMaritalStatus", includeMaritalStatus);
            SetBool("IncludeCitizenship", includeCitizenship);
            SetBool("IncludeSsnHistory", includeSsnHistory);

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
            (_, XElement RequestBody) = PrepareGetPersonDocument(
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

            XElement requestBodyEl = RequestBody;

            if (!string.IsNullOrWhiteSpace(options?.Id))
            {
                SetChildValue(requestBodyEl, "Id", options!.Id!);
            }

            if (!string.IsNullOrWhiteSpace(options?.PublicId))
            {
                SetChildValue(requestBodyEl, "PublicId", options!.PublicId!);
            }
            else if (!string.IsNullOrWhiteSpace(options?.Ssn))
            {
                SetChildValue(requestBodyEl, "SSN", options!.Ssn!);
            }

            if (!string.IsNullOrWhiteSpace(options?.ExternalId))
            {
                SetChildValue(requestBodyEl, "ExternalId", options!.ExternalId!);
            }

            if (options is not null && options.Include != GetPersonInclude.None)
            {
                GetPersonInclude inc = options.Include;
                void SetIf(GetPersonInclude flag, string element) { if ((inc & flag) == flag) { SetChildValue(requestBodyEl, element, "true"); } }

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

            string xmlString = doc.Declaration != null
                ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
                : doc.ToString(SaveOptions.DisableFormatting);

            return await SendAsync(xmlString, "GetPerson", ct).ConfigureAwait(false);
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
