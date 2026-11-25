using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;

namespace UltimateAlarm.CoreService
{
    using UltimateAlarm.Core;

    public sealed class AlarmService : ServiceBase
    {
        private readonly AlarmStore _store = new AlarmStore();
        private readonly PowerWakeManager _powerWake = new PowerWakeManager();
        private readonly object _stateLock = new object();

        private ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private AutoResetEvent _wakeTickEvent = new AutoResetEvent(false);

        private Thread _schedulerThread;
        private Thread _ipcThread;

        private ActiveRingState _activeRing;
        private ManualResetEvent _inProcessAudioStop;
        private Thread _inProcessAudioThread;
        private Process _workerProcess;
        private uint _workerSessionId = 0xFFFFFFFF;
        private EventWaitHandle _globalStopEvent;
        private DateTime _ringStartUtc;
        private DateTime _lastWorkerLaunchUtc = DateTime.MinValue;
        private bool _bootAutoLockHandled;
        private volatile bool _forceWakeReArm = true;
        private int _activeIpcHandlers;

        // PIN verification brute-force rate limiting
        private readonly object _pinLock = new object();
        private int _pinFailCount;
        private DateTime _pinLockoutUntilUtc = DateTime.MinValue;

        public AlarmService()
        {
            ServiceName = PowerWakeManager.ServiceName;
            CanStop = true;
            CanShutdown = true;
            CanHandlePowerEvent = true;
            CanHandleSessionChangeEvent = true;
        }

        public void StartEngine()
        {
            AppPaths.EnsureDirectories();
            PowerWakeManager.DisablePowerThrottling();
            AppLogger.Info("Service", "UltimateAlarm engine starting (IsSystem=" + IsRunningAsSystem() + ").");

            if (IsRunningAsSystem())
            {
                AlarmConfig startCfg = _store.LoadConfig();
                SecurityHelper.MigrateLegacyTrayRunKeyIfNeeded(startCfg.LaunchUiInTrayAtLogon);
                if (startCfg.LaunchUiInTrayAtLogon)
                {
                    ThreadPool.QueueUserWorkItem(delegate { TryEnsureTrayUiInConsoleSession(); });
                }
            }

            _shutdownEvent.Reset();
            _globalStopEvent = OpenOrCreateStopEvent(false);

            // Resume active ring if service/PC restarted mid-alarm before user dismissed.
            ActiveRingState persistedRing = _store.LoadActiveRing();
            if (persistedRing != null)
            {
                DateTime started;
                if (DateTime.TryParseExact(persistedRing.StartedAtIso, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out started))
                {
                    int maxMins = persistedRing.MaxRingMinutes > 0 ? persistedRing.MaxRingMinutes : 45;
                    if (!persistedRing.IsTest && DateTime.Now <= started.AddMinutes(maxMins))
                    {
                        AppLogger.Warn("Service", "Resuming un-dismissed alarm '" + persistedRing.Label + "' after restart.");
                        StartRinging(persistedRing);
                    }
                    else
                    {
                        _store.SaveActiveRing(null);
                    }
                }
                else
                {
                    _store.SaveActiveRing(null);
                }
            }

            _schedulerThread = new Thread(SchedulerLoop);
            _schedulerThread.IsBackground = true;
            _schedulerThread.SetApartmentState(ApartmentState.MTA);
            _schedulerThread.Start();

            _ipcThread = new Thread(IpcServerLoop);
            _ipcThread.IsBackground = true;
            _ipcThread.Start();

            CheckAutoLockOnBoot();
        }

        public void StopEngine()
        {
            AppLogger.Info("Service", "UltimateAlarm engine stopping.");
            _shutdownEvent.Set();
            _wakeTickEvent.Set();
            StopRingingInternal(false);
            _powerWake.DisarmWakeTimer(false);
            PowerWakeManager.SetExecutionHold(false, false);

            if (_schedulerThread != null && _schedulerThread.IsAlive)
            {
                _schedulerThread.Join(3000);
            }
            if (_ipcThread != null && _ipcThread.IsAlive)
            {
                _ipcThread.Join(1500);
            }
            if (_globalStopEvent != null)
            {
                _globalStopEvent.Close();
                _globalStopEvent = null;
            }
            _powerWake.Dispose();
        }

        protected override void OnStart(string[] args)
        {
            StartEngine();
        }

        protected override void OnStop()
        {
            StopEngine();
        }

