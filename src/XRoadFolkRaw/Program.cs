using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XRoad.Config;

using XRoadFolkRaw;
using XRoadFolkRaw.Lib; // for InputValidation + LoggingHelper

// Top-level program
// Build configuration
IConfigurationRoot config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Logger
using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
{
    _ = b.AddConsole();
    _ = b.SetMinimumLevel(LogLevel.Information);
    _ = b.AddFilter("Microsoft", LogLevel.Warning);
});
ILogger log = loggerFactory.CreateLogger("XRoadFolkRaw");
using IDisposable _corr = LoggingHelper.BeginCorrelationScope(log);

// Startup banner
Console.WriteLine("Press Ctrl+Q at any time to quit.\n");


// Read X-Road settings
XRoadSettings xr = config.GetSection("XRoad").Get<XRoadSettings>()!;

// Environment overrides (non-breaking): XR_BASE_URL, XR_USER, XR_PASSWORD
string? _envBase = Environment.GetEnvironmentVariable("XR_BASE_URL");
string? _envUser = Environment.GetEnvironmentVariable("XR_USER");
string? _envPass = Environment.GetEnvironmentVariable("XR_PASSWORD");
if (!string.IsNullOrWhiteSpace(_envBase))
{
    xr.BaseUrl = _envBase.Trim();
}

if (!string.IsNullOrWhiteSpace(_envUser))
{
    xr.Auth ??= new AuthSettings();

    xr.Auth.Username = _envUser.Trim();
}
if (!string.IsNullOrWhiteSpace(_envPass))
{
    xr.Auth ??= new AuthSettings();

    xr.Auth.Password = _envPass;
}


// --- Sanity checks (fail fast) ---
{
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

    // Certificate presence
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
        Console.WriteLine("? Config sanity check failed:");
        foreach (string e in errs)
        {
            Console.WriteLine(" - " + e);
        }

        Environment.ExitCode = 1;
        return;
    }

    log.LogInformation("X-Road client:  SUBSYSTEM:{Client}", $"{xr.Client.XRoadInstance}/{xr.Client.MemberClass}/{xr.Client.MemberCode}/{xr.Client.SubsystemCode}");
    log.LogInformation("X-Road service: SUBSYSTEM:{Service}", $"{xr.Service.XRoadInstance}/{xr.Service.MemberClass}/{xr.Service.MemberCode}/{xr.Service.SubsystemCode}");
}
// --- End sanity checks ---

// Load certificate (project contains CertLoader)
System.Security.Cryptography.X509Certificates.X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
log.LogInformation("[cert] Using {Subject} thumbprint {Thumbprint}", cert.Subject, cert.Thumbprint);

// Create raw client
bool verbose = config.GetValue("Logging:Verbose", false);
bool maskTokens = config.GetValue("Logging:MaskTokens", true);
int httpAttempts = config.GetValue("Retry:Http:Attempts", 3);
int httpBaseDelay = config.GetValue("Retry:Http:BaseDelayMs", 200);
int httpJitter = config.GetValue("Retry:Http:JitterMs", 250);

