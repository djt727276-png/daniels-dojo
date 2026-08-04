namespace DanielsDojo.Application.Common;

/// <summary>
/// Accumulates field-level validation errors so one response can report every problem with a
/// form instead of making the author resubmit to discover the next one.
/// </summary>
/// <remarks>
/// Field names are the client-facing camelCase names, so the Angular form can attach each
/// message to the control that caused it.
/// </remarks>
public sealed class ValidationBuilder
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    /// <summary>Whether any error has been recorded.</summary>
    public bool HasErrors => errors.Count > 0;

    /// <summary>Records a message against a field.</summary>
    public ValidationBuilder Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out List<string>? messages))
        {
            messages = [];
            errors[field] = messages;
        }

        messages.Add(message);
        return this;
    }

    /// <summary>Requires non-blank text no longer than <paramref name="maxLength"/>.</summary>
    public ValidationBuilder Required(string field, string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Add(field, $"{label} is required.");
        }

        return value.Trim().Length > maxLength
            ? Add(field, $"{label} must be {maxLength} characters or fewer.")
            : this;
    }

    /// <summary>Allows blank text, but bounds it when present.</summary>
    public ValidationBuilder Optional(string field, string? value, int maxLength, string label)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            return Add(field, $"{label} must be {maxLength} characters or fewer.");
        }

        return this;
    }

    /// <summary>Records a message when <paramref name="condition"/> holds.</summary>
    public ValidationBuilder When(bool condition, string field, string message) =>
        condition ? Add(field, message) : this;

    /// <summary>Builds the failure result. Only call this when <see cref="HasErrors"/> is true.</summary>
    public OperationResult ToResult() => OperationResult.Invalid(
        ErrorCodes.ValidationFailed,
        errors.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal));
}
