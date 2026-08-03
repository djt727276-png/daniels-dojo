#!/usr/bin/env pwsh
#
# Daniel's Dojo -- local SQL Server 2025 Developer workflow (Windows/PowerShell).
#
#   ./scripts/db.ps1 start | migrate | seed | recreate | stop | status
#
# Everything is namespaced to Daniel's Dojo and uses a non-default host port so it cannot
# collide with any other local SQL Server. The generated password is written outside the
# repository (.local/, git-ignored) and stored in the API's .NET user secrets. No credential
# or connection string is ever written to a tracked file.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'migrate', 'seed', 'recreate', 'stop', 'status')]
    [string] $Command = 'status',

    # Seed profile for 'seed' and 'recreate'.
    [ValidateSet('reference', 'development')]
    [string] $Profile = 'reference',

    # Required acknowledgement for the destructive 'recreate' command.
    [switch] $Confirm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Fixed local target. 'recreate' may never act on anything other than these. ----------
$ContainerName = 'danielsdojo-sql'
$VolumeName    = 'danielsdojo-sql-data'
$DatabaseName  = 'DanielsDojo'
$HostPort      = 14333
$SqlImage      = 'mcr.microsoft.com/mssql/server:2025-latest'

$RootDir       = Split-Path -Parent $PSScriptRoot
$LocalDir      = Join-Path $RootDir '.local'
$PasswordFile  = Join-Path $LocalDir 'sql-password.txt'
$ApiProject    = Join-Path $RootDir 'apps/api/src/DanielsDojo.Api'
$InfraProject  = Join-Path $RootDir 'apps/api/src/DanielsDojo.Infrastructure'

function Invoke-Checked {
    param([Parameter(Mandatory)][scriptblock] $Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

# Docker writes routine warnings to stderr; redirecting it in Windows PowerShell would turn
# those into terminating errors, so probe by exit code with the preference scoped.
function Test-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is required for the local database but was not found on PATH.'
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & docker info 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker is installed but the daemon is not running. Start Docker Desktop and retry.'
        }
    }
    finally { $ErrorActionPreference = $previous }
}

# Creates the development-only password once and reuses it thereafter.
function Get-LocalPassword {
    if (Test-Path $PasswordFile) {
        $existing = (Get-Content $PasswordFile -Raw).Trim()
        if ($existing) { return $existing }
    }

    if (-not (Test-Path $LocalDir)) {
        New-Item -ItemType Directory -Path $LocalDir | Out-Null
    }

    # Cryptographically random, and shaped to satisfy the SQL Server complexity policy.
    # RandomNumberGenerator.Create() works on both Windows PowerShell 5.1 and pwsh 7+;
    # the static Fill overload exists only on the latter.
    $bytes = New-Object 'byte[]' 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $generated = 'Dd1!' + ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '')

    # Written without a byte-order mark. Set-Content -Encoding utf8 emits a BOM on Windows
    # PowerShell; PowerShell strips it on read but Bash does not, so a database created with
    # this script would then fail to authenticate from scripts/db.sh.
    [System.IO.File]::WriteAllText(
        $PasswordFile,
        $generated,
        (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Generated a new development-only SQL password at $PasswordFile (git-ignored)."
    return $generated
}

function Get-ConnectionString {
    param([Parameter(Mandatory)][string] $Password, [string] $Database = $DatabaseName)
    return "Server=localhost,$HostPort;Database=$Database;User Id=sa;Password=$Password;TrustServerCertificate=True;Encrypt=True"
}

function Get-ContainerState {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $state = & docker inspect --format '{{.State.Status}}' $ContainerName 2>$null
        if ($LASTEXITCODE -ne 0) { return 'absent' }
        return ($state | Select-Object -First 1).Trim()
    }
    finally { $ErrorActionPreference = $previous }
}

function Wait-ForSql {
    param([Parameter(Mandatory)][string] $Password)

    Write-Host "Waiting for SQL Server on localhost:$HostPort ..."
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        for ($attempt = 1; $attempt -le 60; $attempt++) {
            & docker exec $ContainerName /opt/mssql-tools18/bin/sqlcmd `
                -S localhost -U sa -P $Password -C -Q 'SELECT 1' 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host 'SQL Server is accepting connections.'
                return
            }
            Start-Sleep -Seconds 2
        }
    }
    finally { $ErrorActionPreference = $previous }

    throw "SQL Server did not become ready within 120 seconds. Inspect 'docker logs $ContainerName'."
}

function Set-UserSecret {
    param([Parameter(Mandatory)][string] $Password)

    $connectionString = Get-ConnectionString -Password $Password
    Invoke-Checked { dotnet user-secrets set 'ConnectionStrings:DanielsDojoDatabase' $connectionString --project $ApiProject | Out-Null }
    Write-Host 'Stored the connection string in the API project .NET user secrets.'
}

function Start-Database {
    Test-Docker
    $password = Get-LocalPassword
    $state = Get-ContainerState

    switch ($state) {
        'absent' {
            Write-Host "Creating container '$ContainerName' on port $HostPort ..."
            Invoke-Checked {
                docker run -d `
                    --name $ContainerName `
                    -e 'ACCEPT_EULA=Y' `
                    -e "MSSQL_SA_PASSWORD=$password" `
                    -e 'MSSQL_PID=Developer' `
                    -p "${HostPort}:1433" `
                    -v "${VolumeName}:/var/opt/mssql" `
                    $SqlImage | Out-Null
            }
        }
        'running' { Write-Host "Container '$ContainerName' is already running." }
        default {
            Write-Host "Starting existing container '$ContainerName' ..."
            Invoke-Checked { docker start $ContainerName | Out-Null }
        }
    }

    Wait-ForSql -Password $password
    Set-UserSecret -Password $password
}

