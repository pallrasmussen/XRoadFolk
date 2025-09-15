using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class SecurityModel : PageModel
{
    public string? UserName { get; private set; }
    public string AuthenticationType { get; private set; } = string.Empty;
    public bool IsAuthenticated { get; private set; }
    public IReadOnlyList<Claim> Claims { get; private set; } = Array.Empty<Claim>();
    public IReadOnlyList<(string Sid, string? Name)> Groups { get; private set; } = Array.Empty<(string, string?)>();
    public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
    public bool ImplicitWindowsAdminEnabled { get; private set; }
    public string ImplicitWindowsAdminMode { get; private set; } = string.Empty;

    private readonly RoleMappingOptions _roleOpts;
    public SecurityModel(IOptions<RoleMappingOptions> roleOpts)
    {
        ArgumentNullException.ThrowIfNull(roleOpts);
        _roleOpts = roleOpts.Value;
    }

    public void OnGet()
    {
        var principal = User;
        UserName = principal?.Identity?.Name;
        AuthenticationType = principal?.Identity?.AuthenticationType ?? string.Empty;
        IsAuthenticated = principal?.Identity?.IsAuthenticated == true;
        Claims = principal?.Claims?.OrderBy(c => c.Type, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Value, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<Claim>();
        Roles = Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList();
        ImplicitWindowsAdminEnabled = _roleOpts.ImplicitWindowsAdminMode != ImplicitAdminMode.None;
        ImplicitWindowsAdminMode = _roleOpts.ImplicitWindowsAdminMode.ToString();

        var groups = new List<(string Sid, string? Name)>();
        // Windows-specific enumeration guarded to avoid CA1416 warnings
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (principal?.Identity is WindowsIdentity win && win.Groups is not null)
                {
                    foreach (var sid in win.Groups)
                    {
                        try
                        {
                            string? sidValue = sid?.Value;
                            if (string.IsNullOrWhiteSpace(sidValue))
                            {
                                continue;
                            }
                            string? name = null;
                            try
                            {
                                name = sid?.Translate(typeof(NTAccount))?.Value;
                            }
                            catch { }
                            groups.Add((sidValue, name));
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        var claimSids = Claims.Where(c => c.Type == ClaimTypes.GroupSid).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v));
        foreach (var sidValue in claimSids)
        {
            if (!groups.Any(g => string.Equals(g.Sid, sidValue, StringComparison.OrdinalIgnoreCase)))
            {
                string? name = null;
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var si = new System.Security.Principal.SecurityIdentifier(sidValue);
                        try { name = si.Translate(typeof(NTAccount)).Value; } catch { }
                    }
                    catch { }
                }
                groups.Add((sidValue, name));
            }
        }
        Groups = groups.OrderBy(g => g.Sid, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
