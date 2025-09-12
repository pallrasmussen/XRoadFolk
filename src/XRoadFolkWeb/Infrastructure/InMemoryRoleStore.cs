#pragma warning disable IDE0011
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Caching.Memory;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace XRoadFolkWeb.Infrastructure;

public interface ICurrentUserAccessor
{
    string? Name { get; }
}

internal sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    public HttpContextCurrentUserAccessor(IHttpContextAccessor http) => _http = http;
    public string? Name => _http.HttpContext?.User?.Identity?.IsAuthenticated == true ? _http.HttpContext?.User?.Identity?.Name : null;
}

// Directory lookup options
public sealed class DirectoryLookupOptions
{
    public string? Domain { get; set; }
    public string? Container { get; set; } // e.g. OU=Users,DC=example,DC=com
    public string? Username { get; set; } // optional service account (sam / UPN)
    public string? Password { get; set; }
    public int CacheSeconds { get; set; } = 300; // positive cache TTL
    public int NegativeCacheSeconds { get; set; } = 60; // TTL for not-found entries
}

public interface IAccountLookup { bool Exists(string samOrUpn); }

internal sealed class DirectoryAccountLookup : IAccountLookup
{
    private readonly ILogger<DirectoryAccountLookup> _log;
    private readonly DirectoryLookupOptions _opts;
    private readonly IMemoryCache _cache;
    private static readonly ConcurrentDictionary<string, object> _keyLocks = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryAccountLookup(ILogger<DirectoryAccountLookup> log, IOptions<DirectoryLookupOptions> opts, IMemoryCache cache)
    { _log = log; _opts = opts.Value; _cache = cache; }

    public bool Exists(string samOrUpn)
    {
        if (string.IsNullOrWhiteSpace(samOrUpn)) return false;
        // If no domain configured treat as pass-through
        if (string.IsNullOrWhiteSpace(_opts.Domain)) return true;
        if (!OperatingSystem.IsWindows()) { _log.LogDebug("Directory lookup skipped (non-Windows) for {User}", samOrUpn); return true; }

        string key = "diruser:" + samOrUpn.ToLowerInvariant();
        if (_cache.TryGetValue(key, out bool cached)) return cached;

        object gate = _keyLocks.GetOrAdd(key, _ => new object());
        lock (gate)
        {
            if (_cache.TryGetValue(key, out cached)) return cached;
            bool exists = QueryDirectory(samOrUpn);
            int seconds = exists ? Math.Max(5, _opts.CacheSeconds) : Math.Max(5, _opts.NegativeCacheSeconds);
            _cache.Set(key, exists, TimeSpan.FromSeconds(seconds));
            return exists;
        }
    }

    private bool QueryDirectory(string samOrUpn)
    {
        if (!OperatingSystem.IsWindows()) return true; // fail open on non-Windows
        try
        {
            return WinQuery(samOrUpn);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Directory query failed for {User}; treating as not found.", samOrUpn);
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private bool WinQuery(string samOrUpn)
    {
        ContextType ctype = ContextType.Domain;
        using var ctx = CreateContext(ctype)!;
        if (ctx is null) return true;
        UserPrincipal? principal = samOrUpn.Contains('@')
            ? UserPrincipal.FindByIdentity(ctx, IdentityType.UserPrincipalName, samOrUpn)
            : UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, samOrUpn);
        bool ok = principal is not null;
        _log.LogDebug("Directory lookup {Result} for {User}", ok ? "HIT" : "MISS", samOrUpn);
        return ok;
    }

    [SupportedOSPlatform("windows")]
    private PrincipalContext? CreateContext(ContextType type)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_opts.Username) && !string.IsNullOrWhiteSpace(_opts.Password))
            {
                return string.IsNullOrWhiteSpace(_opts.Container)
                    ? new PrincipalContext(type, _opts.Domain, _opts.Username, _opts.Password)
                    : new PrincipalContext(type, _opts.Domain, _opts.Container, _opts.Username, _opts.Password);
            }
            return string.IsNullOrWhiteSpace(_opts.Container)
                ? new PrincipalContext(type, _opts.Domain)
                : new PrincipalContext(type, _opts.Domain, _opts.Container);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Create PrincipalContext failed (Domain={Domain})", _opts.Domain);
            return null;
        }
    }
}

