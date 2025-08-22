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

        private static List<PersonRow> ParsePeopleList(string xml)
        {
            List<PersonRow> rows = new();
            if (string.IsNullOrWhiteSpace(xml)) return rows;

            XDocument doc = XDocument.Parse(xml);
            var people = doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo");
            foreach (var p in people)
            {
                string? Get(string ln) => p.Elements().FirstOrDefault(x => x.Name.LocalName == ln)?.Value?.Trim();
                rows.Add(new PersonRow
                {
                    PublicId = Get("PublicId") ?? Get("PersonId"),
                    SSN = Get("SSN"),
                    FirstName = Get("FirstName"),
                    LastName = Get("LastName"),
                    DateOfBirth = Get("DateOfBirth") ?? Get("BirthDate")
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