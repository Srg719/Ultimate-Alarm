using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using UltimateAlarm.Core;

namespace UltimateAlarm.UI
{
    internal sealed class AlarmEditorDialog : DarkChromeForm
    {
        public AlarmItem Result { get; private set; }

        private ModernTimePicker _dtTime;
        private ModernDatePicker _dtOneShotDate;
        private ModernTextBox _txtLabel;
        private ModernComboBox _cmbSound;
        private ModernTextBox _txtWav;
        private ModernComboBox _cmbChallenge;
        private ModernSlider _trkVolume;
        private ModernNumeric _numVolume;
        private ModernNumeric _numCatchup;
        private ModernNumeric _numSnooze;
        private ModernButton[] _dayButtons;
        private bool[] _daySelected;
        private ModernButton _btnPreview;
        private readonly object _previewLock = new object();
        private ManualResetEvent _previewStop;
        private Icon _dialogIcon;

        public AlarmEditorDialog(AlarmItem existing)
            : base(false, false, false, true)
        {
            Result = existing != null ? CloneItem(existing) : new AlarmItem();
            Text = existing != null ? "Edit Alarm" : "New Alarm";
            Size = new Size(564, 616);
            StartPosition = FormStartPosition.CenterParent;

            _dialogIcon = UiTheme.CreateAppIcon(false, 32);
            Icon = _dialogIcon;
            RefreshChromeIcon(_dialogIcon);

            BuildLayout(existing);
        }

        private void BuildLayout(AlarmItem existing)
        {
            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.BgCanvas,
                Padding = new Padding(24, 12, 24, 12)
            };

            Label lblHead = new Label
            {
                Text = existing != null ? "Update Alarm Schedule" : "Create Bulletproof Alarm",
                Font = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(24, 8),
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = "Configure wake time, repeat pattern, audio preset, and dismiss challenge.",
                Font = UiTheme.FontSmall,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(24, 36),
                AutoSize = true
            };
            content.Controls.AddRange(new Control[] { lblHead, lblSub });

            int y = 72;
            AddFieldLabel(content, "Alarm Time (24h)", 24, y + 8);
            _dtTime = new ModernTimePicker
            {
                Location = new Point(168, y),
                Size = new Size(116, 36),
                Value = new DateTime(2026, 1, 1, existing != null ? existing.Hour : 7, existing != null ? existing.Minute : 0, 0)
            };
            content.Controls.Add(_dtTime);

