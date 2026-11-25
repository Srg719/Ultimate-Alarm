using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using UltimateAlarm.Core;

namespace UltimateAlarm.UI
{
    internal sealed class SetupWizardForm : DarkChromeForm
    {
        private const int SidebarWidth = 232;

        private readonly Action _releaseMutexForElevation;
        private readonly AlarmStore _store = new AlarmStore();
        private AlarmConfig _workingCfg;
        private WakeDiagnostics _diag;
        private List<AudioDeviceInfo> _devices;

        private int _stepIndex = 0;
        private Panel[] _stepPanels;
        private StepNavItem[] _stepNavItems;
        private Panel _contentHost;

        private ModernButton _btnBack;
        private ModernButton _btnNext;
        private ModernButton _btnElevate;

        // Step 2 controls
        private ModernComboBox _cmbSpeakers;
        private ModernSlider _trkWizardVolume;
        private ModernNumeric _numWizardVolume;
        private ModernCheckBox _chkRingAll;
        private ModernCheckBox _chkHdmi;
        private ModernCheckBox _chkHeadphones;
        private Panel _wizardDevicesHost;
        private ModernButton _btnWizardTestAudio;
        private Label _lblAudioTestStatus;
        private readonly object _testLock = new object();
        private ManualResetEvent _testStopEvent;
        private Icon _wizardIcon;
        private bool _suppressWizardEvents;

        // Step 3 controls
        private ModernRadioButton _rbModeA;
        private ModernRadioButton _rbModeB;
        private ModernCheckBox _chkAutoLock;
        private ModernTextBox _txtDismissPin;
        private ModernCheckBox _chkTrayAutoStart;
        private ModernCheckBox _chkShortcuts;
        private Panel _modeBInfoPanel;

        // Step 4 controls
        private ModernCheckBox _chkCreateFirstAlarm;
        private ModernTimePicker _dtFirstTime;
        private ModernTextBox _txtFirstLabel;
        private ModernComboBox _cmbFirstRepeat;
        private ModernComboBox _cmbFirstSound;
        private ModernComboBox _cmbFirstChallenge;
        private ModernTextBox _txtApplyLog;
        private ModernButton _btnApplyAll;
        private bool _applySucceeded;
        private bool _isApplying;

        public bool ElevationRequested { get; private set; }

        public SetupWizardForm(Action releaseMutexForElevation)
            : base(false, false, false, true)
        {
            _releaseMutexForElevation = releaseMutexForElevation;
            _workingCfg = _store.LoadConfig();
            _devices = AudioRouter.EnumerateActiveOutputDevices();
            _diag = PowerWakeManager.CollectDiagnostics(_workingCfg, null);

            Text = "UltimateAlarm - Setup Wizard";
            Size = new Size(900, 660);
            StartPosition = FormStartPosition.CenterScreen;

            _wizardIcon = UiTheme.CreateAppIcon(false, 32);
            Icon = _wizardIcon;
            RefreshChromeIcon(_wizardIcon);

            BuildShell();
            ShowStep(0);
        }

