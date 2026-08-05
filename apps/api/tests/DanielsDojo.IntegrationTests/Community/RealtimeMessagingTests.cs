using System.Net;
using System.Text.Json;
using DanielsDojo.Domain.Community;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// The live community channel, end to end over the real test server.
/// </summary>
/// <remarks>
/// The properties under test are the design itself: the hub is receive-only and scoped to
/// the validated identity. A message sent over audited REST rings the recipient's
/// connection; an anonymous connection never starts; and a bystander hears nothing, because
/// subscription comes from the token, not from anything a client asks for.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class RealtimeMessagingTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string Base = "/api/v1/community";

    private ApiHarness _harness = null!;
    private TestActor _sender = null!;
    private TestActor _recipient = null!;
    private TestActor _bystander = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _sender = await MemberAsync("rt-sender");
        _recipient = await MemberAsync("rt-recipient");
        _bystander = await MemberAsync("rt-bystander");

        // Friendship, established the ordinary way.
        using HttpClient sender = _harness.CreateClient(_sender);
        using JsonDocument _ = await sender.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/friend-requests",
            new { Handle = "rt-recipient" },
            HttpStatusCode.NoContent);

        Guid requestId;

        await using (DanielsDojoDbContext context = fixture.CreateContext())
        {
            requestId = (await context.FriendRequests.SingleAsync(
                request => request.RequestedByUserId == _sender.UserId
                    && request.Status == FriendRequestStatus.Pending)).Id;
        }

        using HttpClient recipient = _harness.CreateClient(_recipient);
        using JsonDocument __ = await recipient.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/friend-requests/{requestId}/accept",
            null,
            HttpStatusCode.NoContent);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ARestMessageRingsTheRecipientsConnectionAndNobodyElses()
    {
        TaskCompletionSource<Guid> recipientRang = new();
        TaskCompletionSource<Guid> bystanderRang = new();

        await using HubConnection recipientHub = Connect(_recipient, recipientRang);
        await using HubConnection bystanderHub = Connect(_bystander, bystanderRang);

        await recipientHub.StartAsync();
        await bystanderHub.StartAsync();

        // The sender writes through the ordinary audited REST surface.
        using HttpClient sender = _harness.CreateClient(_sender);
        using JsonDocument conversation = await sender.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations",
            new { Handle = "rt-recipient" },
            HttpStatusCode.OK);

        Guid conversationId = conversation.RootElement.GetProperty("id").GetGuid();

        using JsonDocument _ = await sender.SendJsonAsync(
            HttpMethod.Post,
            $"{Base}/conversations/{conversationId}/messages",
            new { Body = "Live hello." },
            HttpStatusCode.OK);

        // The recipient's connection rings with the conversation to go and fetch.
        Guid rangWith = await recipientRang.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(conversationId, rangWith);

        // The bystander hears nothing: subscription is identity-scoped.
        await Task.Delay(500);
        Assert.False(bystanderRang.Task.IsCompleted);
    }

    [Fact]
    public async Task AnAnonymousConnectionIsRefused()
    {
        await using HubConnection anonymous = new HubConnectionBuilder()
            .WithUrl($"{_harness.Factory.Server.BaseAddress}hubs/community", options =>
            {
                options.HttpMessageHandlerFactory = _ => _harness.Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
    }

    // ---------------------------------------------------------------- helpers

    private HubConnection Connect(TestActor actor, TaskCompletionSource<Guid> rang)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl($"{_harness.Factory.Server.BaseAddress}hubs/community", options =>
            {
                options.HttpMessageHandlerFactory = _ => _harness.Factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(actor.Token);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        connection.On<Guid>("MessageReceived", conversationId => rang.TrySetResult(conversationId));

        return connection;
    }

    private async Task<TestActor> MemberAsync(string handle)
    {
        TestActor actor = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(actor);

        using JsonDocument created = await client.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/me/community/profile",
            new { Handle = handle, Bio = (string?)null, AcceptGuidelines = true, AttestEligibility = true },
            HttpStatusCode.OK);

        string? rowVersion = created.RootElement.GetProperty("rowVersion").GetString();

        using JsonDocument _ = await client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/me/community/profile",
            new
            {
                Bio = (string?)null,
                IsDiscoverable = true,
                FriendRequestPolicy = "Everyone",
                MessagePolicy = "FriendsOnly",
                RowVersion = rowVersion,
            },
            HttpStatusCode.OK);

        return actor;
    }
}
