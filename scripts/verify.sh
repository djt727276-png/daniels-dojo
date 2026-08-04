#!/usr/bin/env bash
#
# Daniel's Dojo — local verification (Linux/macOS).
# Runs the same logical checks as scripts/verify.ps1. Fails immediately on any
# command failure. Safe to rerun; only ignored build/test output is produced.
#
# Docker is REQUIRED: the database tests run real SQL Server 2025 through
# Testcontainers. They are never silently skipped — if Docker is unavailable this
# script fails and says so.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

BUILD_CONFIG="Release"
SOLUTION="apps/api/DanielsDojo.slnx"
WEB_DIR="apps/web"
INFRA_PROJECT="apps/api/src/DanielsDojo.Infrastructure"
SCRIPT_OUTPUT="artifacts/database/InitialPlatformSchema.idempotent.sql"

echo "==> [1/15] Confirm required tool versions"
node --version
npm --version
dotnet --version
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]')"
DOTNET_MAJOR="$(dotnet --version | cut -d. -f1)"
if [ "$NODE_MAJOR" != "24" ]; then
  echo "ERROR: Node.js 24.x is required (see .nvmrc); found $(node --version)." >&2
  exit 1
fi
if [ "$DOTNET_MAJOR" != "10" ]; then
  echo "ERROR: .NET SDK 10.x is required (see global.json); found $(dotnet --version)." >&2
  exit 1
fi

# The database tests are not optional. Fail here rather than appearing to pass later.
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  echo "ERROR: Docker is required: the database tests run real SQL Server 2025 via Testcontainers. Start Docker and rerun." >&2
  exit 1
fi

echo "==> [2/15] Restore repository-local .NET tools (dotnet-ef)"
dotnet tool restore
dotnet ef --version

echo "==> [3/15] Restore .NET dependencies"
dotnet restore "$SOLUTION"

echo "==> [4/15] Verify .NET formatting"
dotnet format "$SOLUTION" --verify-no-changes --no-restore

echo "==> [5/15] Build .NET solution (Release, no restore)"
dotnet build "$SOLUTION" --configuration "$BUILD_CONFIG" --no-restore

# EF checks run without a database: 'migrations list --no-connect' and the model-change
# check both work purely from the compiled model.
echo "==> [6/15] List EF Core migrations"
dotnet ef migrations list --project "$INFRA_PROJECT" --startup-project "$INFRA_PROJECT" \
  --no-connect --no-build --configuration "$BUILD_CONFIG"

echo "==> [7/15] Confirm no pending model changes"
dotnet ef migrations has-pending-model-changes --project "$INFRA_PROJECT" \
  --startup-project "$INFRA_PROJECT" --no-build --configuration "$BUILD_CONFIG"

echo "==> [8/15] Generate the idempotent migration script (verification artifact)"
mkdir -p "$(dirname "$SCRIPT_OUTPUT")"
dotnet ef migrations script --idempotent --project "$INFRA_PROJECT" \
  --startup-project "$INFRA_PROJECT" --no-build --configuration "$BUILD_CONFIG" \
  --output "$SCRIPT_OUTPUT"
echo "    wrote $SCRIPT_OUTPUT (git-ignored)"

# Covers the Phase 3 authentication and authorization suites too. Those issue locally signed
# JWTs, so no Entra tenant or internet access is ever required.
echo "==> [9/15] Run .NET tests (Release, no build) — includes real SQL Server"
dotnet test "$SOLUTION" --configuration "$BUILD_CONFIG" --no-build

echo "==> [10/15] Install frontend dependencies (npm ci)"
( cd "$WEB_DIR" && npm ci )

echo "==> [11/15] Frontend formatting check and lint"
( cd "$WEB_DIR" && npm run format:check )
( cd "$WEB_DIR" && npm run lint )

echo "==> [12/15] Frontend unit tests and production build"
( cd "$WEB_DIR" && npm run test:ci )
( cd "$WEB_DIR" && npm run build )

# Cheap static guard against the one configuration mistake that would matter most: a
# production build that selects the Development sign-in harness.
echo "==> [13/15] Scan for Development authentication in production configuration"
if ! grep -qE "mode:[[:space:]]*'entra'" apps/web/src/environments/environment.production.ts; then
  echo "ERROR: environment.production.ts must pin the auth mode to entra." >&2
  exit 1
fi
if ! grep -qE 'production:[[:space:]]*true' apps/web/src/environments/environment.production.ts; then
  echo "ERROR: environment.production.ts must set production: true." >&2
  exit 1
fi
if grep -Pzoq '"Development"\s*:\s*\{[^}]*"Enabled"\s*:\s*true' \
     apps/api/src/DanielsDojo.Api/appsettings.json; then
  echo "ERROR: appsettings.json must not enable the Development authentication harness." >&2
  exit 1
fi
echo "    production configuration excludes the Development auth harness"

# Phase 4 boundaries, asserted cheaply rather than trusted. A spoofable partition key would
# silently turn every community rate limit into no limit at all, and a payment SDK would mean
# the pricing screens had quietly stopped being database-only.
echo "==> [14/15] Scan for spoofable rate-limit partitions and payment SDK creep"
for header in 'X-Forwarded-For' 'RemoteIpAddress' 'X-Real-IP'; do
  if grep -qF "$header" apps/api/src/DanielsDojo.Api/Common/RateLimiting.cs; then
    echo "ERROR: RateLimiting.cs must not partition on $header; use the local application user id." >&2
    exit 1
  fi
done
if ! grep -q 'user\.UserId' apps/api/src/DanielsDojo.Api/Common/RateLimiting.cs; then
  echo "ERROR: RateLimiting.cs must partition authenticated limits by the local application user id." >&2
  exit 1
fi
for package in 'Stripe.net' 'Azure.Storage.Blobs' 'Mux'; do
  if grep -qF "$package" Directory.Packages.props; then
    echo "ERROR: $package belongs to a later phase and must not be referenced yet." >&2
    exit 1
  fi
done
echo "    limits are partitioned by local user id; no payment or media SDK is referenced"

# Docker availability was already asserted before the test step, so this always runs.
echo "==> [15/15] Build API Docker image"
docker build -f apps/api/Dockerfile -t daniels-dojo-api:verify .

echo ""
echo "Verification completed successfully."
