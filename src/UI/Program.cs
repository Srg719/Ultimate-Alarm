using System;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using UltimateAlarm.Core;
using UltimateAlarm.CoreService;

namespace UltimateAlarm.UI
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _globalSessionPresence;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && ServiceEntry.IsServiceArgument(args[0]))
            {
                return ServiceEntry.Run(args);
            }

            try { NativeMethods.SetProcessDPIAware(); } catch { }

            bool startMinimized = false;
            bool forceSetup = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase)) startMinimized = true;
                    if (string.Equals(args[i], "--setup", StringComparison.OrdinalIgnoreCase)) forceSetup = true;
                }
            }

            if (!AcquireSingleInstanceMutex(forceSetup ? 2000 : 200))
            {
                if (!startMinimized && SignalExistingUiToShow())
                {
                    return 0;
                }
                if (!startMinimized)
                {
                    MessageBox.Show("UltimateAlarm UI is already running in the system tray.", "UltimateAlarm", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            PublishSessionPresence();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AlarmStore store = new AlarmStore();
            AlarmConfig cfg = store.LoadConfig();
            UiTheme.ApplyTheme(cfg.UiTheme);

            if (forceSetup || (!cfg.SetupCompleted && !startMinimized))
            {
                using (SetupWizardForm wizard = new SetupWizardForm(ReleaseSingleInstanceMutex))
                {
                    DialogResult res = wizard.ShowDialog();
                    if (wizard.ElevationRequested || res == DialogResult.Abort)
                    {
                        ReleaseSingleInstanceMutex();
                        return 0;
                    }
                }
            }

            Application.Run(new MainForm(startMinimized, ReleaseSingleInstanceMutex));
            ReleaseSingleInstanceMutex();
            return 0;
        }

        private static bool AcquireSingleInstanceMutex(int retryMs)
        {
            int elapsed = 0;
            while (true)
            {
                bool createdNew;
                Mutex m = new Mutex(true, "Local\\UltimateAlarmUiSingleInstance", out createdNew);
                if (createdNew)
                {
                    _singleInstanceMutex = m;
                    return true;
                }
                m.Close();
                if (elapsed >= retryMs)
                {
                    return false;
                }
                Thread.Sleep(150);
                elapsed += 150;
            }
        }

        private static bool SignalExistingUiToShow()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting("Local\\UltimateAlarmShowUiEvent", out ev))
                {
                    using (ev)
                    {
                        ev.Set();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void PublishSessionPresence()
        {
            try
            {
                string name = "Global\\UltimateAlarmUiSession_" + Process.GetCurrentProcess().SessionId;
                EventWaitHandleSecurity sec = new EventWaitHandleSecurity();
                SecurityIdentifier sys = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(sys, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new EventWaitHandleAccessRule(admin, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new EventWaitHandleAccessRule(authUsers, EventWaitHandleRights.Synchronize, AccessControlType.Allow));
                bool created;
                _globalSessionPresence = new EventWaitHandle(false, EventResetMode.ManualReset, name, out created, sec);
            }
            catch { }
        }

        private static void ReleaseSingleInstanceMutex()
        {
            if (_globalSessionPresence != null)
            {
                try { _globalSessionPresence.Close(); } catch { }
                _globalSessionPresence = null;
            }
            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                try { _singleInstanceMutex.Close(); } catch { }
                _singleInstanceMutex = null;
            }
        }
    }
}
