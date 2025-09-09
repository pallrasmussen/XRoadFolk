using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XRoadFolkWeb.Infrastructure;
using System.Text;

namespace XRoadFolkWeb.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class RoleAuditModel : PageModel
{
    private readonly IRoleAuditSink _audit;

    public RoleAuditModel(IRoleAuditSink audit) => _audit = audit;

    [BindProperty(SupportsGet = true)] public int Max { get; set; } = 200;
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 50;
    [BindProperty(SupportsGet = true)] public string? FilterUser { get; set; }
    [BindProperty(SupportsGet = true)] public string? FilterRole { get; set; }
    [BindProperty(SupportsGet = true)] public string? FilterAction { get; set; }
    [BindProperty(SupportsGet = true)] public bool? FilterSuccess { get; set; }

    public IReadOnlyList<RoleAuditEntry> Entries { get; private set; } = Array.Empty<RoleAuditEntry>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }

    public void OnGet() => Load();

    private IEnumerable<RoleAuditEntry> ApplyFilters(IEnumerable<RoleAuditEntry> src)
    {
        if (!string.IsNullOrWhiteSpace(FilterUser)) { src = src.Where(e => e.User.Contains(FilterUser, StringComparison.OrdinalIgnoreCase)); }
        if (!string.IsNullOrWhiteSpace(FilterRole)) { src = src.Where(e => (e.Role ?? string.Empty).Contains(FilterRole, StringComparison.OrdinalIgnoreCase)); }
        if (!string.IsNullOrWhiteSpace(FilterAction)) { src = src.Where(e => e.Action.Contains(FilterAction, StringComparison.OrdinalIgnoreCase)); }
        if (FilterSuccess.HasValue) { src = src.Where(e => e.Success == FilterSuccess.Value); }
        return src;
    }

    private void Load()
    {
        Max = Math.Clamp(Max, 1, 5000);
        PageSize = Math.Clamp(PageSize, 1, 500);
        PageNumber = Math.Max(1, PageNumber);
        var snapshot = _audit.Snapshot(Max);
        var filtered = ApplyFilters(snapshot).ToList();
        Total = filtered.Count;
        TotalPages = Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }
        int skip = (PageNumber - 1) * PageSize;
        Entries = (skip >= Total ? Array.Empty<RoleAuditEntry>() : filtered.Skip(skip).Take(PageSize).ToList());
    }

    public IActionResult OnGetDownload(int? max, string? format, string? filterUser, string? filterRole, string? filterAction, bool? filterSuccess)
    {
        Max = Math.Clamp(max ?? Max, 1, 5000);
        FilterUser = filterUser; FilterRole = filterRole; FilterAction = filterAction; FilterSuccess = filterSuccess;
        var list = _audit.Snapshot(Max);
        var filtered = ApplyFilters(list).ToList();
        format = (format ?? "json").Trim().ToLowerInvariant();
        if (format == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendLine("UtcTimestamp,Action,User,Role,Actor,Success,Details");
            foreach (var e in filtered)
            {
                static string esc(string? v) => '"' + (v ?? string.Empty).Replace('"', '\"') + '"';
                sb.Append(esc(e.UtcTimestamp.ToString("o"))).Append(',')
                  .Append(esc(e.Action)).Append(',')
                  .Append(esc(e.User)).Append(',')
                  .Append(esc(e.Role)).Append(',')
                  .Append(esc(e.Actor)).Append(',')
                  .Append(e.Success ? "true" : "false").Append(',')
                  .Append(esc(e.Details)).Append('\n');
            }
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"role-audit-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        return new JsonResult(filtered);
    }
}
