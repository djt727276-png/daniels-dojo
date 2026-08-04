using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Proves community write limits are partitioned by the local application user, not by
/// anything a client controls.
/// </summary>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class RateLimitTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string ReportsUrl = "/api/v1/community/reports";

    /// <summary>Matches the configured report limit.</summary>
    private const int ReportLimit = 5;

    private ApiHarness _harness = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();
        _harness = ApiHarness.Create(fixture);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ExceedingTheReportLimit_Returns429WithAStableCode()
    {
        TestActor reporter = await MemberAsync("limit-reporter");
        Guid[] targets = await SeedReportableThreadsAsync(ReportLimit + 1);

        using HttpClient client = _harness.CreateClient(reporter);

        for (int index = 0; index < ReportLimit; index++)
        {
            using HttpResponseMessage accepted =
                await client.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[index]));

            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        using HttpResponseMessage limited =
            await client.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[ReportLimit]));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal("platform.rate_limited", problem.ProblemCode());
    }

    [Fact]
    public async Task SpoofableHeaders_DoNotCreateANewBucket()
    {
        TestActor reporter = await MemberAsync("header-spoofer");
        Guid[] targets = await SeedReportableThreadsAsync(ReportLimit + 1);

        using HttpClient client = _harness.CreateClient(reporter);

        for (int index = 0; index < ReportLimit; index++)
        {
            await client.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[index]));
        }

        // A fresh forwarded-for address is exactly the trick a partitioning-by-IP scheme would
        // fall for. The bucket is keyed on the local user, so it changes nothing.
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(ReportsUrl, UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                Report(targets[ReportLimit]), options: ApiHarness.Json),
        };

        request.Headers.Add("X-Forwarded-For", "203.0.113.42");
        request.Headers.Add("X-Real-IP", "203.0.113.42");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task AnotherMemberIsUnaffectedByTheFirstMembersLimit()
    {
        TestActor first = await MemberAsync("busy-member");
        TestActor second = await MemberAsync("quiet-member");
        Guid[] targets = await SeedReportableThreadsAsync(ReportLimit + 1);

        using HttpClient busy = _harness.CreateClient(first);

        for (int index = 0; index < ReportLimit; index++)
        {
            await busy.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[index]));
        }

        using HttpResponseMessage exhausted =
            await busy.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[ReportLimit]));
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);

        using HttpClient quiet = _harness.CreateClient(second);
        using HttpResponseMessage allowed =
            await quiet.SendJsonAsync(HttpMethod.Post, ReportsUrl, Report(targets[0]));

        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private static object Report(Guid threadId) => new
    {
        TargetType = "Thread",
        TargetId = threadId,
        ReasonCode = "Spam",
        Detail = (string?)null,
    };

    private async Task<TestActor> MemberAsync(string handle)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        return actor;
    }

    /// <summary>Creates threads directly so the report limit is reached without hitting the write limit.</summary>
    private async Task<Guid[]> SeedReportableThreadsAsync(int count)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        Guid categoryId = await context.ForumCategories
            .Where(category => category.Slug == "general")
            .Select(category => category.Id)
            .SingleAsync();

        Guid authorId = await context.Users.Select(user => user.Id).FirstAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<Guid> ids = [];

        for (int index = 0; index < count; index++)
        {
            var thread = new ForumThread
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                AuthorUserId = authorId,
                Title = $"Reportable thread {index}",
                Status = ForumThreadStatus.Open,
                LastActivityAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            context.ForumThreads.Add(thread);
            ids.Add(thread.Id);
        }

        await context.SaveChangesAsync();

        return [.. ids];
    }

    private async Task SeedCategoryAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        if (await context.ForumCategories.AnyAsync(category => category.Slug == "general"))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        context.ForumCategories.Add(new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = "general",
            Name = "General",
            Description = "Anything about the platform.",
            SortOrder = 0,
            Status = ForumCategoryStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
    }
}
