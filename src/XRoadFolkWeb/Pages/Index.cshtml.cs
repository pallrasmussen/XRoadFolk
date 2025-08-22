using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;

namespace XRoadFolkWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly PeopleService _service;
        private readonly IStringLocalizer<InputValidation> _valLoc;

        public IndexModel(PeopleService service, IStringLocalizer<InputValidation> valLoc)
        {
            _service = service;
            _valLoc = valLoc;
        }

        [BindProperty] public string? Ssn { get; set; }
        [BindProperty] public string? FirstName { get; set; }
        [BindProperty] public string? LastName { get; set; }
        [BindProperty] public string? DateOfBirth { get; set; }

        public List<PersonRow> Results { get; private set; } = new();
        public List<(string Key, string Value)>? PersonDetails { get; private set; }
        public List<string> Errors { get; private set; } = new();

        public async Task OnGetAsync(string? publicId = null)
        {
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                // Drill-down details
                string xml = await _service.GetPersonAsync(publicId);
                PersonDetails = FlattenResponse(xml);
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var (ok, errs, ssnNorm, dob) = InputValidation.ValidateCriteria(Ssn, FirstName, LastName, DateOfBirth, _valLoc);
            if (!ok)
            {
                Errors = errs;
                return Page();
            }

            string xml = await _service.GetPeoplePublicInfoAsync(ssnNorm, FirstName, LastName, dob);
            Results = ParsePeopleList(xml);
            return Page();
        }

        public IActionResult OnPostClear()
        {
            // Reset server-side state (not strictly needed with redirect, but harmless)
            Ssn = FirstName = LastName = DateOfBirth = null;
            Results = new();
            PersonDetails = null;
            Errors = new();

            // PRG: remove ?handler=Clear and avoid stale ModelState
            return RedirectToPage();
        }

        public IActionResult OnGetClear()
        {
            return RedirectToPage();
        }

        private static List<PersonRow> ParsePeopleList(string xml)
        {
            List<PersonRow> rows = new();
            if (string.IsNullOrWhiteSpace(xml)) return rows;

            XDocument doc = XDocument.Parse(xml);

            // Collect people first (used for count/fallbacks)
            var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo").ToList();

            // Optional: SSN may only appear in the request criteria; use as fallback if single result
            string? requestSsn = doc
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "ListOfPersonPublicInfoCriteria")?
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "PersonPublicInfoCriteria")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "SSN")?
                .Value?.Trim();

            foreach (var p in people)
            {
                string? publicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                                ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim();

                // Names are under Names/Name with Type and Value
                var nameItems = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Names")?
                               .Elements().Where(x => x.Name.LocalName == "Name")
                            ?? Enumerable.Empty<XElement>();

                // FirstName(s): order by <Order>, join if multiple
                var firstNames = nameItems
                    .Where(n => string.Equals(
                        n.Elements().FirstOrDefault(e => e.Name.LocalName == "Type")?.Value,
                        "FirstName", StringComparison.OrdinalIgnoreCase))
                    .Select(n => new
                    {
                        OrderText = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Order")?.Value,
                        Value = n.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value?.Trim()
                    })
                    .OrderBy(n => int.TryParse(n.OrderText, out var o) ? o : int.MaxValue)
                    .Select(n => n.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

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
            var pairs = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(xml)) return pairs;

            XDocument doc = XDocument.Parse(xml);
            var body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
            if (body == null) return pairs;
            var resp = body.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Response", StringComparison.OrdinalIgnoreCase));
            if (resp == null) return pairs;

            void Flatten(XElement el, string path)
            {
                var children = el.Elements().ToList();
                if (children.Count == 0)
                {
                    var v = el.Value?.Trim();
                    if (!string.IsNullOrEmpty(v))
                    {
                        var key = string.IsNullOrEmpty(path) ? el.Name.LocalName : path;
                        pairs.Add((key, v));
                    }
                    return;
                }

                foreach (var grp in children.GroupBy(c => c.Name.LocalName))
                {
                    if (grp.Count() == 1)
                    {
                        var child = grp.First();
                        var next = string.IsNullOrEmpty(path) ? grp.Key : $"{path}.{grp.Key}";
                        Flatten(child, next);
                    }
                    else
                    {
                        int idx = 0;
                        foreach (var child in grp)
                        {
                            var next = string.IsNullOrEmpty(path) ? $"{grp.Key}[{idx}]" : $"{path}.{grp.Key}[{idx}]";
                            Flatten(child, next);
                            idx++;
                        }
                    }
                }
            }

            foreach (var child in resp.Elements())
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