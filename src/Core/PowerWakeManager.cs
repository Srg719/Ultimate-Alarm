using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace UltimateAlarm.Core
{
    public sealed class PowerWakeManager : IDisposable
    {
        public const string BackupTaskName = "UltimateAlarmWakeBackup";
        public const string ServiceName = "UltimateAlarmService";
        public const string SleepSubgroupGuid = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        public const string RtcWakeSettingGuid = "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d";
        public const string UsbSubgroupGuid = "2a737441-1930-4402-8d77-b2bebba308a3";
        public const string UsbSelectiveSuspendGuid = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        private const string AudioClassRegPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}";

        private static readonly Regex GuidRegex = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        private IntPtr _waitableTimer = IntPtr.Zero;
        private string _armedTimerIso = "";
        private string _armedTaskIso = "";

        public void ArmWakeTimer(DateTime? nextWakeLocal, int preWakeSeconds, bool syncTaskBackup)
        {
            if (!nextWakeLocal.HasValue)
            {
                DisarmWakeTimer(syncTaskBackup);
                return;
            }

            DateTime wakeTarget = nextWakeLocal.Value.AddSeconds(-Math.Max(0, Math.Min(300, preWakeSeconds)));
            if (wakeTarget <= DateTime.Now.AddSeconds(2))
            {
                wakeTarget = DateTime.Now.AddSeconds(2);
            }

            string targetKey = wakeTarget.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            if (!string.Equals(_armedTimerIso, targetKey, StringComparison.Ordinal))
            {
                EnsureTimerHandle();
                if (_waitableTimer != IntPtr.Zero)
                {
                    long dueFileTime = wakeTarget.ToUniversalTime().ToFileTimeUtc();
                    bool ok = NativeMethods.SetWaitableTimer(_waitableTimer, ref dueFileTime, 0, IntPtr.Zero, IntPtr.Zero, true);
                    if (ok)
                    {
                        _armedTimerIso = targetKey;
                        AppLogger.Info("PowerWake", "Armed kernel RTC WaitableTimer (fResume=true) for " + targetKey);
                    }
                    else
                    {
                        AppLogger.Warn("PowerWake", "SetWaitableTimer failed; Win32=" + Marshal.GetLastWin32Error());
                    }
                }
            }

            if (syncTaskBackup && !string.Equals(_armedTaskIso, targetKey, StringComparison.Ordinal))
            {
                string err;
                if (UpsertBackupScheduledTask(wakeTarget, out err))
                {
                    _armedTaskIso = targetKey;
                }
                else if (!string.IsNullOrEmpty(err))
                {
                    AppLogger.Warn("PowerWake", "Backup Scheduled Task sync skipped/failed: " + err);
                    _armedTaskIso = targetKey;
                }
            }
        }

        public void DisarmWakeTimer(bool removeTaskBackup)
        {
            if (_waitableTimer != IntPtr.Zero && !string.IsNullOrEmpty(_armedTimerIso))
            {
                NativeMethods.CancelWaitableTimer(_waitableTimer);
                _armedTimerIso = "";
            }
            if (removeTaskBackup && !string.IsNullOrEmpty(_armedTaskIso))
            {
                DeleteBackupScheduledTask();
                _armedTaskIso = "";
            }
        }

        public static void SetExecutionHold(bool keepAwake, bool wakeDisplay)
        {
            if (keepAwake)
            {
                uint flags = NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED;
                if (wakeDisplay)
                {
                    flags |= NativeMethods.ES_DISPLAY_REQUIRED;
                }
                NativeMethods.SetThreadExecutionState(flags);
            }
            else
            {
                NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            }
        }

        /// <summary>
        /// Exempts the process from Windows 10/11 Power Throttling (EcoQoS) so Power Saver never throttles alarm scheduling or audio.
        /// </summary>
        public static void DisablePowerThrottling()
        {
            try
            {
                NativeMethods.PROCESS_POWER_THROTTLING_STATE state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
                {
                    Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = 0
                };
                NativeMethods.SetProcessInformation(
                    NativeMethods.GetCurrentProcess(),
                    NativeMethods.ProcessPowerThrottling,
                    ref state,
                    Marshal.SizeOf(typeof(NativeMethods.PROCESS_POWER_THROTTLING_STATE)));
            }
            catch { }
        }

        /// <summary>
        /// Wakes DPMS-off displays and dismisses active screen savers when an alarm rings in the user console session.
        /// </summary>
        public static void WakeDisplayAndDismissScreenSaver()
        {
            try
            {
                SetExecutionHold(true, true);
                NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[1];
                inputs[0].type = NativeMethods.INPUT_MOUSE;
                inputs[0].U.mi = new NativeMethods.MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };
                NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            }
            catch { }
        }

        public static bool EnableWakeTimers(out string log, out string error)
        {
            error = null;
            StringBuilder sb = new StringBuilder();
            int code;

            List<string> schemes = GetAllPowerSchemeGuids();
            if (schemes.Count == 0) schemes.Add("SCHEME_CURRENT");

            bool anyOk = false;
            for (int i = 0; i < schemes.Count; i++)
            {
                string g = schemes[i];
                string acOut = RunProcessCapture("powercfg.exe",
                    string.Format("/setacvalueindex {0} {1} {2} 1", g, SleepSubgroupGuid, RtcWakeSettingGuid), out code);
                bool acOk = code == 0;
                string dcOut = RunProcessCapture("powercfg.exe",
                    string.Format("/setdcvalueindex {0} {1} {2} 1", g, SleepSubgroupGuid, RtcWakeSettingGuid), out code);
                bool dcOk = code == 0;
                sb.AppendLine(string.Format("Scheme {0} RTCWAKE -> AC:{1} DC:{2}", g, acOk ? "OK" : acOut, dcOk ? "OK" : dcOut));
                if (acOk && dcOk) anyOk = true;
            }

            string actOut = RunProcessCapture("powercfg.exe", "/setactive SCHEME_CURRENT", out code);
            sb.AppendLine("Apply active power scheme -> exit " + code + (string.IsNullOrEmpty(actOut) ? "" : (": " + actOut)));
            log = sb.ToString();
            if (!anyOk)
            {
                error = "Failed to enable AC/DC wake timers: " + log;
                return false;
            }
            return true;
        }

        public static bool OptimizePowerForAlarms(out string log, out string error)
        {
            StringBuilder sb = new StringBuilder();
            string wakeLog;
            bool wakeOk = EnableWakeTimers(out wakeLog, out error);
            sb.AppendLine(wakeLog.TrimEnd());

            // Disable USB selective suspend across all schemes so USB speakers/DACs never sleep
            List<string> schemes = GetAllPowerSchemeGuids();
            if (schemes.Count == 0) schemes.Add("SCHEME_CURRENT");
            int code;
            for (int i = 0; i < schemes.Count; i++)
            {
                string g = schemes[i];
                RunProcessCapture("powercfg.exe", string.Format("/setacvalueindex {0} {1} {2} 0", g, UsbSubgroupGuid, UsbSelectiveSuspendGuid), out code);
                RunProcessCapture("powercfg.exe", string.Format("/setdcvalueindex {0} {1} {2} 0", g, UsbSubgroupGuid, UsbSelectiveSuspendGuid), out code);
            }
            RunProcessCapture("powercfg.exe", "/setactive SCHEME_CURRENT", out code);
            sb.AppendLine("Disabled USB Selective Suspend across power schemes.");

            // Disable HD Audio / NVIDIA HDMI Audio idle power-down (Conservation & Performance)
            string audioLog;
            int audioTouched = DisableAudioDriverIdleSleep(out audioLog);
            if (!string.IsNullOrEmpty(audioLog))
            {
                sb.AppendLine(audioLog.TrimEnd());
            }
            sb.AppendLine("Audio idle power-down endpoints hardened: " + audioTouched);

            // Migrate any exposed Task Manager Run entry into hidden Scheduled Task
            SecurityHelper.MigrateLegacyTrayRunKeyIfNeeded(new AlarmStore().LoadConfig().LaunchUiInTrayAtLogon);
            sb.AppendLine("Startup entry verified (hidden from Task Manager Startup).");

            log = sb.ToString();
            return wakeOk;
        }

        public static void EnsureActiveSchemeWakeTimersEnabled()
        {
            try
            {
                int code;
                string wakeQ = RunProcessCapture("powercfg.exe", string.Format("/q SCHEME_CURRENT {0} {1}", SleepSubgroupGuid, RtcWakeSettingGuid), out code);
                bool acOk = wakeQ.IndexOf("Current AC Power Setting Index: 0x00000001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            wakeQ.IndexOf("Current AC Power Setting Index: 0x00000002", StringComparison.OrdinalIgnoreCase) >= 0;
                bool dcOk = wakeQ.IndexOf("Current DC Power Setting Index: 0x00000001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            wakeQ.IndexOf("Current DC Power Setting Index: 0x00000002", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!acOk || !dcOk)
                {
                    string log, err;
                    if (EnableWakeTimers(out log, out err))
                    {
                        AppLogger.Warn("PowerWake", "Detected disabled RTC wake timers on active power plan; automatically restored AC & DC wake timers.");
                    }
                }
            }
            catch { }
        }

        private static List<string> GetAllPowerSchemeGuids()
        {
            List<string> list = new List<string>();
            try
            {
                int code;
                string raw = RunProcessCapture("powercfg.exe", "/list", out code);
                MatchCollection matches = GuidRegex.Matches(raw ?? "");
                for (int i = 0; i < matches.Count; i++)
                {
                    string g = matches[i].Value;
                    if (!list.Contains(g)) list.Add(g);
                }
            }
            catch { }
            return list;
        }

        private static string GetActivePowerSchemeName()
        {
            try
            {
                int code;
                string raw = RunProcessCapture("powercfg.exe", "/getactivescheme", out code);
                int lParen = raw.IndexOf('(');
                int rParen = raw.LastIndexOf(')');
                if (lParen >= 0 && rParen > lParen)
                {
                    return raw.Substring(lParen + 1, rParen - lParen - 1).Trim();
                }
            }
            catch { }
            return "Unknown";
        }

        private static int DisableAudioDriverIdleSleep(out string log)
        {
            int updated = 0;
            StringBuilder sb = new StringBuilder();
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey cls = hklm.OpenSubKey(AudioClassRegPath, false))
                {
                    if (cls != null)
                    {
                        string[] subs = cls.GetSubKeyNames();
                        for (int i = 0; i < subs.Length; i++)
                        {
                            try
                            {
                                using (RegistryKey dev = cls.OpenSubKey(subs[i], false))
                                {
                                    if (dev == null) continue;
                                    string desc = dev.GetValue("DriverDesc") as string;
                                    using (RegistryKey ps = cls.OpenSubKey(subs[i] + "\\PowerSettings", true))
                                    {
                                        if (ps == null) continue;
                                        bool changed = false;
                                        changed |= ZeroRegistryPowerValue(ps, "ConservationIdleTime");
                                        changed |= ZeroRegistryPowerValue(ps, "PerformanceIdleTime");
                                        if (changed)
                                        {
                                            updated++;
                                            sb.AppendLine("Disabled idle sleep on audio driver: " + (desc ?? subs[i]));
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            log = sb.ToString();
            return updated;
        }

        private static bool ZeroRegistryPowerValue(RegistryKey ps, string valueName)
        {
            object val = ps.GetValue(valueName);
            if (val == null) return false;
            RegistryValueKind kind = ps.GetValueKind(valueName);
            if (kind == RegistryValueKind.Binary)
            {
                byte[] bytes = val as byte[];
                if (bytes == null || bytes.Length == 0) return false;
                bool nonZero = false;
                for (int i = 0; i < bytes.Length; i++) if (bytes[i] != 0) nonZero = true;
                if (!nonZero) return false;
                ps.SetValue(valueName, new byte[bytes.Length], RegistryValueKind.Binary);
                return true;
            }
            if (kind == RegistryValueKind.DWord)
            {
                int iv = Convert.ToInt32(val, CultureInfo.InvariantCulture);
                if (iv == 0) return false;
                ps.SetValue(valueName, 0, RegistryValueKind.DWord);
                return true;
            }
            return false;
        }

        private static bool AreAudioIdleTimersDisabled()
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey cls = hklm.OpenSubKey(AudioClassRegPath, false))
                {
                    if (cls == null) return true;
                    string[] subs = cls.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        using (RegistryKey ps = cls.OpenSubKey(subs[i] + "\\PowerSettings", false))
                        {
                            if (ps == null) continue;
                            if (IsNonZeroPowerValue(ps, "ConservationIdleTime") || IsNonZeroPowerValue(ps, "PerformanceIdleTime"))
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        private static bool IsNonZeroPowerValue(RegistryKey ps, string valueName)
        {
            object val = ps.GetValue(valueName);
            if (val == null) return false;
            byte[] bytes = val as byte[];
            if (bytes != null)
            {
                for (int i = 0; i < bytes.Length; i++) if (bytes[i] != 0) return true;
                return false;
            }
            try { return Convert.ToInt32(val, CultureInfo.InvariantCulture) != 0; } catch { return false; }
        }

        private static void InspectScreenSaver(out bool active, out int timeoutSec)
        {
            active = false;
            timeoutSec = 0;
            try
            {
                bool isSystem = false;
                try
                {
                    using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    {
                        isSystem = id != null && id.IsSystem;
                    }
                }
                catch { }

                if (!isSystem)
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
                    {
                        ReadScreenSaverKey(k, ref active, ref timeoutSec);
                    }
                    return;
                }

                string[] sids = Registry.Users.GetSubKeyNames();
                for (int i = 0; i < sids.Length; i++)
                {
                    if (sids[i].StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !sids[i].EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        using (RegistryKey k = Registry.Users.OpenSubKey(sids[i] + @"\Control Panel\Desktop", false))
                        {
                            ReadScreenSaverKey(k, ref active, ref timeoutSec);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ReadScreenSaverKey(RegistryKey k, ref bool active, ref int timeoutSec)
        {
            if (k == null) return;
            string act = k.GetValue("ScreenSaveActive") as string;
            string exe = k.GetValue("SCRNSAVE.EXE") as string;
            string tmo = k.GetValue("ScreenSaveTimeOut") as string;
            if (string.Equals(act, "1", StringComparison.Ordinal) && !string.IsNullOrEmpty(exe))
            {
                active = true;
                int parsed;
                if (int.TryParse(tmo, out parsed)) timeoutSec = parsed;
            }
        }

        private void EnsureTimerHandle()
        {
            if (_waitableTimer == IntPtr.Zero)
            {
                _waitableTimer = NativeMethods.CreateWaitableTimer(IntPtr.Zero, true, null);
            }
        }

        private static bool UpsertBackupScheduledTask(DateTime wakeLocal, out string error)
        {
            error = null;
            try
            {
                string appExe = SecurityHelper.GetMainAppExePath();
                string taskArgs = "--ensure-service";
                if (string.IsNullOrEmpty(appExe) || !File.Exists(appExe))
                {
                    appExe = Path.Combine(AppPaths.BinDir, "ua.exe");
                    taskArgs = "ensure-service";
                }

                string startBoundary = wakeLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                string escapedExe = SecurityElement.Escape(appExe);
                string xml = string.Format(
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>UltimateAlarm redundant WakeToRun trigger</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>{0}</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <WakeToRun>true</WakeToRun>
    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{1}</Command>
      <Arguments>{2}</Arguments>
    </Exec>
  </Actions>
</Task>", startBoundary, escapedExe, taskArgs);

                string tmpXml = SecurityHelper.WriteSecureTempXml("wake_task", xml);
                try
                {
                    int code;
                    string output = RunProcessCapture("schtasks.exe", string.Format("/Create /TN \"{0}\" /XML \"{1}\" /F", BackupTaskName, tmpXml), out code);
                    if (code != 0)
                    {
                        error = output;
                        return false;
                    }
                    return true;
                }
                finally
                {
                    try { File.Delete(tmpXml); } catch { }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void DeleteBackupScheduledTask()
        {
            try
            {
                int code;
                RunProcessCapture("schtasks.exe", string.Format("/Delete /TN \"{0}\" /F", BackupTaskName), out code);
            }
            catch { }
        }

        public static WakeDiagnostics CollectDiagnostics(AlarmConfig cfg, DateTime? nextWake)
        {
            WakeDiagnostics d = new WakeDiagnostics();
            d.IsAdministrator = SecurityHelper.IsAdministrator();

            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    d.RunningAsLocalSystem = id != null && id.IsSystem;
                }
            }
            catch { }

            ServiceController[] svcs = null;
            try
            {
                svcs = ServiceController.GetServices();
                for (int i = 0; i < svcs.Length; i++)
                {
                    if (string.Equals(svcs[i].ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        d.ServiceInstalled = true;
                        d.ServiceRunning = svcs[i].Status == ServiceControllerStatus.Running;
                        break;
                    }
                }
            }
            catch { }
            finally
            {
                if (svcs != null)
                {
                    for (int i = 0; i < svcs.Length; i++)
                    {
                        try { svcs[i].Dispose(); } catch { }
                    }
                }
            }

            int code;
            string powerA = RunProcessCapture("powercfg.exe", "/a", out code);
            string availableSection = powerA;
            int notAvailIdx = powerA.IndexOf("not available on this system", StringComparison.OrdinalIgnoreCase);
            if (notAvailIdx > 0)
            {
                availableSection = powerA.Substring(0, notAvailIdx);
            }

            d.SleepS3Supported = availableSection.IndexOf("S3", StringComparison.OrdinalIgnoreCase) >= 0;
            d.ModernStandbyS0Supported = availableSection.IndexOf("S0 Low Power Idle", StringComparison.OrdinalIgnoreCase) >= 0;
            d.HibernateS4Supported = availableSection.IndexOf("Hibernate", StringComparison.OrdinalIgnoreCase) >= 0;
            d.FastStartupEnabled = availableSection.IndexOf("Fast Startup", StringComparison.OrdinalIgnoreCase) >= 0;
            d.PowerSummary = powerA.Trim();
            d.ActivePowerPlanName = GetActivePowerSchemeName();

            string wakeQ = RunProcessCapture("powercfg.exe", string.Format("/q SCHEME_CURRENT {0} {1}", SleepSubgroupGuid, RtcWakeSettingGuid), out code);
            d.WakeTimersEnabledAC = wakeQ.IndexOf("Current AC Power Setting Index: 0x00000001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    wakeQ.IndexOf("Current AC Power Setting Index: 0x00000002", StringComparison.OrdinalIgnoreCase) >= 0;
            d.WakeTimersEnabledDC = wakeQ.IndexOf("Current DC Power Setting Index: 0x00000001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    wakeQ.IndexOf("Current DC Power Setting Index: 0x00000002", StringComparison.OrdinalIgnoreCase) >= 0;

            string usbQ = RunProcessCapture("powercfg.exe", string.Format("/q SCHEME_CURRENT {0} {1}", UsbSubgroupGuid, UsbSelectiveSuspendGuid), out code);
            d.UsbSelectiveSuspendDisabled = usbQ.IndexOf("Current AC Power Setting Index: 0x00000000", StringComparison.OrdinalIgnoreCase) >= 0;
            d.AudioIdleSleepDisabled = AreAudioIdleTimersDisabled();

            bool scrActive;
            int scrTimeout;
            InspectScreenSaver(out scrActive, out scrTimeout);
            d.ScreenSaverActive = scrActive;
            d.ScreenSaverTimeoutSeconds = scrTimeout;

            RunProcessCapture("schtasks.exe", string.Format("/Query /TN \"{0}\"", BackupTaskName), out code);
            d.TaskBackupRegistered = code == 0;
            d.TrayAutoStartRegistered = SecurityHelper.IsTrayAutoStartRegistered();
            d.StartupExposedInTaskManager = SecurityHelper.IsTrayExposedInTaskManagerStartup();

            string autoUser;
            d.AutoLogonEnabled = SecurityHelper.IsAutoLogonConfigured(out autoUser);
            d.AutoLogonUser = autoUser;
            d.HasLsaPasswordStored = d.AutoLogonEnabled;
            d.AutoLockOnBoot = cfg != null && cfg.AutoLockOnBoot;

            HardwareInfo hw = SecurityHelper.GetHardwareInfo();
            d.MotherboardManufacturer = hw.BaseBoardManufacturer;
            d.MotherboardModel = !string.IsNullOrEmpty(hw.BaseBoardProduct) ? hw.BaseBoardProduct : hw.SystemProductName;
            d.BiosVendor = hw.BiosVendor;
            d.BiosVersion = hw.BiosVersion;
            d.BiosRtcGuide = SecurityHelper.BuildBiosRtcGuide(hw);

            d.NextWakeIso = nextWake.HasValue ? nextWake.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "None";

            List<AudioDeviceInfo> targets = AudioRouter.SelectTargetDevices(cfg, null);
            if (cfg != null && cfg.RingAllOutputDevices && targets.Count > 0)
            {
                List<string> names = new List<string>();
                for (int i = 0; i < targets.Count; i++)
                {
                    names.Add(targets[i].FriendlyName);
                }
                d.SelectedSpeakerName = "ALL (" + targets.Count + "): " + string.Join(" + ", names.ToArray());
                d.SelectedSpeakerId = "ALL";
            }
            else if (targets.Count > 0)
            {
                d.SelectedSpeakerName = targets[0].FriendlyName;
                d.SelectedSpeakerId = targets[0].Id;
            }
            else
            {
                d.SelectedSpeakerName = "None eligible";
                d.SelectedSpeakerId = "";
            }
            return d;
        }

        public static string RunProcessCapture(string exe, string args, out int exitCode)
        {
            exitCode = -1;
            try
            {
                string resolvedExe = Path.IsPathRooted(exe) ? exe : SecurityHelper.ResolveSystemTool(exe);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = resolvedExe;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();
                using (Process p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stdout.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stderr.AppendLine(e.Data);
                    };
                    if (!p.Start())
                    {
                        return "Failed to start " + resolvedExe;
                    }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return "Timed out running " + exe;
                    }
                    // Parameterless WaitForExit flushes async output/error event handlers cleanly.
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                    return (stdout.ToString() + "\n" + stderr.ToString()).Trim();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public void Dispose()
        {
            if (_waitableTimer != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_waitableTimer);
                _waitableTimer = IntPtr.Zero;
            }
        }
    }
}
