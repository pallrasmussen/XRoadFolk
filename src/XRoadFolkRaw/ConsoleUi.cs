using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib;
using System.Net.Http;

namespace XRoadFolkRaw
{

    internal sealed partial class ConsoleUi(IConfiguration config, PeopleService service, ILogger log, IStringLocalizer<ConsoleUi> loc, IStringLocalizer<InputValidation> valLoc)
    {
        private readonly IConfiguration _config = config;
        private readonly PeopleService _service = service;
        private readonly ILogger _log = log;
        private readonly IStringLocalizer<ConsoleUi> _loc = loc;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;

        public async Task RunAsync()
        {
            Console.WriteLine();
            Console.WriteLine(_loc["BannerSeparator"]);
            Console.WriteLine(_loc["InputModeStrict"]);
            Console.WriteLine(_loc["ProvideInfo"]);
            Console.WriteLine(_loc["ExampleInput"]);
            Console.WriteLine(_loc["QuitPrompt"]);
            Console.WriteLine(_loc["BannerSeparator"]);

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine(_loc["EnterSearchCriteria"]);
                string? ssnInput = Prompt(_loc["SSN"], out bool quit);
                if (quit) { break; }
                string? fnInput = Prompt(_loc["FirstName"], out quit);
                if (quit) { break; }
                string? lnInput = Prompt(_loc["LastName"], out quit);
                if (quit) { break; }
                string? dobInput = Prompt(_loc["DateOfBirthPrompt"], out quit);
                if (quit) { break; }

                (bool Ok, List<string> Errors, string? SsnNorm, DateTimeOffset? Dob) = InputValidation.ValidateCriteria(ssnInput, fnInput, lnInput, dobInput, _valLoc);
                if (!Ok)
                {
                    Console.WriteLine(_loc["InputInvalid"]);
                    foreach (string e in Errors)
                    {
                        Console.WriteLine(_loc["ErrorBullet"] + e);
                    }
                    Console.WriteLine(_loc["PleaseTryAgain"]);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(SsnNorm))
                {
                    Console.WriteLine(_loc["UsingSsn", MaskSsn(SsnNorm)]);
                }
                else
                {
                    Console.WriteLine(_loc["UsingNameDob", fnInput ?? "", lnInput ?? "", Dob?.ToString("yyyy-MM-dd") ?? ""]);
                }

                string responseXml;
                try
                {
                    responseXml = await _service.GetPeoplePublicInfoAsync(SsnNorm, fnInput, lnInput, Dob);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Operation cancelled.");
                    break;
                }
                catch (HttpRequestException ex)
                {
                    LogFailedGetPeoplePublicInfo(_log, ex);
                    Console.WriteLine(ex.Message);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(responseXml))
                {
                    Console.WriteLine(_loc["NoResponse"]);
                    continue;
                }

                XDocument listDoc;
                try { listDoc = XDocument.Parse(responseXml); }
                catch (System.Xml.XmlException ex)
                {
                    LogFailedParseGetPeoplePublicInfo(_log, ex);
                    Console.WriteLine(ex.Message);
                    continue;
                }

                List<XElement> people = [.. listDoc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];
                if (people.Count == 0)
                {
                    Console.WriteLine(_loc["NoMatches"]);
                    continue;
                }

                // Print all pairs for each PersonPublicInfo
                PrintGetPeoplePublicInfoAllPairs(listDoc, _log, _loc);

                // Print summary only for each PersonPublicInfo
                PrintGetPeoplePublicInfoSummary(listDoc, _log, _loc);

                int maxChain = Math.Max(1, _config.GetValue("Chaining:MaxPersons", 25));
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
                    string dobPart = string.IsNullOrWhiteSpace(dob) ? "" : $" {_loc["DOB"]}:{dob}";
                    Console.WriteLine(_loc["FetchingGetPerson", publicId ?? personSsn ?? (fn + " " + ln), dobPart]);

                    try
                    {
                        string personResp = await _service.GetPersonAsync(publicId);
                        if (!string.IsNullOrWhiteSpace(personResp))
                        {
                            XDocument doc = XDocument.Parse(personResp);
                            PrintGetPersonAllPairs(doc, _log, _loc);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Operation cancelled.");
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        LogFailedGetPerson(_log, ex);
                        Console.WriteLine(ex.Message);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        LogFailedGetPerson(_log, ex);
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }

        private static string? Prompt(string label, out bool quit)
        {
            Console.Write($"{label}: ");
            string? input = ConsoleInput.ReadLineOrCtrlQ(out quit);
            if (quit)
            {
                return null; // special value indicating Ctrl+Q
            }

            return string.IsNullOrWhiteSpace(input) ? "" : input.Trim();
        }

        private static string MaskSsn(string? ssn)
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

        private static void PrintPair(string key, string? value)
        {
            Console.WriteLine($"{key} : \"{value ?? ""}\"");
        }

        private static void PrintSeparator(string title = "")
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 60));
            if (!string.IsNullOrWhiteSpace(title))
            {
                Console.WriteLine(title);
            }
            Console.WriteLine(new string('=', 60));
        }