public sealed record RoleAuditEntry(DateTime UtcTimestamp, string Action, string User, string? Role, string? Actor, bool Success, string? Details);

public interface IRoleAuditSink
{
    void Record(string action, string user, string role, string? actor = null, bool success = true, string? details = null);
    void RecordUserRemoval(string action, string user, string? actor = null, bool success = true, string? details = null);
    IReadOnlyList<RoleAuditEntry> Snapshot(int max = 1000);
}

internal sealed class RoleAuditSink : IRoleAuditSink
{
    private readonly ILogger<RoleAuditSink> _log;
    private readonly ConcurrentQueue<RoleAuditEntry> _entries = new();
    private const int MaxEntries = 5000;
    public RoleAuditSink(ILogger<RoleAuditSink> log) => _log = log;
    private void Add(RoleAuditEntry e) { _entries.Enqueue(e); while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { } }
    public void Record(string action, string user, string role, string? actor = null, bool success = true, string? details = null)
    { var entry = new RoleAuditEntry(DateTime.UtcNow, action, user, role, actor, success, details); Add(entry); _log.LogInformation("RoleAudit {Action} User={User} Role={Role} Actor={Actor} Success={Success} {Details}", action, user, role, actor ?? "?", success, details ?? ""); }
    public void RecordUserRemoval(string action, string user, string? actor = null, bool success = true, string? details = null)
    { var entry = new RoleAuditEntry(DateTime.UtcNow, action, user, null, actor, success, details); Add(entry); _log.LogInformation("RoleAudit {Action} User={User} Actor={Actor} Success={Success} {Details}", action, user, actor ?? "?", success, details ?? ""); }
    public IReadOnlyList<RoleAuditEntry> Snapshot(int max = 1000) => _entries.Reverse().Take(max).ToList();
}

public sealed class RoleMappingOptions
{
    public List<string> Admins { get; set; } = new();
    public List<string>? AdminPatterns { get; set; }
    public List<string> Users { get; set; } = new();
    public List<string>? UserPatterns { get; set; }
    public bool AutoAssignUser { get; set; } = true;
    public string? FilePath { get; set; }
    public string? Provider { get; set; }
    public string? ConnectionString { get; set; }
    public bool AuditEnabled { get; set; } = true;
    public bool EnforceDirectoryUserExists { get; set; }
    public bool ImplicitWindowsAdminEnabled { get; set; } = true; // new flag
    public string? UserOverridesFile { get; set; } // path to overrides JSON (User, ExtraRoles[], Disabled)
    public string[] AllowedRoles { get; set; } = new[] { AppRoles.Admin, AppRoles.User }; // least privilege allow-list
}

// ================= User Overrides (JSON) =================
public sealed record UserOverride(string User, IReadOnlyCollection<string> ExtraRoles, bool Disabled, DateTime ModifiedUtc);

public interface IUserOverrideStore
{
    UserOverride? Get(string user);
    void Upsert(string user, IEnumerable<string> extraRoles, bool disabled, string? actor = null);
    bool Remove(string user, string? actor = null);
    IReadOnlyCollection<UserOverride> Snapshot();
}