using (FolkRawClient client = new(
    xr.BaseUrl, cert, TimeSpan.FromSeconds(xr.Http.TimeoutSeconds),
    logger: log, verbose: verbose, maskTokens: maskTokens,
    retryAttempts: httpAttempts, retryBaseDelayMs: httpBaseDelay, retryJitterMs: httpJitter))
{
    // Token provider (project contains FolkTokenProviderRaw + LoginAsync on client)
    FolkTokenProviderRaw tokenProvider = new(client, ct => client.LoginAsync(
        loginXmlPath: xr.Raw.LoginXmlPath,
        xId: Guid.NewGuid().ToString("N"),
        userId: xr.Auth.UserId,
        username: xr.Auth.Username ?? string.Empty,
        password: xr.Auth.Password ?? string.Empty,
        protocolVersion: xr.Headers.ProtocolVersion,
        clientXRoadInstance: xr.Client.XRoadInstance,
        clientMemberClass: xr.Client.MemberClass,
        clientMemberCode: xr.Client.MemberCode,
        clientSubsystemCode: xr.Client.SubsystemCode,
        serviceXRoadInstance: xr.Service.XRoadInstance,
        serviceMemberClass: xr.Service.MemberClass,
        serviceMemberCode: xr.Service.MemberCode,
        serviceSubsystemCode: xr.Service.SubsystemCode,
        serviceCode: xr.Service.ServiceCode,
        serviceVersion: xr.Service.ServiceVersion ?? "v1",
        ct: ct
    ), refreshSkew: TimeSpan.FromSeconds(60));

    string token = await tokenProvider.GetTokenAsync();
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("Token provider returned null/empty token.");
    }

    log.LogInformation("Token acquired (len={Len})", token.Length);

    // === Inputs banner ===
    Console.WriteLine();
    Console.WriteLine("==============================================");
    Console.WriteLine("INPUTS MODE: Strict");
    Console.WriteLine("Provide: SSN  OR  FirstName + LastName + DateOfBirth.");
    Console.WriteLine("Example: FirstName=Anna, LastName=Olsen, DateOfBirth=1990-05-01");
    Console.WriteLine("Type 'q' at any prompt to quit.");
    Console.WriteLine("==============================================");

    // === Prompt loop ===
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Enter GetPeoplePublicInfo search criteria (leave blank to skip):");
        string ssnInput = Prompt("SSN");
        if (IsQuit(ssnInput))
        {
            break;
        }

        string fnInput = Prompt("FirstName");
        if (IsQuit(fnInput))
        {
            break;
        }

        string lnInput = Prompt("LastName");
        if (IsQuit(lnInput))
        {
            break;
        }

        string dobInput = Prompt("DateOfBirth (YYYY-MM-DD)");
        if (IsQuit(dobInput))
        {
            break;
        }

        (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = InputValidation.ValidateCriteria(ssnInput, fnInput, lnInput, dobInput);
        if (!Ok)
        {
            Console.WriteLine("? Input not valid:");
            foreach (string e in Errors)
            {
                Console.WriteLine(" - " + e);
            }

            Console.WriteLine("Please try again.");
            continue;
        }

        // Echo what will be used
        if (!string.IsNullOrWhiteSpace(SsnNorm))
        {
            Console.WriteLine($"→ Using SSN = {MaskSsn(SsnNorm)}");
        }
        else
        {
            Console.WriteLine($"→ Using Name = \"{fnInput} {lnInput}\", DOB = {Dob:yyyy-MM-dd}");
        }

        // Call GetPeoplePublicInfo
        string opXml = config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";


        string responseXml = await client.GetPeoplePublicInfoAsync(
            xmlPath: opXml,
            xId: Guid.NewGuid().ToString("N"),
            userId: xr.Auth.UserId,
            token: token!,
            protocolVersion: xr.Headers.ProtocolVersion,
            clientXRoadInstance: xr.Client.XRoadInstance,
            clientMemberClass: xr.Client.MemberClass,
            clientMemberCode: xr.Client.MemberCode,
            clientSubsystemCode: xr.Client.SubsystemCode,
            serviceXRoadInstance: xr.Service.XRoadInstance,
            serviceMemberClass: xr.Service.MemberClass,
            serviceMemberCode: xr.Service.MemberCode,
            serviceSubsystemCode: xr.Service.SubsystemCode,
            serviceCode: "GetPeoplePublicInfo",
            serviceVersion: "v1",
            ssn: SsnNorm,
            firstName: fnInput,
            lastName: lnInput,
            dateOfBirth: Dob
        );



        if (string.IsNullOrWhiteSpace(responseXml))
        {
            Console.WriteLine("No response received. Please try again.");
            continue;
        }






        XDocument listDoc;
        try { listDoc = XDocument.Parse(responseXml); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to parse GetPeoplePublicInfo response; please try again.");
            continue;
        }

        List<XElement> people = [.. listDoc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];
        if (people.Count == 0)
        {
            Console.WriteLine("No matches found. Please refine inputs.");
            continue;
        }

        // Print table
        PrintPeoplePublicInfoTable(listDoc, log, maxRows: config.GetValue("Output:MaxPeopleToPrint", 25));

        // Chain: GetPerson for each
        string personXmlPath = config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";
        int maxChain = Math.Max(1, config.GetValue("Chaining:MaxPersons", 25));
        int count = 0;
        foreach (XElement p in people)
        {
            if (++count > maxChain)
            {
                break;
            }

            string? Get(string ln)
            {
                return p.Elements().FirstOrDefault(x => x.Name.LocalName == ln)?.Value?.Trim();
            }

            string? publicId = Get("PublicId") ?? Get("PersonId");
            string? personSsn = Get("SSN");
            string? fn = Get("FirstName");
            string? ln = Get("LastName");
            string? dob = Get("DateOfBirth") ?? Get("BirthDate");

            Console.WriteLine();
            Console.WriteLine($"=== Fetching GetPerson for {publicId ?? personSsn ?? (fn + " " + ln)} {(string.IsNullOrWhiteSpace(dob) ? "" : " DOB:" + dob)} ===");

            try
            {
                string personResp = await client.GetPersonAsync(
                    xmlPath: personXmlPath,
                    xId: Guid.NewGuid().ToString("N"),
                    userId: xr.Auth.UserId,
                    token: token!,
                    protocolVersion: xr.Headers.ProtocolVersion,
                    clientXRoadInstance: xr.Client.XRoadInstance,
                    clientMemberClass: xr.Client.MemberClass,
                    clientMemberCode: xr.Client.MemberCode,
                    clientSubsystemCode: xr.Client.SubsystemCode,
                    serviceXRoadInstance: xr.Service.XRoadInstance,
                    serviceMemberClass: xr.Service.MemberClass,
                    serviceMemberCode: xr.Service.MemberCode,
                    serviceSubsystemCode: xr.Service.SubsystemCode,
                    serviceCode: "GetPerson",
                    serviceVersion: "v1",
                    publicId: publicId,
                    includeAddress: config.GetValue("GetPerson:Include:Address", true),
                    includeContact: config.GetValue("GetPerson:Include:Contact", true),
                    includeBirthDate: config.GetValue("GetPerson:Include:BirthDate", true),
                    includeDeathDate: config.GetValue("GetPerson:Include:DeathDate", false),
                    includeGender: config.GetValue("GetPerson:Include:Gender", true),
                    includeMaritalStatus: config.GetValue("GetPerson:Include:MaritalStatus", false),
                    includeCitizenship: config.GetValue("GetPerson:Include:Citizenship", true),
                    includeSsnHistory: config.GetValue("GetPerson:Include:SsnHistory", false)
                );

                if (!string.IsNullOrWhiteSpace(personResp))
                {
                    XDocument doc = XDocument.Parse(personResp);
                    PrintGetPersonAllPairs(doc, log);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to fetch/print GetPerson for the selected entry.");
            }
        }

        // End after one cycle
        break;
    }
}

