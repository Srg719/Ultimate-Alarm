param(
    [switch]$Cli,
    [switch]$NonInteractive,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$BinDir = if (Test-Path (Join-Path $Root "UltimateAlarm.exe")) { $Root } else { Join-Path $Root "bin" }
$AppExe = Join-Path $BinDir "UltimateAlarm.exe"
$CliExe = Join-Path $BinDir "ua.exe"

if (-not $NoBuild -and (Test-Path (Join-Path $Root "build.ps1")) -and (-not (Test-Path $AppExe) -or -not (Test-Path $CliExe))) {
    & (Join-Path $Root "build.ps1")
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Requesting Administrator permissions via UAC..."
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($Cli) { $argList += " -Cli" }
    if ($NonInteractive) { $argList += " -NonInteractive" }
    if ($NoBuild) { $argList += " -NoBuild" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    exit 0
}

if ($NonInteractive) {
    & $CliExe install
    exit $LASTEXITCODE
}

if ($Cli) {
    & $CliExe setup
    exit $LASTEXITCODE
}

Write-Host "Launching UltimateAlarm Setup Wizard..."
Start-Process -FilePath $AppExe -ArgumentList "--setup" -WorkingDirectory $BinDir
