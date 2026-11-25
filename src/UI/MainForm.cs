using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using UltimateAlarm.Core;

namespace UltimateAlarm.UI
{
    internal sealed class MainForm : DarkChromeForm
    {
        private const int SidebarWidth = 218;

        private readonly AlarmStore _offlineStore = new AlarmStore();
        private readonly Action _releaseMutex;
        private readonly bool _startMinimized;

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Icon _normalIcon;
        private Icon _ringingIcon;
        private EventWaitHandle _showUiEvent;
        private System.Windows.Forms.Timer _pollTimer;
        private int _diagCounter;

        private bool _allowCloseExit;
        private bool _suppressEvents;
        private ServiceStatusResponse _lastStatus;
        private WakeDiagnostics _cachedDiag;
        private string _lastAlarmsSignature = "";
        private string _lastDevicesSignature = "";
        private string _activeChallengeOccurrence = "";
        private RingChallengeForm _challengeForm;

        // Integrated Title Bar indicators
        private StatusPill _pillService;
        private StatusPill _pillWake;
        private StatusPill _pillSpeaker;
        private Panel _ringAlertStrip;
        private Label _lblRingAlert;

        // Sidebar & Navigation
        private Panel _sidebarPanel;
        private Panel _themePanel;
        private Label _lblThemeHeading;
        private ThemeOptionButton[] _themeButtons;
        private Panel _sidebarFooter;
        private CardPanel _healthCard;
        private Label _lblWakeHint;
        private Panel _mainHost;
        private NavButton[] _navButtons;
        private Panel[] _pages;
        private Label _lblSidebarHealth;
        private Label _lblSidebarContext;
        private Panel _sidebarDotService;
        private Panel _sidebarDotWake;

        // Alarms page
        private Panel _heroActions;
        private Label _lblHeroClock;
        private Label _lblHeroDate;
        private Label _lblNextAlarm;
        private ModernButton _btnHeroStop;
        private Label _lblAlarmsSectionTitle;
        private Panel _alarmsListHost;

        // Audio page
        private CardPanel _audioConfigCard;
        private ModernComboBox _cmbPinnedSpeaker;
        private ModernButton _btnSaveSpeaker;
        private ModernSlider _trkDefaultVolume;
        private ModernNumeric _numDefaultVolume;
        private ModernButton _btnSaveVolume;
        private ModernCheckBox _chkRingAllOutputs;
        private ModernCheckBox _chkHdmiFallback;
        private ModernCheckBox _chkHeadphoneFallback;
        private Panel _audioDivider;
        private ModernButton _btnTestAudioPage;
        private ModernButton _btnStopTestAudio;
        private Label _lblTestTone;
        private ModernComboBox _cmbTestTone;
        private Label _lblDevicesSection;
        private Panel _devicesListHost;

        // Power & Security page
        private Panel _powerGridHost;
        private CardPanel _cardSvc;
        private CardPanel _cardWake;
        private CardPanel _cardPower;
        private CardPanel _cardBackup;
        private CardPanel _cardSecurity;
        private CardPanel _cardBios;
        private Label _lblSvcCard;
        private Label _lblWakeCard;
        private Label _lblPowerCard;
        private Label _lblBackupCard;
        private Label _lblSecuritySummary;
        private ModernCheckBox _chkTaskBackup;
        private ModernCheckBox _chkAutoLockOnBoot;
        private ModernButton _btnOpenWizardPow;
        private ModernButton _btnOpenNetplwiz;
        private ModernTextBox _txtBiosGuide;

        // Logs page
        private ModernTextBox _txtLogs;
        private ModernButton _btnLogRefresh;
        private ModernButton _btnLogFolder;
        private ModernButton _btnLogUninstall;

        public MainForm(bool startMinimized, Action releaseMutex)
            : base(true, true, true, true)
        {
            _startMinimized = startMinimized;
            _releaseMutex = releaseMutex;

            AlarmConfig bootCfg = _offlineStore.LoadConfig();
            UiTheme.ApplyTheme(bootCfg != null ? bootCfg.UiTheme : "Default");
            ApplyCurrentTheme();

            Text = "UltimateAlarm - Bulletproof Windows Alarm";
            Size = new Size(1080, 720);
            MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterScreen;

            _normalIcon = UiTheme.CreateAppIcon(false, 32);
            _ringingIcon = UiTheme.CreateAppIcon(true, 32);
            Icon = _normalIcon;
            RefreshChromeIcon(_normalIcon);

            BuildUi();
            BuildTray();
            SwitchPage(0);

            try
            {
                bool createdShow;
                _showUiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\UltimateAlarmShowUiEvent", out createdShow);
            }
            catch { }

            _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _pollTimer.Tick += delegate
            {
                if (_showUiEvent != null && _showUiEvent.WaitOne(0))
                {
                    ShowAndFocus();
                }
                UpdateClockAndCountdown();
                _diagCounter++;
                RefreshFromService(_diagCounter % 60 == 0);
            };
            _pollTimer.Start();

            RefreshFromService(true);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startMinimized && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowCloseExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }
            if (_showUiEvent != null)
            {
                try { _showUiEvent.Close(); } catch { }
                _showUiEvent = null;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            if (_normalIcon != null) { try { _normalIcon.Dispose(); } catch { } _normalIcon = null; }
            if (_ringingIcon != null) { try { _ringingIcon.Dispose(); } catch { } _ringingIcon = null; }
            base.OnFormClosing(e);
        }

        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Open UltimateAlarm", null, delegate { ShowAndFocus(); });
            _trayMenu.Items.Add("Run Setup Wizard", null, delegate { OpenSetupWizard(); });
            _trayMenu.Items.Add("Test Speaker (10s @ Target Volume)", null, delegate { TriggerTestRing(); });
            _trayMenu.Items.Add("Dismiss Active Alarm", null, delegate { SendSimpleCommand("DISMISS"); });
            _trayMenu.Items.Add("-");
            _trayMenu.Items.Add("Exit Tray UI (Service keeps running)", null, delegate
            {
                _allowCloseExit = true;
                Close();
            });

            _trayIcon = new NotifyIcon
            {
                Icon = _normalIcon,
                Text = "UltimateAlarm",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += delegate { ShowAndFocus(); };
        }

        private void ShowAndFocus()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            RefreshFromService(true);
        }

