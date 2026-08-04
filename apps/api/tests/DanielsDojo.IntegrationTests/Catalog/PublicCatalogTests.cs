using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Commerce;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Authentication;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Catalog;

/// <summary>
/// Proves the public catalog exposes Published data only, pages deterministically, and resolves
/// prices from the database rather than any hard-coded amount.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class PublicCatalogTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private StagingApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        // No reference seed: the reference profile installs its own active membership price,
        // which would mask the pricing assertions below. This suite provides all its own data.
        await fixture.ResetWithoutSeedAsync();
        _factory = new StagingApiFactory(fixture.ConnectionString);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory?.Dispose();

    // ------------------------------------------------------------------ listing

    [Fact]
    public async Task CourseList_ReturnsOnlyPublishedCourses()
    {
        await SeedCatalogAsync();

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses");
        string[] slugs = ReadSlugs(body);

        Assert.Contains("published-course", slugs);
        Assert.DoesNotContain("draft-course", slugs);
        Assert.DoesNotContain("archived-course", slugs);
    }

    [Fact]
    public async Task CourseList_PagesDeterministically()
    {
        await SeedManyCoursesAsync(count: 7);

        JsonDocument first = await GetJsonAsync("/api/v1/catalog/courses?page=1&pageSize=3");
        JsonDocument second = await GetJsonAsync("/api/v1/catalog/courses?page=2&pageSize=3");
        JsonDocument firstAgain = await GetJsonAsync("/api/v1/catalog/courses?page=1&pageSize=3");

        string[] pageOne = ReadSlugs(first);
        string[] pageTwo = ReadSlugs(second);

        Assert.Equal(3, pageOne.Length);
        Assert.Equal(3, pageTwo.Length);

        // Stable ordering: the same page returns the same rows, and pages never overlap.
        Assert.Equal(pageOne, ReadSlugs(firstAgain));
        Assert.Empty(pageOne.Intersect(pageTwo));

        Assert.Equal(7, first.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, first.RootElement.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task CourseList_ClampsPageSizeToTheMaximum()
    {
        await SeedCatalogAsync();

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses?pageSize=5000");

        Assert.Equal(48, body.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task CourseList_FiltersBySearchLevelAndTag()
    {
        await SeedCatalogAsync();

        Assert.Contains("published-course", ReadSlugs(
            await GetJsonAsync("/api/v1/catalog/courses?search=Published")));
        Assert.Empty(ReadSlugs(
            await GetJsonAsync("/api/v1/catalog/courses?search=NoSuchCourseAnywhere")));

        Assert.Contains("published-course", ReadSlugs(
            await GetJsonAsync("/api/v1/catalog/courses?level=AllLevels")));
        Assert.Empty(ReadSlugs(await GetJsonAsync("/api/v1/catalog/courses?level=Beginner")));

        // An unrecognised level matches nothing rather than being ignored.
        Assert.Empty(ReadSlugs(await GetJsonAsync("/api/v1/catalog/courses?level=NotALevel")));

        Assert.Contains("published-course", ReadSlugs(
            await GetJsonAsync("/api/v1/catalog/courses?tag=dotnet")));
        Assert.Empty(ReadSlugs(await GetJsonAsync("/api/v1/catalog/courses?tag=missing")));
    }

    // ------------------------------------------------------------------ pricing

    [Fact]
    public async Task CourseCard_CarriesDatabasePricesInMinorUnits()
    {
        await SeedCatalogAsync();

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses");
        JsonElement course = body.RootElement.GetProperty("items")[0];

        JsonElement membership = course.GetProperty("membershipPrice");
        Assert.Equal(999, membership.GetProperty("amountMinor").GetInt64());
        Assert.Equal("USD", membership.GetProperty("currency").GetString());
        Assert.Equal("Month", membership.GetProperty("interval").GetString());

        JsonElement lifetime = course.GetProperty("lifetimePrice");
        Assert.Equal(1999, lifetime.GetProperty("amountMinor").GetInt64());
        Assert.Equal("OneTime", lifetime.GetProperty("interval").GetString());
    }

    [Fact]
    public async Task RetiredPrice_IsNotShown()
    {
        await SeedCatalogAsync();

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            Price membership = await context.Prices.SingleAsync(
                price => price.OfferId == SeedOffers.MembershipOfferId);
            membership.Status = CommerceStatus.Retired;
            membership.RetiredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses");
        JsonElement course = body.RootElement.GetProperty("items")[0];

        Assert.Equal(JsonValueKind.Null, course.GetProperty("membershipPrice").ValueKind);
    }

    [Fact]
    public async Task FuturePrice_IsNotShownBeforeItsEffectiveTime()
    {
        await SeedCatalogAsync();

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            Price membership = await context.Prices.SingleAsync(
                price => price.OfferId == SeedOffers.MembershipOfferId);
            membership.EffectiveFromUtc = DateTimeOffset.UtcNow.AddDays(30);
            await context.SaveChangesAsync();
        }

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses");
        Assert.Equal(
            JsonValueKind.Null,
            body.RootElement.GetProperty("items")[0].GetProperty("membershipPrice").ValueKind);
    }

    // ------------------------------------------------------------------ detail

    [Fact]
    public async Task CourseDetail_ReturnsOnlyPublishedOutlineAndNoSensitiveFields()
    {
        await SeedCatalogAsync();

        JsonDocument body = await GetJsonAsync("/api/v1/catalog/courses/published-course");
        string raw = body.RootElement.GetRawText();

        JsonElement sections = body.RootElement.GetProperty("sections");
        Assert.Equal(1, sections.GetArrayLength());

        string[] lessonSlugs = [.. sections[0].GetProperty("lessons").EnumerateArray()
            .Select(lesson => lesson.GetProperty("slug").GetString()!)];

        Assert.Contains("preview-lesson", lessonSlugs);
        Assert.Contains("members-lesson", lessonSlugs);
        Assert.DoesNotContain("draft-lesson", lessonSlugs);

        // Nothing internal is projected: no body, row version, storage key, or provider ID.
        foreach (string forbidden in new[]
                 { "rowVersion", "bodyMarkdown", "imageStorageKey", "muxAssetId", "createdAtUtc" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Members only body", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonPublicCourse_And_MissingCourse_AreIndistinguishable404s()
    {
        await SeedCatalogAsync();

        using HttpClient client = _factory.CreateClient();

        // Indistinguishable means the responses are byte-identical apart from nothing at all:
        // a draft, an archived course, and a slug that was never used must look the same, or
        // the difference becomes a way to enumerate unreleased content.
        List<(HttpStatusCode Status, string Body)> responses = [];

        foreach (string slug in new[] { "draft-course", "archived-course", "no-such-course" })
        {
            using HttpResponseMessage response =
                await client.GetAsync($"/api/v1/catalog/courses/{slug}");

            responses.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.All(responses, entry => Assert.Equal(HttpStatusCode.NotFound, entry.Status));

        // Compared structurally: ProblemDetails carries a per-request traceId, which is a
        // correlation value rather than anything about the resource, so it is excluded.
        string[] shapes = [.. responses.Select(entry => Describe(entry.Body))];
        Assert.Single(shapes.Distinct());

        // And nothing in the payload reveals whether the course exists or what state it is in.
        foreach (string forbidden in new[] { "draft", "archived", "Draft", "Archived", "slug" })
        {
            Assert.DoesNotContain(forbidden, responses[0].Body, StringComparison.Ordinal);
        }
    }

    /// <summary>Renders a ProblemDetails body without its per-request correlation fields.</summary>
    private static string Describe(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        return string.Join('|', document.RootElement.EnumerateObject()
            .Where(property => property.Name is not ("traceId" or "instance"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value}"));
    }

    // ------------------------------------------------------------------ preview

    [Fact]
    public async Task Preview_ReturnsBodyForAPublishedPreviewArticle()
    {
        await SeedCatalogAsync();

        JsonDocument body = await GetJsonAsync(
            "/api/v1/catalog/courses/published-course/lessons/preview-lesson/preview");

        Assert.Equal("Preview body text.", body.RootElement.GetProperty("body").GetString());
        Assert.Equal("published-course", body.RootElement.GetProperty("courseSlug").GetString());
    }

    [Theory]
    [InlineData("members-lesson")]   // published article, but not a preview
    [InlineData("draft-lesson")]     // preview article, but still a draft
    [InlineData("video-lesson")]     // preview, published, but a video
    [InlineData("no-such-lesson")]
    public async Task Preview_IsRefusedForAnythingElse(string lessonSlug)
    {
        await SeedCatalogAsync();

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/catalog/courses/published-course/lessons/{lessonSlug}/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_IsRefusedWhenTheCourseIsNotPublished()
    {
        await SeedCatalogAsync();

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/catalog/courses/draft-course/lessons/preview-lesson/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_RequireNoAuthentication()
    {
        await SeedCatalogAsync();

        using HttpClient client = _factory.CreateClient();

        foreach (string path in new[]
                 {
                     "/api/v1/catalog/courses",
                     "/api/v1/catalog/courses/published-course",
                     "/api/v1/catalog/courses/published-course/lessons/preview-lesson/preview",
                 })
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] ReadSlugs(JsonDocument document) =>
        [.. document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("slug").GetString()!)];

    private async Task SeedCatalogAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        await SeedOffers.CreateAsync(context);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        Course published = CatalogFactory.Course("published-course", "Published Course",
            PublicationStatus.Published, includedInMembership: true, now);
        Course draft = CatalogFactory.Course("draft-course", "Draft Course",
            PublicationStatus.Draft, includedInMembership: true, now);
        Course archived = CatalogFactory.Course("archived-course", "Archived Course",
            PublicationStatus.Archived, includedInMembership: true, now);

        context.Courses.AddRange(published, draft, archived);

        CourseSection publishedSection =
            CatalogFactory.Section(published.Id, "Published Section", 1, PublicationStatus.Published, now);
        CourseSection draftSection =
            CatalogFactory.Section(published.Id, "Draft Section", 2, PublicationStatus.Draft, now);
        CourseSection draftCourseSection =
            CatalogFactory.Section(draft.Id, "Section", 1, PublicationStatus.Published, now);

        context.CourseSections.AddRange(publishedSection, draftSection, draftCourseSection);

        context.Lessons.AddRange(
            CatalogFactory.Lesson(published.Id, publishedSection.Id, "preview-lesson", 1,
                PublicationStatus.Published, LessonType.Article, isPreview: true,
                body: "Preview body text.", now),
            CatalogFactory.Lesson(published.Id, publishedSection.Id, "members-lesson", 2,
                PublicationStatus.Published, LessonType.Article, isPreview: false,
                body: "Members only body", now),
            CatalogFactory.Lesson(published.Id, publishedSection.Id, "draft-lesson", 3,
                PublicationStatus.Draft, LessonType.Article, isPreview: true,
                body: "Draft body", now),
            CatalogFactory.Lesson(published.Id, publishedSection.Id, "video-lesson", 4,
                PublicationStatus.Published, LessonType.Video, isPreview: true, body: null, now),
            CatalogFactory.Lesson(draft.Id, draftCourseSection.Id, "preview-lesson", 1,
                PublicationStatus.Published, LessonType.Article, isPreview: true,
                body: "Hidden", now));

        Tag tag = new()
        {
            Id = Guid.NewGuid(),
            Name = "dotnet",
            NormalizedName = "DOTNET",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        context.Tags.Add(tag);
        context.CourseTags.Add(new CourseTag { CourseId = published.Id, TagId = tag.Id });

        await SeedOffers.AddLifetimeOfferAsync(context, published.Id, now);

        await context.SaveChangesAsync();
    }

    private async Task SeedManyCoursesAsync(int count)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int index = 0; index < count; index++)
        {
            context.Courses.Add(CatalogFactory.Course(
                $"course-{index:D2}",
                $"Course {index:D2}",
                PublicationStatus.Published,
                includedInMembership: false,
                now.AddMinutes(-index)));
        }

        await context.SaveChangesAsync();
    }
}
