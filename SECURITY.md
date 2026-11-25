# Security Architecture & Hardening (`UltimateAlarm`)

`UltimateAlarm` runs a privileged `NT AUTHORITY\SYSTEM` Windows Service (`UltimateAlarmService`) in Session 0 alongside an unprivileged interactive UI and audio worker in the active console session (Session 1). Because cross-session Windows services are a high-value local privilege escalation (LPE) surface, every privilege boundary in `UltimateAlarm` is explicitly hardened.

---

## Privilege Boundary Overview

```text
+-----------------------------------------------------------------------+
| Session 0 — NT AUTHORITY\SYSTEM (UltimateAlarmService)                |
|  • Scheduler, kernel RTC WaitableTimer, Task Scheduler WakeToRun      |
|  • Hardened Named Pipe Server (\\.\pipe\UltimateAlarmPipe)            |
|  • Launches Audio Worker via WTSQueryUserToken (Interactive User)     |
|    or LocalSystem fallback ONLY at pre-login LogonUI                  |
+-----------------------------------+-----------------------------------+
                                    | Named Pipe IPC (NetworkSid DENY,
                                    | 1 MB max, 3s async read timeout,
                                    | client impersonation checks)
+-----------------------------------v-----------------------------------+
| Session 1 — Interactive Standard User (Tray UI / Audio Worker / CLI)  |
|  • Custom Dark-Chrome Dashboard & Dismiss Challenge UI                |
|  • Multi-output WASAPI playback under real user audio session         |
|  • Synchronize-only access to Global\UltimateAlarmStopEvent           |
+-----------------------------------------------------------------------+
```

---

## Implemented Security Controls

| Threat Class / CWE | Mitigation Implemented | Source Reference |
|---|---|---|
| **TOCTOU / Symlink LPE (CWE-367, CWE-377, CWE-59)** | Elevated `schtasks.exe /Create /XML` never reads XML from user-writable folders (`data\`). Temporary task XML files are written with random GUID filenames inside an Admin/`LocalSystem`-only ACL directory (`%ProgramData%\UltimateAlarm\secure_tmp`) with inherited user permissions stripped. | `src/Core/SecurityHelper.cs` (`WriteSecureTempXml`), `src/Core/PowerWakeManager.cs` |
| **Uncontrolled Search Path / Binary Planting (CWE-427)** | System tools (`powercfg.exe`, `schtasks.exe`, `sc.exe`, `netplwiz.exe`) are resolved exclusively from `%SystemRoot%\System32` (and `explorer.exe` from `%SystemRoot%`). Every `CreateProcessAsUser` call passes an explicit `lpApplicationName` alongside the quoted command line. | `src/Core/SecurityHelper.cs` (`ResolveSystemTool`), `src/Service/AlarmService.cs` |
| **Remote & Local Named Pipe Abuse** | `\\.\pipe\UltimateAlarmPipe` explicitly **denies** `NetworkSid` (blocks remote SMB access), caps payloads at **1 MB**, enforces **3-second async read timeouts**, and dispatches handlers on a bounded concurrency pool (max 8) to prevent single-client IPC DoS hangs. | `src/Core/IpcProtocol.cs`, `src/Service/AlarmService.cs` |
| **Unauthorized Auth/PIN Tampering** | `IpcProtocol.IsCallerAdminOrSystem` impersonates the pipe client (`RunAsClient`) so `APPLY_SETUP_CONFIG` refuses to modify `DismissPinHash`, `AuthMode`, `AutoLockOnBoot`, or `LaunchUiInTrayAtLogon` unless the caller is elevated (`Administrator` or `LocalSystem`). | `src/Core/IpcProtocol.cs`, `src/Service/AlarmService.cs` |
| **Credential & Dismiss PIN Protection** | Default `LocalSystem` mode stores **zero Windows passwords**. Custom Dismiss PINs use **PBKDF2-HMAC-SHA1 (100,000 iterations)** with a 128-bit CSPRNG salt and constant-time byte comparison. `BuildStatusResponse` redacts hashes to a `__CONFIGURED__` sentinel over IPC, `VERIFY_PIN` enforces progressive brute-force lockouts (15s → 120s after 5 failures), and Math/TypeCode challenges use `RNGCryptoServiceProvider`. | `src/Core/SecurityHelper.cs`, `src/Service/AlarmService.cs`, `src/UI/Dialogs.cs` |
| **Kernel Object Tampering / Alarm Silencing** | `Global\UltimateAlarmStopEvent` grants `AuthenticatedUserSid` **`Synchronize`-only** rights (no `Modify`), preventing untrusted background processes from silently stopping an active alarm. `Global\UltimateAlarmStoreMutex` restricts non-admins to `Synchronize \| Modify` (no `WRITE_DAC` / `TakeOwnership`) and recovers cleanly from `AbandonedMutexException`. | `src/Service/AlarmService.cs`, `src/Core/AlarmStore.cs`, `src/UI/Program.cs` |
| **UNC/SMB Hash Leakage & WAV OOM DoS** | `CustomWavPath` is validated in both UI and Service IPC to require an absolute local drive `.wav` path — blocking UNC paths (`\\server\share`), Windows device paths (`\\.\pipe`), relative paths, and `..` traversal. `WavAudioData.TryLoad` enforces a **25 MB** file cap and strict header bounds (`8–192 kHz`, `1–8 ch`, `8/16/24/32-bit`). | `src/Core/SecurityHelper.cs` (`IsSafeLocalWavPath`), `src/Core/AudioRouter.cs` |
| **Log Forging (CWE-117) & Input Bounds** | `AppLogger` strips `\r`/`\n` and truncates oversized messages; `SanitizeAlarmItem` validates/clamps hours, minutes, snooze, ring duration, catch-up window, repeat days, label length, theme IDs, and total alarm count (max 100). | `src/Core/AlarmStore.cs`, `src/Service/AlarmService.cs` |

---

## Operational Notes

- **Antivirus / SmartScreen:** Locally compiled unsigned executables do not carry an Authenticode certificate. Always build from source via `build.ps1` and inspect the code before granting UAC elevation.
- **No Telemetry / No Network Access:** `UltimateAlarm` makes zero outbound network connections and stores all configuration locally under `data\alarms.json`.