        private void BuildUi()
        {
            // Match top title strip seamlessly with sidebar on left and body canvas on right (no white bar!)
            SetLeftTitleSidebarMatch(SidebarWidth, UiTheme.BgShell, true);
            PinVisibleTitleText("UltimateAlarm");

            // Quiet compact status pills nestled inside the integrated title bar (same color as body)
            _pillService = new StatusPill
            {
                Location = new Point(SidebarWidth + 24, 8),
                Width = 132,
                Compact = true,
                Text = "Service ...",
                AccentColor = UiTheme.TextMuted,
                Cursor = Cursors.Hand
            };
            _pillService.Click += delegate { SwitchPage(2); };

            _pillWake = new StatusPill
            {
                Location = new Point(SidebarWidth + 164, 8),
                Width = 148,
                Compact = true,
                Text = "Wake Timers ...",
                AccentColor = UiTheme.TextMuted,
                Cursor = Cursors.Hand
            };
            _pillWake.Click += delegate { SwitchPage(2); };

            _pillSpeaker = new StatusPill
            {
                Location = new Point(SidebarWidth + 320, 8),
                Width = 220,
                Compact = true,
                Text = "Audio: Detecting...",
                AccentColor = UiTheme.TextMuted,
                Cursor = Cursors.Hand
            };
            _pillSpeaker.Click += delegate { SwitchPage(1); };

            ChromeTitleBar.Controls.Add(_pillService);
            ChromeTitleBar.Controls.Add(_pillWake);
            ChromeTitleBar.Controls.Add(_pillSpeaker);
            ChromeTitleBar.Resize += delegate
            {
                if (_pillSpeaker != null)
                {
                    int avail = ChromeTitleBar.Width - 155 - _pillSpeaker.Left;
                    _pillSpeaker.Width = Math.Max(120, Math.Min(260, avail));
                }
            };

            // Active Ring Alert Strip (hidden unless an alarm is ringing)
            _ringAlertStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = UiTheme.DangerDark,
                Visible = false,
                Padding = new Padding(20, 7, 20, 7)
            };
            _ringAlertStrip.Paint += delegate(object s, PaintEventArgs e)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.DangerRed))
                {
                    e.Graphics.FillRectangle(br, 0, 0, 4, _ringAlertStrip.Height);
                }
                using (Pen p = new Pen(Color.FromArgb(120, 45, 52), 1f))
                {
                    e.Graphics.DrawLine(p, 0, _ringAlertStrip.Height - 1, _ringAlertStrip.Width, _ringAlertStrip.Height - 1);
                }
            };
            _lblRingAlert = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.FontSubTitle,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "ALARM RINGING NOW"
            };
            ModernButton btnOpenChallenge = new ModernButton
            {
                Dock = DockStyle.Right,
                Width = 190,
                Text = "Open Dismiss Challenge",
                Tone = ButtonTone.Danger
            };
            btnOpenChallenge.Click += delegate { OpenOrFocusChallenge(); };
            _ringAlertStrip.Controls.Add(_lblRingAlert);
            _ringAlertStrip.Controls.Add(btnOpenChallenge);

            // Left Navigation Sidebar
            _sidebarPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = SidebarWidth,
                BackColor = UiTheme.BgShell,
                Padding = new Padding(0, 12, 0, 0)
            };
            _sidebarPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, _sidebarPanel.Width - 1, 0, _sidebarPanel.Width - 1, _sidebarPanel.Height);
                }
            };

            string[] navTitles = new string[] { "Alarms", "Audio & Routing", "Power & Security", "System Logs" };
            UiGlyph[] navGlyphs = new UiGlyph[] { UiGlyph.Alarm, UiGlyph.Speaker, UiGlyph.Shield, UiGlyph.Logs };
            _navButtons = new NavButton[navTitles.Length];
            for (int i = 0; i < navTitles.Length; i++)
            {
                int pageIdx = i;
                NavButton nb = new NavButton
                {
                    Text = navTitles[i],
                    Glyph = navGlyphs[i],
                    Location = new Point(0, 14 + i * 42),
                    Width = SidebarWidth - 1
                };
                nb.Click += delegate { SwitchPage(pageIdx); };
                _navButtons[i] = nb;
                _sidebarPanel.Controls.Add(nb);
            }

            // Theme Switcher Section (live 1-click theme selection)
            _themePanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 148,
                BackColor = UiTheme.BgShell,
                Padding = new Padding(12, 8, 13, 6)
            };
            _themePanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, 14, 0, _themePanel.Width - 15, 0);
                }
            };

            _lblThemeHeading = new Label
            {
                Text = "THEME",
                Font = UiTheme.FontSmallBold,
                ForeColor = UiTheme.TextMuted,
                BackColor = UiTheme.BgShell,
                Location = new Point(14, 8),
                AutoSize = true
            };
            _themePanel.Controls.Add(_lblThemeHeading);

            _themeButtons = new ThemeOptionButton[UiTheme.Themes.Length];
            for (int i = 0; i < UiTheme.Themes.Length; i++)
            {
                ThemePresetInfo preset = UiTheme.Themes[i];
                ThemeOptionButton tb = new ThemeOptionButton(preset)
                {
                    Location = new Point(12, 26 + i * 29),
                    Size = new Size(SidebarWidth - 26, 27),
                    Active = string.Equals(UiTheme.CurrentThemeId, preset.Id, StringComparison.OrdinalIgnoreCase)
                };
                tb.Click += delegate { SwitchTheme(preset.Id, true); };
                _themeButtons[i] = tb;
                _themePanel.Controls.Add(tb);
            }

            // Bottom Sidebar Footer: Health summary + Setup Wizard
            _sidebarFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 144,
                BackColor = UiTheme.BgShell,
                Padding = new Padding(12, 10, 13, 14)
            };
            _sidebarFooter.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, 14, 0, _sidebarFooter.Width - 15, 0);
                }
            };

            Color healthBg = UiTheme.BgHealthCard;
            _healthCard = new CardPanel
            {
                Location = new Point(12, 12),
                Size = new Size(SidebarWidth - 26, 76),
                CardColor = healthBg,
                BorderColor = UiTheme.BorderSubtle,
                CornerRadius = 7
            };

            _sidebarDotService = CreateStatusDot(12, 16, UiTheme.TextMuted, healthBg);
            _lblSidebarHealth = new Label
            {
                Location = new Point(25, 11),
                Size = new Size(156, 17),
                Font = UiTheme.FontSmallBold,
                ForeColor = UiTheme.TextPrimary,
                BackColor = healthBg,
                UseMnemonic = false,
                Text = "Checking service..."
            };

            _sidebarDotWake = CreateStatusDot(12, 36, UiTheme.TextMuted, healthBg);
            _lblWakeHint = new Label
            {
                Location = new Point(25, 31),
                Size = new Size(156, 17),
                Font = UiTheme.FontSmall,
                ForeColor = UiTheme.TextSecondary,
                BackColor = healthBg,
                UseMnemonic = false,
                Text = "Wake & guard active"
            };

            _lblSidebarContext = new Label
            {
                Location = new Point(12, 52),
                Size = new Size(168, 16),
                Font = new Font("Segoe UI", 8f),
                ForeColor = UiTheme.TextMuted,
                BackColor = healthBg,
                UseMnemonic = false,
                Text = "LocalSystem"
            };
            _healthCard.Controls.AddRange(new Control[] { _sidebarDotService, _lblSidebarHealth, _sidebarDotWake, _lblWakeHint, _lblSidebarContext });

            ModernButton btnWizard = new ModernButton
            {
                Text = "Setup Wizard",
                Glyph = UiGlyph.Sliders,
                Tone = ButtonTone.Secondary,
                Location = new Point(12, 98),
                Size = new Size(SidebarWidth - 26, 32)
            };
            btnWizard.Click += delegate { OpenSetupWizard(); };

            _sidebarFooter.Controls.AddRange(new Control[] { _healthCard, btnWizard });
            _sidebarPanel.Controls.Add(_themePanel);
            _sidebarPanel.Controls.Add(_sidebarFooter);

            // Main Content Host
            _mainHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.BgCanvas,
                Padding = new Padding(26, 14, 26, 20)
            };

            _pages = new Panel[]
            {
                BuildAlarmsPage(),
                BuildAudioPage(),
                BuildPowerPage(),
                BuildLogsPage()
            };
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Dock = DockStyle.Fill;
                _pages[i].Visible = false;
                _mainHost.Controls.Add(_pages[i]);
            }

            BodyHost.Controls.Add(_mainHost);
            BodyHost.Controls.Add(_ringAlertStrip);
            BodyHost.Controls.Add(_sidebarPanel);
        }

        protected override bool IsInteractiveTitleControl(Control c)
        {
            if (c is StatusPill) return true;
            return base.IsInteractiveTitleControl(c);
        }

        private Panel CreateStatusDot(int x, int y, Color col, Color bgCol)
        {
            Panel dot = new Panel { Location = new Point(x, y), Size = new Size(7, 7), BackColor = bgCol, Tag = col };
            dot.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color c = (dot.Tag is Color) ? (Color)dot.Tag : UiTheme.TextMuted;
                using (SolidBrush br = new SolidBrush(c))
                {
                    e.Graphics.FillEllipse(br, 0, 0, 6, 6);
                }
            };
            return dot;
        }

        private Panel BuildAlarmsPage()
        {
            Panel page = new Panel { BackColor = UiTheme.BgCanvas };

            // Hero Summary Card
            CardPanel heroCard = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = 104,
                Padding = new Padding(22, 16, 20, 16)
            };

            _lblHeroClock = new Label
            {
                Location = new Point(22, 16),
                Size = new Size(154, 38),
                Font = UiTheme.FontClockHero,
                ForeColor = UiTheme.TextPrimary,
                UseMnemonic = false,
                Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            };

            _lblHeroDate = new Label
            {
                Location = new Point(182, 28),
                Size = new Size(176, 22),
                Font = UiTheme.FontSubTitle,
                ForeColor = UiTheme.TextSecondary,
                UseMnemonic = false,
                Text = DateTime.Now.ToString("dddd, MMM dd", CultureInfo.InvariantCulture)
            };

            _lblNextAlarm = new Label
            {
                Location = new Point(24, 64),
                Size = new Size(340, 22),
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.AccentPrimary,
                AutoEllipsis = true,
                UseMnemonic = false,
                Text = "Checking schedule..."
            };

            _heroActions = new Panel
            {
                Dock = DockStyle.Right,
                Width = 386,
                BackColor = UiTheme.BgSurface,
                Tag = "SURFACE"
            };

            ModernButton btnAdd = new ModernButton
            {
                Text = "New Alarm",
                Glyph = UiGlyph.Plus,
                Tone = ButtonTone.Primary,
                Size = new Size(118, 34),
                Location = new Point(0, 18)
            };
            btnAdd.Click += delegate { AddNewAlarm(); };

            ModernButton btnTest = new ModernButton
            {
                Text = "Test Ring",
                Glyph = UiGlyph.Play,
                Tone = ButtonTone.Secondary,
                Size = new Size(110, 34),
                Location = new Point(126, 18)
            };
            btnTest.Click += delegate { TriggerTestRing(); };

            _btnHeroStop = new ModernButton
            {
                Text = "Stop / Dismiss",
                Glyph = UiGlyph.Stop,
                Tone = ButtonTone.Secondary,
                Size = new Size(138, 34),
                Location = new Point(244, 18)
            };
            heroCard.Resize += delegate
            {
                int maxW = Math.Max(200, heroCard.ClientSize.Width - _heroActions.Width - 54);
                _lblNextAlarm.Width = maxW;
            };
            _btnHeroStop.Click += delegate
            {
                if (_lastStatus != null && _lastStatus.ActiveRing != null && !_lastStatus.ActiveRing.IsTest)
                {
                    OpenOrFocusChallenge();
                }
                else
                {
                    SendSimpleCommand("DISMISS");
                }
            };
            _heroActions.Controls.AddRange(new Control[] { btnAdd, btnTest, _btnHeroStop });

            heroCard.Controls.AddRange(new Control[] { _lblHeroClock, _lblHeroDate, _lblNextAlarm, _heroActions });

            // Section Heading Bar
            Panel secBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.BgCanvas };
            _lblAlarmsSectionTitle = new Label
            {
                Text = "ARMED ALARMS",
                Font = UiTheme.FontSmallBold,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(2, 15),
                AutoSize = true
            };
            secBar.Controls.Add(_lblAlarmsSectionTitle);

            _alarmsListHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.BgCanvas };
            UiTheme.ApplyDarkScrollbars(_alarmsListHost);
            _alarmsListHost.Resize += delegate { ResizeCards(_alarmsListHost); };

            page.Controls.Add(_alarmsListHost);
            page.Controls.Add(secBar);
            page.Controls.Add(heroCard);
            return page;
        }

        private Panel BuildAudioPage()
        {
            Panel page = new Panel { BackColor = UiTheme.BgCanvas };

            // Clean Page Header
            Panel header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = UiTheme.BgCanvas };
            Label lblTitle = new Label
            {
                Text = "Audio & Speaker Routing",
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(0, 0),
                UseMnemonic = false,
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = "Select your physical alarm speaker, enforce a minimum volume floor, and manage headphone protection.",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(0, 28),
                UseMnemonic = false,
                AutoSize = true
            };
            ModernButton btnRefreshDevs = new ModernButton
            {
                Text = "Refresh",
                Glyph = UiGlyph.Refresh,
                Tone = ButtonTone.Secondary,
                Size = new Size(102, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(header.Width - 102, 6)
            };
            btnRefreshDevs.Click += delegate { RefreshFromService(true); };
            header.Resize += delegate { btnRefreshDevs.Left = header.Width - btnRefreshDevs.Width; };
            header.Controls.AddRange(new Control[] { lblTitle, lblSub, btnRefreshDevs });

            // Aligned Settings Card
            _audioConfigCard = new CardPanel { Dock = DockStyle.Top, Height = 258 };

            Label lblPin = new Label { Text = "Target Speaker", Location = new Point(20, 23), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold };
            _cmbPinnedSpeaker = new ModernComboBox { Location = new Point(168, 17), Width = 480 };
            _btnSaveSpeaker = new ModernButton { Text = "Save", Tone = ButtonTone.Primary, Size = new Size(88, 32) };
            _btnSaveSpeaker.Click += delegate { SavePinnedSpeaker(); };

            Label lblVol = new Label { Text = "Alarm Volume", Location = new Point(20, 65), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold };
            _trkDefaultVolume = new ModernSlider
            {
                Location = new Point(162, 60),
                Width = 370,
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Value = AudioRouter.DefaultVolumePercent
            };
            _numDefaultVolume = new ModernNumeric
            {
                Size = new Size(92, 32),
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Suffix = "%",
                Value = AudioRouter.DefaultVolumePercent
            };
            _btnSaveVolume = new ModernButton { Text = "Save", Tone = ButtonTone.Primary, Size = new Size(88, 32) };
            _btnSaveVolume.Click += delegate { SaveDefaultVolume(); };

            _trkDefaultVolume.Scroll += delegate { _numDefaultVolume.Value = _trkDefaultVolume.Value; };
            _numDefaultVolume.ValueChanged += delegate
            {
                int v = _numDefaultVolume.Value;
                if (_trkDefaultVolume.Value != v) _trkDefaultVolume.Value = v;
                _btnTestAudioPage.Text = (_chkRingAllOutputs != null && _chkRingAllOutputs.Checked ? "Test All Outputs @ " : "Test Output @ ") + v + "%";
            };

            _audioDivider = new Panel { Location = new Point(20, 108), Height = 1, Width = 720, BackColor = UiTheme.BorderSubtle };

            _chkRingAllOutputs = new ModernCheckBox
            {
                Text = "Ring all connected output devices simultaneously (Speakers, HDMI, USB)",
                Location = new Point(20, 120),
                Size = new Size(680, 26),
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary
            };
            _chkRingAllOutputs.CheckedChanged += delegate { OnMainRingAllChanged(); };

            _chkHdmiFallback = new ModernCheckBox
            {
                Text = "Allow Monitor HDMI fallback if physical Speakers are missing",
                Location = new Point(20, 148),
                Size = new Size(680, 26),
                ForeColor = UiTheme.TextSecondary
            };
            _chkHdmiFallback.CheckedChanged += delegate { SavePolicyFlag("HDMI", _chkHdmiFallback.Checked); };

            _chkHeadphoneFallback = new ModernCheckBox
            {
                Text = "Also ring plugged-in Headphones / Headsets at alarm volume",
                BadgeText = "HEADPHONE OVERRIDE",
                BadgeColor = UiTheme.WarnAmber,
                Location = new Point(20, 176),
                Size = new Size(680, 26),
                ForeColor = UiTheme.TextSecondary
            };
            _chkHeadphoneFallback.CheckedChanged += delegate { SavePolicyFlag("HEADPHONES", _chkHeadphoneFallback.Checked); };

            _btnTestAudioPage = new ModernButton
            {
                Text = "Test All Outputs @ 80%",
                Glyph = UiGlyph.Play,
                Tone = ButtonTone.Success,
                Location = new Point(20, 212),
                Size = new Size(204, 32)
            };
            _btnTestAudioPage.Click += delegate { TriggerTestRing(); };

            _btnStopTestAudio = new ModernButton
            {
                Text = "Stop Test",
                Glyph = UiGlyph.Stop,
                Tone = ButtonTone.Secondary,
                Location = new Point(234, 212),
                Size = new Size(104, 32)
            };
            _btnStopTestAudio.Click += delegate { SendSimpleCommand("DISMISS"); };

            _lblTestTone = new Label
            {
                Text = "Ringtone",
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(352, 219),
                AutoSize = true,
                UseMnemonic = false
            };

            _cmbTestTone = new ModernComboBox
            {
                Location = new Point(418, 212),
                Width = 280
            };
            for (int i = 0; i < AudioRouter.BuiltInPresets.Length; i++)
            {
                _cmbTestTone.AddItem(AudioRouter.BuiltInPresets[i]);
            }
            _cmbTestTone.SelectedIndex = 0;

            _audioConfigCard.Controls.AddRange(new Control[]
            {
                lblPin, _cmbPinnedSpeaker, _btnSaveSpeaker,
                lblVol, _trkDefaultVolume, _numDefaultVolume, _btnSaveVolume,
                _audioDivider,
                _chkRingAllOutputs, _chkHdmiFallback, _chkHeadphoneFallback,
                _btnTestAudioPage, _btnStopTestAudio, _lblTestTone, _cmbTestTone
            });
            _audioConfigCard.Resize += delegate { LayoutAudioConfigCard(); };
            LayoutAudioConfigCard();

            Panel devSecBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = UiTheme.BgCanvas };
            _lblDevicesSection = new Label
            {
                Text = "DETECTED OUTPUT DEVICES",
                Font = UiTheme.FontSmallBold,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(2, 14),
                AutoSize = true
            };
            devSecBar.Controls.Add(_lblDevicesSection);

            _devicesListHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.BgCanvas };
            UiTheme.ApplyDarkScrollbars(_devicesListHost);
            _devicesListHost.Resize += delegate { ResizeCards(_devicesListHost); };

            page.Controls.Add(_devicesListHost);
            page.Controls.Add(devSecBar);
            page.Controls.Add(_audioConfigCard);
            page.Controls.Add(header);
            return page;
        }

        private void LayoutAudioConfigCard()
        {
            if (_audioConfigCard == null) return;
            int w = Math.Max(560, _audioConfigCard.ClientSize.Width);
            int rightEdge = w - 20;
            int saveX = rightEdge - 88;
            int inputX = 168;

            _btnSaveSpeaker.Location = new Point(saveX, 17);
            _cmbPinnedSpeaker.Location = new Point(inputX, 17);
            _cmbPinnedSpeaker.Width = Math.Max(180, saveX - inputX - 10);

            _btnSaveVolume.Location = new Point(saveX, 58);
            _numDefaultVolume.Location = new Point(saveX - _numDefaultVolume.Width - 10, 58);
            _trkDefaultVolume.Location = new Point(inputX - 6, 60);
            _trkDefaultVolume.Width = Math.Max(120, _numDefaultVolume.Left - _trkDefaultVolume.Left - 12);

            _audioDivider.Width = Math.Max(200, rightEdge - 20);
            int chkW = Math.Max(260, rightEdge - 20);
            _chkRingAllOutputs.Width = chkW;
            _chkHdmiFallback.Width = chkW;
            _chkHeadphoneFallback.Width = chkW;

            if (_cmbTestTone != null && _lblTestTone != null)
            {
                int toneLabelX = 352;
                int toneComboX = 418;
                _lblTestTone.Location = new Point(toneLabelX, 219);
                _cmbTestTone.Location = new Point(toneComboX, 212);
                _cmbTestTone.Width = Math.Max(140, rightEdge - toneComboX);
            }
        }

        private Panel BuildPowerPage()
        {
            Panel page = new Panel { BackColor = UiTheme.BgCanvas };

            Panel header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = UiTheme.BgCanvas };
            Label lblTitle = new Label
            {
                Text = "Power, Wake Timers & Boot Security",
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(0, 0),
                UseMnemonic = false,
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = "Verify Windows Service health, hardware sleep states, RTC wake timers, and cold-boot BIOS settings.",
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(0, 28),
                UseMnemonic = false,
                AutoSize = true
            };
            header.Controls.AddRange(new Control[] { lblTitle, lblSub });

            _powerGridHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.BgCanvas };
            UiTheme.ApplyDarkScrollbars(_powerGridHost);
            _powerGridHost.Resize += delegate { LayoutPowerPage(); };

            _cardSvc = new CardPanel();
            Label h1 = new Label { Text = "Windows Service Engine", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), UseMnemonic = false, AutoSize = true };
            _lblSvcCard = new Label { Location = new Point(16, 38), Size = new Size(340, 42), ForeColor = UiTheme.TextSecondary, UseMnemonic = false, Text = "Checking..." };
            ModernButton btnInstallSvc = new ModernButton { Text = "Install / Repair Service", Tone = ButtonTone.Primary, Location = new Point(16, 90), Size = new Size(176, 30) };
            btnInstallSvc.Click += delegate { RunRepairServiceAction(); };
            _cardSvc.Controls.AddRange(new Control[] { h1, _lblSvcCard, btnInstallSvc });

            _cardWake = new CardPanel();
            Label h2 = new Label { Text = "Power Plans, Wake Timers & Audio Idle", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), UseMnemonic = false, AutoSize = true };
            _lblWakeCard = new Label { Location = new Point(16, 38), Size = new Size(340, 46), ForeColor = UiTheme.TextSecondary, UseMnemonic = false, Text = "Checking..." };
            ModernButton btnFixWake = new ModernButton { Text = "Harden Power & Wake", Tone = ButtonTone.Success, Location = new Point(16, 90), Size = new Size(176, 30) };
            btnFixWake.Click += delegate { RunFixWakeAction(); };
            _cardWake.Controls.AddRange(new Control[] { h2, _lblWakeCard, btnFixWake });

            _cardPower = new CardPanel();
            Label h3 = new Label { Text = "Hardware Sleep & Screen Saver", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), UseMnemonic = false, AutoSize = true };
            _lblPowerCard = new Label { Location = new Point(16, 38), Size = new Size(340, 52), ForeColor = UiTheme.TextSecondary, UseMnemonic = false, Text = "Checking..." };
            _cardPower.Controls.AddRange(new Control[] { h3, _lblPowerCard });

            _cardBackup = new CardPanel();
            Label h4 = new Label { Text = "Redundancy & Startup Guard", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), UseMnemonic = false, AutoSize = true };
            _lblBackupCard = new Label { Location = new Point(16, 38), Size = new Size(340, 22), ForeColor = UiTheme.TextSecondary, UseMnemonic = false, Text = "Checking..." };
            _chkTaskBackup = new ModernCheckBox { Text = "Enable WakeToRun Task Scheduler backup", Location = new Point(16, 66), Size = new Size(340, 24), ForeColor = UiTheme.TextPrimary };
            _chkTaskBackup.CheckedChanged += delegate { SavePolicyFlag("TASKBACKUP", _chkTaskBackup.Checked); };
            _cardBackup.Controls.AddRange(new Control[] { h4, _lblBackupCard, _chkTaskBackup });

            _cardSecurity = new CardPanel();
            Label h5 = new Label { Text = "Boot Sign-In & Auto-Lock Security", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), UseMnemonic = false, AutoSize = true };
            _lblSecuritySummary = new Label { Location = new Point(16, 38), Size = new Size(700, 24), ForeColor = UiTheme.TextSecondary, UseMnemonic = false, Text = "Checking..." };
            _chkAutoLockOnBoot = new ModernCheckBox { Text = "Auto-lock screen immediately after boot (when Auto-Logon is active)", Location = new Point(16, 74), Size = new Size(440, 26), ForeColor = UiTheme.TextPrimary };
            _chkAutoLockOnBoot.CheckedChanged += delegate { SavePolicyFlag("AUTOLOCK", _chkAutoLockOnBoot.Checked); };

            _btnOpenWizardPow = new ModernButton { Text = "Setup Wizard", Glyph = UiGlyph.Sliders, Tone = ButtonTone.Secondary, Size = new Size(128, 30) };
            _btnOpenWizardPow.Click += delegate { OpenSetupWizard(); };

            _btnOpenNetplwiz = new ModernButton { Text = "Windows Accounts", Tone = ButtonTone.Secondary, Size = new Size(142, 30) };
            _btnOpenNetplwiz.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo { FileName = SecurityHelper.ResolveSystemTool("netplwiz.exe"), UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "UltimateAlarm", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            _cardSecurity.Controls.AddRange(new Control[] { h5, _lblSecuritySummary, _chkAutoLockOnBoot, _btnOpenWizardPow, _btnOpenNetplwiz });

            _cardBios = new CardPanel();
            Label h6 = new Label { Text = "Cold Shutdown (S5) BIOS / UEFI Power-On Guide", Font = UiTheme.FontSubTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(16, 12), AutoSize = true };
            _txtBiosGuide = new ModernTextBox
            {
                Location = new Point(16, 40),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = UiTheme.FontMono
            };
            _cardBios.Controls.AddRange(new Control[] { h6, _txtBiosGuide });

            _powerGridHost.Controls.AddRange(new Control[] { _cardSvc, _cardWake, _cardPower, _cardBackup, _cardSecurity, _cardBios });

            page.Controls.Add(_powerGridHost);
            page.Controls.Add(header);
            LayoutPowerPage();
            return page;
        }

        private void LayoutPowerPage()
        {
            if (_powerGridHost == null) return;
            int scrollPad = _powerGridHost.VerticalScroll.Visible ? 18 : 2;
            int w = Math.Max(600, _powerGridHost.ClientSize.Width - scrollPad);
            int gap = 12;
            int half = (w - gap) / 2;

            _cardSvc.Bounds = new Rectangle(0, 4, half, 130);
            _lblSvcCard.Width = half - 32;

            _cardWake.Bounds = new Rectangle(half + gap, 4, w - half - gap, 130);
            _lblWakeCard.Width = _cardWake.Width - 32;

            _cardPower.Bounds = new Rectangle(0, 144, half, 98);
            _lblPowerCard.Width = half - 32;

            _cardBackup.Bounds = new Rectangle(half + gap, 144, w - half - gap, 98);
            _lblBackupCard.Width = _cardBackup.Width - 32;
            _chkTaskBackup.Width = _cardBackup.Width - 32;

            _cardSecurity.Bounds = new Rectangle(0, 252, w, 108);
            _lblSecuritySummary.Width = w - 32;
            _btnOpenNetplwiz.Location = new Point(w - _btnOpenNetplwiz.Width - 16, 68);
            _btnOpenWizardPow.Location = new Point(_btnOpenNetplwiz.Left - _btnOpenWizardPow.Width - 8, 68);
            _chkAutoLockOnBoot.Width = Math.Max(200, _btnOpenWizardPow.Left - 24);

            int biosTop = 370;
            int biosH = Math.Max(160, _powerGridHost.ClientSize.Height - biosTop - 6);
            _cardBios.Bounds = new Rectangle(0, biosTop, w, biosH);
            _txtBiosGuide.Bounds = new Rectangle(16, 40, w - 32, biosH - 54);
        }

        private Panel BuildLogsPage()
        {
            Panel page = new Panel { BackColor = UiTheme.BgCanvas };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = UiTheme.BgCanvas };

            Label lbl = new Label { Text = "System & Audio Logs", Font = UiTheme.FontTitle, ForeColor = UiTheme.TextPrimary, Location = new Point(0, 0), UseMnemonic = false, AutoSize = true };
            Label sub = new Label { Text = "Service wake events, audio routing, and trigger history.", Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Location = new Point(0, 29), UseMnemonic = false, AutoSize = true };

            _btnLogRefresh = new ModernButton { Text = "Refresh", Glyph = UiGlyph.Refresh, Tone = ButtonTone.Secondary, Size = new Size(102, 32) };
            _btnLogRefresh.Click += delegate { LoadLogsTail(); };

            _btnLogFolder = new ModernButton { Text = "App Folder", Glyph = UiGlyph.Folder, Tone = ButtonTone.Secondary, Size = new Size(116, 32) };
            _btnLogFolder.Click += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SecurityHelper.ResolveSystemTool("explorer.exe"),
                        Arguments = "\"" + AppPaths.RootDir + "\"",
                        UseShellExecute = false
                    });
                }
                catch { }
            };

            _btnLogUninstall = new ModernButton { Text = "Uninstall Service", Glyph = UiGlyph.Trash, Tone = ButtonTone.DangerGhost, Size = new Size(146, 32) };
            _btnLogUninstall.Click += delegate { RunUninstallAction(); };

            header.Resize += delegate
            {
                int r = header.Width;
                _btnLogUninstall.Location = new Point(r - _btnLogUninstall.Width, 6);
                _btnLogFolder.Location = new Point(_btnLogUninstall.Left - _btnLogFolder.Width - 8, 6);
                _btnLogRefresh.Location = new Point(_btnLogFolder.Left - _btnLogRefresh.Width - 8, 6);
            };
            header.Controls.AddRange(new Control[] { lbl, sub, _btnLogRefresh, _btnLogFolder, _btnLogUninstall });

            _txtLogs = new ModernTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = UiTheme.FontMono
            };

            page.Controls.Add(_txtLogs);
            page.Controls.Add(header);
            return page;
        }

        private void SwitchPage(int idx)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = (i == idx);
                _navButtons[i].Active = (i == idx);
            }
            if (idx == 2) LayoutPowerPage();
            if (idx == 3) LoadLogsTail();
            if (idx == 1 || idx == 2) RefreshFromService(true);
        }

        public void SwitchTheme(string themeId, bool persist)
        {
            string normalized = UiTheme.NormalizeThemeId(themeId);
            UiTheme.ApplyTheme(normalized);

            Icon oldNormal = _normalIcon;
            Icon oldRing = _ringingIcon;
            _normalIcon = UiTheme.CreateAppIcon(false, 32);
            _ringingIcon = UiTheme.CreateAppIcon(true, 32);
            Icon = _normalIcon;
            RefreshChromeIcon(_normalIcon);
            if (_trayIcon != null)
            {
                bool ringing = _lastStatus != null && _lastStatus.ActiveRing != null;
                _trayIcon.Icon = ringing ? _ringingIcon : _normalIcon;
            }
            if (oldNormal != null) try { oldNormal.Dispose(); } catch { }
            if (oldRing != null) try { oldRing.Dispose(); } catch { }

            ApplyCurrentTheme();
            if (_sidebarPanel != null) _sidebarPanel.BackColor = UiTheme.BgShell;
            if (_themePanel != null) _themePanel.BackColor = UiTheme.BgShell;
            if (_lblThemeHeading != null)
            {
                _lblThemeHeading.BackColor = UiTheme.BgShell;
                _lblThemeHeading.ForeColor = UiTheme.TextMuted;
            }
            if (_sidebarFooter != null) _sidebarFooter.BackColor = UiTheme.BgShell;
            if (_healthCard != null)
            {
                _healthCard.CardColor = UiTheme.BgHealthCard;
                _healthCard.BorderColor = UiTheme.BorderSubtle;
                if (_sidebarDotService != null) _sidebarDotService.BackColor = UiTheme.BgHealthCard;
                if (_sidebarDotWake != null) _sidebarDotWake.BackColor = UiTheme.BgHealthCard;
                if (_lblSidebarHealth != null) { _lblSidebarHealth.BackColor = UiTheme.BgHealthCard; _lblSidebarHealth.ForeColor = UiTheme.TextPrimary; }
                if (_lblWakeHint != null) { _lblWakeHint.BackColor = UiTheme.BgHealthCard; _lblWakeHint.ForeColor = UiTheme.TextSecondary; }
                if (_lblSidebarContext != null) { _lblSidebarContext.BackColor = UiTheme.BgHealthCard; _lblSidebarContext.ForeColor = UiTheme.TextMuted; }
            }
            if (_ringAlertStrip != null) _ringAlertStrip.BackColor = UiTheme.DangerDark;
            if (_mainHost != null) _mainHost.BackColor = UiTheme.BgCanvas;
            if (_heroActions != null) _heroActions.BackColor = UiTheme.BgSurface;
            if (_audioDivider != null) _audioDivider.BackColor = UiTheme.BorderSubtle;

            if (_themeButtons != null)
            {
                for (int i = 0; i < _themeButtons.Length; i++)
                {
                    _themeButtons[i].Active = string.Equals(_themeButtons[i].Preset.Id, normalized, StringComparison.OrdinalIgnoreCase);
                    _themeButtons[i].Invalidate();
                }
            }

            if (_pages != null)
            {
                for (int i = 0; i < _pages.Length; i++)
                {
                    RefreshThemeTree(_pages[i]);
                }
            }

            // Re-sync dynamic cards & accents
            if (_chkHeadphoneFallback != null) _chkHeadphoneFallback.BadgeColor = UiTheme.WarnAmber;
            _lastAlarmsSignature = "";
            _lastDevicesSignature = "";
            if (_lastStatus != null)
            {
                RefreshFromService(false);
            }
            UpdateClockAndCountdown();
            Invalidate(true);
            Update();

            if (persist)
            {
                SaveUiTheme(normalized);
            }
        }

        private void RefreshThemeTree(Control ctrl)
        {
            if (ctrl == null) return;

            CardPanel card = ctrl as CardPanel;
            if (card != null)
            {
                card.RefreshTheme();
            }
            else if (ctrl is ModernTextBox)
            {
                ((ModernTextBox)ctrl).RefreshTheme();
            }
            else if (ctrl is ModernNumeric)
            {
                ((ModernNumeric)ctrl).RefreshTheme();
            }
            else if (ctrl is Panel && ctrl != _sidebarPanel && ctrl != _themePanel && ctrl != _sidebarFooter && ctrl != _ringAlertStrip)
            {
                string tag = ctrl.Tag as string;
                if (tag == "SURFACE") ctrl.BackColor = UiTheme.BgSurface;
                else if (ctrl == _audioDivider) ctrl.BackColor = UiTheme.BorderSubtle;
                else if (ctrl.Parent is CardPanel) ctrl.BackColor = UiTheme.BgSurface;
                else ctrl.BackColor = UiTheme.BgCanvas;
            }
            else if (ctrl is Label)
            {
                Label lbl = (Label)ctrl;
                if (lbl == _lblHeroDate)
                {
                    lbl.ForeColor = UiTheme.TextSecondary;
                }
                else if (lbl == _lblHeroClock || lbl.Font == UiTheme.FontTitle || lbl.Font == UiTheme.FontSubTitle || lbl.Font == UiTheme.FontDisplay)
                {
                    lbl.ForeColor = UiTheme.TextPrimary;
                }
                else if (lbl == _lblAlarmsSectionTitle || lbl == _lblDevicesSection)
                {
                    lbl.ForeColor = UiTheme.TextMuted;
                }
                else if (lbl != _lblNextAlarm)
                {
                    lbl.ForeColor = UiTheme.TextSecondary;
                }
            }
            else if (ctrl is ModernCheckBox)
            {
                ModernCheckBox cb = (ModernCheckBox)ctrl;
                cb.ForeColor = (cb.Font == UiTheme.FontBodyBold) ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            }

            for (int i = 0; i < ctrl.Controls.Count; i++)
            {
                RefreshThemeTree(ctrl.Controls[i]);
            }
            ctrl.Invalidate();
        }

        private void SaveUiTheme(string themeId)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "SET_UI_THEME", StringParam = themeId }, 1000);
            if (resp == null || !resp.Success || resp.Config == null || !string.Equals(resp.Config.UiTheme, themeId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    AlarmConfig cfg = _offlineStore.LoadConfig();
                    cfg.UiTheme = themeId;
                    _offlineStore.SaveConfig(cfg);
                }
                catch { }
            }
        }

        private void UpdateClockAndCountdown()
        {
            DateTime now = DateTime.Now;
            _lblHeroClock.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _lblHeroDate.Text = now.ToString("dddd, MMM dd", CultureInfo.InvariantCulture);

            if (_lastStatus != null && _lastStatus.Config != null)
            {
                DateTime? earliest = null;
                AlarmItem earliestAlarm = null;
                for (int i = 0; i < _lastStatus.Config.Alarms.Count; i++)
                {
                    DateTime? cand = _lastStatus.Config.Alarms[i].GetNextScheduledTime(now);
                    if (cand.HasValue && (!earliest.HasValue || cand.Value < earliest.Value))
                    {
                        earliest = cand;
                        earliestAlarm = _lastStatus.Config.Alarms[i];
                    }
                }

                if (earliest.HasValue && earliestAlarm != null)
                {
                    TimeSpan diff = earliest.Value - now;
                    if (diff.TotalSeconds < 0) diff = TimeSpan.Zero;
                    string span = diff.TotalDays >= 1
                        ? string.Format("{0}d {1}h {2}m", (int)diff.TotalDays, diff.Hours, diff.Minutes)
                        : string.Format("{0}h {1}m {2}s", (int)diff.TotalHours, diff.Minutes, diff.Seconds);
                    _lblNextAlarm.Text = string.Format("Next: {0:00}:{1:00}  ·  {2}  ·  in {3}",
                        earliestAlarm.Hour, earliestAlarm.Minute, earliestAlarm.Label, span);
                    _lblNextAlarm.ForeColor = UiTheme.AccentPrimary;
                }
                else
                {
                    _lblNextAlarm.Text = "No alarms armed — click New Alarm to schedule one.";
                    _lblNextAlarm.ForeColor = UiTheme.TextMuted;
                }
            }
        }

        private void RefreshFromService(bool includeDiagnostics)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(
                new IpcRequest { Command = includeDiagnostics ? "GET_DIAGNOSTICS" : "GET_STATUS" },
                includeDiagnostics ? 1500 : 700);

            bool online = resp != null && resp.Success;
            if (!online)
            {
                resp = new ServiceStatusResponse
                {
                    Success = false,
                    Config = _offlineStore.LoadConfig(),
                    ActiveRing = _offlineStore.LoadActiveRing(),
                    Devices = AudioRouter.EnumerateActiveOutputDevices()
                };
                if (includeDiagnostics)
                {
                    resp.Diagnostics = PowerWakeManager.CollectDiagnostics(resp.Config, null);
                }
            }

            _lastStatus = resp;
            if (resp.Diagnostics != null)
            {
                _cachedDiag = resp.Diagnostics;
            }

            // Top bar + sidebar status
            if (online)
            {
                _pillService.Text = "Service Online";
                _pillService.AccentColor = UiTheme.SuccessGreen;
                _lblSidebarHealth.Text = "Service Online";
                _sidebarDotService.Tag = UiTheme.SuccessGreen;
            }
            else
            {
                _pillService.Text = "Service Offline";
                _pillService.AccentColor = UiTheme.DangerRed;
                _lblSidebarHealth.Text = "Service Offline";
                _sidebarDotService.Tag = UiTheme.DangerRed;
            }
            _sidebarDotService.Invalidate();

            if (resp.ActiveRing != null)
            {
                _ringAlertStrip.Visible = true;
                _lblRingAlert.Text = "ALARM RINGING: " + resp.ActiveRing.Label + "  (Started " + resp.ActiveRing.StartedAtIso + ")";
                _btnHeroStop.Tone = ButtonTone.Danger;
                _trayIcon.Icon = _ringingIcon;
                _trayIcon.Text = "UltimateAlarm - RINGING!";
            }
            else
            {
                _ringAlertStrip.Visible = false;
                _btnHeroStop.Tone = ButtonTone.Secondary;
                _trayIcon.Icon = _normalIcon;
                _trayIcon.Text = online ? ("UltimateAlarm - Next: " + (resp.NextWakeIso ?? "None")) : "UltimateAlarm - Offline";
            }

            List<AudioDeviceInfo> targetDevices = AudioRouter.SelectTargetDevices(resp.Config, null);
            if (resp.Config != null && resp.Config.RingAllOutputDevices && targetDevices.Count > 0)
            {
                _pillSpeaker.Text = "Audio: All (" + targetDevices.Count + " outputs)";
                _pillSpeaker.AccentColor = UiTheme.AccentPrimary;
            }
            else if (targetDevices.Count > 0)
            {
                _pillSpeaker.Text = "Speaker: " + targetDevices[0].FriendlyName;
                _pillSpeaker.AccentColor = targetDevices[0].Category == "Headphones" ? UiTheme.WarnAmber : UiTheme.SuccessGreen;
            }
            else
            {
                _pillSpeaker.Text = "Audio: None active";
                _pillSpeaker.AccentColor = UiTheme.DangerRed;
            }

            string modeLabel = (resp.Config != null && resp.Config.AuthMode == "AutoLogonAndLock")
                ? "Auto-Lock"
                : "LocalSystem";
            string adminLabel = SecurityHelper.IsAdministrator() ? "Admin" : "User";
            _lblSidebarContext.Text = modeLabel + "  ·  " + adminLabel;

            string alarmSig = JsonHelper.Serialize(resp.Config != null ? resp.Config.Alarms : new List<AlarmItem>());
            if (!string.Equals(alarmSig, _lastAlarmsSignature, StringComparison.Ordinal))
            {
                _lastAlarmsSignature = alarmSig;
                RebuildAlarmCards(resp.Config);
            }

            string devSig = JsonHelper.Serialize(resp.Devices) + "|" + (resp.Config != null ? (resp.Config.PinnedSpeakerDeviceId + "|" + resp.Config.RingAllOutputDevices + "|" + resp.Config.AlarmVolumePercent + "|" + resp.Config.AllowHeadphoneFallback + "|" + resp.Config.AllowHdmiFallback) : "");
            if (!string.Equals(devSig, _lastDevicesSignature, StringComparison.Ordinal))
            {
                _lastDevicesSignature = devSig;
                PopulateAudioControls(resp.Config, resp.Devices, targetDevices);
            }

            if (resp.Diagnostics != null)
            {
                PopulateDiagnostics(resp.Diagnostics, resp.Config);
            }

            if (resp.ActiveRing != null && !resp.ActiveRing.IsTest)
            {
                if (!string.Equals(_activeChallengeOccurrence, resp.ActiveRing.OccurrenceKey, StringComparison.Ordinal))
                {
                    _activeChallengeOccurrence = resp.ActiveRing.OccurrenceKey;
                    OpenOrFocusChallenge();
                }
            }
            else
            {
                _activeChallengeOccurrence = "";
                if (_challengeForm != null && !_challengeForm.IsDisposed)
                {
                    _challengeForm.Close();
                    _challengeForm = null;
                }
            }
            UpdateClockAndCountdown();
        }

        private static void DisposeAndClearControls(Control host)
        {
            if (host == null) return;
            while (host.Controls.Count > 0)
            {
                Control c = host.Controls[host.Controls.Count - 1];
                host.Controls.RemoveAt(host.Controls.Count - 1);
                try { c.Dispose(); } catch { }
            }
        }

        private void RebuildAlarmCards(AlarmConfig cfg)
        {
            _alarmsListHost.SuspendLayout();
            DisposeAndClearControls(_alarmsListHost);

            int count = (cfg != null && cfg.Alarms != null) ? cfg.Alarms.Count : 0;
            _lblAlarmsSectionTitle.Text = count > 0 ? string.Format("ARMED ALARMS ({0})", count) : "ARMED ALARMS";

            int cardWidth = Math.Max(460, _alarmsListHost.ClientSize.Width - (_alarmsListHost.VerticalScroll.Visible ? 18 : 1));
            if (count == 0)
            {
                CardPanel empty = new CardPanel { Location = new Point(0, 4), Size = new Size(cardWidth, 120) };
                Label lbl = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = UiTheme.TextSecondary,
                    Font = UiTheme.FontSubTitle,
                    Text = "No alarms scheduled yet.\nClick New Alarm above or open Setup Wizard to arm your first bulletproof alarm."
                };
                empty.Controls.Add(lbl);
                _alarmsListHost.Controls.Add(empty);
                _alarmsListHost.ResumeLayout();
                return;
            }

            int y = 4;
            for (int i = 0; i < cfg.Alarms.Count; i++)
            {
                AlarmItem a = cfg.Alarms[i];
                CardPanel card = new CardPanel
                {
                    Location = new Point(0, y),
                    Size = new Size(cardWidth, 82),
                    BorderColor = UiTheme.BorderSubtle,
                    LeftAccentColor = a.Enabled ? UiTheme.AccentPrimary : Color.Transparent
                };

                Label lblTime = new Label
                {
                    Text = string.Format("{0:00}:{1:00}", a.Hour, a.Minute),
                    Font = UiTheme.FontDisplay,
                    ForeColor = a.Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted,
                    Location = new Point(22, 16),
                    AutoSize = true
                };

                Label lblName = new Label
                {
                    Text = string.IsNullOrEmpty(a.Label) ? "Alarm" : a.Label,
                    Font = UiTheme.FontSubTitle,
                    ForeColor = a.Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted,
                    Location = new Point(136, 17),
                    Size = new Size(Math.Max(180, cardWidth - 360), 22)
                };

                int alarmVol = a.VolumePercent > 0
                    ? AudioRouter.ClampVolumePercent(a.VolumePercent)
                    : AudioRouter.ResolveTargetVolumePercent(cfg, null);
                Label lblMeta = new Label
                {
                    Text = string.Format("{0}  ·  {1}  ·  {2}% vol  ·  {3} dismiss  ·  {4}m catch-up",
                        a.FormatDays(), AudioRouter.GetPresetShortName(a.SoundPreset), alarmVol, a.ChallengeType ?? "Math", a.BootCatchupMinutes),
                    Font = UiTheme.FontSmall,
                    ForeColor = a.Enabled ? UiTheme.TextSecondary : UiTheme.TextMuted,
                    Location = new Point(136, 44),
                    Size = new Size(Math.Max(220, cardWidth - 360), 20)
                };

                ModernToggleSwitch sw = new ModernToggleSwitch
                {
                    Location = new Point(cardWidth - 196, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                sw.SetCheckedSilent(a.Enabled);
                string alarmId = a.Id;
                sw.CheckedChanged += delegate { ToggleAlarm(alarmId, sw.Checked); };

                ModernButton btnEdit = new ModernButton
                {
                    Text = "Edit",
                    Glyph = UiGlyph.Edit,
                    Tone = ButtonTone.Secondary,
                    Size = new Size(76, 30),
                    Location = new Point(cardWidth - 138, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                btnEdit.Click += delegate { EditAlarm(alarmId); };

                ModernButton btnDel = new ModernButton
                {
                    Text = "",
                    Glyph = UiGlyph.Trash,
                    Tone = ButtonTone.DangerGhost,
                    Size = new Size(34, 30),
                    Location = new Point(cardWidth - 52, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                btnDel.Click += delegate { DeleteAlarm(alarmId); };

                card.Controls.AddRange(new Control[] { lblTime, lblName, lblMeta, sw, btnEdit, btnDel });
                _alarmsListHost.Controls.Add(card);
                y += 92;
            }
            _alarmsListHost.ResumeLayout();
        }

        private void PopulateAudioControls(AlarmConfig cfg, List<AudioDeviceInfo> devices, List<AudioDeviceInfo> activeTargets)
        {
            _suppressEvents = true;
            try
            {
                int vol = AudioRouter.ResolveTargetVolumePercent(cfg, null);
                bool ringAll = cfg != null && cfg.RingAllOutputDevices;
                _numDefaultVolume.Value = vol;
                _trkDefaultVolume.Value = vol;
                _btnTestAudioPage.Text = (ringAll ? "Test All Outputs @ " : "Test Output @ ") + vol + "%";
                _chkRingAllOutputs.SetCheckedSilent(ringAll);
                _cmbPinnedSpeaker.Enabled = !ringAll;
                _chkHdmiFallback.SetCheckedSilent(cfg == null || cfg.AllowHdmiFallback);
                _chkHeadphoneFallback.SetCheckedSilent(cfg != null && cfg.AllowHeadphoneFallback);

                _cmbPinnedSpeaker.ClearItems();
                _cmbPinnedSpeaker.AddItem(new DeviceChoice("", "Auto-detect Physical Speakers (Recommended)"));
                int selIdx = 0;
                string pinnedId = cfg != null ? (cfg.PinnedSpeakerDeviceId ?? "") : "";

                _devicesListHost.SuspendLayout();
                DisposeAndClearControls(_devicesListHost);
                int devCount = devices != null ? devices.Count : 0;
                _lblDevicesSection.Text = string.Format("DETECTED OUTPUT DEVICES ({0})", devCount);

                int cardWidth = Math.Max(460, _devicesListHost.ClientSize.Width - (_devicesListHost.VerticalScroll.Visible ? 18 : 1));
                int y = 4;

                if (devices != null)
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        AudioDeviceInfo d = devices[i];
                        int idx = _cmbPinnedSpeaker.AddItem(new DeviceChoice(d.Id, "[" + d.Category + "] " + d.FriendlyName));
                        if (!string.IsNullOrEmpty(pinnedId) && string.Equals(pinnedId, d.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            selIdx = idx;
                        }

                        bool isChosen = false;
                        if (activeTargets != null)
                        {
                            for (int t = 0; t < activeTargets.Count; t++)
                            {
                                if (string.Equals(activeTargets[t].Id, d.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    isChosen = true;
                                    break;
                                }
                            }
                        }

                        CardPanel card = new CardPanel
                        {
                            Location = new Point(0, y),
                            Size = new Size(cardWidth, 66),
                            BorderColor = isChosen ? UiTheme.SuccessBorder : UiTheme.BorderSubtle,
                            LeftAccentColor = isChosen ? UiTheme.SuccessGreen : Color.Transparent
                        };

                        Color catColor = d.Category == "Speakers" ? UiTheme.SuccessGreen
                            : (d.Category == "Headphones" ? UiTheme.DangerRed
                            : (d.Category == "HDMI/Display" ? UiTheme.WarnAmber : UiTheme.TextMuted));
                        UiGlyph devGlyph = d.Category == "Headphones" ? UiGlyph.Headphones
                            : (d.Category == "HDMI/Display" ? UiGlyph.Monitor : UiGlyph.Speaker);

                        Panel iconBox = new Panel
                        {
                            Location = new Point(16, 15),
                            Size = new Size(36, 36),
                            BackColor = UiTheme.BgSurface
                        };
                        iconBox.Paint += delegate(object s, PaintEventArgs e)
                        {
                            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                            using (GraphicsPath gp = UiTheme.CreateRoundRect(new Rectangle(0, 0, 35, 35), 7))
                            using (SolidBrush br = new SolidBrush(Color.FromArgb(24, catColor.R, catColor.G, catColor.B)))
                            using (Pen p = new Pen(Color.FromArgb(65, catColor.R, catColor.G, catColor.B), 1f))
                            {
                                e.Graphics.FillPath(br, gp);
                                e.Graphics.DrawPath(p, gp);
                            }
                            UiIcons.DrawGlyph(e.Graphics, devGlyph, new Rectangle(9, 9, 18, 18), catColor, 1.55f);
                        };

                        Label lName = new Label
                        {
                            Text = d.FriendlyName,
                            Font = UiTheme.FontBodyBold,
                            ForeColor = UiTheme.TextPrimary,
                            Location = new Point(64, 13),
                            Size = new Size(Math.Max(160, cardWidth - 364), 20),
                            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                            AutoEllipsis = true,
                            UseMnemonic = false
                        };

                        string defaultTag = d.IsDefaultMultimedia ? "Windows Default" : "Secondary";
                        Label lSub = new Label
                        {
                            Text = string.Format("{0}  ·  {1}  ·  Priority {2}", d.Category, defaultTag, d.SpeakerScore),
                            Font = UiTheme.FontSmall,
                            ForeColor = UiTheme.TextSecondary,
                            Location = new Point(64, 35),
                            Size = new Size(Math.Max(160, cardWidth - 364), 18),
                            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                            AutoEllipsis = true,
                            UseMnemonic = false
                        };

                        if (isChosen)
                        {
                            StatusPill activePill = new StatusPill
                            {
                                Text = "Active Target",
                                AccentColor = UiTheme.SuccessGreen,
                                Compact = true,
                                Width = 112,
                                Location = new Point(cardWidth - 288, 21),
                                Anchor = AnchorStyles.Top | AnchorStyles.Right
                            };
                            card.Controls.Add(activePill);
                        }

                        Label lVol = new Label
                        {
                            Text = string.Format("{0:0}%\n{1}", d.VolumeScalar * 100f, d.IsMuted ? "MUTED" : "Unmuted"),
                            Font = UiTheme.FontSmallBold,
                            ForeColor = d.IsMuted ? UiTheme.WarnAmber : UiTheme.TextSecondary,
                            Location = new Point(cardWidth - 170, 14),
                            Size = new Size(72, 38),
                            TextAlign = ContentAlignment.MiddleRight,
                            Anchor = AnchorStyles.Top | AnchorStyles.Right
                        };

                        string devId = d.Id;
                        ModernButton btnTestDev = new ModernButton
                        {
                            Text = "Test",
                            Glyph = UiGlyph.Play,
                            Tone = ButtonTone.Secondary,
                            Size = new Size(74, 30),
                            Location = new Point(cardWidth - 90, 18),
                            Anchor = AnchorStyles.Top | AnchorStyles.Right
                        };
                        btnTestDev.Click += delegate { TriggerTestRing(devId); };

                        card.Controls.AddRange(new Control[] { iconBox, lName, lSub, lVol, btnTestDev });
                        _devicesListHost.Controls.Add(card);
                        y += 74;
                    }
                }
                _cmbPinnedSpeaker.SelectedIndex = selIdx;
                _devicesListHost.ResumeLayout();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void PopulateDiagnostics(WakeDiagnostics d, AlarmConfig cfg)
        {
            _suppressEvents = true;
            try
            {
                _chkTaskBackup.SetCheckedSilent(cfg == null || cfg.EnableTaskSchedulerWakeBackup);
                _chkAutoLockOnBoot.SetCheckedSilent(cfg == null || cfg.AutoLockOnBoot);
            }
            finally
            {
                _suppressEvents = false;
            }

            bool wakeOk = d.WakeTimersEnabledAC && d.WakeTimersEnabledDC;
            _pillWake.Text = wakeOk ? "Wake Timers OK" : "Wake Timers Off";
            _pillWake.AccentColor = wakeOk ? UiTheme.SuccessGreen : UiTheme.WarnAmber;
            _sidebarDotWake.Tag = wakeOk ? UiTheme.SuccessGreen : UiTheme.WarnAmber;
            _sidebarDotWake.Invalidate();

            _lblSvcCard.Text = d.ServiceRunning
                ? "Running as " + (d.RunningAsLocalSystem ? "NT AUTHORITY\\SYSTEM (pre-login ready)\nPower Throttling: Disabled" : "User Context")
                : (d.ServiceInstalled ? "Installed but stopped — click Install / Repair Service." : "Not installed — run Setup Wizard or Repair Service.");

            string planName = string.IsNullOrEmpty(d.ActivePowerPlanName) ? "Current" : d.ActivePowerPlanName;
            _lblWakeCard.Text = string.Format("Plan: {0}  ·  Wake AC/DC: {1}/{2}\nUSB Suspend: {3}  ·  Audio Idle Sleep: {4}",
                planName,
                d.WakeTimersEnabledAC ? "On" : "OFF",
                d.WakeTimersEnabledDC ? "On" : "OFF",
                d.UsbSelectiveSuspendDisabled ? "Disabled" : "Active",
                d.AudioIdleSleepDisabled ? "Disabled" : "Active (4-30s)");

            string scrStr = d.ScreenSaverActive ? ("Active (" + d.ScreenSaverTimeoutSeconds + "s, auto-wake)") : "Off (auto-wake active)";
            _lblPowerCard.Text = string.Format("Sleep S3: {0}  ·  Standby S0: {1}  ·  Hibernate S4: {2}\nFast Startup: {3}  ·  Screen Saver: {4}",
                d.SleepS3Supported ? "Yes" : "No",
                d.ModernStandbyS0Supported ? "Yes" : "No",
                d.HibernateS4Supported ? "Yes" : "No",
                d.FastStartupEnabled ? "On" : "Off",
                scrStr);

            string startupState = d.StartupExposedInTaskManager
                ? "Exposed in Task Mgr"
                : (d.TrayAutoStartRegistered ? "Protected (Hidden)" : "Service-Managed");
            _lblBackupCard.Text = string.Format("Task Backup: {0}  ·  Startup: {1}",
                d.TaskBackupRegistered ? "Registered" : "Not set",
                startupState);

            string pinState = (cfg != null && !string.IsNullOrEmpty(cfg.DismissPinHash)) ? "Configured (PBKDF2)" : "Not set";
            _lblSecuritySummary.Text = string.Format(
                "Auth Mode: {0}  ·  Windows Auto-Logon: {1} ({2})  ·  Dismiss PIN: {3}",
                cfg != null ? cfg.AuthMode : "LocalSystem",
                d.AutoLogonEnabled ? "Enabled" : "Disabled",
                string.IsNullOrEmpty(d.AutoLogonUser) ? "N/A" : d.AutoLogonUser,
                pinState);

            _txtBiosGuide.Text = d.BiosRtcGuide ?? "";
        }

        private void ResizeCards(Panel host)
        {
            int scrollPad = host.VerticalScroll.Visible ? 18 : 1;
            int w = Math.Max(460, host.ClientSize.Width - scrollPad);
            for (int i = 0; i < host.Controls.Count; i++)
            {
                host.Controls[i].Width = w;
            }
        }

        private void OpenSetupWizard()
        {
            using (SetupWizardForm wiz = new SetupWizardForm(_releaseMutex))
            {
                DialogResult res = wiz.ShowDialog(this);
                if (wiz.ElevationRequested || res == DialogResult.Abort)
                {
                    _allowCloseExit = true;
                    Application.Exit();
                    return;
                }
                RefreshFromService(true);
            }
        }

        private void AddNewAlarm()
        {
            using (AlarmEditorDialog dlg = new AlarmEditorDialog(null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveAlarmItem(dlg.Result);
                }
            }
        }

        private void EditAlarm(string id)
        {
            if (_lastStatus == null || _lastStatus.Config == null) return;
            AlarmItem existing = null;
            for (int i = 0; i < _lastStatus.Config.Alarms.Count; i++)
            {
                if (string.Equals(_lastStatus.Config.Alarms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    existing = _lastStatus.Config.Alarms[i];
                    break;
                }
            }
            if (existing == null) return;
            using (AlarmEditorDialog dlg = new AlarmEditorDialog(existing))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveAlarmItem(dlg.Result);
                }
            }
        }

        private void SaveAlarmItem(AlarmItem item)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "ADD_OR_UPDATE_ALARM", Alarm = item }, 1200);
            if (resp != null && !resp.Success && resp.Config != null && !string.IsNullOrEmpty(resp.Error))
            {
                MessageBox.Show(resp.Error, "Alarm Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                bool found = false;
                for (int i = 0; i < cfg.Alarms.Count; i++)
                {
                    if (string.Equals(cfg.Alarms[i].Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms[i] = item;
                        found = true;
                        break;
                    }
                }
                if (!found) cfg.Alarms.Add(item);
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private void ToggleAlarm(string id, bool enabled)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "TOGGLE_ALARM", AlarmId = id, BoolFlag = enabled }, 1200);
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                for (int i = 0; i < cfg.Alarms.Count; i++)
                {
                    if (string.Equals(cfg.Alarms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms[i].Enabled = enabled;
                    }
                }
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private void DeleteAlarm(string id)
        {
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "DELETE_ALARM", AlarmId = id }, 1200);
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                for (int i = cfg.Alarms.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(cfg.Alarms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.Alarms.RemoveAt(i);
                    }
                }
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private string GetSelectedTestToneId()
        {
            if (_cmbTestTone != null)
            {
                AudioRouter.SoundPresetInfo info = _cmbTestTone.SelectedItem as AudioRouter.SoundPresetInfo;
                if (info != null) return info.Id;
            }
            return "KlaxonPulse";
        }

        private void TriggerTestRing()
        {
            TriggerTestRing(null);
        }

        private void TriggerTestRing(string deviceId)
        {
            int testVol = AudioRouter.ClampVolumePercent(_numDefaultVolume.Value);
            string toneId = GetSelectedTestToneId();
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest
            {
                Command = "TEST_RING",
                StringParam = toneId,
                IntParam = testVol,
                DeviceId = deviceId ?? ""
            }, 1200);
            if (resp == null || !resp.Success)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    AlarmConfig cfg = _offlineStore.LoadConfig();
                    ActiveRingState r = new ActiveRingState
                    {
                        SoundPreset = toneId,
                        TargetDeviceId = deviceId ?? "",
                        ForceFullVolume = true,
                        TargetVolumePercent = testVol,
                        IsTest = true
                    };
                    using (ManualResetEvent stop = new ManualResetEvent(false))
                    {
                        AudioRouter.PlayAlarmLoop(cfg, r, stop, 4);
                    }
                });
                MessageBox.Show("Service is offline; running direct 4-second speaker test (" + AudioRouter.GetPresetShortName(toneId) + ") at " + testVol + "% unmuted.", "UltimateAlarm", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            RefreshFromService(false);
        }

        private void SaveDefaultVolume()
        {
            int vol = AudioRouter.ClampVolumePercent(_numDefaultVolume.Value);
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "SET_AUDIO_POLICY", StringParam = "VOLUME", IntParam = vol }, 1200);
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                cfg.AlarmVolumePercent = vol;
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private void SavePinnedSpeaker()
        {
            DeviceChoice choice = _cmbPinnedSpeaker.SelectedItem as DeviceChoice;
            string id = choice != null ? choice.Id : "";
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "SET_PINNED_SPEAKER", DeviceId = id }, 1200);
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                cfg.PinnedSpeakerDeviceId = id;
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private void OnMainRingAllChanged()
        {
            if (_suppressEvents) return;
            bool enableAll = _chkRingAllOutputs.Checked;
            if (enableAll && !_chkHeadphoneFallback.Checked)
            {
                DialogResult res = MessageBox.Show(
                    "Permission Confirmation — Multi-Device Broadcast\n\n" +
                    "UltimateAlarm will ring ALL connected speakers & HDMI/monitor audio devices simultaneously at your chosen alarm volume (" + _numDefaultVolume.Value + "%).\n\n" +
                    "Do you ALSO grant permission to ring plugged-in Headphones / Headsets simultaneously?\n\n" +
                    "• Yes = Ring EVERY connected output (including Headphones)\n" +
                    "• No  = Ring all Speakers & HDMI monitors while keeping Headphones blocked",
                    "Multi-Output Audio Permission",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    _chkHeadphoneFallback.SetCheckedSilent(true);
                    SavePolicyFlag("HEADPHONES", true);
                }
            }
            SavePolicyFlag("RINGALL", enableAll);
        }

        private void SavePolicyFlag(string kind, bool value)
        {
            if (_suppressEvents) return;
            ServiceStatusResponse resp = IpcProtocol.SendRequest(new IpcRequest { Command = "SET_AUDIO_POLICY", StringParam = kind, BoolFlag = value }, 1200);
            if (resp == null || !resp.Success)
            {
                AlarmConfig cfg = _offlineStore.LoadConfig();
                if (kind == "HDMI") cfg.AllowHdmiFallback = value;
                if (kind == "HEADPHONES") cfg.AllowHeadphoneFallback = value;
                if (kind == "RINGALL") cfg.RingAllOutputDevices = value;
                if (kind == "TASKBACKUP") cfg.EnableTaskSchedulerWakeBackup = value;
                if (kind == "AUTOLOCK") cfg.AutoLockOnBoot = value;
                _offlineStore.SaveConfig(cfg);
            }
            RefreshFromService(true);
        }

        private void RunRepairServiceAction()
        {
            if (!SecurityHelper.IsAdministrator())
            {
                OpenSetupWizard();
                return;
            }
            string log, err;
            if (SecurityHelper.InstallOrRepairService(out log, out err))
            {
                MessageBox.Show("UltimateAlarmService installed/repaired and running as LocalSystem.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(err, "Service Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshFromService(true);
        }

        private void RunFixWakeAction()
        {
            ServiceStatusResponse ipcResp = IpcProtocol.SendRequest(new IpcRequest { Command = "OPTIMIZE_POWER" }, 4000);
            if (ipcResp != null && ipcResp.Success)
            {
                MessageBox.Show(
                    "PC Power & Startup Hardened:\n\n" +
                    "• AC & DC Wake Timers enabled across all power schemes (including Power Saver)\n" +
                    "• USB Selective Suspend & HD/HDMI Audio idle power-down disabled\n" +
                    "• Legacy Task Manager Startup entry removed (protected via Service & hidden logon task)",
                    "Power & Wake Hardened", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshFromService(true);
                return;
            }
            if (!SecurityHelper.IsAdministrator())
            {
                MessageBox.Show("Service is offline and UI is not elevated. Use 'Setup Wizard' -> Grant Admin to optimize PC power and wake timers.", "Admin Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string log, err;
            if (PowerWakeManager.OptimizePowerForAlarms(out log, out err))
            {
                MessageBox.Show("PC power settings, wake timers across all plans, audio idle sleep, and protected startup have been configured!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(err, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshFromService(true);
        }

        private void RunUninstallAction()
        {
            if (!SecurityHelper.IsAdministrator())
            {
                MessageBox.Show("Run UltimateAlarm.exe as Administrator or run uninstall.ps1 to uninstall the system service.", "Admin Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult r = MessageBox.Show(
                "Stop and remove UltimateAlarmService, WakeToRun scheduled task, and Tray auto-start?",
                "Confirm Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            string log, err;
            if (SecurityHelper.UninstallServiceAndTasks(out log, out err))
            {
                MessageBox.Show(log, "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshFromService(true);
            }
            else
            {
                MessageBox.Show(err, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadLogsTail()
        {
            try
            {
                if (!File.Exists(AppPaths.LogFilePath))
                {
                    _txtLogs.Text = "No log file yet at " + AppPaths.LogFilePath;
                    return;
                }
                string[] lines = File.ReadAllLines(AppPaths.LogFilePath, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 200);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++)
                {
                    sb.AppendLine(lines[i]);
                }
                _txtLogs.Text = sb.ToString();
                _txtLogs.ScrollToEnd();
            }
            catch (Exception ex)
            {
                _txtLogs.Text = "Error reading log: " + ex.Message;
            }
        }

        private void SendSimpleCommand(string command)
        {
            IpcProtocol.SendRequest(new IpcRequest { Command = command }, 1200);
            RefreshFromService(false);
        }

        private void OpenOrFocusChallenge()
        {
            if (_lastStatus == null || _lastStatus.ActiveRing == null) return;
            if (_challengeForm != null && !_challengeForm.IsDisposed)
            {
                _challengeForm.Activate();
                return;
            }
            _challengeForm = new RingChallengeForm(_lastStatus.ActiveRing, _lastStatus.Config);
            _challengeForm.FormClosed += delegate
            {
                _challengeForm = null;
                RefreshFromService(false);
            };
            _challengeForm.Show();
            _challengeForm.BringToFront();
            _challengeForm.Activate();
        }

        private sealed class DeviceChoice
        {
            public readonly string Id;
            public readonly string Text;
            public DeviceChoice(string id, string text) { Id = id; Text = text; }
            public override string ToString() { return Text; }
        }
    }
}