internal sealed class JsonUserOverrideStore : IUserOverrideStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, UserOverride> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<JsonUserOverrideStore> _log;

    public JsonUserOverrideStore(ILogger<JsonUserOverrideStore> log, IOptions<RoleMappingOptions> opts)
    {
        _log = log; var cfg = opts.Value;
        string baseDir = AppContext.BaseDirectory;
        string defaultDir = Path.Combine(baseDir, "data");
        Directory.CreateDirectory(defaultDir);
        _file = string.IsNullOrWhiteSpace(cfg.UserOverridesFile)
            ? Path.Combine(defaultDir, "user-overrides.json")
            : Path.GetFullPath(cfg.UserOverridesFile!);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var doc = JsonSerializer.Deserialize<List<UserOverrideDto>>(json) ?? new();
                _data = doc.Where(d => !string.IsNullOrWhiteSpace(d.User))
                    .Select(d => new UserOverride(d.User!, d.ExtraRoles?.Where(r=>!string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(), d.Disabled, d.ModifiedUtc == default ? DateTime.UtcNow : d.ModifiedUtc))
                    .ToDictionary(d => d.User, d => d, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Load user overrides failed");
        }
    }

    private void PersistUnsafe()
    {
        try
        {
            var export = _data.Values.OrderBy(v => v.User, StringComparer.OrdinalIgnoreCase)
                .Select(v => new UserOverrideDto
                {
                    User = v.User,
                    ExtraRoles = v.ExtraRoles.ToArray(),
                    Disabled = v.Disabled,
                    ModifiedUtc = v.ModifiedUtc
                }).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Persist user overrides failed");
        }
    }

    public UserOverride? Get(string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return null;
        lock (_gate) { return _data.TryGetValue(user, out var ov) ? ov : null; }
    }

    public void Upsert(string user, IEnumerable<string> extraRoles, bool disabled, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        string u = user.Trim();
        string[] roles = extraRoles?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        lock (_gate)
        {
            _data[u] = new UserOverride(u, roles, disabled, DateTime.UtcNow);
            PersistUnsafe();
        }
        _log.LogInformation("UserOverride upsert {User} Disabled={Disabled} Roles={Roles}", u, disabled, string.Join(';', roles));
    }

    public bool Remove(string user, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        lock (_gate)
        {
            bool ok = _data.Remove(user);
            if (ok) PersistUnsafe();
            return ok;
        }
    }

    public IReadOnlyCollection<UserOverride> Snapshot()
    {
        lock (_gate) { return _data.Values.ToArray(); }
    }

    private sealed class UserOverrideDto
    {
        public string? User { get; set; }
        public string[]? ExtraRoles { get; set; }
        public bool Disabled { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }
}

// ================= DB Role Store =================
internal sealed class DbRoleStore : IAppRoleStore
{
    private readonly RoleDbContext _db; private readonly ILogger<DbRoleStore> _log; private readonly IRoleAuditSink? _audit; private readonly bool _auditEnabled; private readonly ConcurrentDictionary<string, HashSet<string>> _cache = new(StringComparer.OrdinalIgnoreCase); private readonly object _sync = new();
    private readonly IAccountLookup? _acct; private readonly RoleMappingOptions _opts;
    public DbRoleStore(RoleDbContext db, ILogger<DbRoleStore> log, IOptions<RoleMappingOptions> opts, IRoleAuditSink? audit, IAccountLookup? acct)
    { _db = db; _log = log; _audit = audit; _acct = acct; _opts = opts.Value; _auditEnabled = _opts.AuditEnabled; _db.Database.Migrate(); var val = _opts; if (val.Admins is not null) foreach (var a in val.Admins.Where(a => !string.IsNullOrWhiteSpace(a) && !a.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase)) AddToRole(a, AppRoles.Admin, "seed"); if (val.Users is not null) foreach (var u in val.Users.Where(u => !string.IsNullOrWhiteSpace(u) && !u.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase)) AddToRole(u, AppRoles.User, "seed"); LoadCache(); }
    private void LoadCache() { foreach (var g in _db.UserRoles.AsNoTracking().Where(r => !r.IsDeleted).GroupBy(r => r.User)) _cache[g.Key] = new HashSet<string>(g.Select(r => r.Role), StringComparer.OrdinalIgnoreCase); }
    public IReadOnlyCollection<string> GetRoles(string userPrincipalNameOrSam) => string.IsNullOrWhiteSpace(userPrincipalNameOrSam) ? Array.Empty<string>() : (_cache.TryGetValue(userPrincipalNameOrSam, out var hs) ? hs : Array.Empty<string>());
    public IReadOnlyCollection<string> GetDeletedRoles(string samAccountName) => string.IsNullOrWhiteSpace(samAccountName) ? Array.Empty<string>() : _db.UserRoles.AsNoTracking().Where(r => r.User == samAccountName && r.IsDeleted).Select(r => r.Role).ToArray();
    public void AddToRole(string samAccountName, string role, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return;
        bool exists = _acct?.Exists(samAccountName) ?? true;
        if (!exists)
        {
            string msg = $"Directory account '{samAccountName}' not found.";
            if (_opts.EnforceDirectoryUserExists) { _log.LogWarning("{Msg} Rejecting role add for {Role}.", msg, role); _audit?.Record("AddRoleRejected", samAccountName, role, actor, false, "DirectoryNotFound"); return; }
            _log.LogWarning("{Msg} Proceeding (enforcement disabled).", msg);
        }
        lock (_sync)
        {
            var existing = _db.UserRoles.FirstOrDefault(r => r.User == samAccountName && r.Role == role);
            if (existing is null) _db.UserRoles.Add(new AppUserRole { User = samAccountName, Role = role, CreatedBy = actor });
            else if (existing.IsDeleted) { existing.IsDeleted = false; existing.DeletedBy = null; existing.DeletedUtc = null; existing.ModifiedBy = actor; existing.ModifiedUtc = DateTime.UtcNow; }
            try { _db.SaveChanges(); _audit?.Record("AddOrRestoreRole", samAccountName, role, actor, _auditEnabled); } catch (Exception ex) { _log.LogError(ex, "Persist role {User}-{Role} failed", samAccountName, role); _audit?.Record("AddOrRestoreRole", samAccountName, role, actor, false, ex.Message); }
            var set = _cache.GetOrAdd(samAccountName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)); set.Add(role);
        }
    }
    public bool RestoreRole(string samAccountName, string role, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return false; lock (_sync) { var entity = _db.UserRoles.FirstOrDefault(r => r.User == samAccountName && r.Role == role && r.IsDeleted); if (entity is null) return false; entity.IsDeleted = false; entity.DeletedBy = null; entity.DeletedUtc = null; entity.ModifiedBy = actor; entity.ModifiedUtc = DateTime.UtcNow; try { _db.SaveChanges(); _audit?.Record("RestoreRole", samAccountName, role, actor, _auditEnabled); } catch (Exception ex) { _audit?.Record("RestoreRole", samAccountName, role, actor, false, ex.Message); return false; } var set = _cache.GetOrAdd(samAccountName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)); set.Add(role); return true; } }
    public bool RemoveFromRole(string samAccountName, string role, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return false; lock (_sync) { var entity = _db.UserRoles.FirstOrDefault(r => r.User == samAccountName && r.Role == role); if (entity is null || entity.IsDeleted) return false; entity.IsDeleted = true; entity.DeletedBy = actor; entity.DeletedUtc = DateTime.UtcNow; entity.ModifiedBy = actor; entity.ModifiedUtc = entity.DeletedUtc; try { _db.SaveChanges(); _audit?.Record("SoftDeleteRole", samAccountName, role, actor, _auditEnabled); } catch (Exception ex) { _audit?.Record("SoftDeleteRole", samAccountName, role, actor, false, ex.Message); return false; } if (_cache.TryGetValue(samAccountName, out var set)) { bool removed = set.Remove(role); if (set.Count == 0) _cache.TryRemove(samAccountName, out _); return removed; } } return false; }
    public bool RemoveUser(string samAccountName, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName)) return false; lock (_sync) { var rows = _db.UserRoles.Where(r => r.User == samAccountName && !r.IsDeleted).ToList(); if (rows.Count == 0) return false; DateTime now = DateTime.UtcNow; foreach (var r in rows) { r.IsDeleted = true; r.DeletedBy = actor; r.DeletedUtc = now; r.ModifiedBy = actor; r.ModifiedUtc = now; } try { _db.SaveChanges(); _audit?.RecordUserRemoval("SoftDeleteUser", samAccountName, actor, _auditEnabled); } catch (Exception ex) { _audit?.RecordUserRemoval("SoftDeleteUser", samAccountName, actor, false, ex.Message); return false; } _cache.TryRemove(samAccountName, out _); return true; } }
    public int PurgeDeleted(TimeSpan olderThan, string? actor = null)
    { DateTime cutoff = DateTime.UtcNow - olderThan; var doomed = _db.UserRoles.Where(r => r.IsDeleted && r.DeletedUtc != null && r.DeletedUtc < cutoff).ToList(); if (doomed.Count == 0) return 0; _db.UserRoles.RemoveRange(doomed); try { int count = _db.SaveChanges(); _audit?.Record("PurgeDeleted", "*", $"{count} roles", actor, _auditEnabled); return count; } catch (Exception ex) { _audit?.Record("PurgeDeleted", "*", "err", actor, false, ex.Message); return 0; } }
    public IReadOnlyDictionary<string, HashSet<string>> Snapshot() => _cache;
    public IReadOnlyCollection<string> GetAllUsers() => _db.UserRoles.AsNoTracking().Select(r => r.User).Distinct().ToArray();
}

