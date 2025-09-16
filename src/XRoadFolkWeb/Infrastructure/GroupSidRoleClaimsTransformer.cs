using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Security.Principal;

namespace XRoadFolkWeb.Infrastructure;

/// <summary>
/// Populates role claims based on Windows group SIDs and optional configured group names.
/// Supports implicit admin mapping via Builtin/Domain Administrators when enabled in options.
/// </summary>
public sealed class GroupSidRoleClaimsTransformer : IClaimsTransformation
{
    private readonly IMemoryCache _cache;
    private readonly RoleMappingOptions _opts;
    private readonly ILogger<GroupSidRoleClaimsTransformer>? _log;
    private const string CachePrefix = "sidroles|";

    private static readonly string[] _builtinAdmins = new[] { "S-1-5-32-544" }; // Builtin Administrators only

    public GroupSidRoleClaimsTransformer(IMemoryCache cache, IOptions<RoleMappingOptions> opts, ILogger<GroupSidRoleClaimsTransformer>? log = null)
    { _cache = cache ?? throw new ArgumentNullException(nameof(cache)); _opts = (opts ?? throw new ArgumentNullException(nameof(opts))).Value; _log = log; }

    /// <summary>
    /// Transforms the claims principal by adding role claims based on group SIDs and names.
    /// </summary>
    /// <param name="principal">The claims principal to transform.</param>
    /// <returns>The task object representing the asynchronous operation, with the transformed claims principal.</returns>
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal!);
        }
        if (principal.Identity is not ClaimsIdentity id)
        {
            return Task.FromResult(principal!);
        }
        if (id.HasClaim(c => c.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal!);
        }

        HashSet<string> allowed = new((_opts.AllowedRoles ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

        string[] groupSids = principal.FindAll(ClaimTypes.GroupSid).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] groupNames = Array.Empty<string>();
        try
        {
            if (OperatingSystem.IsWindows() && principal.Identity is WindowsIdentity win && win.Groups is not null)
            {
                var list = new List<string>();
                foreach (var sid in win.Groups)
                {
                    try
                    {
                        var nt = sid.Translate(typeof(NTAccount)) as NTAccount;
                        if (nt != null)
                        {
                            list.Add(nt.Value);
                        }
                    }
                    catch { }
                }
                groupNames = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        catch { }

        string cacheKey = CachePrefix + string.Join('|', groupSids.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) + "|names:" + string.Join('|', groupNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        string[] roles = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Implicit admin via Windows groups per mode
                if (allowed.Contains(AppRoles.Admin) && _opts.ImplicitWindowsAdminMode != ImplicitAdminMode.None)
                {
                    bool builtin = groupSids.Any(IsBuiltinAdminSid);
                    bool domain = groupSids.Any(IsDomainAdminSid);
                    if ((builtin && (_opts.ImplicitWindowsAdminMode == ImplicitAdminMode.BuiltinOnly || _opts.ImplicitWindowsAdminMode == ImplicitAdminMode.BuiltinAndDomain))
                        || (domain && _opts.ImplicitWindowsAdminMode == ImplicitAdminMode.BuiltinAndDomain))
                    {
                        found.Add(AppRoles.Admin);
                    }
                }

                // Admin/User via configured group names (if provided)
                if (groupNames.Length > 0)
                {
                    foreach (var g in _opts.AdminGroupNames ?? new())
                    {
                        if (groupNames.Contains(g, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(AppRoles.Admin);
                            break;
                        }
                    }
                    foreach (var g in _opts.UserGroupNames ?? new())
                    {
                        if (groupNames.Contains(g, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(AppRoles.User);
                            break;
                        }
                    }
                }

                if (allowed.Contains(AppRoles.User) && !found.Contains(AppRoles.Admin) && !found.Contains(AppRoles.User))
                {
                    found.Add(AppRoles.User);
                }
            }
            catch (Exception ex) { _log?.LogDebug(ex, "Group SID/Name role mapping failed"); }
            return found.Where(r => allowed.Contains(r)).ToArray();
        }) ?? Array.Empty<string>();

        foreach (var r in roles) { id.AddClaim(new Claim(ClaimTypes.Role, r)); }
        return Task.FromResult(principal!);
    }

    private static bool IsBuiltinAdminSid(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }
        return _builtinAdmins.Contains(sid, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDomainAdminSid(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid)) { return false; }
        return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && (sid.EndsWith("-512", StringComparison.Ordinal) || sid.EndsWith("-519", StringComparison.Ordinal));
    }
}
