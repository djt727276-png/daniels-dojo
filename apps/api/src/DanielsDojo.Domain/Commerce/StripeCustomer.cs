using DanielsDojo.Domain.Identity;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// One-to-one link between a platform user and the payment provider's customer record.
/// Stores identifiers only — no card data, tokens, or provider secrets.
/// </summary>
public sealed class StripeCustomer
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Linked user. Unique: one provider customer per user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Provider customer identifier. Unique.</summary>
    public string StripeCustomerId { get; set; } = string.Empty;

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>The linked user.</summary>
    public User? User { get; set; }
}
