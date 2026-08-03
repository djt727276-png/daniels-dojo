using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Identity;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Authentication;

/// <summary>
/// Proves the administrator bootstrap is idempotent, preserves existing roles, and writes
/// exactly one audit record in the same transaction as the grant.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AdminRoleGrantTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Grant_AddsAdmin_PreservesStudent_AndWritesOneAuditRecord()
    {
        Guid userId = await CreateStudentAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();
        AdminRoleGrantService service = new(context, TimeProvider.System);

        AdminGrantResult result = await service.GrantAsync(
            userId, "Founding administrator.", "operator@workstation", "corr-1");

        Assert.True(result.Succeeded);
        Assert.True(result.RoleWasAdded);
        Assert.NotNull(result.AuditLogId);

        await using DanielsDojoDbContext verify = fixture.CreateContext();

        Guid[] roleIds = [.. await verify.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.RoleId)
            .ToListAsync()];

        Assert.Contains(SeedIds.AdminRole, roleIds);
        Assert.Contains(SeedIds.StudentRole, roleIds);
        Assert.Equal(2, roleIds.Length);

        AuditLog audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal(AdminRoleGrantService.AuditAction, audit.Action);
        Assert.Equal(AdminRoleGrantService.AuditTargetType, audit.TargetType);
        Assert.Equal(userId.ToString(), audit.TargetId);
        Assert.Equal("Founding administrator.", audit.Reason);
        Assert.Equal("corr-1", audit.CorrelationId);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        Assert.Contains("RoleAdded", audit.MetadataJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grant_IsIdempotent_AndAuditsEveryAttempt()
    {
        Guid userId = await CreateStudentAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();
        AdminRoleGrantService service = new(context, TimeProvider.System);

        AdminGrantResult first = await service.GrantAsync(userId, "First.", "op", "corr-1");
        AdminGrantResult second = await service.GrantAsync(userId, "Second.", "op", "corr-2");

        Assert.True(first.RoleWasAdded);
        Assert.False(second.RoleWasAdded);
        Assert.True(second.Succeeded);

        await using DanielsDojoDbContext verify = fixture.CreateContext();

        // The role is not duplicated, and both operator actions are on record.
        Assert.Equal(1, await verify.UserRoles.CountAsync(
            assignment => assignment.UserId == userId && assignment.RoleId == SeedIds.AdminRole));
        Assert.Equal(2, await verify.AuditLogs.CountAsync());

        AuditLog rerun = await verify.AuditLogs.SingleAsync(log => log.CorrelationId == "corr-2");
        Assert.Contains("AlreadyHeld", rerun.MetadataJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grant_ForUnknownUser_Fails_AndWritesNoAudit()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        AdminRoleGrantService service = new(context, TimeProvider.System);

        AdminGrantResult result = await service.GrantAsync(
            Guid.NewGuid(), "Typo in the user id.", "op", "corr-x");

        Assert.False(result.Succeeded);
        Assert.Equal(AdminGrantFailure.UserNotFound, result.Failure);
        Assert.Null(result.AuditLogId);

        await using DanielsDojoDbContext verify = fixture.CreateContext();
        Assert.Equal(0, await verify.AuditLogs.CountAsync());
        Assert.Equal(0, await verify.UserRoles.CountAsync(
            assignment => assignment.RoleId == SeedIds.AdminRole));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Grant_RequiresNonEmptyReason(string reason)
    {
        Guid userId = await CreateStudentAsync();

        await using DanielsDojoDbContext context = fixture.CreateContext();
        AdminRoleGrantService service = new(context, TimeProvider.System);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.GrantAsync(userId, reason, "op", "corr"));

        await using DanielsDojoDbContext verify = fixture.CreateContext();
        Assert.Equal(0, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Audit_DoesNotRecordPersonalData()
    {
        Guid userId = await CreateStudentAsync(email: "private.person@example.test", name: "Private Person");

        await using DanielsDojoDbContext context = fixture.CreateContext();
        AdminRoleGrantService service = new(context, TimeProvider.System);

        await service.GrantAsync(userId, "Promoting founder.", "operator@workstation", "corr-1");

        await using DanielsDojoDbContext verify = fixture.CreateContext();
        AuditLog audit = await verify.AuditLogs.SingleAsync();

        string serialized = $"{audit.TargetId}{audit.Reason}{audit.MetadataJson}";

        Assert.DoesNotContain("private.person@example.test", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private Person", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateStudentAsync(
        string email = "student@example.test",
        string name = "Student Person")
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        User user = new()
        {
            Id = Guid.NewGuid(),
            IdentityProvider = UserProvisioningService.IdentityProviderName,
            ExternalIssuer = TestTokenIssuer.TenantId,
            ExternalSubjectId = Guid.NewGuid().ToString(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = name,
            EmailVerified = true,
            Status = UserStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Users.Add(user);
        context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = SeedIds.StudentRole,
            AssignedAtUtc = now,
        });

        await context.SaveChangesAsync();
        return user.Id;
    }
}
