using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UltimateAlarm.Core
{
    public static class AudioRouter
    {
        private const int STGM_READ = 0x00000000;
        private const uint DEVICE_STATE_ACTIVE = 0x00000001;
        private const uint CLSCTX_ALL = 23;
        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        private static readonly Guid IID_IAudioClock = new Guid("CD63314F-3FBA-4a1b-812C-EF96358728E7");
        private static readonly Guid IID_ISimpleAudioVolume = new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8");
        private static readonly Guid AlarmAudioSessionGuid = new Guid("7D8A1E24-5B9C-4E11-A3F0-9C4D2A1B6E88");

        private static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00aa00389b71");

        private static PropertyKey PKEY_Device_DeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        private static PropertyKey PKEY_Device_FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        private static PropertyKey PKEY_DeviceInterface_FriendlyName = new PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
        private static PropertyKey PKEY_AudioEndpoint_FormFactor = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

        public static List<AudioDeviceInfo> EnumerateActiveOutputDevices()
        {
            List<AudioDeviceInfo> list = new List<AudioDeviceInfo>();
            NativeMethods.CoInitializeEx(IntPtr.Zero, 0);

            Type enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType);
            try
            {
                string defaultConsoleId = GetDefaultDeviceId(enumerator, ERole.eMultimedia);
                string defaultCommId = GetDefaultDeviceId(enumerator, ERole.eCommunications);

                IMMDeviceCollection collection;
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out collection);
                if (hr != 0 || collection == null)
                {
                    return list;
                }

                try
                {
                    int count;
                    collection.GetCount(out count);
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (collection.Item(i, out dev) != 0 || dev == null)
                        {
                            continue;
                        }
                        try
                        {
                            AudioDeviceInfo info = InspectDevice(dev, defaultConsoleId, defaultCommId);
                            if (info != null)
                            {
                                list.Add(info);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(collection);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            list.Sort(delegate(AudioDeviceInfo a, AudioDeviceInfo b)
            {
                return b.SpeakerScore.CompareTo(a.SpeakerScore);
            });
            return list;
        }

        public static List<AudioDeviceInfo> SelectTargetDevices(AlarmConfig cfg, string overrideDeviceId)
        {
            List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
            List<AudioDeviceInfo> devices = EnumerateActiveOutputDevices();
            if (devices.Count == 0)
            {
                return result;
            }

            if (string.IsNullOrEmpty(overrideDeviceId) && cfg != null && cfg.RingAllOutputDevices)
            {
                bool allowHeadphones = cfg.AllowHeadphoneFallback;
                for (int i = 0; i < devices.Count; i++)
                {
                    AudioDeviceInfo d = devices[i];
                    if (string.Equals(d.Category, "Virtual", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!allowHeadphones && string.Equals(d.Category, "Headphones", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    result.Add(d);
                }

                if (result.Count == 0 && allowHeadphones)
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        if (!string.Equals(devices[i].Category, "Virtual", StringComparison.Ordinal))
                        {
                            result.Add(devices[i]);
                        }
                    }
                }
                return result;
            }

            AudioDeviceInfo single = SelectSpeakerDevice(cfg, overrideDeviceId);
            if (single != null)
            {
                result.Add(single);
            }
            return result;
        }

        public static AudioDeviceInfo SelectSpeakerDevice(AlarmConfig cfg, string overrideDeviceId)
        {
            List<AudioDeviceInfo> devices = EnumerateActiveOutputDevices();
            if (devices.Count == 0)
            {
                return null;
            }

            string desiredId = !string.IsNullOrEmpty(overrideDeviceId)
                ? overrideDeviceId
                : (cfg != null ? cfg.PinnedSpeakerDeviceId : null);

            if (!string.IsNullOrEmpty(desiredId))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i].Id, desiredId, StringComparison.OrdinalIgnoreCase))
                    {
                        return devices[i];
                    }
                }
                AppLogger.Warn("AudioRouter", "Pinned speaker device not active (" + desiredId + "); falling back to auto-detected speaker.");
            }

            bool allowHdmi = cfg == null || cfg.AllowHdmiFallback;
            bool allowHeadphones = cfg != null && cfg.AllowHeadphoneFallback;

            // Pass 1: Strict Speakers / LineLevel (score >= 100)
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].SpeakerScore >= 100)
                {
                    return devices[i];
                }
            }

            // Pass 2: HDMI / Display speakers if allowed
            if (allowHdmi)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i].Category, "HDMI/Display", StringComparison.Ordinal))
                    {
                        return devices[i];
                    }
                }
            }

            // Pass 3: Headphones only if explicitly permitted
            if (allowHeadphones)
            {
                return devices[0];
            }

            // Never silently blast headphones when prohibited; try any non-headphone device first.
            for (int i = 0; i < devices.Count; i++)
            {
                if (!string.Equals(devices[i].Category, "Headphones", StringComparison.Ordinal))
                {
                    return devices[i];
                }
            }

            return null;
        }

        public const int MinVolumePercent = 30;
        public const int DefaultVolumePercent = 80;

        public sealed class SoundPresetInfo
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string ShortName;

            public SoundPresetInfo(string id, string displayName, string shortName)
            {
                Id = id;
                DisplayName = displayName;
                ShortName = shortName;
            }

            public override string ToString() { return DisplayName; }
        }

        public static readonly SoundPresetInfo[] BuiltInPresets = new SoundPresetInfo[]
        {
            new SoundPresetInfo("KlaxonPulse", "Klaxon Pulse (Classic 3-Beep)", "Klaxon Pulse"),
            new SoundPresetInfo("ClassicTwinBell", "Classic Twin-Bell Clock", "Twin-Bell"),
            new SoundPresetInfo("DigitalWatch", "Digital Watch (Rapid 4-Chirp)", "Digital Watch"),
            new SoundPresetInfo("EmergencySiren", "Emergency Siren (Sweeping)", "Emergency Siren"),
            new SoundPresetInfo("NuclearAlert", "Industrial Red Alert", "Red Alert"),
            new SoundPresetInfo("RadarBeacon", "Air Raid / Radar Sweep", "Radar Sweep"),
            new SoundPresetInfo("MilitaryBugle", "Military Bugle (Reveille)", "Military Bugle"),
            new SoundPresetInfo("CyberNeonPulse", "Cyber Neon Synth", "Cyber Neon"),
            new SoundPresetInfo("SonarPingAlert", "Submarine Sonar Alert", "Sonar Alert"),
            new SoundPresetInfo("EscalatingChime", "Escalating Chime (Bright)", "Escalating Chime"),
            new SoundPresetInfo("MorningPiano", "Morning Rhodes (Gentle)", "Morning Rhodes")
        };

        public static string GetPresetShortName(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return "Klaxon Pulse";
            if (string.Equals(presetId, "CustomWav", StringComparison.OrdinalIgnoreCase)) return "Custom WAV";
            for (int i = 0; i < BuiltInPresets.Length; i++)
            {
                if (string.Equals(BuiltInPresets[i].Id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    return BuiltInPresets[i].ShortName;
                }
            }
            return presetId;
        }

        public static int ClampVolumePercent(int percent)
        {
            return Math.Max(MinVolumePercent, Math.Min(100, percent));
        }

        public static int ResolveTargetVolumePercent(AlarmConfig cfg, ActiveRingState ring)
        {
            if (ring != null && ring.TargetVolumePercent > 0)
            {
                return ClampVolumePercent(ring.TargetVolumePercent);
            }
            if (cfg != null && cfg.AlarmVolumePercent > 0)
            {
                return ClampVolumePercent(cfg.AlarmVolumePercent);
            }
            return DefaultVolumePercent;
        }

        public static bool ForceDeviceFullVolume(string deviceId, out string error)
        {
            return ForceDeviceVolume(deviceId, 100, out error);
        }

        public static bool ForceDeviceVolume(string deviceId, int volumePercent, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(deviceId))
            {
                error = "Empty device ID.";
                return false;
            }
            float targetScalar = ClampVolumePercent(volumePercent) / 100f;
            try
            {
                NativeMethods.CoInitializeEx(IntPtr.Zero, 0);
                Type enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType);
                try
                {
                    IMMDevice dev;
                    int hr = enumerator.GetDevice(deviceId, out dev);
                    if (hr != 0 || dev == null)
                    {
                        error = "GetDevice failed hr=0x" + hr.ToString("X8");
                        return false;
                    }
                    try
                    {
                        Guid iid = IID_IAudioEndpointVolume;
                        object obj;
                        hr = dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out obj);
                        if (hr != 0 || obj == null)
                        {
                            error = "Activate IAudioEndpointVolume failed hr=0x" + hr.ToString("X8");
                            return false;
                        }
                        IAudioEndpointVolume vol = (IAudioEndpointVolume)obj;
                        try
                        {
                            Guid ctx = Guid.Empty;
                            bool curMute;
                            float curVol;
                            bool needMaster = true;
                            if (vol.GetMute(out curMute) == 0 && vol.GetMasterVolumeLevelScalar(out curVol) == 0)
                            {
                                needMaster = curMute || Math.Abs(curVol - targetScalar) > 0.015f;
                            }

                            if (needMaster)
                            {
                                vol.SetMute(false, ref ctx);
                                vol.SetMasterVolumeLevelScalar(targetScalar, ref ctx);
                            }

                            uint chCount;
                            if (vol.GetChannelCount(out chCount) == 0 && chCount > 0 && chCount <= 16)
                            {
                                for (uint c = 0; c < chCount; c++)
                                {
                                    float chVol;
                                    if (needMaster || vol.GetChannelVolumeLevelScalar(c, out chVol) != 0 || Math.Abs(chVol - targetScalar) > 0.015f)
                                    {
                                        vol.SetChannelVolumeLevelScalar(c, targetScalar, ref ctx);
                                    }
                                }
                            }
                            return true;
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(vol);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(dev);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void PlayAlarmLoop(
            AlarmConfig config,
            ActiveRingState ringState,
            ManualResetEvent stopSignal,
            int maxSecondsOverride)
        {
            NativeMethods.CoInitializeEx(IntPtr.Zero, 0);

            DateTime started = DateTime.UtcNow;
            int maxSeconds = maxSecondsOverride > 0
                ? maxSecondsOverride
                : (ringState != null && ringState.MaxRingMinutes > 0 ? ringState.MaxRingMinutes * 60 : 0);

            string soundPreset = ringState != null && !string.IsNullOrEmpty(ringState.SoundPreset)
                ? ringState.SoundPreset
                : "KlaxonPulse";
            string wavPath = ringState != null ? ringState.CustomWavPath : null;
            string overrideDeviceId = ringState != null ? ringState.TargetDeviceId : null;
            bool forceVolume = ringState == null || ringState.ForceFullVolume;
            int targetVolumePercent = ResolveTargetVolumePercent(config, ringState);

            WavAudioData customWav = null;
            if (string.Equals(soundPreset, "CustomWav", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(wavPath))
            {
                if (!string.IsNullOrEmpty(wavPath) && File.Exists(wavPath))
                {
                    customWav = WavAudioData.TryLoad(wavPath);
                    if (customWav == null)
                    {
                        AppLogger.Warn("AudioRouter", "Failed to parse custom WAV (" + wavPath + "); falling back to KlaxonPulse.");
                    }
                }
            }

            int consecutiveFailures = 0;
            while (!stopSignal.WaitOne(0))
            {
                if (maxSeconds > 0 && (DateTime.UtcNow - started).TotalSeconds >= maxSeconds)
                {
                    AppLogger.Info("AudioRouter", "Max ring duration reached; stopping playback.");
                    break;
                }

                List<AudioDeviceInfo> targets = SelectTargetDevices(config, overrideDeviceId);
                if (targets.Count == 0)
                {
                    AppLogger.Warn("AudioRouter", "No eligible speaker device active right now; retrying in 1s (Beep fallback).");
                    NativeMethods.Beep(1200, 350);
                    stopSignal.WaitOne(650);
                    continue;
                }

                if (targets.Count == 1)
                {
                    AudioDeviceInfo target = targets[0];
                    if (forceVolume)
                    {
                        string volErr;
                        if (!ForceDeviceVolume(target.Id, targetVolumePercent, out volErr))
                        {
                            AppLogger.Warn("AudioRouter", "ForceDeviceVolume warning on " + target.FriendlyName + ": " + volErr);
                        }
                    }

                    string playErr;
                    bool ok = PlayViaWasapi(target, soundPreset, customWav, forceVolume, targetVolumePercent, stopSignal, started, maxSeconds, out playErr);
                    if (stopSignal.WaitOne(0) || (maxSeconds > 0 && (DateTime.UtcNow - started).TotalSeconds >= maxSeconds))
                    {
                        break;
                    }

                    if (!ok)
                    {
                        consecutiveFailures++;
                        AppLogger.Warn("AudioRouter", string.Format("WASAPI playback error on '{0}': {1}. Trying WinMM fallback.", target.FriendlyName, playErr));
                        string winmmErr;
                        bool mmOk = PlayViaWinMM(target, soundPreset, customWav, forceVolume, targetVolumePercent, stopSignal, started, maxSeconds, out winmmErr);
                        if (!mmOk && !stopSignal.WaitOne(0))
                        {
                            AppLogger.Warn("AudioRouter", "WinMM fallback failed: " + winmmErr + ". Using kernel Beep until audio subsystem is ready.");
                            NativeMethods.Beep(1400, 300);
                            stopSignal.WaitOne(700);
                        }
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }

                    if (consecutiveFailures > 0)
                    {
                        stopSignal.WaitOne(500);
                    }
                }
                else
                {
                    StringBuilder names = new StringBuilder();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (i > 0) names.Append(" | ");
                        names.Append(targets[i].FriendlyName);
                    }
                    AppLogger.Info("AudioRouter", "RingAllOutputDevices active: broadcasting simultaneously to " + targets.Count + " output device(s) at " + targetVolumePercent + "% -> [" + names.ToString() + "]");
                    PlayMultiDeviceSimultaneously(targets, soundPreset, customWav, forceVolume, targetVolumePercent, stopSignal, started, maxSeconds);
                    break;
                }
            }
        }

        private static void PlayMultiDeviceSimultaneously(
            List<AudioDeviceInfo> targets,
            string soundPreset,
            WavAudioData customWav,
            bool forceVolume,
            int targetVolumePercent,
            ManualResetEvent stopSignal,
            DateTime startedUtc,
            int maxSeconds)
        {
            using (ManualResetEvent startGate = new ManualResetEvent(false))
            {
                List<Thread> workers = new List<Thread>(targets.Count);
                int activeCount = targets.Count;

                for (int i = 0; i < targets.Count; i++)
                {
                    AudioDeviceInfo dev = targets[i];
                    int index = i;
                    Thread t = new Thread(delegate()
                    {
                        NativeMethods.CoInitializeEx(IntPtr.Zero, 0);
                        startGate.WaitOne(2000);
                        int consecutiveErrs = 0;
                        while (!stopSignal.WaitOne(0))
                        {
                            if (maxSeconds > 0 && (DateTime.UtcNow - startedUtc).TotalSeconds >= maxSeconds)
                            {
                                break;
                            }

                            if (forceVolume)
                            {
                                string volErr;
                                if (!ForceDeviceVolume(dev.Id, targetVolumePercent, out volErr) && consecutiveErrs == 0)
                                {
                                    AppLogger.Warn("AudioRouter", string.Format("[{0}] ForceDeviceVolume warning: {1}", dev.FriendlyName, volErr));
                                }
                            }

                            string playErr;
                            bool ok = PlayViaWasapi(dev, soundPreset, customWav, forceVolume, targetVolumePercent, stopSignal, startedUtc, maxSeconds, out playErr);
                            if (stopSignal.WaitOne(0) || (maxSeconds > 0 && (DateTime.UtcNow - startedUtc).TotalSeconds >= maxSeconds))
                            {
                                break;
                            }

                            if (!ok)
                            {
                                consecutiveErrs++;
                                if (consecutiveErrs <= 3)
                                {
                                    AppLogger.Warn("AudioRouter", string.Format("[{0}] Multi-output WASAPI failed ({1}); trying WinMM fallback.", dev.FriendlyName, playErr));
                                }
                                string winmmErr;
                                bool mmOk = PlayViaWinMM(dev, soundPreset, customWav, forceVolume, targetVolumePercent, stopSignal, startedUtc, maxSeconds, out winmmErr);
                                if (!mmOk && !stopSignal.WaitOne(0))
                                {
                                    if (consecutiveErrs <= 3)
                                    {
                                        AppLogger.Warn("AudioRouter", string.Format("[{0}] WinMM fallback failed ({1}); backing off.", dev.FriendlyName, winmmErr));
                                    }
                                    if (index == 0 && consecutiveErrs <= 3)
                                    {
                                        NativeMethods.Beep(1400, 300);
                                    }
                                    stopSignal.WaitOne(consecutiveErrs >= 3 ? 3000 : 800);
                                }
                                else if (mmOk)
                                {
                                    consecutiveErrs = 0;
                                }
                            }
                            else
                            {
                                consecutiveErrs = 0;
                            }
                        }
                        Interlocked.Decrement(ref activeCount);
                    });
                    t.IsBackground = true;
                    t.SetApartmentState(ApartmentState.MTA);
                    t.Start();
                    workers.Add(t);
                }

                // Release all output streams together at the exact same moment.
                startGate.Set();

                while (!stopSignal.WaitOne(100))
                {
                    if (maxSeconds > 0 && (DateTime.UtcNow - startedUtc).TotalSeconds >= maxSeconds)
                    {
                        break;
                    }
                    if (Thread.VolatileRead(ref activeCount) <= 0)
                    {
                        break;
                    }
                }

                for (int i = 0; i < workers.Count; i++)
                {
                    try { workers[i].Join(1200); } catch { }
                }
            }
        }

        private static bool PlayViaWasapi(
            AudioDeviceInfo target,
            string soundPreset,
            WavAudioData customWav,
            bool forceVolume,
            int targetVolumePercent,
            ManualResetEvent stopSignal,
            DateTime startedUtc,
            int maxSeconds,
            out string error)
        {
            error = null;
            string deviceId = target != null ? target.Id : "";
            string deviceName = (target != null && !string.IsNullOrEmpty(target.FriendlyName)) ? target.FriendlyName : deviceId;
            IntPtr pMixFormat = IntPtr.Zero;
            IntPtr pFallbackFmt = IntPtr.Zero;
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioClient audioClient = null;
            IAudioRenderClient renderClient = null;
            ISimpleAudioVolume sessionVolume = null;
            IAudioMeterInformation meterInfo = null;
            IAudioClock audioClock = null;

            try
            {
                Type enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType);

                int hr = enumerator.GetDevice(deviceId, out device);
                if (hr != 0 || device == null)
                {
                    error = "GetDevice hr=0x" + hr.ToString("X8");
                    return false;
                }

                try
                {
                    Guid iidMeter = IID_IAudioMeterInformation;
                    object objMeter;
                    if (device.Activate(ref iidMeter, CLSCTX_ALL, IntPtr.Zero, out objMeter) == 0 && objMeter != null)
                    {
                        meterInfo = (IAudioMeterInformation)objMeter;
                    }
                }
                catch { }

                Guid iidClient = IID_IAudioClient;
                object objClient;
                hr = device.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out objClient);
                if (hr != 0 || objClient == null)
                {
                    error = "Activate IAudioClient hr=0x" + hr.ToString("X8");
                    return false;
                }
                audioClient = (IAudioClient)objClient;

                hr = audioClient.GetMixFormat(out pMixFormat);
                if (hr != 0 || pMixFormat == IntPtr.Zero)
                {
                    error = "GetMixFormat hr=0x" + hr.ToString("X8");
                    return false;
                }

                MixFormatDesc fmt = MixFormatDesc.FromIntPtr(pMixFormat);
                long bufferDurationHns = 2000000; // 200ms shared buffer
                IntPtr pSessionGuid = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
                bool usedAutoConvert = false;
                try
                {
                    Marshal.StructureToPtr(AlarmAudioSessionGuid, pSessionGuid, false);
                    if (fmt.IsSupported)
                    {
                        hr = audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, bufferDurationHns, 0, pMixFormat, pSessionGuid);
                    }
                    else
                    {
                        hr = unchecked((int)0x88890008); // AUDCLNT_E_UNSUPPORTED_FORMAT
                    }

                    if (hr != 0)
                    {
                        // Retry with Windows WASAPI automatic PCM resampling/format conversion (48kHz 16-bit stereo).
                        NativeMethods.WAVEFORMATEX fbWfx = new NativeMethods.WAVEFORMATEX();
                        fbWfx.wFormatTag = NativeMethods.WAVE_FORMAT_PCM;
                        fbWfx.nChannels = 2;
                        fbWfx.nSamplesPerSec = 48000;
                        fbWfx.wBitsPerSample = 16;
                        fbWfx.nBlockAlign = 4;
                        fbWfx.nAvgBytesPerSec = 48000 * 4;
                        fbWfx.cbSize = 0;

                        pFallbackFmt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.WAVEFORMATEX)));
                        Marshal.StructureToPtr(fbWfx, pFallbackFmt, false);

                        int hrFb = audioClient.Initialize(
                            AUDCLNT_SHAREMODE_SHARED,
                            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                            bufferDurationHns,
                            0,
                            pFallbackFmt,
                            pSessionGuid);
                        if (hrFb == 0)
                        {
                            hr = 0;
                            usedAutoConvert = true;
                            fmt = new MixFormatDesc
                            {
                                FormatTag = NativeMethods.WAVE_FORMAT_PCM,
                                Channels = 2,
                                SampleRate = 48000,
                                BitsPerSample = 16,
                                IsFloat = false,
                                IsSupported = true
                            };
                        }
                        else
                        {
                            error = "IAudioClient.Initialize hr=0x" + hr.ToString("X8") + " (AutoConvert hr=0x" + hrFb.ToString("X8") + ")";
                            return false;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pSessionGuid);
                }

                uint bufferFrames;
                hr = audioClient.GetBufferSize(out bufferFrames);
                if (hr != 0 || bufferFrames == 0)
                {
                    error = "GetBufferSize hr=0x" + hr.ToString("X8");
                    return false;
                }

                Guid iidRender = IID_IAudioRenderClient;
                object objRender;
                hr = audioClient.GetService(ref iidRender, out objRender);
                if (hr != 0 || objRender == null)
                {
                    error = "GetService IAudioRenderClient hr=0x" + hr.ToString("X8");
                    return false;
                }
                renderClient = (IAudioRenderClient)objRender;

                try
                {
                    Guid iidClock = IID_IAudioClock;
                    object objClock;
                    if (audioClient.GetService(ref iidClock, out objClock) == 0 && objClock != null)
                    {
                        audioClock = (IAudioClock)objClock;
                    }
                }
                catch { }

                Guid iidSimpleVol = IID_ISimpleAudioVolume;
                object objSessVol;
                if (audioClient.GetService(ref iidSimpleVol, out objSessVol) == 0 && objSessVol != null)
                {
                    sessionVolume = (ISimpleAudioVolume)objSessVol;
                    Guid ctx = Guid.Empty;
                    sessionVolume.SetMute(false, ref ctx);
                    sessionVolume.SetMasterVolume(1.0f, ref ctx);
                }

                AlarmWaveformGenerator gen = new AlarmWaveformGenerator((int)fmt.SampleRate, soundPreset, customWav);

                // Pre-fill initial buffer before Start
                if (!FillWasapiFrames(audioClient, renderClient, bufferFrames, fmt, gen))
                {
                    error = "Initial FillWasapiFrames failed.";
                    return false;
                }

                hr = audioClient.Start();
                if (hr != 0)
                {
                    error = "IAudioClient.Start hr=0x" + hr.ToString("X8");
                    return false;
                }

                DateTime streamStartedUtc = DateTime.UtcNow;
                DateTime lastVolumeEnforce = DateTime.UtcNow;
                float maxPeakObserved = 0f;
                bool meterReadOk = false;
                bool verifiedLogged = false;

                while (!stopSignal.WaitOne(25))
                {
                    if (maxSeconds > 0 && (DateTime.UtcNow - startedUtc).TotalSeconds >= maxSeconds)
                    {
                        break;
                    }

                    if (meterInfo != null && !verifiedLogged)
                    {
                        float pk;
                        if (meterInfo.GetPeakValue(out pk) == 0)
                        {
                            meterReadOk = true;
                            if (pk > maxPeakObserved) maxPeakObserved = pk;
                        }
                    }

                    if (!verifiedLogged && (DateTime.UtcNow - streamStartedUtc).TotalMilliseconds >= 850)
                    {
                        ulong clockPos = 0;
                        ulong clockFreq = 0;
                        bool clockReadOk = false;
                        if (audioClock != null)
                        {
                            ulong qpc;
                            if (audioClock.GetPosition(out clockPos, out qpc) == 0)
                            {
                                clockReadOk = true;
                            }
                            audioClock.GetFrequency(out clockFreq);
                        }

                        bool stalledClock = clockReadOk && clockPos == 0;
                        bool nonZeroSettled = gen.HasEmittedNonZero &&
                            (DateTime.UtcNow - gen.FirstNonZeroUtc).TotalMilliseconds >= 450;
                        bool silentMeter = meterReadOk && nonZeroSettled && maxPeakObserved < 0.0001f;
                        if (stalledClock || silentMeter)
                        {
                            error = string.Format("Silent/stalled WASAPI stream (clockPos={0}, maxPeak={1:F4})", clockPos, maxPeakObserved);
                            try { audioClient.Stop(); } catch { }
                            return false;
                        }

                        verifiedLogged = true;
                        AppLogger.Info("AudioRouter", string.Format(
                            "[{0}] WASAPI active: {1}Hz/{2}bit/{3}ch{4}, clockPos={5}, peak={6:F4}",
                            deviceName, fmt.SampleRate, fmt.BitsPerSample, fmt.Channels,
                            usedAutoConvert ? " (AutoConvert)" : "", clockPos, maxPeakObserved));
                    }

                    if (forceVolume && (DateTime.UtcNow - lastVolumeEnforce).TotalMilliseconds >= 300)
                    {
                        lastVolumeEnforce = DateTime.UtcNow;
                        string dummy;
                        ForceDeviceVolume(deviceId, targetVolumePercent, out dummy);
                        if (sessionVolume != null)
                        {
                            bool sMute;
                            float sVol;
                            if (sessionVolume.GetMute(out sMute) != 0 || sMute ||
                                sessionVolume.GetMasterVolume(out sVol) != 0 || sVol < 0.99f)
                            {
                                Guid ctx = Guid.Empty;
                                sessionVolume.SetMute(false, ref ctx);
                                sessionVolume.SetMasterVolume(1.0f, ref ctx);
                            }
                        }
                    }

                    uint padding;
                    hr = audioClient.GetCurrentPadding(out padding);
                    if (hr != 0)
                    {
                        error = "GetCurrentPadding hr=0x" + hr.ToString("X8");
                        return false;
                    }

                    uint available = bufferFrames > padding ? bufferFrames - padding : 0;
                    if (available > 0)
                    {
                        if (!FillWasapiFrames(audioClient, renderClient, available, fmt, gen))
                        {
                            error = "FillWasapiFrames failed mid-stream.";
                            return false;
                        }
                    }
                }

                try { audioClient.Stop(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (pFallbackFmt != IntPtr.Zero) Marshal.FreeHGlobal(pFallbackFmt);
                if (pMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pMixFormat);
                if (audioClock != null) Marshal.ReleaseComObject(audioClock);
                if (meterInfo != null) Marshal.ReleaseComObject(meterInfo);
                if (sessionVolume != null) Marshal.ReleaseComObject(sessionVolume);
                if (renderClient != null) Marshal.ReleaseComObject(renderClient);
                if (audioClient != null) Marshal.ReleaseComObject(audioClient);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        private static bool FillWasapiFrames(
            IAudioClient audioClient,
            IAudioRenderClient renderClient,
            uint frames,
            MixFormatDesc fmt,
            AlarmWaveformGenerator gen)
        {
            IntPtr pData;
            int hr = renderClient.GetBuffer(frames, out pData);
            if (hr != 0 || pData == IntPtr.Zero)
            {
                return false;
            }

            int channels = fmt.Channels;
            int totalSamples = (int)frames * channels;
            if (fmt.IsFloat && fmt.BitsPerSample == 32)
            {
                float[] buf = new float[totalSamples];
                for (uint f = 0; f < frames; f++)
                {
                    float s = gen.NextSample();
                    int baseIdx = (int)f * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        buf[baseIdx + ch] = s;
                    }
                }
                Marshal.Copy(buf, 0, pData, totalSamples);
            }
            else if (!fmt.IsFloat && fmt.BitsPerSample == 16)
            {
                short[] buf = new short[totalSamples];
                for (uint f = 0; f < frames; f++)
                {
                    short s = (short)Math.Max(-32767, Math.Min(32767, (int)(gen.NextSample() * 32000f)));
                    int baseIdx = (int)f * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        buf[baseIdx + ch] = s;
                    }
                }
                Marshal.Copy(buf, 0, pData, totalSamples);
            }
            else if (!fmt.IsFloat && fmt.BitsPerSample == 24)
            {
                int totalBytes = totalSamples * 3;
                byte[] buf = new byte[totalBytes];
                for (uint f = 0; f < frames; f++)
                {
                    int sample24 = Math.Max(-8388607, Math.Min(8388607, (int)(gen.NextSample() * 8300000.0)));
                    int baseIdx = (int)f * channels * 3;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int off = baseIdx + ch * 3;
                        buf[off] = (byte)(sample24 & 0xFF);
                        buf[off + 1] = (byte)((sample24 >> 8) & 0xFF);
                        buf[off + 2] = (byte)((sample24 >> 16) & 0xFF);
                    }
                }
                Marshal.Copy(buf, 0, pData, totalBytes);
            }
            else
            {
                // 32-bit integer PCM
                int[] buf = new int[totalSamples];
                for (uint f = 0; f < frames; f++)
                {
                    int s = (int)(gen.NextSample() * 2140000000.0);
                    int baseIdx = (int)f * channels;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        buf[baseIdx + ch] = s;
                    }
                }
                Marshal.Copy(buf, 0, pData, totalSamples);
            }

            hr = renderClient.ReleaseBuffer(frames, 0);
            return hr == 0;
        }

        private static bool PlayViaWinMM(
            AudioDeviceInfo target,
            string soundPreset,
            WavAudioData customWav,
            bool forceVolume,
            int targetVolumePercent,
            ManualResetEvent stopSignal,
            DateTime startedUtc,
            int maxSeconds,
            out string error)
        {
            error = null;
            IntPtr hWaveOut = IntPtr.Zero;
            IntPtr[] buffers = new IntPtr[2];
            IntPtr[] headers = new IntPtr[2];

            try
            {
                uint count = NativeMethods.waveOutGetNumDevs();
                if (count == 0)
                {
                    error = "No waveOut devices.";
                    return false;
                }

                uint matchedIndex = 0xFFFFFFFF;
                string matchedName = "";
                string targetFriendly = (target != null && target.FriendlyName != null) ? target.FriendlyName.Trim() : "";
                string targetDesc = (target != null && target.Description != null) ? target.Description.Trim() : "";
                string targetIface = (target != null && target.InterfaceName != null) ? target.InterfaceName.Trim() : "";
                bool targetIsSpeakers = target != null && string.Equals(target.Category, "Speakers", StringComparison.OrdinalIgnoreCase);

                int bestScore = 0;
                for (uint i = 0; i < count; i++)
                {
                    NativeMethods.WAVEOUTCAPS caps;
                    if (NativeMethods.waveOutGetDevCaps((IntPtr)i, out caps, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEOUTCAPS))) == 0)
                    {
                        string pname = (caps.szPname ?? "").Trim();
                        if (string.IsNullOrEmpty(pname))
                        {
                            continue;
                        }

                        int score = 0;
                        if (!string.IsNullOrEmpty(targetFriendly))
                        {
                            if (string.Equals(targetFriendly, pname, StringComparison.OrdinalIgnoreCase))
                            {
                                score = 100;
                            }
                            else if (targetFriendly.StartsWith(pname, StringComparison.OrdinalIgnoreCase) ||
                                     pname.StartsWith(targetFriendly, StringComparison.OrdinalIgnoreCase))
                            {
                                score = 90;
                            }
                        }
                        if (score == 0 && !string.IsNullOrEmpty(targetDesc) &&
                            pname.StartsWith(targetDesc, StringComparison.OrdinalIgnoreCase))
                        {
                            score = 60;
                            if (!string.IsNullOrEmpty(targetIface) &&
                                pname.IndexOf(targetIface.Substring(0, Math.Min(6, targetIface.Length)), StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                score = 80;
                            }
                        }
                        // Only allow generic "speaker" fallback when the target itself is actually a Speakers device
                        // (never hijack HDMI/Display or Headphones onto Speakers).
                        if (score == 0 && targetIsSpeakers &&
                            pname.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            pname.IndexOf("headphone", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            score = 30;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            matchedIndex = i;
                            matchedName = pname;
                        }
                    }
                }

                if (matchedIndex == 0xFFFFFFFF)
                {
                    error = "No matching WinMM device found for '" + targetFriendly + "' (refusing WAVE_MAPPER).";
                    return false;
                }

                AppLogger.Info("AudioRouter", string.Format("[{0}] Opening WinMM fallback waveOut#{1} '{2}'.", targetFriendly, matchedIndex, matchedName));

                const int sampleRate = 48000;
                const int channels = 2;
                const int chunkFrames = 4800; // 100ms
                int chunkBytes = chunkFrames * channels * 2;

                NativeMethods.WAVEFORMATEX wfx = new NativeMethods.WAVEFORMATEX();
                wfx.wFormatTag = NativeMethods.WAVE_FORMAT_PCM;
                wfx.nChannels = channels;
                wfx.nSamplesPerSec = sampleRate;
                wfx.wBitsPerSample = 16;
                wfx.nBlockAlign = (ushort)(channels * 2);
                wfx.nAvgBytesPerSec = (uint)(sampleRate * wfx.nBlockAlign);
                wfx.cbSize = 0;

                int mm = NativeMethods.waveOutOpen(out hWaveOut, matchedIndex, ref wfx, IntPtr.Zero, IntPtr.Zero, NativeMethods.CALLBACK_NULL);
                if (mm != NativeMethods.MMSYSERR_NOERROR || hWaveOut == IntPtr.Zero)
                {
                    error = "waveOutOpen err=" + mm;
                    return false;
                }

                NativeMethods.waveOutSetVolume(hWaveOut, 0xFFFFFFFF);
                AlarmWaveformGenerator gen = new AlarmWaveformGenerator(sampleRate, soundPreset, customWav);

                int hdrSize = Marshal.SizeOf(typeof(NativeMethods.WAVEHDR));
                for (int b = 0; b < 2; b++)
                {
                    buffers[b] = Marshal.AllocHGlobal(chunkBytes);
                    headers[b] = Marshal.AllocHGlobal(hdrSize);
                    FillWinMmBuffer(buffers[b], chunkFrames, channels, gen);

                    NativeMethods.WAVEHDR hdr = new NativeMethods.WAVEHDR();
                    hdr.lpData = buffers[b];
                    hdr.dwBufferLength = (uint)chunkBytes;
                    Marshal.StructureToPtr(hdr, headers[b], false);
                    NativeMethods.waveOutPrepareHeader(hWaveOut, headers[b], (uint)hdrSize);
                    NativeMethods.waveOutWrite(hWaveOut, headers[b], (uint)hdrSize);
                }

                DateTime lastVolEnforce = DateTime.UtcNow;
                while (!stopSignal.WaitOne(20))
                {
                    if (maxSeconds > 0 && (DateTime.UtcNow - startedUtc).TotalSeconds >= maxSeconds)
                    {
                        break;
                    }
                    if (forceVolume && (DateTime.UtcNow - lastVolEnforce).TotalMilliseconds >= 250)
                    {
                        lastVolEnforce = DateTime.UtcNow;
                        string dummy;
                        ForceDeviceVolume(target.Id, targetVolumePercent, out dummy);
                    }

                    for (int b = 0; b < 2; b++)
                    {
                        NativeMethods.WAVEHDR hdr = (NativeMethods.WAVEHDR)Marshal.PtrToStructure(headers[b], typeof(NativeMethods.WAVEHDR));
                        if ((hdr.dwFlags & NativeMethods.WHDR_DONE) != 0)
                        {
                            NativeMethods.waveOutUnprepareHeader(hWaveOut, headers[b], (uint)hdrSize);
                            FillWinMmBuffer(buffers[b], chunkFrames, channels, gen);
                            hdr.dwFlags = 0;
                            hdr.dwBufferLength = (uint)chunkBytes;
                            Marshal.StructureToPtr(hdr, headers[b], false);
                            NativeMethods.waveOutPrepareHeader(hWaveOut, headers[b], (uint)hdrSize);
                            NativeMethods.waveOutWrite(hWaveOut, headers[b], (uint)hdrSize);
                        }
                    }
                }

                NativeMethods.waveOutReset(hWaveOut);
                for (int b = 0; b < 2; b++)
                {
                    if (headers[b] != IntPtr.Zero)
                    {
                        NativeMethods.waveOutUnprepareHeader(hWaveOut, headers[b], (uint)hdrSize);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (hWaveOut != IntPtr.Zero)
                {
                    try { NativeMethods.waveOutClose(hWaveOut); } catch { }
                }
                for (int b = 0; b < 2; b++)
                {
                    if (headers[b] != IntPtr.Zero) Marshal.FreeHGlobal(headers[b]);
                    if (buffers[b] != IntPtr.Zero) Marshal.FreeHGlobal(buffers[b]);
                }
            }
        }

        private static void FillWinMmBuffer(IntPtr ptr, int frames, int channels, AlarmWaveformGenerator gen)
        {
            short[] pcm = new short[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                short s = (short)Math.Max(-32767, Math.Min(32767, (int)(gen.NextSample() * 32000f)));
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    pcm[baseIdx + c] = s;
                }
            }
            Marshal.Copy(pcm, 0, ptr, pcm.Length);
        }

        private static string GetDefaultDeviceId(IMMDeviceEnumerator enumerator, ERole role)
        {
            try
            {
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out dev) == 0 && dev != null)
                {
                    try
                    {
                        string id;
                        if (dev.GetId(out id) == 0)
                        {
                            return id;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(dev);
                    }
                }
            }
            catch { }
            return null;
        }

        private static AudioDeviceInfo InspectDevice(IMMDevice dev, string defaultMultimediaId, string defaultCommId)
        {
            string id;
            if (dev.GetId(out id) != 0 || string.IsNullOrEmpty(id))
            {
                return null;
            }

            string friendly = "";
            string desc = "";
            string iface = "";
            int formFactor = 10;

            IPropertyStore store;
            if (dev.OpenPropertyStore(STGM_READ, out store) == 0 && store != null)
            {
                try
                {
                    friendly = ReadStringProp(store, PKEY_Device_FriendlyName);
                    desc = ReadStringProp(store, PKEY_Device_DeviceDesc);
                    iface = ReadStringProp(store, PKEY_DeviceInterface_FriendlyName);
                    formFactor = ReadIntProp(store, PKEY_AudioEndpoint_FormFactor, 10);
                }
                finally
                {
                    Marshal.ReleaseComObject(store);
                }
            }

            float volScalar = 0f;
            bool muted = false;
            try
            {
                Guid iid = IID_IAudioEndpointVolume;
                object obj;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out obj) == 0 && obj != null)
                {
                    IAudioEndpointVolume vol = (IAudioEndpointVolume)obj;
                    try
                    {
                        vol.GetMasterVolumeLevelScalar(out volScalar);
                        vol.GetMute(out muted);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(vol);
                    }
                }
            }
            catch { }

            string category;
            int score = ScoreDevice(friendly, desc, iface, formFactor, out category);

            AudioDeviceInfo info = new AudioDeviceInfo();
            info.Id = id;
            info.FriendlyName = friendly;
            info.Description = desc;
            info.InterfaceName = iface;
            info.FormFactor = formFactor;
            info.FormFactorName = FormatFormFactor(formFactor);
            info.IsDefaultMultimedia = string.Equals(id, defaultMultimediaId, StringComparison.OrdinalIgnoreCase);
            info.IsDefaultCommunications = string.Equals(id, defaultCommId, StringComparison.OrdinalIgnoreCase);
            info.VolumeScalar = volScalar;
            info.IsMuted = muted;
            info.SpeakerScore = score;
            info.Category = category;
            return info;
        }

        private static int ScoreDevice(string friendly, string desc, string iface, int formFactor, out string category)
        {
            string combined = ((friendly ?? "") + " " + (desc ?? "") + " " + (iface ?? "")).ToLowerInvariant();

            if (ContainsAny(combined, "vb-audio", "voicemeeter", "virtual", "cable", "scream", "steam streaming", "oculus", "nvidia broadcast"))
            {
                category = "Virtual";
                return -500;
            }

            if (formFactor == 3 || formFactor == 5 || formFactor == 6 ||
                ContainsAny(combined, "headphone", "headset", "earphone", "earbud", "airpods", "buds", "hands-free", "hfp", "bthhf"))
            {
                category = "Headphones";
                return -1000;
            }

            if (combined.Contains("bluetooth") && !combined.Contains("speaker"))
            {
                category = "Headphones";
                return -900;
            }

            if (formFactor == 9 || ContainsAny(combined, "hdmi", "displayport", "nvidia high definition", "amd high definition", "intel(r) display"))
            {
                category = "HDMI/Display";
                return 10;
            }

            int score = 0;
            if (formFactor == 1 || formFactor == 2)
            {
                score += 150;
            }
            if (ContainsAny(combined, "speaker", "line out"))
            {
                score += 100;
            }
            if (ContainsAny(combined, "high definition audio", "realtek", "via ", "conexant", "sound blaster"))
            {
                score += 50;
            }
            if (combined.Contains("usb"))
            {
                score -= 10; // Prefer motherboard 3.5mm speakers over random USB audio unless pinned
            }

            if (score >= 100)
            {
                category = "Speakers";
                return score;
            }

            category = "Other";
            return score;
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (text.Contains(terms[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static string FormatFormFactor(int ff)
        {
            switch (ff)
            {
                case 0: return "RemoteNetwork";
                case 1: return "Speakers";
                case 2: return "LineLevel";
                case 3: return "Headphones";
                case 4: return "Microphone";
                case 5: return "Headset";
                case 6: return "Handset";
                case 7: return "DigitalPassthrough";
                case 8: return "SPDIF";
                case 9: return "DigitalDisplay(HDMI)";
                default: return "Unknown";
            }
        }

        private static string ReadStringProp(IPropertyStore store, PropertyKey key)
        {
            NativeMethods.PropVariant pv = new NativeMethods.PropVariant();
            try
            {
                if (store.GetValue(ref key, out pv) == 0 && pv.vt == 31 && pv.pointerValue != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(pv.pointerValue) ?? "";
                }
                return "";
            }
            catch
            {
                return "";
            }
            finally
            {
                NativeMethods.PropVariantClear(ref pv);
            }
        }

        private static int ReadIntProp(IPropertyStore store, PropertyKey key, int defaultValue)
        {
            NativeMethods.PropVariant pv = new NativeMethods.PropVariant();
            try
            {
                if (store.GetValue(ref key, out pv) == 0)
                {
                    if (pv.vt == 19 || pv.vt == 3) // VT_UI4 or VT_I4
                    {
                        return (int)(pv.pointerValue.ToInt64() & 0xFFFFFFFF);
                    }
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
            finally
            {
                NativeMethods.PropVariantClear(ref pv);
            }
        }

        private sealed class MixFormatDesc
        {
            public ushort FormatTag;
            public int Channels;
            public uint SampleRate;
            public ushort BitsPerSample;
            public bool IsFloat;
            public bool IsSupported;

            public static MixFormatDesc FromIntPtr(IntPtr ptr)
            {
                NativeMethods.WAVEFORMATEX wfx = (NativeMethods.WAVEFORMATEX)Marshal.PtrToStructure(ptr, typeof(NativeMethods.WAVEFORMATEX));
                MixFormatDesc d = new MixFormatDesc();
                d.FormatTag = wfx.wFormatTag;
                d.Channels = wfx.nChannels;
                d.SampleRate = wfx.nSamplesPerSec;
                d.BitsPerSample = wfx.wBitsPerSample;

                if (wfx.wFormatTag == NativeMethods.WAVE_FORMAT_IEEE_FLOAT && wfx.wBitsPerSample == 32)
                {
                    d.IsFloat = true;
                    d.IsSupported = true;
                }
                else if (wfx.wFormatTag == NativeMethods.WAVE_FORMAT_PCM && (wfx.wBitsPerSample == 16 || wfx.wBitsPerSample == 24 || wfx.wBitsPerSample == 32))
                {
                    d.IsFloat = false;
                    d.IsSupported = true;
                }
                else if (wfx.wFormatTag == NativeMethods.WAVE_FORMAT_EXTENSIBLE && wfx.cbSize >= 22)
                {
                    byte[] guidBytes = new byte[16];
                    Marshal.Copy(new IntPtr(ptr.ToInt64() + 24), guidBytes, 0, 16);
                    Guid sub = new Guid(guidBytes);
                    if (sub == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && wfx.wBitsPerSample == 32)
                    {
                        d.IsFloat = true;
                        d.IsSupported = true;
                    }
                    else if (sub == KSDATAFORMAT_SUBTYPE_PCM && (wfx.wBitsPerSample == 16 || wfx.wBitsPerSample == 24 || wfx.wBitsPerSample == 32))
                    {
                        d.IsFloat = false;
                        d.IsSupported = true;
                    }
                }
                return d;
            }
        }

        private sealed class AlarmWaveformGenerator
        {
            private readonly int _sampleRate;
            private readonly string _preset;
            private readonly WavAudioData _wav;
            private long _sampleIndex;
            private double _wavPos;
            private double _phaseA;
            private double _phaseB;
            public bool HasEmittedNonZero { get; private set; }
            public DateTime FirstNonZeroUtc { get; private set; }

            private static readonly double[] BugleFreqs = new double[]
            {
                392.00, 523.25, 659.25, 523.25, 659.25, 783.99, 659.25, 523.25,
                659.25, 523.25, 659.25, 783.99, 1046.50, 783.99, 659.25, 523.25
            };

            private static readonly double[] NeonArpFreqs = new double[]
            {
                440.00, 523.25, 659.25, 880.00, 783.99, 659.25, 587.33, 659.25
            };

            private static readonly double[][] PianoChords = new double[][]
            {
                new double[] { 261.63, 329.63, 392.00, 493.88, 587.33 }, // Cmaj9
                new double[] { 220.00, 261.63, 329.63, 392.00, 493.88 }, // Am9
                new double[] { 174.61, 261.63, 329.63, 440.00, 523.25 }, // Fmaj7
                new double[] { 196.00, 293.66, 392.00, 493.88, 659.25 }  // G6
            };

            public AlarmWaveformGenerator(int sampleRate, string preset, WavAudioData wav)
            {
                _sampleRate = sampleRate > 0 ? sampleRate : 48000;
                _preset = preset ?? "KlaxonPulse";
                _wav = wav;
            }

            public float NextSample()
            {
                float val = NextSampleCore();
                if (!HasEmittedNonZero && Math.Abs(val) > 0.0005f)
                {
                    HasEmittedNonZero = true;
                    FirstNonZeroUtc = DateTime.UtcNow;
                }
                return val;
            }

            private float NextSampleCore()
            {
                if (_wav != null && _wav.MonoSamples.Length > 0)
                {
                    int idx = (int)_wavPos % _wav.MonoSamples.Length;
                    float sample = _wav.MonoSamples[idx];
                    _wavPos += (double)_wav.SampleRate / _sampleRate;
                    if (_wavPos >= _wav.MonoSamples.Length)
                    {
                        _wavPos -= _wav.MonoSamples.Length;
                    }
                    return sample;
                }

                double t = (double)_sampleIndex / _sampleRate;
                _sampleIndex++;

                if (string.Equals(_preset, "EmergencySiren", StringComparison.OrdinalIgnoreCase))
                {
                    // Sweeping dual-harmonic siren (700 Hz <-> 1500 Hz every 0.8s, phase-continuous)
                    double phaseMod = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * 1.25 * t);
                    double freq = 700.0 + 800.0 * phaseMod;
                    _phaseA += 2.0 * Math.PI * freq / _sampleRate;
                    _phaseB += 2.0 * Math.PI * (freq * 1.5) / _sampleRate;
                    if (_phaseA > 1e6) _phaseA -= 1e6;
                    if (_phaseB > 1e6) _phaseB -= 1e6;
                    double s1 = Math.Sin(_phaseA);
                    double s2 = 0.45 * Math.Sin(_phaseB);
                    return (float)(Math.Tanh((s1 + s2) * 1.8) * 0.94);
                }

                if (string.Equals(_preset, "EscalatingChime", StringComparison.OrdinalIgnoreCase))
                {
                    double cycle = t % 1.2;
                    double[] notes = new double[] { 880.0, 1108.73, 1318.51, 1760.0 };
                    int noteIdx = Math.Min(3, (int)(cycle / 0.25));
                    if (cycle > 1.0)
                    {
                        return 0f;
                    }
                    double localT = cycle - noteIdx * 0.25;
                    double env = Math.Min(1.0, localT / 0.01) * Math.Exp(-localT * 4.5);
                    double wave = Math.Sin(2.0 * Math.PI * notes[noteIdx] * localT) + 0.35 * Math.Sin(4.0 * Math.PI * notes[noteIdx] * localT);
                    return (float)(Math.Tanh(wave) * env * 0.92);
                }

                if (string.Equals(_preset, "ClassicTwinBell", StringComparison.OrdinalIgnoreCase))
                {
                    // Mechanical twin-bell alarm clock: 18 Hz rapid hammer clatter on metallic inharmonic partials
                    double burst = t % 1.0;
                    if (burst > 0.72)
                    {
                        return 0f;
                    }
                    double hitPeriod = 1.0 / 18.0;
                    double hitT = burst % hitPeriod;
                    double strikeEnv = Math.Min(1.0, hitT / 0.0018) * Math.Exp(-hitT * 28.0);
                    double burstTail = burst > 0.66 ? Math.Max(0.0, (0.72 - burst) / 0.06) : 1.0;
                    double bell =
                        0.65 * Math.Sin(2.0 * Math.PI * 1480.0 * t) +
                        0.50 * Math.Sin(2.0 * Math.PI * 2915.0 * t) +
                        0.38 * Math.Sin(2.0 * Math.PI * 4170.0 * t) +
                        0.25 * Math.Sin(2.0 * Math.PI * 5620.0 * t);
                    return (float)(Math.Tanh(bell * (0.45 + 1.35 * strikeEnv)) * burstTail * 0.92);
                }

                if (string.Equals(_preset, "DigitalWatch", StringComparison.OrdinalIgnoreCase))
                {
                    // Classic sharp Casio-style rapid 4-chirp high-pitch piezo alarm
                    double cycle = t % 1.0;
                    if (cycle >= 0.48)
                    {
                        return 0f;
                    }
                    double sub = cycle % 0.12;
                    if (sub >= 0.062)
                    {
                        return 0f;
                    }
                    double env = Math.Min(1.0, sub / 0.002) * Math.Min(1.0, (0.062 - sub) / 0.003);
                    double p1 = Math.Sin(2.0 * Math.PI * 2048.0 * sub);
                    double p2 = 0.45 * Math.Sin(2.0 * Math.PI * 4096.0 * sub);
                    double p3 = 0.25 * Math.Sin(2.0 * Math.PI * 6144.0 * sub);
                    return (float)(Math.Tanh((p1 + p2 + p3) * 2.1) * env * 0.93);
                }

                if (string.Equals(_preset, "NuclearAlert", StringComparison.OrdinalIgnoreCase))
                {
                    // Heavy industrial two-tone tritone red-alert warning horn
                    double cycle = t % 1.1;
                    if (cycle >= 0.82)
                    {
                        return 0f;
                    }
                    double freq = cycle < 0.41 ? 440.0 : 622.25;
                    double local = cycle < 0.41 ? cycle : (cycle - 0.41);
                    double env = Math.Min(1.0, local / 0.012) * Math.Min(1.0, (0.41 - local) / 0.018);
                    _phaseA += 2.0 * Math.PI * freq / _sampleRate;
                    if (_phaseA > 1e6) _phaseA -= 1e6;
                    double fm = 0.55 * Math.Sin(_phaseA * 2.0);
                    double horn =
                        Math.Sin(_phaseA + fm) +
                        0.60 * Math.Sin(_phaseA * 2.0) +
                        0.40 * Math.Sin(_phaseA * 3.0) +
                        0.22 * Math.Sin(_phaseA * 5.0);
                    return (float)(Math.Tanh(horn * 1.45) * env * 0.95);
                }

                if (string.Equals(_preset, "RadarBeacon", StringComparison.OrdinalIgnoreCase))
                {
                    // Dramatic rising air-raid / radar turbine sweep (380 Hz -> 1420 Hz)
                    double cycle = t % 2.8;
                    double freq;
                    double env = 1.0;
                    if (cycle < 1.8)
                    {
                        double k = cycle / 1.8;
                        freq = 380.0 + 1040.0 * (k * k);
                    }
                    else if (cycle < 2.2)
                    {
                        freq = 1420.0;
                    }
                    else
                    {
                        double k = (cycle - 2.2) / 0.6;
                        freq = 1420.0 - 980.0 * k;
                        env = Math.Max(0.25, 1.0 - 0.7 * k);
                    }
                    _phaseA += 2.0 * Math.PI * freq / _sampleRate;
                    if (_phaseA > 1e6) _phaseA -= 1e6;
                    double wave =
                        Math.Sin(_phaseA) +
                        0.55 * Math.Sin(_phaseA * 2.0) +
                        0.28 * Math.Sin(_phaseA * 3.0);
                    return (float)(Math.Tanh(wave * 1.55) * env * 0.94);
                }

                if (string.Equals(_preset, "MilitaryBugle", StringComparison.OrdinalIgnoreCase))
                {
                    // Brassy Reveille-inspired wake-up fanfare
                    double cycle = t % 3.2;
                    if (cycle >= 2.72)
                    {
                        return 0f;
                    }
                    double step = 0.17;
                    int idx = Math.Min(BugleFreqs.Length - 1, (int)(cycle / step));
                    double local = cycle - idx * step;
                    double baseF = BugleFreqs[idx];
                    double vib = 1.0 + 0.008 * Math.Sin(2.0 * Math.PI * 5.5 * local);
                    double env = Math.Min(1.0, local / 0.012) * Math.Min(1.0, (step - local) / 0.020);
                    _phaseA += 2.0 * Math.PI * (baseF * vib) / _sampleRate;
                    if (_phaseA > 1e6) _phaseA -= 1e6;
                    double brass =
                        Math.Sin(_phaseA) +
                        0.68 * Math.Sin(_phaseA * 2.0) +
                        0.48 * Math.Sin(_phaseA * 3.0) +
                        0.26 * Math.Sin(_phaseA * 4.0) +
                        0.14 * Math.Sin(_phaseA * 5.0);
                    return (float)(Math.Tanh(brass * 0.95) * env * 0.92);
                }

                if (string.Equals(_preset, "CyberNeonPulse", StringComparison.OrdinalIgnoreCase))
                {
                    // Driving synthwave arpeggio + tight sub-bass pulse
                    double step = 0.125; // 120 BPM 16th-notes
                    int stepIdx = (int)(t / step);
                    double local = t - stepIdx * step;
                    double leadF = NeonArpFreqs[stepIdx % NeonArpFreqs.Length];
                    double bassF = ((stepIdx / 8) % 2 == 0) ? 110.0 : 130.81;
                    double env = Math.Min(1.0, local / 0.006) * Math.Exp(-local * 9.0);
                    // Detuned dual-saw approximation
                    double p1 = ((local * leadF) % 1.0) * 2.0 - 1.0;
                    double p2 = ((local * (leadF * 1.006)) % 1.0) * 2.0 - 1.0;
                    double lead = (p1 + p2) * 0.5 * env;
                    double subEnv = Math.Exp(-((t % 0.25)) * 7.0);
                    double sub = 0.55 * Math.Sin(2.0 * Math.PI * bassF * t) * subEnv;
                    return (float)(Math.Tanh((lead * 1.3 + sub) * 1.35) * 0.92);
                }

                if (string.Equals(_preset, "SonarPingAlert", StringComparison.OrdinalIgnoreCase))
                {
                    // Submarine sonar ping alternating with urgent triple-ping burst
                    double cycle = t % 2.0;
                    double local;
                    double freq = 1320.0;
                    double decay = 4.2;
                    if (cycle < 0.95)
                    {
                        local = cycle;
                    }
                    else if (cycle < 1.22)
                    {
                        local = cycle - 0.95;
                        freq = 1480.0;
                        decay = 9.0;
                    }
                    else if (cycle < 1.49)
                    {
                        local = cycle - 1.22;
                        freq = 1480.0;
                        decay = 9.0;
                    }
                    else
                    {
                        local = cycle - 1.49;
                        freq = 1760.0;
                        decay = 6.5;
                    }
                    double env = Math.Min(1.0, local / 0.004) * Math.Exp(-local * decay);
                    double ping =
                        Math.Sin(2.0 * Math.PI * freq * local) +
                        0.32 * Math.Sin(2.0 * Math.PI * (freq * 2.01) * local) * Math.Exp(-local * 6.0) +
                        0.15 * Math.Sin(2.0 * Math.PI * (freq * 3.0) * local) * Math.Exp(-local * 12.0);
                    return (float)(Math.Tanh(ping * 1.35) * env * 0.92);
                }

                if (string.Equals(_preset, "MorningPiano", StringComparison.OrdinalIgnoreCase))
                {
                    // Warm electric-piano / Rhodes arpeggio progression (pleasant wake-up)
                    double chordDur = 1.6;
                    int chordIdx = (int)(t / chordDur) % PianoChords.Length;
                    double localChord = t % chordDur;
                    double[] notes = PianoChords[chordIdx];
                    double mix = 0.0;
                    for (int n = 0; n < notes.Length; n++)
                    {
                        double start = n * 0.14;
                        if (localChord < start) continue;
                        double nt = localChord - start;
                        double atk = Math.Min(1.0, nt / 0.008);
                        double body = Math.Exp(-nt * 2.3);
                        double tine = 0.30 * Math.Exp(-nt * 16.0);
                        double f = notes[n];
                        mix += atk * (
                            body * Math.Sin(2.0 * Math.PI * f * nt) +
                            0.22 * body * Math.Sin(2.0 * Math.PI * (f * 2.0) * nt) +
                            tine * Math.Sin(2.0 * Math.PI * (f * 4.0) * nt));
                    }
                    return (float)(Math.Tanh(mix * 0.55) * 0.90);
                }

                // Default: KlaxonPulse (piercing 3-beep alarm clock burst every 1.0s)
                double p = t % 1.0;
                bool active = (p < 0.14) || (p >= 0.22 && p < 0.36) || (p >= 0.44 && p < 0.58);
                if (!active)
                {
                    return 0f;
                }
                double fundamental = Math.Sin(2.0 * Math.PI * 960.0 * t);
                double fifth = Math.Sin(2.0 * Math.PI * 1440.0 * t);
                double thirdHarm = 0.33 * Math.Sin(2.0 * Math.PI * 2880.0 * t);
                return (float)(Math.Tanh((fundamental + 0.65 * fifth + thirdHarm) * 2.2) * 0.95);
            }
        }

        private sealed class WavAudioData
        {
            public int SampleRate;
            public float[] MonoSamples;

            private const long MaxWavFileBytes = 25 * 1024 * 1024; // 25 MB cap prevents OOM DoS

            public static WavAudioData TryLoad(string path)
            {
                try
                {
                    string safeErr;
                    if (!SecurityHelper.IsSafeLocalWavPath(path, out safeErr))
                    {
                        AppLogger.Warn("AudioRouter", "Blocked unsafe WAV path: " + safeErr);
                        return null;
                    }
                    FileInfo fi = new FileInfo(path);
                    if (!fi.Exists || fi.Length <= 44 || fi.Length > MaxWavFileBytes)
                    {
                        AppLogger.Warn("AudioRouter", "Custom WAV missing, empty, or exceeds 25 MB limit.");
                        return null;
                    }

                    using (FileStream fs = File.OpenRead(path))
                    using (BinaryReader br = new BinaryReader(fs))
                    {
                        if (new string(br.ReadChars(4)) != "RIFF") return null;
                        br.ReadInt32();
                        if (new string(br.ReadChars(4)) != "WAVE") return null;

                        ushort audioFormat = 0;
                        ushort channels = 0;
                        int sampleRate = 0;
                        ushort bitsPerSample = 0;
                        byte[] dataBytes = null;

                        while (fs.Position + 8 <= fs.Length)
                        {
                            string chunkId = new string(br.ReadChars(4));
                            int chunkSize = br.ReadInt32();
                            if (chunkSize < 0 || chunkSize > MaxWavFileBytes || fs.Position + chunkSize > fs.Length) break;

                            if (chunkId == "fmt ")
                            {
                                if (chunkSize < 16) return null;
                                audioFormat = br.ReadUInt16();
                                channels = br.ReadUInt16();
                                sampleRate = br.ReadInt32();
                                br.ReadInt32(); // byteRate
                                br.ReadUInt16(); // blockAlign
                                bitsPerSample = br.ReadUInt16();
                                int remain = chunkSize - 16;
                                if (remain > 0) fs.Seek(remain, SeekOrigin.Current);
                            }
                            else if (chunkId == "data")
                            {
                                dataBytes = br.ReadBytes(chunkSize);
                            }
                            else
                            {
                                fs.Seek(chunkSize, SeekOrigin.Current);
                            }
                            if ((chunkSize & 1) == 1 && fs.Position < fs.Length)
                            {
                                fs.Seek(1, SeekOrigin.Current);
                            }
                        }

                        if (dataBytes == null || channels == 0 || channels > 8 || sampleRate < 8000 || sampleRate > 192000) return null;
                        if (audioFormat != 1 && audioFormat != 3) return null;
                        if (bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32) return null;

                        int bytesPerSample = bitsPerSample / 8;
                        int frameSize = bytesPerSample * channels;
                        if (frameSize <= 0) return null;

                        int frames = dataBytes.Length / frameSize;
                        float[] mono = new float[frames];
                        for (int f = 0; f < frames; f++)
                        {
                            double sum = 0;
                            for (int c = 0; c < channels; c++)
                            {
                                int off = f * frameSize + c * bytesPerSample;
                                if (audioFormat == 3 && bitsPerSample == 32)
                                {
                                    sum += BitConverter.ToSingle(dataBytes, off);
                                }
                                else if (audioFormat == 1 && bitsPerSample == 16)
                                {
                                    sum += BitConverter.ToInt16(dataBytes, off) / 32768.0;
                                }
                                else if (audioFormat == 1 && bitsPerSample == 8)
                                {
                                    sum += (dataBytes[off] - 128) / 128.0;
                                }
                                else if (audioFormat == 1 && bitsPerSample == 24)
                                {
                                    int v = dataBytes[off] | (dataBytes[off + 1] << 8) | ((sbyte)dataBytes[off + 2] << 16);
                                    sum += v / 8388608.0;
                                }
                            }
                            mono[f] = (float)Math.Max(-1.0, Math.Min(1.0, sum / channels));
                        }

                        WavAudioData res = new WavAudioData();
                        res.SampleRate = sampleRate;
                        res.MonoSamples = mono;
                        return res;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
            public PropertyKey(Guid g, uint p) { fmtid = g; pid = p; }
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
            [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int pcDevices);
            [PreserveSig] int Item(int nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            [PreserveSig] int GetState(out uint pdwState);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig] int GetValue(ref PropertyKey key, out NativeMethods.PropVariant pv);
            [PreserveSig] int SetValue(ref PropertyKey key, ref NativeMethods.PropVariant pv);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
            [PreserveSig] int GetChannelCount(out uint pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
            [PreserveSig] int GetStreamLatency(out long phnsLatency);
            [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
            [PreserveSig] int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
            [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
            [PreserveSig] int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float fLevel, ref Guid EventContext);
            [PreserveSig] int GetMasterVolume(out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float pfPeak);
            [PreserveSig] int GetMeteringChannelCount(out uint pnChannelCount);
            [PreserveSig] int GetChannelsPeakValues(uint u32ChannelCount, [Out] float[] afPeakValues);
            [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        }

        [ComImport, Guid("CD63314F-3FBA-4a1b-812C-EF96358728E7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClock
        {
            [PreserveSig] int GetFrequency(out ulong pu64Frequency);
            [PreserveSig] int GetPosition(out ulong pu64Position, out ulong pu64QPCPosition);
            [PreserveSig] int GetCharacteristics(out uint pdwCharacteristics);
        }
    }
}