// ===== Helpers =====
static bool IsQuit(string? s)
{
    return string.Equals(s?.Trim(), "q", StringComparison.OrdinalIgnoreCase);
}

static string Prompt(string label)
{
    Console.Write($"{label}: ");
    string? input = Console.ReadLine();
    return string.IsNullOrWhiteSpace(input) ? "" : input.Trim();
}

static string MaskSsn(string? ssn)
{
    if (string.IsNullOrWhiteSpace(ssn))
    {
        return "";
    }

    string digits = new([.. ssn.Where(char.IsDigit)]);
    return digits.Length <= 3
        ? new string('*', digits.Length)
        : string.Concat(new string('*', digits.Length - 3), digits.AsSpan(digits.Length - 3));
}

static void PrintPair(string key, string? value)
{
    Console.WriteLine($"{key} : \"{value ?? ""}\"");
}

static void PrintSeparator(string title = "")
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 60));
    if (!string.IsNullOrWhiteSpace(title))
    {
        Console.WriteLine(title);
    }

    Console.WriteLine(new string('=', 60));
}

static void PrintAllPairs(XElement el, string path = "")
{
    List<XElement> children = [.. el.Elements()];
    if (children.Count == 0)
    {
        string? v = el.Value?.Trim();
        if (!string.IsNullOrEmpty(v))
        {
            string key = string.IsNullOrEmpty(path) ? el.Name.LocalName : path;
            PrintPair(key, v);
        }
        Environment.ExitCode = 1;
        return;
    }

    IEnumerable<IGrouping<string, XElement>> byName = children.GroupBy(c => c.Name.LocalName);
    foreach (IGrouping<string, XElement> grp in byName)
    {
        if (grp.Count() == 1)
        {
            XElement child = grp.First();
            string next = string.IsNullOrEmpty(path) ? grp.Key : (path + "." + grp.Key);
            PrintAllPairs(child, next);
        }
        else
        {
            int idx = 0;
            foreach (XElement? child in grp)
            {
                string next = string.IsNullOrEmpty(path) ? $"{grp.Key}[{idx}]" : $"{path}.{grp.Key}[{idx}]";
                PrintAllPairs(child, next);
                idx++;
            }
        }
    }
}

