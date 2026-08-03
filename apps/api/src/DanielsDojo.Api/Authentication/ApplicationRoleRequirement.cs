using DanielsDojo.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DanielsDojo.Api.Authentication;

/// <summary>Requires that the resolved local user holds a named application role.</summary>
/// <param name="roleName">Role name as seeded in the local database.</param>
internal sealed class ApplicationRoleRequirement(string roleName) : IAuthorizationRequirement
{
    /// <summary>Required role name.</summary>
    public string RoleName { get; } = roleName;
}

/// <summary>
/// Evaluates <see cref="ApplicationRoleRequirement"/> against the local database's role
/// assignments only.
/// </summary>
/// <remarks>
/// Roles are read from <see cref="ICurrentUser"/>, which the provisioning middleware populated
/// from <c>identity.UserRoles</c>. A <c>roles</c> or <c>groups</c> claim in the token is
/// deliberately ignored: application permissions are the local database's decision, and a
/// client must never be able to influence them by presenting a claim.
/// </remarks>
internal sealed class ApplicationRoleHandler(ICurrentUser currentUser)
    : AuthorizationHandler<ApplicationRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApplicationRoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (currentUser.User?.IsInRole(requirement.RoleName) == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