        protected override void OnShutdown()
        {
            StopEngine();
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            AppLogger.Info("Service", "PowerEvent received: " + powerStatus);
            if (powerStatus == PowerBroadcastStatus.ResumeAutomatic ||
                powerStatus == PowerBroadcastStatus.ResumeSuspend ||
                powerStatus == PowerBroadcastStatus.ResumeCritical)
            {
                PowerWakeManager.SetExecutionHold(true, true);
                _wakeTickEvent.Set();
            }
            return true;
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            AppLogger.Info("Service", "SessionChange: " + changeDescription.Reason + " SessionId=" + changeDescription.SessionId);
            if (changeDescription.Reason == SessionChangeReason.SessionLogon)
            {
                CheckAutoLockOnBoot();
                if (IsRunningAsSystem() && _store.LoadConfig().LaunchUiInTrayAtLogon)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(2500);
                        TryEnsureTrayUiInConsoleSession();
                    });
                }
            }
            lock (_stateLock)
            {
                if (_activeRing != null && _workerProcess != null)
                {
                    uint currentConsole = NativeMethods.WTSGetActiveConsoleSessionId();
                    bool identityChanged = (changeDescription.Reason == SessionChangeReason.SessionLogon ||
                                            changeDescription.Reason == SessionChangeReason.SessionLogoff);
                    if (currentConsole != 0xFFFFFFFF && (currentConsole != _workerSessionId || identityChanged))
                    {
                        AppLogger.Info("Service", "Console session/logon state changed during active alarm (" + _workerSessionId + " -> " + currentConsole + ", reason=" + changeDescription.Reason + "); restarting audio worker.");
                        KillWorkerOnly();
                        EnsureAudioWorkerRunning();
                    }
                }
            }
        }

        private void CheckAutoLockOnBoot()
        {
            try
            {
                if (_bootAutoLockHandled || !IsRunningAsSystem())
                {
                    return;
                }

                ulong uptimeMs = NativeMethods.GetTickCount64();
                if (uptimeMs > 180000)
                {
                    _bootAutoLockHandled = true;
                    return;
                }

                AlarmConfig cfg = _store.LoadConfig();
                if (!cfg.AutoLockOnBoot || !string.Equals(cfg.AuthMode, "AutoLogonAndLock", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Prevent re-locking on service restart during the same boot cycle.
                DateTime bootUtc = DateTime.UtcNow.AddMilliseconds(-(double)uptimeMs);
                if (File.Exists(AppPaths.BootLockMarkerPath))
                {
                    string existing = File.ReadAllText(AppPaths.BootLockMarkerPath).Trim();
                    DateTime recordedBoot;
                    if (DateTime.TryParseExact(existing, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out recordedBoot))
                    {
                        if (Math.Abs((bootUtc - recordedBoot).TotalSeconds) < 45)
                        {
                            _bootAutoLockHandled = true;
                            return;
                        }
                    }
                }

                // Wait briefly on a background worker so explorer/audio init completes before locking.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(1500);
                        string err;
                        if (SecurityHelper.LockActiveConsoleSession(out err))
                        {
                            _bootAutoLockHandled = true;
                            AppPaths.EnsureDirectories();
                            File.WriteAllText(AppPaths.BootLockMarkerPath, bootUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                            AppLogger.Info("Security", "AutoLockOnBoot locked console session cleanly after auto-logon.");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Security", "AutoLockOnBoot attempt failed: " + ex.Message);
                    }
                });
            }
            catch { }
        }

        private void SchedulerLoop()
        {
            DateTime lastWakeArmCheck = DateTime.MinValue;
            DateTime lastPowerAudit = DateTime.MinValue;
            while (!_shutdownEvent.WaitOne(0))
            {
                try
                {
                    DateTime now = DateTime.Now;
                    AlarmConfig cfg = _store.LoadConfig();
                    bool configDirty = false;

                    // 1. Check for due alarms if not already ringing (real alarms supersede test rings, never overwrite another active real alarm)
                    bool realAlarmActive;
                    lock (_stateLock)
                    {
                        realAlarmActive = _activeRing != null && !_activeRing.IsTest;
                    }

                    if (!realAlarmActive)
                    {
                        for (int i = 0; i < cfg.Alarms.Count; i++)
                        {
                            AlarmItem alarm = cfg.Alarms[i];
                            string occKey;
                            bool isSnooze;
                            DateTime? due = alarm.GetDueOccurrence(now, out occKey, out isSnooze);
                            if (due.HasValue)
                            {
                                alarm.LastHandledOccurrence = occKey;
                                if (isSnooze)
                                {
                                    alarm.SnoozeUntilIso = null;
                                }
                                configDirty = true;

                                ActiveRingState ring = new ActiveRingState();
                                ring.AlarmId = alarm.Id;
                                ring.Label = alarm.Label;
                                ring.OccurrenceKey = occKey;
                                ring.StartedAtIso = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                                ring.SoundPreset = alarm.SoundPreset;
                                ring.CustomWavPath = alarm.CustomWavPath;
                                ring.TargetDeviceId = alarm.TargetDeviceId;
                                ring.ForceFullVolume = alarm.ForceFullVolume;
                                ring.TargetVolumePercent = alarm.VolumePercent > 0
                                    ? AudioRouter.ClampVolumePercent(alarm.VolumePercent)
                                    : AudioRouter.ClampVolumePercent(cfg.AlarmVolumePercent);
                                ring.MaxRingMinutes = alarm.MaxRingMinutes;
                                ring.SnoozeMinutes = alarm.SnoozeMinutes;
                                ring.ChallengeType = alarm.ChallengeType;
                                ring.IsTest = false;

                                AppLogger.Info("Scheduler", string.Format("Triggering alarm '{0}' ({1}) occ={2} snooze={3}", alarm.Label, alarm.Id, occKey, isSnooze));
                                _store.SaveConfig(cfg);
                                configDirty = false;
                                StartRinging(ring);
                                break;
                            }
                        }
                    }

                    if (configDirty)
                    {
                        _store.SaveConfig(cfg);
                    }

                    // 2. Keep active ring alive & supervise worker
                    bool keepAwakeForPending = false;
                    DateTime? nextWake = ComputeNextWakeTime(cfg, now);
                    if (nextWake.HasValue && (nextWake.Value - now).TotalMinutes <= 3.0)
                    {
                        keepAwakeForPending = true;
                    }

                    lock (_stateLock)
                    {
                        if (_activeRing != null)
                        {
                            PowerWakeManager.SetExecutionHold(true, true);
                            int maxSecs = _activeRing.IsTest
                                ? 10
                                : (_activeRing.MaxRingMinutes > 0 ? _activeRing.MaxRingMinutes * 60 : 0);

                            if (maxSecs > 0 && (DateTime.UtcNow - _ringStartUtc).TotalSeconds >= maxSecs)
                            {
                                AppLogger.Info("Scheduler", "Active alarm reached max ring duration; stopping.");
                                bool wasTest = _activeRing.IsTest;
                                string doneAlarmId = _activeRing.AlarmId;
                                string doneOcc = _activeRing.OccurrenceKey;
                                StopRingingInternal(true);
                                if (!wasTest)
                                {
                                    MarkAlarmDismissed(doneAlarmId, doneOcc);
                                }
                            }
                            else if (_activeRing.IsTest && _workerProcess != null && _workerProcess.HasExited &&
                                     _workerProcess.ExitCode == 0 && (DateTime.UtcNow - _ringStartUtc).TotalSeconds >= 8.5)
                            {
                                // Test worker completed its 10s duration cleanly; stop without respawning a tail worker.
                                StopRingingInternal(true);
                            }
                            else
                            {
                                EnsureAudioWorkerRunning();
                            }
                        }
                        else
                        {
                            PowerWakeManager.SetExecutionHold(keepAwakeForPending, false);
                        }
                    }

                    // 3. Arm RTC WaitableTimer + Task Scheduler backup (immediately when config changed or every 10s)
                    if (_forceWakeReArm || (now - lastWakeArmCheck).TotalSeconds >= 10 || nextWake == null)
                    {
                        _forceWakeReArm = false;
                        lastWakeArmCheck = now;
                        if (IsRunningAsSystem())
                        {
                            _powerWake.ArmWakeTimer(nextWake, cfg.PreWakeOffsetSeconds, cfg.EnableTaskSchedulerWakeBackup);
                        }
                        else
                        {
                            _powerWake.ArmWakeTimer(nextWake, cfg.PreWakeOffsetSeconds, false);
                        }
                    }

                    // 4. Periodically guard active power scheme against optimizers/Power Saver turning off wake timers
                    if (nextWake.HasValue && IsRunningAsSystem() && (now - lastPowerAudit).TotalSeconds >= 60)
                    {
                        lastPowerAudit = now;
                        PowerWakeManager.EnsureActiveSchemeWakeTimersEnabled();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Scheduler", "Loop error: " + ex);
                }

                WaitHandle.WaitAny(new WaitHandle[] { _shutdownEvent, _wakeTickEvent }, 1000);
            }
        }

        private DateTime? ComputeNextWakeTime(AlarmConfig cfg, DateTime now)
        {
            DateTime? earliest = null;
            for (int i = 0; i < cfg.Alarms.Count; i++)
            {
                DateTime? cand = cfg.Alarms[i].GetNextScheduledTime(now);
                if (cand.HasValue && (!earliest.HasValue || cand.Value < earliest.Value))
                {
                    earliest = cand;
                }
            }
            return earliest;
        }

        private void StartRinging(ActiveRingState ring)
        {
            lock (_stateLock)
            {
                StopRingingInternal(false);
                _activeRing = ring;
                _ringStartUtc = DateTime.UtcNow;
                _store.SaveActiveRing(ring);
                if (_globalStopEvent != null)
                {
                    _globalStopEvent.Reset();
                }
                PowerWakeManager.SetExecutionHold(true, true);
                EnsureAudioWorkerRunning();
                if (!ring.IsTest && IsRunningAsSystem())
                {
                    ThreadPool.QueueUserWorkItem(delegate { TryEnsureTrayUiInConsoleSession(); });
                }
            }
        }

        private void StopRingingInternal(bool clearPersistedRing)
        {
            lock (_stateLock)
            {
                _activeRing = null;
                if (clearPersistedRing)
                {
                    _store.SaveActiveRing(null);
                }

                if (_globalStopEvent != null)
                {
                    try { _globalStopEvent.Set(); } catch { }
                }

                if (_inProcessAudioStop != null)
                {
                    try { _inProcessAudioStop.Set(); } catch { }
                }
                if (_inProcessAudioThread != null && _inProcessAudioThread.IsAlive)
                {
                    _inProcessAudioThread.Join(1500);
                }
                _inProcessAudioThread = null;
                if (_inProcessAudioStop != null)
                {
                    _inProcessAudioStop.Dispose();
                    _inProcessAudioStop = null;
                }

                KillWorkerOnly();
                PowerWakeManager.SetExecutionHold(false, false);
            }
        }

        private void KillWorkerOnly()
        {
            if (_workerProcess != null)
            {
                try
                {
                    if (!_workerProcess.HasExited)
                    {
                        if (!_workerProcess.WaitForExit(800))
                        {
                            _workerProcess.Kill();
                        }
                    }
                }
                catch { }
                try { _workerProcess.Dispose(); } catch { }
                _workerProcess = null;
                _workerSessionId = 0xFFFFFFFF;
            }
        }

        private void EnsureAudioWorkerRunning()
        {
            if (_activeRing == null)
            {
                return;
            }

            if (_workerProcess != null)
            {
                try
                {
                    if (!_workerProcess.HasExited)
                    {
                        return;
                    }
                    AppLogger.Warn("Service", "Console audio worker exited unexpectedly (code=" + _workerProcess.ExitCode + "); switching/restarting.");
                    _workerProcess.Dispose();
                    _workerProcess = null;
                }
                catch
                {
                    _workerProcess = null;
                }
            }

            if (_inProcessAudioThread != null && _inProcessAudioThread.IsAlive)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastWorkerLaunchUtc).TotalMilliseconds < 800)
            {
                return;
            }
            _lastWorkerLaunchUtc = DateTime.UtcNow;

            // Try launching worker inside active console session (prefers logged-in user token for full multi-output WASAPI routing, falls back to LocalSystem for pre-login LogonUI).
            if (IsRunningAsSystem())
            {
                Process launched;
                uint sessId;
                string tokenKind;
                string err;
                if (TryLaunchConsoleSessionWorker(out launched, out sessId, out tokenKind, out err))
                {
                    _workerProcess = launched;
                    _workerSessionId = sessId;
                    AppLogger.Info("Service", "Started audio worker PID=" + launched.Id + " in Console Session " + sessId + " (Token=" + tokenKind + ")");
                    return;
                }
                AppLogger.Warn("Service", "Console session worker spawn failed (" + err + "); falling back to in-process audio.");
            }

            StartInProcessAudioFallback();
        }

        private void StartInProcessAudioFallback()
        {
            if (_activeRing == null) return;
            ActiveRingState ringCopy = _activeRing;
            AlarmConfig cfgCopy = _store.LoadConfig();
            _inProcessAudioStop = new ManualResetEvent(false);
            ManualResetEvent stopSig = _inProcessAudioStop;

            _inProcessAudioThread = new Thread(delegate()
            {
                try
                {
                    int maxSecs = ringCopy.IsTest ? 10 : 0;
                    AudioRouter.PlayAlarmLoop(cfgCopy, ringCopy, stopSig, maxSecs);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("AudioFallback", "In-process audio error: " + ex);
                }
            });
            _inProcessAudioThread.IsBackground = true;
            _inProcessAudioThread.SetApartmentState(ApartmentState.MTA);
            _inProcessAudioThread.Start();
        }

        private static bool TryLaunchConsoleSessionWorker(out Process process, out uint sessionId, out string tokenKind, out string error)
        {
            process = null;
            sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
            tokenKind = "None";
            error = null;

            if (sessionId == 0xFFFFFFFF)
            {
                error = "No active console session.";
                return false;
            }

            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string cmdLine = "\"" + exePath + "\" --audio-worker";

            // 1. Prefer the active interactive user's token whenever a user is logged into the console session
            //    (works both unlocked and Win+L locked). This gives Audiosrv the real user session context so
            //    WASAPI multi-endpoint broadcasting (Speakers + HDMI + USB) renders reliably on every output.
            IntPtr hUserToken = IntPtr.Zero;
            IntPtr userEnvBlock = IntPtr.Zero;
            int userSpawnErr = 0;
            try
            {
                if (sessionId != 0 && NativeMethods.WTSQueryUserToken(sessionId, out hUserToken) && hUserToken != IntPtr.Zero)
                {
                    NativeMethods.CreateEnvironmentBlock(out userEnvBlock, hUserToken, false);
                    uint userFlags = NativeMethods.CREATE_NO_WINDOW | NativeMethods.HIGH_PRIORITY_CLASS |
                        (userEnvBlock != IntPtr.Zero ? NativeMethods.CREATE_UNICODE_ENVIRONMENT : 0u);

                    string[] desktops = new string[] { "winsta0\\default", null };
                    for (int d = 0; d < desktops.Length; d++)
                    {
                        NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                        si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                        si.lpDesktop = desktops[d];

                        NativeMethods.PROCESS_INFORMATION pi;
                        bool ok = NativeMethods.CreateProcessAsUser(
                            hUserToken,
                            exePath,
                            cmdLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            userFlags,
                            userEnvBlock,
                            AppPaths.RootDir,
                            ref si,
                            out pi);

                        if (ok)
                        {
                            NativeMethods.CloseHandle(pi.hThread);
                            NativeMethods.CloseHandle(pi.hProcess);
                            process = Process.GetProcessById(pi.dwProcessId);
                            tokenKind = "InteractiveUser";
                            return true;
                        }
                        userSpawnErr = Marshal.GetLastWin32Error();
                    }
                    AppLogger.Warn("Service", "InteractiveUser audio worker launch failed (win32=" + userSpawnErr + "); falling back to LocalSystem session token.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Service", "InteractiveUser audio worker launch exception: " + ex.Message);
            }
            finally
            {
                if (userEnvBlock != IntPtr.Zero)
                {
                    try { NativeMethods.DestroyEnvironmentBlock(userEnvBlock); } catch { }
                }
                if (hUserToken != IntPtr.Zero) NativeMethods.CloseHandle(hUserToken);
            }

            // 2. Fallback: Pre-login LogonUI screen (no interactive user logged on yet) -> LocalSystem token moved into console session.
            IntPtr hSysToken = IntPtr.Zero;
            IntPtr hDupToken = IntPtr.Zero;
            IntPtr sysEnvBlock = IntPtr.Zero;

            try
            {
                const uint MAXIMUM_ALLOWED = 0x02000000;
                if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), MAXIMUM_ALLOWED, out hSysToken))
                {
                    error = "OpenProcessToken win32=" + Marshal.GetLastWin32Error();
                    return false;
                }

                if (!NativeMethods.DuplicateTokenEx(
                    hSysToken,
                    MAXIMUM_ALLOWED,
                    IntPtr.Zero,
                    NativeMethods.SecurityImpersonation,
                    NativeMethods.TokenPrimary,
                    out hDupToken))
                {
                    error = "DuplicateTokenEx win32=" + Marshal.GetLastWin32Error();
                    return false;
                }

                if (!NativeMethods.SetTokenInformation(hDupToken, NativeMethods.TokenSessionId, ref sessionId, 4))
                {
                    error = "SetTokenInformation(TokenSessionId=" + sessionId + ") win32=" + Marshal.GetLastWin32Error();
                    return false;
                }

                NativeMethods.CreateEnvironmentBlock(out sysEnvBlock, hDupToken, false);
                uint sysFlags = NativeMethods.CREATE_NO_WINDOW | NativeMethods.HIGH_PRIORITY_CLASS |
                    (sysEnvBlock != IntPtr.Zero ? NativeMethods.CREATE_UNICODE_ENVIRONMENT : 0u);

                string[] desktops = new string[] { "winsta0\\default", null };
                int lastErr = 0;
                for (int d = 0; d < desktops.Length; d++)
                {
                    NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                    si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                    si.lpDesktop = desktops[d];

                    NativeMethods.PROCESS_INFORMATION pi;
                    bool ok = NativeMethods.CreateProcessAsUser(
                        hDupToken,
                        exePath,
                        cmdLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        sysFlags,
                        sysEnvBlock,
                        AppPaths.RootDir,
                        ref si,
                        out pi);

                    if (ok)
                    {
                        NativeMethods.CloseHandle(pi.hThread);
                        NativeMethods.CloseHandle(pi.hProcess);
                        process = Process.GetProcessById(pi.dwProcessId);
                        tokenKind = "LocalSystem";
                        return true;
                    }
                    lastErr = Marshal.GetLastWin32Error();
                }

                error = "CreateProcessAsUser failed win32=" + lastErr + (userSpawnErr != 0 ? (" (userWin32=" + userSpawnErr + ")") : "");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (sysEnvBlock != IntPtr.Zero)
                {
                    try { NativeMethods.DestroyEnvironmentBlock(sysEnvBlock); } catch { }
                }
                if (hDupToken != IntPtr.Zero) NativeMethods.CloseHandle(hDupToken);
                if (hSysToken != IntPtr.Zero) NativeMethods.CloseHandle(hSysToken);
            }
        }

        private void MarkAlarmDismissed(string alarmId, string occurrenceKey)
        {
            if (string.IsNullOrEmpty(alarmId)) return;
            AlarmConfig cfg = _store.LoadConfig();
            for (int i = 0; i < cfg.Alarms.Count; i++)
            {
                if (string.Equals(cfg.Alarms[i].Id, alarmId, StringComparison.OrdinalIgnoreCase))
                {
                    cfg.Alarms[i].LastDismissedOccurrence = occurrenceKey;
                    cfg.Alarms[i].SnoozeUntilIso = null;
                    cfg.Alarms[i].SnoozeOccurrence = null;
                    if (cfg.Alarms[i].IsOneShot())
                    {
                        cfg.Alarms[i].Enabled = false;
                    }
                    _store.SaveConfig(cfg);
                    break;
                }
            }
        }

        private void IpcServerLoop()
        {
            while (!_shutdownEvent.WaitOne(0))
            {
                NamedPipeServerStream server = null;
                bool handedOff = false;
                try
                {
                    if (Thread.VolatileRead(ref _activeIpcHandlers) >= 8)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    server = IpcProtocol.CreateServerStream();
                    IAsyncResult asyncWait = server.BeginWaitForConnection(null, null);
                    int idx = WaitHandle.WaitAny(new WaitHandle[] { _shutdownEvent, asyncWait.AsyncWaitHandle });
                    if (idx == 0)
                    {
                        break;
                    }
                    server.EndWaitForConnection(asyncWait);

                    NamedPipeServerStream clientPipe = server;
                    Interlocked.Increment(ref _activeIpcHandlers);
                    handedOff = true;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            HandleClientConnection(clientPipe);
                        }
                        finally
                        {
                            try { clientPipe.Dispose(); } catch { }
                            Interlocked.Decrement(ref _activeIpcHandlers);
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (!_shutdownEvent.WaitOne(0))
                    {
                        AppLogger.Warn("IPC", "Pipe loop exception: " + ex.Message);
                        Thread.Sleep(250);
                    }
                }
                finally
                {
                    if (!handedOff && server != null)
                    {
                        try { server.Dispose(); } catch { }
                    }
                }
            }
        }

        private void HandleClientConnection(NamedPipeServerStream server)
        {
            try
            {
                string reqJson = IpcProtocol.ReadMessageWithTimeout(server, 3000);
                if (string.IsNullOrEmpty(reqJson))
                {
                    return;
                }
                bool callerPrivileged = IpcProtocol.IsCallerAdminOrSystem(server);
                IpcRequest req = JsonHelper.Deserialize<IpcRequest>(reqJson);
                ServiceStatusResponse resp = ProcessCommand(req, callerPrivileged);
                IpcProtocol.WriteMessage(server, JsonHelper.Serialize(resp));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("IPC", "Client handling error: " + ex.Message);
                try
                {
                    ServiceStatusResponse err = new ServiceStatusResponse();
                    err.Success = false;
                    err.Error = ex.Message;
                    IpcProtocol.WriteMessage(server, JsonHelper.Serialize(err));
                }
                catch { }
            }
        }

        private static bool SanitizeAlarmItem(AlarmItem item, DateTime now, out string validationError)
        {
            validationError = null;
            if (item == null)
            {
                validationError = "Alarm payload missing.";
                return false;
            }
            if (item.Hour < 0 || item.Hour > 23 || item.Minute < 0 || item.Minute > 59)
            {
                validationError = "Alarm time must be between 00:00 and 23:59.";
                return false;
            }
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            else if (item.Id.Length > 32)
            {
                item.Id = item.Id.Substring(0, 32);
            }

            string lbl = (item.Label ?? "Alarm").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (string.IsNullOrEmpty(lbl)) lbl = "Alarm";
            if (lbl.Length > 120) lbl = lbl.Substring(0, 120);
            item.Label = lbl;

            string wavErr;
            if (!SecurityHelper.IsSafeLocalWavPath(item.CustomWavPath, out wavErr))
            {
                validationError = wavErr;
                return false;
            }
            item.CustomWavPath = (item.CustomWavPath ?? "").Trim();

            if (item.VolumePercent > 0)
            {
                item.VolumePercent = AudioRouter.ClampVolumePercent(item.VolumePercent);
            }
            item.BootCatchupMinutes = Math.Max(5, Math.Min(720, item.BootCatchupMinutes > 0 ? item.BootCatchupMinutes : 45));
            item.MaxRingMinutes = Math.Max(1, Math.Min(240, item.MaxRingMinutes > 0 ? item.MaxRingMinutes : 30));
            item.SnoozeMinutes = Math.Max(0, Math.Min(60, item.SnoozeMinutes));

            if (item.DaysOfWeek != null && item.DaysOfWeek.Length > 0)
            {
                List<int> cleanDays = new List<int>();
                for (int i = 0; i < item.DaysOfWeek.Length; i++)
                {
                    int d = item.DaysOfWeek[i];
                    if (d >= 0 && d <= 6 && !cleanDays.Contains(d)) cleanDays.Add(d);
                }
                cleanDays.Sort();
                item.DaysOfWeek = cleanDays.ToArray();
            }
            else
            {
                item.DaysOfWeek = new int[0];
            }

            if (string.IsNullOrEmpty(item.SoundPreset) || item.SoundPreset.Length > 64)
            {
                item.SoundPreset = "KlaxonPulse";
            }
            string ch = (item.ChallengeType ?? "Math").Trim();
            if (!string.Equals(ch, "None", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ch, "Math", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ch, "TypeCode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ch, "Pin", StringComparison.OrdinalIgnoreCase))
            {
                ch = "Math";
            }
            item.ChallengeType = ch;

            if (item.IsOneShot())
            {
                // Preserve valid user-selected future OneShotDate; only recompute if missing, malformed, or already past.
                DateTime parsedDate;
                bool keepExisting = false;
                if (!string.IsNullOrEmpty(item.OneShotDate) &&
                    DateTime.TryParseExact(item.OneShotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    DateTime occ = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, item.Hour, item.Minute, 0);
                    if (occ > now)
                    {
                        keepExisting = true;
                    }
                }
                if (!keepExisting)
                {
                    item.OneShotDate = null;
                    item.EnsureOneShotDate(now);
                }
            }
            else
            {
                item.OneShotDate = null;
            }
            return true;
        }

        private ServiceStatusResponse ProcessCommand(IpcRequest req, bool callerPrivileged)
        {
            string cmd = (req != null && req.Command != null) ? req.Command.ToUpperInvariant() : "GET_STATUS";
            DateTime now = DateTime.Now;

            if (cmd == "ADD_OR_UPDATE_ALARM" && req.Alarm != null)
            {
                AlarmItem item = req.Alarm;
                string valErr;
                if (!SanitizeAlarmItem(item, now, out valErr))
                {
                    ServiceStatusResponse bad = BuildStatusResponse(false);
                    bad.Success = false;
                    bad.Error = valErr;
                    return bad;
                }

                AlarmConfig cfg = _store.LoadConfig();
                item.LastHandledOccurrence = null;
                item.LastDismissedOccurrence = null;
                item.SnoozeUntilIso = null;
                item.SnoozeOccurrence = null;

                bool updated = false;
                for (int i = 0; i < cfg.Alarms.Count; i++)
                {
                    if (string.Equals(cfg.Alarms[i].Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms[i] = item;
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                {
                    if (cfg.Alarms.Count >= 100)
                    {
                        ServiceStatusResponse full = BuildStatusResponse(false);
                        full.Success = false;
                        full.Error = "Maximum alarm limit (100) reached.";
                        return full;
                    }
                    cfg.Alarms.Add(item);
                }
                _store.SaveConfig(cfg);
                _forceWakeReArm = true;
                _wakeTickEvent.Set();
            }
            else if (cmd == "DELETE_ALARM" && !string.IsNullOrEmpty(req.AlarmId))
            {
                AlarmConfig cfg = _store.LoadConfig();
                for (int i = cfg.Alarms.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(cfg.Alarms[i].Id, req.AlarmId, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms.RemoveAt(i);
                    }
                }
                _store.SaveConfig(cfg);
                _forceWakeReArm = true;
                _wakeTickEvent.Set();
            }
            else if (cmd == "TOGGLE_ALARM" && !string.IsNullOrEmpty(req.AlarmId))
            {
                AlarmConfig cfg = _store.LoadConfig();
                for (int i = 0; i < cfg.Alarms.Count; i++)
                {
                    if (string.Equals(cfg.Alarms[i].Id, req.AlarmId, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms[i].Enabled = req.BoolFlag;
                        if (req.BoolFlag)
                        {
                            cfg.Alarms[i].LastHandledOccurrence = null;
                            cfg.Alarms[i].LastDismissedOccurrence = null;
                            if (cfg.Alarms[i].IsOneShot())
                            {
                                DateTime parsedDate;
                                bool futureOk = false;
                                if (!string.IsNullOrEmpty(cfg.Alarms[i].OneShotDate) &&
                                    DateTime.TryParseExact(cfg.Alarms[i].OneShotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                                {
                                    DateTime occ = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, cfg.Alarms[i].Hour, cfg.Alarms[i].Minute, 0);
                                    futureOk = occ > now;
                                }
                                if (!futureOk)
                                {
                                    cfg.Alarms[i].OneShotDate = null;
                                    cfg.Alarms[i].EnsureOneShotDate(now);
                                }
                            }
                        }
                        break;
                    }
                }
                _store.SaveConfig(cfg);
                _forceWakeReArm = true;
                _wakeTickEvent.Set();
            }
            else if (cmd == "DISMISS")
            {
                lock (_stateLock)
                {
                    if (_activeRing != null)
                    {
                        bool wasTest = _activeRing.IsTest;
                        string aid = _activeRing.AlarmId;
                        string occ = _activeRing.OccurrenceKey;
                        StopRingingInternal(true);
                        if (!wasTest)
                        {
                            MarkAlarmDismissed(aid, occ);
                        }
                    }
                }
                _wakeTickEvent.Set();
            }
            else if (cmd == "SNOOZE")
            {
                lock (_stateLock)
                {
                    if (_activeRing != null)
                    {
                        string aid = _activeRing.AlarmId;
                        string occ = _activeRing.OccurrenceKey;
                        int mins = req.IntParam > 0 ? req.IntParam : (_activeRing.SnoozeMinutes > 0 ? _activeRing.SnoozeMinutes : 5);
                        bool wasTest = _activeRing.IsTest;
                        StopRingingInternal(true);

                        if (!wasTest && !string.IsNullOrEmpty(aid))
                        {
                            AlarmConfig cfg = _store.LoadConfig();
                            for (int i = 0; i < cfg.Alarms.Count; i++)
                            {
                                if (string.Equals(cfg.Alarms[i].Id, aid, StringComparison.OrdinalIgnoreCase))
                                {
                                    cfg.Alarms[i].SnoozeUntilIso = DateTime.Now.AddMinutes(mins).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                                    cfg.Alarms[i].SnoozeOccurrence = occ;
                                    _store.SaveConfig(cfg);
                                    break;
                                }
                            }
                        }
                    }
                }
                _wakeTickEvent.Set();
            }
            else if (cmd == "TEST_RING")
            {
                AlarmConfig cfg = _store.LoadConfig();
                int testVol = req.IntParam > 0
                    ? AudioRouter.ClampVolumePercent(req.IntParam)
                    : AudioRouter.ClampVolumePercent(cfg.AlarmVolumePercent);
                string devOverride = req.DeviceId ?? "";
                string testLabel = "All-Output Test Ring (" + testVol + "%, 10s)";
                if (!string.IsNullOrEmpty(devOverride))
                {
                    AudioDeviceInfo targetDev = AudioRouter.SelectSpeakerDevice(cfg, devOverride);
                    string devName = targetDev != null ? targetDev.FriendlyName : "Selected Device";
                    testLabel = "Device Test: " + devName + " (" + testVol + "%, 10s)";
                }
                else if (!cfg.RingAllOutputDevices)
                {
                    testLabel = "Speaker Test Ring (" + testVol + "%, 10s)";
                }
                ActiveRingState testRing = new ActiveRingState();
                testRing.AlarmId = "TEST";
                testRing.Label = testLabel;
                testRing.OccurrenceKey = now.ToString("yyyy-MM-dd@HH:mm:ss", CultureInfo.InvariantCulture);
                testRing.StartedAtIso = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                testRing.SoundPreset = !string.IsNullOrEmpty(req.StringParam) ? req.StringParam : "KlaxonPulse";
                testRing.TargetDeviceId = devOverride;
                testRing.ForceFullVolume = true;
                testRing.TargetVolumePercent = testVol;
                testRing.MaxRingMinutes = 1;
                testRing.SnoozeMinutes = 0;
                testRing.ChallengeType = "None";
                testRing.IsTest = true;
                StartRinging(testRing);
            }
            else if (cmd == "SET_PINNED_SPEAKER")
            {
                AlarmConfig cfg = _store.LoadConfig();
                cfg.PinnedSpeakerDeviceId = req.DeviceId ?? "";
                _store.SaveConfig(cfg);
            }
            else if (cmd == "SET_AUDIO_POLICY")
            {
                AlarmConfig cfg = _store.LoadConfig();
                if (req.StringParam == "HDMI") cfg.AllowHdmiFallback = req.BoolFlag;
                if (req.StringParam == "HEADPHONES") cfg.AllowHeadphoneFallback = req.BoolFlag;
                if (req.StringParam == "RINGALL") cfg.RingAllOutputDevices = req.BoolFlag;
                if (req.StringParam == "VOLUME") cfg.AlarmVolumePercent = AudioRouter.ClampVolumePercent(req.IntParam);
                if (req.StringParam == "TASKBACKUP") cfg.EnableTaskSchedulerWakeBackup = req.BoolFlag;
                if (req.StringParam == "AUTOLOCK") cfg.AutoLockOnBoot = req.BoolFlag;
                _store.SaveConfig(cfg);
                _wakeTickEvent.Set();
            }
            else if (cmd == "SET_UI_THEME")
            {
                string raw = req.StringParam ?? "Default";
                string safeTheme = "Default";
                if (string.Equals(raw, "MoreDark", StringComparison.OrdinalIgnoreCase)) safeTheme = "MoreDark";
                else if (string.Equals(raw, "UltimateDark", StringComparison.OrdinalIgnoreCase)) safeTheme = "UltimateDark";
                else if (string.Equals(raw, "Neon", StringComparison.OrdinalIgnoreCase)) safeTheme = "Neon";
                AlarmConfig cfg = _store.LoadConfig();
                cfg.UiTheme = safeTheme;
                _store.SaveConfig(cfg);
            }
            else if (cmd == "APPLY_SETUP_CONFIG" && req.Config != null)
            {
                AlarmConfig cfg = _store.LoadConfig();
                cfg.SetupCompleted = req.Config.SetupCompleted;
                cfg.PinnedSpeakerDeviceId = req.Config.PinnedSpeakerDeviceId ?? "";
                cfg.RingAllOutputDevices = req.Config.RingAllOutputDevices;
                cfg.AlarmVolumePercent = AudioRouter.ClampVolumePercent(req.Config.AlarmVolumePercent);
                cfg.AllowHdmiFallback = req.Config.AllowHdmiFallback;
                cfg.AllowHeadphoneFallback = req.Config.AllowHeadphoneFallback;
                cfg.EnableTaskSchedulerWakeBackup = req.Config.EnableTaskSchedulerWakeBackup;
                if (!string.IsNullOrEmpty(req.Config.UiTheme))
                {
                    cfg.UiTheme = req.Config.UiTheme;
                }
                if (req.Config.PreWakeOffsetSeconds > 0 && req.Config.PreWakeOffsetSeconds <= 300)
                {
                    cfg.PreWakeOffsetSeconds = req.Config.PreWakeOffsetSeconds;
                }

                // Security-sensitive fields (AuthMode, AutoLockOnBoot, Dismiss PIN) require an elevated Admin/SYSTEM caller.
                if (callerPrivileged)
                {
                    cfg.AuthMode = string.Equals(req.Config.AuthMode, "AutoLogonAndLock", StringComparison.OrdinalIgnoreCase)
                        ? "AutoLogonAndLock"
                        : "LocalSystem";
                    cfg.AutoLockOnBoot = req.Config.AutoLockOnBoot;
                    cfg.LaunchUiInTrayAtLogon = req.Config.LaunchUiInTrayAtLogon;

                    bool incomingPinNotRedacted = !string.Equals(req.Config.DismissPinHash, SecurityHelper.PinConfiguredSentinel, StringComparison.Ordinal);
                    if (incomingPinNotRedacted && (!string.IsNullOrEmpty(req.Config.DismissPinHash) || req.BoolFlag))
                    {
                        cfg.DismissPinHash = req.Config.DismissPinHash ?? "";
                        cfg.DismissPinSalt = req.Config.DismissPinSalt ?? "";
                    }
                }
                else
                {
                    AppLogger.Warn("Security", "Non-admin IPC APPLY_SETUP_CONFIG ignored privileged auth/PIN fields.");
                }

                if (req.Config.Alarms != null && req.Config.Alarms.Count > 0 && req.Config.Alarms.Count <= 100)
                {
                    List<AlarmItem> validAlarms = new List<AlarmItem>();
                    for (int i = 0; i < req.Config.Alarms.Count; i++)
                    {
                        string errIgn;
                        if (SanitizeAlarmItem(req.Config.Alarms[i], now, out errIgn))
                        {
                            validAlarms.Add(req.Config.Alarms[i]);
                        }
                    }
                    if (validAlarms.Count > 0) cfg.Alarms = validAlarms;
                }
                _store.SaveConfig(cfg);
                _forceWakeReArm = true;
                _wakeTickEvent.Set();
                return BuildStatusResponse(true);
            }
            else if (cmd == "ENABLE_WAKE_TIMERS")
            {
                string log;
                string err;
                bool ok = PowerWakeManager.EnableWakeTimers(out log, out err);
                ServiceStatusResponse resp = BuildStatusResponse(true);
                resp.Success = ok;
                resp.Error = err;
                return resp;
            }
            else if (cmd == "OPTIMIZE_POWER")
            {
                string log;
                string err;
                bool ok = PowerWakeManager.OptimizePowerForAlarms(out log, out err);
                ServiceStatusResponse resp = BuildStatusResponse(true);
                resp.Success = ok;
                resp.Error = err;
                return resp;
            }
            else if (cmd == "VERIFY_PIN")
            {
                ServiceStatusResponse resp = new ServiceStatusResponse();
                lock (_pinLock)
                {
                    if (DateTime.UtcNow < _pinLockoutUntilUtc)
                    {
                        int waitSecs = Math.Max(1, (int)Math.Ceiling((_pinLockoutUntilUtc - DateTime.UtcNow).TotalSeconds));
                        resp.Success = false;
                        resp.Error = "Too many failed PIN attempts. Wait " + waitSecs + "s.";
                        return resp;
                    }
                }

                AlarmConfig cfg = _store.LoadConfig();
                bool ok = SecurityHelper.VerifyPin(req.StringParam ?? "", cfg.DismissPinHash, cfg.DismissPinSalt);
                lock (_pinLock)
                {
                    if (ok)
                    {
                        _pinFailCount = 0;
                        _pinLockoutUntilUtc = DateTime.MinValue;
                    }
                    else
                    {
                        _pinFailCount++;
                        if (_pinFailCount >= 5)
                        {
                            int cooldown = Math.Min(120, 15 * (1 << Math.Min(3, _pinFailCount - 5)));
                            _pinLockoutUntilUtc = DateTime.UtcNow.AddSeconds(cooldown);
                            AppLogger.Warn("Security", "PIN brute-force throttle engaged (" + _pinFailCount + " fails, cooldown=" + cooldown + "s).");
                        }
                    }
                }

                resp.Success = ok;
                if (!ok) resp.Error = "Incorrect PIN.";
                return resp;
            }

            return BuildStatusResponse(cmd == "GET_DIAGNOSTICS");
        }

        private ServiceStatusResponse BuildStatusResponse(bool includeDiagnostics)
        {
            AlarmConfig cfg = _store.LoadConfig();
            DateTime now = DateTime.Now;
            DateTime? nextWake = ComputeNextWakeTime(cfg, now);

            // Redact PBKDF2 PIN hash & salt before sending config over IPC to unprivileged clients.
            AlarmConfig safeCfg = JsonHelper.Deserialize<AlarmConfig>(JsonHelper.Serialize(cfg));
            safeCfg.DismissPinHash = !string.IsNullOrEmpty(cfg.DismissPinHash) ? SecurityHelper.PinConfiguredSentinel : "";
            safeCfg.DismissPinSalt = "";

            ServiceStatusResponse resp = new ServiceStatusResponse();
            resp.Success = true;
            resp.ServerTimeIso = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            resp.NextWakeIso = nextWake.HasValue ? nextWake.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "None";
            lock (_stateLock)
            {
                resp.ActiveRing = _activeRing;
            }
            resp.Config = safeCfg;
            resp.Devices = AudioRouter.EnumerateActiveOutputDevices();
            if (includeDiagnostics)
            {
                resp.Diagnostics = PowerWakeManager.CollectDiagnostics(safeCfg, nextWake);
            }
            return resp;
        }

        public static EventWaitHandle OpenOrCreateStopEvent(bool openOnly)
        {
            const string globalName = "Global\\UltimateAlarmStopEvent";
            try
            {
                EventWaitHandle existing;
                EventWaitHandleRights desired = openOnly
                    ? EventWaitHandleRights.Synchronize
                    : (EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify);
                if (EventWaitHandle.TryOpenExisting(globalName, desired, out existing))
                {
                    return existing;
                }
                if (openOnly)
                {
                    return null;
                }
                EventWaitHandleSecurity sec = new EventWaitHandleSecurity();
                SecurityIdentifier sys = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(sys, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new EventWaitHandleAccessRule(admin, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                // AudioWorker only waits/polls on StopEvent (Synchronize only — never Modify, so untrusted apps cannot silence alarms).
                sec.AddAccessRule(new EventWaitHandleAccessRule(authUsers, EventWaitHandleRights.Synchronize, AccessControlType.Allow));
                bool created;
                return new EventWaitHandle(false, EventResetMode.ManualReset, globalName, out created, sec);
            }
            catch
            {
                if (openOnly) return null;
                return new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\UltimateAlarmStopEvent");
            }
        }

        private static void TryEnsureTrayUiInConsoleSession()
        {
            IntPtr userToken = IntPtr.Zero;
            IntPtr envBlock = IntPtr.Zero;
            try
            {
                uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
                if (sessionId == 0xFFFFFFFF || sessionId == 0) return;

                EventWaitHandle existing;
                if (EventWaitHandle.TryOpenExisting("Global\\UltimateAlarmUiSession_" + sessionId, out existing))
                {
                    existing.Close();
                    return;
                }

                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken) || userToken == IntPtr.Zero)
                {
                    return;
                }

                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                NativeMethods.CreateEnvironmentBlock(out envBlock, userToken, false);

                NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                si.lpDesktop = "winsta0\\default";

                uint flags = NativeMethods.NORMAL_PRIORITY_CLASS |
                    (envBlock != IntPtr.Zero ? NativeMethods.CREATE_UNICODE_ENVIRONMENT : 0u);
                NativeMethods.PROCESS_INFORMATION pi;
                bool ok = NativeMethods.CreateProcessAsUser(
                    userToken,
                    exePath,
                    "\"" + exePath + "\" --tray",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    flags,
                    envBlock,
                    AppPaths.RootDir,
                    ref si,
                    out pi);
                if (ok)
                {
                    NativeMethods.CloseHandle(pi.hThread);
                    NativeMethods.CloseHandle(pi.hProcess);
                    AppLogger.Info("Service", "Launched protected Tray UI in console session " + sessionId + " (PID=" + pi.dwProcessId + ").");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Service", "TryEnsureTrayUiInConsoleSession skipped: " + ex.Message);
            }
            finally
            {
                if (envBlock != IntPtr.Zero)
                {
                    try { NativeMethods.DestroyEnvironmentBlock(envBlock); } catch { }
                }
                if (userToken != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(userToken);
                }
            }
        }

        private static bool IsRunningAsSystem()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return id != null && id.IsSystem;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
