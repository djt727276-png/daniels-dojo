using System.Globalization;
using System.Threading.RateLimiting;
using DanielsDojo.Application.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace DanielsDojo.Api.Common;

/// <summary>Named rate-limit policies applied to community writes.</summary>
internal static class RateLimitPolicies
{
    /// <summary>Creating threads and posts, and editing them.</summary>
    public const string CommunityWrite = "community-write";

    /// <summary>Adding and removing reactions.</summary>
    public const string CommunityReaction = "community-reaction";

    /// <summary>Filing reports.</summary>
    public const string CommunityReport = "community-report";

    /// <summary>Sending friend requests.</summary>
    public const string CommunityFriendRequest = "community-friend-request";

    /// <summary>Sending direct messages.</summary>
    public const string CommunityMessage = "community-message";

    /// <summary>Searching for members by handle.</summary>
    public const string ProfileSearch = "profile-search";
}

/// <summary>
/// Registers the community rate limits.
/// </summary>
/// <remarks>
/// <para>
/// Every authenticated limit is partitioned by the immutable local application user
/// identifier. Not by a forwarded-for header, which any client can set; not by a token claim,
/// which is only as trustworthy as the token; and not by a handle or email, which a member can
/// change. One account is one bucket, and the only way to get a second bucket is to be a
/// second account.
/// </para>
/// <para>
/// An unauthenticated request cannot reach these endpoints at all — authorization runs first —
/// so the fallback partition exists only so the limiter is total, and it is deliberately tiny.
/// </para>
/// </remarks>
internal static class RateLimiting
{
    /// <summary>Partition name used when no local user has been resolved.</summary>
    private const string AnonymousPartition = "anonymous";

    /// <summary>Adds the named policies and the shared 429 response.</summary>
    public static IServiceCollection AddDanielsDojoRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = static (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // A stable code so the client can say "slow down" rather than "something
                // went wrong". No detail about other callers or limits is disclosed.
                return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                        title = "Too many requests",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "That was a lot of requests in a short time. Wait a moment and try again.",
                        code = "platform.rate_limited",
                    },
                    cancellationToken));
            };

            AddFixedWindow(options, RateLimitPolicies.CommunityWrite, permitLimit: 10, windowMinutes: 1);
            AddFixedWindow(options, RateLimitPolicies.CommunityReaction, permitLimit: 60, windowMinutes: 1);
            AddFixedWindow(options, RateLimitPolicies.CommunityReport, permitLimit: 5, windowMinutes: 10);
            AddFixedWindow(options, RateLimitPolicies.CommunityFriendRequest, permitLimit: 20, windowMinutes: 60);
            AddFixedWindow(options, RateLimitPolicies.CommunityMessage, permitLimit: 30, windowMinutes: 1);
            AddFixedWindow(options, RateLimitPolicies.ProfileSearch, permitLimit: 30, windowMinutes: 1);
        });

        return services;
    }

    private static void AddFixedWindow(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        int windowMinutes) =>
        options.AddPolicy(policyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext, policyName),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    /// <summary>
    /// Builds the bucket key from the resolved local user, falling back to a single shared
    /// anonymous bucket. Nothing from the request headers contributes to the key.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext, string policyName)
    {
        ApplicationUser? user = httpContext.RequestServices
            .GetService<ICurrentUser>()?.User;

        return user is null
            ? $"{policyName}:{AnonymousPartition}"
            : $"{policyName}:{user.UserId:D}";
    }
}
