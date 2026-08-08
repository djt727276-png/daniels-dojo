using DanielsDojo.Application.Common;
using DanielsDojo.Domain.Catalog;
using Xunit;

namespace DanielsDojo.UnitTests;

/// <summary>
/// Covers the catalog rules that have no database dependency, so a change to the status graph
/// or the slug rule fails here in milliseconds rather than in an endpoint suite.
/// </summary>
public sealed class PublicationStatusGraphTests
{
    [Theory]
    [InlineData(PublicationStatus.Draft, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Draft, PublicationStatus.Archived)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Draft)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Archived)]
    [InlineData(PublicationStatus.Archived, PublicationStatus.Draft)]
    public void AllowedTransitions_AreAccepted(PublicationStatus from, PublicationStatus to) =>
        Assert.True(PublicationStatusGraph.CanTransition(from, to));

    [Theory]
    [InlineData(PublicationStatus.Archived, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Draft, PublicationStatus.Draft)]
    [InlineData(PublicationStatus.Published, PublicationStatus.Published)]
    [InlineData(PublicationStatus.Archived, PublicationStatus.Archived)]
    public void OtherTransitions_AreRefused(PublicationStatus from, PublicationStatus to) =>
        Assert.False(PublicationStatusGraph.CanTransition(from, to));

    [Fact]
    public void ArchivedOffersOnlyDraft() =>
        Assert.Equal(
            [PublicationStatus.Draft],
            PublicationStatusGraph.AllowedTargets(PublicationStatus.Archived));
}

/// <summary>Slug rules, which decide what can appear in a public URL.</summary>
public sealed class CatalogSlugTests
{
    [Theory]
    [InlineData("atlas-enterprise-developer")]
    [InlineData("dotnet10")]
    [InlineData("abc")]
    public void AcceptsLowercaseKebabCase(string slug) => Assert.True(CatalogSlug.IsValid(slug));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("under_score")]
    [InlineData("dot.separated")]
    [InlineData("café")]
    public void RejectsAnythingElse(string? slug) => Assert.False(CatalogSlug.IsValid(slug));

    [Fact]
    public void RejectsAnOverlongSlug() =>
        Assert.False(CatalogSlug.IsValid(new string('a', CatalogSlug.MaxLength + 1)));
}

/// <summary>
/// Deriving a slug from a title, which is what happens when an author names a lesson and never
/// sees a URL segment at all.
/// </summary>
public sealed class CatalogSlugFromTitleTests
{
    [Theory]
    [InlineData("Introduction to C#", "introduction-to-csharp")]
    [InlineData("Getting Started with C++", "getting-started-with-cplusplus")]
    [InlineData("EF Core & Data Access", "ef-core-data-access")]
    [InlineData("  Trimmed   Spacing  ", "trimmed-spacing")]
    [InlineData("Café Culture", "cafe-culture")]
    [InlineData("Lesson #1: Setup", "lesson-1-setup")]
    [InlineData("dotnet 10", "dotnet-10")]
    [InlineData("Already-Kebab-Case", "already-kebab-case")]
    [InlineData("What's new?", "what-s-new")]
    public void DerivesTheExpectedSlug(string title, string expected) =>
        Assert.Equal(expected, CatalogSlug.FromTitle(title));

    [Theory]
    [InlineData("Introduction to C#")]
    [InlineData("!!!")]
    [InlineData("Go")]
    [InlineData("日本語")]
    [InlineData(null)]
    [InlineData("")]
    public void AlwaysProducesAValidSlug(string? title) =>
        Assert.True(CatalogSlug.IsValid(CatalogSlug.FromTitle(title)));

