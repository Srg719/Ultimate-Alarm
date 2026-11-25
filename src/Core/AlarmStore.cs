using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace UltimateAlarm.Core
{
    public static class AppPaths
    {
        private static string _rootDir;
        private static string _exeDir;

        public static string ExeDir
        {
            get
            {
                if (_exeDir == null)
                {
                    _exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                return _exeDir;
            }
        }

        public static string RootDir
        {
            get
            {
                if (_rootDir == null)
                {
                    DirectoryInfo di = new DirectoryInfo(ExeDir);
                    if (string.Equals(di.Name, "bin", StringComparison.OrdinalIgnoreCase) && di.Parent != null)
                    {
                        _rootDir = di.Parent.FullName;
                    }
                    else
                    {
                        _rootDir = ExeDir;
                    }
                }
                return _rootDir;
            }
            set
            {
                _rootDir = value;
            }
        }

        /// <summary>
        /// Resolves executable folder seamlessly in both dev repo (Root\bin) and portable release (single folder).
        /// </summary>
        public static string BinDir
        {
            get
            {
                if (File.Exists(Path.Combine(ExeDir, "UltimateAlarm.exe")) ||
                    File.Exists(Path.Combine(ExeDir, "ua.exe")))
                {
                    return ExeDir;
                }
                string subBin = Path.Combine(RootDir, "bin");
                return Directory.Exists(subBin) ? subBin : ExeDir;
            }
        }

        public static string DataDir { get { return Path.Combine(RootDir, "data"); } }
        public static string LogsDir { get { return Path.Combine(RootDir, "logs"); } }
        public static string ConfigPath { get { return Path.Combine(DataDir, "alarms.json"); } }
        public static string ConfigBackupPath { get { return Path.Combine(DataDir, "alarms.json.bak"); } }
        public static string ActiveRingPath { get { return Path.Combine(DataDir, "active_ring.json"); } }
        public static string BootLockMarkerPath { get { return Path.Combine(DataDir, "boot_lock.txt"); } }
        public static string LogFilePath { get { return Path.Combine(LogsDir, "ultimate-alarm.log"); } }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogsDir);
        }
    }

    public static class AppLogger
    {
        private static readonly object _logLock = new object();

        public static void Info(string component, string message)
        {
            Write("INFO", component, message);
        }

        public static void Warn(string component, string message)
        {
            Write("WARN", component, message);
        }

        public static void Error(string component, string message)
        {
            Write("ERROR", component, message);
        }

        private static void Write(string level, string component, string message)
        {
            try
            {
                lock (_logLock)
                {
                    AppPaths.EnsureDirectories();
                    string path = AppPaths.LogFilePath;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        try
                        {
                            string rotated = path + ".old";
                            if (File.Exists(rotated))
                            {
                                File.Delete(rotated);
                            }
                            File.Move(path, rotated);
                        }
                        catch { }
                    }
                    // Prevent CWE-117 log forging via embedded CR/LF
                    string safeMsg = (message ?? "").Replace('\r', ' ').Replace('\n', ' ');
                    if (safeMsg.Length > 2000) safeMsg = safeMsg.Substring(0, 2000) + "...";
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}{4}",
                        DateTime.Now, level, component, safeMsg, Environment.NewLine);
                    byte[] bytes = Encoding.UTF8.GetBytes(line);

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                            {
                                fs.Write(bytes, 0, bytes.Length);
                            }
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt < 2) Thread.Sleep(15);
                        }
                    }
                }
            }
            catch
            {
                // Logging must never crash the alarm engine.
            }
        }
    }

    public static class JsonHelper
    {
        public static string Serialize<T>(T obj)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return (T)ser.ReadObject(ms);
            }
        }
    }

    public sealed class AlarmStore
    {
        private static readonly object _inProcessLock = new object();

        private Mutex OpenCrossProcessMutex()
        {
            try
            {
                Mutex existing;
                if (Mutex.TryOpenExisting("Global\\UltimateAlarmStoreMutex", out existing))
                {
                    return existing;
                }
            }
            catch
            {
                // Fall through to creation.
            }

            bool createdNew;
            try
            {
                MutexSecurity security = new MutexSecurity();
                SecurityIdentifier sysSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                security.AddAccessRule(new MutexAccessRule(sysSid, MutexRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new MutexAccessRule(adminSid, MutexRights.FullControl, AccessControlType.Allow));
                // Grant Synchronize|Modify only (no ChangePermissions/TakeOwnership) to prevent ACL-lockout DoS
                security.AddAccessRule(new MutexAccessRule(authUsers, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
                return new Mutex(false, "Global\\UltimateAlarmStoreMutex", out createdNew, security);
            }
            catch
            {
                return new Mutex(false, "Local\\UltimateAlarmStoreMutex", out createdNew);
            }
        }

        private static bool AcquireMutexSafe(Mutex mutex, int timeoutMs)
        {
            try
            {
                return mutex.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                // Previous holder crashed or was terminated; we now own the mutex cleanly.
                return true;
            }
        }

        public AlarmConfig LoadConfig()
        {
            lock (_inProcessLock)
            {
                using (Mutex mutex = OpenCrossProcessMutex())
                {
                    bool acquired = false;
                    try
                    {
                        acquired = AcquireMutexSafe(mutex, 3000);
                        AppPaths.EnsureDirectories();
                        AlarmConfig cfg = TryReadConfig(AppPaths.ConfigPath);
                        if (cfg == null)
                        {
                            cfg = TryReadConfig(AppPaths.ConfigBackupPath);
                            if (cfg != null)
                            {
                                AppLogger.Warn("AlarmStore", "Primary alarms.json corrupted or missing; restored from backup.");
                            }
                        }
                        if (cfg == null)
                        {
                            cfg = new AlarmConfig();
                        }
                        if (cfg.Alarms == null)
                        {
                            cfg.Alarms = new System.Collections.Generic.List<AlarmItem>();
                        }
                        if (string.IsNullOrEmpty(cfg.AuthMode))
                        {
                            cfg.AuthMode = "LocalSystem";
                        }
                        cfg.AlarmVolumePercent = cfg.AlarmVolumePercent <= 0
                            ? AudioRouter.DefaultVolumePercent
                            : AudioRouter.ClampVolumePercent(cfg.AlarmVolumePercent);
                        DateTime now = DateTime.Now;
                        for (int i = 0; i < cfg.Alarms.Count; i++)
                        {
                            if (cfg.Alarms[i].DaysOfWeek == null)
                            {
                                cfg.Alarms[i].DaysOfWeek = new int[0];
                            }
                            if (cfg.Alarms[i].Enabled && cfg.Alarms[i].IsOneShot() && string.IsNullOrEmpty(cfg.Alarms[i].OneShotDate))
                            {
                                cfg.Alarms[i].EnsureOneShotDate(now);
                            }
                        }
                        return cfg;
                    }
                    finally
                    {
                        if (acquired)
                        {
                            try { mutex.ReleaseMutex(); } catch { }
                        }
                    }
                }
            }
        }

        public void SaveConfig(AlarmConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (config.Alarms == null)
            {
                config.Alarms = new System.Collections.Generic.List<AlarmItem>();
            }

            lock (_inProcessLock)
            {
                using (Mutex mutex = OpenCrossProcessMutex())
                {
                    bool acquired = false;
                    try
                    {
                        acquired = AcquireMutexSafe(mutex, 3000);
                        AppPaths.EnsureDirectories();
                        string json = JsonHelper.Serialize(config);
                        WriteAtomic(AppPaths.ConfigPath, AppPaths.ConfigBackupPath, json);
                    }
                    finally
                    {
                        if (acquired)
                        {
                            try { mutex.ReleaseMutex(); } catch { }
                        }
                    }
                }
            }
        }

        public ActiveRingState LoadActiveRing()
        {
            lock (_inProcessLock)
            {
                try
                {
                    if (!File.Exists(AppPaths.ActiveRingPath))
                    {
                        return null;
                    }
                    string json = File.ReadAllText(AppPaths.ActiveRingPath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return null;
                    }
                    return JsonHelper.Deserialize<ActiveRingState>(json);
                }
                catch
                {
                    return null;
                }
            }
        }

        public void SaveActiveRing(ActiveRingState state)
        {
            lock (_inProcessLock)
            {
                AppPaths.EnsureDirectories();
                if (state == null)
                {
                    if (File.Exists(AppPaths.ActiveRingPath))
                    {
                        File.Delete(AppPaths.ActiveRingPath);
                    }
                    return;
                }
                string json = JsonHelper.Serialize(state);
                WriteAtomic(AppPaths.ActiveRingPath, null, json);
            }
        }

        private static AlarmConfig TryReadConfig(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }
                return JsonHelper.Deserialize<AlarmConfig>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteAtomic(string targetPath, string backupPath, string content)
        {
            string tmpPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmpPath, content, Encoding.UTF8);
            if (File.Exists(targetPath))
            {
                if (!string.IsNullOrEmpty(backupPath))
                {
                    File.Replace(tmpPath, targetPath, backupPath, true);
                }
                else
                {
                    File.Copy(tmpPath, targetPath, true);
                    File.Delete(tmpPath);
                }
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }
        }
    }
}
