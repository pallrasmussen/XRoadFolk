using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib;

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
                catch (Exception ex)
                {
                    LogFailedGetPeoplePublicInfo(_log, ex);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(responseXml))
                {
                    Console.WriteLine(_loc["NoResponse"]);
                    continue;
                }

                XDocument listDoc;
                try { listDoc = XDocument.Parse(responseXml); }
                catch (Exception ex)
                {
                    LogFailedParseGetPeoplePublicInfo(_log, ex);
                    continue;
                }

                List<XElement> people = [.. listDoc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];
                if (people.Count == 0)
                {
                    Console.WriteLine(_loc["NoMatches"]);
                    continue;
                }

                PrintPeoplePublicInfoTable(listDoc, _log, _loc, maxRows: _config.GetValue("Output:MaxPeopleToPrint", 25));

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
                    catch (Exception ex)
                    {
                        LogFailedGetPerson(_log, ex);
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

        private static string Trunc(string? s, int width)
        {
            string t = s ?? "";
            return width <= 0 ? "" : t.Length <= width ? t.PadRight(width) : string.Concat(t.AsSpan(0, Math.Max(0, width - 1)), "…");
        }

        private static string? GetLocal(XElement? parent, string localName)
        {
            return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();
        }

        private static IEnumerable<XElement> DescByLocal(XContainer doc, string localName)
        {
            return doc.Descendants().Where(e => e.Name.LocalName == localName);
        }

        private static void PrintPeoplePublicInfoTable(XDocument doc, ILogger log, IStringLocalizer loc, int maxRows = 25)
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
                Console.WriteLine(loc["NoRows"]);
                return;
            }
            const int wSsn = 12, wName = 28, wDob = 12, wGen = 8, wL1 = 26, wL2 = 26;
            Console.WriteLine($"{Trunc(loc["SSN"], wSsn)} {Trunc(loc["Name"], wName)} {Trunc(loc["DOB"], wDob)} {Trunc(loc["Gender"], wGen)} {Trunc(loc["Addr1"], wL1)} {Trunc(loc["Addr2"], wL2)}");
            Console.WriteLine(new string('-', 12 + 1 + 28 + 1 + 12 + 1 + 8 + 1 + 26 + 1 + 26));
            foreach ((string ssn, string name, string dob, string gender, string line1, string line2) in rows.Take(take))
            {
                Console.WriteLine($"{Trunc(ssn, wSsn)} {Trunc(name, wName)} {Trunc(dob, wDob)} {Trunc(gender, wGen)} {Trunc(line1, wL1)} {Trunc(line2, wL2)}");
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
