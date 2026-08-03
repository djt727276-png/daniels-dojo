#!/usr/bin/env pwsh
#
# Daniel's Dojo — Phase 1 local verification (Windows/PowerShell).
# Runs the same logical checks as scripts/verify.sh. Fails immediately on any
# command failure. Safe to rerun; only ignored build/test output is produced.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Fail the script if an external command returns a non-zero exit code.
function Invoke-Checked {
    param([Parameter(Mandatory)][scriptblock] $Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

$RootDir = Split-Path -Parent $PSScriptRoot
Set-Location $RootDir

$BuildConfig = 'Release'
$Solution = 'apps/api/DanielsDojo.slnx'
$WebDir = 'apps/web'

Write-Host '==> [1/10] Confirm required tool versions'
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

Write-Host '==> [2/10] Restore .NET dependencies'
Invoke-Checked { dotnet restore $Solution }

Write-Host '==> [3/10] Build .NET solution (Release, no restore)'
Invoke-Checked { dotnet build $Solution --configuration $BuildConfig --no-restore }

Write-Host '==> [4/10] Run .NET tests (Release, no build)'
Invoke-Checked { dotnet test $Solution --configuration $BuildConfig --no-build }

Write-Host '==> [5/10] Install frontend dependencies (npm ci)'
Push-Location $WebDir
try {
    Invoke-Checked { npm ci }

    Write-Host '==> [6/10] Frontend formatting check'
    Invoke-Checked { npm run format:check }

    Write-Host '==> [7/10] Frontend lint'
    Invoke-Checked { npm run lint }

    Write-Host '==> [8/10] Frontend unit tests (single run, no watch)'
    Invoke-Checked { npm run test:ci }

    Write-Host '==> [9/10] Angular production build'
    Invoke-Checked { npm run build }
}
finally {
    Pop-Location
}

Write-Host '==> [10/10] Build API Docker image (if Docker is available)'
$dockerAvailable = $false
if (Get-Command docker -ErrorAction SilentlyContinue) {
    # Probe the daemon by exit code only. Redirecting a native command's stderr in
    # Windows PowerShell wraps each stderr line in a NativeCommandError, which
    # $ErrorActionPreference = 'Stop' would turn into a terminating error — Docker
    # writes routine warnings to stderr even when it is healthy.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & docker info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $dockerAvailable = $true }
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}
if ($dockerAvailable) {
    Invoke-Checked { docker build -f apps/api/Dockerfile -t daniels-dojo-api:verify . }
}
else {
    Write-Host 'SKIPPED: Docker is not installed or not running — image build not attempted.'
}

Write-Host ''
Write-Host 'Verification completed successfully.'
