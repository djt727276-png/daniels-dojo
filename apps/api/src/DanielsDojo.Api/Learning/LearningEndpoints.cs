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
