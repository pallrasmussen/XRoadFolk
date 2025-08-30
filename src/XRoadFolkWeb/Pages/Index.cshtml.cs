using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Configuration;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Validation;
using XRoadFolkWeb.Features.People;
using PersonRow = XRoadFolkWeb.Features.People.PersonRow;

namespace XRoadFolkWeb.Pages
{
    [RequireSsnOrNameDob(nameof(Ssn), nameof(FirstName), nameof(LastName), nameof(DateOfBirth))]
    public class IndexModel(PeopleService service, IStringLocalizer<InputValidation> valLoc, IStringLocalizer<IndexModel> loc, IMemoryCache cache, IConfiguration config, PeopleResponseParser parser) : PageModel
    {
        private readonly PeopleService _service = service;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<IndexModel> _loc = loc;
        private readonly IMemoryCache _cache = cache;
        private readonly IConfiguration _config = config;
        private readonly PeopleResponseParser _parser = parser;

        [BindProperty, Ssn]
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

        // Expose enabled include keys to the view
        public List<string> EnabledPersonIncludeKeys { get; private set; } = [];

        private const string ResponseKey = "PeoplePublicInfoResponse";

        public async Task OnGetAsync(
            string? publicId = null,
            string? ssn = null,
            string? firstName = null,
            string? lastName = null,
            string? dateOfBirth = null)
        {
            EnabledPersonIncludeKeys = [.. IncludeConfigHelper.GetEnabledIncludeKeys(_config)];

            // Prefill bound properties from query to retain form values after redirects or navigation
            if (!string.IsNullOrWhiteSpace(ssn)) Ssn = ssn;
            if (!string.IsNullOrWhiteSpace(firstName)) FirstName = firstName;
            if (!string.IsNullOrWhiteSpace(lastName)) LastName = lastName;
            if (!string.IsNullOrWhiteSpace(dateOfBirth)) DateOfBirth = dateOfBirth;

            // Optional: load person details by PublicId (AJAX panel)
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                try
                {
                    string xml = await _service.GetPersonAsync(publicId);
                    PersonDetails = _parser.FlattenResponse(xml);

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

            // If any search criteria provided via GET, perform the search (PRG target)
            bool hasCriteria = !string.IsNullOrWhiteSpace(Ssn)
                            || !string.IsNullOrWhiteSpace(FirstName)
                            || !string.IsNullOrWhiteSpace(LastName)
                            || !string.IsNullOrWhiteSpace(DateOfBirth);
            if (hasCriteria)
            {
                // Only validate the present path (SSN vs Name+DOB)
                (bool ok, List<string> errs, string? ssnNorm, DateTimeOffset? dob) =
                    InputValidation.ValidateCriteria(Ssn, FirstName, LastName, DateOfBirth, _valLoc);

                if (!ok)
                {
                    Errors = errs;
                    return;
                }

                try
                {
                    string xml = await _service.GetPeoplePublicInfoAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob);
                    PeoplePublicInfoResponseXml = xml;
                    PeoplePublicInfoResponseXmlPretty = _parser.PrettyFormatXml(xml);
                    Results = _parser.ParsePeopleList(xml);

                    _ = _cache.Set(ResponseKey, Results, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    });
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
            // Clear person details on every new search
            PersonDetails = null;
            SelectedNameSuffix = string.Empty;

            // Load enabled include keys once per request
            EnabledPersonIncludeKeys = [.. IncludeConfigHelper.GetEnabledIncludeKeys(_config)];

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

            // Perform the operation
            try
            {
                string xml = await _service.GetPeoplePublicInfoAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob);
                PeoplePublicInfoResponseXml = xml;
                PeoplePublicInfoResponseXmlPretty = _parser.PrettyFormatXml(xml);
                Results = _parser.ParsePeopleList(xml);

                _ = _cache.Set(ResponseKey, Results, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            catch (Exception ex)
            {
                Errors.Add(ex.Message);
                return Page();
            }

            // PRG: Redirect to GET with criteria in query string so Back never re-submits the form
            return RedirectToPage(new
            {
                ssn = Ssn,
                firstName = FirstName,
                lastName = LastName,
                dateOfBirth = DateOfBirth
            });
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

        public async Task<IActionResult> OnGetPersonDetailsAsync(string? publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId))
            {
                return BadRequest(new { ok = false, error = "Missing publicId." });
            }

            try
            {
                string xml = await _service.GetPersonAsync(publicId);
                List<(string Key, string Value)> pairs = _parser.FlattenResponse(xml);

                // Filter out any RequestHeader/requestBody content (any level, case-insensitive)
                List<(string Key, string Value)> filtered = pairs
                    .Where(p =>
                    {
                        if (string.IsNullOrEmpty(p.Key)) return true;
                        string k = p.Key;
                        return !(k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                              || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                              || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                              || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase));
                    })
                    // Also remove the same noise fields used by Summary: Id, Fixed, AuthorityCode, PersonAddressId
                    .Where(p =>
                    {
                        if (string.IsNullOrEmpty(p.Key)) return true;
                        string key = p.Key;
                        int lastDot = key.LastIndexOf('.');
                        string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                        int bpos = sub.IndexOf('[');
                        if (bpos >= 0) sub = sub[..bpos];
                        string s = sub.ToLowerInvariant();
                        return s != "id" && s != "fixed" && s != "authoritycode" && s != "personaddressid";
                    })
                    .ToList();

                // Build allow-list from centralized helper
                HashSet<string> allowed = IncludeConfigHelper.GetEnabledIncludeKeys(_config);

                if (allowed.Count > 0)
                {
                    // Relaxed matching: equal or prefix match either way (to handle plural/list variants)
                    static bool Matches(string seg, string allowedKey)
                    {
                        return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                            || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                            || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
                    }

                    filtered = filtered.Where(p =>
                    {
                        if (string.IsNullOrWhiteSpace(p.Key)) return false;
                        string key = p.Key;
                        int dot = key.IndexOf('.');
                        string seg = dot >= 0 ? key[..dot] : key;
                        int bpos = seg.IndexOf('[');
                        if (bpos >= 0) seg = seg[..bpos];
                        foreach (string a in allowed)
                        {
                            if (Matches(seg, a)) return true;
                        }
                        return false;
                    }).ToList();
                }

                return new JsonResult(new
                {
                    ok = true,
                    publicId,
                    raw = xml,
                    pretty = _parser.PrettyFormatXml(xml),
                    details = filtered.Select(p => new { key = p.Key, value = p.Value }).ToArray()
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { ok = false, error = ex.Message });
            }
        }
    }
}