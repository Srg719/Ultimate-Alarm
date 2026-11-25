using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace UltimateAlarm.CoreService
{
    using UltimateAlarm.Core;

    public static class ServiceEntry
    {
        public static bool IsServiceArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return false;
            return string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(arg, "--audio-worker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(arg, "--lock-session", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(arg, "--ensure-service", StringComparison.OrdinalIgnoreCase);
        }

        public static int Run(string[] args)
        {
            if (args == null || args.Length == 0 || string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
            {
                ServiceBase.Run(new ServiceBase[] { new AlarmService() });
                return 0;
            }

            if (string.Equals(args[0], "--audio-worker", StringComparison.OrdinalIgnoreCase))
            {
                int maxSeconds = 0;
                if (args.Length > 1)
                {
                    int parsed;
                    if (int.TryParse(args[1], out parsed))
                    {
                        maxSeconds = parsed;
                    }
                }
                return RunAudioWorker(maxSeconds);
            }

            if (string.Equals(args[0], "--console", StringComparison.OrdinalIgnoreCase))
            {
                return RunConsoleMode();
            }

            if (string.Equals(args[0], "--lock-session", StringComparison.OrdinalIgnoreCase))
            {
                bool locked = NativeMethods.LockWorkStation();
                AppLogger.Info("SessionLock", "LockWorkStation invoked inside user session; result=" + locked);
                return locked ? 0 : 1;
            }

            if (string.Equals(args[0], "--ensure-service", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureServiceRunning();
            }

            return 1;
        }

        private static int EnsureServiceRunning()
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
                AppLogger.Warn("ServiceEntry", "ensure-service failed: " + ex.Message);
                return 1;
            }
        }

        private static int RunConsoleMode()
        {
            Console.WriteLine("UltimateAlarm Service running in console mode. Press Ctrl+C or Enter to stop.");
            AlarmService svc = new AlarmService();
            svc.StartEngine();

            ManualResetEvent done = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                done.Set();
            };

            Thread keyThread = new Thread(delegate()
            {
                try { Console.ReadLine(); } catch { }
                done.Set();
            });
            keyThread.IsBackground = true;
            keyThread.Start();

            done.WaitOne();
            svc.StopEngine();
            return 0;
        }

        private static int RunAudioWorker(int maxSecondsOverride)
        {
            try
            {
                string userName = "";
                try
                {
                    using (System.Security.Principal.WindowsIdentity wid = System.Security.Principal.WindowsIdentity.GetCurrent())
                    {
                        userName = wid != null ? wid.Name : "";
                    }
                }
                catch { }
                AppLogger.Info("AudioWorker", "Started audio worker PID=" + System.Diagnostics.Process.GetCurrentProcess().Id +
                    " Session=" + System.Diagnostics.Process.GetCurrentProcess().SessionId +
                    (string.IsNullOrEmpty(userName) ? "" : (" User=" + userName)));

                AlarmStore store = new AlarmStore();
                AlarmConfig cfg = store.LoadConfig();
                ActiveRingState ring = store.LoadActiveRing();
                if (ring == null)
                {
                    AppLogger.Warn("AudioWorker", "No active_ring.json found; exiting.");
                    return 0;
                }

                if (maxSecondsOverride <= 0 && ring.IsTest)
                {
                    maxSecondsOverride = 10;
                }

                PowerWakeManager.DisablePowerThrottling();
                PowerWakeManager.WakeDisplayAndDismissScreenSaver();

                using (ManualResetEvent localStop = new ManualResetEvent(false))
                {
                    EventWaitHandle globalStop = AlarmService.OpenOrCreateStopEvent(true);
                    int tickCount = 0;
                    Thread watcher = new Thread(delegate()
                    {
                        while (!localStop.WaitOne(200))
                        {
                            tickCount++;
                            if (tickCount % 40 == 0)
                            {
                                PowerWakeManager.WakeDisplayAndDismissScreenSaver();
                            }
                            if (globalStop != null && globalStop.WaitOne(0))
                            {
                                localStop.Set();
                                break;
                            }
                            if (!File.Exists(AppPaths.ActiveRingPath))
                            {
                                localStop.Set();
                                break;
                            }
                        }
                    });
                    watcher.IsBackground = true;
                    watcher.Start();

                    AudioRouter.PlayAlarmLoop(cfg, ring, localStop, maxSecondsOverride);
                    localStop.Set();
                    watcher.Join(500);
                    if (globalStop != null)
                    {
                        globalStop.Close();
                    }
                }

                PowerWakeManager.SetExecutionHold(false, false);
                AppLogger.Info("AudioWorker", "Exiting cleanly.");
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("AudioWorker", "Fatal error: " + ex);
                return 1;
            }
        }
    }
}
