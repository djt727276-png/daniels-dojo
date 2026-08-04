using System.Globalization;
using DanielsDojo.Application.Commerce;
using DanielsDojo.Application.Common;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Auditing;
using DanielsDojo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DanielsDojo.Infrastructure.Commerce;

/// <summary>
/// Local offer and price management.
/// </summary>
/// <remarks>
/// <para>
/// Money is never edited in place once it has been sold under. A price is editable only while
/// it is a draft; changing an active amount means publishing a new price and retiring the old
/// one, so an order written last month still resolves to the amount that was actually charged.
/// </para>
/// <para>
/// No provider identifier is read from or written by any request here. The request records
/// have no field for one, so a client cannot claim a Stripe product or price simply by putting
/// its ID in a body.
/// </para>
/// </remarks>
internal sealed class AdminPricingService : IAdminPricingService
{
    /// <summary>Hard ceiling on the offer list, which has no paging of its own.</summary>
    private const int MaxOfferListSize = 200;

    private readonly DanielsDojoDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly AuditTrail audit;

    public AdminPricingService(
        DanielsDojoDbContext context,
        IOperationContext operationContext,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        audit = new AuditTrail(context, operationContext, timeProvider);
    }

    public async Task<IReadOnlyList<AdminOffer>> ListOffersAsync(
        CancellationToken cancellationToken = default)
    {
        List<Offer> offers = await Query(tracked: false)
            .OrderBy(offer => offer.Name)
            .ThenBy(offer => offer.Id)
            .Take(MaxOfferListSize)
            .ToListAsync(cancellationToken);

        return offers.Select(Project).ToArray();
    }

