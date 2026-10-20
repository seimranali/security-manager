namespace SecurityManager.Shared.Kernel.Security;

/// <summary>Role names carried in the JWT "role" claim.</summary>
public static class Roles
{
    /// <summary>Full access, including user management.</summary>
    public const string Admin = "Admin";

    /// <summary>Can read and change security configuration and triage alerts.</summary>
    public const string Analyst = "Analyst";

    /// <summary>Read-only access.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Machine identity used for service-to-service calls.</summary>
    public const string Service = "Service";

    public static readonly string[] UserRoles = [Admin, Analyst, Viewer];

    /// <summary>Roles allowed to perform state-changing (non-GET) requests.</summary>
    public static readonly string[] Writers = [Admin, Analyst, Service];
}

/// <summary>Claim types used in issued tokens (short JWT names, no inbound mapping).</summary>
public static class AppClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string FullName = "full_name";
    public const string Role = "role";
    public const string TokenId = "jti";
}