// ================= Persistent (JSON) Store =================
internal sealed class PersistentRoleStore : IAppRoleStore
{
    private sealed class JsonRoleState { public string Role { get; set; } = string.Empty; public bool IsDeleted { get; set; } public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public DateTime? DeletedUtc { get; set; } public string? DeletedBy { get; set; } }
    private readonly ConcurrentDictionary<string, List<JsonRoleState>> _userRoles = new(StringComparer.OrdinalIgnoreCase); private readonly string _filePath; private readonly object _persistGate = new(); private readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }; private readonly IRoleAuditSink? _audit; private readonly bool _auditEnabled; private readonly IAccountLookup? _acct; private readonly RoleMappingOptions _opts;
    public PersistentRoleStore(IOptions<RoleMappingOptions> opts, ILogger<PersistentRoleStore> log, IRoleAuditSink? audit, IAccountLookup? acct)
    { ArgumentNullException.ThrowIfNull(opts); ArgumentNullException.ThrowIfNull(log); _audit = audit; _acct = acct; _opts = opts.Value; _auditEnabled = _opts.AuditEnabled; var val = _opts; string baseDir = AppContext.BaseDirectory; string defaultDir = Path.Combine(baseDir, "data"); Directory.CreateDirectory(defaultDir); _filePath = string.IsNullOrWhiteSpace(val.FilePath) ? Path.Combine(defaultDir, "roles.json") : Path.GetFullPath(val.FilePath); try { if (File.Exists(_filePath)) { using FileStream fs = File.OpenRead(_filePath); var loaded = JsonSerializer.Deserialize<Dictionary<string, List<JsonRoleState>>>(fs, _jsonOpts) ?? new(); foreach (var kv in loaded) _userRoles[kv.Key] = kv.Value ?? new List<JsonRoleState>(); } } catch (Exception ex) { log.LogWarning(ex, "Load roles '{Path}' failed", _filePath); } if (val.Admins is not null) foreach (var a in val.Admins.Where(a => !string.IsNullOrWhiteSpace(a) && !a.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase)) AddToRole(a, AppRoles.Admin, "seed"); if (val.Users is not null) foreach (var u in val.Users.Where(u => !string.IsNullOrWhiteSpace(u) && !u.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase)) AddToRole(u, AppRoles.User, "seed"); Persist(log); }
    public IReadOnlyCollection<string> GetRoles(string userPrincipalNameOrSam) => string.IsNullOrWhiteSpace(userPrincipalNameOrSam) ? Array.Empty<string>() : (_userRoles.TryGetValue(userPrincipalNameOrSam, out var list) ? list.Where(r => !r.IsDeleted).Select(r => r.Role).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>());
    public IReadOnlyCollection<string> GetDeletedRoles(string samAccountName) => string.IsNullOrWhiteSpace(samAccountName) ? Array.Empty<string>() : (_userRoles.TryGetValue(samAccountName, out var list) ? list.Where(r => r.IsDeleted).Select(r => r.Role).ToArray() : Array.Empty<string>());
    public void AddToRole(string samAccountName, string role, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return; bool exists = _acct?.Exists(samAccountName) ?? true; if (!exists) { if (_opts.EnforceDirectoryUserExists) { _audit?.Record("AddRoleRejected", samAccountName, role, actor, false, "DirectoryNotFound"); return; } } var list = _userRoles.GetOrAdd(samAccountName, _ => new List<JsonRoleState>()); lock (list) { var existing = list.FirstOrDefault(r => r.Role.Equals(role, StringComparison.OrdinalIgnoreCase)); if (existing is null) list.Add(new JsonRoleState { Role = role, IsDeleted = false, CreatedUtc = DateTime.UtcNow }); else if (existing.IsDeleted) { existing.IsDeleted = false; existing.DeletedBy = null; existing.DeletedUtc = null; } } _audit?.Record("AddOrRestoreRole", samAccountName, role, actor, _auditEnabled); _ = Task.Run(() => Persist(null)); }
    public bool RestoreRole(string samAccountName, string role, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return false; if (_userRoles.TryGetValue(samAccountName, out var list)) { bool changed = false; lock (list) { var existing = list.FirstOrDefault(r => r.Role.Equals(role, StringComparison.OrdinalIgnoreCase) && r.IsDeleted); if (existing is not null) { existing.IsDeleted = false; existing.DeletedBy = null; existing.DeletedUtc = null; changed = true; } } if (changed) { _audit?.Record("RestoreRole", samAccountName, role, actor, _auditEnabled); _ = Task.Run(() => Persist(null)); return true; } } return false; }
    public bool RemoveFromRole(string samAccountName, string role, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName) || string.IsNullOrWhiteSpace(role)) return false; if (_userRoles.TryGetValue(samAccountName, out var list)) { bool changed = false; lock (list) { var existing = list.FirstOrDefault(r => r.Role.Equals(role, StringComparison.OrdinalIgnoreCase) && !r.IsDeleted); if (existing is not null) { existing.IsDeleted = true; existing.DeletedBy = actor; existing.DeletedUtc = DateTime.UtcNow; changed = true; } } if (changed) { _audit?.Record("SoftDeleteRole", samAccountName, role, actor, _auditEnabled); _ = Task.Run(() => Persist(null)); return true; } } return false; }
    public bool RemoveUser(string samAccountName, string? actor = null)
    { if (string.IsNullOrWhiteSpace(samAccountName)) return false; if (_userRoles.TryGetValue(samAccountName, out var list)) { bool changed = false; DateTime now = DateTime.UtcNow; lock (list) { foreach (var r in list.Where(r => !r.IsDeleted)) { r.IsDeleted = true; r.DeletedBy = actor; r.DeletedUtc = now; changed = true; } } if (changed) { _audit?.RecordUserRemoval("SoftDeleteUser", samAccountName, actor, _auditEnabled); _ = Task.Run(() => Persist(null)); return true; } } return false; }
    public int PurgeDeleted(TimeSpan olderThan, string? actor = null)
    { DateTime cutoff = DateTime.UtcNow - olderThan; int removed = 0; foreach (var kv in _userRoles) { lock (kv.Value) { int before = kv.Value.Count; kv.Value.RemoveAll(r => r.IsDeleted && r.DeletedUtc != null && r.DeletedUtc < cutoff); removed += before - kv.Value.Count; } } if (removed > 0) { _audit?.Record("PurgeDeleted", "*", removed.ToString(), actor, _auditEnabled); _ = Task.Run(() => Persist(null)); } return removed; }
    public IReadOnlyDictionary<string, HashSet<string>> Snapshot() { var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); foreach (var kv in _userRoles) { var active = kv.Value.Where(v => !v.IsDeleted).Select(v => v.Role).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); if (active.Length > 0) result[kv.Key] = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase); } return result; }
    public IReadOnlyCollection<string> GetAllUsers() => _userRoles.Keys.ToArray();
    private void Persist(ILogger? log) { try { lock (_persistGate) { var export = _userRoles.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase); Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!); using FileStream fs = File.Create(_filePath); JsonSerializer.Serialize(fs, export, _jsonOpts); } } catch (Exception ex) { log?.LogWarning(ex, "Persist roles '{Path}' failed", _filePath); } }
}

