using System.Text.RegularExpressions;

namespace DanielsDojo.Domain.Catalog;

/// <summary>
/// Rules for catalog URL segments.
/// </summary>
/// <remarks>
/// Deliberately narrow: lowercase ASCII letters, digits, and single interior hyphens. A slug
/// becomes part of a public URL and of the identity of purchased content, so anything that
/// could normalize differently in a browser, a database collation, or a link — uppercase,
/// underscores, dots, percent-escapes, Unicode look-alikes — is rejected at the door rather
/// than sanitised into something the author did not type.
/// </remarks>
public static partial class CatalogSlug
{
    /// <summary>Shortest acceptable slug.</summary>
    public const int MinLength = 3;

    /// <summary>Longest acceptable slug, matching the column width.</summary>
    public const int MaxLength = 128;

    /// <summary>Whether the value is an acceptable slug.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length >= MinLength
        && value.Length <= MaxLength
        && Pattern().IsMatch(value);

    /// <summary>Human-readable statement of the rule, safe to show a client.</summary>
    public const string Requirement =
        "Use 3 to 128 characters: lowercase letters, numbers, and single hyphens between them.";

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
