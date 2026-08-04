using DanielsDojo.Application.Commerce;
using DanielsDojo.Domain.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>Registers the payment provider named by configuration.</summary>
/// <remarks>
/// The same explicit-mode rule as media: the mode is acted on literally, never inferred from
/// whether a key is present. A production deployment missing its key fails at startup rather
/// than silently swapping in the deterministic adapter — which would take money nowhere while
/// looking like it had.
/// </remarks>
public static class CommerceRegistration
{
    /// <summary>Registers the payment adapter and the services built on it.</summary>
    public static IServiceCollection AddCommerce(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PaymentProviderOptions>()
            .Bind(configuration.GetSection(PaymentProviderOptions.SectionName))
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || !string.IsNullOrWhiteSpace(options.SecretKey),
                $"{PaymentProviderOptions.SectionName}:SecretKey is required when Mode is Real.")
            .Validate(
                static options => options.Mode != ProviderMode.Real
                    || !string.IsNullOrWhiteSpace(options.WebhookSecret),
                $"{PaymentProviderOptions.SectionName}:WebhookSecret is required when Mode is Real; "
                + "without it no inbound payment notification can be authenticated.")
            .ValidateOnStart();

        ProviderMode mode = ReadMode(configuration);

        switch (mode)
        {
            case ProviderMode.Real:
                services.AddScoped<IPaymentProvider, StripePaymentProvider>();
                break;

            case ProviderMode.Deterministic:
                // Singleton so a session created in one request is completable in the next —
                // the whole point of the deterministic payment machine.
                services.AddSingleton<DeterministicPaymentProvider>();
                services.AddScoped<IPaymentProvider>(provider =>
                    provider.GetRequiredService<DeterministicPaymentProvider>());
                break;

            case ProviderMode.Disabled:
            default:
                services.AddScoped<IPaymentProvider, DisabledPaymentProvider>();
                break;
        }

        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ICommerceWebhookService, CommerceWebhookService>();

        return services;
    }

    private static ProviderMode ReadMode(IConfiguration configuration)
    {
        string? configured = configuration.GetSection(PaymentProviderOptions.SectionName)["Mode"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return ProviderMode.Disabled;
        }

        return Enum.TryParse(configured, ignoreCase: false, out ProviderMode mode)
            ? mode
            : throw new InvalidOperationException(
                $"{PaymentProviderOptions.SectionName}:Mode must be one of Disabled, "
                + "Deterministic, or Real. The value is compared exactly, so casing matters.");
    }
}

/// <summary>The adapter used when payments are switched off. Refuses rather than pretending.</summary>
internal sealed class DisabledPaymentProvider : IPaymentProvider
{
    public ProviderMode Mode => ProviderMode.Disabled;

    public Task<string> EnsureCustomerAsync(
        Guid userId, string email, string displayName,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<CheckoutTicket> CreateCheckoutAsync(
        string customerId, string providerPriceId, bool isRecurring, string clientReference,
        CancellationToken cancellationToken = default) => throw Refuse();

    public Task<CheckoutState?> GetCheckoutAsync(
        string sessionId, CancellationToken cancellationToken = default) => throw Refuse();

    public Task<SubscriptionState?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default) => throw Refuse();

    public Task<PortalTicket> CreatePortalAsync(
        string customerId, CancellationToken cancellationToken = default) => throw Refuse();

    /// <summary>Always refuses, so a deployment with payments off cannot be driven anonymously.</summary>
    public PaymentProviderEvent? VerifyEvent(string payload, string? signatureHeader, DateTimeOffset now) =>
        null;

    public Task<(string ProductId, string PriceId)> EnsurePriceAsync(
        string offerCode, string offerName, Guid localPriceId, long amountMinor, string currency,
        bool isRecurring, CancellationToken cancellationToken = default) => throw Refuse();

    private static InvalidOperationException Refuse() =>
        new($"Payments are disabled. Set {PaymentProviderOptions.SectionName}:Mode to "
            + "Deterministic or Real before using the commerce pipeline.");
}
