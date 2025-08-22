using System;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Pages
{
    public class IndexModel(PeopleService service, IStringLocalizer<InputValidation> valLoc, IStringLocalizer<XRoadFolkWeb.SharedResource> sr) : PageModel
    {
        private readonly PeopleService _service = service;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<XRoadFolkWeb.SharedResource> _sr = sr;

        [BindProperty] public string? Ssn { get; set; }
        [BindProperty] public string? FirstName { get; set; }
        [BindProperty] public string? LastName { get; set; }
        [BindProperty] public string? DateOfBirth { get; set; }

        public List<PersonRow> Results { get; private set; } = [];
        public List<(string Key, string Value)>? PersonDetails { get; private set; }
        public string SelectedNameSuffix { get; private set; } = string.Empty;
        public List<string> Errors { get; private set; } = [];

        public async Task OnGetAsync(string? publicId = null)
        {
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                try
                {
                    string xml = await _service.GetPersonAsync(publicId);
                    PersonDetails = FlattenResponse(xml);

                    string? first = PersonDetails
                        ?.FirstOrDefault(p => p.Key.EndsWith(".FirstName", StringComparison.OrdinalIgnoreCase)).Value;
                    string? last = PersonDetails
                        ?.FirstOrDefault(p => p.Key.EndsWith(".LastName", StringComparison.OrdinalIgnoreCase)).Value;
                    SelectedNameSuffix = (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                        ? _sr["SelectedNameSuffixFormat", string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                        : string.Empty;
                }
                catch (Exception ex)
                {
                    Errors.Add(ex.Message);
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            (bool ok, List<string> errs, string? ssnNorm, DateTimeOffset? dob) = InputValidation.ValidateCriteria(Ssn, FirstName, LastName, DateOfBirth, _valLoc);
            if (!ok)
            {
                Errors = errs;
                return Page();
            }

            try
            {
                string xml = await _service.GetPeoplePublicInfoAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob);
                Results = ParsePeopleList(xml);
                SelectedNameSuffix = string.Empty;
            }
            catch (Exception ex)
            {
                Errors.Add(ex.Message);
            }
            return Page();
        }

        public IActionResult OnPostClear()
        {
            // Reset server-side state (not strictly needed with redirect, but harmless)
            Ssn = FirstName = LastName = DateOfBirth = null;
            Results = [];
            PersonDetails = null;
            Errors = [];
            SelectedNameSuffix = string.Empty;

            // PRG: remove ?handler=Clear and avoid stale ModelState
            return RedirectToPage();
        }

        public IActionResult OnGetClear()
        {
            return RedirectToPage();
        }

        private static List<PersonRow> ParsePeopleList(string xml)
        {
            List<PersonRow> rows = [];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return rows;
            }

            XDocument doc = XDocument.Parse(xml);

            // Collect people first (used for count/fallbacks)
            List<XElement> people = [.. doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];

            // Optional: SSN may only appear in the request criteria; use as fallback if single result
            string? requestSsn = doc
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "ListOfPersonPublicInfoCriteria")?
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "PersonPublicInfoCriteria")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "SSN")?
                .Value?.Trim();

            foreach (XElement? p in people)
            {
                string? publicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                                ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim();

                // Names are under Names/Name with Type and Value
                IEnumerable<XElement> nameItems = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Names")?
                               .Elements().Where(x => x.Name.LocalName == "Name")
                            ?? [];

                // FirstName(s): order by <Order>, join if multiple
                List<string?> firstNames = [.. nameItems
                    .Where(n => string.Equals(
                        n.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value,
                        "FirstName", StringComparison.OrdinalIgnoreCase))
                    .Select(n => new
                    {
                        OrderText = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Order")?.Value,
                        Value = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim()
                    })
                    .OrderBy(n => int.TryParse(n.OrderText, out int o) ? o : int.MaxValue)
                    .Select(n => n.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))];

                string? firstName = firstNames.Count > 0 ? string.Join(" ", firstNames) : null;

                string? lastName = nameItems
                    .Where(n => string.Equals(
                        n.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value,
                        "LastName", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim())
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                // Date of birth is provided as CivilStatusDate with Born status
                string? civilStatusDate = p.Elements().FirstOrDefault(x => x.Name.LocalName == "CivilStatusDate")?.Value?.Trim();
                string? dateOfBirth = !string.IsNullOrWhiteSpace(civilStatusDate) && civilStatusDate.Length >= 10
                    ? civilStatusDate[..10]
                    : civilStatusDate;

                // SSN usually not present in PersonPublicInfo; fallback to request SSN if single result
                string? ssn = p.Elements().FirstOrDefault(x => x.Name.LocalName == "SSN")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(ssn) && people.Count == 1 && !string.IsNullOrWhiteSpace(requestSsn))
                {
                    ssn = requestSsn;
                }

                rows.Add(new PersonRow
                {
                    PublicId = publicId,
                    SSN = ssn,
                    FirstName = firstName,
                    LastName = lastName,
                    DateOfBirth = dateOfBirth
                });
            }
            return rows;
        }

        private static List<(string Key, string Value)> FlattenResponse(string xml)
        {
            List<(string, string)> pairs = [];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return pairs;
            }

            XDocument doc = XDocument.Parse(xml);
            XElement? body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
            if (body == null)
            {
                return pairs;
            }

            XElement? resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
            if (resp == null)
            {
                return pairs;
            }

            void Flatten(XElement el, string path)
            {
                List<XElement> children = [.. el.Elements()];
                if (children.Count == 0)
                {
                    string? v = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(v))
                    {
                        string key = string.IsNullOrEmpty(path) ? el.Name.LocalName : path;
                        pairs.Add((key, v));
                    }
                    return;
                }

                foreach (IGrouping<string, XElement> grp in children.GroupBy(c => c.Name.LocalName))
                {
                    if (grp.Count() == 1)
                    {
                        XElement child = grp.First();
                        string next = string.IsNullOrEmpty(path) ? grp.Key : $"{path}.{grp.Key}";
                        Flatten(child, next);
                    }
                    else
                    {
                        int idx = 0;
                        foreach (XElement? child in grp)
                        {
                            string next = string.IsNullOrEmpty(path) ? $"{grp.Key}[{idx}]" : $"{path}.{grp.Key}[{idx}]";
                            Flatten(child, next);
                            idx++;
                        }
                    }
                }
            }

            foreach (XElement child in resp.Elements())
            {
                Flatten(child, "");
            }
            return pairs;
        }

        public sealed class PersonRow
        {
            public string? PublicId { get; set; }
            public string? SSN { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? DateOfBirth { get; set; }
        }
    }
}