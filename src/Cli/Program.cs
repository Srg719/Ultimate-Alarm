using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace UltimateAlarm.Cli
{
    using UltimateAlarm.Core;

    internal static class Program
    {
        [MTAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "setup":
                        return CmdSetup();
                    case "install":
                        return CmdInstall();
                    case "uninstall":
                        return CmdUninstall(args);
                    case "fix-wake":
                        return CmdFixWake();
                    case "status":
                        return CmdStatus();
                    case "devices":
                        return CmdDevices();
                    case "diag":
                        return CmdDiag();
                    case "add":
                        return CmdAdd(args);
                    case "enable":
                        return CmdToggle(args, true);
                    case "disable":
                        return CmdToggle(args, false);
                    case "remove":
                    case "delete":
                        return CmdRemove(args);
                    case "dismiss":
                        return CmdSimpleIpc("DISMISS", "Alarm dismissed.");
                    case "snooze":
                        return CmdSnooze(args);
                    case "pin-speaker":
                        return CmdPinSpeaker(args);
                    case "ring-all":
                        return CmdRingAll(args);
                    case "volume":
                        return CmdVolume(args);
                    case "presets":
                        return CmdPresets();
                    case "theme":
                        return CmdTheme(args);
                    case "test-ring":
                        return CmdTestRing(args);
                    case "ensure-service":
                        return CmdEnsureService();
                    default:
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static ServiceStatusResponse QueryOrFallback(IpcRequest req, out bool fromService)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(req, 1200);
            if (resp != null && resp.Success)
            {
                fromService = true;
                return resp;
            }
            fromService = false;
            return null;
        }

        private static int CmdSetup()
        {
            Console.WriteLine("============================================================");
            Console.WriteLine("   UltimateAlarm Universal Setup Wizard (CLI)");
            Console.WriteLine("============================================================");
            Console.WriteLine();

            if (!SecurityHelper.IsAdministrator())
            {
                Console.WriteLine("[!] Administrator privileges are needed to:");
                Console.WriteLine("    - Install UltimateAlarmService (LocalSystem pre-login engine)");
                Console.WriteLine("    - Enable Windows AC/DC Wake Timers & Task Scheduler WakeToRun");
                Console.WriteLine("    - Register Tray auto-start & boot protections");
                Console.Write("Request UAC elevation now? [Y/n]: ");
                string ans = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrEmpty(ans) || ans.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                {
                    string exe = Path.Combine(AppPaths.BinDir, "ua.exe");
                    string err;
                    if (SecurityHelper.RelaunchElevated(exe, "setup", out err))
                    {
                        Console.WriteLine("Elevated setup launched in a new Administrator window.");
                        return 0;
                    }
                    Console.Error.WriteLine("Elevation failed: " + err);
                    return 1;
                }
                return 1;
            }

            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = store.LoadConfig();
            HardwareInfo hw = SecurityHelper.GetHardwareInfo();
            Console.WriteLine("Hardware detected : " + hw.BaseBoardManufacturer + " " +
                (!string.IsNullOrEmpty(hw.BaseBoardProduct) ? hw.BaseBoardProduct : hw.SystemProductName));
            Console.WriteLine("BIOS              : " + hw.BiosVendor + " " + hw.BiosVersion);
            Console.WriteLine();

            // Step 1: Speaker selection
            Console.WriteLine("--- Step 1/4: Speaker Verification ---");
            List<AudioDeviceInfo> devices = AudioRouter.EnumerateActiveOutputDevices();
            if (devices.Count == 0)
            {
                Console.WriteLine("No active Windows audio output devices detected right now.");
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    AudioDeviceInfo d = devices[i];
                    Console.WriteLine(string.Format("  [{0}] [{1,-12}] {2} (Vol={3:0}%, Muted={4})",
                        i + 1, d.Category, d.FriendlyName, d.VolumeScalar * 100f, d.IsMuted));
                }
                Console.Write("Ring ALL connected output devices simultaneously? [y/N]: ");
                string allAns = (Console.ReadLine() ?? "").Trim();
                if (allAns.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.RingAllOutputDevices = true;
                    cfg.PinnedSpeakerDeviceId = "";
                    Console.Write("Also grant permission to ring Headphones/Headsets at 100%? [y/N]: ");
                    string hpAns = (Console.ReadLine() ?? "").Trim();
                    cfg.AllowHeadphoneFallback = hpAns.StartsWith("y", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine("Multi-output broadcast enabled (Headphones=" + (cfg.AllowHeadphoneFallback ? "Included" : "Blocked") + ").");
                }
                else
                {
                    cfg.RingAllOutputDevices = false;
                    Console.Write("Select speaker number, or press Enter for Auto-Detect Physical Speakers: ");
                    string spkAns = (Console.ReadLine() ?? "").Trim();
                    int idx;
                    if (int.TryParse(spkAns, out idx) && idx >= 1 && idx <= devices.Count)
                    {
                        cfg.PinnedSpeakerDeviceId = devices[idx - 1].Id;
                        Console.WriteLine("Pinned: " + devices[idx - 1].FriendlyName);
                    }
                    else
                    {
                        cfg.PinnedSpeakerDeviceId = "";
                        Console.WriteLine("Using smart Auto-Detect Speakers (never rings on headphones).");
                    }
                }

                Console.Write("Target alarm volume % [30-100, default=80]: ");
                string volAns = (Console.ReadLine() ?? "").Trim();
                int parsedVol;
                cfg.AlarmVolumePercent = int.TryParse(volAns, out parsedVol)
                    ? AudioRouter.ClampVolumePercent(parsedVol)
                    : AudioRouter.DefaultVolumePercent;
                Console.WriteLine("Alarm volume set to " + cfg.AlarmVolumePercent + "% (minimum floor: 30%).");

                Console.Write("Run 3-second " + cfg.AlarmVolumePercent + "% unmuted speaker test now? [Y/n]: ");
                string testAns = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrEmpty(testAns) || testAns.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                {
                    ActiveRingState testRing = new ActiveRingState
                    {
                        SoundPreset = "KlaxonPulse",
                        ForceFullVolume = true,
                        TargetVolumePercent = cfg.AlarmVolumePercent,
                        IsTest = true
                    };
                    using (ManualResetEvent stop = new ManualResetEvent(false))
                    {
                        AudioRouter.PlayAlarmLoop(cfg, testRing, stop, 3);
                    }
                }
            }
            Console.WriteLine();

            // Step 2: Windows Sign-In & Boot Security
            Console.WriteLine("--- Step 2/4: Windows Boot & Sign-In Mode ---");
            Console.WriteLine("  [1] Mode A (Recommended, zero password stored):");
            Console.WriteLine("      Service runs as LocalSystem and rings at the Windows password/lock screen.");
            Console.WriteLine("  [2] Mode B (Boot Auto-Lock Guard):");
            Console.WriteLine("      Enables automatic LockWorkStation immediately after boot if you use");
            Console.WriteLine("      Windows Auto-Logon (configured via Microsoft Sysinternals Autologon / netplwiz).");
            Console.Write("Choose mode [1/2, default=1]: ");
            string modeAns = (Console.ReadLine() ?? "").Trim();
            if (modeAns == "2")
            {
                cfg.AuthMode = "AutoLogonAndLock";
                cfg.AutoLockOnBoot = true;
                Console.WriteLine("Configured Boot Auto-Lock Guard.");
            }
            else
            {
                cfg.AuthMode = "LocalSystem";
            }
            Console.WriteLine();

            // Step 3: Dismiss PIN + First Alarm
            Console.WriteLine("--- Step 3/4: Dismiss Challenge & First Alarm ---");
            Console.Write("Optional custom Dismiss PIN (press Enter to skip / use Math challenge): ");
            string pin = ReadMaskedInput();
            Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(pin))
            {
                string hash, salt;
                SecurityHelper.CreatePinHash(pin.Trim(), out hash, out salt);
                cfg.DismissPinHash = hash;
                cfg.DismissPinSalt = salt;
                Console.WriteLine("Dismiss PIN hashed with PBKDF2 + random salt.");
            }

            Console.Write("Create/arm an initial alarm now? [Y/n]: ");
            string addAns = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrEmpty(addAns) || addAns.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("Alarm time (24h HH:mm, e.g. 07:00): ");
                string timeStr = (Console.ReadLine() ?? "").Trim();
                DateTime parsed;
                if (DateTime.TryParseExact(timeStr, new string[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    Console.Write("Label (default 'Wake up'): ");
                    string lbl = (Console.ReadLine() ?? "").Trim();
                    Console.Write("Repeat days (daily / weekdays / mon,wed,fri / once, default=daily): ");
                    string daysStr = (Console.ReadLine() ?? "").Trim();
                    if (string.IsNullOrEmpty(daysStr)) daysStr = "daily";

                    AlarmItem item = new AlarmItem();
                    item.Hour = parsed.Hour;
                    item.Minute = parsed.Minute;
                    item.Label = string.IsNullOrEmpty(lbl) ? "Wake up" : lbl;
                    item.DaysOfWeek = ParseDays(daysStr);
                    if (!string.IsNullOrEmpty(cfg.DismissPinHash))
                    {
                        item.ChallengeType = "Pin";
                    }
                    if (item.IsOneShot())
                    {
                        item.EnsureOneShotDate(DateTime.Now);
                    }
                    cfg.Alarms.Add(item);
                }
                else
                {
                    Console.WriteLine("Invalid time skipped; you can add alarms anytime via UI or 'ua add'.");
                }
            }
            Console.WriteLine();

            // Step 4: Execute all system configuration
            Console.WriteLine("--- Step 4/4: Applying All Permissions & Configuration ---");
            cfg.SetupCompleted = true;
            cfg.LaunchUiInTrayAtLogon = true;
            cfg.EnableTaskSchedulerWakeBackup = true;
            store.SaveConfig(cfg);
            Console.WriteLine("[OK] Configuration saved (AuthMode=" + cfg.AuthMode + ").");

            string wakeLog, wakeErr;
            if (PowerWakeManager.EnableWakeTimers(out wakeLog, out wakeErr))
            {
                Console.WriteLine("[OK] Enabled Windows AC & DC Wake Timers.");
            }
            else
            {
                Console.Error.WriteLine("[WARN] Wake timer setup: " + wakeErr);
            }

            string trayErr;
            if (SecurityHelper.SetTrayAutoStart(true, out trayErr))
            {
                Console.WriteLine("[OK] Registered UltimateAlarm Tray UI at Windows logon.");
            }

            string svcLog, svcErr;
            if (!SecurityHelper.InstallOrRepairService(out svcLog, out svcErr))
            {
                Console.Error.WriteLine("[ERROR] Service installation failed: " + svcErr);
                return 1;
            }
            Console.WriteLine("[OK] UltimateAlarmService installed and RUNNING as LocalSystem.");
            Console.WriteLine();
            Console.WriteLine(SecurityHelper.BuildBiosRtcGuide(hw));
            Console.WriteLine();
            Console.WriteLine("Setup complete! Launch UltimateAlarm.exe or run 'ua status' anytime.");
            return 0;
        }

        private static int CmdInstall()
        {
            if (!SecurityHelper.IsAdministrator())
            {
                Console.Error.WriteLine("Run 'ua install' from an Administrator terminal (or run 'ua setup').");
                return 1;
            }
            string wakeLog, wakeErr;
            PowerWakeManager.OptimizePowerForAlarms(out wakeLog, out wakeErr);
            Console.WriteLine(wakeLog.Trim());

            string trayErr;
            SecurityHelper.SetTrayAutoStart(true, out trayErr);

            string svcLog, svcErr;
            if (!SecurityHelper.InstallOrRepairService(out svcLog, out svcErr))
            {
                Console.Error.WriteLine("Install failed: " + svcErr);
                return 1;
            }
            Console.WriteLine(svcLog.Trim());
            return 0;
        }

        private static int CmdUninstall(string[] args)
        {
            if (!SecurityHelper.IsAdministrator())
            {
                Console.Error.WriteLine("Run 'ua uninstall' from an Administrator terminal.");
                return 1;
            }
            string log, err;
            if (!SecurityHelper.UninstallServiceAndTasks(out log, out err))
            {
                Console.Error.WriteLine("Uninstall failed: " + err);
                return 1;
            }
            Console.WriteLine(log.Trim());
            return 0;
        }

        private static int CmdFixWake()
        {
            if (!SecurityHelper.IsAdministrator())
            {
                Console.Error.WriteLine("Run 'ua fix-wake' from an Administrator terminal.");
                return 1;
            }
            string log, err;
            bool ok = PowerWakeManager.OptimizePowerForAlarms(out log, out err);
            Console.WriteLine(log.Trim());
            if (!ok)
            {
                Console.Error.WriteLine("Failed: " + err);
                return 1;
            }
            return 0;
        }

        private static int CmdStatus()
        {
            bool fromService;
            IpcRequest req = new IpcRequest { Command = "GET_DIAGNOSTICS" };
            ServiceStatusResponse resp = QueryOrFallback(req, out fromService);

            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = fromService ? resp.Config : store.LoadConfig();
            ActiveRingState ring = fromService ? resp.ActiveRing : store.LoadActiveRing();

            Console.WriteLine("=== UltimateAlarm Status ===");
            Console.WriteLine("Service IPC : " + (fromService ? "ONLINE (Next wake: " + resp.NextWakeIso + ")" : "OFFLINE (reading local config)"));
            Console.WriteLine("Auth Mode   : " + cfg.AuthMode + (cfg.AuthMode == "AutoLogonAndLock" ? (" (AutoLockOnBoot=" + cfg.AutoLockOnBoot + ")") : ""));
            if (ring != null)
            {
                Console.WriteLine("ACTIVE RING : " + ring.Label + " (started " + ring.StartedAtIso + ")");
            }
            Console.WriteLine("Audio Mode  : " + (cfg.RingAllOutputDevices ? "RING ALL CONNECTED OUTPUTS (Headphones=" + (cfg.AllowHeadphoneFallback ? "Allowed" : "Blocked") + ")" : (string.IsNullOrEmpty(cfg.PinnedSpeakerDeviceId) ? "Auto-detect Speakers" : cfg.PinnedSpeakerDeviceId)));
            Console.WriteLine("Alarm Vol   : " + AudioRouter.ResolveTargetVolumePercent(cfg, null) + "% (Min 30%)");
            Console.WriteLine("Alarms (" + cfg.Alarms.Count + "):");
            for (int i = 0; i < cfg.Alarms.Count; i++)
            {
                AlarmItem a = cfg.Alarms[i];
                int v = a.VolumePercent > 0 ? AudioRouter.ClampVolumePercent(a.VolumePercent) : AudioRouter.ResolveTargetVolumePercent(cfg, null);
                Console.WriteLine(string.Format("  [{0}] {1:00}:{2:00} | {3} | {4} | {5} | {6} | Vol={7}% | Challenge={8}",
                    a.Enabled ? "ON " : "OFF",
                    a.Hour,
                    a.Minute,
                    a.Id,
                    a.FormatDays(),
                    a.Label,
                    a.SoundPreset,
                    v,
                    a.ChallengeType));
            }
            return 0;
        }

        private static int CmdDevices()
        {
            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = store.LoadConfig();
            List<AudioDeviceInfo> devices = AudioRouter.EnumerateActiveOutputDevices();
            List<AudioDeviceInfo> targets = AudioRouter.SelectTargetDevices(cfg, null);

            Console.WriteLine("=== Active Windows Audio Output Devices ===");
            for (int i = 0; i < devices.Count; i++)
            {
                AudioDeviceInfo d = devices[i];
                bool isSel = false;
                for (int t = 0; t < targets.Count; t++)
                {
                    if (string.Equals(targets[t].Id, d.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        isSel = true;
                        break;
                    }
                }
                Console.WriteLine(string.Format("{0} [{1}] {2}",
                    isSel ? ">> SELECTED <<" : "             ",
                    d.Category,
                    d.FriendlyName));
                Console.WriteLine(string.Format("    Id={0} | Form={1} | Score={2} | Vol={3:0}% | Muted={4} | Default={5}",
                    d.Id,
                    d.FormFactorName,
                    d.SpeakerScore,
                    d.VolumeScalar * 100f,
                    d.IsMuted,
                    d.IsDefaultMultimedia ? "Yes" : "No"));
            }
            return 0;
        }

        private static int CmdDiag()
        {
            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = store.LoadConfig();
            WakeDiagnostics diag = PowerWakeManager.CollectDiagnostics(cfg, null);
            bool fromService;
            IpcRequest req = new IpcRequest { Command = "GET_DIAGNOSTICS" };
            ServiceStatusResponse resp = QueryOrFallback(req, out fromService);
            if (fromService && resp.Diagnostics != null)
            {
                diag = resp.Diagnostics;
            }

            Console.WriteLine("=== UltimateAlarm Readiness Diagnostics ===");
            Console.WriteLine("Service Installed : " + diag.ServiceInstalled);
            Console.WriteLine("Service Running   : " + diag.ServiceRunning);
            Console.WriteLine("Running as SYSTEM : " + diag.RunningAsLocalSystem);
            Console.WriteLine("Active Power Plan : " + (diag.ActivePowerPlanName ?? "Unknown"));
            Console.WriteLine("Wake Timers AC/DC : " + diag.WakeTimersEnabledAC + " / " + diag.WakeTimersEnabledDC);
            Console.WriteLine("USB Suspend Off   : " + diag.UsbSelectiveSuspendDisabled);
            Console.WriteLine("Audio Idle Off    : " + diag.AudioIdleSleepDisabled);
            Console.WriteLine("Screen Saver      : " + (diag.ScreenSaverActive ? ("Active (" + diag.ScreenSaverTimeoutSeconds + "s)") : "Inactive"));
            Console.WriteLine("Sleep (S3 / S0)   : " + diag.SleepS3Supported + " / " + diag.ModernStandbyS0Supported);
            Console.WriteLine("Hibernate (S4)    : " + diag.HibernateS4Supported);
            Console.WriteLine("Task Backup       : " + diag.TaskBackupRegistered);
            Console.WriteLine("Tray Auto-Start   : " + diag.TrayAutoStartRegistered + " (TaskMgr Startup Exposed=" + diag.StartupExposedInTaskManager + ")");
            Console.WriteLine("Auto-Logon / Lock : " + diag.AutoLogonEnabled + " (" + (diag.AutoLogonUser ?? "") + ") | AutoLock=" + diag.AutoLockOnBoot);
            Console.WriteLine("Next Wake         : " + diag.NextWakeIso);
            Console.WriteLine("Selected Speaker  : " + diag.SelectedSpeakerName + " (" + diag.SelectedSpeakerId + ")");
            Console.WriteLine();
            Console.WriteLine("--- Cold Shutdown (S5) BIOS Guide ---");
            Console.WriteLine(diag.BiosRtcGuide ?? "");
            return 0;
        }

        private static int CmdAdd(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua add <HH:mm> [--label \"Wake up\"] [--days daily|mon,wed,thu|weekdays|once] [--sound KlaxonPulse|EmergencySiren|EscalatingChime] [--wav path] [--challenge None|Math|TypeCode|Pin]");
                return 1;
            }

            DateTime parsedTime;
            if (!DateTime.TryParseExact(args[1], new string[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
            {
                Console.Error.WriteLine("Invalid time format. Use HH:mm (24-hour), e.g. 06:30");
                return 1;
            }

            AlarmItem item = new AlarmItem();
            item.Hour = parsedTime.Hour;
            item.Minute = parsedTime.Minute;

            for (int i = 2; i < args.Length; i++)
            {
                string opt = args[i].ToLowerInvariant();
                if (opt == "--label" && i + 1 < args.Length)
                {
                    item.Label = args[++i];
                }
                else if (opt == "--sound" && i + 1 < args.Length)
                {
                    item.SoundPreset = args[++i];
                }
                else if (opt == "--wav" && i + 1 < args.Length)
                {
                    item.CustomWavPath = args[++i];
                    item.SoundPreset = "CustomWav";
                }
                else if (opt == "--challenge" && i + 1 < args.Length)
                {
                    item.ChallengeType = args[++i];
                }
                else if (opt == "--catchup" && i + 1 < args.Length)
                {
                    int c;
                    if (int.TryParse(args[++i], out c)) item.BootCatchupMinutes = c;
                }
                else if (opt == "--volume" && i + 1 < args.Length)
                {
                    int v;
                    if (int.TryParse(args[++i], out v)) item.VolumePercent = AudioRouter.ClampVolumePercent(v);
                }
                else if (opt == "--days" && i + 1 < args.Length)
                {
                    item.DaysOfWeek = ParseDays(args[++i]);
                }
            }

            if (item.IsOneShot())
            {
                item.EnsureOneShotDate(DateTime.Now);
            }

            IpcRequest req = new IpcRequest { Command = "ADD_OR_UPDATE_ALARM", Alarm = item };
            bool fromService;
            QueryOrFallback(req, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                cfg.Alarms.Add(item);
                store.SaveConfig(cfg);
            }

            Console.WriteLine(string.Format("Added alarm {0} at {1:00}:{2:00} ({3}) [{4}]",
                item.Id, item.Hour, item.Minute, item.FormatDays(), item.Label));
            return 0;
        }

        private static int[] ParseDays(string spec)
        {
            string s = (spec ?? "").Trim().ToLowerInvariant();
            if (s == "once" || s == "none" || s == "") return new int[0];
            if (s == "daily" || s == "everyday") return new int[] { 0, 1, 2, 3, 4, 5, 6 };
            if (s == "weekdays") return new int[] { 1, 2, 3, 4, 5 };

            string[] parts = s.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<int> days = new List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int d = -1;
                if (p.StartsWith("su")) d = 0;
                else if (p.StartsWith("mo")) d = 1;
                else if (p.StartsWith("tu")) d = 2;
                else if (p.StartsWith("we")) d = 3;
                else if (p.StartsWith("th")) d = 4;
                else if (p.StartsWith("fr")) d = 5;
                else if (p.StartsWith("sa")) d = 6;
                if (d >= 0 && !days.Contains(d)) days.Add(d);
            }
            return days.ToArray();
        }

        private static int CmdToggle(string[] args, bool enable)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua " + (enable ? "enable" : "disable") + " <alarmId>");
                return 1;
            }
            string id = args[1];
            IpcRequest req = new IpcRequest { Command = "TOGGLE_ALARM", AlarmId = id, BoolFlag = enable };
            bool fromService;
            QueryOrFallback(req, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                for (int i = 0; i < cfg.Alarms.Count; i++)
                {
                    if (string.Equals(cfg.Alarms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms[i].Enabled = enable;
                    }
                }
                store.SaveConfig(cfg);
            }
            Console.WriteLine("Alarm " + id + " " + (enable ? "enabled" : "disabled") + ".");
            return 0;
        }

        private static int CmdRemove(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua remove <alarmId>");
                return 1;
            }
            string id = args[1];
            IpcRequest req = new IpcRequest { Command = "DELETE_ALARM", AlarmId = id };
            bool fromService;
            QueryOrFallback(req, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                for (int i = cfg.Alarms.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(cfg.Alarms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms.RemoveAt(i);
                    }
                }
                store.SaveConfig(cfg);
            }
            Console.WriteLine("Removed alarm " + id + ".");
            return 0;
        }

        private static int CmdSimpleIpc(string command, string successMsg)
        {
            IpcRequest req = new IpcRequest { Command = command };
            ServiceStatusResponse resp = IpcProtocol.SendRequest(req, 2000);
            if (resp != null && resp.Success)
            {
                Console.WriteLine(successMsg);
                return 0;
            }
            Console.Error.WriteLine("Service did not respond: " + (resp != null ? resp.Error : "unknown error"));
            return 1;
        }

        private static int CmdSnooze(string[] args)
        {
            int mins = 5;
            if (args.Length > 1) int.TryParse(args[1], out mins);
            IpcRequest req = new IpcRequest { Command = "SNOOZE", IntParam = mins };
            ServiceStatusResponse resp = IpcProtocol.SendRequest(req, 2000);
            if (resp != null && resp.Success)
            {
                Console.WriteLine("Snoozed for " + mins + " minutes.");
                return 0;
            }
            Console.Error.WriteLine("Service did not respond: " + (resp != null ? resp.Error : "unknown error"));
            return 1;
        }

        private static int CmdPinSpeaker(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua pin-speaker <deviceId|auto>");
                return 1;
            }
            string devId = string.Equals(args[1], "auto", StringComparison.OrdinalIgnoreCase) ? "" : args[1];
            IpcRequest req = new IpcRequest { Command = "SET_PINNED_SPEAKER", DeviceId = devId };
            bool fromService;
            QueryOrFallback(req, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                cfg.PinnedSpeakerDeviceId = devId;
                store.SaveConfig(cfg);
            }
            Console.WriteLine("Pinned speaker set to: " + (string.IsNullOrEmpty(devId) ? "Auto-detect Speakers" : devId));
            return 0;
        }

        private static int CmdRingAll(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua ring-all <on|off> [--include-headphones]");
                return 1;
            }
            bool enable = string.Equals(args[1], "on", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[1], "true", StringComparison.OrdinalIgnoreCase);
            bool includeHeadphones = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--include-headphones", StringComparison.OrdinalIgnoreCase))
                {
                    includeHeadphones = true;
                }
            }

            bool fromService;
            QueryOrFallback(new IpcRequest { Command = "SET_AUDIO_POLICY", StringParam = "RINGALL", BoolFlag = enable }, out fromService);
            if (enable && includeHeadphones)
            {
                bool dummy;
                QueryOrFallback(new IpcRequest { Command = "SET_AUDIO_POLICY", StringParam = "HEADPHONES", BoolFlag = true }, out dummy);
            }

            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                cfg.RingAllOutputDevices = enable;
                if (enable && includeHeadphones) cfg.AllowHeadphoneFallback = true;
                store.SaveConfig(cfg);
            }
            Console.WriteLine("Ring ALL connected output devices: " + (enable ? "ON" : "OFF") +
                (enable ? (" (Headphones=" + (includeHeadphones ? "Included" : "Unchanged") + ")") : ""));
            return 0;
        }

        private static int CmdVolume(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua volume <30-100>");
                return 1;
            }
            int requested;
            if (!int.TryParse(args[1], out requested))
            {
                Console.Error.WriteLine("Invalid volume number. Use an integer from 30 to 100.");
                return 1;
            }
            if (requested < AudioRouter.MinVolumePercent)
            {
                Console.WriteLine("Notice: Minimum allowed alarm volume is 30%. Clamping to 30%.");
            }
            int clamped = AudioRouter.ClampVolumePercent(requested);

            bool fromService;
            QueryOrFallback(new IpcRequest { Command = "SET_AUDIO_POLICY", StringParam = "VOLUME", IntParam = clamped }, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                cfg.AlarmVolumePercent = clamped;
                store.SaveConfig(cfg);
            }
            Console.WriteLine("Default alarm volume set to " + clamped + "%.");
            return 0;
        }

        private static int CmdPresets()
        {
            Console.WriteLine("=== Built-In Synthesized Alarm Ringtones ===");
            for (int i = 0; i < AudioRouter.BuiltInPresets.Length; i++)
            {
                AudioRouter.SoundPresetInfo p = AudioRouter.BuiltInPresets[i];
                Console.WriteLine(string.Format("  {0,-18} {1}", p.Id, p.DisplayName));
            }
            Console.WriteLine("  CustomWav          Custom local .wav file (--wav <path>)");
            return 0;
        }

        private static int CmdTheme(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: ua theme <Default|MoreDark|UltimateDark|Neon>");
                return 1;
            }
            string raw = args[1].Trim();
            string theme = "Default";
            if (string.Equals(raw, "MoreDark", StringComparison.OrdinalIgnoreCase)) theme = "MoreDark";
            else if (string.Equals(raw, "UltimateDark", StringComparison.OrdinalIgnoreCase)) theme = "UltimateDark";
            else if (string.Equals(raw, "Neon", StringComparison.OrdinalIgnoreCase)) theme = "Neon";
            else if (!string.Equals(raw, "Default", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Unknown theme '" + raw + "'. Choose: Default, MoreDark, UltimateDark, Neon");
                return 1;
            }

            bool fromService;
            QueryOrFallback(new IpcRequest { Command = "SET_UI_THEME", StringParam = theme }, out fromService);
            if (!fromService)
            {
                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                cfg.UiTheme = theme;
                store.SaveConfig(cfg);
            }
            Console.WriteLine("UI theme set to: " + theme);
            return 0;
        }

        private static int CmdTestRing(string[] args)
        {
            int secs = 5;
            string preset = "KlaxonPulse";
            bool direct = false;
            int volOverride = 0;

            for (int i = 1; i < args.Length; i++)
            {
                int s;
                if (string.Equals(args[i], "--direct", StringComparison.OrdinalIgnoreCase))
                {
                    direct = true;
                }
                else if (string.Equals(args[i], "--volume", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int v;
                    if (int.TryParse(args[++i], out v)) volOverride = AudioRouter.ClampVolumePercent(v);
                }
                else if (int.TryParse(args[i], out s))
                {
                    secs = Math.Max(1, Math.Min(30, s));
                }
                else
                {
                    preset = args[i];
                }
            }

            if (!direct)
            {
                IpcRequest req = new IpcRequest { Command = "TEST_RING", StringParam = preset, IntParam = volOverride };
                bool fromService;
                QueryOrFallback(req, out fromService);
                if (fromService)
                {
                    Console.WriteLine("Triggered test ring via UltimateAlarmService (" + preset + "). Run 'ua dismiss' to stop early.");
                    return 0;
                }
            }

            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = store.LoadConfig();
            int targetVol = volOverride > 0 ? volOverride : AudioRouter.ResolveTargetVolumePercent(cfg, null);
            List<AudioDeviceInfo> targets = AudioRouter.SelectTargetDevices(cfg, null);
            if (targets.Count == 0)
            {
                Console.Error.WriteLine("No eligible speaker output device found!");
                return 1;
            }
            string targetDesc = targets.Count > 1 ? ("ALL " + targets.Count + " outputs") : targets[0].FriendlyName;
            Console.WriteLine("Direct speaker test -> " + targetDesc + " (" + secs + "s, " + targetVol + "% unmuted, " + preset + ")...");

            ActiveRingState ring = new ActiveRingState
            {
                AlarmId = "DIRECT-TEST",
                Label = "Direct Speaker Test",
                SoundPreset = preset,
                ForceFullVolume = true,
                TargetVolumePercent = targetVol,
                IsTest = true
            };

            using (ManualResetEvent stop = new ManualResetEvent(false))
            {
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    stop.Set();
                };
                AudioRouter.PlayAlarmLoop(cfg, ring, stop, secs);
            }
            Console.WriteLine("Test complete.");
            return 0;
        }

        private static int CmdEnsureService()
        {
            try
            {
                using (ServiceController sc = new ServiceController(PowerWakeManager.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("CLI", "ensure-service failed: " + ex.Message);
                return 1;
            }
        }

        private static string ReadMaskedInput()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
            return sb.ToString();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("UltimateAlarm CLI (ua.exe)");
            Console.WriteLine("  ua setup                                   Interactive first-time setup wizard");
            Console.WriteLine("  ua install | uninstall                     Install/remove service & wake backup");
            Console.WriteLine("  ua fix-wake                                Enable Windows AC & DC wake timers");
            Console.WriteLine("  ua status                                  Show service status & alarms");
            Console.WriteLine("  ua devices                                 List output audio endpoints & scores");
            Console.WriteLine("  ua diag                                    Full wake, security & BIOS readiness");
            Console.WriteLine("  ua add <HH:mm> [--label \"...\"] [--days daily|mon,wed,thu|weekdays|once]");
            Console.WriteLine("         [--sound <preset>] [--wav <local.wav>] [--challenge None|Math|TypeCode|Pin] [--volume 30-100]");
            Console.WriteLine("  ua presets                                 List all 11 built-in ringtones");
            Console.WriteLine("  ua theme <Default|MoreDark|UltimateDark|Neon>");
            Console.WriteLine("  ua enable <id> | disable <id> | remove <id>");
            Console.WriteLine("  ua pin-speaker <deviceId|auto>");
            Console.WriteLine("  ua volume <30-100>                         Set default volume (min 30%)");
            Console.WriteLine("  ua ring-all <on|off> [--include-headphones] Ring all outputs simultaneously");
            Console.WriteLine("  ua test-ring [seconds] [preset] [--volume 30-100] [--direct]");
            Console.WriteLine("  ua dismiss | snooze [minutes]");
            Console.WriteLine("  ua ensure-service");
        }
    }
}
