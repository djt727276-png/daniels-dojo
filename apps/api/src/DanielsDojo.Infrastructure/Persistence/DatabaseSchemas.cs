namespace DanielsDojo.Infrastructure.Persistence;

/// <summary>
/// SQL schema names. Modules are separated at the database level so ownership stays
/// obvious and permissions can be granted per module later.
/// </summary>
public static class DatabaseSchemas
{
    /// <summary>Users, roles, and role assignments.</summary>
    public const string Identity = "identity";

    /// <summary>Courses, sections, lessons, media metadata, and tags.</summary>
    public const string Catalog = "catalog";

    /// <summary>Offers, prices, orders, subscriptions, entitlements, and provider records.</summary>
    public const string Commerce = "commerce";

    /// <summary>Enrollments and lesson progress.</summary>
    public const string Learning = "learning";

    /// <summary>Append-only audit trail.</summary>
    public const string Audit = "audit";

    /// <summary>Profiles, forum, relationships, messaging, notifications, and reports.</summary>
    public const string Community = "community";

    /// <summary>EF Core migration history. Never cleared by test resets.</summary>
    public const string Infrastructure = "infrastructure";
}
