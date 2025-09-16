namespace XRoadFolkWeb.Infrastructure;

/// <summary>
/// Abstraction for storing and querying application role assignments.
/// Implementations may persist in-memory or to a database and may support soft-delete semantics.
/// </summary>
public interface IAppRoleStore
{
    /// <summary>
    /// Gets the roles assigned to a user.
    /// </summary>
    /// <param name="userPrincipalNameOrSam">The user principal name or SAM account name of the user.</param>
    /// <returns>A read-only collection of role names.</returns>
    IReadOnlyCollection<string> GetRoles(string userPrincipalNameOrSam);

    /// <summary>
    /// Adds a role to a user.
    /// </summary>
    /// <param name="samAccountName">The SAM account name of the user.</param>
    /// <param name="role">The role to add.</param>
    /// <param name="actor">The actor performing the operation, if any.</param>
    void AddToRole(string samAccountName, string role, string? actor = null);

    /// <summary>
    /// Removes a role from a user.
    /// </summary>
    /// <param name="samAccountName">The SAM account name of the user.</param>
    /// <param name="role">The role to remove.</param>
    /// <param name="actor">The actor performing the operation, if any.</param>
    /// <returns>true if the role was successfully removed; otherwise, false.</returns>
    bool RemoveFromRole(string samAccountName, string role, string? actor = null);

    /// <summary>
    /// Removes a user from the role store.
    /// </summary>
    /// <param name="samAccountName">The SAM account name of the user.</param>
    /// <param name="actor">The actor performing the operation, if any.</param>
    /// <returns>true if the user was successfully removed; otherwise, false.</returns>
    bool RemoveUser(string samAccountName, string? actor = null);

    /// <summary>
    /// Takes a snapshot of the current role assignments.
    /// </summary>
    /// <returns>A read-only dictionary representing the current role assignments.</returns>
    IReadOnlyDictionary<string, HashSet<string>> Snapshot();

    /// <summary>
    /// Gets the deleted roles for a user.
    /// </summary>
    /// <param name="samAccountName">The SAM account name of the user.</param>
    /// <returns>A read-only collection of deleted role names.</returns>
    IReadOnlyCollection<string> GetDeletedRoles(string samAccountName);

    /// <summary>
    /// Restores a deleted role for a user.
    /// </summary>
    /// <param name="samAccountName">The SAM account name of the user.</param>
    /// <param name="role">The role to restore.</param>
    /// <param name="actor">The actor performing the operation, if any.</param>
    /// <returns>true if the role was successfully restored; otherwise, false.</returns>
    bool RestoreRole(string samAccountName, string role, string? actor = null);

    /// <summary>
    /// Purges deleted roles that are older than the specified timespan.
    /// </summary>
    /// <param name="olderThan">The timespan indicating how old the deleted roles can be to still be purged.</param>
    /// <param name="actor">The actor performing the operation, if any.</param>
    /// <returns>The number of roles purged.</returns>
    int PurgeDeleted(TimeSpan olderThan, string? actor = null);

    /// <summary>
    /// Gets all users in the role store.
    /// </summary>
    /// <returns>A read-only collection of SAM account names.</returns>
    IReadOnlyCollection<string> GetAllUsers();
}
