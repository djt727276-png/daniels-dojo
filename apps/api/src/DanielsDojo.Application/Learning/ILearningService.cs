using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Learning;

/// <summary>
/// The learner-facing course experience: curriculum, lessons, progress, and My Learning.
/// </summary>
/// <remarks>
/// Every method consults <see cref="ICourseAccessEvaluator"/> rather than deciding access for
/// itself, so a change to what a membership covers lands here without anyone remembering to
/// update it. Nothing here trusts a client's claim about what it may see.
/// </remarks>
public interface ILearningService
{
    /// <summary>
    /// Builds a course outline for one viewer, annotated with what they may open and how far
    /// they have got.
    /// </summary>
    /// <param name="userId">The signed-in learner, or null for an anonymous viewer.</param>
    /// <param name="courseSlug">Course URL segment.</param>
    Task<OperationResult<CourseCurriculum>> GetCurriculumAsync(
        Guid? userId,
        string courseSlug,
        CancellationToken cancellationToken = default);

    /// <summary>Opens one lesson, recording that the learner started it.</summary>
    Task<OperationResult<LessonDetail>> GetLessonAsync(
        Guid? userId,
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a resume position and, optionally, completion.</summary>
    Task<OperationResult<ProgressRecorded>> RecordProgressAsync(
        Guid userId,
        Guid lessonId,
        ProgressUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>The learner's shelf, resolved from entitlements at read time.</summary>
    Task<OperationResult<IReadOnlyList<MyLearningCourse>>> ListMyLearningAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>The learner's earned certificates.</summary>
    Task<OperationResult<IReadOnlyList<CertificateView>>> ListCertificatesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Public verification of a certificate code. Anonymous by design.</summary>
    Task<OperationResult<CertificateVerification>> VerifyCertificateAsync(
        string verificationCode,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a certificate. Admin only; a reason is mandatory.</summary>
    Task<OperationResult<CertificateView>> RevokeCertificateAsync(
        Guid certificateId,
        string reason,
        CancellationToken cancellationToken = default);
}
