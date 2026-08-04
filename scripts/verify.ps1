#!/usr/bin/env pwsh
#
# Daniel's Dojo -- local verification (Windows/PowerShell).
# Runs the same logical checks as scripts/verify.sh. Fails immediately on any
# command failure. Safe to rerun; only ignored build/test output is produced.
#
# Docker is REQUIRED: the database tests run real SQL Server 2025 through
# Testcontainers. They are never silently skipped -- if Docker is unavailable this
# script fails and says so.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Fail the script if an external command returns a non-zero exit code.
#
# Exit code is the only success signal. Windows PowerShell wraps anything a native command
# writes to stderr in a NativeCommandError whenever the caller redirects the pipeline, and
# $ErrorActionPreference = 'Stop' would turn that into a failure even for a tool that merely
# logged a warning and returned 0. Scoping the preference keeps the check honest.
function Invoke-Checked {
    param([Parameter(Mandatory)][scriptblock] $Command)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

$RootDir = Split-Path -Parent $PSScriptRoot
Set-Location $RootDir

$BuildConfig = 'Release'
$Solution = 'apps/api/DanielsDojo.slnx'
$WebDir = 'apps/web'
$InfraProject = 'apps/api/src/DanielsDojo.Infrastructure'
$ScriptOutput = 'artifacts/database/InitialPlatformSchema.idempotent.sql'

# Probe the Docker daemon by exit code only. Redirecting a native command's stderr in
# Windows PowerShell wraps each line in a NativeCommandError, which $ErrorActionPreference
# would turn into a terminating error -- Docker writes routine warnings to stderr even when
# it is perfectly healthy.
function Test-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & docker info 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    }
    finally { $ErrorActionPreference = $previous }
}

Write-Host '==> [1/15] Confirm required tool versions'
Invoke-Checked { node --version }
Invoke-Checked { npm --version }
Invoke-Checked { dotnet --version }
# Parse the version text in PowerShell rather than passing a quoted JS expression
# to `node -p`: PowerShell strips the inner double quotes when building the native
# command line, which makes the expression a syntax error.
$nodeMajor = ((& node --version).TrimStart('v') -split '\.')[0]
$dotnetMajor = ((& dotnet --version) -split '\.')[0]
if ($nodeMajor -ne '24') {
    throw "Node.js 24.x is required (see .nvmrc); found $(node --version)."
}
if ($dotnetMajor -ne '10') {
    throw ".NET SDK 10.x is required (see global.json); found $(dotnet --version)."
}

# The database tests are not optional. Fail here rather than appearing to pass later.
if (-not (Test-DockerAvailable)) {
    throw 'Docker is required: the database tests run real SQL Server 2025 via Testcontainers. Start Docker Desktop and rerun.'
}

Write-Host '==> [2/15] Restore repository-local .NET tools (dotnet-ef)'
Invoke-Checked { dotnet tool restore }
Invoke-Checked { dotnet ef --version }

Write-Host '==> [3/15] Restore .NET dependencies'
Invoke-Checked { dotnet restore $Solution }

Write-Host '==> [4/15] Verify .NET formatting'
Invoke-Checked { dotnet format $Solution --verify-no-changes --no-restore }

Write-Host '==> [5/15] Build .NET solution (Release, no restore)'
Invoke-Checked { dotnet build $Solution --configuration $BuildConfig --no-restore }

# EF checks run without a database: 'migrations list --no-connect' and the model-change
# check both work purely from the compiled model.
Write-Host '==> [6/15] List EF Core migrations'
Invoke-Checked { dotnet ef migrations list --project $InfraProject --startup-project $InfraProject --no-connect --no-build --configuration $BuildConfig }

Write-Host '==> [7/15] Confirm no pending model changes'
Invoke-Checked { dotnet ef migrations has-pending-model-changes --project $InfraProject --startup-project $InfraProject --no-build --configuration $BuildConfig }

Write-Host '==> [8/15] Generate the idempotent migration script (verification artifact)'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ScriptOutput) | Out-Null
Invoke-Checked { dotnet ef migrations script --idempotent --project $InfraProject --startup-project $InfraProject --no-build --configuration $BuildConfig --output $ScriptOutput }
Write-Host "    wrote $ScriptOutput (git-ignored)"

# Covers the Phase 3 authentication and authorization suites too. Those issue locally signed
# JWTs, so no Entra tenant or internet access is ever required.
Write-Host '==> [9/15] Run .NET tests (Release, no build) -- includes real SQL Server'
Invoke-Checked { dotnet test $Solution --configuration $BuildConfig --no-build }

Write-Host '==> [10/15] Install frontend dependencies (npm ci)'
Push-Location $WebDir
try {
    Invoke-Checked { npm ci }

    Write-Host '==> [11/15] Frontend formatting check'
    Invoke-Checked { npm run format:check }

    Write-Host '     Frontend lint'
    Invoke-Checked { npm run lint }

    Write-Host '==> [12/15] Frontend unit tests (single run, no watch)'
    Invoke-Checked { npm run test:ci }

    Write-Host '     Angular production build'
    Invoke-Checked { npm run build }
}
finally {
    Pop-Location
}

# Cheap static guard against the one configuration mistake that would matter most: a
# production build that selects the Development sign-in harness.
Write-Host '==> [13/15] Scan for Development authentication in production configuration'
$productionEnvironment = Get-Content 'apps/web/src/environments/environment.production.ts' -Raw
if ($productionEnvironment -notmatch "mode:\s*'entra'") {
    throw 'apps/web/src/environments/environment.production.ts must pin the auth mode to entra.'
}
if ($productionEnvironment -notmatch 'production:\s*true') {
    throw 'apps/web/src/environments/environment.production.ts must set production: true.'
}
$apiSettings = Get-Content 'apps/api/src/DanielsDojo.Api/appsettings.json' -Raw
if ($apiSettings -match '"Development"\s*:\s*\{[^}]*"Enabled"\s*:\s*true') {
    throw 'appsettings.json must not enable the Development authentication harness.'
}
Write-Host '    production configuration excludes the Development auth harness'

# Phase 4 boundaries, asserted cheaply rather than trusted. A spoofable partition key would
# silently turn every community rate limit into no limit at all, and a payment SDK would mean
# the pricing screens had quietly stopped being database-only.
Write-Host '==> [14/15] Scan for spoofable rate-limit partitions and payment SDK creep'
$rateLimiting = Get-Content 'apps/api/src/DanielsDojo.Api/Common/RateLimiting.cs' -Raw
foreach ($header in @('X-Forwarded-For', 'RemoteIpAddress', 'X-Real-IP')) {
    if ($rateLimiting -match [regex]::Escape($header)) {
        throw "RateLimiting.cs must not partition on $header; use the local application user id."
    }
}
if ($rateLimiting -notmatch 'user\.UserId') {
    throw 'RateLimiting.cs must partition authenticated limits by the local application user id.'
}
$packages = Get-Content 'Directory.Packages.props' -Raw
foreach ($package in @('Stripe.net', 'Azure.Storage.Blobs', 'Mux')) {
    if ($packages -match [regex]::Escape($package)) {
        throw "$package belongs to a later phase and must not be referenced yet."
    }
}
Write-Host '    limits are partitioned by local user id; no payment or media SDK is referenced'

# Docker availability was already asserted before the test step, so this always runs.
Write-Host '==> [15/15] Build API Docker image'
Invoke-Checked { docker build -f apps/api/Dockerfile -t daniels-dojo-api:verify . }

Write-Host ''
Write-Host 'Verification completed successfully.'
