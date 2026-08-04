namespace DanielsDojo.Application.Common;

/// <summary>
/// Converts a SQL Server <c>rowversion</c> to and from the opaque Base64 token clients echo
/// back on a write.
/// </summary>
/// <remarks>
/// The token is deliberately opaque: a client must round-trip exactly what it was given. It
/// carries no meaning a caller can construct, so a write cannot be forced through by inventing
/// a token — an unparsable or wrong-length value is rejected before the database is touched.
/// </remarks>
public static class RowVersionToken
{
    /// <summary>Length in bytes of a SQL Server rowversion.</summary>
    private const int RowVersionLength = 8;

    /// <summary>Encodes a row version for transport.</summary>
    public static string Encode(byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);

        return Convert.ToBase64String(rowVersion);
    }

    /// <summary>
    /// Decodes a client token. Returns false for null, blank, non-Base64, or wrong-length
    /// input rather than throwing.
    /// </summary>
    public static bool TryDecode(string? token, out byte[] rowVersion)
    {
        rowVersion = [];

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[RowVersionLength];

        if (!Convert.TryFromBase64String(token, buffer, out int written)
            || written != RowVersionLength)
        {
            return false;
        }

        rowVersion = buffer.ToArray();
        return true;
    }
}