        private void BuildShell()
        {
            SetLeftTitleSidebarMatch(SidebarWidth, UiTheme.BgShell, true);
            PinVisibleTitleText("UltimateAlarm");

            Panel sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = SidebarWidth,
                BackColor = UiTheme.BgShell,
                Padding = new Padding(0, 12, 0, 0)
            };
            sidebar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
                }
            };

            Label lblWizSub = new Label
            {
                Text = "SETUP WIZARD",
                Font = UiTheme.FontSmallBold,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(20, 8),
                AutoSize = true
            };
            sidebar.Controls.Add(lblWizSub);

            string[] titles = new string[]
            {
                "Permissions & PC",
                "Speaker Routing",
                "Sign-In & Security",
                "Alarm & Apply"
            };
            _stepNavItems = new StepNavItem[titles.Length];
            for (int i = 0; i < titles.Length; i++)
            {
                int stepIdx = i;
                StepNavItem item = new StepNavItem(i + 1, titles[i])
                {
                    Location = new Point(0, 36 + i * 44),
                    Width = SidebarWidth - 1
                };
                item.Click += delegate { if (!_isApplying) ShowStep(stepIdx); };
                _stepNavItems[i] = item;
                sidebar.Controls.Add(item);
            }

            // Properly docked privilege badge at bottom of sidebar
            Panel privFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                BackColor = UiTheme.BgShell,
                Padding = new Padding(14, 10, 14, 14)
            };
            bool isAdmin = SecurityHelper.IsAdministrator();
            CardPanel privCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                CardColor = UiTheme.BgHealthCard,
                BorderColor = isAdmin ? UiTheme.SuccessBorder : Color.FromArgb(110, UiTheme.WarnAmber.R, UiTheme.WarnAmber.G, UiTheme.WarnAmber.B),
                CornerRadius = 6
            };
            Label lblModeNote = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 6, 10, 6),
                Font = UiTheme.FontSmallBold,
                ForeColor = isAdmin ? UiTheme.SuccessGreen : UiTheme.WarnAmber,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = isAdmin
                    ? "Administrator Ready"
                    : "Standard User\n(UAC needed to apply)"
            };
            privCard.Controls.Add(lblModeNote);
            privFooter.Controls.Add(privCard);
            sidebar.Controls.Add(privFooter);

            // Footer action bar
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = UiTheme.BgShell
            };
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
                }
            };

            _btnBack = new ModernButton
            {
                Text = "Back",
                Glyph = UiGlyph.ChevronLeft,
                Width = 96,
                Height = 34,
                Location = new Point(22, 13),
                Tone = ButtonTone.Secondary
            };
            _btnBack.Click += delegate { ShowStep(_stepIndex - 1); };

            ModernButton btnCancel = new ModernButton
            {
                Text = "Skip for now",
                Width = 108,
                Height = 34,
                Location = new Point(400, 13),
                Tone = ButtonTone.Ghost
            };
            btnCancel.Click += delegate
            {
                DialogResult = _applySucceeded ? DialogResult.OK : DialogResult.Cancel;
                Close();
            };

            _btnNext = new ModernButton
            {
                Text = "Next",
                Glyph = UiGlyph.ChevronRight,
                Width = 128,
                Height = 34,
                Location = new Point(516, 13),
                Tone = ButtonTone.Primary
            };
            _btnNext.Click += delegate { OnNextClicked(); };

            footer.Resize += delegate
            {
                _btnNext.Location = new Point(footer.Width - _btnNext.Width - 26, 13);
                btnCancel.Location = new Point(_btnNext.Left - btnCancel.Width - 8, 13);
            };
            footer.Controls.AddRange(new Control[] { _btnBack, btnCancel, _btnNext });

            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.BgCanvas,
                Padding = new Padding(26, 14, 26, 14)
            };

            _stepPanels = new Panel[]
            {
                BuildStep1Permissions(),
                BuildStep2Audio(),
                BuildStep3Security(),
                BuildStep4AlarmAndApply()
            };
            for (int i = 0; i < _stepPanels.Length; i++)
            {
                _stepPanels[i].Dock = DockStyle.Fill;
                _stepPanels[i].Visible = false;
                _contentHost.Controls.Add(_stepPanels[i]);
            }

            Panel rightShell = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.BgCanvas };
            rightShell.Controls.Add(_contentHost);
            rightShell.Controls.Add(footer);

            BodyHost.Controls.Add(rightShell);
            BodyHost.Controls.Add(sidebar);
        }

        private Panel BuildStep1Permissions()
        {
            Panel p = new Panel { BackColor = UiTheme.BgCanvas };
            AddHeader(p, "System Permissions & Hardware Check",
                "UltimateAlarm collects required permissions and hardware readiness upfront so it runs autonomously.");

            CardPanel hwCard = new CardPanel { Location = new Point(0, 64), Size = new Size(612, 192) };
            string boardName = (_diag.MotherboardManufacturer + " " + _diag.MotherboardModel).Trim();
            if (string.IsNullOrEmpty(boardName)) boardName = "Generic Windows PC";

            AddKvRow(hwCard, 16, "Motherboard", boardName);
            AddKvRow(hwCard, 44, "Firmware", (_diag.BiosVendor + " " + _diag.BiosVersion).Trim());
            AddKvRow(hwCard, 72, "Sleep Support",
                "S3 Sleep: " + (_diag.SleepS3Supported ? "Yes" : "No") +
                "  ·  Modern Standby S0: " + (_diag.ModernStandbyS0Supported ? "Yes" : "No") +
                "  ·  Hibernate S4: " + (_diag.HibernateS4Supported ? "Yes" : "No"));
            AddKvRow(hwCard, 100, "Wake Timers",
                "AC Power: " + (_diag.WakeTimersEnabledAC ? "Enabled" : "Disabled") +
                "  ·  DC Battery: " + (_diag.WakeTimersEnabledDC ? "Enabled" : "Disabled"));
            AddKvRow(hwCard, 128, "Audio Devices",
                _devices.Count + " active output(s)  ·  Target: " + _diag.SelectedSpeakerName);
            AddKvRow(hwCard, 156, "Service State",
                _diag.ServiceRunning ? "Running (LocalSystem)" : (_diag.ServiceInstalled ? "Installed (Stopped)" : "Not installed yet"));
            p.Controls.Add(hwCard);

            bool isAdmin = SecurityHelper.IsAdministrator();
            CardPanel permCard = new CardPanel
            {
                Location = new Point(0, 270),
                Size = new Size(612, 224),
                BorderColor = isAdmin ? UiTheme.SuccessBorder : Color.FromArgb(110, 82, 25),
                LeftAccentColor = isAdmin ? UiTheme.SuccessGreen : UiTheme.WarnAmber
            };

            Label lblPermTitle = new Label
            {
                Text = isAdmin
                    ? "Administrator Access Granted"
                    : "Administrator Permission Required",
                Font = UiTheme.FontSubTitle,
                ForeColor = isAdmin ? UiTheme.SuccessGreen : UiTheme.WarnAmber,
                Location = new Point(18, 16),
                Size = new Size(574, 24)
            };

            Label lblPermBody = new Label
            {
                Location = new Point(18, 46),
                Size = new Size(574, 114),
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Text = "During Step 4, setup will apply these system protections:\n\n" +
                       "  1. Install UltimateAlarmService as LocalSystem (rings before Windows sign-in)\n" +
                       "  2. Enable AC & DC Windows RTC Wake Timers + Task Scheduler backup\n" +
                       "  3. Configure boot security (Auto-Lock guard & PBKDF2 Dismiss PIN)\n" +
                       "  4. Register Tray auto-start and optional Desktop / Start Menu shortcuts"
            };

            _btnElevate = new ModernButton
            {
                Text = "Grant Administrator Permission (UAC)",
                Glyph = UiGlyph.Lock,
                Location = new Point(18, 170),
                Size = new Size(300, 34),
                Tone = ButtonTone.Primary,
                Visible = !isAdmin
            };
            _btnElevate.Click += delegate { RequestElevation(); };

            permCard.Controls.AddRange(new Control[] { lblPermTitle, lblPermBody, _btnElevate });
            p.Controls.Add(permCard);
            return p;
        }

        private void AddKvRow(Control host, int y, string key, string value)
        {
            Label lk = new Label
            {
                Text = key,
                Location = new Point(18, y),
                Size = new Size(125, 22),
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                UseMnemonic = false
            };
            Label lv = new Label
            {
                Text = value,
                Location = new Point(148, y),
                Size = new Size(446, 22),
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextPrimary,
                UseMnemonic = false
            };
            host.Controls.Add(lk);
            host.Controls.Add(lv);
        }

        private Panel BuildStep2Audio()
        {
            Panel p = new Panel { BackColor = UiTheme.BgCanvas };
            AddHeader(p, "Speaker Detection & Headphone Protection",
                "Verify which output device is your physical speaker so alarms never ring quietly into headphones.");

            Label lblSel = new Label { Text = "Preferred Speaker", Location = new Point(0, 68), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold };
            _cmbSpeakers = new ModernComboBox { Location = new Point(144, 62), Width = 468 };
            _cmbSpeakers.AddItem(new SpeakerChoice("", "Auto-detect Physical Speakers (Recommended)"));
            int selIdx = 0;
            for (int i = 0; i < _devices.Count; i++)
            {
                AudioDeviceInfo d = _devices[i];
                int idx = _cmbSpeakers.AddItem(new SpeakerChoice(d.Id, "[" + d.Category + "] " + d.FriendlyName));
                if (!string.IsNullOrEmpty(_workingCfg.PinnedSpeakerDeviceId) &&
                    string.Equals(_workingCfg.PinnedSpeakerDeviceId, d.Id, StringComparison.OrdinalIgnoreCase))
                {
                    selIdx = idx;
                }
            }
            _cmbSpeakers.SelectedIndex = selIdx;
            _cmbSpeakers.Enabled = !_workingCfg.RingAllOutputDevices;

            int initVol = AudioRouter.ResolveTargetVolumePercent(_workingCfg, null);
            Label lblVol = new Label { Text = "Alarm Volume", Location = new Point(0, 108), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold };
            _trkWizardVolume = new ModernSlider
            {
                Location = new Point(138, 104),
                Width = 374,
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Value = initVol
            };
            _numWizardVolume = new ModernNumeric
            {
                Location = new Point(520, 102),
                Size = new Size(92, 32),
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Suffix = "%",
                Value = initVol
            };
            _trkWizardVolume.Scroll += delegate { _numWizardVolume.Value = _trkWizardVolume.Value; };
            _numWizardVolume.ValueChanged += delegate
            {
                int v = _numWizardVolume.Value;
                if (_trkWizardVolume.Value != v) _trkWizardVolume.Value = v;
                if (_testStopEvent == null)
                {
                    _btnWizardTestAudio.Text = "Test Output @ " + v + "% (3s)";
                }
            };

            _chkRingAll = new ModernCheckBox
            {
                Text = "Ring all connected outputs simultaneously (Speakers, HDMI, USB)",
                Location = new Point(0, 144),
                Size = new Size(612, 24),
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary
            };
            _chkRingAll.SetCheckedSilent(_workingCfg.RingAllOutputDevices);
            _chkRingAll.CheckedChanged += delegate { OnWizardRingAllChanged(); };

            _chkHdmi = new ModernCheckBox
            {
                Text = "Allow Monitor HDMI speaker fallback if 3.5mm Speakers are missing",
                Location = new Point(0, 170),
                Size = new Size(612, 24),
                ForeColor = UiTheme.TextSecondary
            };
            _chkHdmi.SetCheckedSilent(_workingCfg.AllowHdmiFallback);

            _chkHeadphones = new ModernCheckBox
            {
                Text = "Also ring Headphones / Headsets at target volume",
                BadgeText = "OVERRIDE",
                BadgeColor = UiTheme.WarnAmber,
                Location = new Point(0, 196),
                Size = new Size(612, 24),
                ForeColor = UiTheme.TextSecondary
            };
            _chkHeadphones.SetCheckedSilent(_workingCfg.AllowHeadphoneFallback);

            _btnWizardTestAudio = new ModernButton
            {
                Text = "Test Output @ " + initVol + "% (3s)",
                Glyph = UiGlyph.Play,
                Location = new Point(0, 228),
                Size = new Size(216, 32),
                Tone = ButtonTone.Primary
            };
            _btnWizardTestAudio.Click += delegate { ToggleWizardAudioTest(); };

            _lblAudioTestStatus = new Label
            {
                Text = "Minimum volume floor is 30% so alarms stay audible.",
                Location = new Point(228, 234),
                Size = new Size(384, 22),
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.FontSmall
            };

            // Clean custom dark device table instead of stark white Win32 ListView headers
            _wizardDevicesHost = new Panel
            {
                Location = new Point(0, 270),
                Size = new Size(612, 234),
                AutoScroll = true,
                BackColor = UiTheme.BgCanvas
            };
            UiTheme.ApplyDarkScrollbars(_wizardDevicesHost);
            int dy = 0;
            int rowW = _devices.Count > 4 ? 592 : 612;
            for (int i = 0; i < _devices.Count; i++)
            {
                AudioDeviceInfo d = _devices[i];
                Color catColor = d.Category == "Speakers" ? UiTheme.SuccessGreen
                    : (d.Category == "Headphones" ? UiTheme.DangerRed : UiTheme.WarnAmber);
                CardPanel row = new CardPanel
                {
                    Location = new Point(0, dy),
                    Size = new Size(rowW, 52),
                    LeftAccentColor = catColor
                };
                Label lCat = new Label
                {
                    Text = d.Category.ToUpperInvariant(),
                    Font = UiTheme.FontSmallBold,
                    ForeColor = catColor,
                    Location = new Point(14, 17),
                    Size = new Size(92, 18)
                };
                Label lDev = new Label
                {
                    Text = d.FriendlyName,
                    Font = UiTheme.FontBodyBold,
                    ForeColor = UiTheme.TextPrimary,
                    Location = new Point(112, 8),
                    Size = new Size(rowW - 210, 20)
                };
                Label lForm = new Label
                {
                    Text = d.FormFactorName + "  ·  Priority " + d.SpeakerScore,
                    Font = UiTheme.FontSmall,
                    ForeColor = UiTheme.TextSecondary,
                    Location = new Point(112, 28),
                    Size = new Size(rowW - 210, 16)
                };
                Label lVolRow = new Label
                {
                    Text = string.Format("{0:0}%", d.VolumeScalar * 100f),
                    Font = UiTheme.FontBodyBold,
                    ForeColor = UiTheme.TextSecondary,
                    Location = new Point(rowW - 76, 16),
                    Size = new Size(60, 20),
                    TextAlign = ContentAlignment.MiddleRight
                };
                row.Controls.AddRange(new Control[] { lCat, lDev, lForm, lVolRow });
                _wizardDevicesHost.Controls.Add(row);
                dy += 58;
            }

            p.Controls.AddRange(new Control[]
            {
                lblSel, _cmbSpeakers, lblVol, _trkWizardVolume, _numWizardVolume,
                _chkRingAll, _chkHdmi, _chkHeadphones, _btnWizardTestAudio, _lblAudioTestStatus, _wizardDevicesHost
            });
            return p;
        }

        private Panel BuildStep3Security()
        {
            Panel p = new Panel { BackColor = UiTheme.BgCanvas };
            AddHeader(p, "Windows Sign-In, Boot Security & Dismiss PIN",
                "Choose how UltimateAlarm protects your PC when waking or booting with a Windows sign-in password.");

            bool useModeB = string.Equals(_workingCfg.AuthMode, "AutoLogonAndLock", StringComparison.OrdinalIgnoreCase);

            CardPanel cardA = new CardPanel { Location = new Point(0, 64), Size = new Size(612, 92) };
            _rbModeA = new ModernRadioButton
            {
                Text = "Mode A (Recommended): Zero Stored Password — LocalSystem Console",
                Location = new Point(16, 12),
                Size = new Size(576, 24),
                ForeColor = UiTheme.SuccessGreen,
                Checked = !useModeB
            };
            Label lblA = new Label
            {
                Text = "Rings directly at the Windows Lock / Sign-In screen as LocalSystem. Never asks for or stores your Windows password anywhere.",
                Location = new Point(42, 42),
                Size = new Size(550, 38),
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.FontSmall
            };
            cardA.Controls.AddRange(new Control[] { _rbModeA, lblA });

            CardPanel cardB = new CardPanel { Location = new Point(0, 168), Size = new Size(612, 176) };
            _rbModeB = new ModernRadioButton
            {
                Text = "Mode B (Optional): Boot Auto-Lock Guard (for Sysinternals Autologon PCs)",
                Location = new Point(16, 12),
                Size = new Size(576, 24),
                ForeColor = UiTheme.AccentPrimary,
                Checked = useModeB
            };
            Label lblB = new Label
            {
                Text = "If your audio driver suspends output before login, enable Windows Auto-Logon via Microsoft's signed Sysinternals Autologon tool. UltimateAlarm will immediately lock your screen right after boot.",
                Location = new Point(42, 40),
                Size = new Size(550, 38),
                ForeColor = UiTheme.TextSecondary,
                Font = UiTheme.FontSmall
            };

            _modeBInfoPanel = new Panel { Location = new Point(42, 84), Size = new Size(552, 78), BackColor = UiTheme.BgSurface };
            _chkAutoLock = new ModernCheckBox
            {
                Text = "Auto-lock workstation immediately after boot (LockWorkStation guard)",
                Location = new Point(0, 2),
                Size = new Size(540, 24),
                ForeColor = UiTheme.TextPrimary
            };
            _chkAutoLock.SetCheckedSilent(_workingCfg.AutoLockOnBoot);

            ModernButton btnNetplwiz = new ModernButton { Text = "Windows User Accounts (netplwiz)", Location = new Point(0, 36), Size = new Size(256, 30), Tone = ButtonTone.Secondary, Font = UiTheme.FontSmallBold };
            btnNetplwiz.Click += delegate { TryLaunchTool("netplwiz.exe"); };

            ModernButton btnSysint = new ModernButton { Text = "Microsoft Sysinternals Autologon", Location = new Point(266, 36), Size = new Size(256, 30), Tone = ButtonTone.Secondary, Font = UiTheme.FontSmallBold };
            btnSysint.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://learn.microsoft.com/en-us/sysinternals/downloads/autologon", UseShellExecute = true }); } catch { }
            };

            _modeBInfoPanel.Controls.AddRange(new Control[] { _chkAutoLock, btnNetplwiz, btnSysint });
            cardB.Controls.AddRange(new Control[] { _rbModeB, lblB, _modeBInfoPanel });

            _rbModeA.CheckedChanged += delegate { _modeBInfoPanel.Enabled = _rbModeB.Checked; };
            _rbModeB.CheckedChanged += delegate { _modeBInfoPanel.Enabled = _rbModeB.Checked; };
            _modeBInfoPanel.Enabled = _rbModeB.Checked;

            CardPanel pinCard = new CardPanel { Location = new Point(0, 356), Size = new Size(612, 126) };
            Label lPin = new Label { Text = "Alarm Dismiss PIN", Location = new Point(16, 18), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold };
            _txtDismissPin = new ModernTextBox { Location = new Point(176, 12), Size = new Size(180, 32), UseSystemPasswordChar = true };
            Label lPinHint = new Label { Text = "(blank = keep existing / use Math)", Location = new Point(366, 19), AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontSmall };

            _chkTrayAutoStart = new ModernCheckBox
            {
                Text = "Launch UltimateAlarm Tray UI automatically when Windows signs in",
                Location = new Point(16, 54),
                Size = new Size(570, 24),
                ForeColor = UiTheme.TextPrimary
            };
            _chkTrayAutoStart.SetCheckedSilent(_workingCfg.LaunchUiInTrayAtLogon);

            _chkShortcuts = new ModernCheckBox
            {
                Text = "Create Desktop & Start Menu shortcuts for UltimateAlarm",
                Location = new Point(16, 84),
                Size = new Size(570, 24),
                ForeColor = UiTheme.TextPrimary
            };
            _chkShortcuts.SetCheckedSilent(true);

            pinCard.Controls.AddRange(new Control[] { lPin, _txtDismissPin, lPinHint, _chkTrayAutoStart, _chkShortcuts });

            p.Controls.AddRange(new Control[] { cardA, cardB, pinCard });
            return p;
        }

        private Panel BuildStep4AlarmAndApply()
        {
            Panel p = new Panel { BackColor = UiTheme.BgCanvas };
            AddHeader(p, "Initial Alarm & Apply Configuration",
                "Optionally create your first alarm, then apply all permissions, wake timers, and service protections.");

            CardPanel alarmCard = new CardPanel { Location = new Point(0, 64), Size = new Size(612, 184) };
            _chkCreateFirstAlarm = new ModernCheckBox
            {
                Text = "Create and arm an initial alarm now",
                Location = new Point(16, 14),
                Size = new Size(400, 24),
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary
            };
            _chkCreateFirstAlarm.SetCheckedSilent(_workingCfg.Alarms == null || _workingCfg.Alarms.Count == 0);

            Label lt = new Label { Text = "Time", Location = new Point(16, 54), AutoSize = true, ForeColor = UiTheme.TextSecondary };
            _dtFirstTime = new ModernTimePicker { Location = new Point(62, 46), Size = new Size(116, 34), Value = new DateTime(2026, 1, 1, 7, 0, 0) };

            Label ll = new Label { Text = "Label", Location = new Point(192, 54), AutoSize = true, ForeColor = UiTheme.TextSecondary };
            _txtFirstLabel = new ModernTextBox { Location = new Point(238, 47), Size = new Size(166, 32), Text = "Morning Wake Up" };

            Label lr = new Label { Text = "Repeat", Location = new Point(416, 54), AutoSize = true, ForeColor = UiTheme.TextSecondary };
            _cmbFirstRepeat = new ModernComboBox { Location = new Point(468, 47), Width = 126 };
            _cmbFirstRepeat.AddItems("Every Day", "Weekdays", "Once");
            _cmbFirstRepeat.SelectedIndex = 0;

            Label ls = new Label { Text = "Sound", Location = new Point(16, 98), AutoSize = true, ForeColor = UiTheme.TextSecondary };
            _cmbFirstSound = new ModernComboBox { Location = new Point(62, 92), Width = 208 };
            for (int i = 0; i < AudioRouter.BuiltInPresets.Length; i++)
            {
                _cmbFirstSound.AddItem(AudioRouter.BuiltInPresets[i]);
            }
            _cmbFirstSound.SelectedIndex = 0;

            Label lc = new Label { Text = "Challenge", Location = new Point(282, 98), AutoSize = true, ForeColor = UiTheme.TextSecondary };
            _cmbFirstChallenge = new ModernComboBox { Location = new Point(352, 92), Width = 148 };
            _cmbFirstChallenge.AddItems("Math", "TypeCode", "Pin", "None");
            _cmbFirstChallenge.SelectedIndex = 0;

            _btnApplyAll = new ModernButton
            {
                Text = "Apply All Permissions & Start Protection",
                Glyph = UiGlyph.Check,
                Location = new Point(16, 136),
                Size = new Size(320, 34),
                Tone = ButtonTone.Success
            };
            _btnApplyAll.Click += delegate { RunFullSetupApply(); };

            alarmCard.Controls.AddRange(new Control[]
            {
                _chkCreateFirstAlarm, lt, _dtFirstTime, ll, _txtFirstLabel, lr, _cmbFirstRepeat,
                ls, _cmbFirstSound, lc, _cmbFirstChallenge, _btnApplyAll
            });

            _txtApplyLog = new ModernTextBox
            {
                Location = new Point(0, 260),
                Size = new Size(612, 244),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = UiTheme.FontMono,
                Text = "Click 'Apply All Permissions & Start Protection' above to install the LocalSystem service, enable AC/DC wake timers, save security settings, and display your hardware BIOS RTC instructions."
            };

            p.Controls.AddRange(new Control[] { alarmCard, _txtApplyLog });
            return p;
        }

        private void AddHeader(Panel p, string title, string subtitle)
        {
            Label lt = new Label { Text = title, Font = UiTheme.FontTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(0, 0), Size = new Size(612, 28), UseMnemonic = false };
            Label ls = new Label { Text = subtitle, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Location = new Point(0, 30), Size = new Size(612, 26), UseMnemonic = false };
            p.Controls.Add(lt);
            p.Controls.Add(ls);
        }

        private void ShowStep(int index)
        {
            _stepIndex = Math.Max(0, Math.Min(_stepPanels.Length - 1, index));
            for (int i = 0; i < _stepPanels.Length; i++)
            {
                _stepPanels[i].Visible = (i == _stepIndex);
                _stepNavItems[i].State = (i == _stepIndex)
                    ? StepNavState.Active
                    : (i < _stepIndex ? StepNavState.Completed : StepNavState.Pending);
            }
            _btnBack.Enabled = (_stepIndex > 0 && !_isApplying);
            if (_stepIndex == _stepPanels.Length - 1)
            {
                _btnNext.Text = _applySucceeded ? "Finish" : "Apply & Finish";
                _btnNext.Glyph = _applySucceeded ? UiGlyph.Check : UiGlyph.ChevronRight;
                _btnNext.Tone = _applySucceeded ? ButtonTone.Success : ButtonTone.Primary;
            }
            else
            {
                _btnNext.Text = "Next";
                _btnNext.Glyph = UiGlyph.ChevronRight;
                _btnNext.Tone = ButtonTone.Primary;
            }
        }

        private void OnNextClicked()
        {
            if (_stepIndex < _stepPanels.Length - 1)
            {
                ShowStep(_stepIndex + 1);
                return;
            }
            if (_applySucceeded)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                RunFullSetupApply();
            }
        }

        private void TryLaunchTool(string exe)
        {
            try { Process.Start(new ProcessStartInfo { FileName = SecurityHelper.ResolveSystemTool(exe), UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "UltimateAlarm", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void OnWizardRingAllChanged()
        {
            if (_suppressWizardEvents) return;
            _cmbSpeakers.Enabled = !_chkRingAll.Checked;
            if (_chkRingAll.Checked && !_chkHeadphones.Checked)
            {
                DialogResult res = MessageBox.Show(
                    "Permission Confirmation — Multi-Device Broadcast\n\n" +
                    "UltimateAlarm will ring ALL connected speakers & HDMI/monitor audio devices simultaneously at your chosen alarm volume (" + _numWizardVolume.Value + "%).\n\n" +
                    "Do you ALSO grant permission to ring plugged-in Headphones / Headsets simultaneously?\n\n" +
                    "• Yes = Ring EVERY connected output (including Headphones)\n" +
                    "• No  = Ring all Speakers & HDMI monitors while keeping Headphones blocked",
                    "Multi-Output Audio Permission",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    _suppressWizardEvents = true;
                    _chkHeadphones.SetCheckedSilent(true);
                    _suppressWizardEvents = false;
                }
            }
        }

        private void ToggleWizardAudioTest()
        {
            lock (_testLock)
            {
                if (_testStopEvent != null)
                {
                    try { _testStopEvent.Set(); } catch { }
                    return;
                }
            }

            int testVol = AudioRouter.ClampVolumePercent(_numWizardVolume.Value);
            SpeakerChoice choice = _cmbSpeakers.SelectedItem as SpeakerChoice;
            AlarmConfig testCfg = new AlarmConfig
            {
                PinnedSpeakerDeviceId = choice != null ? choice.Id : "",
                RingAllOutputDevices = _chkRingAll.Checked,
                AlarmVolumePercent = testVol,
                AllowHdmiFallback = _chkHdmi.Checked,
                AllowHeadphoneFallback = _chkHeadphones.Checked
            };

            List<AudioDeviceInfo> targets = AudioRouter.SelectTargetDevices(testCfg, null);
            if (targets.Count == 0)
            {
                _lblAudioTestStatus.Text = "No eligible output device active!";
                _lblAudioTestStatus.ForeColor = UiTheme.DangerRed;
                return;
            }

            string targetDesc = targets.Count > 1
                ? ("ALL " + targets.Count + " connected output devices")
                : targets[0].FriendlyName;

            _lblAudioTestStatus.Text = "Playing @ " + testVol + "% unmuted -> " + targetDesc;
            _lblAudioTestStatus.ForeColor = UiTheme.AccentPrimary;
            _btnWizardTestAudio.Text = "Stop Test";
            _btnWizardTestAudio.Glyph = UiGlyph.Stop;
            _btnWizardTestAudio.Tone = ButtonTone.Danger;
            ManualResetEvent stopHandle = new ManualResetEvent(false);
            lock (_testLock)
            {
                _testStopEvent = stopHandle;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ActiveRingState r = new ActiveRingState
                    {
                        SoundPreset = "KlaxonPulse",
                        ForceFullVolume = true,
                        TargetVolumePercent = testVol,
                        IsTest = true
                    };
                    AudioRouter.PlayAlarmLoop(testCfg, r, stopHandle, 3);
                }
                finally
                {
                    lock (_testLock)
                    {
                        if (_testStopEvent == stopHandle)
                        {
                            _testStopEvent = null;
                        }
                    }
                    try { stopHandle.Dispose(); } catch { }
                    if (!IsDisposed)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _btnWizardTestAudio.Text = "Test Output @ " + _numWizardVolume.Value + "% (3s)";
                                _btnWizardTestAudio.Glyph = UiGlyph.Play;
                                _btnWizardTestAudio.Tone = ButtonTone.Primary;
                                _lblAudioTestStatus.Text = "Test finished (" + testVol + "%) on: " + targetDesc;
                                _lblAudioTestStatus.ForeColor = UiTheme.SuccessGreen;
                            });
                        }
                        catch { }
                    }
                }
            });
        }

        private void RequestElevation()
        {
            if (_releaseMutexForElevation != null)
            {
                _releaseMutexForElevation();
            }
            string exe = SecurityHelper.GetMainAppExePath();
            string err;
            if (SecurityHelper.RelaunchElevated(exe, "--setup", out err))
            {
                ElevationRequested = true;
                DialogResult = DialogResult.Abort;
                Close();
            }
            else
            {
                MessageBox.Show(err, "Elevation Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunFullSetupApply()
        {
            if (_isApplying) return;

            if (!SecurityHelper.IsAdministrator())
            {
                DialogResult r = MessageBox.Show(
                    "Administrator permission is required to install UltimateAlarmService, enable Wake Timers, and configure startup protections.\n\nElevate via UAC now?",
                    "Administrator Permission Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r == DialogResult.Yes)
                {
                    RequestElevation();
                }
                return;
            }

            bool useModeB = _rbModeB.Checked;
            SpeakerChoice spkChoice = _cmbSpeakers.SelectedItem as SpeakerChoice;
            string pinnedSpeaker = spkChoice != null ? spkChoice.Id : "";
            int targetVol = AudioRouter.ClampVolumePercent(_numWizardVolume.Value);
            bool ringAllOutputs = _chkRingAll.Checked;
            bool allowHdmi = _chkHdmi.Checked;
            bool allowHp = _chkHeadphones.Checked;
            bool autoLock = _chkAutoLock.Checked;
            bool trayStart = _chkTrayAutoStart.Checked;
            bool shortcuts = _chkShortcuts.Checked;
            string dismissPinRaw = (_txtDismissPin.Text ?? "").Trim();

            bool addFirst = _chkCreateFirstAlarm.Checked;
            int firstHour = _dtFirstTime.Hour;
            int firstMin = _dtFirstTime.Minute;
            string firstLabel = string.IsNullOrWhiteSpace(_txtFirstLabel.Text) ? "Morning Wake Up" : _txtFirstLabel.Text.Trim();
            string firstRepeat = _cmbFirstRepeat.SelectedItem.ToString();
            AudioRouter.SoundPresetInfo firstSoundInfo = _cmbFirstSound.SelectedItem as AudioRouter.SoundPresetInfo;
            string firstSound = firstSoundInfo != null ? firstSoundInfo.Id : _cmbFirstSound.SelectedItem.ToString();
            string firstChallenge = _cmbFirstChallenge.SelectedItem.ToString();

            _isApplying = true;
            _btnApplyAll.Enabled = false;
            _btnBack.Enabled = false;
            _btnNext.Enabled = false;
            _txtApplyLog.Clear();

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool allOk = true;
                try
                {
                    AppendLog("[1/6] Saving configuration & alarm schedule...");
                    AlarmConfig cfg = _store.LoadConfig();
                    cfg.SetupCompleted = true;
                    cfg.AuthMode = useModeB ? "AutoLogonAndLock" : "LocalSystem";
                    cfg.AutoLockOnBoot = autoLock;
                    cfg.LaunchUiInTrayAtLogon = trayStart;
                    cfg.PinnedSpeakerDeviceId = pinnedSpeaker;
                    cfg.RingAllOutputDevices = ringAllOutputs;
                    cfg.AlarmVolumePercent = targetVol;
                    cfg.AllowHdmiFallback = allowHdmi;
                    cfg.AllowHeadphoneFallback = allowHp;
                    cfg.EnableTaskSchedulerWakeBackup = true;
                    cfg.UiTheme = UiTheme.CurrentThemeId;
                    AppendLog("      Audio mode: " + (ringAllOutputs ? "Ring ALL connected outputs simultaneously" : "Single Speaker priority") + " @ " + targetVol + "% (Headphones=" + (allowHp ? "Allowed" : "Blocked") + ").");

                    if (!string.IsNullOrEmpty(dismissPinRaw))
                    {
                        string hash, salt;
                        SecurityHelper.CreatePinHash(dismissPinRaw, out hash, out salt);
                        cfg.DismissPinHash = hash;
                        cfg.DismissPinSalt = salt;
                        AppendLog("      Dismiss PIN hashed with PBKDF2 + random salt.");
                    }

                    if (addFirst)
                    {
                        AlarmItem alarm = new AlarmItem
                        {
                            Hour = firstHour,
                            Minute = firstMin,
                            Label = firstLabel,
                            SoundPreset = firstSound,
                            ChallengeType = (firstChallenge == "Pin" && string.IsNullOrEmpty(cfg.DismissPinHash)) ? "Math" : firstChallenge,
                            VolumePercent = targetVol,
                            ForceFullVolume = true
                        };
                        if (firstRepeat == "Every Day") alarm.DaysOfWeek = new int[] { 0, 1, 2, 3, 4, 5, 6 };
                        else if (firstRepeat == "Weekdays") alarm.DaysOfWeek = new int[] { 1, 2, 3, 4, 5 };
                        else
                        {
                            alarm.DaysOfWeek = new int[0];
                            alarm.EnsureOneShotDate(DateTime.Now);
                        }
                        cfg.Alarms.Add(alarm);
                        AppendLog(string.Format("      Armed initial alarm: {0:00}:{1:00} ({2}) [{3}]", alarm.Hour, alarm.Minute, alarm.FormatDays(), alarm.Label));
                    }

                    _store.SaveConfig(cfg);

                    AppendLog("[2/6] Boot security mode: " + cfg.AuthMode + (useModeB ? (" (AutoLockOnBoot=" + autoLock + ")") : ""));

                    AppendLog("[3/6] Hardening Power Plans (AC/DC Wake Timers, USB & Audio Idle)...");
                    string wLog, wErr;
                    if (PowerWakeManager.OptimizePowerForAlarms(out wLog, out wErr))
                    {
                        AppendLog("      All power schemes, USB suspend, and audio idle sleep hardened.");
                    }
                    else
                    {
                        AppendLog("      [WARN] " + wErr);
                    }

                    AppendLog("[4/6] Configuring Tray auto-start" + (shortcuts ? " & Desktop/Start Menu shortcuts..." : "..."));
                    string tErr;
                    if (SecurityHelper.SetTrayAutoStart(trayStart, out tErr))
                    {
                        AppendLog("      Tray auto-start " + (trayStart ? "enabled." : "disabled."));
                    }
                    if (shortcuts)
                    {
                        SecurityHelper.CreateAppShortcuts(SecurityHelper.GetMainAppExePath(), true, true);
                        AppendLog("      Desktop & Start Menu shortcuts created.");
                    }

                    AppendLog("[5/6] Installing & starting UltimateAlarmService (LocalSystem)...");
                    string sLog, sErr;
                    if (SecurityHelper.InstallOrRepairService(out sLog, out sErr))
                    {
                        AppendLog("      Service RUNNING as NT AUTHORITY\\SYSTEM.");
                    }
                    else
                    {
                        AppendLog("      [ERROR] " + sErr);
                        allOk = false;
                    }

                    AppendLog("[6/6] Syncing service IPC & WakeToRun Task Scheduler backup...");
                    ServiceStatusResponse ipcResp = IpcProtocol.SendRequest(new IpcRequest { Command = "APPLY_SETUP_CONFIG", Config = cfg }, 2500);
                    if (ipcResp != null && ipcResp.Success)
                    {
                        AppendLog("      Service IPC ONLINE | Next Wake: " + ipcResp.NextWakeIso);
                    }
                    else
                    {
                        AppendLog("      [WARN] IPC sync pending: " + (ipcResp != null ? ipcResp.Error : "timeout"));
                    }

                    HardwareInfo hw = SecurityHelper.GetHardwareInfo();
                    AppendLog("");
                    AppendLog("=== SETUP " + (allOk ? "SUCCEEDED" : "COMPLETED WITH WARNINGS") + " ===");
                    AppendLog(SecurityHelper.BuildBiosRtcGuide(hw));
                }
                catch (Exception ex)
                {
                    allOk = false;
                    AppendLog("[FATAL] " + ex.Message);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _isApplying = false;
                                _applySucceeded = allOk;
                                _btnApplyAll.Enabled = true;
                                _btnBack.Enabled = true;
                                _btnNext.Enabled = true;
                                if (allOk)
                                {
                                    _btnNext.Text = "Finish";
                                    _btnNext.Glyph = UiGlyph.Check;
                                    _btnNext.Tone = ButtonTone.Success;
                                }
                            });
                        }
                        catch { }
                    }
                }
            });
        }

        private void AppendLog(string line)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _txtApplyLog.AppendText(line + Environment.NewLine);
                });
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            lock (_testLock)
            {
                if (_testStopEvent != null)
                {
                    try { _testStopEvent.Set(); } catch { }
                }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_wizardIcon != null) { try { _wizardIcon.Dispose(); } catch { } _wizardIcon = null; }
            base.OnFormClosed(e);
        }

        private enum StepNavState
        {
            Pending,
            Active,
            Completed
        }

        private sealed class StepNavItem : Control
        {
            private readonly int _num;
            private readonly string _title;
            private StepNavState _state;
            private bool _hover;

            public StepNavState State
            {
                get { return _state; }
                set { _state = value; Invalidate(); }
            }

            public StepNavItem(int num, string title)
            {
                _num = num;
                _title = title;
                Height = 38;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgShell);

                if (_state == StepNavState.Active || _hover)
                {
                    Color bg = _state == StepNavState.Active ? UiTheme.BgNavActive : UiTheme.BgNavHover;
                    using (GraphicsPath gp = UiTheme.CreateRoundRect(new Rectangle(12, 1, Width - 24, Height - 2), 6))
                    using (SolidBrush br = new SolidBrush(bg))
                    {
                        g.FillPath(br, gp);
                    }
                }

                Rectangle badgeRect = new Rectangle(22, (Height - 20) / 2, 20, 20);
                Color badgeBg = _state == StepNavState.Active ? UiTheme.AccentPrimary
                    : (_state == StepNavState.Completed ? UiTheme.SuccessGreen : UiTheme.BgElevated);
                using (SolidBrush bbr = new SolidBrush(badgeBg))
                {
                    g.FillEllipse(bbr, badgeRect);
                }

                if (_state == StepNavState.Completed)
                {
                    UiIcons.DrawGlyph(g, UiGlyph.Check, new Rectangle(badgeRect.X + 4, badgeRect.Y + 4, 12, 12), UiTheme.TextOnSuccess, 1.8f);
                }
                else
                {
                    TextRenderer.DrawText(g, _num.ToString(CultureInfo.InvariantCulture), UiTheme.FontSmallBold, badgeRect,
                        _state == StepNavState.Active ? UiTheme.TextOnAccent : UiTheme.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                Color fg = _state == StepNavState.Active ? UiTheme.TextPrimary
                    : (_state == StepNavState.Completed ? UiTheme.TextSecondary : UiTheme.TextMuted);
                Font f = _state == StepNavState.Active ? UiTheme.FontBodyBold : UiTheme.FontBody;
                TextRenderer.DrawText(g, _title, f, new Rectangle(52, 0, Width - 62, Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class SpeakerChoice
        {
            public readonly string Id;
            public readonly string Label;
            public SpeakerChoice(string id, string label) { Id = id; Label = label; }
            public override string ToString() { return Label; }
        }
    }
}
