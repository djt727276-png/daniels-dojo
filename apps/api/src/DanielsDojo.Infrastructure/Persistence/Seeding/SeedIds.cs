namespace DanielsDojo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Deterministic identifiers for seeded rows. Fixed GUIDs make seeding idempotent across
/// machines and environments: a rerun matches existing rows by key instead of inserting
/// duplicates. The <c>dd…</c> prefix marks a row as seed-owned at a glance.
/// </summary>
public static class SeedIds
{
    /// <summary>Student role.</summary>
    public static readonly Guid StudentRole = new("dd000001-0000-4000-8000-000000000001");

    /// <summary>Admin role.</summary>
    public static readonly Guid AdminRole = new("dd000001-0000-4000-8000-000000000002");

    /// <summary>Instructor role.</summary>
    public static readonly Guid InstructorRole = new("dd000001-0000-4000-8000-000000000003");

    /// <summary>Support role.</summary>
    public static readonly Guid SupportRole = new("dd000001-0000-4000-8000-000000000004");

    /// <summary>The Atlas Enterprise Developer course. A course sold inside Daniel's Dojo.</summary>
    public static readonly Guid AtlasCourse = new("dd000010-0000-4000-8000-000000000001");

    /// <summary>All-access monthly membership offer.</summary>
    public static readonly Guid MembershipOffer = new("dd000020-0000-4000-8000-000000000001");

    /// <summary>Lifetime purchase offer for the Atlas Enterprise Developer course.</summary>
    public static readonly Guid AtlasLifetimeOffer = new("dd000020-0000-4000-8000-000000000002");

    /// <summary>Monthly membership price.</summary>
    public static readonly Guid MembershipMonthlyPrice = new("dd000030-0000-4000-8000-000000000001");

    /// <summary>One-time Atlas lifetime price.</summary>
    public static readonly Guid AtlasLifetimePrice = new("dd000030-0000-4000-8000-000000000002");

    /// <summary>Development-only administrator account.</summary>
    public static readonly Guid DevelopmentAdminUser = new("dd000040-0000-4000-8000-000000000001");

    /// <summary>Development-only student account.</summary>
    public static readonly Guid DevelopmentStudentUser = new("dd000040-0000-4000-8000-000000000002");

    /// <summary>Development-only general discussion forum category.</summary>
    public static readonly Guid GeneralForumCategory = new("dd000070-0000-4000-8000-000000000001");

    /// <summary>Development-only course help forum category.</summary>
    public static readonly Guid CourseHelpForumCategory = new("dd000070-0000-4000-8000-000000000002");

    /// <summary>Development-only announcements forum category.</summary>
    public static readonly Guid AnnouncementsForumCategory =
        new("dd000070-0000-4000-8000-000000000003");

    /// <summary>Development-only first Atlas section.</summary>
    public static readonly Guid AtlasSectionOne = new("dd000050-0000-4000-8000-000000000001");

    /// <summary>Development-only second Atlas section.</summary>
    public static readonly Guid AtlasSectionTwo = new("dd000050-0000-4000-8000-000000000002");

    /// <summary>Development-only lesson: course orientation (video, preview).</summary>
    public static readonly Guid AtlasLessonWelcome = new("dd000060-0000-4000-8000-000000000001");

    /// <summary>Development-only lesson: environment setup (article).</summary>
    public static readonly Guid AtlasLessonSetup = new("dd000060-0000-4000-8000-000000000002");

    /// <summary>Development-only lesson: solution structure (video).</summary>
    public static readonly Guid AtlasLessonStructure = new("dd000060-0000-4000-8000-000000000003");

    /// <summary>Development-only lesson: deployment checklist (article).</summary>
    public static readonly Guid AtlasLessonDeployment = new("dd000060-0000-4000-8000-000000000004");
}