        private static void PrintAllPairs(XElement el, string path = "")
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
                    foreach (XElement child in grp)
                    {
                        string next = string.IsNullOrEmpty(path) ? $"{grp.Key}[{idx}]" : $"{path}.{grp.Key}[{idx}]";
                        PrintAllPairs(child, next);
                        idx++;
                    }
                }
            }
        }

        // Logging helpers using source generators
        [LoggerMessage(Level = LogLevel.Warning, Message = "SOAP Body not found in GetPerson response.")]
        public static partial void LogSoapBodyNotFound(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "GetPerson response element not found.")]
        public static partial void LogGetPersonResponseNotFound(ILogger logger);

        private static void PrintGetPersonAllPairs(XDocument doc, ILogger log, IStringLocalizer loc)
        {
            PrintSeparator(loc["GetPersonAllPairs"]);
            XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
            if (body == null)
            {
                LogSoapBodyNotFound(log);
                return;
            }
            XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
            if (resp == null)
            {
                LogGetPersonResponseNotFound(log);
                return;
            }
            foreach (XElement child in resp.Elements())
            {
                PrintAllPairs(child);
            }
        }

        private static void PrintGetPeoplePublicInfoAllPairs(XDocument doc, ILogger log, IStringLocalizer loc)
        {
            PrintSeparator(loc["GetPeoplePublicInfoAllPairs"]);
            // Find all PersonPublicInfo elements in the document
            var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo").ToList();
            if (people.Count == 0)
            {
                log.LogWarning("No PersonPublicInfo elements found in response.");
                return;
            }
            int idx = 0;
            foreach (var person in people)
            {
                PrintSeparator($"{loc["Person"]} #{++idx}");
                PrintAllPairs(person);
            }
        }

        private static void PrintGetPeoplePublicInfoSummary(XDocument doc, ILogger log, IStringLocalizer loc)
        {
            PrintSeparator(loc["GetPeoplePublicInfoSummary"]);
            var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo").ToList();
            if (people.Count == 0)
            {
                log.LogWarning("No PersonPublicInfo elements found in response.");
                return;
            }

            // Print header
            Console.WriteLine($"{loc["PublicId"],-18} {loc["SSN"],-14} {loc["FirstName"],-16} {loc["LastName"],-16} {loc["DateOfBirth"],-14}");
            Console.WriteLine(new string('-', 78));

            foreach (var person in people)
            {
                string? publicId = person.Element(person.Name.Namespace + "PublicId")?.Value?.Trim()
                                ?? person.Element(person.Name.Namespace + "PersonId")?.Value?.Trim();

                // SSN is in the request, not in the response, so may be missing
                string? ssn = person.Element(person.Name.Namespace + "SSN")?.Value?.Trim();

                // Names are nested under <Names><Name> with <Type>FirstName</Type> and <Type>LastName</Type>
                string? first = person
                    .Element(person.Name.Namespace + "Names")?
                    .Elements(person.Name.Namespace + "Name")
                    .FirstOrDefault(n => n.Element(person.Name.Namespace + "Type")?.Value == "FirstName")?
                    .Element(person.Name.Namespace + "Value")?.Value?.Trim();

                string? last = person
                    .Element(person.Name.Namespace + "Names")?
                    .Elements(person.Name.Namespace + "Name")
                    .FirstOrDefault(n => n.Element(person.Name.Namespace + "Type")?.Value == "LastName")?
                    .Element(person.Name.Namespace + "Value")?.Value?.Trim();

                // Date of birth is in the Created field of the OfficialName or FirstName, or in CivilStatusDate
                string? dob = person
                    .Element(person.Name.Namespace + "Names")?
                    .Elements(person.Name.Namespace + "Name")
                    .FirstOrDefault(n => n.Element(person.Name.Namespace + "Type")?.Value == "OfficialName")?
                    .Element(person.Name.Namespace + "Created")?.Value?.Substring(0, 10);

                if (string.IsNullOrWhiteSpace(dob))
                {
                    dob = person.Element(person.Name.Namespace + "CivilStatusDate")?.Value?.Substring(0, 10);
                }

                Console.WriteLine($"{publicId,-18} {ssn,-14} {first,-16} {last,-16} {dob,-14}");
            }
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
                       Message = "Failed to call GetPeoplePublicInfo.")]
        static partial void LogFailedGetPeoplePublicInfo(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
                       Message = "Failed to parse GetPeoplePublicInfo response; please try again.")]
        static partial void LogFailedParseGetPeoplePublicInfo(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
                       Message = "Failed to fetch/print GetPerson for the selected entry.")]
        static partial void LogFailedGetPerson(ILogger logger, Exception ex);
    }
}
