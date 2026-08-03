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
- **Docker** — **required**. It runs the local SQL Server 2025 Developer database, the
  database integration tests (real SQL Server via Testcontainers), and the API image build.

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

## Database: one-command local bootstrap

Start Docker, then run **one** command. It creates the SQL Server 2025 container, generates a
development-only password outside the repository, stores the connection string in .NET user
secrets, applies migrations, and seeds.

```powershell
# Windows PowerShell
./scripts/db.ps1 recreate -Profile development -Confirm
```

```bash
# Linux / macOS
./scripts/db.sh recreate development --confirm
```

Day-to-day commands (`start`, `migrate`, `seed`, `recreate`, `stop`, `status`) and the exact
container, volume, database, and port are documented in
[`infra/local/README.md`](infra/local/README.md). **No password or connection string is ever
committed** — the generated credential lives in the git-ignored `.local/` directory.

`recreate` is destructive. It only ever acts on the fixed local Daniel's Dojo container,
volume, and database, prints that target, and refuses to run without explicit confirmation.

Migrations and seeding are always explicit operator actions — the API never migrates or seeds
during ordinary startup:

```bash
dotnet run --project apps/api/src/DanielsDojo.Api -- database migrate
dotnet run --project apps/api/src/DanielsDojo.Api -- database seed --profile reference
```

The `development` seed profile is refused unless the host environment is exactly
`Development`. See [`docs/architecture/phase-2-data-design.md`](docs/architecture/phase-2-data-design.md)
for the schema, invariants, and seed contents.

## Authentication

Customer sign-up and sign-in run through **Entra External ID**; Daniel's Dojo stores no
password or password hash. Application permissions come from the local database, never from a
token claim, and the API is the authorization boundary.

Sign-in ships **disabled with placeholder configuration** — no tenant or client ID is invented.
Supply the public identifiers per environment to switch it on. Setup, claim mapping, the
401-versus-403 contract, the audited administrator-grant command, and the manual live
acceptance sequence are documented in
[`docs/architecture/phase-3-authentication.md`](docs/architecture/phase-3-authentication.md).

```bash
# Promote a customer to administrator (explicit, audited, no HTTP route exists for this)
dotnet run --project apps/api/src/DanielsDojo.Api -- \
  identity grant-admin --user-id <internal-guid> --reason "Founding administrator" --confirm
```

Afterwards, add the same external identity to the Entra `DanielsDojo-Admins-MFA` group so
administrator sign-in is MFA-enforced. That step is manual in Phase 3.

## Backend: install, build, test, run

Run from the repository root.

```bash
# Restore the pinned dotnet-ef tool once per clone
dotnet tool restore

# Restore, build (Release), and test the solution
dotnet restore apps/api/DanielsDojo.slnx
dotnet build   apps/api/DanielsDojo.slnx -c Release --no-restore
dotnet test    apps/api/DanielsDojo.slnx -c Release --no-build   # needs Docker

# Run the API (HTTPS profile) for local development
dotnet run --project apps/api/src/DanielsDojo.Api --launch-profile https
```

`dotnet test` starts a disposable SQL Server 2025 container through Testcontainers. The
database tests are never skipped silently — without Docker they fail.

With the `https` profile the API listens on **https://localhost:7148** (and
**http://localhost:5148**). Useful URLs:

- System status: `https://localhost:7148/api/v1/system/status`
- Liveness: `https://localhost:7148/health/live` — independent of SQL, so a database outage
  never causes an orchestrator to kill a healthy process.
- Readiness: `https://localhost:7148/health/ready` — healthy only when the configured database
  is reachable **and** fully migrated.
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

Both scripts run the same 13 logical checks — tool versions, `dotnet tool restore`, .NET
restore, .NET format verification, Release build, EF migrations list, the pending-model-change
check, idempotent script generation, .NET tests (including real SQL Server), `npm ci`, format
check, lint, frontend tests, Angular production build, and the API image build. They fail
immediately on any error and are safe to rerun.

**Docker must be running.** The scripts assert it up front and fail with a clear message
rather than skipping the database tests.

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

## Excluded from Phase 3

Phase 3 adds **authentication and application authorization only**. This repository still does
**not** contain, and Phase 3 intentionally does not implement:

- passwords, password hashes, ASP.NET Core Identity local credentials, or social login.
  Credentials live entirely in Entra External ID.
- Stripe SDK calls, Checkout, portal sessions, webhook HTTP endpoints, subscriptions, trials,
  refunds, or entitlement evaluation. The commerce tables exist so their invariants hold from
  the first row written; nothing reads or writes them yet.
- course or catalog CRUD endpoints, and no Angular catalog or student learning screens. The
  only admin surface is a smoke endpoint plus a route that proves the role gate works.
- Mux, Blob Storage, email delivery, Azure resources, Bicep resources, or any deployment stage.
- trials, coupons, annual plans, tiers, bundles, certificates, bookmarks, or background jobs.
- speculative infrastructure (Redis, queues, microservices, MediatR, AutoMapper,
  FluentValidation) and generic repository or unit-of-work abstractions.

The CI pipeline remains validation-only: it builds, tests, and produces a migration script
artifact, and never deploys or runs migrations against a shared database.
