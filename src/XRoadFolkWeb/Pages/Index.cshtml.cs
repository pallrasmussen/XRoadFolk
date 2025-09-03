using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Features.Index;
using XRoadFolkWeb.Features.People;
using XRoadFolkWeb.Validation;
using PersonRow = XRoadFolkWeb.Features.People.PersonRow;
using XRoadFolkWeb.Extensions;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;

namespace XRoadFolkWeb.Pages
{
    [RequireSsnOrNameDob(nameof(Ssn), nameof(FirstName), nameof(LastName), nameof(DateOfBirth))]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed partial class IndexModel(
        PeopleSearchCoordinator search,
        PersonDetailsProvider details,
        IStringLocalizer<InputValidation> valLoc,
        IStringLocalizer<IndexModel> loc,
        IConfiguration config,
        PeopleResponseParser parser,
        ILogger<IndexModel> logger,
        IHostEnvironment env,
        ILogger<SharedResource> featureLogger) : PageModel
    {
        private readonly PeopleSearchCoordinator _search = search;
        private readonly PersonDetailsProvider _details = details;
        private readonly IStringLocalizer<InputValidation> _valLoc = valLoc;
        private readonly IStringLocalizer<IndexModel> _loc = loc;
        private readonly IConfiguration _config = config;
        private readonly PeopleResponseParser _parser = parser;
        private readonly ILogger<IndexModel> _logger = logger;
        private readonly IHostEnvironment _env = env;
        private readonly ILogger _featureLog = featureLogger;

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

        public IReadOnlyList<PersonRow> Results { get; private set; } = [];
        public IReadOnlyList<(string Key, string Value)>? PersonDetails { get; private set; }
        public string SelectedNameSuffix { get; private set; } = string.Empty;

        private List<string> _errors = [];
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>
        /// Holds full GetPeoplePublicInfo response (raw + pretty)
        /// </summary>
        public string? PeoplePublicInfoResponseXml { get; private set; }
        public string? PeoplePublicInfoResponseXmlPretty { get; private set; }

        /// <summary>
        /// Expose enabled include keys to the view (lazy-loaded once per request) as read-only
        /// </summary>
        private IReadOnlyList<string>? _enabledPersonIncludeKeys;
        public IReadOnlyList<string> EnabledPersonIncludeKeys => _enabledPersonIncludeKeys ??= BuildEnabledIncludeKeys();

        private IReadOnlyList<string> BuildEnabledIncludeKeys()
        {
            var baseKeys = IncludeConfigHelper.GetEnabledIncludeKeys(_config);
            HashSet<string> list = new(baseKeys, StringComparer.OrdinalIgnoreCase) { "Person", "Names" };
            return [.. list];
        }

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
        private static partial Regex PublicIdRegex();

        private static bool IsValidPublicId(string s)
        {
            return !string.IsNullOrWhiteSpace(s) && PublicIdRegex().IsMatch(s);
        }

        private string BuildUserError(Exception ex)
        {
            bool detailed = _config.GetBoolOrDefault("Features:DetailedErrors", _env.IsDevelopment(), _featureLog);
            if (detailed)
            {
                string msg = ex.Message;
                if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                {
                    msg += " | " + ex.InnerException.Message;
                }
                string traceId = HttpContext?.TraceIdentifier ?? string.Empty;
                return string.IsNullOrWhiteSpace(traceId) ? msg : $"{msg} (TraceId: {traceId})";
            }

            LocalizedString l = _loc["UnexpectedError"];
            return l.ResourceNotFound ? "An unexpected error occurred." : l.Value;
        }

        public async Task OnGetAsync(
            string? publicId = null,
            string? ssn = null,
            string? firstName = null,
            string? lastName = null,
            string? dateOfBirth = null)
        {
            PrefillFromQuery(ssn, firstName, lastName, dateOfBirth);

            await LoadPersonDetailsIfRequestedAsync(publicId, HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false);

            if (!HasCriteria())
            {
                return;
            }

            (bool Ok, IReadOnlyList<string> Errs, string? SsnNorm, DateTimeOffset? Dob) vc = ValidateQueryCriteria();
            if (!vc.Ok)
            {
                LogValidationFailed(_logger, vc.Errs.Count);
                _errors.Clear();
                _errors.AddRange(vc.Errs);
                return;
            }

            try
            {
                await PerformSearchAsync(vc.SsnNorm, vc.Dob, HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSearchError(_logger, ex);
                _errors.Add(BuildUserError(ex));
            }
        }

        private void PrefillFromQuery(string? ssn, string? firstName, string? lastName, string? dateOfBirth)
        {
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
        }

        private async Task LoadPersonDetailsIfRequestedAsync(string? publicId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(publicId))
            {
                return;
            }

            if (!IsValidPublicId(publicId))
            {
                LocalizedString msg = _loc["InvalidPublicId"]; // optional localization key
                _errors.Add(msg.ResourceNotFound ? "Invalid publicId." : msg.Value);
                return;
            }

            try
            {
                (IReadOnlyList<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix) res = await _details.GetAsync(publicId, _loc, EnabledPersonIncludeKeys, ct).ConfigureAwait(false);
                PersonDetails = res.Details;
                SelectedNameSuffix = res.SelectedNameSuffix;
            }
            catch (Exception ex)
            {
                LogPersonDetailsError(_logger, ex, publicId);
                _errors.Add(BuildUserError(ex));
            }
        }

        private bool HasCriteria()
        {
            return !string.IsNullOrWhiteSpace(Ssn)
                || !string.IsNullOrWhiteSpace(FirstName)
                || !string.IsNullOrWhiteSpace(LastName)
                || !string.IsNullOrWhiteSpace(DateOfBirth);
        }

        private (bool Ok, IReadOnlyList<string> Errs, string? SsnNorm, DateTimeOffset? Dob) ValidateQueryCriteria()
        {
            var res = InputValidation.ValidateCriteria(Ssn, FirstName, LastName, DateOfBirth, _valLoc);
            return (res.Ok, res.Errors, res.SsnNorm, res.Dob);
        }

        private async Task PerformSearchAsync(string? ssnNorm, DateTimeOffset? dob, CancellationToken ct)
        {
            (string Xml, string Pretty, IReadOnlyList<PersonRow> Results) res = await _search.SearchAsync(ssnNorm ?? string.Empty, FirstName, LastName, dob, ct).ConfigureAwait(false);
            PeoplePublicInfoResponseXml = res.Xml;
            PeoplePublicInfoResponseXmlPretty = res.Pretty;
            Results = res.Results;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Clear person details on every new search
            PersonDetails = null;
            SelectedNameSuffix = string.Empty;

            if (!ModelState.IsValid)
            {
                int count = ModelState.Values.Sum(v => v.Errors.Count);
                LogValidationFailed(_logger, count);
                _errors.Clear();
                _errors.AddRange(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
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
                _errors.Clear();
                _errors.Add(msg);
                return Page();
            }

            // Only validate the chosen path to avoid spurious cross-field errors
            (bool ok, IReadOnlyList<string> errs, string? ssnNorm, DateTimeOffset? dob) =
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

                _errors.Clear();
                _errors.AddRange(errs);
                return Page();
            }

            try
            {
                await PerformSearchAsync(ssnNorm, dob, HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSearchError(_logger, ex);
                _errors.Add(BuildUserError(ex));
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
            _errors.Clear();
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
                (IReadOnlyList<(string Key, string Value)> Details, string Pretty, string SelectedNameSuffix) res = await _details.GetAsync(publicId, _loc, EnabledPersonIncludeKeys, HttpContext?.RequestAborted ?? CancellationToken.None);
                return new JsonResult(new
                {
                    ok = true,
                    publicId,
                    pretty = res.Pretty,
                    details = res.Details.Select(p => new { key = p.Key, value = p.Value }).ToArray(),
                });
            }
            catch (Exception ex)
            {
                LogPersonDetailsError(_logger, ex, publicId!);
                // Reuse shared error builder for consistency and to avoid duplication
                return new JsonResult(new { ok = false, error = BuildUserError(ex) });
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