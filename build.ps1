<#
.SYNOPSIS
    Builds AtasStrategies.dll and copies it into the ATAS Strategies folder.

.DESCRIPTION
    1. Optionally runs the unit tests (-Test).
    2. Clears bin\<Configuration> so the DLL copied is guaranteed to come from this build.
    3. Runs "dotnet build" on AtasStrategies.csproj.
    4. Copies the newly built AtasStrategies.dll to the ATAS Strategies folder.
    Stops without copying if the tests or the build fail.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Test
.EXAMPLE
    .\build.ps1 -Destination "D:\ATAS\Strategies" -Configuration Debug
#>
param(
    [string]$Destination = "C:\Users\ADMIN\AppData\Roaming\ATAS\Strategies",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$Test
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "AtasStrategies.csproj"
$dllName = "AtasStrategies.dll"
$binFolder = Join-Path $root "bin\$Configuration"

function Fail([string]$message, [int]$code = 1) {
    Write-Host ""
    Write-Host "FAILED: $message" -ForegroundColor Red
    exit $code
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "dotnet was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
}

if ($Test) {
    Write-Host "Running unit tests..." -ForegroundColor Cyan
    dotnet test (Join-Path $root "Tests\AtasStrategies.Tests.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { Fail "Unit tests failed. Nothing was copied." $LASTEXITCODE }
}

# Remove the previous output so an old DLL can never be copied by mistake.
if (Test-Path $binFolder) {
    Write-Host "Clearing $binFolder" -ForegroundColor Cyan
    try {
        Remove-Item -Path $binFolder -Recurse -Force
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        Fail "Could not clear $binFolder. A program is probably using files in it (close ATAS and any editor showing the output) and run this again."
    }
}

Write-Host "Building $project ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { Fail "Build failed. Nothing was copied." $LASTEXITCODE }

# Find the DLL this build produced (works whatever the target framework folder is called).
$dll = Get-ChildItem -Path $binFolder -Filter $dllName -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $dll) { Fail "Build succeeded but $dllName was not found under bin\$Configuration." }

if (-not (Test-Path $Destination)) {
    Write-Host "Creating $Destination" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
}

Write-Host "Copying $($dll.FullName)" -ForegroundColor Cyan
Write-Host "     to $Destination" -ForegroundColor Cyan
try {
    Copy-Item -Path $dll.FullName -Destination $Destination -Force
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Fail "Could not copy the DLL. ATAS is probably using it: close ATAS (or remove the strategies from all charts) and run this again."
}

$copied = Get-Item (Join-Path $Destination $dllName)
Write-Host ""
Write-Host "Done: $($copied.FullName)" -ForegroundColor Green
Write-Host "      $($copied.Length) bytes, built $($dll.LastWriteTime)" -ForegroundColor Green
Write-Host "In ATAS, open the strategies window and click the blinking refresh button (or restart ATAS)."
