using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Learning;
using DanielsDojo.Application.System;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Learning;

/// <summary>
/// The one implementation of <see cref="ICourseAccessEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Public rather than internal for the same reason as the Admin grant service: the access
/// matrix is exercised directly against a real database, because routing every case through
/// HTTP would test the endpoints rather than the rules.
/// </para>
/// <para>
/// Precedence is deliberate and ordered strongest-first: an administrator previewing their own
/// unpublished work, then a lifetime purchase, then an active membership, then a complimentary
/// grant, then the Development-only seeded grant, then an explicitly published free preview.
/// Lifetime outranks membership because it is the durable one — a member who holds both should
/// be told the thing that survives a cancellation.
/// </para>
/// <para>
/// Time comes from the injected <see cref="TimeProvider"/>, so a period boundary is testable
/// rather than something that only misbehaves at month end.
/// </para>
/// </remarks>
public sealed class CourseAccessEvaluator(
    DanielsDojoDbContext context,
    IApplicationEnvironment environment,
    TimeProvider timeProvider) : ICourseAccessEvaluator
{
    /// <summary>
    /// The Development grant exists so the seeded student can exercise the product before any
    /// real purchase exists. It is gated on an exact ordinal environment match — stricter than
    /// a case-insensitive host check — so it cannot be reached by an environment named
    /// "development" or "DEVELOPMENT" in a deployed configuration.
    /// </summary>
    private bool IsExactlyDevelopment =>
        string.Equals(environment.EnvironmentName, "Development", StringComparison.Ordinal);

    public async Task<CourseAccess> EvaluateCourseAsync(
        Guid? userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var course = await context.Courses
            .AsNoTracking()
            .Where(candidate => candidate.Id == courseId)
            .Select(candidate => new { candidate.Status, candidate.IncludedInMembership })
            .FirstOrDefaultAsync(cancellationToken);

        if (course is null)
        {
            return CourseAccess.Deny(CourseAccessDenial.NotFound);
        }

        return await DecideAsync(
            userId,
            courseId,
            course.Status,
            course.IncludedInMembership,
            lessonIsPreview: false,
            lessonPublished: true,
            cancellationToken);
    }

    public async Task<CourseAccess> EvaluateLessonAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await context.Lessons
            .AsNoTracking()
            .Where(candidate => candidate.Id == lessonId)
            .Select(candidate => new
            {
                candidate.CourseId,
                LessonStatus = candidate.Status,
                candidate.IsPreview,
                SectionStatus = candidate.CourseSection!.Status,
                CourseStatus = candidate.Course!.Status,
                candidate.Course.IncludedInMembership,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
        {
            return CourseAccess.Deny(CourseAccessDenial.NotFound);
        }

        // A lesson counts as published only when everything above it is too. Publication does
        // not cascade downward, so it must not be assumed upward either.
        bool fullyPublished =
            lesson.LessonStatus == PublicationStatus.Published
            && lesson.SectionStatus == PublicationStatus.Published
            && lesson.CourseStatus == PublicationStatus.Published;

        return await DecideAsync(
            userId,
            lesson.CourseId,
            lesson.CourseStatus,
            lesson.IncludedInMembership,
            lesson.IsPreview,
            fullyPublished,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListAccessibleCourseIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Entitlement> grants = await ActiveGrantsAsync(userId, now, cancellationToken);

        // A course-scoped grant names its course; a membership covers whatever is currently
        // flagged for membership, which is why the set is resolved now rather than stored.
        var courseIds = grants
            .Where(grant => grant.Scope == EntitlementScope.Course && grant.CourseId is not null)
            .Select(grant => grant.CourseId!.Value)
            .ToHashSet();

        if (grants.Any(grant => grant.Scope == EntitlementScope.AllMembershipCourses))
        {
            List<Guid> membershipCourses = await context.Courses
                .AsNoTracking()
                .Where(course => course.IncludedInMembership
                    && course.Status == PublicationStatus.Published)
                .Select(course => course.Id)
                .ToListAsync(cancellationToken);

            courseIds.UnionWith(membershipCourses);
        }

        if (IsExactlyDevelopment && userId == SeedIds.DevelopmentStudentUser)
        {
            List<Guid> published = await context.Courses
                .AsNoTracking()
                .Where(course => course.Status == PublicationStatus.Published)
                .Select(course => course.Id)
                .ToListAsync(cancellationToken);

            courseIds.UnionWith(published);
        }

        return [.. courseIds];
    }

    /// <summary>Applies the precedence order to one course for one viewer.</summary>
    private async Task<CourseAccess> DecideAsync(
        Guid? userId,
        Guid courseId,
        PublicationStatus courseStatus,
        bool includedInMembership,
        bool lessonIsPreview,
        bool lessonPublished,
        CancellationToken cancellationToken)
    {
        // 1. Administrators review unpublished work. This is a role check against the local
        //    database, never a token claim.
        if (userId is { } adminCandidate && await IsAdminAsync(adminCandidate, cancellationToken))
        {
            return CourseAccess.Allow(CourseAccessReason.AdminPreview);
        }

        // Beyond this point nothing unpublished is visible to anyone.
        if (courseStatus != PublicationStatus.Published || !lessonPublished)
        {
            return CourseAccess.Deny(CourseAccessDenial.NotPublished);
        }

        if (userId is not { } memberId)
        {
            // 2. An anonymous viewer gets exactly the explicitly published previews.
            return lessonIsPreview
                ? CourseAccess.Allow(CourseAccessReason.PublicPreview, previewOnly: true)
                : CourseAccess.Deny(CourseAccessDenial.AuthenticationRequired);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Entitlement> grants = await ActiveGrantsAsync(memberId, now, cancellationToken);

        // 3. A lifetime purchase of this course. Durable, so it is reported ahead of a
        //    membership that happens to cover the same course today.
        Entitlement? lifetime = grants.FirstOrDefault(grant =>
            grant.Scope == EntitlementScope.Course
            && grant.CourseId == courseId
            && grant.Source == EntitlementSource.Purchase);

        if (lifetime is not null)
        {
            return CourseAccess.Allow(CourseAccessReason.Lifetime, lifetime.EndsAtUtc);
        }

        // 4. An active membership, but only for a course actually flagged for membership.
        Entitlement? membership = grants.FirstOrDefault(grant =>
            grant.Scope == EntitlementScope.AllMembershipCourses);

        if (membership is not null && includedInMembership)
        {
            return CourseAccess.Allow(CourseAccessReason.Membership, membership.EndsAtUtc);
        }

        // 5. A complimentary grant issued by an administrator.
        Entitlement? manual = grants.FirstOrDefault(grant =>
            grant.Source == EntitlementSource.Manual
            && (grant.Scope == EntitlementScope.AllMembershipCourses
                ? includedInMembership
                : grant.CourseId == courseId));

        if (manual is not null)
        {
            return CourseAccess.Allow(CourseAccessReason.ManualGrant, manual.EndsAtUtc);
        }

        // 6. The seeded Development student, in Development only.
        if (IsExactlyDevelopment && memberId == SeedIds.DevelopmentStudentUser)
        {
            return CourseAccess.Allow(CourseAccessReason.DevelopmentGrant);
        }

        // 7. A free preview is still open to a signed-in member with no purchase.
        if (lessonIsPreview)
        {
            return CourseAccess.Allow(CourseAccessReason.PublicPreview, previewOnly: true);
        }

        // Nothing granted it. Distinguish "ended" from "never had" only from this member's own
        // rows, which discloses nothing about anybody else.
        return await DescribeRefusalAsync(memberId, courseId, includedInMembership, cancellationToken);
    }

    /// <summary>
    /// Chooses between "expired", "revoked", and "never purchased" using only the caller's own
    /// entitlement history, so the wording is useful without leaking another member's state.
    /// </summary>
    private async Task<CourseAccess> DescribeRefusalAsync(
        Guid userId,
        Guid courseId,
        bool includedInMembership,
        CancellationToken cancellationToken)
    {
        List<Entitlement> history = await context.Entitlements
            .AsNoTracking()
            .Where(grant => grant.UserId == userId
                && (grant.CourseId == courseId
                    || (includedInMembership
                        && grant.Scope == EntitlementScope.AllMembershipCourses)))
            .OrderByDescending(grant => grant.UpdatedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (history.Count == 0)
        {
            return CourseAccess.Deny(CourseAccessDenial.PurchaseRequired);
        }

        return history.Any(grant => grant.Status == EntitlementStatus.Revoked)
            ? CourseAccess.Deny(CourseAccessDenial.Revoked)
            : CourseAccess.Deny(CourseAccessDenial.Expired);
    }

    /// <summary>
    /// Grants that are active right now: not revoked, not expired, and already started.
    /// </summary>
    /// <remarks>
    /// A cancelled membership keeps its <see cref="Entitlement.EndsAtUtc"/> at the paid period
    /// boundary rather than being revoked, which is exactly how cancellation is meant to behave
    /// — the member keeps what they paid for until it runs out.
    /// </remarks>
    private Task<List<Entitlement>> ActiveGrantsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.Entitlements
            .AsNoTracking()
            .Where(grant => grant.UserId == userId
                && grant.Status == EntitlementStatus.Active
                && grant.StartsAtUtc <= now
                && (grant.EndsAtUtc == null || grant.EndsAtUtc > now))
            .ToListAsync(cancellationToken);

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        context.UserRoles
            .AsNoTracking()
            .AnyAsync(
                link => link.UserId == userId && link.Role!.Name == ApplicationRoles.Admin,
                cancellationToken);
}
