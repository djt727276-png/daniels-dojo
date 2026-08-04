using DanielsDojo.Api.Authentication;
using DanielsDojo.Api.Catalog;
using DanielsDojo.Api.Commerce;
using DanielsDojo.Api.Community;
using DanielsDojo.Api.Common;
using DanielsDojo.Api.Hosting;
using DanielsDojo.Application.Common;
using DanielsDojo.Application.Identity;
using DanielsDojo.Application.System;
using DanielsDojo.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON console logging. Scopes are excluded and no request bodies or
// secrets are logged, keeping log output safe for shared/aggregated sinks.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
});

// Cross-cutting framework services. The database check is tagged for readiness only, so
// liveness stays independent of SQL and a database outage never restarts the process.
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDatabaseReadinessCheck();
builder.Services.AddOpenApi();

// Persistence. Registration opens no connection and never migrates or seeds.
builder.Services.AddInfrastructure(builder.Configuration);

// Entra External ID bearer validation plus local, database-backed application authorization.
// The Development sign-in harness is registered only inside the Development environment.
builder.Services.AddDanielsDojoAuthentication(builder.Configuration, builder.Environment);

// Actor and correlation for audited writes. The actor is the local application user resolved
// by the provisioning middleware, never a token claim.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOperationContext, HttpOperationContext>();

// Community write limits, partitioned by the immutable local application user identifier.
builder.Services.AddDanielsDojoRateLimiting();

// System-status vertical slice: injectable time + host-environment abstraction.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApplicationEnvironment, HostApplicationEnvironment>();
builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();

var app = builder.Build();

// Explicit operator commands ("database migrate", "database seed --profile ...") run here
// and exit. Serving traffic never migrates or seeds as a side effect of startup.
if (DatabaseCommand.Matches(args))
{
    return await DatabaseCommand.ExecuteAsync(app, args);
}

// "identity grant-admin --user-id <guid> --reason "..." --confirm". There is deliberately no
// HTTP route that can grant Admin.
if (IdentityCommand.Matches(args))
{
    return await IdentityCommand.ExecuteAsync(app, args);
}

// Centralized exception handling produces RFC 7807 ProblemDetails responses and
// never exposes stack traces to clients in any environment.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // OpenAPI is exposed only in Development.
    app.MapOpenApi();

    // HTTPS redirection is enabled for local development only. In the container the
    // app listens on plain HTTP (port 8080) so health probes are never redirected.
    app.UseHttpsRedirection();
}

app.UseCors(AuthenticationRegistration.CorsPolicy);

// Order matters: authenticate the token, resolve or provision the local user, then authorize
// against the local database's roles. Anonymous requests pass through the middle stage
// untouched, so the public routes below never trigger provisioning.
app.UseAuthentication();
app.UseMiddleware<LocalUserProvisioningMiddleware>();
app.UseAuthorization();

// After authorization, so a partition key can use the local user the middleware resolved.
app.UseRateLimiter();

// Development sign-in harness. Mapped only inside Development with the harness enabled, so
// the route simply does not exist elsewhere and the API answers 404 rather than 403.
if (DevelopmentAuthOptions.IsExactlyDevelopment(app.Environment)
    && app.Services.GetRequiredService<IOptions<DevelopmentAuthOptions>>().Value.Enabled)
{
    app.MapDevelopmentAuthEndpoints();
}

// Versioned, unauthenticated system-status endpoint.
var apiV1 = app.MapGroup("/api/v1");
apiV1.MapGet("/system/status",
    (ISystemStatusService statusService) => TypedResults.Ok(statusService.GetStatus()))
    .AllowAnonymous();

// Anonymous catalog: published projections only.
apiV1.MapPublicCatalogEndpoints();

// Catalog authoring. The group requires the database-backed Admin role.
apiV1.MapAdminCatalogEndpoints();

// Offer and price management. Database-only: nothing here calls a payment provider.
apiV1.MapAdminPricingEndpoints();

// The signed-in member's own screens. Every route resolves the caller from the local user.
apiV1.MapMemberEndpoints();

// Community: forums for members, moderation for Admins.
apiV1.MapForumEndpoints();
apiV1.MapSocialEndpoints();
apiV1.MapModerationEndpoints();

// Authenticated session view. Every value comes from the local user record resolved by the
// provisioning middleware, never from the token.
apiV1.MapGet("/auth/session", (ICurrentUser currentUser) =>
{
    var user = currentUser.User!;
    return TypedResults.Ok(
        new SessionResponse(user.UserId, user.DisplayName, user.Email, user.RoleNames));
})
    .RequireAuthorization(AuthenticationRegistration.StudentPolicy);

// Admin smoke endpoint: proves the local Admin role gate works end to end.
apiV1.MapGet("/admin/session", (TimeProvider timeProvider) =>
    TypedResults.Ok(new AdminSessionResponse("ok", timeProvider.GetUtcNow())))
    .RequireAuthorization(AuthenticationRegistration.AdminPolicy);

// Liveness: succeeds whenever the process is running (no dependency checks run).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
});

// Readiness: gated on the tagged dependency checks. The database check makes this
// unhealthy whenever SQL is unreachable or migrations are still pending.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration => registration.Tags.Contains(DependencyInjection.ReadinessTag),
});

await app.RunAsync();

return 0;

// Exposes the implicit Program class to the integration-test host (WebApplicationFactory)
// in the conventional supported manner, without widening production visibility further.
#pragma warning disable CA1050 // Top-level Program has no namespace by design.
public partial class Program { }
#pragma warning restore CA1050
