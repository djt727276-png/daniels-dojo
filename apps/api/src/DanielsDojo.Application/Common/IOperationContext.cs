namespace DanielsDojo.Application.Common;

/// <summary>
/// Ambient facts about the current request that every audited mutation needs: who acted and
/// which request it belonged to.
/// </summary>
/// <remarks>
/// Separate from <see cref="Identity.ICurrentUser"/> so infrastructure services can write audit
/// rows without taking a dependency on HTTP. The actor is the immutable local application user
/// identifier — never a claim value, which a client could influence.
/// </remarks>
public interface IOperationContext
{
    /// <summary>Local application user identifier of the actor, or null for system actions.</summary>
    Guid? ActorUserId { get; }

    /// <summary>Correlation identifier tying audit rows and logs to one request.</summary>
    string CorrelationId { get; }
}
