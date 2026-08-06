using DanielsDojo.Application.Identity;
using Microsoft.Extensions.Options;

namespace DanielsDojo.Api.Authentication;

/// <summary>
/// Turns a validated access token into a local Daniel's Dojo user, then hands off to
/// authorization. Runs after <c>UseAuthentication</c> and before <c>UseAuthorization</c>, so
/// policies evaluate against the local database rather than token claims.
/// </summary>
/// <remarks>
/// Anonymous requests pass straight through — public routes such as system status and the
/// health probes must never touch the database here. Every failure produces a plain 403 with no
/// hint about which check tripped, so the endpoint cannot be used to probe account state.
/// </remarks>
internal sealed partial class LocalUserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserProvisioningService provisioningService,
        CurrentUserAccessor currentUserAccessor,
        IOptions<EntraExternalIdOptions> options,
        ILogger<LocalUserProvisioningMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        EntraExternalIdOptions settings = options.Value;

        // Scope and authorized-party are properties of the token rather than of the user, so
        // they are checked here before any database work.
        if (!ExternalIdentityClaims.HasRequiredScope(context.User, settings.RequiredScope))
        {
            LogDenied(logger, "required scope missing");
            await WriteForbiddenAsync(context).ConfigureAwait(false);
            return;
        }

        if (!ExternalIdentityClaims.IsAllowedClient(context.User, settings.AllowedClientIds))
        {
            LogDenied(logger, "calling client not allowlisted");
            await WriteForbiddenAsync(context).ConfigureAwait(false);
            return;
        }

        ExternalUserIdentity? identity = ExternalIdentityClaims.TryRead(context.User, settings);
        if (identity is null)
        {
            LogDenied(logger, "immutable identity claims missing");
            await WriteForbiddenAsync(context).ConfigureAwait(false);
            return;
        }

        UserProvisioningResult result = await provisioningService
            .ResolveAsync(identity, context.RequestAborted)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            string reason = result.Failure.ToString();
            LogDenied(logger, reason);

            // A missing email claim means the token is valid but carries nothing to
            // contact the customer with — almost always because the API registration was
            // never asked to emit the claim, not because the customer did anything wrong.
            // Logging the claim *names* present (never their values, which are personal
            // data) turns a silent 403 into a one-look diagnosis.
            if (result.Failure == UserProvisioningFailure.MissingEmailClaim)
            {
                LogClaimNames(
                    logger,
                    string.Join(',', context.User.Claims.Select(claim => claim.Type).Distinct()));
            }

            await WriteForbiddenAsync(context).ConfigureAwait(false);
            return;
        }

        currentUserAccessor.Set(result.User!);

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an RFC 7807 response carrying no token, claim, or account detail. The client is
    /// told it may not proceed and nothing more.
    /// </summary>
    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        IProblemDetailsService problemDetails =
            context.RequestServices.GetRequiredService<IProblemDetailsService>();

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "The signed-in account may not access this resource.",
            },
        }).ConfigureAwait(false);
    }

    // Reason is a fixed internal string, never a claim value or personal data.
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message = "Request denied during local user resolution: {Reason}.")]
    private static partial void LogDenied(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Warning,
        Message = "The validated token carried no email claim. Claim names present: {ClaimNames}. "
            + "Configure the API app registration to emit the claim named by "
            + "Authentication:EntraExternalId:EmailClaimName. Claim values are never logged.")]
    private static partial void LogClaimNames(ILogger logger, string claimNames);
}
