#!/usr/bin/env bash
#
# Daniel's Dojo — Phase 1 local verification (Linux/macOS).
# Runs the same logical checks as scripts/verify.ps1. Fails immediately on any
# command failure. Safe to rerun; only ignored build/test output is produced.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

BUILD_CONFIG="Release"
SOLUTION="apps/api/DanielsDojo.slnx"
WEB_DIR="apps/web"

echo "==> [1/10] Confirm required tool versions"
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

echo "==> [2/10] Restore .NET dependencies"
dotnet restore "$SOLUTION"

echo "==> [3/10] Build .NET solution (Release, no restore)"
dotnet build "$SOLUTION" --configuration "$BUILD_CONFIG" --no-restore

echo "==> [4/10] Run .NET tests (Release, no build)"
dotnet test "$SOLUTION" --configuration "$BUILD_CONFIG" --no-build

echo "==> [5/10] Install frontend dependencies (npm ci)"
( cd "$WEB_DIR" && npm ci )

echo "==> [6/10] Frontend formatting check"
( cd "$WEB_DIR" && npm run format:check )

echo "==> [7/10] Frontend lint"
( cd "$WEB_DIR" && npm run lint )

echo "==> [8/10] Frontend unit tests (single run, no watch)"
( cd "$WEB_DIR" && npm run test:ci )

echo "==> [9/10] Angular production build"
( cd "$WEB_DIR" && npm run build )

echo "==> [10/10] Build API Docker image (if Docker is available)"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  docker build -f apps/api/Dockerfile -t daniels-dojo-api:verify .
else
  echo "SKIPPED: Docker is not installed or not running — image build not attempted."
fi

echo ""
echo "Verification completed successfully."
