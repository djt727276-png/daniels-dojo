namespace DanielsDojo.Application.Commerce;

/// <summary>
/// A price as the pricing screen sees it.
/// </summary>
/// <remarks>
/// There is deliberately no provider price identifier here. Nothing in Phase 4 talks to a
/// payment provider, and an operator screen has no use for a key it cannot act on.
/// </remarks>
public sealed record AdminPrice(
    Guid Id,
    long AmountMinor,
    string Currency,
    string BillingInterval,
    int BillingIntervalCount,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? RetiredAtUtc,
    bool Editable,
    string RowVersion);

/// <summary>An offer and every price published beneath it.</summary>
public sealed record AdminOffer(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Kind,
    Guid? CourseId,
    string? CourseTitle,
    string Status,
    bool ProviderLinked,
    bool CommercialFieldsEditable,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AdminPrice> Prices,
    string RowVersion);

/// <summary>Creates a Draft offer.</summary>
public sealed record CreateOfferRequest(
    string Code,
    string Name,
    string Description,
    string Kind,
    Guid? CourseId);

/// <summary>
/// Updates an offer.
/// </summary>
/// <remarks>
/// Code, kind, and course are only accepted while the offer is a draft; once it is Active or
/// Retired they are what existing orders, subscriptions, and entitlements were sold under.
/// </remarks>
public sealed record UpdateOfferRequest(
    string Code,
    string Name,
    string Description,
    Guid? CourseId,
    string RowVersion);

/// <summary>A commerce status change, with the reason the audit trail records.</summary>
public sealed record CommerceStatusChangeRequest(string Reason, string RowVersion);

/// <summary>
/// Publishes a new Draft price.
/// </summary>
/// <remarks>
/// No provider key is accepted. Changing an amount means publishing a new price and retiring
/// the old one, which is why there is no "amount" edit for anything that has been active.
/// </remarks>
public sealed record CreatePriceRequest(
    long AmountMinor,
    string Currency,
    string BillingInterval,
    DateTimeOffset EffectiveFromUtc);

/// <summary>Edits a Draft price. Refused for Active and Retired prices.</summary>
public sealed record UpdatePriceRequest(
    long AmountMinor,
    string Currency,
    string BillingInterval,
    DateTimeOffset EffectiveFromUtc,
    string RowVersion);
