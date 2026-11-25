using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace UltimateAlarm.Core
{
    public static class SecurityHelper
    {
        private const string WinlogonRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string RunRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string TrayRunValueName = "UltimateAlarmTray";
        public const string TrayLogonTaskName = "UltimateAlarmTrayLogon";
        private const string BiosRegPath = @"HARDWARE\DESCRIPTION\System\BIOS";

        public const string PinConfiguredSentinel = "__CONFIGURED__";

        /// <summary>
        /// Resolves built-in Windows tools exclusively from %SystemRoot%\System32 (or %SystemRoot% for explorer.exe)
        /// to prevent CWE-427 local directory binary planting / privilege escalation.
        /// </summary>
        public static string ResolveSystemTool(string exeName)
        {
            if (string.Equals(exeName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(winDir))
                {
                    string exp = Path.Combine(winDir, "explorer.exe");
                    if (File.Exists(exp)) return exp;
                }
            }
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(sys32))
            {
                string full = Path.Combine(sys32, exeName);
                if (File.Exists(full)) return full;
            }
            return exeName;
        }

        /// <summary>
        /// Validates that a custom WAV path points to a local .wav file (blocking UNC/SMB hash-leak and device paths).
        /// </summary>
        public static bool IsSafeLocalWavPath(string path, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(path)) return true;
            string trimmed = path.Trim();
            if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
                trimmed.StartsWith("\\\\.\\", StringComparison.Ordinal))
            {
                reason = "Network UNC and Windows device paths are blocked for security.";
                return false;
            }
            if (trimmed.Length < 4 || !char.IsLetter(trimmed[0]) || trimmed[1] != ':' || (trimmed[2] != '\\' && trimmed[2] != '/'))
            {
                reason = "Custom WAV must use an absolute local drive path (e.g. E:\\Sounds\\alarm.wav).";
                return false;
            }
            if (trimmed.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                reason = "Path traversal segments ('..') are not allowed in Custom WAV path.";
                return false;
            }
            try
            {
                string full = Path.GetFullPath(trimmed);
                if (!Path.IsPathRooted(full) || full.Length < 4 || full[1] != ':')
                {
                    reason = "Custom WAV must use a local drive path.";
                    return false;
                }
                if (!string.Equals(Path.GetExtension(full), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Custom sound file must have a .wav extension.";
                    return false;
                }
                return true;
            }
            catch
            {
                reason = "Invalid custom WAV file path.";
                return false;
            }
        }

        /// <summary>
        /// Creates a random temporary XML file inside an Admin/SYSTEM-only directory so elevated schtasks.exe
        /// never reads XML from a user-writable folder (prevents CWE-377/CWE-367 TOCTOU privilege escalation).
        /// </summary>
        public static string WriteSecureTempXml(string prefix, string xmlContent)
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "UltimateAlarm",
                "secure_tmp");
            DirectoryInfo di = Directory.CreateDirectory(baseDir);

            try
            {
                DirectorySecurity ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false);
                SecurityIdentifier sysSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                ds.AddAccessRule(new FileSystemAccessRule(sysSid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                ds.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                di.SetAccessControl(ds);
            }
            catch { }

            string path = Path.Combine(baseDir, prefix + "_" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(path, xmlContent, Encoding.Unicode);
            return path;
        }

        public static string GetMainAppExePath()
        {
            string rootExe = Path.Combine(AppPaths.RootDir, "UltimateAlarm.exe");
            if (File.Exists(rootExe))
            {
                return rootExe;
            }
            string mainExe = Path.Combine(AppPaths.BinDir, "UltimateAlarm.exe");
            if (File.Exists(mainExe))
            {
                return mainExe;
            }
            return Assembly.GetExecutingAssembly().Location;
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    if (id == null) return false;
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool RelaunchElevated(string exePath, string arguments, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = arguments ?? "";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppPaths.ExeDir;
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223
                    ? "Administrator permission prompt (UAC) was cancelled."
                    : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void SplitAccountName(string fullAccount, out string domain, out string userName)
        {
            string raw = (fullAccount ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                domain = Environment.MachineName;
                userName = Environment.UserName;
                return;
            }
            int slash = raw.IndexOf('\\');
            if (slash > 0)
            {
                domain = raw.Substring(0, slash).Trim();
                userName = raw.Substring(slash + 1).Trim();
                if (domain == ".")
                {
                    domain = Environment.MachineName;
                }
                return;
            }
            if (raw.Contains("@"))
            {
                domain = "";
                userName = raw;
                return;
            }
            domain = Environment.MachineName;
            userName = raw;
        }

        /// <summary>
        /// Read-only inspection of Windows AutoAdminLogon status (never accesses or writes credentials).
        /// </summary>
        public static bool IsAutoLogonConfigured(out string domainUser)
        {
            domainUser = "";
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey winlogon = hklm.OpenSubKey(WinlogonRegPath, false))
                {
                    if (winlogon == null) return false;
                    string flag = winlogon.GetValue("AutoAdminLogon") as string;
                    string user = winlogon.GetValue("DefaultUserName") as string;
                    string dom = winlogon.GetValue("DefaultDomainName") as string;
                    if (!string.IsNullOrEmpty(user))
                    {
                        domainUser = string.IsNullOrEmpty(dom) ? user : (dom + "\\" + user);
                    }
                    return string.Equals(flag, "1", StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool LockActiveConsoleSession(out string error)
        {
            error = null;
            uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                error = "No active console session.";
                return false;
            }

            IntPtr userToken = IntPtr.Zero;
            try
            {
                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken) || userToken == IntPtr.Zero)
                {
                    error = "No logged-on user token for session " + sessionId + " (Win32=" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                string appExe = GetMainAppExePath();
                if (!File.Exists(appExe))
                {
                    error = "Application executable not found: " + appExe;
                    return false;
                }

                NativeMethods.STARTUPINFO si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                si.lpDesktop = "winsta0\\default";

                NativeMethods.PROCESS_INFORMATION pi;
                string cmdLine = "\"" + appExe + "\" --lock-session";
                bool ok = NativeMethods.CreateProcessAsUser(
                    userToken,
                    appExe,
                    cmdLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    NativeMethods.CREATE_NO_WINDOW | NativeMethods.NORMAL_PRIORITY_CLASS,
                    IntPtr.Zero,
                    AppPaths.RootDir,
                    ref si,
                    out pi);

                if (!ok)
                {
                    error = "CreateProcessAsUser(--lock-session) failed win32=" + Marshal.GetLastWin32Error();
                    return false;
                }

                NativeMethods.CloseHandle(pi.hThread);
                NativeMethods.CloseHandle(pi.hProcess);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (userToken != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(userToken);
                }
            }
        }

        public static void CreatePinHash(string pin, out string hashBase64, out string saltBase64)
        {
            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(pin ?? "", salt, 100000))
            {
                byte[] hash = pbkdf2.GetBytes(32);
                saltBase64 = Convert.ToBase64String(salt);
                hashBase64 = Convert.ToBase64String(hash);
            }
        }

        public static bool VerifyPin(string candidatePin, string hashBase64, string saltBase64)
        {
            if (string.IsNullOrEmpty(hashBase64) || string.IsNullOrEmpty(saltBase64) ||
                string.Equals(hashBase64, PinConfiguredSentinel, StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                byte[] expected = Convert.FromBase64String(hashBase64);
                byte[] salt = Convert.FromBase64String(saltBase64);
                if (expected.Length != 32 || salt.Length < 16)
                {
                    return false;
                }
                using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(candidatePin ?? "", salt, 100000))
                {
                    byte[] actual = pbkdf2.GetBytes(expected.Length);
                    int diff = actual.Length ^ expected.Length;
                    for (int i = 0; i < actual.Length && i < expected.Length; i++)
                    {
                        diff |= actual[i] ^ expected[i];
                    }
                    return diff == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static HardwareInfo GetHardwareInfo()
        {
            HardwareInfo hw = new HardwareInfo();
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = hklm.OpenSubKey(BiosRegPath, false))
                {
                    if (key != null)
                    {
                        hw.BaseBoardManufacturer = (key.GetValue("BaseBoardManufacturer") as string ?? "").Trim();
                        hw.BaseBoardProduct = (key.GetValue("BaseBoardProduct") as string ?? "").Trim();
                        hw.SystemManufacturer = (key.GetValue("SystemManufacturer") as string ?? "").Trim();
                        hw.SystemProductName = (key.GetValue("SystemProductName") as string ?? "").Trim();
                        hw.BiosVendor = (key.GetValue("BIOSVendor") as string ?? "").Trim();
                        hw.BiosVersion = (key.GetValue("BIOSVersion") as string ?? "").Trim();
                    }
                }
            }
            catch { }
            return hw;
        }

        public static string BuildBiosRtcGuide(HardwareInfo hw)
        {
            string mfr = ((hw != null ? hw.BaseBoardManufacturer : "") + " " + (hw != null ? hw.SystemManufacturer : "")).ToUpperInvariant();
            string board = hw != null && !string.IsNullOrEmpty(hw.BaseBoardProduct) ? hw.BaseBoardProduct :
                           (hw != null && !string.IsNullOrEmpty(hw.SystemProductName) ? hw.SystemProductName : "your PC");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Detected Hardware: " + (hw != null ? hw.BaseBoardManufacturer : "Unknown") + " " + board +
                          (hw != null && !string.IsNullOrEmpty(hw.BiosVendor) ? (" (" + hw.BiosVendor + " " + hw.BiosVersion + ")") : ""));
            sb.AppendLine("Windows software cannot schedule a wake-up from a full S5 power-off; only Sleep (S3/S0) and Hibernate (S4) can be armed dynamically by Windows.");
            sb.AppendLine("To auto-start from a complete S5 shutdown and ring at the sign-in screen:");

            if (mfr.Contains("ASUS"))
            {
                sb.AppendLine("1. Reboot and press Del or F2 to enter ASUS UEFI/BIOS.");
                sb.AppendLine("2. Go to Advanced -> APM Configuration -> Power On By RTC -> [Enabled].");
                sb.AppendLine("3. Set RTC Alarm Date = [0] (Every Day) and set Hour/Min/Sec 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("GIGABYTE"))
            {
                sb.AppendLine("1. Reboot and press Del to enter Gigabyte BIOS.");
                sb.AppendLine("2. Go to Power Management / Settings -> Platform Power -> Resume by Alarm -> [Enabled].");
                sb.AppendLine("3. Set Wake up day = 0 (Every Day) and Hour/Min/Sec 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("MSI") || mfr.Contains("MICRO-STAR"))
            {
                sb.AppendLine("1. Reboot and press Del to enter MSI Click BIOS.");
                sb.AppendLine("2. Go to Settings -> Advanced -> Wake Up Event Setup -> Resume By RTC Alarm -> [Enabled].");
                sb.AppendLine("3. Set Date = 0 (Every Day) and Time 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("ASROCK"))
            {
                sb.AppendLine("1. Reboot and press F2 or Del to enter ASRock UEFI.");
                sb.AppendLine("2. Go to Advanced -> ACPI Configuration -> RTC Alarm Power On -> [Enabled].");
                sb.AppendLine("3. Configure Everyday / Time 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("DELL"))
            {
                sb.AppendLine("1. Reboot and press F2 to enter Dell BIOS Setup.");
                sb.AppendLine("2. Go to Power Management -> Auto On Time -> Every Day / Weekdays, 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("HP") || mfr.Contains("HEWLETT"))
            {
                sb.AppendLine("1. Reboot and press F10 to enter HP Computer Setup.");
                sb.AppendLine("2. Look under Advanced -> Power-On Options / BIOS Power-On -> set days & time 2-3 minutes BEFORE your alarm.");
            }
            else if (mfr.Contains("LENOVO"))
            {
                sb.AppendLine("1. Reboot and press F1 / F2 / Fn+F2 to enter Lenovo BIOS.");
                sb.AppendLine("2. Look under Power -> Automatic Power On / Wake Up on Alarm -> set Daily time 2-3 minutes BEFORE your alarm.");
            }
            else
            {
                sb.AppendLine("1. Reboot and press Del / F2 / F10 to enter BIOS/UEFI Setup.");
                sb.AppendLine("2. Under Power / ACPI / APM Configuration, enable 'Power On By RTC' or 'Resume by Alarm'.");
                sb.AppendLine("3. Set daily wake time 2-3 minutes BEFORE your morning alarm.");
            }

            sb.AppendLine("4. Save & Exit (usually F10). Keep your Windows password enabled; UltimateAlarmService boots as LocalSystem and rings within the Boot Catch-up window.");
            return sb.ToString().TrimEnd();
        }

        public static void EnsureWritableDataFolders()
        {
            AppPaths.EnsureDirectories();
            try
            {
                SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

                string[] dirs = new string[] { AppPaths.DataDir, AppPaths.LogsDir };
                for (int i = 0; i < dirs.Length; i++)
                {
                    DirectoryInfo di = new DirectoryInfo(dirs[i]);
                    DirectorySecurity ds = di.GetAccessControl();
                    ds.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    ds.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    ds.AddAccessRule(new FileSystemAccessRule(usersSid, FileSystemRights.Modify,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    di.SetAccessControl(ds);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Security", "Folder ACL adjustment skipped: " + ex.Message);
            }
        }

        public static bool InstallOrRepairService(out string log, out string error)
        {
            return InstallOrRepairServiceAt(GetMainAppExePath(), out log, out error);
        }

        public static bool InstallOrRepairServiceAt(string appExePath, out string log, out string error)
        {
            StringBuilder sb = new StringBuilder();
            error = null;

            if (!IsAdministrator())
            {
                error = "Administrator privilege is required to install or repair UltimateAlarmService.";
                log = "";
                return false;
            }

            if (string.IsNullOrEmpty(appExePath) || !File.Exists(appExePath))
            {
                error = "UltimateAlarm executable not found at: " + appExePath;
                log = "";
                return false;
            }

            EnsureWritableDataFolders();
            sb.AppendLine("Data/logs folders verified at " + AppPaths.DataDir);
            RemoveLegacyRunStartupEntries();

            bool exists = ServiceExists(PowerWakeManager.ServiceName);
            if (exists)
            {
                sb.AppendLine("Existing " + PowerWakeManager.ServiceName + " found; stopping before updating...");
                StopServiceIfRunning(PowerWakeManager.ServiceName);
            }

            int code;
            string binArg = "\"\\\"" + appExePath + "\\\" --service\"";
            if (!exists)
            {
                string createOut = PowerWakeManager.RunProcessCapture("sc.exe",
                    string.Format("create \"{0}\" binPath= {1} start= auto DisplayName= \"UltimateAlarm Power & Audio Service\" obj= LocalSystem",
                        PowerWakeManager.ServiceName, binArg),
                    out code);
                sb.AppendLine("sc create -> exit " + code + ": " + createOut);
                if (code != 0)
                {
                    error = "sc.exe create failed: " + createOut;
                    log = sb.ToString();
                    return false;
                }
            }
            else
            {
                string cfgOut = PowerWakeManager.RunProcessCapture("sc.exe",
                    string.Format("config \"{0}\" binPath= {1} start= auto DisplayName= \"UltimateAlarm Power & Audio Service\" obj= LocalSystem",
                        PowerWakeManager.ServiceName, binArg),
                    out code);
                sb.AppendLine("sc config -> exit " + code + ": " + cfgOut);
                if (code != 0)
                {
                    error = "sc.exe config failed: " + cfgOut;
                    log = sb.ToString();
                    return false;
                }
            }

            PowerWakeManager.RunProcessCapture("sc.exe",
                string.Format("description \"{0}\" \"Bulletproof LocalSystem alarm engine with sleep wake timers, speaker-forced 100% volume audio, and pre-login support.\"",
                    PowerWakeManager.ServiceName),
                out code);

            PowerWakeManager.RunProcessCapture("sc.exe",
                string.Format("failure \"{0}\" reset= 86400 actions= restart/5000/restart/10000/restart/30000",
                    PowerWakeManager.ServiceName),
                out code);
            sb.AppendLine("Configured automatic crash recovery (restart on failure).");

            try
            {
                using (ServiceController sc = new ServiceController(PowerWakeManager.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    sb.AppendLine("UltimateAlarmService is now RUNNING as LocalSystem.");
                }
            }
            catch (Exception ex)
            {
                error = "Failed to start UltimateAlarmService: " + ex.Message;
                log = sb.ToString();
                return false;
            }

            log = sb.ToString();
            return true;
        }

        public static bool UninstallServiceAndTasks(out string log, out string error)
        {
            StringBuilder sb = new StringBuilder();
            error = null;

            if (!IsAdministrator())
            {
                error = "Administrator privilege is required to uninstall UltimateAlarmService.";
                log = "";
                return false;
            }

            if (ServiceExists(PowerWakeManager.ServiceName))
            {
                StopServiceIfRunning(PowerWakeManager.ServiceName);
                int code;
                string delOut = PowerWakeManager.RunProcessCapture("sc.exe", "delete \"" + PowerWakeManager.ServiceName + "\"", out code);
                sb.AppendLine("sc delete -> exit " + code + ": " + delOut);
            }
            else
            {
                sb.AppendLine("Service was not installed.");
            }

            PowerWakeManager.DeleteBackupScheduledTask();
            sb.AppendLine("Removed scheduled wake backup task (if present).");

            string trayErr;
            SetTrayAutoStart(false, out trayErr);
            sb.AppendLine("Removed tray UI auto-start entry.");

            log = sb.ToString();
            return true;
        }

        public static bool SetTrayAutoStart(bool enabled, out string error)
        {
            return SetTrayAutoStartForExe(GetMainAppExePath(), enabled, out error);
        }

        /// <summary>
        /// Removes legacy Run / StartupApproved entries so UltimateAlarm never shows up in Task Manager Startup
        /// where users or optimizers could accidentally disable it.
        /// </summary>
        public static void RemoveLegacyRunStartupEntries()
        {
            string[] regPaths = new string[] { RunRegPath, StartupApprovedRunPath };
            for (int i = 0; i < regPaths.Length; i++)
            {
                try
                {
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = hklm.OpenSubKey(regPaths[i], true))
                    {
                        if (key != null) key.DeleteValue(TrayRunValueName, false);
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(regPaths[i], true))
                    {
                        if (key != null) key.DeleteValue(TrayRunValueName, false);
                    }
                }
                catch { }
            }
        }

        public static bool IsTrayExposedInTaskManagerStartup()
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey runKey = hklm.OpenSubKey(RunRegPath, false))
                {
                    if (runKey != null && runKey.GetValue(TrayRunValueName) != null) return true;
                }
            }
            catch { }

            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunRegPath, false))
                {
                    if (runKey != null && runKey.GetValue(TrayRunValueName) != null) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool SetTrayAutoStartForExe(string appExe, bool enabled, out string error)
        {
            error = null;
            RemoveLegacyRunStartupEntries();

            if (!enabled)
            {
                int delCode;
                PowerWakeManager.RunProcessCapture("schtasks.exe", string.Format("/Delete /TN \"{0}\" /F", TrayLogonTaskName), out delCode);
                return true;
            }

            if (!IsAdministrator())
            {
                error = "Administrator privilege is required to register the protected logon task (UltimateAlarmService also launches the tray UI automatically).";
                return false;
            }

            try
            {
                string escapedExe = System.Security.SecurityElement.Escape(appExe);
                string xml = string.Format(
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>UltimateAlarm Protected Tray &amp; Dismiss Challenge UI</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
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
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{0}</Command>
      <Arguments>--tray</Arguments>
    </Exec>
  </Actions>
</Task>", escapedExe);

                string tmpXml = WriteSecureTempXml("tray_logon", xml);
                try
                {
                    int code;
                    string output = PowerWakeManager.RunProcessCapture("schtasks.exe", string.Format("/Create /TN \"{0}\" /XML \"{1}\" /F", TrayLogonTaskName, tmpXml), out code);
                    if (code != 0)
                    {
                        error = "schtasks create failed: " + output;
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

        public static bool IsTrayAutoStartRegistered()
        {
            int code;
            PowerWakeManager.RunProcessCapture("schtasks.exe", string.Format("/Query /TN \"{0}\"", TrayLogonTaskName), out code);
            if (code == 0) return true;
            return IsTrayExposedInTaskManagerStartup();
        }

        public static void MigrateLegacyTrayRunKeyIfNeeded(bool launchTrayEnabled)
        {
            if (IsTrayExposedInTaskManagerStartup())
            {
                string err;
                SetTrayAutoStart(launchTrayEnabled, out err);
                AppLogger.Info("Security", "Migrated legacy Task Manager Run startup entry to hidden logon protection (enabled=" + launchTrayEnabled + ").");
            }
            else if (launchTrayEnabled && IsAdministrator() && !IsTrayAutoStartRegistered())
            {
                string err;
                SetTrayAutoStart(true, out err);
            }
        }

        public static void CreateAppShortcuts(string appExe, bool desktop, bool startMenu)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                string workDir = Path.GetDirectoryName(appExe) ?? AppPaths.BinDir;
                try
                {
                    if (desktop)
                    {
                        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "UltimateAlarm.lnk");
                        SaveShortcut(shellType, shell, lnk, appExe, "", workDir, "UltimateAlarm - Bulletproof Windows Alarm");
                    }
                    if (startMenu)
                    {
                        string smDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "UltimateAlarm");
                        Directory.CreateDirectory(smDir);
                        SaveShortcut(shellType, shell, Path.Combine(smDir, "UltimateAlarm.lnk"), appExe, "", workDir, "UltimateAlarm Dashboard");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
            catch { }
        }

        private static void SaveShortcut(Type shellType, object shell, string lnkPath, string targetExe, string args, string workDir, string desc)
        {
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            try
            {
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { targetExe });
                if (!string.IsNullOrEmpty(args))
                {
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
                }
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { targetExe + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            finally
            {
                Marshal.ReleaseComObject(sc);
            }
        }

        private static bool ServiceExists(string serviceName)
        {
            ServiceController[] svcs = null;
            try
            {
                svcs = ServiceController.GetServices();
                for (int i = 0; i < svcs.Length; i++)
                {
                    if (string.Equals(svcs[i].ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
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
            return false;
        }

        private static void StopServiceIfRunning(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(12));
                }
            }
            catch { }
        }
    }
}
