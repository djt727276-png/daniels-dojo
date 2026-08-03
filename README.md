# Daniel's Dojo

Phase 1 — the production-quality **walking skeleton** on which later phases build.
This phase delivers a layered ASP.NET Core backend, an Angular frontend, one real
frontend-to-backend system-status vertical slice, tests, an API container build, a
validation-only CI pipeline, and cross-platform verification scripts.

Phase 1 deliberately contains **no commerce, courses, persistence, or
authentication**. Those are future work (see “Excluded from Phase 1” below).

## Architecture

The backend is a layered modular monolith following clean-architecture dependency
rules. Dependencies point inward only:

```
Api            ->  Application
Api            ->  Infrastructure
Infrastructure ->  Application
Infrastructure ->  Domain
Application    ->  Domain
Domain         ->  (nothing)
```

- **Domain** — future core business rules; references no other project. Phase 1
  holds only an assembly marker.
- **Application** — use cases and application contracts; references Domain only.
  It owns the immutable `SystemStatus` contract, the `ISystemStatusService`, and
  the `IApplicationEnvironment` abstraction. It has no dependency on ASP.NET Core,
  databases, Azure, or UI concerns and uses only base-class-library abstractions
  such as `System.TimeProvider`.
- **Infrastructure** — future external integrations and persistence; references
  Application and Domain. Phase 1 keeps it minimal (no EF Core, no provider SDKs).
- **Api** — the ASP.NET Core host; references Application and Infrastructure. It
  wires the host-environment bridge, registers `TimeProvider.System`, ProblemDetails,
  health checks, and development-only OpenAPI, and exposes the endpoints below.

### The system-status vertical slice

The Angular Home page renders an **API status card** driven by a real HTTP request
to `GET /api/v1/system/status`. The card shows explicit **loading**, **healthy**,
and **unavailable** states, with a **retry** control when the API is unavailable.
The browser calls the relative path `/api/v1/system/status`; the Angular dev-server
proxy (`apps/web/proxy.conf.json`) forwards `/api/*` to the local API.

## Prerequisites

These versions are locked for Phase 1:

- **Node.js 24.15.0** (see `.nvmrc`) with the bundled npm.
- **.NET SDK 10.0.302** (see `global.json`; roll-forward stays within the 10.0
  servicing line).
- **Angular CLI / Angular 22.1.x** (declared in `apps/web/package.json`). Unit
  tests use the Angular 22 default `@angular/build:unit-test` builder with the
  Vitest runner on jsdom; the application uses zoneless change detection.
- **Docker** (optional locally) — only needed to build the API image.

## Repository structure

```
/
  .editorconfig .gitattributes .gitignore .nvmrc
  global.json Directory.Build.props Directory.Packages.props
  apps/
    web/    Angular 22 application (system-status slice, tests, proxy)
    api/    ASP.NET Core solution (Domain/Application/Infrastructure/Api + tests)
  infra/    Bicep boundary only — resources deferred (see infra/README.md)
  pipelines/ci.yml   Azure DevOps validation pipeline
  scripts/  verify.sh / verify.ps1
```

## Backend: install, build, test, run

Run from the repository root.

```bash
# Restore, build (Release), and test the solution
dotnet restore apps/api/DanielsDojo.slnx
dotnet build   apps/api/DanielsDojo.slnx -c Release --no-restore
dotnet test    apps/api/DanielsDojo.slnx -c Release --no-build

# Run the API (HTTPS profile) for local development
dotnet run --project apps/api/src/DanielsDojo.Api --launch-profile https
```

With the `https` profile the API listens on **https://localhost:7148** (and
**http://localhost:5148**). Useful URLs:

- System status: `https://localhost:7148/api/v1/system/status`
- Liveness: `https://localhost:7148/health/live`
- Readiness: `https://localhost:7148/health/ready`
- OpenAPI (Development only): `https://localhost:7148/openapi/v1.json`

## Frontend: install, run with the API proxy

`apps/web/package-lock.json` is committed, so installs are reproducible:

```bash
cd apps/web
npm ci               # install exactly the locked dependency versions
npm start            # ng serve with the /api proxy (proxy.conf.json)
```

The UI is served at **http://localhost:4200**. Start the API first (above) so the
Home page status card can reach `/api/v1/system/status` through the proxy and show
the live **healthy** state; stop the API to see the **unavailable** state and the
**Retry** control.

Other frontend commands (run in `apps/web`):

```bash
npm run build         # production build
npm run test:ci       # unit tests once (Vitest, jsdom), no watch
npm run lint          # ESLint
npm run format:check  # Prettier check
```

## Verification scripts

Both scripts run the same logical checks — tool versions, .NET restore/build/test
(Release), `npm ci`, format check, lint, unit tests, Angular production build, and
(when Docker is available) the API image build. They fail immediately on any error
and are safe to rerun.

```bash
# Linux / macOS
./scripts/verify.sh
```

```powershell
# Windows PowerShell
./scripts/verify.ps1
```

## API container

```bash
# Build from the repository root (context must include the root MSBuild props)
docker build -f apps/api/Dockerfile -t daniels-dojo-api .
docker run --rm -p 8080:8080 daniels-dojo-api
# then: curl http://localhost:8080/health/live
```

The image uses the official .NET 10 images, publishes in Release, runs as the
non-root image user, listens on port 8080, and contains no secrets.

## Local HTTPS certificate troubleshooting

Local development uses the ASP.NET Core developer certificate, and the Angular
proxy is configured with `"secure": false` so it forwards to the HTTPS API without
rejecting that certificate. If the browser or `dotnet run` reports an untrusted or
missing local certificate:

```bash
dotnet dev-certs https --trust      # trust the local dev certificate
# If problems persist, reset and re-trust:
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

On Linux, `--trust` support varies by distribution; you may need to trust the
exported certificate manually for your browser. Do **not** work around certificate
issues by disabling HTTPS security globally (for example, do not set
`NODE_TLS_REJECT_UNAUTHORIZED=0`); the proxy’s scoped `"secure": false` is the only
certificate relaxation needed, and it applies to local development only.

## Excluded from Phase 1

Phase 1 intentionally does **not** implement, and this repository does not contain:
EF Core or any database, SQL/migrations/entities/repositories/seed data;
authentication, users, roles, or authorization (no Entra External ID, MSAL, or
ASP.NET auth); payments or commerce (no Stripe, prices, subscriptions, webhooks);
video or media (no Mux, Blob Storage, course resources); course/lesson/progress/
entitlement/enrollment/admin models; Bicep resources or Azure deployment; any
deployment pipeline; and speculative infrastructure (Redis, queues, microservices,
MediatR, AutoMapper, FluentValidation). No Azure deployment, authentication, SQL,
payments, or course features exist yet.