static void PrintGetPersonAllPairs(XDocument doc, ILogger log)
{
    PrintSeparator("GetPerson (all pairs)");
    XNamespace? ns = doc.Root?.Name.Namespace;
    XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
    if (body == null)
    {
        log.LogWarning("SOAP Body not found in GetPerson response.");
        Environment.ExitCode = 1;
        return;
    }
    XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
    if (resp == null)
    {
        log.LogWarning("GetPerson response element not found.");
        Environment.ExitCode = 1;
        return;
    }
    foreach (XElement child in resp.Elements())
    {
        PrintAllPairs(child);
    }
}

static string Trunc(string? s, int width)
{
    string t = s ?? "";
    return width <= 0 ? "" : t.Length <= width ? t.PadRight(width) : string.Concat(t.AsSpan(0, Math.Max(0, width - 1)), "…");
}

static string? GetLocal(XElement? parent, string localName)
{
    return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();
}

static IEnumerable<XElement> DescByLocal(XContainer doc, string localName)
{
    return doc.Descendants().Where(e => e.Name.LocalName == localName);
}

static void PrintPeoplePublicInfoTable(XDocument doc, ILogger log, int maxRows = 25)
{
    List<(string ssn, string name, string dob, string gender, string line1, string line2)> rows = [];
    foreach (XElement p in DescByLocal(doc, "PersonPublicInfo"))
    {
        string ssn = GetLocal(p, "SSN") ?? "";
        string first = GetLocal(p, "FirstName") ?? "";
        string last = GetLocal(p, "LastName") ?? "";
        string dob = GetLocal(p, "DateOfBirth") ?? "";
        string gender = GetLocal(p, "Gender") ?? "";

        XElement? addr = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Address");
        string street = GetLocal(addr, "Street") ?? "";
        string bnum = GetLocal(addr, "BuildingNumber") ?? "";
        string line1 = string.IsNullOrWhiteSpace(bnum) ? street : (street + " " + bnum).Trim();
        string line2 = string.Join(" ", new[] { GetLocal(addr, "PostalCode"), GetLocal(addr, "City") }.Where(s => !string.IsNullOrWhiteSpace(s)));

        rows.Add((ssn, $"{first} {last}".Trim(), dob, gender, line1, line2));
    }

    int take = Math.Min(maxRows, rows.Count);
    if (take == 0)
    {
        Console.WriteLine("(no rows)");
        Environment.ExitCode = 1;
        return;
    }

    PrintSeparator("GetPeoplePublicInfo (table)");
    const int wSsn = 12, wName = 28, wDob = 12, wGen = 8, wL1 = 26, wL2 = 26;
    Console.WriteLine($"{Trunc("SSN", wSsn)} {Trunc("Name", wName)} {Trunc("DOB", wDob)} {Trunc("Gender", wGen)} {Trunc("Addr 1", wL1)} {Trunc("Addr 2", wL2)}");
    Console.WriteLine(new string('-', 12 + 1 + 28 + 1 + 12 + 1 + 8 + 1 + 26 + 1 + 26));
    foreach ((string ssn, string name, string dob, string gender, string line1, string line2) in rows.Take(take))
    {
        Console.WriteLine($"{Trunc(ssn, wSsn)} {Trunc(name, wName)} {Trunc(dob, wDob)} {Trunc(gender, wGen)} {Trunc(line1, wL1)} {Trunc(line2, wL2)}");
    }
}