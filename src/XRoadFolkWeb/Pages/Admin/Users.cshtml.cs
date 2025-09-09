using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XRoadFolkWeb.Infrastructure;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace XRoadFolkWeb.Pages.Admin;

[ValidateAntiForgeryToken]
[Authorize(Policy = "AdminOnly")]
public class UsersModel : PageModel
{
    private readonly IAppRoleStore _store;
    private readonly IAccountLookup _acct;
    private readonly RoleMappingOptions _opts;
    public UsersModel(IAppRoleStore store, IAccountLookup acct, IOptions<RoleMappingOptions> opts)
    { ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(acct); ArgumentNullException.ThrowIfNull(opts); _store = store; _acct = acct; _opts = opts.Value; }

    [BindProperty]
    public string Sam { get; set; } = string.Empty;
    [BindProperty]
    public string Role { get; set; } = AppRoles.User; // default to User

    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }
    [BindProperty(SupportsGet = true)] public string? SearchUser { get; set; }
    [BindProperty(SupportsGet = true)] public string? SearchRole { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; } // user_asc | user_desc | roles_desc | roles_asc
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public int TotalUsers { get; private set; }
    public int TotalPages { get; private set; }

    public IReadOnlyDictionary<string, HashSet<string>> Users { get; private set; } = new Dictionary<string, HashSet<string>>();
    public Dictionary<string, IReadOnlyCollection<string>> DeletedRoles { get; private set; } = new();
    public string? Message { get; private set; }

    private IEnumerable<KeyValuePair<string, HashSet<string>>> ApplyFilters(IEnumerable<KeyValuePair<string, HashSet<string>>> data)
    {
        if (!string.IsNullOrWhiteSpace(SearchUser))
        {
            string term = SearchUser.Trim();
            data = data.Where(kv => kv.Key.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(SearchRole))
        {
            string rterm = SearchRole.Trim();
            data = data.Where(kv => kv.Value.Any(r => r.Contains(rterm, StringComparison.OrdinalIgnoreCase)));
        }
        return data;
    }

    private IEnumerable<KeyValuePair<string, HashSet<string>>> ApplySort(IEnumerable<KeyValuePair<string, HashSet<string>>> data)
    {
        return Sort switch
        {
            "user_desc" => data.OrderByDescending(k => k.Key, StringComparer.OrdinalIgnoreCase),
            "roles_asc" => data.OrderBy(k => k.Value.Count).ThenBy(k=>k.Key, StringComparer.OrdinalIgnoreCase),
            "roles_desc" => data.OrderByDescending(k => k.Value.Count).ThenBy(k=>k.Key, StringComparer.OrdinalIgnoreCase),
            _ => data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void Load()
    {
        // base snapshot
        var snap = _store.Snapshot();
        IEnumerable<KeyValuePair<string, HashSet<string>>> query = snap;
        query = ApplyFilters(query);
        query = ApplySort(query);

        PageSize = Math.Clamp(PageSize, 1, 500);
        PageNumber = Math.Max(1, PageNumber);
        TotalUsers = query.Count();
        TotalPages = TotalUsers == 0 ? 1 : (int)Math.Ceiling(TotalUsers / (double)PageSize);
        if (PageNumber > TotalPages) { PageNumber = TotalPages; }
        int skip = (PageNumber - 1) * PageSize;
        var pageItems = (skip >= TotalUsers) ? Enumerable.Empty<KeyValuePair<string, HashSet<string>>>() : query.Skip(skip).Take(PageSize);
        Users = pageItems.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        if (ShowDeleted)
        {
            DeletedRoles = Users.Keys.ToDictionary(k => k, k => (IReadOnlyCollection<string>)_store.GetDeletedRoles(k));
        }
        else
        {
            DeletedRoles = new();
        }
    }

    public void OnGet() => Load();

    public IActionResult OnPostAdd()
    {
        if (!string.IsNullOrWhiteSpace(Sam) && !string.IsNullOrWhiteSpace(Role))
        {
            string sam = Sam.Trim();
            string role = Role.Trim();
            bool exists = _acct.Exists(sam);
            if (!exists && _opts.EnforceDirectoryUserExists)
            {
                Message = $"Directory account '{sam}' not found. User not added.";
            }
            else
            {
                if (!exists)
                {
                    Message = $"Directory account '{sam}' not found (enforcement disabled) – adding anyway.";
                }
                _store.AddToRole(sam, role, User?.Identity?.Name);
                if (exists) { Message = $"Added {sam} to role {role}"; }
            }
        }
        Load();
        return Page();
    }

    public IActionResult OnPostRemoveRole([FromForm] string sam, [FromForm] string role, [FromForm] bool? showDeleted)
    {
        ShowDeleted = showDeleted ?? ShowDeleted;
        if (!string.IsNullOrWhiteSpace(sam) && !string.IsNullOrWhiteSpace(role))
        {
            bool removed = _store.RemoveFromRole(sam.Trim(), role.Trim(), User?.Identity?.Name);
            Message = removed ? $"Removed role {role} from {sam}" : $"Role {role} not found for {sam}";
        }
        Load();
        return Page();
    }

    public IActionResult OnPostRemoveUser([FromForm] string sam, [FromForm] bool? showDeleted)
    {
        ShowDeleted = showDeleted ?? ShowDeleted;
        if (!string.IsNullOrWhiteSpace(sam))
        {
            bool removed = _store.RemoveUser(sam.Trim(), User?.Identity?.Name);
            Message = removed ? $"Removed user {sam}" : $"User {sam} not found";
        }
        Load();
        return Page();
    }

    public IActionResult OnPostRestoreRole([FromForm] string sam, [FromForm] string role)
    {
        ShowDeleted = true; // ensure remains visible after restore
        if (!string.IsNullOrWhiteSpace(sam) && !string.IsNullOrWhiteSpace(role))
        {
            bool ok = _store.RestoreRole(sam.Trim(), role.Trim(), User?.Identity?.Name);
            Message = ok ? $"Restored role {role} for {sam}" : $"Could not restore role {role} for {sam}";
        }
        Load();
        return Page();
    }

    public IActionResult OnPostPurge([FromForm] int days)
    {
        ShowDeleted = true;
        int d = Math.Clamp(days, 0, 3650);
        TimeSpan span = TimeSpan.FromDays(d);
        int purged = _store.PurgeDeleted(span, User?.Identity?.Name);
        Message = purged > 0 ? $"Purged {purged} deleted role assignments older than {d} days" : $"No deleted roles older than {d} days to purge";
        Load();
        return Page();
    }

    public IActionResult OnGetExport(string format = "csv")
    {
        format ??= "csv";
        Load();
        var users = _store.GetAllUsers();
        var rows = new List<(string User,string[] Active,string[] Deleted)>();
        foreach (var u in users)
        {
            var active = _store.GetRoles(u).ToArray();
            var deleted = _store.GetDeletedRoles(u).ToArray();
            rows.Add((u, active, deleted));
        }
        format = format.ToLowerInvariant();
        if (format == "json")
        {
            return new JsonResult(rows.Select(r => new { user = r.User, active = r.Active, deleted = r.Deleted }));
        }
        var sb = new StringBuilder();
        sb.AppendLine("User,ActiveRoles,DeletedRoles");
        foreach (var r in rows.OrderBy(r=>r.User,StringComparer.OrdinalIgnoreCase))
        {
            string esc(string s) => '"'+ s.Replace('"','\"') + '"';
            sb.Append(esc(r.User)).Append(',')
              .Append(esc(string.Join(';', r.Active)))
              .Append(',')
              .Append(esc(string.Join(';', r.Deleted)))
              .Append('\n');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"roles-export-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
