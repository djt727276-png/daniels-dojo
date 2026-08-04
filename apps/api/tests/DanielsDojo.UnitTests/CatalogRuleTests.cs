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
