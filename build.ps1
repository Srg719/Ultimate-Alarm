param(
    [switch]$Clean,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinDir = Join-Path $Root "bin"
$DistDir = Join-Path $Root "dist"
$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $Csc)) {
    throw "C# compiler not found at $Csc"
}

if ($Clean -and (Test-Path $BinDir)) {
    Remove-Item -Recurse -Force $BinDir
}
New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root "data") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Root "logs") | Out-Null

$IconPath = Join-Path $Root "assets\UltimateAlarm.ico"
$ManifestPath = Join-Path $Root "src\Common\app.manifest"
$AsmInfo = Join-Path $Root "src\Common\AssemblyInfo.cs"

$CoreSources = @(
    (Join-Path $Root "src\Core\Models.cs"),
    (Join-Path $Root "src\Core\AlarmStore.cs"),
    (Join-Path $Root "src\Core\NativeMethods.cs"),
    (Join-Path $Root "src\Core\AudioRouter.cs"),
    (Join-Path $Root "src\Core\SecurityHelper.cs"),
    (Join-Path $Root "src\Core\PowerWakeManager.cs"),
    (Join-Path $Root "src\Core\IpcProtocol.cs")
)

$ServiceSources = @(
    (Join-Path $Root "src\Service\AlarmService.cs"),
    (Join-Path $Root "src\Service\Program.cs")
)

$UiSources = @(
    (Join-Path $Root "src\UI\Theme.cs"),
    (Join-Path $Root "src\UI\DarkChromeForm.cs"),
    (Join-Path $Root "src\UI\ModernControls.cs"),
    (Join-Path $Root "src\UI\Dialogs.cs"),
    (Join-Path $Root "src\UI\SetupWizardForm.cs"),
    (Join-Path $Root "src\UI\MainForm.cs"),
    (Join-Path $Root "src\UI\Program.cs")
)

$AppExe = Join-Path $BinDir "UltimateAlarm.exe"
$CliExe = Join-Path $BinDir "ua.exe"

Write-Host "Building standalone UltimateAlarm.exe (GUI + Service + Audio Worker)..."
$appArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/warn:4",
    "/out:$AppExe",
    "/win32icon:$IconPath",
    "/win32manifest:$ManifestPath",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Runtime.Serialization.dll",
    "/r:System.ServiceProcess.dll",
    $AsmInfo
) + $CoreSources + $ServiceSources + $UiSources

& $Csc @appArgs
if ($LASTEXITCODE -ne 0) { throw "UltimateAlarm.exe build failed." }

Write-Host "Building standalone ua.exe (CLI)..."
$cliArgs = @(
    "/nologo",
    "/target:exe",
    "/platform:x64",
    "/optimize+",
    "/warn:4",
    "/out:$CliExe",
    "/win32icon:$IconPath",
    "/win32manifest:$ManifestPath",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Runtime.Serialization.dll",
    "/r:System.ServiceProcess.dll",
    $AsmInfo
) + $CoreSources + @((Join-Path $Root "src\Cli\Program.cs"))

& $Csc @cliArgs
if ($LASTEXITCODE -ne 0) { throw "ua.exe build failed." }

# Copy standalone single-file exe to dist\ and repo root for direct launch
Copy-Item $AppExe (Join-Path $DistDir "UltimateAlarm.exe") -Force
try {
    Copy-Item $AppExe (Join-Path $Root "UltimateAlarm.exe") -Force
} catch {
    Write-Warning "Could not overwrite root UltimateAlarm.exe (close it if currently running): $($_.Exception.Message)"
}
Write-Host "Build succeeded -> $BinDir"
Write-Host "Standalone release executable -> $(Join-Path $DistDir 'UltimateAlarm.exe')"

if ($Package) {
    $StageDir = Join-Path $DistDir "UltimateAlarm-win-x64"
    $ZipPath = Join-Path $DistDir "UltimateAlarm-win-x64.zip"
    if (Test-Path $StageDir) { Remove-Item -Recurse -Force $StageDir }
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
    Copy-Item $AppExe $StageDir
    Copy-Item $CliExe $StageDir
    Copy-Item (Join-Path $Root "README.md") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "SECURITY.md") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "LICENSE") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "install.ps1") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "uninstall.ps1") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "Apply-Update.ps1") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "Apply-Update.bat") $StageDir -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "Start-UltimateAlarm.bat") $StageDir -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath -Force
    Write-Host "Portable release package created -> $ZipPath"
}
