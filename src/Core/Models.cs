using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace UltimateAlarm.Core
{
    [DataContract]
    public class AlarmItem
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Label { get; set; }

        [DataMember]
        public int Hour { get; set; }

        [DataMember]
        public int Minute { get; set; }

        /// <summary>
        /// 0=Sunday .. 6=Saturday. Empty/null means one-shot alarm.
        /// </summary>
        [DataMember]
        public int[] DaysOfWeek { get; set; }

        /// <summary>
        /// Explicit target date (yyyy-MM-dd) for one-shot alarms.
        /// </summary>
        [DataMember]
        public string OneShotDate { get; set; }

        [DataMember]
        public bool Enabled { get; set; }

        /// <summary>
        /// Built-in preset ID (KlaxonPulse, ClassicTwinBell, DigitalWatch, EmergencySiren, etc.) or CustomWav
        /// </summary>
        [DataMember]
        public string SoundPreset { get; set; }

        [DataMember]
        public string CustomWavPath { get; set; }

        /// <summary>
        /// Empty = Auto-detect Speakers endpoint. Otherwise exact MMDevice ID.
        /// </summary>
        [DataMember]
        public string TargetDeviceId { get; set; }

        [DataMember]
        public bool ForceFullVolume { get; set; }

        /// <summary>
        /// Per-alarm target volume (30..100). 0 means inherit AlarmConfig.AlarmVolumePercent.
        /// </summary>
        [DataMember]
        public int VolumePercent { get; set; }

        [DataMember]
        public int BootCatchupMinutes { get; set; }

        [DataMember]
        public int MaxRingMinutes { get; set; }

        [DataMember]
        public int SnoozeMinutes { get; set; }

        /// <summary>
        /// None, Math, TypeCode, Pin
        /// </summary>
        [DataMember]
        public string ChallengeType { get; set; }

        [DataMember]
        public string LastHandledOccurrence { get; set; }

        [DataMember]
        public string LastDismissedOccurrence { get; set; }

        [DataMember]
        public string SnoozeUntilIso { get; set; }

        [DataMember]
        public string SnoozeOccurrence { get; set; }

        public AlarmItem()
        {
            InitDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            InitDefaults();
        }

        private void InitDefaults()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Label = "Alarm";
            Enabled = true;
            SoundPreset = "KlaxonPulse";
            CustomWavPath = "";
            TargetDeviceId = "";
            ForceFullVolume = true;
            VolumePercent = 0;
            BootCatchupMinutes = 45;
            MaxRingMinutes = 30;
            SnoozeMinutes = 5;
            ChallengeType = "Math";
            DaysOfWeek = new int[0];
        }

        public bool IsOneShot()
        {
            return DaysOfWeek == null || DaysOfWeek.Length == 0;
        }

        public void EnsureOneShotDate(DateTime referenceNow)
        {
            if (!IsOneShot())
            {
                OneShotDate = null;
                return;
            }
            if (!string.IsNullOrEmpty(OneShotDate))
            {
                return;
            }
            DateTime candidate = new DateTime(referenceNow.Year, referenceNow.Month, referenceNow.Day, Hour, Minute, 0);
            if (candidate <= referenceNow)
            {
                candidate = candidate.AddDays(1);
            }
            OneShotDate = candidate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public bool MatchesDate(DateTime date)
        {
            if (IsOneShot())
            {
                if (string.IsNullOrEmpty(OneShotDate))
                {
                    return false;
                }
                return string.Equals(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), OneShotDate, StringComparison.Ordinal);
            }

            int dow = (int)date.DayOfWeek;
            for (int i = 0; i < DaysOfWeek.Length; i++)
            {
                if (DaysOfWeek[i] == dow)
                {
                    return true;
                }
            }
            return false;
        }

        public DateTime? GetDueOccurrence(DateTime now, out string occurrenceKey, out bool isSnoozeWake)
        {
            occurrenceKey = null;
            isSnoozeWake = false;

            if (!Enabled)
            {
                return null;
            }

            int catchup = BootCatchupMinutes > 0 ? BootCatchupMinutes : 45;

            if (!string.IsNullOrEmpty(SnoozeUntilIso))
            {
                DateTime snoozeUntil;
                if (DateTime.TryParseExact(SnoozeUntilIso, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out snoozeUntil))
                {
                    if (now >= snoozeUntil && now <= snoozeUntil.AddMinutes(catchup))
                    {
                        occurrenceKey = !string.IsNullOrEmpty(SnoozeOccurrence)
                            ? SnoozeOccurrence
                            : snoozeUntil.ToString("yyyy-MM-dd@HH:mm", CultureInfo.InvariantCulture);
                        if (!string.Equals(LastDismissedOccurrence, occurrenceKey, StringComparison.Ordinal))
                        {
                            isSnoozeWake = true;
                            return snoozeUntil;
                        }
                    }
                    if (now < snoozeUntil)
                    {
                        return null;
                    }
                }
            }

            for (int dayOffset = -1; dayOffset <= 0; dayOffset++)
            {
                DateTime day = now.Date.AddDays(dayOffset);
                if (!MatchesDate(day))
                {
                    continue;
                }
                DateTime occ = new DateTime(day.Year, day.Month, day.Day, Hour, Minute, 0);
                if (now >= occ && now <= occ.AddMinutes(catchup))
                {
                    string key = occ.ToString("yyyy-MM-dd@HH:mm", CultureInfo.InvariantCulture);
                    if (!string.Equals(LastHandledOccurrence, key, StringComparison.Ordinal) &&
                        !string.Equals(LastDismissedOccurrence, key, StringComparison.Ordinal))
                    {
                        occurrenceKey = key;
                        return occ;
                    }
                }
            }

            return null;
        }

        public DateTime? GetNextScheduledTime(DateTime from)
        {
            if (!Enabled)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(SnoozeUntilIso))
            {
                DateTime snoozeUntil;
                if (DateTime.TryParseExact(SnoozeUntilIso, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out snoozeUntil))
                {
                    if (snoozeUntil > from)
                    {
                        return snoozeUntil;
                    }
                }
            }

            if (IsOneShot())
            {
                DateTime parsedDate;
                if (!string.IsNullOrEmpty(OneShotDate) &&
                    DateTime.TryParseExact(OneShotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    DateTime occ = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, Hour, Minute, 0);
                    string key = occ.ToString("yyyy-MM-dd@HH:mm", CultureInfo.InvariantCulture);
                    if (occ > from && !string.Equals(LastDismissedOccurrence, key, StringComparison.Ordinal))
                    {
                        return occ;
                    }
                }
                return null;
            }

            for (int d = 0; d <= 8; d++)
            {
                DateTime day = from.Date.AddDays(d);
                if (!MatchesDate(day))
                {
                    continue;
                }
                DateTime occ = new DateTime(day.Year, day.Month, day.Day, Hour, Minute, 0);
                string key = occ.ToString("yyyy-MM-dd@HH:mm", CultureInfo.InvariantCulture);
                if (occ > from && !string.Equals(LastDismissedOccurrence, key, StringComparison.Ordinal))
                {
                    return occ;
                }
            }

            return null;
        }

        public string FormatDays()
        {
            if (IsOneShot())
            {
                return string.IsNullOrEmpty(OneShotDate) ? "One-shot" : ("Once (" + OneShotDate + ")");
            }
            if (DaysOfWeek.Length == 7)
            {
                return "Every day";
            }
            if (DaysOfWeek.Length == 5)
            {
                int[] copy = (int[])DaysOfWeek.Clone();
                Array.Sort(copy);
                if (copy[0] == 1 && copy[1] == 2 && copy[2] == 3 && copy[3] == 4 && copy[4] == 5)
                {
                    return "Weekdays";
                }
            }
            string[] names = new string[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            List<string> parts = new List<string>();
            Array.Sort(DaysOfWeek);
            for (int i = 0; i < DaysOfWeek.Length; i++)
            {
                int idx = DaysOfWeek[i];
                if (idx >= 0 && idx < 7)
                {
                    parts.Add(names[idx]);
                }
            }
            return string.Join(", ", parts.ToArray());
        }
    }

    [DataContract]
    public class AlarmConfig
    {
        [DataMember]
        public bool SetupCompleted { get; set; }

        /// <summary>
        /// "LocalSystem" (zero password stored) or "AutoLogonAndLock" (LSA secret + immediate boot lock).
        /// </summary>
        [DataMember]
        public string AuthMode { get; set; }

        [DataMember]
        public bool AutoLockOnBoot { get; set; }

        [DataMember]
        public bool LaunchUiInTrayAtLogon { get; set; }

        [DataMember]
        public string DismissPinHash { get; set; }

        [DataMember]
        public string DismissPinSalt { get; set; }

        [DataMember]
        public string PinnedSpeakerDeviceId { get; set; }

        [DataMember]
        public bool RingAllOutputDevices { get; set; }

        /// <summary>
        /// Global alarm volume (30..100). Minimum enforced floor is 30%.
        /// </summary>
        [DataMember]
        public int AlarmVolumePercent { get; set; }

        [DataMember]
        public bool AllowHdmiFallback { get; set; }

        [DataMember]
        public bool AllowHeadphoneFallback { get; set; }

        [DataMember]
        public bool EnableTaskSchedulerWakeBackup { get; set; }

        [DataMember]
        public int PreWakeOffsetSeconds { get; set; }

        /// <summary>
        /// UI visual theme: Default, MoreDark, UltimateDark, Neon
        /// </summary>
        [DataMember]
        public string UiTheme { get; set; }

        [DataMember]
        public List<AlarmItem> Alarms { get; set; }

        public AlarmConfig()
        {
            InitDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            InitDefaults();
        }

        private void InitDefaults()
        {
            SetupCompleted = false;
            AuthMode = "LocalSystem";
            AutoLockOnBoot = true;
            LaunchUiInTrayAtLogon = true;
            DismissPinHash = "";
            DismissPinSalt = "";
            PinnedSpeakerDeviceId = "";
            RingAllOutputDevices = false;
            AlarmVolumePercent = 80;
            AllowHdmiFallback = true;
            AllowHeadphoneFallback = false;
            EnableTaskSchedulerWakeBackup = true;
            PreWakeOffsetSeconds = 15;
            UiTheme = "Default";
            Alarms = new List<AlarmItem>();
        }
    }

    [DataContract]
    public class ActiveRingState
    {
        [DataMember]
        public string AlarmId { get; set; }

        [DataMember]
        public string Label { get; set; }

        [DataMember]
        public string OccurrenceKey { get; set; }

        [DataMember]
        public string StartedAtIso { get; set; }

        [DataMember]
        public string SoundPreset { get; set; }

        [DataMember]
        public string CustomWavPath { get; set; }

        [DataMember]
        public string TargetDeviceId { get; set; }

        [DataMember]
        public bool ForceFullVolume { get; set; }

        [DataMember]
        public int TargetVolumePercent { get; set; }

        [DataMember]
        public int MaxRingMinutes { get; set; }

        [DataMember]
        public int SnoozeMinutes { get; set; }

        [DataMember]
        public string ChallengeType { get; set; }

        [DataMember]
        public bool IsTest { get; set; }
    }

    [DataContract]
    public class AudioDeviceInfo
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string FriendlyName { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public string InterfaceName { get; set; }

        [DataMember]
        public int FormFactor { get; set; }

        [DataMember]
        public string FormFactorName { get; set; }

        [DataMember]
        public bool IsDefaultMultimedia { get; set; }

        [DataMember]
        public bool IsDefaultCommunications { get; set; }

        [DataMember]
        public float VolumeScalar { get; set; }

        [DataMember]
        public bool IsMuted { get; set; }

        [DataMember]
        public int SpeakerScore { get; set; }

        [DataMember]
        public string Category { get; set; }
    }

    [DataContract]
    public class HardwareInfo
    {
        [DataMember]
        public string BaseBoardManufacturer { get; set; }

        [DataMember]
        public string BaseBoardProduct { get; set; }

        [DataMember]
        public string SystemManufacturer { get; set; }

        [DataMember]
        public string SystemProductName { get; set; }

        [DataMember]
        public string BiosVendor { get; set; }

        [DataMember]
        public string BiosVersion { get; set; }
    }

    [DataContract]
    public class WakeDiagnostics
    {
        [DataMember]
        public bool IsAdministrator { get; set; }

        [DataMember]
        public bool ServiceInstalled { get; set; }

        [DataMember]
        public bool ServiceRunning { get; set; }

        [DataMember]
        public bool RunningAsLocalSystem { get; set; }

        [DataMember]
        public bool WakeTimersEnabledAC { get; set; }

        [DataMember]
        public bool WakeTimersEnabledDC { get; set; }

        [DataMember]
        public bool SleepS3Supported { get; set; }

        [DataMember]
        public bool ModernStandbyS0Supported { get; set; }

        [DataMember]
        public bool HibernateS4Supported { get; set; }

        [DataMember]
        public bool FastStartupEnabled { get; set; }

        [DataMember]
        public bool TaskBackupRegistered { get; set; }

        [DataMember]
        public bool TrayAutoStartRegistered { get; set; }

        [DataMember]
        public bool AutoLogonEnabled { get; set; }

        [DataMember]
        public string AutoLogonUser { get; set; }

        [DataMember]
        public bool HasLsaPasswordStored { get; set; }

        [DataMember]
        public bool AutoLockOnBoot { get; set; }

        [DataMember]
        public string NextWakeIso { get; set; }

        [DataMember]
        public string SelectedSpeakerName { get; set; }

        [DataMember]
        public string SelectedSpeakerId { get; set; }

        [DataMember]
        public string MotherboardManufacturer { get; set; }

        [DataMember]
        public string MotherboardModel { get; set; }

        [DataMember]
        public string BiosVendor { get; set; }

        [DataMember]
        public string BiosVersion { get; set; }

        [DataMember]
        public string BiosRtcGuide { get; set; }

        [DataMember]
        public string PowerSummary { get; set; }

        [DataMember]
        public string ActivePowerPlanName { get; set; }

        [DataMember]
        public bool StartupExposedInTaskManager { get; set; }

        [DataMember]
        public bool UsbSelectiveSuspendDisabled { get; set; }

        [DataMember]
        public bool AudioIdleSleepDisabled { get; set; }

        [DataMember]
        public bool ScreenSaverActive { get; set; }

        [DataMember]
        public int ScreenSaverTimeoutSeconds { get; set; }
    }

    [DataContract]
    public class ServiceStatusResponse
    {
        [DataMember]
        public bool Success { get; set; }

        [DataMember]
        public string Error { get; set; }

        [DataMember]
        public string ServerTimeIso { get; set; }

        [DataMember]
        public string NextWakeIso { get; set; }

        [DataMember]
        public ActiveRingState ActiveRing { get; set; }

        [DataMember]
        public AlarmConfig Config { get; set; }

        [DataMember]
        public List<AudioDeviceInfo> Devices { get; set; }

        [DataMember]
        public WakeDiagnostics Diagnostics { get; set; }
    }

    [DataContract]
    public class IpcRequest
    {
        [DataMember]
        public string Command { get; set; }

        [DataMember]
        public string AlarmId { get; set; }

        [DataMember]
        public AlarmItem Alarm { get; set; }

        [DataMember]
        public AlarmConfig Config { get; set; }

        [DataMember]
        public string DeviceId { get; set; }

        [DataMember]
        public bool BoolFlag { get; set; }

        [DataMember]
        public int IntParam { get; set; }

        [DataMember]
        public string StringParam { get; set; }
    }
}
