using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Learning;

namespace DanielsDojo.Api.Learning;

/// <summary>Why a certificate is being revoked.</summary>
/// <param name="Reason">Mandatory human explanation, recorded on the certificate.</param>
internal sealed record RevokeCertificateRequest(string Reason);

/// <summary>
/// The learner-facing course experience.
/// </summary>
/// <remarks>
/// The curriculum and lesson routes allow anonymous callers through so a published preview
/// works without a sign-in; the access evaluator, not the route, decides what comes back.
/// Progress and My Learning require a signed-in learner because they are that person's own
/// record.
/// </remarks>
internal static class LearningEndpoints
{
    /// <summary>Maps the learning routes.</summary>
    public static void MapLearningEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder learning = apiV1.MapGroup("/learning");

        learning.MapGet("/courses/{courseSlug}", async (
                string courseSlug,
                ICurrentUser currentUser,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetCurriculumAsync(
                    currentUser.User?.UserId, courseSlug, cancellationToken)))
            .AllowAnonymous()
            .WithName("GetCourseCurriculum");

        learning.MapGet("/lessons/{lessonId:guid}", async (
                Guid lessonId,
                ICurrentUser currentUser,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetLessonAsync(
                    currentUser.User?.UserId, lessonId, cancellationToken)))
            .AllowAnonymous()
            .WithName("GetLessonDetail");

        learning.MapPost("/lessons/{lessonId:guid}/progress", async (
                Guid lessonId,
                ProgressUpdate update,
                ICurrentUser currentUser,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RecordProgressAsync(
                    currentUser.User!.UserId, lessonId, update, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy)
            .WithName("RecordLessonProgress");

        learning.MapGet("/certificates", async (
                ICurrentUser currentUser,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ListCertificatesAsync(currentUser.User!.UserId, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy)
            .WithName("GetMyCertificates");

        // Public verification: anyone holding a printed code may confirm it. Only what the
        // certificate itself displays comes back.
        apiV1.MapGet("/certificates/{verificationCode}/verify", async (
                string verificationCode,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.VerifyCertificateAsync(verificationCode, cancellationToken)))
            .AllowAnonymous()
            .WithName("VerifyCertificate");

        apiV1.MapPost("/admin/certificates/{certificateId:guid}/revoke", async (
                Guid certificateId,
                RevokeCertificateRequest request,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RevokeCertificateAsync(
                    certificateId, request.Reason, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("RevokeCertificate");

        // Reviews. Reading is public on published courses; writing is gated server-side on
        // entitlement plus real progress, and one slot per member per course.
        apiV1.MapGet("/catalog/courses/{courseSlug}/reviews", async (
                string courseSlug,
                int? page,
                ICurrentUser currentUser,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetCourseReviewsAsync(
                    courseSlug, currentUser.User == null ? null : currentUser.User.UserId, page ?? 0, cancellationToken)))
            .AllowAnonymous()
            .WithName("GetCourseReviews");

        learning.MapPut("/courses/{courseSlug}/review", async (
                string courseSlug,
                WriteReviewRequest request,
                ICurrentUser currentUser,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.WriteReviewAsync(
                    currentUser.User!.UserId, courseSlug, request, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy)
            .WithName("WriteCourseReview");

        learning.MapDelete("/courses/{courseSlug}/review", async (
                string courseSlug,
                ICurrentUser currentUser,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.DeleteReviewAsync(
                    currentUser.User!.UserId, courseSlug, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy)
            .WithName("DeleteCourseReview");

        apiV1.MapGet("/admin/reviews", async (
                string? status,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ListForModerationAsync(status, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("ListReviewsForModeration");

        apiV1.MapPost("/admin/reviews/{reviewId:guid}/hide", async (
                Guid reviewId,
                RevokeCertificateRequest request,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.HideReviewAsync(reviewId, request.Reason, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("HideReview");

        apiV1.MapPost("/admin/reviews/{reviewId:guid}/restore", async (
                Guid reviewId,
                ICourseReviewService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.RestoreReviewAsync(reviewId, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy)
            .WithName("RestoreReview");

        learning.MapGet("/my-learning", async (
                ICurrentUser currentUser,
                ILearningService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ListMyLearningAsync(currentUser.User!.UserId, cancellationToken)))
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy)
            .WithName("GetMyLearning");
    }
}