# Runs the explicit database CLI exposed by the API host. Ordinary API startup never
# migrates or seeds; this is the only path that does.
function Invoke-DatabaseCommand {
    param([Parameter(Mandatory)][string[]] $Arguments, [Parameter(Mandatory)][string] $Password)

    $env:ConnectionStrings__DanielsDojoDatabase = Get-ConnectionString -Password $Password
    $env:DANIELSDOJO_DB_CONNECTION = $env:ConnectionStrings__DanielsDojoDatabase
    try {
        Invoke-Checked { dotnet run --project $ApiProject --no-launch-profile -- @Arguments }
    }
    finally {
        Remove-Item Env:ConnectionStrings__DanielsDojoDatabase -ErrorAction SilentlyContinue
        Remove-Item Env:DANIELSDOJO_DB_CONNECTION -ErrorAction SilentlyContinue
    }
}

function Invoke-Migrate {
    $password = Get-LocalPassword
    Write-Host "Applying migrations to '$DatabaseName' ..."
    Invoke-DatabaseCommand -Arguments @('database', 'migrate') -Password $password
}

function Invoke-Seed {
    param([string] $SeedProfile = $Profile)
    $password = Get-LocalPassword
    Write-Host "Seeding '$DatabaseName' with the '$SeedProfile' profile ..."

    # ASPNETCORE_ENVIRONMENT gates the development profile inside the seeder itself.
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    if ($SeedProfile -eq 'development') { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
    try {
        Invoke-DatabaseCommand -Arguments @('database', 'seed', '--profile', $SeedProfile) -Password $password
    }
    finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }
}

function Invoke-Recreate {
    Test-Docker

    Write-Host ''
    Write-Host 'DESTRUCTIVE OPERATION -- the following local target will be deleted and rebuilt:' -ForegroundColor Yellow
    Write-Host "  container : $ContainerName"
    Write-Host "  volume    : $VolumeName  (all data in it is lost)"
    Write-Host "  database  : $DatabaseName on localhost:$HostPort"
    Write-Host ''
    Write-Host 'This command can only ever act on the fixed local target above. It does not' -ForegroundColor Yellow
    Write-Host 'accept a connection string and can never reach a shared or hosted database.' -ForegroundColor Yellow
    Write-Host ''

    if (-not $Confirm) {
        throw "Refusing to recreate without explicit acknowledgement. Rerun with: ./scripts/db.ps1 recreate -Confirm"
    }

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & docker rm -f $ContainerName 2>&1 | Out-Null
        & docker volume rm $VolumeName 2>&1 | Out-Null
    }
    finally { $ErrorActionPreference = $previous }

    Write-Host 'Removed the existing container and volume.'
    Start-Database
    Invoke-Migrate
    Invoke-Seed -SeedProfile $Profile
    Write-Host ''
    Write-Host "Recreate complete: '$DatabaseName' is migrated and seeded with the '$Profile' profile."
}

function Stop-Database {
    Test-Docker
    if ((Get-ContainerState) -eq 'absent') {
        Write-Host "Container '$ContainerName' does not exist; nothing to stop."
        return
    }
    Invoke-Checked { docker stop $ContainerName | Out-Null }
    Write-Host "Stopped '$ContainerName'. Data is retained in volume '$VolumeName'."
}

function Show-Status {
    Write-Host "container : $ContainerName [$(Get-ContainerState)]"
    Write-Host "volume    : $VolumeName"
    Write-Host "database  : $DatabaseName on localhost:$HostPort"
    Write-Host "image     : $SqlImage"
    if (Test-Path $PasswordFile) {
        Write-Host "password  : present at $PasswordFile (git-ignored)"
    }
    else {
        Write-Host 'password  : not yet generated -- run ./scripts/db.ps1 start'
    }

    if ((Get-ContainerState) -eq 'running') {
        $password = Get-LocalPassword
        $env:DANIELSDOJO_DB_CONNECTION = Get-ConnectionString -Password $password
        try {
            Write-Host ''
            Write-Host 'applied migrations:'
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try { & dotnet ef migrations list --project $InfraProject --startup-project $InfraProject --prefix-output 2>&1 | Select-String '^\s*(Applied|Pending)' }
            finally { $ErrorActionPreference = $previous }
        }
        finally { Remove-Item Env:DANIELSDOJO_DB_CONNECTION -ErrorAction SilentlyContinue }
    }
}

switch ($Command) {
    'start'    { Start-Database }
    'migrate'  { Invoke-Migrate }
    'seed'     { Invoke-Seed }
    'recreate' { Invoke-Recreate }
    'stop'     { Stop-Database }
    'status'   { Show-Status }
}
