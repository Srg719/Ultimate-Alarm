param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinDir = if (Test-Path (Join-Path $Root "ua.exe")) { $Root } else { Join-Path $Root "bin" }
$CliExe = Join-Path $BinDir "ua.exe"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($RemoveData) { $argList += " -RemoveData" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    exit 0
}

Get-Process -Name "UltimateAlarm" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $CliExe) {
    & $CliExe uninstall
}

if ($RemoveData) {
    $dataDir = Join-Path $Root "data"
    $logsDir = Join-Path $Root "logs"
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force $dataDir }
    if (Test-Path $logsDir) { Remove-Item -Recurse -Force $logsDir }
    Write-Host "Removed local data and logs folders."
}

Write-Host "UltimateAlarm uninstall complete."
