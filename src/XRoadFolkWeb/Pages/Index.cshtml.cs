//using System;
//using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
//using System.Linq;
//using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;
using XRoadFolkWeb.Validation;
using SsnAttr = XRoadFolkWeb.Validation.SsnAttribute; // ADD: alias to the intended SsnAttribute

namespace XRoadFolkWeb.Pages
{
    [RequireSsnOrNameDob(nameof(Ssn), nameof(FirstName), nameof(LastName), nameof(DateOfBirth))]
    public class IndexModel(PeopleService service, IStringLocalizer<InputValidation> valLoc, IStringLocalizer<IndexModel> loc, IMemoryCache cache) : PageModel
    {
        private readonly PeopleService _service = service;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<IndexModel> _loc = loc;
        private readonly IMemoryCache _cache = cache;

        [BindProperty, SsnAttr] // USE the alias to disambiguate
        [Display(Name = "SSN", ResourceType = typeof(Resources.Labels))]
        public string? Ssn { get; set; }

        [BindProperty]
        [Display(Name = "FirstName", ResourceType = typeof(Resources.Labels))]
        [Name(MessageKey = "FirstName_Invalid")]
        [MaxLength(100,
            ErrorMessageResourceType = typeof(Resources.ValidationMessages),
            ErrorMessageResourceName = "FirstName_MaxLength")]
        [LettersOnly]
        public string? FirstName { get; set; }

        [BindProperty]
        [Display(Name = "LastName", ResourceType = typeof(Resources.Labels))]
        [Name(MessageKey = "LastName_Invalid")]
        [MaxLength(100,
            ErrorMessageResourceType = typeof(Resources.ValidationMessages),
            ErrorMessageResourceName = "LastName_MaxLength")]
        [LettersOnly]
        public string? LastName { get; set; }

        [BindProperty]
        [Display(Name = "DateOfBirth", ResourceType = typeof(Resources.Labels))]
        [Dob]
        public string? DateOfBirth { get; set; }

        public List<PersonRow> Results { get; private set; } = [];
        public List<(string Key, string Value)>? PersonDetails { get; private set; }
        public string SelectedNameSuffix { get; private set; } = string.Empty;
        public List<string> Errors { get; private set; } = [];

        // Holds full GetPeoplePublicInfo response (raw + pretty)
        public string? PeoplePublicInfoResponseXml { get; private set; }
        public string? PeoplePublicInfoResponseXmlPretty { get; private set; }

        private const string ResponseKey = "PeoplePublicInfoResponse";

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
                        ? _loc["SelectedNameSuffixFormat", string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)))]
                        : string.Empty;
                }
                catch (Exception ex)
                {
                    Errors.Add(ex.Message);
                }
            }
        }

        // Replace the ValidateCriteria call in OnPostAsync with this guarded version
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Errors = [.. ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];
                return Page();
            }

            // Decide the input path:
            bool usingSsn = !string.IsNullOrWhiteSpace(Ssn);
            string? first = FirstName;
            string? last = LastName;
            string? dobInput = DateOfBirth;

            // If SSN is empty, require First + Last + DOB (presence only here).
            if (!usingSsn && (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) || string.IsNullOrWhiteSpace(dobInput)))
            {
                LocalizedString msg = _valLoc[name: InputValidation.Errors.ProvideSsnOrNameDob];
                ModelState.AddModelError(string.Empty, msg);
                Errors = [msg];
                return Page();
            }

            // Only validate the chosen path to avoid spurious cross-field errors
            (bool ok, List<string> errs, string? ssnNorm, DateTimeOffset? dob) =
                InputValidation.ValidateCriteria(
                    usingSsn ? Ssn : null,
                    usingSsn ? null : first,
                    usingSsn ? null : last,
                    usingSsn ? null : dobInput,
                    _valLoc);

            if (!ok)
            {
                foreach (string err in errs)
                {
                    ModelState.AddModelError(string.Empty, err);
                }

                Errors = errs;
                return Page();
            }

            try
            {
                string xml = await _service.GetPeoplePublicInfoAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob);
                PeoplePublicInfoResponseXml = xml;
                PeoplePublicInfoResponseXmlPretty = PrettyFormatXml(xml);
                Results = ParsePeopleList(xml);
                SelectedNameSuffix = string.Empty;

                _ = _cache.Set(ResponseKey, Results, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            catch (Exception ex)
            {
                Errors.Add(ex.Message);
            }
            return Page();
        }

        public IActionResult OnPostClear()
        {
            Ssn = FirstName = LastName = DateOfBirth = null;
            Results = [];
            PersonDetails = null;
            Errors = [];
            SelectedNameSuffix = string.Empty;
            PeoplePublicInfoResponseXml = null;
            PeoplePublicInfoResponseXmlPretty = null;
            return RedirectToPage();
        }

        public IActionResult OnGetClear()
        {
            return RedirectToPage();
        }

        private static string PrettyFormatXml(string xml)
        {
            try
            {
                XDocument doc = XDocument.Parse(xml);
                return doc.ToString(SaveOptions.None);
            }
            catch
            {
                return xml;
            }
        }

        private static List<PersonRow> ParsePeopleList(string xml)
        {
            List<PersonRow> rows = [];
            if (string.IsNullOrWhiteSpace(xml))
            {
                return rows;
            }

            XDocument doc = XDocument.Parse(xml);

            List<XElement> people = [.. doc.Descendants().Where(e => e.Name.LocalName == "PersonPublicInfo")];

            string? requestSsn = doc
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "ListOfPersonPublicInfoCriteria")?
                .Descendants().FirstOrDefault(e => e.Name.LocalName == "PersonPublicInfoCriteria")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "SSN")?
                .Value?.Trim();

            foreach (XElement? p in people)
            {
                string? publicId = p.Elements().FirstOrDefault(x => x.Name.LocalName == "PublicId")?.Value?.Trim()
                                ?? p.Elements().FirstOrDefault(x => x.Name.LocalName == "PersonId")?.Value?.Trim();

                IEnumerable<XElement> nameItems = p.Elements().FirstOrDefault(x => x.Name.LocalName == "Names")?
                               .Elements().Where(x => x.Name.LocalName == "Name")
                            ?? [];

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

                string? civilStatusDate = p.Elements().FirstOrDefault(x => x.Name.LocalName == "CivilStatusDate")?.Value?.Trim();
                string? dateOfBirth = !string.IsNullOrWhiteSpace(civilStatusDate) && civilStatusDate.Length >= 10
                    ? civilStatusDate[..10]
                    : civilStatusDate;

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