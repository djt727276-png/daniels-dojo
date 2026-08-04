using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Common;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Application.Identity;

namespace DanielsDojo.Api.Commerce;

/// <summary>
/// Customer purchasing: checkout, confirmation, billing portal, order history, and the
/// signature-authenticated payment webhook.
/// </summary>
internal static class CheckoutEndpoints
{
    /// <summary>Maps the customer commerce routes.</summary>
    public static void MapCheckoutEndpoints(this RouteGroupBuilder apiV1)
    {
        RouteGroupBuilder billing = apiV1
            .MapGroup("/billing")
            .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

        billing.MapPost("/checkout", async (
                StartCheckoutRequest request,
                ICurrentUser currentUser,
                ICheckoutService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.StartCheckoutAsync(
                    currentUser.User!.UserId, request, cancellationToken)))
            .WithName("StartCheckout");

        // The browser returns here after paying. The session identifier is a lookup key, not
        // proof: the service reads the session from the provider before anything is granted.
        billing.MapPost("/checkout/{sessionId}/confirm", async (
                string sessionId,
                ICurrentUser currentUser,
                ICheckoutService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.ConfirmCheckoutAsync(
                    currentUser.User!.UserId, sessionId, cancellationToken)))
            .WithName("ConfirmCheckout");

        billing.MapPost("/portal", async (
                ICurrentUser currentUser,
                ICheckoutService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.StartPortalAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("StartBillingPortal");

        billing.MapGet("/", async (
                ICurrentUser currentUser,
                ICheckoutService service,
                CancellationToken cancellationToken) =>
            OperationResults.ToResponse(
                await service.GetBillingAsync(currentUser.User!.UserId, cancellationToken)))
            .WithName("GetBillingOverview");

        // Anonymous by necessity — the provider holds no credential of ours — authenticated by
        // signature instead. The raw body is read once and never logged.
        apiV1.MapPost("/billing/webhooks/stripe", async (
                HttpRequest request,
                ICommerceWebhookService service,
                CancellationToken cancellationToken) =>
            {
                using StreamReader reader = new(request.Body);
                string payload = await reader.ReadToEndAsync(cancellationToken);

                string? signature = request.Headers["Stripe-Signature"].FirstOrDefault();

                return await service.HandlePaymentEventAsync(payload, signature, cancellationToken)
                    ? Results.Accepted()
                    : Results.Unauthorized();
            })
            .AllowAnonymous()
            .WithName("ReceivePaymentProviderEvent");
    }
}
