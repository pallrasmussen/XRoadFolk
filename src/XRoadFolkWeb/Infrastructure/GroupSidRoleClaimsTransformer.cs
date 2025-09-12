using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace XRoadFolkWeb.Infrastructure;

public sealed class GroupSidRoleClaimsTransformer : IClaimsTransformation
{
    private readonly IMemoryCache _cache;
    private readonly RoleMappingOptions _opts;
    private readonly ILogger<GroupSidRoleClaimsTransformer>? _log;
    private const string CachePrefix = "sidroles|";

    private static readonly string[] _defaultAdminSidPatterns = new[]
    {
        "S-1-5-32-544", // Builtin Administrators
    };

    public GroupSidRoleClaimsTransformer(IMemoryCache cache, IOptions<RoleMappingOptions> opts, ILogger<GroupSidRoleClaimsTransformer>? log = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(opts);
        _cache = cache; _opts = opts.Value; _log = log;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal);
        }
        if (principal.Identity is not ClaimsIdentity id)
        {
            return Task.FromResult(principal);
        }
        if (id.HasClaim(c => c.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal); // existing roles (store) remain authoritative
        }

        var groupSids = principal.FindAll(ClaimTypes.GroupSid)
                                 .Select(c => c.Value)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToArray();
        if (groupSids.Length == 0)
        {
            return Task.FromResult(principal);
        }

        // Build allowed roles set (least privilege)
        HashSet<string> allowed = new((_opts.AllowedRoles ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

        string cacheKey = CachePrefix + string.Join('|', groupSids.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        string[] roles = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60); // short TTL
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (_opts.ImplicitWindowsAdminEnabled && allowed.Contains(AppRoles.Admin) && groupSids.Any(IsAdminSid))
                {
                    found.Add(AppRoles.Admin);
                }
                if (allowed.Contains(AppRoles.User) && !found.Contains(AppRoles.Admin))
                {
                    found.Add(AppRoles.User);
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Group SID role mapping failed");
            }
            return found.Where(r => allowed.Contains(r)).ToArray();
        }) ?? Array.Empty<string>();

        foreach (var r in roles)
        {
            id.AddClaim(new Claim(ClaimTypes.Role, r));
        }
        if (roles.Length > 0)
        {
            try { _log?.LogDebug("SID->Roles {Roles} user {User}", string.Join(',', roles), principal.Identity?.Name); } catch { }
        }
        return Task.FromResult(principal);
    }

    private static bool IsAdminSid(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }
        if (_defaultAdminSidPatterns.Contains(sid, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && (sid.EndsWith("-512", StringComparison.Ordinal) || sid.EndsWith("-519", StringComparison.Ordinal)))
        {
            return true;
        }
        return false;
    }
}
