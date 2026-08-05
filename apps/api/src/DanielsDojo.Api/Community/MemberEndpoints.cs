using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Community;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Community;

/// <summary>
/// The signed-in member's own screens: dashboard, learning list, and community profile.
/// </summary>
/// <remarks>
/// Every route resolves the member from <see cref="ICurrentUser"/> — the local user the
/// provisioning middleware established — so there is no user identifier in any path or body
/// that a caller could change to read someone else's data.
/// </remarks>
internal static class MemberEndpoints
{
    /// <summary>Maps the member's own routes.</summary>
    public static void MapMemberEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder me = apiV1
            .MapGroup("/me")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        me.MapGet("/dashboard", async (
                ICurrentUser currentUser,
                IMemberService members,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(
                await members.GetDashboardAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("GetMemberDashboard");

        me.MapGet("/courses", async (
                ICurrentUser currentUser,
                IMemberService members,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(
                await members.GetMyCoursesAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("GetMemberCourses");

        me.MapGet("/community/status", async (
                ICurrentUser currentUser,
                ICommunityAccessEvaluator evaluator,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(CommunityStatusResponse.From(
                await evaluator.EvaluateAsync(currentUser.User!.UserId, cancellationToken))))
            .WithName("GetMemberCommunityStatus");

        me.MapGet("/community/profile", async (
                ICurrentUser currentUser,
                IMemberService members,
                CancellationToken cancellationToken) =>
            {
                MyCommunityProfile? profile =
                    await members.GetCommunityProfileAsync(currentUser.User!.UserId, cancellationToken);

                // 404 means "you have not set one up", which is exactly what the client needs
                // in order to show the setup screen.
                return profile is null ? Results.NotFound() : Results.Ok(profile);
            })
            .WithName("GetMemberCommunityProfile");

        me.MapPost("/community/profile", async (
                CompleteCommunitySetupRequest request,
                ICurrentUser currentUser,
                IMemberService members,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await members.CompleteCommunitySetupAsync(
                    currentUser.User!.UserId, request, cancellationToken)))
            .WithName("CompleteMemberCommunitySetup");

        me.MapPut("/community/profile", async (
                UpdateCommunityProfileRequest request,
                ICurrentUser currentUser,
                IMemberService members,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await members.UpdateCommunityProfileAsync(
                    currentUser.User!.UserId, request, cancellationToken)))
            .WithName("UpdateMemberCommunityProfile");

        me.MapPut("/community/profile/avatar", async (
                IFormFile file,
                ICurrentUser currentUser,
                IAvatarService avatars,
                CancellationToken cancellationToken) =>
            {
                await using Stream content = file.OpenReadStream();

                Application.Common.OperationResult result = await avatars.SetAsync(
                    currentUser.User!.UserId, content, file.Length, cancellationToken);

                return result.Succeeded ? Results.NoContent() : OperationResults.ToProblem(result);
            })
            .WithName("SetMemberAvatar")
            .RequireRateLimiting(RateLimitPolicies.CommunityWrite)
            .DisableAntiforgery();

        me.MapDelete("/community/profile/avatar", async (
                ICurrentUser currentUser,
                IAvatarService avatars,
                CancellationToken cancellationToken) =>
            {
                await avatars.RemoveAsync(currentUser.User!.UserId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RemoveMemberAvatar");
    }
}
