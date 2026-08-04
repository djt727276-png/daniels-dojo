using System.Security.Cryptography;
using System.Text;
using DanielsDojo.Domain.Catalog;
using DanielsDojo.Infrastructure.Media;
using Xunit;

namespace DanielsDojo.UnitTests;

/// <summary>
/// Notification authentication and playback token minting.
/// </summary>
/// <remarks>
/// The webhook endpoint is anonymous by necessity — the provider holds no credential of ours —
/// so the signature is the only thing standing between a stranger and the ability to mark
/// somebody's lesson failed. Each case below is a way that could be got wrong.
/// </remarks>
public sealed class ProviderSignatureTests
{
    private const string Secret = "a-shared-secret-nobody-else-has";
    private const string Payload = """{"id":"evt_1","type":"video.asset.ready"}""";

    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact]
    public void AGenuineSignatureIsAccepted()
    {
        string header = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now);

        Assert.True(ProviderSignatures.IsValidWebhookSignature(Payload, header, Secret, Now, Tolerance));
    }

    [Fact]
    public void ADifferentSecretIsRejected()
    {
        string header = ProviderSignatures.CreateWebhookSignature(Payload, "somebody-elses-secret", Now);

        Assert.False(ProviderSignatures.IsValidWebhookSignature(Payload, header, Secret, Now, Tolerance));
    }

    [Fact]
    public void ATamperedPayloadIsRejected()
    {
        string header = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now);

        Assert.False(ProviderSignatures.IsValidWebhookSignature(
            """{"id":"evt_1","type":"video.asset.errored"}""",
            header,
            Secret,
            Now,
            Tolerance));
    }

    [Fact]
    public void ACapturedDeliveryStopsWorkingOnceItIsOldEnough()
    {
        string header = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now);

        Assert.True(ProviderSignatures.IsValidWebhookSignature(
            Payload, header, Secret, Now.AddMinutes(4), Tolerance));

        Assert.False(ProviderSignatures.IsValidWebhookSignature(
            Payload, header, Secret, Now.AddMinutes(6), Tolerance));
    }

    [Fact]
    public void ASignatureFromTheFutureIsRejected()
    {
        // A clock far ahead of ours is either broken or an attempt to buy an unlimited window.
        string header = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now.AddHours(1));

        Assert.False(ProviderSignatures.IsValidWebhookSignature(Payload, header, Secret, Now, Tolerance));
    }

    [Fact]
    public void ReplayingACapturedSignatureUnderAFreshTimestampFails()
    {
        string original = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now.AddHours(-1));
        string stolenSignature = original[(original.IndexOf("v1=", StringComparison.Ordinal) + 3)..];

        string forged = $"t={Now.ToUnixTimeSeconds()},v1={stolenSignature}";

        // The timestamp is inside the signed material, so moving it invalidates the signature.
        Assert.False(ProviderSignatures.IsValidWebhookSignature(Payload, forged, Secret, Now, Tolerance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("t=notanumber,v1=abcd")]
    [InlineData("v1=abcd")]
    [InlineData("t=1,v1=zzzz")]
    public void AMalformedHeaderIsRejectedRatherThanThrowing(string? header) =>
        Assert.False(ProviderSignatures.IsValidWebhookSignature(Payload, header, Secret, Now, Tolerance));

    [Fact]
    public void NoConfiguredSecretMeansNothingIsAccepted()
    {
        string header = ProviderSignatures.CreateWebhookSignature(Payload, Secret, Now);

        // Failing closed here is the difference between a rejected request and an endpoint
        // anyone on the internet can drive.
        Assert.False(ProviderSignatures.IsValidWebhookSignature(Payload, header, "", Now, Tolerance));
    }

    // ------------------------------------------------------------------ playback tokens

    [Fact]
    public void AnHmacPlaybackTokenCarriesTheIdentifierAndAnExpiry()
    {
        DateTimeOffset expiry = Now.AddMinutes(30);

        string token = ProviderSignatures.CreateHmacPlaybackToken(Secret, "key-1", "playback-abc", expiry);

        Assert.Equal(3, token.Split('.').Length);
        Assert.Equal(expiry.ToUnixTimeSeconds(), ProviderSignatures.ReadExpiry(token)!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void TwoPlaybackIdentifiersNeverProduceTheSameToken()
    {
        DateTimeOffset expiry = Now.AddMinutes(30);

        Assert.NotEqual(
            ProviderSignatures.CreateHmacPlaybackToken(Secret, "key-1", "playback-abc", expiry),
            ProviderSignatures.CreateHmacPlaybackToken(Secret, "key-1", "playback-xyz", expiry));
    }

    [Fact]
    public void AnRsaPlaybackTokenVerifiesAgainstTheMatchingPublicKey()
    {
        using RSA key = RSA.Create(2048);

        string token = ProviderSignatures.CreateRsaPlaybackToken(
            key, "key-1", "playback-abc", Now.AddMinutes(30));

        string[] parts = token.Split('.');
        string signingInput = $"{parts[0]}.{parts[1]}";

        byte[] signature = Convert.FromBase64String(
            parts[2].Replace('-', '+').Replace('_', '/')
                .PadRight(parts[2].Length + ((4 - (parts[2].Length % 4)) % 4), '='));

        Assert.True(key.VerifyData(
            Encoding.ASCII.GetBytes(signingInput),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }
}

/// <summary>
/// Reading provider payloads.
/// </summary>
/// <remarks>
/// A payload whose shape has changed is an operational fact to report, not a crash on a public
/// endpoint, so every malformed case here has to produce null rather than an exception.
/// </remarks>
public sealed class MuxEventParserTests
{
    [Fact]
    public void AReadyEventWithAPlaybackIdentifierBecomesReady()
    {
        var parsed = MuxEventParser.Parse("""
            {
              "id": "evt_1",
              "type": "video.asset.ready",
              "created_at": "2026-08-04T12:00:00Z",
              "data": {
                "id": "asset_1",
                "status": "ready",
                "duration": 61.4,
                "aspect_ratio": "16:9",
                "passthrough": "lesson-video-1",
                "playback_ids": [{ "id": "pb_1", "policy": "signed" }]
              }
            }
            """);

        Assert.NotNull(parsed);
        Assert.Equal("evt_1", parsed.EventId);
        Assert.Equal("asset_1", parsed.AssetId);
        Assert.Equal("lesson-video-1", parsed.UploadId);
        Assert.Equal(LessonVideoStatus.Ready, parsed.State!.Status);
        Assert.Equal("pb_1", parsed.State.PlaybackId);
        Assert.Equal(61, parsed.State.DurationSeconds);
    }

    [Fact]
    public void ReadyWithoutAPlaybackIdentifierIsNotTreatedAsReady()
    {
        var parsed = MuxEventParser.Parse("""
            {
              "id": "evt_2",
              "type": "video.asset.ready",
              "data": { "id": "asset_2", "status": "ready" }
            }
            """);

        // Nothing to play means not ready, whatever the provider called it.
        Assert.Equal(LessonVideoStatus.Processing, parsed!.State!.Status);
    }

    [Fact]
    public void AnErroredEventCarriesAReason()
    {
        var parsed = MuxEventParser.Parse("""
            {
              "id": "evt_3",
              "type": "video.asset.errored",
              "data": { "id": "asset_3", "status": "errored", "errors": { "type": "invalid_input" } }
            }
            """);

        Assert.Equal(LessonVideoStatus.Failed, parsed!.State!.Status);
        Assert.Equal("invalid_input", parsed.State.FailureCode);
    }

    [Fact]
    public void AnErroredEventWithNoStatedReasonStillRecordsOne()
    {
        var parsed = MuxEventParser.Parse("""
            { "id": "evt_4", "type": "video.asset.errored", "data": { "id": "a", "status": "errored" } }
            """);

        // The schema requires a failure code on a failed video, so one is always produced.
        Assert.Equal("provider_errored", parsed!.State!.FailureCode);
    }

    [Fact]
    public void AnEventWithNoIdentifierIsRejected()
    {
        // Without one, a redelivery cannot be told from a new event, so it cannot be applied
        // exactly once — which means it must not be applied at all.
        Assert.Null(MuxEventParser.Parse("""{ "type": "video.asset.ready", "data": {} }"""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "id": "evt_5" }""")]
    public void AnythingUnrecognisedProducesNullRatherThanThrowing(string payload) =>
        Assert.Null(MuxEventParser.Parse(payload));
}
