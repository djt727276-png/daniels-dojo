using DanielsDojo.Api.Hosting;
using DanielsDojo.Application.System;
using DanielsDojo.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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

// Versioned, unauthenticated system-status endpoint.
var apiV1 = app.MapGroup("/api/v1");
apiV1.MapGet("/system/status",
    (ISystemStatusService statusService) => TypedResults.Ok(statusService.GetStatus()));

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
