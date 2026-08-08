using System.Globalization;
using System.Text;
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

    /// <summary>
    /// Slug used when a title carries no characters this scheme can represent — a title made
    /// only of punctuation, or written entirely in a script that does not transliterate.
    /// </summary>
    public const string Fallback = "lesson";

    /// <summary>
    /// Derives a valid slug from a human title.
    /// </summary>
    /// <remarks>
    /// Generation is deliberately separate from <see cref="IsValid"/>: a slug an author types
    /// is still rejected rather than repaired, because they meant something specific by it.
    /// This path exists for the opposite case — the author never typed a slug at all, so there
    /// is nothing to preserve and everything to derive.
    /// <para>
    /// Accented letters fold to their ASCII base ("Café" → "cafe"). The two suffixes that
    /// carry meaning in programming titles are spelled out rather than dropped, so
    /// "Introduction to C#" becomes "introduction-to-csharp" instead of the ambiguous
    /// "introduction-to-c". Everything else that is not a lowercase letter or digit becomes a
    /// separator, runs of separators collapse, and the result is trimmed to the column width
    /// without ending mid-hyphen.
    /// </para>
    /// The result always satisfies <see cref="IsValid"/>.
    /// </remarks>
    public static string FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Fallback;
        }

        string expanded = ExpandProgrammingSuffixes(title);
        string decomposed = expanded.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char character in decomposed)
        {
            // Drop the combining marks FormD split out, keeping the ASCII base letter.
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(character);

            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string candidate = builder.ToString().Trim('-');

        if (candidate.Length > MaxLength)
        {
            candidate = candidate[..MaxLength].Trim('-');
        }

        if (candidate.Length == 0)
        {
            return Fallback;
        }

        // A one- or two-character title ("Go") is legal input but too short to be a slug, so it
        // is extended into the shortest acceptable form rather than refused.
        return candidate.Length < MinLength ? $"{candidate}-{Fallback}" : candidate;
    }

    /// <summary>
    /// Returns <paramref name="candidate"/> when free, or the first numbered variant that is —
    /// "intro", then "intro-2", "intro-3", and so on.
    /// </summary>
    /// <param name="candidate">A slug already known to be valid.</param>
    /// <param name="isTaken">Whether a slug is already used by a sibling.</param>
    /// <remarks>
    /// The suffix is appended within <see cref="MaxLength"/> by trimming the stem, so a very
    /// long title can still produce a distinct slug rather than colliding forever. The scan is
    /// bounded: after enough attempts the caller is better served by an error than a loop, and
    /// the guard makes that impossible to reach in practice.
    /// </remarks>
    public static string MakeUnique(string candidate, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!isTaken(candidate))
        {
            return candidate;
        }

        for (int attempt = 2; attempt <= 1000; attempt++)
        {
            string suffix = $"-{attempt.ToString(CultureInfo.InvariantCulture)}";
            string stem = candidate.Length + suffix.Length > MaxLength
                ? candidate[..(MaxLength - suffix.Length)].TrimEnd('-')
                : candidate;

            string numbered = stem + suffix;

            if (!isTaken(numbered))
            {
                return numbered;
            }
        }

        // Unreachable for any realistic section: a thousand lessons would have to share one
        // title. Returning the candidate lets the caller's own duplicate check report it.
        return candidate;
    }

    /// <summary>
    /// Spells out the language suffixes that would otherwise vanish from a title.
    /// </summary>
    /// <remarks>
    /// Both patterns require a letter immediately before the symbol, so "C#" and "C++" expand
    /// while an ordinary "Lesson #1" or "A + B" keeps its punctuation as a separator.
    /// </remarks>
    private static string ExpandProgrammingSuffixes(string title)
    {
        string expanded = SharpSuffix().Replace(title, "${language}sharp");

        return PlusPlusSuffix().Replace(expanded, "${language}plusplus");
    }

    [GeneratedRegex("(?<language>[A-Za-z])#", RegexOptions.CultureInvariant)]
    private static partial Regex SharpSuffix();

    [GeneratedRegex(@"(?<language>[A-Za-z])\+\+", RegexOptions.CultureInvariant)]
    private static partial Regex PlusPlusSuffix();
}
