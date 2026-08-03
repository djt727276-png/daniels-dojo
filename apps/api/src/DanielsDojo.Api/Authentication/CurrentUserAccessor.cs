using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Scoped holder for the local user behind the current request. The provisioning middleware is
/// the only writer; everything downstream reads it.
/// </summary>
internal sealed class CurrentUserAccessor : ICurrentUser
{
    /// <inheritdoc />
    public ApplicationUser? User { get; private set; }

    /// <summary>Records the resolved local user for this request.</summary>
    public void Set(ApplicationUser user) => User = user;
}
