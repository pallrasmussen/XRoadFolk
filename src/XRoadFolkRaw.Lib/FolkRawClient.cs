using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Polly;

namespace XRoadFolkRaw.Lib;

public sealed partial class FolkRawClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger? _log;
    private readonly bool _verbose;
    private readonly bool _maskTokens;
    private readonly int _retryAttempts;
    private readonly int _retryBaseDelayMs;
    private readonly int _retryJitterMs;
    private static readonly Random JitterRandom = Random.Shared;
    private static readonly HashSet<string> SourceHeaders =
        new(["service", "client", "id", "protocolVersion", "userId"]);
    private readonly ConcurrentDictionary<string, XDocument> _templateCache = new(StringComparer.OrdinalIgnoreCase);

    public FolkRawClient(string serviceUrl, X509Certificate2? clientCertificate = null, TimeSpan? timeout = null, ILogger? logger = null, bool verbose = false, bool maskTokens = true, int retryAttempts = 3, int retryBaseDelayMs = 200, int retryJitterMs = 250)
    {
        ArgumentNullException.ThrowIfNull(serviceUrl);

        //var handler = new HttpClientHandler();

        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
        };

        if (clientCertificate != null)
        {
            handler.ClientCertificates.Add(clientCertificate);
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(serviceUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };
        _log = logger;
        _verbose = verbose;
        _maskTokens = maskTokens;
        _retryAttempts = retryAttempts;
        _retryBaseDelayMs = retryBaseDelayMs;
        _retryJitterMs = retryJitterMs;
    }

    public void PreloadTemplates(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            LoadTemplate(path);
        }
    }

    private XDocument LoadTemplate(string path)
    {
        return _templateCache.GetOrAdd(path, p =>
        {
            if (!File.Exists(p))
            {
                throw new FileNotFoundException($"{p} not found", p);
            }
            return XDocument.Load(p);
        });
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

        XDocument doc = new XDocument(LoadTemplate(loginXmlPath));

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

        XElement loginReq = body?.Element(prod + "Login")?.Element("request")
            ?? throw new InvalidOperationException("Cannot find prod:Login/request in Login.xml");
        SetChildValue(loginReq, "username", username);
        SetChildValue(loginReq, "password", password);

        string xmlString = doc.Declaration != null
            ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
            : doc.ToString(SaveOptions.DisableFormatting);

        return await SendAsync(xmlString, "Login", ct);
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

        XDocument doc = new XDocument(LoadTemplate(xmlPath));

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
        // Clear any existing criteria to avoid mixing with template values
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
            SetChildValue(criteria, "DateOfBirth", dateOfBirth.Value.ToString("yyyy-MM-dd"));
        }

        XElement? requestHeader = requestEl.Element("requestHeader");
        if (requestHeader == null) { requestHeader = new XElement("requestHeader"); requestEl.Add(requestHeader); }
        SetChildValue(requestHeader, "token", token);

        string xmlString = doc.Declaration != null
            ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
            : doc.ToString(SaveOptions.DisableFormatting);

        return await SendAsync(xmlString, "GetPeoplePublicInfo", ct);
    }

    private async Task<string> SendAsync(string xmlString, string opName, CancellationToken ct)
    {
        if (_verbose && _log is not null)
        {
            LogSoapRequest(_log, SoapSanitizer.Scrub(xmlString, _maskTokens));
        }

        Polly.Retry.AsyncRetryPolicy policy = Policy.Handle<HttpRequestException>()
                           .Or<TaskCanceledException>()
                           .WaitAndRetryAsync(_retryAttempts,
                               i => TimeSpan.FromMilliseconds((_retryBaseDelayMs * (1 << (i - 1))) + JitterRandom.Next(0, _retryJitterMs)),
                               (ex, ts, attempt, ctx) => LogHttpRetryWarning(_log!, ex, attempt, ts.TotalMilliseconds));

        string respText = await policy.ExecuteAsync(async () =>
        {
            using HttpRequestMessage request = new(HttpMethod.Post, _http.BaseAddress);
            request.Content = new StringContent(xmlString);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            string text = await response.Content.ReadAsStringAsync(ct);
            return !response.IsSuccessStatusCode
                ? throw new HttpRequestException($"{opName} failed. HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{text}")
                : text;
        });

        if (_verbose && _log is not null)
        {
            LogSoapResponse(_log, SoapSanitizer.Scrub(respText, _maskTokens));
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

        XDocument doc = new XDocument(LoadTemplate(xmlPath));

        XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace xro = "http://x-road.eu/xsd/xroad.xsd";
        XNamespace prod = "http://us-folk-v2.x-road.eu/producer";
        XNamespace iden = "http://x-road.eu/xsd/identifiers";
        XNamespace x = "http://x-road.eu/xsd/x-road.xsd";

        XElement header = doc.Root?.Element(soapenv + "Header") ?? throw new InvalidOperationException("Missing SOAP Header");
        XElement body = doc.Root?.Element(soapenv + "Body") ?? throw new InvalidOperationException("Missing SOAP Body");

        // Remove legacy x:* headers
        header.Elements().Where(e => e.Name.Namespace == x).Remove();

        // Remove controlled xro:* headers and rebuild
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

        // ----- BODY -----
        XElement opEl = body?.Element(prod + "GetPerson")
            ?? throw new InvalidOperationException("Cannot find prod:GetPerson in body");
        XElement requestEl = opEl.Element("request")
            ?? throw new InvalidOperationException("Cannot find request under prod:GetPerson");
        XElement requestBodyEl = requestEl.Element("requestBody")
            ?? throw new InvalidOperationException("Cannot find requestBody under request");

        // Preferred: PublicId; Fallback: SSN if provided
        if (!string.IsNullOrWhiteSpace(publicId))
        {
            SetChildValue(requestBodyEl, "PublicId", publicId);
        }
        else if (!string.IsNullOrWhiteSpace(ssnForPerson))
        {
            SetChildValue(requestBodyEl, "SSN", ssnForPerson);
        }

        // Optional include flags
        void SetBool(string name, bool? val) { if (val.HasValue) { SetChildValue(requestBodyEl, name, val.Value ? "true" : "false"); } }
        SetBool("IncludeAddress", includeAddress);
        SetBool("IncludeContact", includeContact);
        SetBool("IncludeBirthDate", includeBirthDate);
        SetBool("IncludeDeathDate", includeDeathDate);
        SetBool("IncludeGender", includeGender);
        SetBool("IncludeMaritalStatus", includeMaritalStatus);
        SetBool("IncludeCitizenship", includeCitizenship);
        SetBool("IncludeSsnHistory", includeSsnHistory);

        // Token
        XElement? requestHeader = requestEl.Element("requestHeader");
        if (requestHeader == null) { requestHeader = new XElement("requestHeader"); requestEl.Add(requestHeader); }
        SetChildValue(requestHeader, "token", token);

        string xmlString = doc.Declaration != null
            ? doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting)
            : doc.ToString(SaveOptions.DisableFormatting);

        return await SendAsync(xmlString, "GetPerson", ct);
    }


    public void Dispose()
    {
        _http.Dispose();
    }
}
