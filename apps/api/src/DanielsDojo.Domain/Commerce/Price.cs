namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// A published amount for an offer. Money is stored as integer minor units plus an
/// uppercase ISO-4217 currency — never floating point. Once a price has been used
/// externally it is immutable: a change publishes a new row and retires the old one.
/// </summary>
public sealed class Price
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning offer.</summary>
    public Guid OfferId { get; set; }

    /// <summary>Amount in minor units, for example 999 for USD 9.99. Must be positive.</summary>
    public long AmountMinor { get; set; }

    /// <summary>Uppercase ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How often the amount is charged.</summary>
    public BillingInterval BillingInterval { get; set; }

    /// <summary>Number of intervals per billing cycle. Constrained to 1 at launch.</summary>
    public int BillingIntervalCount { get; set; } = 1;

    /// <summary>Provider price identifier. Unique when present.</summary>
    public string? StripePriceId { get; set; }

    /// <summary>Lifecycle state.</summary>
    public CommerceStatus Status { get; set; } = CommerceStatus.Draft;

    /// <summary>When the price becomes chargeable, stored UTC.</summary>
    public DateTimeOffset EffectiveFromUtc { get; set; }

    /// <summary>When the price stopped being chargeable. Must not precede the effective date.</summary>
    public DateTimeOffset? RetiredAtUtc { get; set; }

    /// <summary>Creation instant, stored UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last modification instant, stored UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The owning offer.</summary>
    public Offer? Offer { get; set; }
}