    public async Task<AdminOffer?> GetOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        Offer? offer = await Query(tracked: false)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);

        return offer is null ? null : Project(offer);
    }

    public async Task<OperationResult<AdminOffer>> CreateOfferAsync(
        CreateOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = new ValidationBuilder()
            .Required("code", request.Code, 64, "Code")
            .Required("name", request.Name, 200, "Name")
            .Required("description", request.Description, 1000, "Description")
            .When(
                !Enum.TryParse(request.Kind, ignoreCase: true, out OfferKind _),
                "kind",
                "Choose a valid offer kind.");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AdminOffer>();
        }

        var kind = Enum.Parse<OfferKind>(request.Kind, ignoreCase: true);
        OperationResult? shape = await ValidateOfferShapeAsync(kind, request.CourseId, cancellationToken);

        if (shape is not null)
        {
            return shape.ToFailure<AdminOffer>();
        }

        string code = request.Code.Trim();

        if (await context.Offers.AnyAsync(offer => offer.Code == code, cancellationToken))
        {
            return OperationResult.Invalid(
                ErrorCodes.DuplicateValue,
                "code",
                "Another offer already uses this code.").ToFailure<AdminOffer>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var created = new Offer
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Kind = kind,
            CourseId = kind == OfferKind.CourseLifetime ? request.CourseId : null,
            Status = CommerceStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Offers.Add(created);
        audit.Append(
            "Commerce.Offer.Created",
            nameof(Offer),
            created.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = created.Code,
                ["kind"] = created.Kind.ToString(),
            });

        return await SaveAndReloadAsync(created.Id, cancellationToken);
    }

    public async Task<OperationResult<AdminOffer>> UpdateOfferAsync(
        Guid offerId,
        UpdateOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Offer? offer = await Query(tracked: true)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);

        if (offer is null)
        {
            return OperationResult.NotFound().ToFailure<AdminOffer>();
        }

        var validation = new ValidationBuilder()
            .Required("code", request.Code, 64, "Code")
            .Required("name", request.Name, 200, "Name")
            .Required("description", request.Description, 1000, "Description");

        if (validation.HasErrors)
        {
            return validation.ToResult().ToFailure<AdminOffer>();
        }

        string code = request.Code.Trim();
        bool commercialChange =
            !string.Equals(code, offer.Code, StringComparison.Ordinal)
            || request.CourseId != offer.CourseId;

        if (commercialChange && !CommerceStatusGraph.IsEditable(offer.Status))
        {
            return OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "code",
                "The code and course are fixed once an offer has been activated. Retire it and "
                + "publish a new offer instead.").ToFailure<AdminOffer>();
        }

        if (commercialChange)
        {
            OperationResult? shape =
                await ValidateOfferShapeAsync(offer.Kind, request.CourseId, cancellationToken);

            if (shape is not null)
            {
                return shape.ToFailure<AdminOffer>();
            }

            if (await context.Offers.AnyAsync(
                    other => other.Code == code && other.Id != offerId,
                    cancellationToken))
            {
                return OperationResult.Invalid(
                    ErrorCodes.DuplicateValue,
                    "code",
                    "Another offer already uses this code.").ToFailure<AdminOffer>();
            }
        }

        if (!ApplyRowVersion(offer, request.RowVersion))
        {
            return InvalidRowVersion().ToFailure<AdminOffer>();
        }

        if (commercialChange)
        {
            offer.Code = code;
            offer.CourseId = offer.Kind == OfferKind.CourseLifetime ? request.CourseId : null;
        }

        offer.Name = request.Name.Trim();
        offer.Description = request.Description.Trim();
        offer.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Commerce.Offer.Updated",
            nameof(Offer),
            offer.Id,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["code"] = offer.Code,
                ["commercialChange"] = commercialChange ? "true" : "false",
            });

        return await SaveAndReloadAsync(offerId, cancellationToken);
    }

    public async Task<OperationResult<AdminOffer>> ChangeOfferStatusAsync(
        Guid offerId,
        string targetStatus,
        CommerceStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Offer? offer = await Query(tracked: true)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);

        if (offer is null)
        {
            return OperationResult.NotFound().ToFailure<AdminOffer>();
        }

        OperationResult? refusal = ValidateStatusChange(
            offer.Status, targetStatus, request.Reason, out CommerceStatus target);

        if (refusal is not null)
        {
            return refusal.ToFailure<AdminOffer>();
        }

        // An offer with no active price is not purchasable, so activating it would advertise
        // something the checkout could not complete.
        if (target == CommerceStatus.Active
            && !offer.Prices.Any(price => price.Status == CommerceStatus.Active))
        {
            return OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "status",
                "Activate a price for this offer first.").ToFailure<AdminOffer>();
        }

        if (!ApplyRowVersion(offer, request.RowVersion))
        {
            return InvalidRowVersion().ToFailure<AdminOffer>();
        }

        CommerceStatus previous = offer.Status;
        offer.Status = target;
        offer.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Commerce.Offer.StatusChanged",
            nameof(Offer),
            offer.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

        return await SaveAndReloadAsync(offerId, cancellationToken);
    }

    public async Task<OperationResult<AdminOffer>> CreatePriceAsync(
        Guid offerId,
        CreatePriceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Offer? offer = await Query(tracked: true)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);

        if (offer is null)
        {
            return OperationResult.NotFound().ToFailure<AdminOffer>();
        }

        OperationResult? invalid = ValidatePriceFields(
            request.AmountMinor, request.Currency, request.BillingInterval, out BillingInterval interval);

        if (invalid is not null)
        {
            return invalid.ToFailure<AdminOffer>();
        }

        OperationResult? mismatch = ValidateIntervalForKind(offer.Kind, interval);

        if (mismatch is not null)
        {
            return mismatch.ToFailure<AdminOffer>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var price = new Price
        {
            Id = Guid.CreateVersion7(),
            OfferId = offerId,
            AmountMinor = request.AmountMinor,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            BillingInterval = interval,
            BillingIntervalCount = 1,
            Status = CommerceStatus.Draft,
            EffectiveFromUtc = request.EffectiveFromUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Prices.Add(price);
        audit.Append(
            "Commerce.Price.Created",
            nameof(Price),
            price.Id,
            metadata: PriceMetadata(offerId, price));

        return await SaveAndReloadAsync(offerId, cancellationToken);
    }

    public async Task<OperationResult<AdminOffer>> UpdatePriceAsync(
        Guid offerId,
        Guid priceId,
        UpdatePriceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Offer? offer = await Query(tracked: true)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);
        Price? price = offer?.Prices.FirstOrDefault(candidate => candidate.Id == priceId);

        if (offer is null || price is null)
        {
            return OperationResult.NotFound().ToFailure<AdminOffer>();
        }

        if (!CommerceStatusGraph.IsEditable(price.Status))
        {
            return OperationResult.Invalid(
                ErrorCodes.PriceImmutable,
                "amountMinor",
                "A price cannot change once it has been activated. Retire it and publish a new "
                + "price instead.").ToFailure<AdminOffer>();
        }

        OperationResult? invalid = ValidatePriceFields(
            request.AmountMinor, request.Currency, request.BillingInterval, out BillingInterval interval);

        if (invalid is not null)
        {
            return invalid.ToFailure<AdminOffer>();
        }

        OperationResult? mismatch = ValidateIntervalForKind(offer.Kind, interval);

        if (mismatch is not null)
        {
            return mismatch.ToFailure<AdminOffer>();
        }

        if (!ApplyRowVersion(price, request.RowVersion))
        {
            return InvalidRowVersion().ToFailure<AdminOffer>();
        }

        price.AmountMinor = request.AmountMinor;
        price.Currency = request.Currency.Trim().ToUpperInvariant();
        price.BillingInterval = interval;
        price.EffectiveFromUtc = request.EffectiveFromUtc;
        price.UpdatedAtUtc = timeProvider.GetUtcNow();

        audit.Append(
            "Commerce.Price.Updated",
            nameof(Price),
            price.Id,
            metadata: PriceMetadata(offerId, price));

        return await SaveAndReloadAsync(offerId, cancellationToken);
    }

    public async Task<OperationResult<AdminOffer>> ChangePriceStatusAsync(
        Guid offerId,
        Guid priceId,
        string targetStatus,
        CommerceStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Offer? offer = await Query(tracked: true)
            .FirstOrDefaultAsync(candidate => candidate.Id == offerId, cancellationToken);
        Price? price = offer?.Prices.FirstOrDefault(candidate => candidate.Id == priceId);

        if (offer is null || price is null)
        {
            return OperationResult.NotFound().ToFailure<AdminOffer>();
        }

        OperationResult? refusal = ValidateStatusChange(
            price.Status, targetStatus, request.Reason, out CommerceStatus target);

        if (refusal is not null)
        {
            return refusal.ToFailure<AdminOffer>();
        }

        // Two live prices for one offer would make "the current price" ambiguous, and the
        // public catalog would have to guess. Retiring the incumbent is an explicit decision.
        if (target == CommerceStatus.Active
            && offer.Prices.Any(other => other.Id != priceId && other.Status == CommerceStatus.Active))
        {
            return OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "status",
                "Retire the current active price before activating another one.")
                .ToFailure<AdminOffer>();
        }

        if (!ApplyRowVersion(price, request.RowVersion))
        {
            return InvalidRowVersion().ToFailure<AdminOffer>();
        }

        CommerceStatus previous = price.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();

        price.Status = target;
        price.UpdatedAtUtc = now;

        if (target == CommerceStatus.Retired)
        {
            // Never before the effective date: a check constraint enforces the same thing.
            price.RetiredAtUtc = now < price.EffectiveFromUtc ? price.EffectiveFromUtc : now;
        }

        audit.Append(
            "Commerce.Price.StatusChanged",
            nameof(Price),
            price.Id,
            request.Reason,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offerId"] = offerId.ToString("D"),
                ["previousStatus"] = previous.ToString(),
                ["status"] = target.ToString(),
            });

        return await SaveAndReloadAsync(offerId, cancellationToken);
    }

    private async Task<OperationResult?> ValidateOfferShapeAsync(
        OfferKind kind,
        Guid? courseId,
        CancellationToken cancellationToken)
    {
        if (kind == OfferKind.Membership)
        {
            // A membership covers many courses, so naming one would be meaningless.
            return courseId is null
                ? null
                : OperationResult.Invalid(
                    ErrorCodes.CommerceRule,
                    "courseId",
                    "A membership offer covers every membership course and cannot name one.");
        }

        if (courseId is null)
        {
            return OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "courseId",
                "A lifetime offer must name the course it sells.");
        }

        bool exists = await context.Courses.AnyAsync(
            course => course.Id == courseId, cancellationToken);

        return exists
            ? null
            : OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "courseId",
                "That course does not exist.");
    }

    private static OperationResult? ValidatePriceFields(
        long amountMinor,
        string? currency,
        string? billingInterval,
        out BillingInterval interval)
    {
        interval = BillingInterval.OneTime;

        if (amountMinor <= 0)
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "amountMinor",
                "Enter an amount greater than zero, in minor units.");
        }

        string normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "currency",
                "Enter a three-letter ISO 4217 currency code.");
        }

        return Enum.TryParse(billingInterval, ignoreCase: true, out interval)
            ? null
            : OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "billingInterval",
                "Choose a valid billing interval.");
    }

    private static OperationResult? ValidateIntervalForKind(OfferKind kind, BillingInterval interval) =>
        (kind, interval) switch
        {
            (OfferKind.Membership, not BillingInterval.Month) => OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "billingInterval",
                "A membership is billed monthly."),
            (OfferKind.CourseLifetime, not BillingInterval.OneTime) => OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "billingInterval",
                "Lifetime access is charged once."),
            _ => null,
        };

    private static OperationResult? ValidateStatusChange(
        CommerceStatus current,
        string targetStatus,
        string reason,
        out CommerceStatus target)
    {
        target = CommerceStatus.Draft;

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Invalid(
                ErrorCodes.ValidationFailed,
                "reason",
                "A reason is required for every status change.");
        }

        if (!Enum.TryParse(targetStatus, ignoreCase: true, out target))
        {
            return OperationResult.Invalid(ErrorCodes.CommerceRule, "status", "Unknown status.");
        }

        return CommerceStatusGraph.CanTransition(current, target)
            ? null
            : OperationResult.Invalid(
                ErrorCodes.CommerceRule,
                "status",
                $"A {current} record cannot move to {target}.");
    }

    private static OperationResult InvalidRowVersion() => OperationResult.Invalid(
        ErrorCodes.InvalidRowVersion,
        "rowVersion",
        "The supplied version token is not valid. Reload the record and try again.");

    private static Dictionary<string, string> PriceMetadata(Guid offerId, Price price) =>
        new(StringComparer.Ordinal)
        {
            ["offerId"] = offerId.ToString("D"),
            ["amountMinor"] = price.AmountMinor.ToString(CultureInfo.InvariantCulture),
            ["currency"] = price.Currency,
            ["billingInterval"] = price.BillingInterval.ToString(),
        };

    private IQueryable<Offer> Query(bool tracked)
    {
        IQueryable<Offer> query = context.Offers
            .Include(offer => offer.Prices)
            .Include(offer => offer.Course);

        return tracked ? query : query.AsNoTracking();
    }

    private bool ApplyRowVersion<TEntity>(TEntity entity, string? token)
        where TEntity : class
    {
        if (!RowVersionToken.TryDecode(token, out byte[] bytes))
        {
            return false;
        }

        context.Entry(entity).Property(nameof(Offer.RowVersion)).OriginalValue = bytes;
        return true;
    }

    private async Task<OperationResult<AdminOffer>> SaveAndReloadAsync(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.ConcurrencyConflict().ToFailure<AdminOffer>();
        }

        context.ChangeTracker.Clear();

        Offer? reloaded = await Query(tracked: false)
            .FirstOrDefaultAsync(offer => offer.Id == offerId, cancellationToken);

        return reloaded is null
            ? OperationResult.NotFound().ToFailure<AdminOffer>()
            : OperationResult.FromValue(Project(reloaded));
    }

    private static AdminOffer Project(Offer offer) => new(
        offer.Id,
        offer.Code,
        offer.Name,
        offer.Description,
        offer.Kind.ToString(),
        offer.CourseId,
        offer.Course?.Title,
        offer.Status.ToString(),
        offer.StripeProductId is not null,
        CommerceStatusGraph.IsEditable(offer.Status),
        offer.CreatedAtUtc,
        offer.UpdatedAtUtc,
        offer.Prices
            .OrderByDescending(price => price.EffectiveFromUtc)
            .ThenBy(price => price.Id)
            .Select(price => new AdminPrice(
                price.Id,
                price.AmountMinor,
                price.Currency,
                price.BillingInterval.ToString(),
                price.BillingIntervalCount,
                price.Status.ToString(),
                price.EffectiveFromUtc,
                price.RetiredAtUtc,
                CommerceStatusGraph.IsEditable(price.Status),
                RowVersionToken.Encode(price.RowVersion)))
            .ToArray(),
        RowVersionToken.Encode(offer.RowVersion));
}