    [Theory]
    [InlineData("!!!")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FallsBackWhenNothingSurvives(string? title) =>
        Assert.Equal(CatalogSlug.Fallback, CatalogSlug.FromTitle(title));

    [Fact]
    public void ExtendsATitleTooShortToBeASlug() =>
        Assert.Equal($"go-{CatalogSlug.Fallback}", CatalogSlug.FromTitle("Go"));

    [Fact]
    public void TruncatesALongTitleWithoutEndingOnAHyphen()
    {
        string slug = CatalogSlug.FromTitle(string.Join(' ', Enumerable.Repeat("word", 60)));

        Assert.True(CatalogSlug.IsValid(slug));
        Assert.True(slug.Length <= CatalogSlug.MaxLength);
        Assert.DoesNotContain("--", slug, StringComparison.Ordinal);
    }
}

/// <summary>
/// Numbering a derived slug past its siblings. Two lessons may legitimately share a title, and
/// the second one still needs its own URL segment.
/// </summary>
public sealed class CatalogSlugUniquenessTests
{
    [Fact]
    public void KeepsTheCandidateWhenFree() =>
        Assert.Equal("intro", CatalogSlug.MakeUnique("intro", static _ => false));

    [Fact]
    public void NumbersPastTheTakenSlugs()
    {
        HashSet<string> taken = ["intro", "intro-2", "intro-3"];

        Assert.Equal("intro-4", CatalogSlug.MakeUnique("intro", taken.Contains));
    }

    [Fact]
    public void IsDeterministicForTheSameInput()
    {
        HashSet<string> taken = ["intro"];

        Assert.Equal(
            CatalogSlug.MakeUnique("intro", taken.Contains),
            CatalogSlug.MakeUnique("intro", taken.Contains));
    }

    [Fact]
    public void KeepsTheNumberedSlugWithinTheColumnWidth()
    {
        string candidate = new('a', CatalogSlug.MaxLength);
        string numbered = CatalogSlug.MakeUnique(candidate, taken => taken == candidate);

        Assert.True(numbered.Length <= CatalogSlug.MaxLength);
        Assert.True(CatalogSlug.IsValid(numbered));
        Assert.EndsWith("-2", numbered, StringComparison.Ordinal);
    }
}

/// <summary>
/// The opaque row-version token. A caller must round-trip exactly what it was given; anything
/// else is refused before a write is attempted.
/// </summary>
public sealed class RowVersionTokenTests
{
    [Fact]
    public void RoundTripsAnEightByteRowVersion()
    {
        byte[] original = [1, 2, 3, 4, 5, 6, 7, 8];

        Assert.True(RowVersionToken.TryDecode(RowVersionToken.Encode(original), out byte[] decoded));
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!!")]
    public void RefusesUnparsableInput(string? token) =>
        Assert.False(RowVersionToken.TryDecode(token, out _));

    [Fact]
    public void RefusesAWrongLengthToken() =>
        Assert.False(RowVersionToken.TryDecode(Convert.ToBase64String([1, 2, 3, 4]), out _));
}

/// <summary>Validation accumulation, which decides what a form is told at once.</summary>
public sealed class ValidationBuilderTests
{
    [Fact]
    public void ReportsEveryFieldInOneResult()
    {
        OperationResult result = new ValidationBuilder()
            .Required("title", "  ", 200, "Title")
            .Required("summary", new string('x', 600), 512, "Summary")
            .When(condition: true, "level", "Choose a valid level.")
            .ToResult();

        Assert.Equal(OperationFailure.Validation, result.Failure);
        Assert.Equal(3, result.Errors!.Count);
        Assert.Contains("title", result.Errors.Keys, StringComparer.Ordinal);
        Assert.Contains("summary", result.Errors.Keys, StringComparer.Ordinal);
        Assert.Contains("level", result.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void OptionalFieldsAcceptBlankButNotOverlongText()
    {
        var builder = new ValidationBuilder().Optional("summary", null, 10, "Summary");
        Assert.False(builder.HasErrors);

        builder.Optional("summary", "much too long to fit", 10, "Summary");
        Assert.True(builder.HasErrors);
    }
}