            AddFieldLabel(content, "One-Shot", 304, y + 8);
            DateTime defDate = DateTime.Today;
            if (!string.IsNullOrEmpty(Result.OneShotDate))
            {
                DateTime parsedDate;
                if (DateTime.TryParseExact(Result.OneShotDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                {
                    defDate = parsedDate;
                }
            }
            _dtOneShotDate = new ModernDatePicker
            {
                Location = new Point(380, y),
                Size = new Size(156, 36),
                Value = defDate
            };
            content.Controls.Add(_dtOneShotDate);

            y += 50;
            AddFieldLabel(content, "Label", 24, y + 6);
            _txtLabel = new ModernTextBox
            {
                Location = new Point(168, y),
                Size = new Size(368, 32),
                Text = Result.Label ?? "Wake up"
            };
            content.Controls.Add(_txtLabel);

            y += 46;
            AddFieldLabel(content, "Repeat Days", 24, y + 6);
            string[] dayNames = new string[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            _dayButtons = new ModernButton[7];
            _daySelected = new bool[7];
            for (int i = 0; i < 7; i++)
            {
                int idx = i;
                bool active = Result.DaysOfWeek != null && Array.IndexOf(Result.DaysOfWeek, i) >= 0;
                _daySelected[i] = active;
                ModernButton btn = new ModernButton
                {
                    Text = dayNames[i],
                    Location = new Point(168 + i * 53, y),
                    Size = new Size(46, 30),
                    Tone = active ? ButtonTone.Primary : ButtonTone.Secondary,
                    CornerRadius = 6
                };
                btn.Click += delegate
                {
                    _daySelected[idx] = !_daySelected[idx];
                    btn.Tone = _daySelected[idx] ? ButtonTone.Primary : ButtonTone.Secondary;
                    UpdateDatePickerEnabled();
                };
                _dayButtons[i] = btn;
                content.Controls.Add(btn);
            }

            y += 38;
            ModernButton btnOnce = new ModernButton { Text = "Once", Location = new Point(168, y), Size = new Size(74, 26), Font = UiTheme.FontSmallBold };
            btnOnce.Click += delegate { SetDayMask(new int[0]); };
            ModernButton btnWeekdays = new ModernButton { Text = "Weekdays", Location = new Point(250, y), Size = new Size(94, 26), Font = UiTheme.FontSmallBold };
            btnWeekdays.Click += delegate { SetDayMask(new int[] { 1, 2, 3, 4, 5 }); };
            ModernButton btnDaily = new ModernButton { Text = "Every Day", Location = new Point(352, y), Size = new Size(94, 26), Font = UiTheme.FontSmallBold };
            btnDaily.Click += delegate { SetDayMask(new int[] { 0, 1, 2, 3, 4, 5, 6 }); };
            content.Controls.AddRange(new Control[] { btnOnce, btnWeekdays, btnDaily });
            UpdateDatePickerEnabled();

            y += 42;
            AddFieldLabel(content, "Sound Preset", 24, y + 6);
            _cmbSound = new ModernComboBox { Location = new Point(168, y), Width = 264 };
            string targetPreset = string.IsNullOrEmpty(Result.SoundPreset) ? "KlaxonPulse" : Result.SoundPreset;
            int soundSelIdx = 0;
            for (int i = 0; i < AudioRouter.BuiltInPresets.Length; i++)
            {
                int idx = _cmbSound.AddItem(AudioRouter.BuiltInPresets[i]);
                if (string.Equals(AudioRouter.BuiltInPresets[i].Id, targetPreset, StringComparison.OrdinalIgnoreCase))
                {
                    soundSelIdx = idx;
                }
            }
            int customIdx = _cmbSound.AddItem(new AudioRouter.SoundPresetInfo("CustomWav", "Custom WAV File...", "Custom WAV"));
            if (string.Equals("CustomWav", targetPreset, StringComparison.OrdinalIgnoreCase))
            {
                soundSelIdx = customIdx;
            }
            _cmbSound.SelectedIndex = soundSelIdx;

            _btnPreview = new ModernButton { Text = "Preview", Glyph = UiGlyph.Play, Location = new Point(440, y), Size = new Size(96, 32), Tone = ButtonTone.Secondary, Font = UiTheme.FontSmallBold };
            _btnPreview.Click += delegate { TogglePreview(); };
            content.Controls.AddRange(new Control[] { _cmbSound, _btnPreview });

            y += 44;
            AddFieldLabel(content, "Custom WAV", 24, y + 6);
            _txtWav = new ModernTextBox { Location = new Point(168, y), Size = new Size(264, 32), Text = Result.CustomWavPath ?? "" };
            ModernButton btnBrowse = new ModernButton { Text = "Browse", Location = new Point(440, y), Size = new Size(96, 32), Font = UiTheme.FontSmallBold };
            btnBrowse.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog { Filter = "WAV Files (*.wav)|*.wav" })
                {
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        _txtWav.Text = ofd.FileName;
                        _cmbSound.SelectedIndex = _cmbSound.ItemCount - 1;
                    }
                }
            };
            content.Controls.AddRange(new Control[] { _txtWav, btnBrowse });

            y += 46;
            AddFieldLabel(content, "Alarm Volume", 24, y + 6);
            AlarmConfig globalCfg = new AlarmStore().LoadConfig();
            int initialVol = Result.VolumePercent > 0
                ? AudioRouter.ClampVolumePercent(Result.VolumePercent)
                : AudioRouter.ClampVolumePercent(globalCfg.AlarmVolumePercent);

