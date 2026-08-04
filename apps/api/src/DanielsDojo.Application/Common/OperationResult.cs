namespace DanielsDojo.Application.Common;

/// <summary>Why an application operation was refused.</summary>
public enum OperationFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>The target does not exist, or the caller may not know that it does.</summary>
    NotFound,

    /// <summary>The request was well formed but broke a business rule.</summary>
    Validation,

    /// <summary>The caller's row version is stale.</summary>
    Concurrency,

    /// <summary>The request conflicts with existing state, such as a duplicate.</summary>
    Conflict,

    /// <summary>The caller is authenticated but not permitted.</summary>
    Forbidden,
}

/// <summary>
/// The outcome of an application operation.
/// </summary>
/// <remarks>
/// Application services return this rather than throwing for expected refusals, so an endpoint
/// maps a known failure to a status code without a stack unwind and without leaking database
/// detail. Genuine faults still throw.
/// </remarks>
public sealed record OperationResult
{
    private OperationResult()
    {
    }

    /// <summary>Why the operation was refused.</summary>
    public OperationFailure Failure { get; private init; }

    /// <summary>Stable machine-readable code, for example <c>platform.concurrency_conflict</c>.</summary>
    public string? Code { get; private init; }

    /// <summary>Human-readable message safe to return to a client.</summary>
    public string? Message { get; private init; }

    /// <summary>Field-level validation errors keyed by client field name.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    /// <summary>Whether the operation succeeded.</summary>
    public bool Succeeded => Failure == OperationFailure.None;

    /// <summary>A successful outcome.</summary>
    public static OperationResult Success() => new();

    /// <summary>The target was not found, or must appear not to exist.</summary>
    public static OperationResult NotFound() => new()
    {
        Failure = OperationFailure.NotFound,
    };

    /// <summary>A business-rule refusal with field-level detail.</summary>
    public static OperationResult Invalid(string code, string field, string message) => new()
    {
        Failure = OperationFailure.Validation,
        Code = code,
        Message = message,
        Errors = new Dictionary<string, string[]> { [field] = [message] },
    };

    /// <summary>A business-rule refusal spanning several fields.</summary>
    public static OperationResult Invalid(string code, IReadOnlyDictionary<string, string[]> errors) => new()
    {
        Failure = OperationFailure.Validation,
        Code = code,
        Message = "The request could not be accepted.",
        Errors = errors,
    };

    /// <summary>The caller's row version is stale.</summary>
    public static OperationResult ConcurrencyConflict() => new()
    {
        Failure = OperationFailure.Concurrency,
        Code = ErrorCodes.ConcurrencyConflict,
        Message =
            "This record changed after you loaded it. Reload to see the current values, then "
            + "reapply your change.",
    };

    /// <summary>The request conflicts with existing state.</summary>
    public static OperationResult Conflict(string code, string message) => new()
    {
        Failure = OperationFailure.Conflict,
        Code = code,
        Message = message,
    };

    /// <summary>The caller is authenticated but not permitted.</summary>
    public static OperationResult Forbidden(string code, string message) => new()
    {
        Failure = OperationFailure.Forbidden,
        Code = code,
        Message = message,
    };

    /// <summary>Wraps a successful value. The payload type is inferred.</summary>
    public static OperationResult<T> FromValue<T>(T value) => new()
    {
        Value = value,
        Outcome = Success(),
    };

    /// <summary>Carries this failure into a value-returning operation's result type.</summary>
    public OperationResult<T> ToFailure<T>() => new() { Outcome = this };
}

/// <summary>An operation outcome that carries a value when it succeeds.</summary>
/// <typeparam name="T">Payload type.</typeparam>
public sealed record OperationResult<T>
{
    /// <summary>The payload, when successful.</summary>
    public T? Value { get; init; }

    /// <summary>The failure outcome, when unsuccessful.</summary>
    public OperationResult Outcome { get; init; } = OperationResult.Success();

    /// <summary>Whether the operation succeeded.</summary>
    public bool Succeeded => Outcome.Succeeded;
}

/// <summary>Stable machine-readable error codes shared by API and client.</summary>
public static class ErrorCodes
{
    /// <summary>The caller's row version is stale.</summary>
    public const string ConcurrencyConflict = "platform.concurrency_conflict";

    /// <summary>A supplied row version was missing or not valid Base64.</summary>
    public const string InvalidRowVersion = "platform.invalid_row_version";

    /// <summary>A value broke a validation rule.</summary>
    public const string ValidationFailed = "platform.validation_failed";

    /// <summary>A status transition is not permitted.</summary>
    public const string InvalidTransition = "catalog.invalid_transition";

    /// <summary>Publication prerequisites are not met.</summary>
    public const string PublishPrerequisite = "catalog.publish_prerequisite";

    /// <summary>The slug is immutable after first publication.</summary>
    public const string SlugLocked = "catalog.slug_locked";

    /// <summary>A reorder payload did not match the exact sibling set.</summary>
    public const string ReorderMismatch = "catalog.reorder_mismatch";

    /// <summary>A unique value already exists.</summary>
    public const string DuplicateValue = "platform.duplicate_value";

    /// <summary>A price may not change after activation.</summary>
    public const string PriceImmutable = "commerce.price_immutable";

    /// <summary>An offer/price combination breaks a commerce rule.</summary>
    public const string CommerceRule = "commerce.rule_violation";

    /// <summary>The member has not completed community profile setup.</summary>
    public const string CommunitySetupRequired = "community.setup_required";

    /// <summary>The member may not participate in the community.</summary>
    public const string CommunityForbidden = "community.forbidden";

    /// <summary>A block prevents the interaction.</summary>
    public const string CommunityBlocked = "community.blocked";
}
