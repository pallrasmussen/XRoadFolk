using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Validation;
using XRoadFolkWeb.Features.People;
using XRoadFolkWeb.Features.Index;
using System.Text.RegularExpressions;
using PersonRow = XRoadFolkWeb.Features.People.PersonRow;

namespace XRoadFolkWeb.Pages
{
    [RequireSsnOrNameDob(nameof(Ssn), nameof(FirstName), nameof(LastName), nameof(DateOfBirth))]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public partial class IndexModel(
        PeopleSearchCoordinator search,
        PersonDetailsProvider details,
        IStringLocalizer<InputValidation> valLoc,
        IStringLocalizer<IndexModel> loc,
        IConfiguration config,
        PeopleResponseParser parser,
        ILogger<IndexModel> logger) : PageModel
    {
        private readonly PeopleSearchCoordinator _search = search;
        private readonly PersonDetailsProvider _details = details;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<IndexModel> _loc = loc;
        private readonly IConfiguration _config = config;
        private readonly PeopleResponseParser _parser = parser;
        private readonly ILogger<IndexModel> _logger = logger;

        [BindProperty, Ssn, TrimDigits]
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

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        private static partial Regex PublicIdRegex();

        private static bool IsValidPublicId(string s)
        {
            return !string.IsNullOrWhiteSpace(s) && PublicIdRegex().IsMatch(s);
        }

        public async Task OnGetAsync(
            string? publicId = null,
            string? ssn = null,
            string? firstName = null,
            string? lastName = null,
            string? dateOfBirth = null)
        {
            EnabledPersonIncludeKeys = [.. IncludeConfigHelper.GetEnabledIncludeKeys(_config)];

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
                if (!IsValidPublicId(publicId))
                {
                    LocalizedString msg = _loc["InvalidPublicId"]; // optional localization key
                    Errors.Add(msg.ResourceNotFound ? "Invalid publicId." : msg.Value);
                }
                else
                {
                    try
                    {
                        (List<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix) res = await _details.GetAsync(publicId, _loc, EnabledPersonIncludeKeys, HttpContext?.RequestAborted ?? CancellationToken.None);
                        PersonDetails = res.Details;
                        SelectedNameSuffix = res.SelectedNameSuffix;
                    }
                    catch (Exception ex)
                    {
                        LogPersonDetailsError(_logger, ex, publicId);
                        LocalizedString msg = _loc["UnexpectedError"];
                        Errors.Add(msg.ResourceNotFound ? "An unexpected error occurred." : msg.Value);
                    }
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
                    LogValidationFailed(_logger, errs.Count);
                    Errors = errs;
                    return;
                }

                try
                {
                    (string Xml, string Pretty, List<PersonRow> Results) res = await _search.SearchAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob, HttpContext?.RequestAborted ?? CancellationToken.None);
                    PeoplePublicInfoResponseXml = res.Xml;
                    PeoplePublicInfoResponseXmlPretty = res.Pretty;
                    Results = res.Results;
                }
                catch (Exception ex)
                {
                    LogSearchError(_logger, ex);
                    LocalizedString msg = _loc["UnexpectedError"];
                    Errors.Add(msg.ResourceNotFound ? "An unexpected error occurred." : msg.Value);
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Clear person details on every new search
            PersonDetails = null;
            SelectedNameSuffix = string.Empty;

            // Load enabled include keys once per request
            EnabledPersonIncludeKeys = [.. IncludeConfigHelper.GetEnabledIncludeKeys(_config)];

            if (!ModelState.IsValid)
            {
                int count = ModelState.Values.SelectMany(v => v.Errors).Count();
                LogValidationFailed(_logger, count);
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
                LogValidationFailed(_logger, 1);
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
                LogValidationFailed(_logger, errs.Count);
                foreach (string err in errs)
                {
                    ModelState.AddModelError(string.Empty, err);
                }

                Errors = errs;
                return Page();
            }

            try
            {
                (string Xml, string Pretty, List<PersonRow> Results) res = await _search.SearchAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob, HttpContext?.RequestAborted ?? CancellationToken.None);
                PeoplePublicInfoResponseXml = res.Xml;
                PeoplePublicInfoResponseXmlPretty = res.Pretty;
                Results = res.Results;
            }
            catch (Exception ex)
            {
                LogSearchError(_logger, ex);
                LocalizedString msg = _loc["UnexpectedError"];
                Errors.Add(msg.ResourceNotFound ? "An unexpected error occurred." : msg.Value);
                return Page();
            }

            // Do not leak criteria via query string; render results directly
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

        public async Task<IActionResult> OnGetPersonDetailsAsync(string? publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId))
            {
                LocalizedString msg = _loc["MissingPublicId"]; // expects localization resource key
                string text = msg.ResourceNotFound ? "Missing publicId." : msg.Value;
                return BadRequest(new { ok = false, error = text });
            }

            if (!IsValidPublicId(publicId))
            {
                LocalizedString msg = _loc["InvalidPublicId"]; // expects localization resource key
                string text = msg.ResourceNotFound ? "Invalid publicId." : msg.Value;
                return BadRequest(new { ok = false, error = text });
            }

            try
            {
                (List<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix) res = await _details.GetAsync(publicId, _loc, EnabledPersonIncludeKeys, HttpContext?.RequestAborted ?? CancellationToken.None);
                return new JsonResult(new
                {
                    ok = true,
                    publicId,
                    raw = string.Empty, // raw xml not returned here
                    pretty = res.Pretty,
                    details = res.Details.Select(p => new { key = p.Key, value = p.Value }).ToArray()
                });
            }
            catch (Exception ex)
            {
                LogPersonDetailsError(_logger, ex, publicId!);
                LocalizedString msg = _loc["UnexpectedError"];
                string text = msg.ResourceNotFound ? "An unexpected error occurred." : msg.Value;
                return new JsonResult(new { ok = false, error = text });
            }
        }

        [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Index: Failed to load person details for PublicId '{PublicId}'")]
        private static partial void LogPersonDetailsError(ILogger logger, Exception ex, string PublicId);

        [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Index: People search failed")]
        private static partial void LogSearchError(ILogger logger, Exception ex);

        [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Index: Input validation failed with {ErrorCount} errors")]
        private static partial void LogValidationFailed(ILogger logger, int ErrorCount);
    }
}