            _trkVolume = new ModernSlider
            {
                Location = new Point(162, y + 2),
                Width = 270,
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Value = initialVol
            };
            _numVolume = new ModernNumeric
            {
                Location = new Point(440, y),
                Size = new Size(96, 32),
                Minimum = AudioRouter.MinVolumePercent,
                Maximum = 100,
                Suffix = "%",
                Value = initialVol
            };
            _trkVolume.Scroll += delegate { _numVolume.Value = _trkVolume.Value; };
            _numVolume.ValueChanged += delegate
            {
                int v = _numVolume.Value;
                if (_trkVolume.Value != v) _trkVolume.Value = v;
            };
            content.Controls.AddRange(new Control[] { _trkVolume, _numVolume });

            y += 46;
            AddFieldLabel(content, "Dismiss Challenge", 24, y + 6);
            _cmbChallenge = new ModernComboBox { Location = new Point(168, y), Width = 200 };
            _cmbChallenge.AddItems("Math", "TypeCode", "Pin", "None");
            _cmbChallenge.SelectedItem = string.IsNullOrEmpty(Result.ChallengeType) ? "Math" : Result.ChallengeType;
            if (_cmbChallenge.SelectedIndex < 0) _cmbChallenge.SelectedIndex = 0;
            content.Controls.Add(_cmbChallenge);

            y += 46;
            AddFieldLabel(content, "Boot Catch-up", 24, y + 6);
            _numCatchup = new ModernNumeric
            {
                Location = new Point(168, y),
                Size = new Size(94, 32),
                Minimum = 5,
                Maximum = 180,
                Suffix = "m",
                Value = Math.Max(5, Math.Min(180, Result.BootCatchupMinutes > 0 ? Result.BootCatchupMinutes : 45))
            };

            AddFieldLabel(content, "Snooze", 290, y + 6);
            _numSnooze = new ModernNumeric
            {
                Location = new Point(352, y),
                Size = new Size(94, 32),
                Minimum = 0,
                Maximum = 30,
                Suffix = "m",
                Value = Math.Max(0, Math.Min(30, Result.SnoozeMinutes))
            };
            content.Controls.AddRange(new Control[] { _numCatchup, _numSnooze });

