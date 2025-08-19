using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib;

namespace XRoadFolkRaw;

internal sealed class ConsoleUi
{
    private readonly IConfiguration _config;
    private readonly PeopleService _service;
    private readonly ILogger _log;

    public ConsoleUi(IConfiguration config, PeopleService service, ILogger log)
    {
        _config = config;
        _service = service;
        _log = log;
    }

    public async Task RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine("INPUTS MODE: Strict");
        Console.WriteLine("Provide: SSN  OR  FirstName + LastName + DateOfBirth.");
        Console.WriteLine("Example: FirstName=Anna, LastName=Olsen, DateOfBirth=1990-05-01");
        Console.WriteLine("Type 'q' at any prompt to quit.");
        Console.WriteLine("==============================================");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Enter GetPeoplePublicInfo search criteria (leave blank to skip):");
            string ssnInput = Prompt("SSN");
            if (IsQuit(ssnInput)) { break; }
            string fnInput = Prompt("FirstName");
            if (IsQuit(fnInput)) { break; }
            string lnInput = Prompt("LastName");
            if (IsQuit(lnInput)) { break; }
            string dobInput = Prompt("DateOfBirth (YYYY-MM-DD)");
            if (IsQuit(dobInput)) { break; }

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

            if (!string.IsNullOrWhiteSpace(SsnNorm))
            {
                Console.WriteLine($"→ Using SSN = {MaskSsn(SsnNorm)}");
            }
            else
            {
                Console.WriteLine($"→ Using Name = \"{fnInput} {lnInput}\", DOB = {Dob:yyyy-MM-dd}");
            }

            string responseXml;
            try
            {
                responseXml = await _service.GetPeoplePublicInfoAsync(SsnNorm, fnInput, lnInput, Dob);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to call GetPeoplePublicInfo.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(responseXml))
            {
                Console.WriteLine("No response received. Please try again.");
                continue;
            }

            XDocument listDoc;
            try { listDoc = XDocument.Parse(responseXml); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse GetPeoplePublicInfo response; please try again.");
                continue;
            }

            List<XElement> people = [.. listDoc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];
            if (people.Count == 0)
            {
                Console.WriteLine("No matches found. Please refine inputs.");
                continue;
            }

            PrintPeoplePublicInfoTable(listDoc, _log, maxRows: _config.GetValue("Output:MaxPeopleToPrint", 25));

            int maxChain = Math.Max(1, _config.GetValue("Chaining:MaxPersons", 25));
            int count = 0;
            foreach (XElement p in people)
            {
                if (++count > maxChain)
                {
                    break;
                }

                string? Get(string ln) => p.Elements().FirstOrDefault(x => x.Name.LocalName == ln)?.Value?.Trim();
                string? publicId = Get("PublicId") ?? Get("PersonId");
                string? personSsn = Get("SSN");
                string? fn = Get("FirstName");
                string? ln = Get("LastName");
                string? dob = Get("DateOfBirth") ?? Get("BirthDate");

                Console.WriteLine();
                Console.WriteLine($"=== Fetching GetPerson for {publicId ?? personSsn ?? (fn + " " + ln)} {(string.IsNullOrWhiteSpace(dob) ? "" : " DOB:" + dob)} ===");

                try
                {
                    string personResp = await _service.GetPersonAsync(publicId);
                    if (!string.IsNullOrWhiteSpace(personResp))
                    {
                        XDocument doc = XDocument.Parse(personResp);
                        PrintGetPersonAllPairs(doc, _log);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to fetch/print GetPerson for the selected entry.");
                }
            }

            break;
        }
    }

    private static bool IsQuit(string? s) => string.Equals(s?.Trim(), "q", StringComparison.OrdinalIgnoreCase);

    private static string Prompt(string label)
    {
        Console.Write($"{label}: ");
        string? input = Console.ReadLine();
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

    private static void PrintPair(string key, string? value) => Console.WriteLine($"{key} : \"{value ?? ""}\"");

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

    private static void PrintGetPersonAllPairs(XDocument doc, ILogger log)
    {
        PrintSeparator("GetPerson (all pairs)");
        XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
        if (body == null)
        {
            log.LogWarning("SOAP Body not found in GetPerson response.");
            return;
        }
        XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
        if (resp == null)
        {
            log.LogWarning("GetPerson response element not found.");
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
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();

    private static IEnumerable<XElement> DescByLocal(XContainer doc, string localName)
        => doc.Descendants().Where(e => e.Name.LocalName == localName);

    private static void PrintPeoplePublicInfoTable(XDocument doc, ILogger log, int maxRows = 25)
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
}
