using DanielsDojo.Application.Common;

namespace DanielsDojo.Application.Commerce;

/// <summary>
/// Local offer and price management.
/// </summary>
/// <remarks>
/// Everything here is database-only. No implementation calls a payment provider, creates a
/// product or price there, or accepts a provider identifier from a client — that integration
/// belongs to a later phase, and pretending otherwise would leave the two systems disagreeing.
/// </remarks>
public interface IAdminPricingService
{
    /// <summary>Lists every offer with its prices.</summary>
    Task<IReadOnlyList<AdminOffer>> ListOffersAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one offer, or null.</summary>
    Task<AdminOffer?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>Creates a Draft offer.</summary>
    Task<OperationResult<AdminOffer>> CreateOfferAsync(
        CreateOfferRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an offer, refusing commercial changes once it has been activated.</summary>
    Task<OperationResult<AdminOffer>> UpdateOfferAsync(
        Guid offerId,
        UpdateOfferRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves an offer through the commerce status graph.</summary>
    Task<OperationResult<AdminOffer>> ChangeOfferStatusAsync(
        Guid offerId,
        string targetStatus,
        CommerceStatusChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes a new Draft price beneath an offer.</summary>
    Task<OperationResult<AdminOffer>> CreatePriceAsync(
        Guid offerId,
        CreatePriceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Edits a Draft price.</summary>
    Task<OperationResult<AdminOffer>> UpdatePriceAsync(
        Guid offerId,
        Guid priceId,
        UpdatePriceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a price through the commerce status graph.</summary>
    Task<OperationResult<AdminOffer>> ChangePriceStatusAsync(
        Guid offerId,
        Guid priceId,
        string targetStatus,
        CommerceStatusChangeRequest request,
        CancellationToken cancellationToken = default);
}