public sealed partial class ClaimsRoleEnricher : Microsoft.AspNetCore.Authentication.IClaimsTransformation
{
    private readonly IAppRoleStore _store; private readonly RoleMappingOptions _opts; private Regex[] _adminRegex = Array.Empty<Regex>(); private Regex[] _userRegex = Array.Empty<Regex>(); private bool _compiled; private readonly ILogger<ClaimsRoleEnricher> _log; private readonly IUserOverrideStore? _overrides; private readonly IRoleAuditSink? _audit;
    public ClaimsRoleEnricher(IAppRoleStore store, IOptions<RoleMappingOptions> opts, ILogger<ClaimsRoleEnricher> log, IUserOverrideStore? overrides = null, IRoleAuditSink? audit = null) { _store = store ?? throw new ArgumentNullException(nameof(store)); ArgumentNullException.ThrowIfNull(opts); _opts = opts.Value ?? new RoleMappingOptions(); _log = log; _overrides = overrides; _audit = audit; }
    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Implicit Windows admin elevation applied for user {User}")] private static partial void LogImplicitElevation(ILogger logger, string User);
    private static Regex[] CompilePatterns(IEnumerable<string>? patterns) => patterns is null ? Array.Empty<Regex>() : patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => "^" + Regex.Escape(p.Trim()).Replace("\\*", ".*") + "$").Select(pat => new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToArray();
    private void EnsurePatterns()
    {
        if (_compiled) return;
        _adminRegex = CompilePatterns((_opts.AdminPatterns ?? new()).Concat((_opts.Admins ?? []).Where(a => a.Contains('*'))));
        _userRegex = CompilePatterns((_opts.UserPatterns ?? new()).Concat((_opts.Users ?? []).Where(u => u.Contains('*'))));
        _compiled = true;
    }
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true) return Task.FromResult(principal);
        EnsurePatterns();
        string? raw = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(raw)) return Task.FromResult(principal);
        string sam = raw.Contains('@') ? raw.Split('@')[0] : raw.Split('\\').Last();
        var id = principal.Identity as ClaimsIdentity;
        var currentRoles = _store.GetRoles(sam);
        foreach (var r in currentRoles)
        {
            bool has = principal.HasClaim(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, r, StringComparison.OrdinalIgnoreCase));
            if (!has)
            {
                id!.AddClaim(new Claim(ClaimTypes.Role, r));
            }
        }
        // Apply overrides BEFORE implicit pattern / auto roles
        UserOverride? ov = _overrides?.Get(sam);
        if (ov is not null)
        {
            if (ov.Disabled)
            {
                foreach (var rc in id!.Claims.Where(c => c.Type == ClaimTypes.Role).ToList()) id.RemoveClaim(rc);
                _log.LogInformation("UserOverride disabled access for {User}", sam);
                _audit?.Record("UserDisabled", sam, "*", null, true, "Override Disabled");
                return Task.FromResult(principal);
            }
            foreach (var extra in ov.ExtraRoles)
            {
                if (!id!.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value.Equals(extra, StringComparison.OrdinalIgnoreCase)))
                {
                    id.AddClaim(new Claim(ClaimTypes.Role, extra));
                    _audit?.Record("OverrideAddRole", sam, extra, null, true, "Override ExtraRole");
                }
            }
        }
        bool hadRoleBeforeImplicit = principal.HasClaim(c => c.Type == ClaimTypes.Role);
        // Implicit Windows admin via groups (feature flag)
        if (_opts.ImplicitWindowsAdminEnabled && !principal.HasClaim(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                bool isAdminGroup = false;
                if (OperatingSystem.IsWindows() && principal.Identity is WindowsIdentity win && win.Groups is not null)
                {
                    foreach (var sid in win.Groups)
                    {
                        string? s = sid?.Value; if (string.IsNullOrEmpty(s)) continue;
                        if (string.Equals(s, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase) || (s.EndsWith("-512", StringComparison.Ordinal) && s.StartsWith("S-1-5-21-", StringComparison.Ordinal)) || (s.EndsWith("-519", StringComparison.Ordinal) && s.StartsWith("S-1-5-21-", StringComparison.Ordinal))) { isAdminGroup = true; break; }
                    }
                }
                else
                {
                    var groupSidClaims = principal.Claims.Where(c => c.Type == ClaimTypes.GroupSid).Select(c => c.Value);
                    isAdminGroup = groupSidClaims.Any(s => string.Equals(s, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase) || (s.EndsWith("-512", StringComparison.Ordinal) && s.StartsWith("S-1-5-21-", StringComparison.Ordinal)) || (s.EndsWith("-519", StringComparison.Ordinal) && s.StartsWith("S-1-5-21-", StringComparison.Ordinal)));
                }
                if (isAdminGroup)
                {
                    id!.AddClaim(new Claim(ClaimTypes.Role, AppRoles.Admin));
                    LogImplicitElevation(_log, sam);
                    _audit?.Record("ImplicitAdminGroup", sam, AppRoles.Admin, null, true, "Group SID Elevation");
                }
            }
            catch { }
        }
        if (_opts.ImplicitWindowsAdminEnabled && !principal.HasClaim(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var rx in _adminRegex)
            {
                if (rx.IsMatch(raw) || rx.IsMatch(sam)) { id!.AddClaim(new Claim(ClaimTypes.Role, AppRoles.Admin)); _audit?.Record("ImplicitAdminPattern", sam, AppRoles.Admin, null, true, "Pattern Match"); break; }
            }
        }
        bool hasAdmin = principal.HasClaim(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.Admin, StringComparison.OrdinalIgnoreCase));
        bool hasUser = principal.HasClaim(c => c.Type == ClaimTypes.Role && c.Value.Equals(AppRoles.User, StringComparison.OrdinalIgnoreCase));
        if (!hasAdmin && !hasUser)
        {
            if (_opts.AutoAssignUser)
            {
                id!.AddClaim(new Claim(ClaimTypes.Role, AppRoles.User));
                _audit?.Record("AutoAssignUser", sam, AppRoles.User, null, true, hadRoleBeforeImplicit ? "PostImplicit" : "NoRolesBefore");
            }
            else
            {
                // Unresolved user (no roles at all) -> audit
                _audit?.Record("UnresolvedUser", sam, "none", null, false, "No matching roles");
            }
        }
        return Task.FromResult(principal);
    }
}

