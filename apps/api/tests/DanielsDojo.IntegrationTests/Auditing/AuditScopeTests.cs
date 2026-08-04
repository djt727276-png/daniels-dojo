using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Auditing;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Auditing;

/// <summary>
/// Pins the scope, atomicity, and privacy of the global Admin audit table.
/// </summary>
/// <remarks>
/// <para>
/// <c>audit.AuditLogs</c> is a privileged-action record, not an activity feed. Routine member
/// activity — posting, reacting, friending, messaging, editing your own profile — has its own
/// history in domain rows, timestamps, statuses, and tombstones. Letting it write here would
/// bury the handful of decisions a reviewer actually needs to find, and would drag member
/// content into a table that is meant to hold identifiers only.
/// </para>
/// <para>
/// A member's own report submission is likewise recorded by the <c>Report</c> row itself. What
/// gets audited is the moderator's later disposition of it.
/// </para>
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AuditScopeTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Community = "/api/v1/community";
    private const string Catalog = "/api/v1/admin/catalog";
    private const string Moderation = "/api/v1/admin/community";

    private ApiHarness _harness = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await SeedCategoryAsync();
        _harness = ApiHarness.Create(fixture);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ---------------------------------------------------------------- scope

    [Fact]
    public async Task RoutineMemberActivity_WritesNoGlobalAuditRows()
    {
        TestActor first = await MemberAsync("audit-scope-one", discoverable: true, openMessages: true);
        TestActor second = await MemberAsync(
            "audit-scope-two", discoverable: true, openToFriends: true, openMessages: true);

        using HttpClient one = _harness.CreateClient(first);
        using HttpClient two = _harness.CreateClient(second);

        // Forum: thread, reply, edit, reaction, subscription, self-removal.
        using JsonDocument thread = await one.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new
            {
                CategorySlug = "general",
                Title = "Routine activity",
                Body = "An ordinary opening post.",
            },
            HttpStatusCode.OK);

        Guid threadId = thread.RootElement.GetProperty("id").GetGuid();
        Guid firstPostId = thread.RootElement
            .GetProperty("posts").GetProperty("items")[0].GetProperty("id").GetGuid();
        string firstPostVersion = thread.RootElement
            .GetProperty("posts").GetProperty("items")[0].GetProperty("rowVersion").GetString()!;

        using JsonDocument replied = await two.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads/{threadId}/posts",
            new { Body = "An ordinary reply.", ReplyToPostId = (Guid?)null },
            HttpStatusCode.OK);

        Guid replyId = replied.RootElement
            .GetProperty("posts").GetProperty("items")[1].GetProperty("id").GetGuid();
        string replyVersion = replied.RootElement
            .GetProperty("posts").GetProperty("items")[1].GetProperty("rowVersion").GetString()!;

        await two.SendJsonAsync(
            HttpMethod.Put,
            $"{Community}/posts/{replyId}",
            new { Body = "An ordinary edit.", RowVersion = replyVersion },
            HttpStatusCode.OK);

        await two.SendJsonAsync(
            HttpMethod.Put, $"{Community}/posts/{firstPostId}/reaction", new { Liked = true }, HttpStatusCode.OK);
        await two.SendJsonAsync(
            HttpMethod.Put, $"{Community}/threads/{threadId}/subscription", new { Subscribed = true }, HttpStatusCode.OK);
        await two.SendJsonAsync(
            HttpMethod.Delete, $"{Community}/posts/{replyId}", payload: null, HttpStatusCode.OK);

        // Social: friend request, acceptance, conversation, message, delete, read state.
        await Expect(one, HttpMethod.Post, $"{Community}/friend-requests", new { Handle = "audit-scope-two" }, HttpStatusCode.NoContent);

        Guid requestId = await PendingRequestIdAsync();
        await Expect(two, HttpMethod.Post, $"{Community}/friend-requests/{requestId}/accept", null, HttpStatusCode.NoContent);

        using JsonDocument conversation = await one.SendJsonAsync(
            HttpMethod.Post, $"{Community}/conversations", new { Handle = "audit-scope-two" }, HttpStatusCode.OK);

        Guid conversationId = conversation.RootElement.GetProperty("id").GetGuid();

        using JsonDocument sent = await one.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/conversations/{conversationId}/messages",
            new { Body = "An ordinary private message." },
            HttpStatusCode.OK);

        Guid messageId = sent.RootElement
            .GetProperty("messages").GetProperty("items")[0].GetProperty("id").GetGuid();

        using JsonDocument opened = await two.GetJsonAsync($"{Community}/conversations/{conversationId}");
        Assert.Single(opened.RootElement.GetProperty("messages").GetProperty("items").EnumerateArray());

        await one.SendJsonAsync(
            HttpMethod.Delete, $"{Community}/messages/{messageId}", payload: null, HttpStatusCode.OK);

        // Notifications, profile settings, report submission, block and unblock.
        await Expect(two, HttpMethod.Put, "/api/v1/me/notifications/read", new { NotificationId = (Guid?)null }, HttpStatusCode.NoContent);
        await UpdatePrivacyAsync(two, discoverable: false, openToFriends: false, openMessages: false);
        await Expect(
            two,
            HttpMethod.Post,
            $"{Community}/reports",
            new { TargetType = "Post", TargetId = firstPostId, ReasonCode = "Spam", Detail = (string?)null },
            HttpStatusCode.Accepted);
        await Expect(one, HttpMethod.Post, $"{Community}/blocks", new { Handle = "audit-scope-two", ReasonCategory = "Personal" }, HttpStatusCode.NoContent);
        await Expect(one, HttpMethod.Delete, $"{Community}/blocks/{second.UserId}", null, HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        List<AuditLog> entries = await context.AuditLogs.ToListAsync();

        Assert.Empty(entries);

        // The history still exists — it just lives in the domain rows where it belongs.
        Assert.True(await context.ForumPosts.AnyAsync(post => post.Status == ForumPostStatus.Removed));
        Assert.True(await context.DirectMessages.AnyAsync(message => message.Status == DirectMessageStatus.Deleted));
        Assert.Equal(1, await context.Reports.CountAsync());
        Assert.Equal(1, await context.ForumPostReactions.CountAsync());
    }

    [Fact]
    public async Task PrivilegedAdminWrites_EachRecordOneAuditRow()
    {
        TestActor admin = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(admin);

        // Catalog authoring.
        using JsonDocument course = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses",
            new
            {
                Slug = "audited-catalog-course",
                Title = "Audited course",
                Summary = "A summary long enough to be useful.",
                Description = "A description that explains the course.",
                Level = "AllLevels",
                IncludedInMembership = true,
            },
            HttpStatusCode.Created);

        Guid courseId = course.RootElement.GetProperty("id").GetGuid();

        using JsonDocument archived = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses/{courseId}/status/Archived",
            new { Reason = "Withdrawn during the audit.", RowVersion = course.RootElement.GetProperty("rowVersion").GetString() },
            HttpStatusCode.OK);

        Assert.Equal("Archived", archived.RootElement.GetProperty("status").GetString());

        // Pricing.
        using JsonDocument offer = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/admin/pricing/offers",
            new
            {
                Code = "audited-membership",
                Name = "Audited membership",
                Description = "All access.",
                Kind = "Membership",
                CourseId = (Guid?)null,
            },
            HttpStatusCode.Created);

        // Moderation.
        TestActor member = await MemberAsync("audited-member");
        using HttpClient memberClient = _harness.CreateClient(member);

        using JsonDocument thread = await memberClient.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "general", Title = "Moderate me", Body = "Body text." },
            HttpStatusCode.OK);

        Guid postId = thread.RootElement
            .GetProperty("posts").GetProperty("items")[0].GetProperty("id").GetGuid();

        await Expect(
            client,
            HttpMethod.Post,
            $"{Moderation}/posts/{postId}/remove",
            new { Reason = "Removed during the audit." },
            HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        List<AuditLog> entries = await context.AuditLogs.OrderBy(entry => entry.OccurredAtUtc).ToListAsync();

        string[] actions = entries.Select(entry => entry.Action).ToArray();

        // "Identity.AdminRoleGranted" is the Phase 3 grant the harness used to make this
        // caller an Admin. It belongs here: an out-of-band privilege change is exactly the
        // kind of action the global table exists for.
        Assert.Equal(
            [
                "Catalog.Course.Created",
                "Catalog.Course.StatusChanged",
                "Commerce.Offer.Created",
                "Community.Post.RemovedByModerator",
                "Identity.AdminRoleGranted",
            ],
            actions.Order(StringComparer.Ordinal).ToArray());

        Assert.All(entries.Where(entry => entry.Action != "Identity.AdminRoleGranted"), entry =>
        {
            Assert.Equal(admin.UserId, entry.ActorUserId);
            Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
            Assert.NotEqual(default, entry.OccurredAtUtc);
        });

        // Reasons are mandatory exactly where a decision needs justifying.
        Assert.Equal(
            "Withdrawn during the audit.",
            Assert.Single(entries, entry => entry.Action == "Catalog.Course.StatusChanged").Reason);
        Assert.Equal(
            "Removed during the audit.",
            Assert.Single(entries, entry => entry.Action == "Community.Post.RemovedByModerator").Reason);

        Assert.Equal(
            offer.RootElement.GetProperty("id").GetGuid().ToString("D"),
            Assert.Single(entries, entry => entry.Action == "Commerce.Offer.Created").TargetId);
    }

    // ---------------------------------------------------------------- atomicity

    [Fact]
    public async Task AStaleAdminWrite_LeavesNeitherTheChangeNorAnAuditRow()
    {
        TestActor admin = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(admin);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses",
            new
            {
                Slug = "stale-audit-course",
                Title = "Original title",
                Summary = "A summary long enough to be useful.",
                Description = "A description that explains the course.",
                Level = "AllLevels",
                IncludedInMembership = true,
            },
            HttpStatusCode.Created);

        Guid courseId = created.RootElement.GetProperty("id").GetGuid();
        string staleVersion = created.RootElement.GetProperty("rowVersion").GetString()!;

        // First write wins and invalidates the token the second writer still holds.
        await client.SendJsonAsync(
            HttpMethod.Put, $"{Catalog}/courses/{courseId}", Update("First edit", staleVersion), HttpStatusCode.OK);

        int auditsBefore = await CountAuditsAsync(courseId);

        using JsonDocument problem = await client.SendJsonAsync(
            HttpMethod.Put, $"{Catalog}/courses/{courseId}", Update("Second edit", staleVersion), HttpStatusCode.Conflict);

        Assert.Equal("platform.concurrency_conflict", problem.ProblemCode());

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Course stored = await context.Courses.SingleAsync(course => course.Id == courseId);

        // Neither half of the losing write survived.
        Assert.Equal("First edit", stored.Title);
        Assert.Equal(auditsBefore, await CountAuditsAsync(courseId));
    }

    [Fact]
    public async Task AnAdminWriteAndItsAuditRowShareOneTransaction()
    {
        TestActor admin = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(admin);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses",
            new
            {
                Slug = "atomic-audit-course",
                Title = "Atomic course",
                Summary = "A summary long enough to be useful.",
                Description = "A description that explains the course.",
                Level = "AllLevels",
                IncludedInMembership = true,
            },
            HttpStatusCode.Created);

        Guid courseId = created.RootElement.GetProperty("id").GetGuid();

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Course stored = await context.Courses.SingleAsync(course => course.Id == courseId);
        AuditLog entry = await context.AuditLogs.SingleAsync(
            log => log.TargetId == courseId.ToString("D") && log.Action == "Catalog.Course.Created");

        // Written by the same SaveChanges: the audit row cannot pre-date the row it describes,
        // and neither can exist without the other.
        Assert.True(entry.OccurredAtUtc >= stored.CreatedAtUtc.AddSeconds(-1));
        Assert.True(entry.OccurredAtUtc <= stored.CreatedAtUtc.AddSeconds(5));
    }

    // ---------------------------------------------------------------- privacy

    [Fact]
    public async Task AuditMetadataCarriesIdentifiersOnly()
    {
        TestActor admin = await _harness.SignInAsync(admin: true);
        using HttpClient client = _harness.CreateClient(admin);

        const string SecretBody = "SENSITIVE-BODY-TEXT-THAT-MUST-NOT-BE-AUDITED";

        using JsonDocument course = await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses",
            new
            {
                Slug = "metadata-privacy-course",
                Title = "Metadata privacy",
                Summary = SecretBody,
                Description = SecretBody,
                Level = "AllLevels",
                IncludedInMembership = true,
            },
            HttpStatusCode.Created);

        Guid courseId = course.RootElement.GetProperty("id").GetGuid();
        Guid sectionId = FirstSectionId(await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses/{courseId}/sections",
            new { Title = "Section", Description = SecretBody },
            HttpStatusCode.OK));

        await client.SendJsonAsync(
            HttpMethod.Post,
            $"{Catalog}/courses/{courseId}/sections/{sectionId}/lessons",
            new
            {
                Slug = "private-lesson",
                Title = "Private lesson",
                Summary = SecretBody,
                LessonType = "Article",
                BodyMarkdown = SecretBody,
                IsPreview = false,
                EstimatedDurationSeconds = (int?)null,
            },
            HttpStatusCode.OK);

        // A member posts a body and is moderated, which is the other route content could leak by.
        TestActor member = await MemberAsync("metadata-privacy-member");
        using HttpClient memberClient = _harness.CreateClient(member);

        using JsonDocument thread = await memberClient.SendJsonAsync(
            HttpMethod.Post,
            $"{Community}/threads",
            new { CategorySlug = "general", Title = "Private thread", Body = SecretBody },
            HttpStatusCode.OK);

        Guid postId = thread.RootElement
            .GetProperty("posts").GetProperty("items")[0].GetProperty("id").GetGuid();

        await Expect(
            client,
            HttpMethod.Post,
            $"{Moderation}/posts/{postId}/remove",
            new { Reason = "Removed during the metadata audit." },
            HttpStatusCode.NoContent);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        List<AuditLog> entries = await context.AuditLogs.ToListAsync();

        Assert.NotEmpty(entries);

        string serialised = string.Join(
            '\n',
            entries.Select(entry => $"{entry.Action}|{entry.Reason}|{entry.MetadataJson}"));

        Assert.DoesNotContain(SecretBody, serialised, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", serialised, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", serialised, StringComparison.Ordinal);

        // No member email or external identity value reaches the trail. The Phase 3 grant row
        // does carry an operator context of the form "account@machine", which is an operator
        // identity for an out-of-band CLI action rather than a customer email address.
        foreach (string email in await context.Users.Select(user => user.Email).ToListAsync())
        {
            Assert.DoesNotContain(email, serialised, StringComparison.OrdinalIgnoreCase);
        }

        foreach (string subject in await context.Users.Select(user => user.ExternalSubjectId).ToListAsync())
        {
            Assert.DoesNotContain(subject, serialised, StringComparison.OrdinalIgnoreCase);
        }

        // Every metadata payload is a flat map of short scalar values.
        foreach (AuditLog entry in entries.Where(entry => entry.MetadataJson is not null))
        {
            using JsonDocument metadata = JsonDocument.Parse(entry.MetadataJson!);

            Assert.Equal(JsonValueKind.Object, metadata.RootElement.ValueKind);
            Assert.All(metadata.RootElement.EnumerateObject(), property =>
            {
                Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
                Assert.True(property.Value.GetString()!.Length <= 256);
            });
        }
    }

    // ---------------------------------------------------------------- helpers

    private static object Update(string title, string rowVersion) => new
    {
        Slug = "stale-audit-course",
        Title = title,
        Summary = "A summary long enough to be useful.",
        Description = "A description that explains the course.",
        Level = "AllLevels",
        IncludedInMembership = true,
        ImageAltText = (string?)null,
        RowVersion = rowVersion,
    };

    private static Guid FirstSectionId(JsonDocument detail)
    {
        using (detail)
        {
            return detail.RootElement.GetProperty("sections")[0].GetProperty("id").GetGuid();
        }
    }

    private async Task<int> CountAuditsAsync(Guid targetId)
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        return await context.AuditLogs.CountAsync(entry => entry.TargetId == targetId.ToString("D"));
    }

    private async Task<Guid> PendingRequestIdAsync()
    {
        await using DanielsDojoDbContext context = fixture.CreateContext();

        return await context.FriendRequests
            .Where(request => request.Status == FriendRequestStatus.Pending)
            .Select(request => request.Id)
            .SingleAsync();
    }

    private static async Task Expect(
        HttpClient client,
        HttpMethod method,
        string path,
        object? payload,
        HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.SendJsonAsync(method, path, payload);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            expected == response.StatusCode,
            $"{method} {path} expected {expected} but returned {response.StatusCode}: {body}");
    }

    private async Task<TestActor> MemberAsync(
        string handle,
        bool discoverable = false,
        bool openToFriends = false,
        bool openMessages = false)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        if (discoverable || openToFriends || openMessages)
        {
            await UpdatePrivacyAsync(
                client,
                discoverable,
                openToFriends,
                openMessages,
                created.RootElement.GetProperty("rowVersion").GetString());
        }

        return actor;
    }

    private static async Task UpdatePrivacyAsync(
        HttpClient client,
        bool discoverable,
        bool openToFriends,
        bool openMessages,
        string? rowVersion = null)
    {
        if (rowVersion is null)
        {
            using JsonDocument current = await client.GetJsonAsync("/api/v1/me/community/profile");
            rowVersion = current.RootElement.GetProperty("rowVersion").GetString();
        }

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = (string?)null,
                IsDiscoverable = discoverable,
                FriendRequestPolicy = openToFriends ? "Everyone" : "NoOne",
                MessagePolicy = openMessages ? "FriendsOnly" : "NoOne",
                RowVersion = rowVersion,
            },
            HttpStatusCode.OK);
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
