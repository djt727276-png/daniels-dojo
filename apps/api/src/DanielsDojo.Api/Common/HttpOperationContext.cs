using System.Diagnostics;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Common;

/// <summary>
/// Supplies the actor and correlation identifier for audited writes.
/// </summary>
/// <remarks>
/// The actor is the immutable local application user identifier resolved by the provisioning
/// middleware — not a token claim, which a client controls, and not an email, which a member
/// can change. Correlation prefers the current activity so an audit row lines up with the
/// distributed trace for the same request.
/// </remarks>
internal sealed class HttpOperationContext(
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor) : IOperationContext
{
    /// <summary>Correlation column width; identifiers are truncated to fit.</summary>
    private const int MaxCorrelationLength = 64;

    public Guid? ActorUserId => currentUser.User?.UserId;

    public string CorrelationId
    {
        get
        {
            string? identifier = Activity.Current?.Id
                ?? httpContextAccessor.HttpContext?.TraceIdentifier;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "unknown";
            }

            return identifier.Length <= MaxCorrelationLength
                ? identifier
                : identifier[..MaxCorrelationLength];
        }
    }
}
