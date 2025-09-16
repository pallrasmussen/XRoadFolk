namespace XRoadFolkWeb.Infrastructure;

/// <summary>
/// Well-known application roles used by claims and authorization policies.
/// </summary>
public static class AppRoles
{
    /// <summary>Application administrator role.</summary>
    public const string Admin = "Admin"; // application administrator
    /// <summary>Regular application user role.</summary>
    public const string User = "User";   // regular application user (non-admin)
}
