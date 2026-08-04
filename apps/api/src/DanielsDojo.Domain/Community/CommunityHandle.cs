using System.Text.RegularExpressions;

namespace DanielsDojo.Domain.Community;

/// <summary>
/// Rules for a public community handle.
/// </summary>
/// <remarks>
/// Restricted to ASCII letters, digits, and single interior underscores or hyphens. A handle
/// is how one member identifies another, so characters that let two different handles look
/// identical — mixed scripts, combining marks, zero-width joiners — are refused rather than
/// normalised, because impersonation is the exact risk here.
/// </remarks>
public static partial class CommunityHandle
{
    /// <summary>Shortest acceptable handle.</summary>
    public const int MinLength = 3;

    /// <summary>Longest acceptable handle, matching the column width.</summary>
    public const int MaxLength = 32;

    /// <summary>Human-readable statement of the rule, safe to show a member.</summary>
    public const string Requirement =
        "Use 3 to 32 characters: letters, numbers, and single hyphens or underscores between them.";

    /// <summary>Whether the value is an acceptable handle.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length >= MinLength
        && value.Length <= MaxLength
        && Pattern().IsMatch(value);

    /// <summary>The upper-cased form used for uniqueness and lookup.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Trim().ToUpperInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>The community guidelines a member accepts during setup.</summary>
public static class CommunityGuidelines
{
    /// <summary>
    /// Version currently in force. Acceptance stores this exact string, so a later revision can
    /// be detected and re-acceptance requested rather than silently assumed.
    /// </summary>
    public const string CurrentVersion = "2026-08-01";
}
