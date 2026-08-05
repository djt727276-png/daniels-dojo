using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DanielsDojo.Infrastructure.Persistence;
using DanielsDojo.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DanielsDojo.IntegrationTests.Community;

/// <summary>
/// Avatar upload, serving, and removal.
/// </summary>
/// <remarks>
/// The invariant under test: no byte a client produced is ever stored or served. A genuine
/// raster upload comes back as a fresh fixed-size JPEG; an SVG or arbitrary bytes are
/// refused by the decode itself; and a block hides the avatar exactly like not having one.
/// </remarks>
[Collection(SqlServerDatabaseSuite.Name)]
public sealed class AvatarTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private const string AvatarRoute = "/api/v1/me/community/profile/avatar";

    private ApiHarness _harness = null!;
    private TestActor _member = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _harness = ApiHarness.Create(fixture);
        _member = await SignedUpMemberAsync("avatar-owner");
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ARealImageIsReEncodedNeverStoredVerbatim()
    {
        byte[] original = MakePng(640, 480);

        using HttpClient client = _harness.CreateClient(_member);
        using HttpResponseMessage uploaded = await UploadAsync(client, original, "photo.png");
        Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);

        // What is served is a server-encoded 256×256 JPEG — not the uploaded PNG.
        using HttpResponseMessage served = await client.GetAsync(
            new Uri($"/api/v1/community/avatars/{_member.UserId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(served.Headers.ETag);

        byte[] stored = await served.Content.ReadAsByteArrayAsync();
        Assert.NotEqual(original, stored);

        using Image roundTripped = Image.Load(stored);
        Assert.Equal(256, roundTripped.Width);
        Assert.Equal(256, roundTripped.Height);
    }

    [Fact]
    public async Task SvgAndArbitraryBytesAreRefused()
    {
        using HttpClient client = _harness.CreateClient(_member);

        byte[] svg = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        using (HttpResponseMessage refused = await UploadAsync(client, svg, "image.svg"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        // Junk with a plausible name and image content type fares no better: the decode is
        // the validator, not the filename or the header.
        byte[] junk = new byte[512];
        Random.Shared.NextBytes(junk);

        using HttpResponseMessage alsoRefused = await UploadAsync(client, junk, "image.png");
        Assert.Equal(HttpStatusCode.BadRequest, alsoRefused.StatusCode);

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.False(await context.ProfileAvatars.AnyAsync());
    }

    [Fact]
    public async Task ABlockHidesTheAvatarLikeItNeverExisted()
    {
        using HttpClient owner = _harness.CreateClient(_member);
        using (HttpResponseMessage _ = await UploadAsync(owner, MakePng(64, 64), "photo.png"))
        {
        }

        TestActor viewer = await SignedUpMemberAsync("curious-viewer");
        using HttpClient viewerClient = _harness.CreateClient(viewer);

        // Visible before the block…
        using (HttpResponseMessage visible = await viewerClient.GetAsync(
            new Uri($"/api/v1/community/avatars/{_member.UserId}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        }

        using (JsonDocument _ = await viewerClient.SendJsonAsync(
            HttpMethod.Post,
            "/api/v1/community/blocks",
            new { Handle = "avatar-owner", ReasonCategory = "Personal" },
            HttpStatusCode.NoContent))
        {
        }

        // …and indistinguishable from absent after it.
        using HttpResponseMessage hidden = await viewerClient.GetAsync(
            new Uri($"/api/v1/community/avatars/{_member.UserId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task RemovalDeletesTheBytesAndTheProfileReportsIt()
    {
        using HttpClient client = _harness.CreateClient(_member);
        using (HttpResponseMessage _ = await UploadAsync(client, MakePng(64, 64), "photo.png"))
        {
        }

        using (JsonDocument profile = await client.GetJsonAsync("/api/v1/me/community/profile"))
        {
            Assert.True(profile.RootElement.GetProperty("hasAvatar").GetBoolean());
        }

        using HttpResponseMessage removed = await client.DeleteAsync(
            new Uri(AvatarRoute, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using (JsonDocument profile = await client.GetJsonAsync("/api/v1/me/community/profile"))
        {
            Assert.False(profile.RootElement.GetProperty("hasAvatar").GetBoolean());
        }

        await using DanielsDojoDbContext context = fixture.CreateContext();
        Assert.False(await context.ProfileAvatars.AnyAsync());
    }

    [Fact]
    public async Task AMemberWithoutAProfileCannotUpload()
    {
        TestActor newcomer = await _harness.SignInAsync();
        using HttpClient client = _harness.CreateClient(newcomer);

        using HttpResponseMessage refused = await UploadAsync(client, MakePng(64, 64), "photo.png");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A genuine PNG with real pixel data, produced by the same library family.</summary>
    private static byte[] MakePng(int width, int height)
    {
        using Image<Rgba32> image = new(width, height, new Rgba32(90, 60, 200));
        using MemoryStream output = new();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] bytes,
        string fileName)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? "image/svg+xml"
                : "image/png");
        form.Add(file, "file", fileName);

        return await client.PutAsync(new Uri(AvatarRoute, UriKind.Relative), form);
    }

    private async Task<TestActor> SignedUpMemberAsync(string handle)
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
}
