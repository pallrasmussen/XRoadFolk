using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Validation;

namespace XRoadFolkWeb.Pages
{
    [RequireSsnOrNameDob(nameof(Ssn), nameof(FirstName), nameof(LastName), nameof(DateOfBirth))]
    public sealed partial class IndexModel(PeopleService service, IStringLocalizer<InputValidation> valLoc, IStringLocalizer<IndexModel> loc, IMemoryCache cache, IConfiguration config, ILogger<IndexModel> logger) : PageModel
    {
        private readonly PeopleService _service = service;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<IndexModel> _loc = loc;
        private readonly IMemoryCache _cache = cache;
        private readonly IConfiguration _config = config;
        private readonly ILogger<IndexModel> _logger = logger;

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

        private string GetFriendlyError()
        {
            LocalizedString s = _loc["UnexpectedError"];
            return s.ResourceNotFound ? "An unexpected error occurred. Please try again later." : s.Value;
        }

        private string GetScopedCacheKey()
        {
            string sid = HttpContext?.Session?.Id;
            if (string.IsNullOrWhiteSpace(sid))
            {
                sid = HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
            }
            return $"{ResponseKey}|{sid}";
        }

        public async Task OnGetAsync(
            string? publicId = null,
            string? ssn = null,
            string? firstName = null,
            string? lastName = null,
            string? dateOfBirth = null)
        {
            // Load enabled include keys once per request
            EnabledPersonIncludeKeys = ReadEnabledIncludeKeys();

            // Prefill bound properties from query to retain form values after redirects or navigation
            if (!string.IsNullOrWhiteSpace(ssn))
            {
                Ssn = ssn;
            }
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                FirstName = firstName;
            }
            if (!string.IsNullOrWhiteSpace(lastName))
            {
                LastName = lastName;
            }
            if (!string.IsNullOrWhiteSpace(dateOfBirth))
            {
                DateOfBirth = dateOfBirth;
            }

            // Optional: load person details by PublicId (AJAX panel)
            if (!string.IsNullOrWhiteSpace(publicId))
            {
                try
                {
                    string xml = await _service.GetPersonAsync(publicId);
                    PersonDetails = PeopleXmlParser.FlattenResponse(xml);

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
                    LogPersonDetailsError(_logger, publicId ?? string.Empty, ex);
                    Errors.Add(GetFriendlyError());
                }
            }

            bool hasCriteria = !string.IsNullOrWhiteSpace(Ssn)
                            || !string.IsNullOrWhiteSpace(FirstName)
                            || !string.IsNullOrWhiteSpace(LastName)
                            || !string.IsNullOrWhiteSpace(DateOfBirth);
            if (hasCriteria)
            {
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
                    PeoplePublicInfoResponseXmlPretty = PeopleXmlParser.FormatPretty(xml);
                    Results = PeopleXmlParser.ParsePeopleList(xml);

                    string key = GetScopedCacheKey();
                    _ = _cache.Set(key, Results, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    });
                }
                catch (Exception ex)
                {
                    LogPeopleSearchError(_logger, !string.IsNullOrWhiteSpace(Ssn), ex);
                    Errors.Add(GetFriendlyError());
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            PersonDetails = null;
            SelectedNameSuffix = string.Empty;

            EnabledPersonIncludeKeys = ReadEnabledIncludeKeys();

            if (!ModelState.IsValid)
            {
                Errors = [.. ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)];
                return Page();
            }

            bool usingSsn = !string.IsNullOrWhiteSpace(Ssn);
            string? first = FirstName;
            string? last = LastName;
            string? dobInput = DateOfBirth;

