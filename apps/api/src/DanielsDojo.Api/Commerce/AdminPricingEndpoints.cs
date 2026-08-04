using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Commerce;

namespace DanielsDojo.Api.Commerce;

/// <summary>
/// Offer and price management, restricted to database-backed Admins.
/// </summary>
/// <remarks>
/// Every route here reads and writes this database only. Nothing calls a payment provider, and
/// no request binds a provider identifier — the contracts simply have no field for one, so a
/// client cannot attach a Stripe product or price by putting its ID in a body.
/// </remarks>
internal static class AdminPricingEndpoints
{
    /// <summary>Maps the Admin pricing routes.</summary>
    public static void MapAdminPricingEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder pricing = apiV1
            .MapGroup("/admin/pricing")
            .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

        pricing.MapGet("/offers", async (
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListOffersAsync(cancellationToken)))
            .WithName("ListAdminOffers");

        pricing.MapGet("/offers/{offerId:guid}", async (
                Guid offerId,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            {
                AdminOffer? offer = await service.GetOfferAsync(offerId, cancellationToken);

                return offer is null ? Results.NotFound() : Results.Ok(offer);
            })
            .WithName("GetAdminOffer");

        pricing.MapPost("/offers", async (
                CreateOfferRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToCreated(
                await service.CreateOfferAsync(request, cancellationToken),
                static offer => $"/api/v1/admin/pricing/offers/{offer.Id}"))
            .WithName("CreateAdminOffer");

        pricing.MapPut("/offers/{offerId:guid}", async (
                Guid offerId,
                UpdateOfferRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdateOfferAsync(offerId, request, cancellationToken)))
            .WithName("UpdateAdminOffer");

        pricing.MapPost("/offers/{offerId:guid}/status/{targetStatus}", async (
                Guid offerId,
                string targetStatus,
                CommerceStatusChangeRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ChangeOfferStatusAsync(
                    offerId, targetStatus, request, cancellationToken)))
            .WithName("ChangeAdminOfferStatus");

        pricing.MapPost("/offers/{offerId:guid}/prices", async (
                Guid offerId,
                CreatePriceRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.CreatePriceAsync(offerId, request, cancellationToken)))
            .WithName("CreateAdminPrice");

        pricing.MapPut("/offers/{offerId:guid}/prices/{priceId:guid}", async (
                Guid offerId,
                Guid priceId,
                UpdatePriceRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.UpdatePriceAsync(offerId, priceId, request, cancellationToken)))
            .WithName("UpdateAdminPrice");

        pricing.MapPost("/offers/{offerId:guid}/prices/{priceId:guid}/status/{targetStatus}", async (
                Guid offerId,
                Guid priceId,
                string targetStatus,
                CommerceStatusChangeRequest request,
                IAdminPricingService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ChangePriceStatusAsync(
                    offerId, priceId, targetStatus, request, cancellationToken)))
            .WithName("ChangeAdminPriceStatus");
    }
}
