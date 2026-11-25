$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Requesting Administrator permissions via UAC to update UltimateAlarm..."
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    exit 0
}

try {
    $BinExe = Join-Path $Root "bin\UltimateAlarm.exe"
    $RootExe = Join-Path $Root "UltimateAlarm.exe"
    $CliExe = Join-Path $Root "bin\ua.exe"

    if (-not (Test-Path $BinExe)) {
        throw "Built executable not found at $BinExe. Run build.ps1 first."
    }

    Write-Host "1/5 Stopping UltimateAlarm UI & service..."
    Get-Process -Name "UltimateAlarm" -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -ne 0 } | Stop-Process -Force -ErrorAction SilentlyContinue
    $svc = Get-Service -Name "UltimateAlarmService" -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Stop-Service -Name "UltimateAlarmService" -Force -ErrorAction SilentlyContinue
        $svc.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(15))
    }
    Get-Process -Name "UltimateAlarm" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Host "2/5 Copying new build to $RootExe..."
    $copied = $false
    for ($try = 1; $try -le 10; $try++) {
        try {
            Copy-Item -Path $BinExe -Destination $RootExe -Force
            $copied = $true
            break
        } catch {
            Start-Sleep -Milliseconds 400
        }
    }
    if (-not $copied) {
        throw "Could not overwrite $RootExe because it is still locked by another process."
    }

    $binLen = (Get-Item $BinExe).Length
    $rootLen = (Get-Item $RootExe).Length
    if ($binLen -ne $rootLen) {
        throw "File size mismatch after copy (bin=$binLen, root=$rootLen)."
    }
    Write-Host "    Verified updated binary ($rootLen bytes)."

    Write-Host "3/5 Starting updated UltimateAlarmService..."
    if (Get-Service -Name "UltimateAlarmService" -ErrorAction SilentlyContinue) {
        Start-Service -Name "UltimateAlarmService"
        (Get-Service -Name "UltimateAlarmService").WaitForStatus("Running", [TimeSpan]::FromSeconds(15))
    }

    if (Test-Path $CliExe) {
        Write-Host "4/5 Re-verifying power, wake timer, audio-idle & startup protections..."
        & $CliExe fix-wake | Out-Null
    }

    # Refresh Windows Start Menu / Desktop shortcuts and notify Explorer shell so the updated icon appears immediately
    try {
        $wsh = New-Object -ComObject WScript.Shell
        $shortcutTargets = @(
            (Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "UltimateAlarm.lnk"),
            (Join-Path ([Environment]::GetFolderPath("CommonPrograms")) "UltimateAlarm\UltimateAlarm.lnk")
        )
        foreach ($lnk in $shortcutTargets) {
            if (Test-Path $lnk) {
                $sc = $wsh.CreateShortcut($lnk)
                $sc.TargetPath = $RootExe
                $sc.WorkingDirectory = $Root
                $sc.IconLocation = "$RootExe,0"
                $sc.Save()
            }
        }
        Add-Type -Namespace WinShell -Name Notify -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);' -ErrorAction SilentlyContinue
        [WinShell.Notify]::SHChangeNotify(0x08000000, 0x0000, [System.IntPtr]::Zero, [System.IntPtr]::Zero)
    } catch { }

    Write-Host "5/5 Launching updated UltimateAlarm UI window..."
    Start-Sleep -Milliseconds 700
    # Close any tray-only auto-launched instance from service startup so the visible window opens cleanly
    Get-Process -Name "UltimateAlarm" -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -ne 0 } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Start-Process -FilePath "explorer.exe" -ArgumentList "`"$RootExe`""

    Write-Host "Update complete! New Themes, 11 Ringtones, and Multi-Output Audio Service are now live."
    Start-Sleep -Seconds 2
}
catch {
    Write-Host ""
    Write-Host "UPDATE FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}
