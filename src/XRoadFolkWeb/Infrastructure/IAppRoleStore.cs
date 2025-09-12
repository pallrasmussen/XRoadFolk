namespace XRoadFolkWeb.Infrastructure;

public interface IAppRoleStore
{
    IReadOnlyCollection<string> GetRoles(string userPrincipalNameOrSam);
    void AddToRole(string samAccountName, string role, string? actor = null);
    bool RemoveFromRole(string samAccountName, string role, string? actor = null);
    bool RemoveUser(string samAccountName, string? actor = null);
    IReadOnlyDictionary<string, HashSet<string>> Snapshot();
    IReadOnlyCollection<string> GetDeletedRoles(string samAccountName);
    bool RestoreRole(string samAccountName, string role, string? actor = null);
    int PurgeDeleted(TimeSpan olderThan, string? actor = null);
    IReadOnlyCollection<string> GetAllUsers();
}
