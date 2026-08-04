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

echo "==> [1/13] Confirm required tool versions"
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

echo "==> [2/13] Restore repository-local .NET tools (dotnet-ef)"
dotnet tool restore
dotnet ef --version

echo "==> [3/13] Restore .NET dependencies"
dotnet restore "$SOLUTION"

echo "==> [4/13] Verify .NET formatting"
dotnet format "$SOLUTION" --verify-no-changes --no-restore

echo "==> [5/13] Build .NET solution (Release, no restore)"
dotnet build "$SOLUTION" --configuration "$BUILD_CONFIG" --no-restore

# EF checks run without a database: 'migrations list --no-connect' and the model-change
# check both work purely from the compiled model.
echo "==> [6/13] List EF Core migrations"
dotnet ef migrations list --project "$INFRA_PROJECT" --startup-project "$INFRA_PROJECT" \
  --no-connect --no-build --configuration "$BUILD_CONFIG"

echo "==> [7/13] Confirm no pending model changes"
dotnet ef migrations has-pending-model-changes --project "$INFRA_PROJECT" \
  --startup-project "$INFRA_PROJECT" --no-build --configuration "$BUILD_CONFIG"

echo "==> [8/13] Generate the idempotent migration script (verification artifact)"
mkdir -p "$(dirname "$SCRIPT_OUTPUT")"
dotnet ef migrations script --idempotent --project "$INFRA_PROJECT" \
  --startup-project "$INFRA_PROJECT" --no-build --configuration "$BUILD_CONFIG" \
  --output "$SCRIPT_OUTPUT"
echo "    wrote $SCRIPT_OUTPUT (git-ignored)"

# Covers the Phase 3 authentication and authorization suites too. Those issue locally signed
# JWTs, so no Entra tenant or internet access is ever required.
echo "==> [9/13] Run .NET tests (Release, no build) — includes real SQL Server"
dotnet test "$SOLUTION" --configuration "$BUILD_CONFIG" --no-build

echo "==> [10/13] Install frontend dependencies (npm ci)"
( cd "$WEB_DIR" && npm ci )

echo "==> [11/13] Frontend formatting check and lint"
( cd "$WEB_DIR" && npm run format:check )
( cd "$WEB_DIR" && npm run lint )

echo "==> [12/13] Frontend unit tests and production build"
( cd "$WEB_DIR" && npm run test:ci )
( cd "$WEB_DIR" && npm run build )

# Cheap static guard against the one configuration mistake that would matter most: a
# production build that selects the Development sign-in harness.
echo "==> [13/14] Scan for Development authentication in production configuration"
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

# Docker availability was already asserted before the test step, so this always runs.
echo "==> [14/14] Build API Docker image"
docker build -f apps/api/Dockerfile -t daniels-dojo-api:verify .

echo ""
echo "Verification completed successfully."
