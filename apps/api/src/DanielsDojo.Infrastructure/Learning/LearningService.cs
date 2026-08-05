using DanielsDojo.Application.Common;
using DanielsDojo.Application.Learning;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Learning;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Learning;

/// <summary>
/// The learner-facing course experience.
/// </summary>
/// <remarks>
/// <para>
/// Two rules shape everything here. Access is decided once, by the evaluator, and then applied
/// per lesson — the curriculum can therefore show a locked outline to somebody who has not
/// bought the course without ever leaking its contents. And progress is only ever additive:
/// a position moves forward, and completion is a latch that a stale tab cannot undo.
/// </para>
/// <para>
/// Unpublished sections and lessons are filtered out of every projection, so an author's
/// half-finished work is invisible to learners even inside a course they own.
/// </para>
/// </remarks>
internal sealed class LearningService(
    DanielsDojoDbContext context,
    ICourseAccessEvaluator access,
    TimeProvider timeProvider) : ILearningService
{
    public async Task<OperationResult<CourseCurriculum>> GetCurriculumAsync(
        Guid? userId,
        string courseSlug,
        CancellationToken cancellationToken = default)
    {
        var course = await context.Courses
            .AsNoTracking()
            .Where(candidate => candidate.Slug == courseSlug)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Slug,
                candidate.Title,
                candidate.Summary,
                candidate.Status,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (course is null)
        {
            return OperationResult.NotFound().ToFailure<CourseCurriculum>();
        }

        CourseAccess decision = await access.EvaluateCourseAsync(userId, course.Id, cancellationToken);

        // An unpublished course is invisible to everyone except an administrator previewing it.
        if (course.Status != PublicationStatus.Published
            && decision.Reason != CourseAccessReason.AdminPreview)
        {
            return OperationResult.NotFound().ToFailure<CourseCurriculum>();
        }

        List<LessonRow> lessons = await PublishedLessonsAsync(course.Id, decision, cancellationToken);
        Dictionary<Guid, LessonProgress> progress = await ProgressAsync(userId, lessons, cancellationToken);

        List<CurriculumSection> sections = [.. lessons
            .GroupBy(lesson => new { lesson.SectionId, lesson.SectionTitle, lesson.SectionSortOrder })
            .OrderBy(group => group.Key.SectionSortOrder)
            .Select(group => new CurriculumSection(
                group.Key.SectionId,
                group.Key.SectionTitle,
                group.Key.SectionSortOrder,
                [.. group
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson => ToCurriculumLesson(lesson, decision, progress))]))];

        int completed = progress.Values.Count(entry => entry.CompletedAtUtc is not null);

        return OperationResult.FromValue(new CourseCurriculum(
            course.Id,
            course.Slug,
            course.Title,
            course.Summary,
            sections,
            HasFullAccess(decision),
            decision.Reason.ToString(),
            decision.Denial.ToString(),
            decision.Code,
            decision.EndsAtUtc,

            // Anyone without full access is a preview viewer, whether the refusal was
            // "purchase required" or an explicit preview grant. The client renders the same
            // locked outline either way.
            !HasFullAccess(decision),
            lessons.Count,
            completed,
            ResumeLesson(lessons, decision, progress)));
    }

    public async Task<OperationResult<LessonDetail>> GetLessonAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        CourseAccess decision = await access.EvaluateLessonAsync(userId, lessonId, cancellationToken);

        if (!decision.Granted)
        {
            return Refuse<LessonDetail>(decision);
        }

        var lesson = await context.Lessons
            .AsNoTracking()
            .Where(candidate => candidate.Id == lessonId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.CourseId,
                CourseSlug = candidate.Course!.Slug,
                candidate.Title,
                candidate.LessonType,
                candidate.BodyMarkdown,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
        {
            return OperationResult.NotFound().ToFailure<LessonDetail>();
        }

        bool playable = await context.LessonVideos
            .AsNoTracking()
            .AnyAsync(
                video => video.LessonId == lessonId
                    && (video.Status == LessonVideoStatus.Ready
                        || video.Status == LessonVideoStatus.Replacing),
                cancellationToken);

        // A preview shows what the course is like; it does not hand out the materials.
        List<LessonResourceLink> resources = decision.AllowsResourceDownload
            ? await context.LessonResources
                .AsNoTracking()
                .Where(resource => resource.LessonId == lessonId
                    && resource.Status == PublicationStatus.Published)
                .OrderBy(resource => resource.SortOrder)
                .Select(resource => new LessonResourceLink(
                    resource.Id, resource.DisplayName, resource.MediaType, resource.SizeBytes))
                .ToListAsync(cancellationToken)
            : [];

        List<LessonRow> siblings = await PublishedLessonsAsync(lesson.CourseId, decision, cancellationToken);
        int index = siblings.FindIndex(candidate => candidate.Id == lessonId);

        // Opening a lesson is what starts it. Only for somebody who holds the course, though —
        // a preview viewer is browsing, and progress belongs to people who own the material.
        LessonProgress? entry = null;

        if (userId is { } learner && HasFullAccess(decision))
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            entry = await StartAsync(learner, lessonId, now, cancellationToken);
            await TouchEnrollmentAsync(learner, lesson.CourseId, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.FromValue(new LessonDetail(
            lesson.Id,
            lesson.CourseId,
            lesson.CourseSlug,
            lesson.Title,
            lesson.LessonType.ToString(),
            lesson.BodyMarkdown,
            playable,
            resources,
            index > 0 ? siblings[index - 1].Id : null,
            index >= 0 && index < siblings.Count - 1 ? siblings[index + 1].Id : null,
            entry?.StartedAtUtc,
            entry?.CompletedAtUtc,
            entry?.LastPositionSeconds ?? 0,
            decision.Reason.ToString()));
    }

    public async Task<OperationResult<ProgressRecorded>> RecordProgressAsync(
        Guid userId,
        Guid lessonId,
        ProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.PositionSeconds < 0)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "positionSeconds",
                "A resume position cannot be negative.")
                .ToFailure<ProgressRecorded>();
        }

        CourseAccess decision = await access.EvaluateLessonAsync(userId, lessonId, cancellationToken);

        // A preview viewer may watch, but their position is not recorded against a course they
        // do not hold — progress belongs to people who own the material.
        if (!decision.Granted || decision.IsPreviewOnly)
        {
            return Refuse<ProgressRecorded>(decision);
        }

        Guid? courseId = await context.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Id == lessonId)
            .Select(lesson => (Guid?)lesson.CourseId)
            .FirstOrDefaultAsync(cancellationToken);

        if (courseId is not { } course)
        {
            return OperationResult.NotFound().ToFailure<ProgressRecorded>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        LessonProgress entry = await StartAsync(userId, lessonId, now, cancellationToken);

        // Forward only. A stale tab reporting an old position must not rewind somebody who has
        // since watched further.
        entry.LastPositionSeconds = Math.Max(entry.LastPositionSeconds, update.PositionSeconds);

        // Completion latches. Reporting false never un-completes a finished lesson.
        if (update.Completed)
        {
            entry.CompletedAtUtc ??= now;
        }

        entry.UpdatedAtUtc = now;

        await TouchEnrollmentAsync(userId, course, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        (int total, int completed) = await CountsAsync(userId, course, cancellationToken);

        // Completing the last published lesson earns the certificate, exactly once. Nothing
        // else in the platform can create one.
        if (total > 0 && completed >= total)
        {
            await IssueCertificateAsync(userId, course, now, cancellationToken);
        }

        return OperationResult.FromValue(new ProgressRecorded(
            lessonId,
            entry.StartedAtUtc,
            entry.CompletedAtUtc,
            entry.LastPositionSeconds,
            total > 0 && completed >= total,
            completed,
            total));
    }

    public async Task<OperationResult<IReadOnlyList<MyLearningCourse>>> ListMyLearningAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> courseIds = await access.ListAccessibleCourseIdsAsync(userId, cancellationToken);

        if (courseIds.Count == 0)
        {
            return OperationResult.FromValue<IReadOnlyList<MyLearningCourse>>([]);
        }

        var courses = await context.Courses
            .AsNoTracking()
            .Where(course => courseIds.Contains(course.Id)
                && course.Status == PublicationStatus.Published)
            .Select(course => new { course.Id, course.Slug, course.Title, course.Summary })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, DateTimeOffset?> lastAccessed = await context.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.UserId == userId && courseIds.Contains(enrollment.CourseId))
            .ToDictionaryAsync(
                enrollment => enrollment.CourseId,
                enrollment => enrollment.LastAccessedAtUtc,
                cancellationToken);

        List<MyLearningCourse> shelf = [];

        foreach (var course in courses)
        {
            CourseAccess decision = await access.EvaluateCourseAsync(userId, course.Id, cancellationToken);
            List<LessonRow> lessons = await PublishedLessonsAsync(course.Id, decision, cancellationToken);
            Dictionary<Guid, LessonProgress> progress = await ProgressAsync(userId, lessons, cancellationToken);

            int completed = progress.Values.Count(entry => entry.CompletedAtUtc is not null);

            shelf.Add(new MyLearningCourse(
                course.Id,
                course.Slug,
                course.Title,
                course.Summary,
                lessons.Count,
                completed,
                lessons.Count == 0 ? 0 : (int)Math.Round(completed * 100.0 / lessons.Count),
                ResumeLesson(lessons, decision, progress),
                lastAccessed.GetValueOrDefault(course.Id),
                decision.Reason.ToString(),
                decision.EndsAtUtc));
        }

        return OperationResult.FromValue<IReadOnlyList<MyLearningCourse>>(
            [.. shelf.OrderByDescending(entry => entry.LastAccessedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(entry => entry.Title, StringComparer.Ordinal)]);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A published lesson with the section context the outline needs.</summary>
    private sealed record LessonRow(
        Guid Id,
        string Slug,
        string Title,
        LessonType LessonType,
        int SortOrder,
        bool IsPreview,
        int? EstimatedDurationSeconds,
        Guid SectionId,
        string SectionTitle,
        int SectionSortOrder,
        bool HasReadyVideo);

    /// <summary>
    /// Published lessons in a course, in reading order.
    /// </summary>
    /// <remarks>
    /// An administrator previewing their own work sees drafts too; nobody else ever does.
    /// Publication does not cascade, so a published lesson inside a draft section stays hidden.
    /// </remarks>
    private async Task<List<LessonRow>> PublishedLessonsAsync(
        Guid courseId,
        CourseAccess decision,
        CancellationToken cancellationToken)
    {
        bool adminPreview = decision.Reason == CourseAccessReason.AdminPreview;

        return await context.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.CourseId == courseId
                && (adminPreview
                    || (lesson.Status == PublicationStatus.Published
                        && lesson.CourseSection!.Status == PublicationStatus.Published)))
            .OrderBy(lesson => lesson.CourseSection!.SortOrder)
            .ThenBy(lesson => lesson.SortOrder)
            .Select(lesson => new LessonRow(
                lesson.Id,
                lesson.Slug,
                lesson.Title,
                lesson.LessonType,
                lesson.SortOrder,
                lesson.IsPreview,
                lesson.EstimatedDurationSeconds,
                lesson.CourseSectionId,
                lesson.CourseSection!.Title,
                lesson.CourseSection.SortOrder,
                context.LessonVideos.Any(video => video.LessonId == lesson.Id
                    && (video.Status == LessonVideoStatus.Ready
                        || video.Status == LessonVideoStatus.Replacing))))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, LessonProgress>> ProgressAsync(
        Guid? userId,
        List<LessonRow> lessons,
        CancellationToken cancellationToken)
    {
        if (userId is not { } learner || lessons.Count == 0)
        {
            return [];
        }

        List<Guid> ids = [.. lessons.Select(lesson => lesson.Id)];

        return await context.LessonProgress
            .AsNoTracking()
            .Where(entry => entry.UserId == learner && ids.Contains(entry.LessonId))
            .ToDictionaryAsync(entry => entry.LessonId, cancellationToken);
    }

    private static CurriculumLesson ToCurriculumLesson(
        LessonRow lesson,
        CourseAccess decision,
        Dictionary<Guid, LessonProgress> progress)
    {
        progress.TryGetValue(lesson.Id, out LessonProgress? entry);

        return new CurriculumLesson(
            lesson.Id,
            lesson.Slug,
            lesson.Title,
            lesson.LessonType.ToString(),
            lesson.SortOrder,
            lesson.IsPreview,
            lesson.EstimatedDurationSeconds,
            IsAccessible(lesson, decision),
            lesson.HasReadyVideo,
            entry?.StartedAtUtc,
            entry?.CompletedAtUtc,
            entry?.LastPositionSeconds ?? 0);
    }

    /// <summary>
    /// Whether one lesson is open to this viewer.
    /// </summary>
    /// <remarks>
    /// A lesson an author marked as a preview is open to everyone, including a viewer the
    /// course-level decision refused — that is what a preview is for. Everything else needs
    /// full access, so the outline can be shown complete while the material stays shut.
    /// </remarks>
    private static bool IsAccessible(LessonRow lesson, CourseAccess decision) =>
        lesson.IsPreview || HasFullAccess(decision);

    /// <summary>Whether the viewer holds the course itself rather than just its previews.</summary>
    private static bool HasFullAccess(CourseAccess decision) =>
        decision.Granted && !decision.IsPreviewOnly;

    /// <summary>
    /// Where "continue" goes: the first accessible lesson still unfinished, or the last
    /// accessible one when the course is complete.
    /// </summary>
    private static Guid? ResumeLesson(
        List<LessonRow> lessons,
        CourseAccess decision,
        Dictionary<Guid, LessonProgress> progress)
    {
        List<LessonRow> open = [.. lessons.Where(lesson => IsAccessible(lesson, decision))];

        if (open.Count == 0)
        {
            return null;
        }

        LessonRow? next = open.FirstOrDefault(lesson =>
            !progress.TryGetValue(lesson.Id, out LessonProgress? entry)
            || entry.CompletedAtUtc is null);

        return (next ?? open[^1]).Id;
    }

    /// <summary>
    /// Loads this learner's progress row, creating it the first time they open the lesson.
    /// </summary>
    /// <param name="now">
    /// The caller's instant, not a fresh reading. A separate reading here can land after the
    /// caller's, which would stamp a completion earlier than the start it belongs to — and the
    /// database rightly refuses a lesson finished before it was begun.
    /// </param>
    private async Task<LessonProgress> StartAsync(
        Guid userId,
        Guid lessonId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        LessonProgress? entry = await context.LessonProgress
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.LessonId == lessonId,
                cancellationToken);

        if (entry is not null)
        {
            return entry;
        }

        entry = new LessonProgress
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            LessonId = lessonId,
            StartedAtUtc = now,
            LastPositionSeconds = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.LessonProgress.Add(entry);

        return entry;
    }

    /// <summary>
    /// Records that the learner opened the course, creating the enrollment on first contact.
    /// </summary>
    private async Task TouchEnrollmentAsync(
        Guid userId,
        Guid courseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Enrollment? enrollment = await context.Enrollments
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.CourseId == courseId,
                cancellationToken);

        if (enrollment is null)
        {
            context.Enrollments.Add(new Enrollment
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CourseId = courseId,
                EnrolledAtUtc = now,
                LastAccessedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });

            return;
        }

        enrollment.LastAccessedAtUtc = now;
        enrollment.UpdatedAtUtc = now;
    }

    private async Task<(int Total, int Completed)> CountsAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        int total = await context.Lessons
            .AsNoTracking()
            .CountAsync(
                lesson => lesson.CourseId == courseId
                    && lesson.Status == PublicationStatus.Published
                    && lesson.CourseSection!.Status == PublicationStatus.Published,
                cancellationToken);

        int completed = await context.LessonProgress
            .AsNoTracking()
            .CountAsync(
                entry => entry.UserId == userId
                    && entry.CompletedAtUtc != null
                    && entry.Lesson!.CourseId == courseId
                    && entry.Lesson.Status == PublicationStatus.Published
                    && entry.Lesson.CourseSection!.Status == PublicationStatus.Published,
                cancellationToken);

        return (total, completed);
    }

    /// <summary>
    /// Issues the completion certificate if this member does not already hold one for the
    /// course. Titles and names are captured at issuance so later edits never rewrite what
    /// was earned.
    /// </summary>
    private async Task IssueCertificateAsync(
        Guid userId,
        Guid courseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool exists = await context.Certificates
            .AsNoTracking()
            .AnyAsync(
                certificate => certificate.UserId == userId && certificate.CourseId == courseId,
                cancellationToken);

        if (exists)
        {
            return;
        }

        var names = await context.Courses
            .AsNoTracking()
            .Where(course => course.Id == courseId)
            .Select(course => new { course.Title })
            .SingleAsync(cancellationToken);

        string holder = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .SingleAsync(cancellationToken);

        context.Certificates.Add(new Certificate
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CourseId = courseId,

            // 128 bits of randomness, base32-flavoured for print friendliness. Unguessable is
            // the property that makes the public lookup safe.
            VerificationCode = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            CourseTitleAtIssue = names.Title,
            HolderNameAtIssue = holder,
            IssuedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two final-lesson completions raced; the unique (UserId, CourseId) index let one
            // insert win, which is exactly the intended outcome.
            context.ChangeTracker.Clear();
        }
    }

    public async Task<OperationResult<IReadOnlyList<CertificateView>>> ListCertificatesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        List<CertificateView> certificates = await context.Certificates
            .AsNoTracking()
            .Where(certificate => certificate.UserId == userId)
            .OrderByDescending(certificate => certificate.IssuedAtUtc)
            .Select(certificate => new CertificateView(
                certificate.Id,
                certificate.CourseId,
                certificate.CourseTitleAtIssue,
                certificate.HolderNameAtIssue,
                certificate.VerificationCode,
                certificate.IssuedAtUtc,
                certificate.RevokedAtUtc == null))
            .ToListAsync(cancellationToken);

        return OperationResult.FromValue<IReadOnlyList<CertificateView>>(certificates);
    }

    public async Task<OperationResult<CertificateVerification>> VerifyCertificateAsync(
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verificationCode) || verificationCode.Length > 32)
        {
            return OperationResult.NotFound().ToFailure<CertificateVerification>();
        }

        string normalized = verificationCode.Trim().ToUpperInvariant();

        CertificateVerification? verification = await context.Certificates
            .AsNoTracking()
            .Where(certificate => certificate.VerificationCode == normalized)
            .Select(certificate => new CertificateVerification(
                certificate.CourseTitleAtIssue,
                certificate.HolderNameAtIssue,
                certificate.IssuedAtUtc,
                certificate.RevokedAtUtc == null,
                certificate.RevokedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return verification is null
            ? OperationResult.NotFound().ToFailure<CertificateVerification>()
            : OperationResult.FromValue(verification);
    }

    public async Task<OperationResult<CertificateView>> RevokeCertificateAsync(
        Guid certificateId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "A revocation must say why.")
                .ToFailure<CertificateView>();
        }

        Certificate? certificate = await context.Certificates
            .FirstOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken);

        if (certificate is null)
        {
            return OperationResult.NotFound().ToFailure<CertificateView>();
        }

        if (certificate.RevokedAtUtc is null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            certificate.RevokedAtUtc = now;
            certificate.RevocationReason = reason.Trim();
            certificate.UpdatedAtUtc = now;

            await context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.FromValue(new CertificateView(
            certificate.Id,
            certificate.CourseId,
            certificate.CourseTitleAtIssue,
            certificate.HolderNameAtIssue,
            certificate.VerificationCode,
            certificate.IssuedAtUtc,
            IsValid: false));
    }

    /// <summary>
    /// Turns an access refusal into an outcome.
    /// </summary>
    /// <remarks>
    /// Something the viewer may not know exists is reported as not found rather than forbidden,
    /// so unpublished work cannot be enumerated by watching which identifiers answer 403.
    /// </remarks>
    private static OperationResult<T> Refuse<T>(CourseAccess decision) =>
        decision.Denial switch
        {
            CourseAccessDenial.NotFound or CourseAccessDenial.NotPublished =>
                OperationResult.NotFound().ToFailure<T>(),

            CourseAccessDenial.AuthenticationRequired =>
                OperationResult.Forbidden(decision.Code, "Sign in to open this lesson.").ToFailure<T>(),

            _ => OperationResult.Forbidden(
                decision.Code,
                "This lesson is not included in your current access.").ToFailure<T>(),
        };
}
