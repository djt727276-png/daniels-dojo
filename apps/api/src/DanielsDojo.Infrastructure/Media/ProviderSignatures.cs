using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DanielsDojo.Infrastructure.Media;

/// <summary>
/// Verification of inbound provider notifications, and minting of playback tokens.
/// </summary>
/// <remarks>
/// <para>
/// Both adapters share this. The deterministic pipeline is not a stub that waves signatures
/// through — it signs and verifies with exactly this code, so a change that breaks signature
/// checking fails in the deterministic suite rather than surviving until production.
/// </para>
/// <para>
/// Tokens are minted by hand rather than through a token library. Issuing a JWT is a header, a
/// payload, and one signature; the security-relevant part is the key handling and the expiry,
/// both of which are visible here. Nothing in this file ever validates an inbound identity
/// token — that stays with the authentication stack, where the hard parts live.
/// </para>
/// <para>
/// Public for the same reason as the access evaluator: signature verification is exercised
/// directly rather than only through an endpoint, because the interesting cases — a replayed
/// timestamp, a tampered body, a missing secret — are hard to reach over HTTP and are exactly
/// the ones that must never regress.
/// </para>
/// </remarks>
public static class ProviderSignatures
{
    /// <summary>
    /// Checks a <c>t=&lt;unix&gt;,v1=&lt;hex&gt;</c> signature header against the payload.
    /// </summary>
    /// <remarks>
    /// The timestamp is inside the signed material, so an attacker cannot replay a captured
    /// delivery with a fresh timestamp — changing it invalidates the signature. The tolerance
    /// window then bounds how long a captured-and-unmodified delivery stays useful.
    /// </remarks>
    public static bool IsValidWebhookSignature(
        string payload,
        string? signatureHeader,
        string secret,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        string? timestamp = null;
        string? provided = null;

        foreach (string part in signatureHeader.Split(',', StringSplitOptions.TrimEntries))
        {
            int separator = part.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            string name = part[..separator];
            string value = part[(separator + 1)..];

            switch (name)
            {
                case "t":
                    timestamp = value;
                    break;
                case "v1":
                    provided = value;
                    break;
                default:
                    break;
            }
        }

        if (timestamp is null
            || provided is null
            || !long.TryParse(timestamp, CultureInfo.InvariantCulture, out long unixSeconds))
        {
            return false;
        }

        TimeSpan age = now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        if (age > tolerance || age < -tolerance)
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        byte[] actual;

        try
        {
            actual = Convert.FromHexString(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        // Fixed-time comparison: a byte-by-byte early exit would leak how much of a guessed
        // signature was correct.
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Produces the signature header a caller would send for a payload.</summary>
    public static string CreateWebhookSignature(string payload, string secret, DateTimeOffset now)
    {
        long timestamp = now.ToUnixTimeSeconds();

        byte[] signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"t={timestamp},v1={Convert.ToHexString(signature).ToLowerInvariant()}");
    }

    /// <summary>Mints a playback token signed with an RSA private key.</summary>
    public static string CreateRsaPlaybackToken(
        RSA privateKey,
        string keyId,
        string playbackId,
        DateTimeOffset expiresAt)
    {
        string signingInput = SigningInput("RS256", keyId, playbackId, expiresAt);

        byte[] signature = privateKey.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    /// <summary>Mints a playback token signed with a shared secret.</summary>
    public static string CreateHmacPlaybackToken(
        string secret,
        string keyId,
        string playbackId,
        DateTimeOffset expiresAt)
    {
        string signingInput = SigningInput("HS256", keyId, playbackId, expiresAt);

        byte[] signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.ASCII.GetBytes(signingInput));

        return $"{signingInput}.{Base64Url(signature)}";
    }

    /// <summary>Reads the expiry out of a token this class produced, for tests and diagnostics.</summary>
    public static DateTimeOffset? ReadExpiry(string token)
    {
        string[] parts = token.Split('.');

        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(FromBase64Url(parts[1]));

            return payload.RootElement.TryGetProperty("exp", out JsonElement expiry)
                ? DateTimeOffset.FromUnixTimeSeconds(expiry.GetInt64())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SigningInput(
        string algorithm,
        string keyId,
        string playbackId,
        DateTimeOffset expiresAt)
    {
        string header = $$"""{"alg":"{{algorithm}}","typ":"JWT","kid":"{{keyId}}"}""";

        // "v" is the video audience: the token authorises playback of one identifier and
        // nothing else, so it is useless if it leaks into a log or a shared link after expiry.
        string payload = $$"""
            {"sub":"{{playbackId}}","aud":"v","exp":{{expiresAt.ToUnixTimeSeconds()}},"kid":"{{keyId}}"}
            """;

        return $"{Base64Url(Encoding.UTF8.GetBytes(header))}."
            + $"{Base64Url(Encoding.UTF8.GetBytes(payload))}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}