            if (!usingSsn && (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) || string.IsNullOrWhiteSpace(dobInput)))
            {
                LocalizedString msg = _valLoc[name: InputValidation.Errors.ProvideSsnOrNameDob];
                ModelState.AddModelError(string.Empty, msg);
                Errors = [msg];
                return Page();
            }

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
                PeoplePublicInfoResponseXmlPretty = PeopleXmlParser.FormatPretty(xml);
                Results = PeopleXmlParser.ParsePeopleList(xml);

                string key = GetScopedCacheKey();
                _ = _cache.Set(key, Results, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            catch (Exception ex)
            {
                LogPeopleSearchPostError(_logger, ex);
                Errors.Add(GetFriendlyError());
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

        private List<string> ReadEnabledIncludeKeys()
        {
            List<string> list = [];
            IConfigurationSection incSec = _config.GetSection("Operations:GetPerson:Request:Include");
            foreach (IConfigurationSection c in incSec.GetChildren())
            {
                if (bool.TryParse(c.Value, out bool on) && on)
                {
                    list.Add(c.Key);
                }
            }
            // Also read flags enum if configured that way
            GetPersonRequestOptions? req = _config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>();
            if (req is not null && req.Include != GetPersonInclude.None)
            {
                foreach (GetPersonInclude flag in Enum.GetValues<GetPersonInclude>())
                {
                    if (flag == GetPersonInclude.None)
                    {
                        continue;
                    }

                    if ((req.Include & flag) == flag)
                    {
                        string name = Enum.GetName(flag) ?? string.Empty;
                        if (!string.IsNullOrEmpty(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            list.Add(name);
                        }
                    }
                }
            }
            return list;
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
                List<(string Key, string Value)> pairs = PeopleXmlParser.FlattenResponse(xml);

                // Filter out any RequestHeader/requestBody
                List<(string Key, string Value)> filtered = [.. pairs
                    .Where(p =>
                    {
                        if (string.IsNullOrEmpty(p.Key))
                        {
                            return true;
                        }

                        string k = p.Key;
                        return !(k.StartsWith("requestheader", StringComparison.OrdinalIgnoreCase)
                              || k.StartsWith("requestbody", StringComparison.OrdinalIgnoreCase)
                              || k.Contains(".requestheader", StringComparison.OrdinalIgnoreCase)
                              || k.Contains(".requestbody", StringComparison.OrdinalIgnoreCase));
                    })
                    .Where(p =>
                    {
                        if (string.IsNullOrEmpty(p.Key))
                        {
                            return true;
                        }

                        string key = p.Key;
                        int lastDot = key.LastIndexOf('.');
                        string sub = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                        int bpos = sub.IndexOf('[');
                        if (bpos >= 0)
                        {
                            sub = sub[..bpos];
                        }
                        string s = sub.ToLowerInvariant();
                        return s is not "id" and not "fixed" and not "authoritycode" and not "personaddressid";
                    })];

                // Allow-list from config
                HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);
                IConfigurationSection incSec = _config.GetSection("Operations:GetPerson:Request:Include");
                foreach (IConfigurationSection c in incSec.GetChildren())
                {
                    if (bool.TryParse(c.Value, out bool on) && on)
                    {
                        _ = allowed.Add(c.Key);
                    }
                }
                GetPersonRequestOptions? req = _config.GetSection("Operations:GetPerson:Request").Get<GetPersonRequestOptions>();
                if (req is not null && req.Include != GetPersonInclude.None)
                {
                    foreach (GetPersonInclude flag in Enum.GetValues<GetPersonInclude>())
                    {
                        if (flag == GetPersonInclude.None)
                        {
                            continue;
                        }

                        if ((req.Include & flag) == flag)
                        {
                            string? name = Enum.GetName(flag);
                            if (!string.IsNullOrEmpty(name))
                            {
                                _ = allowed.Add(name);
                            }
                        }
                    }
                }
                _ = allowed.Add("Summary");
                if (allowed.Contains("Ssn"))
                {
                    _ = allowed.Add("SSN");
                }

                if (allowed.Count > 0)
                {
                    static string RootOf(string segment)
                    {
                        if (string.IsNullOrEmpty(segment))
                        {
                            return segment;
                        }

                        int b = segment.IndexOf('[');
                        return b >= 0 ? segment[..b] : segment;
                    }

                    static bool Matches(string seg, string allowedKey)
                    {
                        return seg.Equals(allowedKey, StringComparison.OrdinalIgnoreCase)
                            || seg.StartsWith(allowedKey, StringComparison.OrdinalIgnoreCase)
                            || allowedKey.StartsWith(seg, StringComparison.OrdinalIgnoreCase);
                    }

                    filtered = [.. filtered.Where(p =>
                    {
                        if (string.IsNullOrWhiteSpace(p.Key))
                        {
                            return false;
                        }

                        string[] parts = p.Key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (string part in parts)
                        {
                            string seg = RootOf(part);
                            foreach (string a in allowed)
                            {
                                if (Matches(seg, a))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    })];
                }

                return new JsonResult(new
                {
                    ok = true,
                    publicId,
                    raw = xml,
                    pretty = PeopleXmlParser.FormatPretty(xml),
                    details = filtered.Select(p => new { key = p.Key, value = p.Value }).ToArray()
                });
            }
            catch (Exception ex)
            {
                LogPersonDetailsError(_logger, publicId ?? string.Empty, ex);
                return new JsonResult(new { ok = false, error = GetFriendlyError() });
            }
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Error loading person details for PublicId={PublicId}")]
        static partial void LogPersonDetailsError(ILogger logger, string PublicId, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Error executing people search (hasSsn={HasSsn})")]
        static partial void LogPeopleSearchError(ILogger logger, bool HasSsn, Exception ex);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Error executing people search (POST)")]
        static partial void LogPeopleSearchPostError(ILogger logger, Exception ex);
    }
}