            // Footer Bar
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = UiTheme.BgShell
            };
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
                }
            };

            ModernButton btnOk = new ModernButton { Text = "Save Alarm", Location = new Point(414, 12), Size = new Size(122, 34), Tone = ButtonTone.Primary };
            btnOk.Click += delegate { SaveAndClose(); };
            ModernButton btnCancel = new ModernButton { Text = "Cancel", Location = new Point(314, 12), Size = new Size(92, 34), Tone = ButtonTone.Secondary, DialogResult = DialogResult.Cancel };

            footer.Controls.AddRange(new Control[] { btnCancel, btnOk });

            BodyHost.Controls.Add(content);
            BodyHost.Controls.Add(footer);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void AddFieldLabel(Control parent, string text, int x, int y)
        {
            Label lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBodyBold, UseMnemonic = false };
            parent.Controls.Add(lbl);
        }

        private void SetDayMask(int[] days)
        {
            for (int i = 0; i < 7; i++)
            {
                bool on = Array.IndexOf(days, i) >= 0;
                _daySelected[i] = on;
                _dayButtons[i].Tone = on ? ButtonTone.Primary : ButtonTone.Secondary;
            }
            UpdateDatePickerEnabled();
        }

        private void UpdateDatePickerEnabled()
        {
            bool anyDay = false;
            for (int i = 0; i < 7; i++) if (_daySelected[i]) anyDay = true;
            _dtOneShotDate.Enabled = !anyDay;
        }

        private string GetSelectedSoundPresetId()
        {
            AudioRouter.SoundPresetInfo info = _cmbSound.SelectedItem as AudioRouter.SoundPresetInfo;
            if (info != null) return info.Id;
            return _cmbSound.SelectedItem != null ? _cmbSound.SelectedItem.ToString() : "KlaxonPulse";
        }

        private void TogglePreview()
        {
            lock (_previewLock)
            {
                if (_previewStop != null)
                {
                    try { _previewStop.Set(); } catch { }
                    return;
                }
            }

            string preset = GetSelectedSoundPresetId();
            string wav = _txtWav.Text.Trim();
            if (string.Equals(preset, "CustomWav", StringComparison.OrdinalIgnoreCase))
            {
                string wavErr = null;
                if (string.IsNullOrEmpty(wav) || !SecurityHelper.IsSafeLocalWavPath(wav, out wavErr) || !System.IO.File.Exists(wav))
                {
                    MessageBox.Show(string.IsNullOrEmpty(wav) ? "Please browse and select a local .wav file first." : (wavErr ?? "Selected .wav file was not found."),
                        "Custom WAV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            ManualResetEvent stopHandle = new ManualResetEvent(false);
            lock (_previewLock)
            {
                _previewStop = stopHandle;
            }
            _btnPreview.Text = "Stop";
            _btnPreview.Glyph = UiGlyph.Stop;
            _btnPreview.Tone = ButtonTone.Danger;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    AlarmConfig cfg = new AlarmStore().LoadConfig();
                    int vol = _numVolume.Value;
                    ActiveRingState r = new ActiveRingState
                    {
                        SoundPreset = preset,
                        CustomWavPath = wav,
                        ForceFullVolume = true,
                        TargetVolumePercent = vol,
                        IsTest = true
                    };
                    AudioRouter.PlayAlarmLoop(cfg, r, stopHandle, 4);
                }
                finally
                {
                    lock (_previewLock)
                    {
                        if (_previewStop == stopHandle)
                        {
                            _previewStop = null;
                        }
                    }
                    try { stopHandle.Dispose(); } catch { }
                    if (!IsDisposed)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _btnPreview.Text = "Preview";
                                _btnPreview.Glyph = UiGlyph.Play;
                                _btnPreview.Tone = ButtonTone.Secondary;
                            });
                        }
                        catch { }
                    }
                }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            lock (_previewLock)
            {
                if (_previewStop != null)
                {
                    try { _previewStop.Set(); } catch { }
                }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_dialogIcon != null) { try { _dialogIcon.Dispose(); } catch { } _dialogIcon = null; }
            base.OnFormClosed(e);
        }

        private void SaveAndClose()
        {
            string wav = _txtWav.Text.Trim();
            string wavErr;
            if (!SecurityHelper.IsSafeLocalWavPath(wav, out wavErr))
            {
                MessageBox.Show(wavErr, "Invalid Custom WAV Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result.Hour = _dtTime.Hour;
            Result.Minute = _dtTime.Minute;
            Result.Label = string.IsNullOrWhiteSpace(_txtLabel.Text) ? "Alarm" : _txtLabel.Text.Trim();

            List<int> days = new List<int>();
            for (int i = 0; i < 7; i++)
            {
                if (_daySelected[i]) days.Add(i);
            }
            Result.DaysOfWeek = days.ToArray();
            if (Result.IsOneShot())
            {
                DateTime targetDate = _dtOneShotDate.Value.Date;
                DateTime occ = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, Result.Hour, Result.Minute, 0);
                if (occ <= DateTime.Now && targetDate == DateTime.Today)
                {
                    occ = occ.AddDays(1);
                }
                Result.OneShotDate = occ.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                Result.OneShotDate = null;
            }
            Result.Enabled = true;
            Result.SoundPreset = GetSelectedSoundPresetId();
            Result.CustomWavPath = wav;
            Result.ChallengeType = _cmbChallenge.SelectedItem != null ? _cmbChallenge.SelectedItem.ToString() : "Math";
            Result.BootCatchupMinutes = _numCatchup.Value;
            Result.SnoozeMinutes = _numSnooze.Value;
            Result.VolumePercent = AudioRouter.ClampVolumePercent(_numVolume.Value);
            Result.ForceFullVolume = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static AlarmItem CloneItem(AlarmItem src)
        {
            return JsonHelper.Deserialize<AlarmItem>(JsonHelper.Serialize(src));
        }
    }

    internal sealed class RingChallengeForm : DarkChromeForm
    {
        private readonly ActiveRingState _ring;
        private readonly AlarmConfig _config;
        private readonly string _expectedAnswer;
        private readonly bool _isPinChallenge;
        private readonly DateTime _openedUtc = DateTime.UtcNow;

        private ModernTextBox _txtAnswer;
        private Label _lblTimer;
        private Label _lblError;
        private Panel _pulseHeader;
        private System.Windows.Forms.Timer _uiTimer;
        private Icon _ringIcon;
        private bool _pulseFlip;

        public RingChallengeForm(ActiveRingState ring, AlarmConfig config)
            : base(false, false, false, false)
        {
            _ring = ring;
            _config = config;
            Text = "ALARM RINGING - " + ring.Label;
            Size = new Size(520, 380);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            _ringIcon = UiTheme.CreateAppIcon(true, 32);
            Icon = _ringIcon;
            RefreshChromeLogo(true);

            SetTitleBarColor(UiTheme.DangerDark);
            BodyHost.BackColor = Color.FromArgb(24, 13, 17);

            _pulseHeader = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiTheme.DangerDark, Padding = new Padding(22, 6, 22, 10) };
            Label lblTitle = new Label
            {
                Text = "ALARM RINGING: " + (ring.Label ?? "Alarm"),
                Font = UiTheme.FontTitle,
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Height = 28
            };
            int activeVol = AudioRouter.ResolveTargetVolumePercent(config, ring);
            _lblTimer = new Label
            {
                Text = activeVol + "% Speaker Volume Active  ·  Elapsed: 00:00",
                Font = UiTheme.FontSmallBold,
                ForeColor = Color.FromArgb(254, 202, 202),
                Dock = DockStyle.Bottom,
                Height = 20
            };
            _pulseHeader.Controls.Add(lblTitle);
            _pulseHeader.Controls.Add(_lblTimer);
            BodyHost.Controls.Add(_pulseHeader);

            string prompt;
            bool isPin;
            _expectedAnswer = GenerateChallenge(ring.ChallengeType, config, out prompt, out isPin);
            _isPinChallenge = isPin;

            Label lblPrompt = new Label
            {
                Text = prompt,
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(24, 92),
                Size = new Size(472, 48),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _txtAnswer = new ModernTextBox
            {
                Font = new Font("Consolas", 16f, FontStyle.Bold),
                Location = new Point(142, 150),
                Size = new Size(236, 40),
                TextAlign = HorizontalAlignment.Center,
                UseSystemPasswordChar = isPin
            };
            if (string.IsNullOrEmpty(_expectedAnswer) && !isPin)
            {
                _txtAnswer.Visible = false;
            }

            _lblError = new Label
            {
                Text = "",
                Font = UiTheme.FontSmallBold,
                ForeColor = Color.FromArgb(252, 165, 165),
                Location = new Point(24, 198),
                Size = new Size(472, 22),
                TextAlign = ContentAlignment.MiddleCenter
            };

            ModernButton btnDismiss = new ModernButton
            {
                Text = "Dismiss Alarm",
                Glyph = UiGlyph.Check,
                Tone = ButtonTone.Success,
                Location = ring.SnoozeMinutes > 0 ? new Point(108, 240) : new Point(172, 240),
                Size = new Size(176, 42),
                Font = UiTheme.FontSubTitle
            };
            btnDismiss.Click += delegate { TryDismiss(); };

            ModernButton btnSnooze = new ModernButton
            {
                Text = "Snooze (" + ring.SnoozeMinutes + "m)",
                Tone = ButtonTone.Secondary,
                Location = new Point(296, 240),
                Size = new Size(118, 42),
                Visible = ring.SnoozeMinutes > 0
            };
            btnSnooze.Click += delegate
            {
                IpcProtocol.SendRequest(new IpcRequest { Command = "SNOOZE", IntParam = ring.SnoozeMinutes }, 1500);
                Close();
            };

            BodyHost.Controls.AddRange(new Control[] { lblPrompt, _txtAnswer, _lblError, btnDismiss, btnSnooze });
            AcceptButton = btnDismiss;

            _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _uiTimer.Tick += delegate
            {
                _pulseFlip = !_pulseFlip;
                Color hdrCol = _pulseFlip ? Color.FromArgb(135, 24, 30) : UiTheme.DangerDark;
                _pulseHeader.BackColor = hdrCol;
                SetTitleBarColor(hdrCol);
                TimeSpan elapsed = DateTime.UtcNow - _openedUtc;
                _lblTimer.Text = string.Format("{0}% Speaker Volume Active  ·  Ringing: {1:00}:{2:00}", activeVol, (int)elapsed.TotalMinutes, elapsed.Seconds);
            };
            _uiTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_txtAnswer != null && _txtAnswer.Visible)
            {
                _txtAnswer.Inner.Focus();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_uiTimer != null)
            {
                _uiTimer.Stop();
                _uiTimer.Dispose();
                _uiTimer = null;
            }
            if (_ringIcon != null) { try { _ringIcon.Dispose(); } catch { } _ringIcon = null; }
            base.OnFormClosed(e);
        }

        private void TryDismiss()
        {
            string entry = (_txtAnswer.Text ?? "").Trim();
            if (_isPinChallenge)
            {
                ServiceStatusResponse pinResp = IpcProtocol.SendRequest(new IpcRequest { Command = "VERIFY_PIN", StringParam = entry }, 1500);
                bool valid = false;
                if (pinResp != null && pinResp.Success)
                {
                    valid = true;
                }
                else if (pinResp != null && !string.IsNullOrEmpty(pinResp.Error) &&
                         (pinResp.Error.IndexOf("Incorrect PIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          pinResp.Error.IndexOf("Too many failed", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // Service explicitly rejected or rate-limited PIN: never bypass via local fallback.
                    RejectInput(pinResp.Error);
                    return;
                }
                else
                {
                    // Service offline fallback: read actual hash from disk (IPC config is redacted).
                    AlarmConfig diskCfg = new AlarmStore().LoadConfig();
                    valid = diskCfg != null && SecurityHelper.VerifyPin(entry, diskCfg.DismissPinHash, diskCfg.DismissPinSalt);
                }

                if (!valid)
                {
                    RejectInput("Incorrect Dismiss PIN. Try again.");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(_expectedAnswer))
            {
                if (!string.Equals(entry, _expectedAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    RejectInput("Incorrect answer. Try again.");
                    return;
                }
            }

            IpcProtocol.SendRequest(new IpcRequest { Command = "DISMISS" }, 1500);
            Close();
        }

        private void RejectInput(string msg)
        {
            _lblError.Text = msg;
            _txtAnswer.SelectAll();
            _txtAnswer.Inner.Focus();
        }

        private static int NextSecureInt(int minInclusive, int maxExclusive)
        {
            int range = maxExclusive - minInclusive;
            if (range <= 1) return minInclusive;
            byte[] buf = new byte[4];
            using (System.Security.Cryptography.RNGCryptoServiceProvider rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            uint val = BitConverter.ToUInt32(buf, 0);
            return minInclusive + (int)(val % (uint)range);
        }

        private static string GenerateChallenge(string type, AlarmConfig cfg, out string prompt, out bool isPin)
        {
            isPin = false;
            if (string.Equals(type, "Pin", StringComparison.OrdinalIgnoreCase) &&
                cfg != null && !string.IsNullOrEmpty(cfg.DismissPinHash))
            {
                isPin = true;
                prompt = "Enter your configured Dismiss PIN:";
                return "";
            }
            if (string.Equals(type, "None", StringComparison.OrdinalIgnoreCase))
            {
                prompt = "Click Dismiss Alarm to stop the siren.";
                return "";
            }
            if (string.Equals(type, "TypeCode", StringComparison.OrdinalIgnoreCase))
            {
                const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
                char[] buf = new char[6];
                for (int i = 0; i < buf.Length; i++) buf[i] = chars[NextSecureInt(0, chars.Length)];
                string code = new string(buf);
                prompt = "Type this code to dismiss:   " + code;
                return code;
            }
            int a = NextSecureInt(14, 49);
            int b = NextSecureInt(12, 39);
            int c = NextSecureInt(3, 9);
            int ans = a + (b * c);
            prompt = string.Format("Solve to dismiss:   {0} + {1} x {2} = ?", a, b, c);
            return ans.ToString(CultureInfo.InvariantCulture);
        }
    }
}
