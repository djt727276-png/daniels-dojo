using DanielsDojo.Infrastructure.Persistence.Seeding;
using Xunit;

namespace DanielsDojo.UnitTests;

/// <summary>
/// Covers the environment guard and the deterministic seed identifiers without needing a
/// database. The database-backed behaviour is proven separately in the integration suite.
/// </summary>
public sealed class SeedGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    [InlineData("Development ")]
    [InlineData("")]
    public void DevelopmentProfile_IsRejected_UnlessEnvironmentMatchesExactly(string environmentName)
    {
        DatabaseSeeder seeder = SeederFactory.Create(environmentName);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => seeder.GuardProfileAllowed(SeedProfile.Development));

        Assert.Contains(environmentName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentProfile_IsAllowed_InDevelopment()
    {
        DatabaseSeeder seeder = SeederFactory.Create("Development");

        seeder.GuardProfileAllowed(SeedProfile.Development);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ReferenceProfile_IsAllowed_Everywhere(string environmentName)
    {
        DatabaseSeeder seeder = SeederFactory.Create(environmentName);

        seeder.GuardProfileAllowed(SeedProfile.Reference);
    }

    [Fact]
    public void SeedIdentifiers_AreDistinct()
    {
        Guid[] ids =
        [
            SeedIds.StudentRole,
            SeedIds.AdminRole,
            SeedIds.InstructorRole,
            SeedIds.SupportRole,
            SeedIds.AtlasCourse,
            SeedIds.MembershipOffer,
            SeedIds.AtlasLifetimeOffer,
            SeedIds.MembershipMonthlyPrice,
            SeedIds.AtlasLifetimePrice,
            SeedIds.DevelopmentAdminUser,
            SeedIds.AtlasSectionOne,
            SeedIds.AtlasSectionTwo,
            SeedIds.AtlasLessonWelcome,
            SeedIds.AtlasLessonSetup,
            SeedIds.AtlasLessonStructure,
            SeedIds.AtlasLessonDeployment,
        ];

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
    }

    [Fact]
    public void SeedProfile_HasExactlyTheTwoSupportedProfiles()
    {
        Assert.Equal(
            ["Reference", "Development"],
            Enum.GetNames<SeedProfile>());
    }
}
