using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// The signing key used by the Development authentication harness.
/// </summary>
/// <remarks>
/// Generated fresh in memory when the process starts and never written to disk, printed, or
/// committed. Restarting the API therefore invalidates every previously issued Development
/// token, which is exactly the behaviour wanted from a local convenience credential.
/// </remarks>
public sealed class DevelopmentSigningKey : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>Key identifier published in the token header.</summary>
    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Credentials used to sign issued tokens.</summary>
    public SigningCredentials CreateSigningCredentials() =>
        new(new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);

    /// <summary>Public key the validation pipeline verifies signatures against.</summary>
    public SecurityKey PublicKey => new RsaSecurityKey(_rsa.ExportParameters(false))
    {
        KeyId = KeyId,
    };

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();
}
