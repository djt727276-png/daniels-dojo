using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.Privacy;

namespace DanielsDojo.Api.Privacy;

/// <summary>Confirms an account deletion. The phrase is typed, not clicked.</summary>
/// <param name="Confirmation">Must be exactly "delete my account".</param>
public sealed record DeleteAccountRequest(string Confirmation);

/// <summary>
/// The member's rights over their own data.
/// </summary>
/// <remarks>
/// Both routes act only on the resolved current user — no identifier appears in any path or
/// body — so neither can ever be pointed at somebody else's account. Deletion additionally
/// requires a typed confirmation phrase, because a destructive action should cost a
/// deliberate sentence, not a stray click.
/// </remarks>
internal static class PrivacyEndpoints
{
    /// <summary>The exact phrase deletion requires.</summary>
    internal const string DeletionPhrase = "delete my account";

    /// <summary>Maps the privacy routes.</summary>
    public static void MapPrivacyEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder me = apiV1
            .MapGroup("/me")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        me.MapGet("/privacy/export", async (
                ICurrentUser currentUser,
                IPrivacyService privacy,
                CancellationToken cancellationToken) =>
            {
                PersonalDataExport export = await privacy.ExportAsync(
                    currentUser.User!.UserId, cancellationToken);

                // Served as a download: this is the member's copy to keep, not a screen.
                return Results.Json(
                    export,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status200OK);
            })
            .WithName("ExportMyData")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite);

        me.MapPost("/privacy/delete-account", async (
                DeleteAccountRequest request,
                ICurrentUser currentUser,
                IPrivacyService privacy,
                CancellationToken cancellationToken) =>
            {
                if (!string.Equals(
                        request.Confirmation?.Trim(),
                        DeletionPhrase,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["confirmation"] =
                            [$"Type \"{DeletionPhrase}\" to confirm."],
                    });
                }

                Application.Common.OperationResult result = await privacy.DeleteAccountAsync(
                    currentUser.User!.UserId, cancellationToken);

                return result.Succeeded ? Results.NoContent() : OperationResults.ToProblem(result);
            })
            .WithName("DeleteMyAccount")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite);
    }
}