public static class RoleServiceCollectionExtensions
{
    public static IServiceCollection AddAppRoleInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(cfg);
        services.AddSingleton<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddMemoryCache();
        services.AddSingleton<IAccountLookup, DirectoryAccountLookup>();
        // Directory options
        services.Configure<DirectoryLookupOptions>(cfg.GetSection("AppRoles:Directory"));
        string? seedSam = Environment.GetEnvironmentVariable("USERNAME");
        var section = cfg.GetSection("AppRoles");
        var adminList = section.GetSection("Admins").Get<string[]>()?.ToList() ?? new List<string>();
        var adminPatterns = section.GetSection("AdminPatterns").Get<string[]>()?.ToList() ?? new List<string>();
        var userList = section.GetSection("Users").Get<string[]>()?.ToList() ?? new List<string>();
        var userPatterns = section.GetSection("UserPatterns").Get<string[]>()?.ToList() ?? new List<string>();
        userList = userList.Where(u => !adminList.Contains(u, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool autoUser = section.GetValue<bool?>("AutoAssignUser") ?? true;
        bool auditEnabled = section.GetValue<bool?>("AuditEnabled") ?? true;
        bool enforceDir = section.GetValue<bool?>("EnforceDirectoryUserExists") ?? false;
        bool implicitWinAdmin = section.GetValue<bool?>("ImplicitWindowsAdminEnabled") ?? true;
        if (!string.IsNullOrWhiteSpace(seedSam) && !adminList.Contains(seedSam, StringComparer.OrdinalIgnoreCase)) adminList.Add(seedSam);
        string provider = section.GetValue<string>("Provider") ?? "Json";
        string? conn = section.GetValue<string>("ConnectionString");
        string? filePath = section.GetValue<string>("FilePath");
        services.AddSingleton<IRoleAuditSink, RoleAuditSink>();
        services.Configure<RoleMappingOptions>(o =>
        {
            o.Admins = adminList; o.AdminPatterns = adminPatterns; o.Users = userList; o.UserPatterns = userPatterns; o.AutoAssignUser = autoUser; o.AuditEnabled = auditEnabled; o.EnforceDirectoryUserExists = enforceDir; o.ImplicitWindowsAdminEnabled = implicitWinAdmin; o.FilePath = filePath; o.Provider = provider; o.ConnectionString = conn;
        });
        if (string.Equals(provider, "Db", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException("AppRoles:ConnectionString required when Provider=Db");
            services.AddDbContext<RoleDbContext>(o => o.UseSqlServer(conn));
            services.AddSingleton<IAppRoleStore, DbRoleStore>();
        }
        else
        {
            services.AddSingleton<IAppRoleStore, PersistentRoleStore>();
        }
        services.AddSingleton<IUserOverrideStore, JsonUserOverrideStore>();
        // Replace registration of ClaimsRoleEnricher to include override dependency
        services.AddSingleton<Microsoft.AspNetCore.Authentication.IClaimsTransformation, ClaimsRoleEnricher>();
        // Directory health check
        services.AddHealthChecks().AddCheck<DirectoryHealthCheck>("directory", tags: new[] { "ready" });
        return services;
    }
}
#pragma warning restore IDE0011
