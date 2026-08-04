using Microsoft.Extensions.Options;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Refuses to start a host that has enabled the Development authentication harness outside
/// the Development environment.
/// </summary>
/// <remarks>
/// The registration code already ignores the setting outside Development, so this validator
/// is the second, louder line of defence: rather than silently running with the harness
/// inert, the host fails immediately and says why. A Production deployment that inherited a
/// Development configuration file is therefore caught at boot rather than shipping with a
/// setting nobody notices.
/// </remarks>
internal sealed class DevelopmentAuthOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<DevelopmentAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, DevelopmentAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (!DevelopmentAuthOptions.IsExactlyDevelopment(environment))
        {
            return ValidateOptionsResult.Fail(
                $"{DevelopmentAuthOptions.SectionName}:Enabled may only be true when the host " +
                $"environment is exactly 'Development'. The current environment is " +
                $"'{environment.EnvironmentName}'. The Development sign-in harness is not a " +
                "production credential source and its endpoint is never mapped outside " +
                "Development.");
        }

        if (options.TokenLifetime <= TimeSpan.Zero || options.TokenLifetime > TimeSpan.FromDays(1))
        {
            return ValidateOptionsResult.Fail(
                $"{DevelopmentAuthOptions.SectionName}:TokenLifetime must be positive and no " +
                "longer than 24 hours.");
        }

        return ValidateOptionsResult.Success;
    }
}
