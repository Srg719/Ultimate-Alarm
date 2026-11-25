using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UltimateAlarm.Core;

namespace UltimateAlarm.UI
{
    internal class DarkChromeForm : Form
    {
        private const int TitleHeight = 40;
        private const int ResizeGrab = 6;

        private readonly bool _resizable;
        private readonly bool _showMinimize;
        private readonly bool _showMaximize;
        private readonly bool _showClose;

        private Panel _titleHost;
        private Label _lblChromeTitle;
        private PictureBox _picChromeIcon;
        private Panel _btnCluster;
        private CaptionButton _btnMin;
        private CaptionButton _btnMax;
        private CaptionButton _btnClose;
        private Panel _leftChromePatch;
        private int _leftChromeWidth;
        private Color _leftChromeColor = UiTheme.BgShell;
        private bool _isRingingLogo;

        protected Panel ChromeTitleBar { get { return _titleHost; } }
        protected Panel ClientBody { get; private set; }
        protected Panel BodyHost { get { return ClientBody; } }

        public DarkChromeForm(bool resizable, bool showMinimize, bool showMaximize, bool showClose)
        {
            _resizable = resizable;
            _showMinimize = showMinimize;
            _showMaximize = showMaximize;
            _showClose = showClose;

            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = UiTheme.BgCanvas;
            ForeColor = UiTheme.TextPrimary;
            Font = UiTheme.FontBody;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = showMaximize;
            MinimizeBox = showMinimize;
            ControlBox = showClose;

            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            BuildChromeShell();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                const int WS_THICKFRAME = 0x00040000;
                const int WS_MINIMIZEBOX = 0x00020000;
                const int WS_MAXIMIZEBOX = 0x00010000;
                const int WS_CAPTION = 0x00C00000;
                const int WS_SYSMENU = 0x00080000;

                cp.Style |= WS_CAPTION;
                if (_showClose) cp.Style |= WS_SYSMENU;
                else cp.Style &= ~WS_SYSMENU;

                if (_resizable) cp.Style |= WS_THICKFRAME;
                else cp.Style &= ~WS_THICKFRAME;

                if (_showMinimize) cp.Style |= WS_MINIMIZEBOX;
                else cp.Style &= ~WS_MINIMIZEBOX;

                if (_showMaximize) cp.Style |= WS_MAXIMIZEBOX;
                else cp.Style &= ~WS_MAXIMIZEBOX;

                return cp;
            }
        }

        public void SetLeftTitleSidebarMatch(int sidebarWidth, Color sidebarColor, bool showTitleInSidebar)
        {
            _leftChromeWidth = sidebarWidth;
            _leftChromeColor = sidebarColor;
            if (_leftChromePatch != null)
            {
                _leftChromePatch.Width = sidebarWidth;
                _leftChromePatch.BackColor = sidebarColor;
                _leftChromePatch.Visible = sidebarWidth > 0;
            }
            if (_lblChromeTitle != null && _picChromeIcon != null)
            {
                if (showTitleInSidebar && sidebarWidth > 0)
                {
                    _picChromeIcon.Parent = _leftChromePatch;
                    _lblChromeTitle.Parent = _leftChromePatch;
                    _picChromeIcon.Location = new Point(16, 9);
                    _lblChromeTitle.Location = new Point(45, 0);
                    _lblChromeTitle.Width = Math.Max(80, sidebarWidth - 52);
                }
                else
                {
                    _picChromeIcon.Parent = _titleHost;
                    _lblChromeTitle.Parent = _titleHost;
                    _picChromeIcon.Location = new Point(14, 9);
                    _lblChromeTitle.Location = new Point(42, 0);
                    _lblChromeTitle.Width = 420;
                }
            }
            Invalidate(true);
        }

        public void SetTitleBarVisibleText(string title)
        {
            if (_lblChromeTitle != null)
            {
                _lblChromeTitle.Text = title;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (_lblChromeTitle != null && string.IsNullOrEmpty(_lblChromeTitle.Tag as string))
            {
                _lblChromeTitle.Text = Text;
            }
        }

        public void PinVisibleTitleText(string customTitle)
        {
            if (_lblChromeTitle != null)
            {
                _lblChromeTitle.Tag = "PINNED";
                _lblChromeTitle.Text = customTitle;
            }
        }

        protected void RefreshChromeIcon(Icon ico)
        {
            RefreshChromeLogo(_isRingingLogo);
        }

        protected void RefreshChromeLogo(bool ringing)
        {
            _isRingingLogo = ringing;
            if (_picChromeIcon != null)
            {
                try
                {
                    Image old = _picChromeIcon.Image;
                    _picChromeIcon.Image = UiTheme.CreateAppLogoBitmap(ringing, 22);
                    if (old != null) old.Dispose();
                }
                catch { }
            }
        }

        public virtual void ApplyCurrentTheme()
        {
            BackColor = UiTheme.BgCanvas;
            ForeColor = UiTheme.TextPrimary;
            if (_titleHost != null) _titleHost.BackColor = UiTheme.BgCanvas;
            if (_btnCluster != null) _btnCluster.BackColor = UiTheme.BgCanvas;
            if (_leftChromePatch != null)
            {
                _leftChromeColor = UiTheme.BgShell;
                _leftChromePatch.BackColor = UiTheme.BgShell;
            }
            if (_lblChromeTitle != null) _lblChromeTitle.ForeColor = UiTheme.TextPrimary;
            if (ClientBody != null) ClientBody.BackColor = UiTheme.BgCanvas;
            if (IsHandleCreated) ApplyDwmDarkChrome();
            Invalidate(true);
        }

        private void BuildChromeShell()
        {
            _titleHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = TitleHeight,
                BackColor = UiTheme.BgCanvas
            };
            _titleHost.MouseDown += OnChromeMouseDown;
            _titleHost.DoubleClick += OnChromeDoubleClick;
            _titleHost.Paint += delegate(object s, PaintEventArgs e)
            {
                if (WindowState != FormWindowState.Maximized)
                {
                    using (Pen p = new Pen(UiTheme.BgWindowBorder, 1f))
                    {
                        e.Graphics.DrawLine(p, 0, 0, _titleHost.Width - 1, 0);
                        if (!_leftChromePatch.Visible) e.Graphics.DrawLine(p, 0, 0, 0, _titleHost.Height);
                        e.Graphics.DrawLine(p, _titleHost.Width - 1, 0, _titleHost.Width - 1, _titleHost.Height);
                    }
                }
            };

            _leftChromePatch = new Panel
            {
                Dock = DockStyle.Left,
                Width = 0,
                BackColor = _leftChromeColor,
                Visible = false
            };
            _leftChromePatch.MouseDown += OnChromeMouseDown;
            _leftChromePatch.DoubleClick += OnChromeDoubleClick;
            _leftChromePatch.Paint += delegate(object s, PaintEventArgs e)
            {
                if (_leftChromePatch.Width > 0)
                {
                    using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
                    {
                        e.Graphics.DrawLine(p, _leftChromePatch.Width - 1, 0, _leftChromePatch.Width - 1, _leftChromePatch.Height);
                    }
                    if (WindowState != FormWindowState.Maximized)
                    {
                        using (Pen bp = new Pen(UiTheme.BgWindowBorder, 1f))
                        {
                            e.Graphics.DrawLine(bp, 0, 0, _leftChromePatch.Width - 1, 0);
                            e.Graphics.DrawLine(bp, 0, 0, 0, _leftChromePatch.Height);
                        }
                    }
                }
            };

            _picChromeIcon = new PictureBox
            {
                Size = new Size(22, 22),
                Location = new Point(14, 9),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent
            };
            _picChromeIcon.MouseDown += OnChromeMouseDown;
            _picChromeIcon.DoubleClick += OnChromeDoubleClick;

            _lblChromeTitle = new Label
            {
                Text = Text,
                Location = new Point(40, 0),
                Size = new Size(420, TitleHeight),
                Font = UiTheme.FontBodyBold,
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            _lblChromeTitle.MouseDown += OnChromeMouseDown;
            _lblChromeTitle.DoubleClick += OnChromeDoubleClick;

            _btnCluster = new Panel
            {
                Dock = DockStyle.Right,
                Width = (_showMinimize ? 44 : 0) + (_showMaximize ? 44 : 0) + (_showClose ? 46 : 0),
                BackColor = UiTheme.BgCanvas
            };

            int bx = 0;
            if (_showMinimize)
            {
                _btnMin = new CaptionButton(CaptionAction.Minimize)
                {
                    Location = new Point(bx, 0),
                    Size = new Size(44, TitleHeight)
                };
                _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
                _btnCluster.Controls.Add(_btnMin);
                bx += 44;
            }
            if (_showMaximize)
            {
                _btnMax = new CaptionButton(CaptionAction.Maximize)
                {
                    Location = new Point(bx, 0),
                    Size = new Size(44, TitleHeight)
                };
                _btnMax.Click += delegate { ToggleMaximizeRestore(); };
                _btnCluster.Controls.Add(_btnMax);
                bx += 44;
            }
            if (_showClose)
            {
                _btnClose = new CaptionButton(CaptionAction.Close)
                {
                    Location = new Point(bx, 0),
                    Size = new Size(46, TitleHeight)
                };
                _btnClose.Click += delegate { Close(); };
                _btnCluster.Controls.Add(_btnClose);
            }

            _titleHost.Controls.Add(_picChromeIcon);
            _titleHost.Controls.Add(_lblChromeTitle);
            _titleHost.Controls.Add(_leftChromePatch);
            _titleHost.Controls.Add(_btnCluster);

            ClientBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.BgCanvas,
                Padding = new Padding(1, 0, 1, 1)
            };
            ClientBody.Paint += delegate(object s, PaintEventArgs e)
            {
                if (WindowState != FormWindowState.Maximized)
                {
                    using (Pen bp = new Pen(UiTheme.BgWindowBorder, 1f))
                    {
                        e.Graphics.DrawLine(bp, 0, 0, 0, ClientBody.Height - 1);
                        e.Graphics.DrawLine(bp, ClientBody.Width - 1, 0, ClientBody.Width - 1, ClientBody.Height - 1);
                        e.Graphics.DrawLine(bp, 0, ClientBody.Height - 1, ClientBody.Width - 1, ClientBody.Height - 1);
                    }
                }
            };

            base.Controls.Add(ClientBody);
            base.Controls.Add(_titleHost);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _picChromeIcon != null && _picChromeIcon.Image != null)
            {
                try
                {
                    Image img = _picChromeIcon.Image;
                    _picChromeIcon.Image = null;
                    img.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }

        private void OnChromeMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            }
            else if (e.Button == MouseButtons.Left && e.Clicks == 2 && _showMaximize)
            {
                ToggleMaximizeRestore();
            }
        }

        private void OnChromeDoubleClick(object sender, EventArgs e)
        {
            if (_showMaximize)
            {
                ToggleMaximizeRestore();
            }
        }

        private void ToggleMaximizeRestore()
        {
            WindowState = (WindowState == FormWindowState.Maximized)
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            if (_btnMax != null)
            {
                _btnMax.IsRestored = (WindowState == FormWindowState.Maximized);
                _btnMax.Invalidate();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_btnMax != null)
            {
                bool zoomed = WindowState == FormWindowState.Maximized;
                if (_btnMax.IsRestored != zoomed)
                {
                    _btnMax.IsRestored = zoomed;
                    _btnMax.Invalidate();
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmDarkChrome();
            RefreshChromeIcon(Icon);
        }

        private void ApplyDwmDarkChrome()
        {
            try
            {
                int dark = 1;
                if (NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));
                }

                // Windows 11 caption/border color match (ignored cleanly on Windows 10)
                int captionColor = ColorTranslator.ToWin32(UiTheme.BgCanvas);
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                int borderColor = ColorTranslator.ToWin32(UiTheme.BgWindowBorder);
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

                // 1px bottom margin engages DWM shadow without painting top glass artifacts
                NativeMethods.MARGINS margins = new NativeMethods.MARGINS
                {
                    cxLeftWidth = 0,
                    cxRightWidth = 0,
                    cyTopHeight = 0,
                    cyBottomHeight = 1
                };
                NativeMethods.DwmExtendFrameIntoClientArea(Handle, ref margins);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                NativeMethods.NCCALCSIZE_PARAMS ncp = (NativeMethods.NCCALCSIZE_PARAMS)
                    Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.NCCALCSIZE_PARAMS));

                if (_resizable)
                {
                    int pad = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXPADDEDBORDER);
                    int borderX = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXFRAME) + pad;
                    int borderY = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYFRAME) + pad;

                    if (NativeMethods.IsZoomed(Handle))
                    {
                        ncp.rgrc0.Left += borderX;
                        ncp.rgrc0.Top += borderY;
                        ncp.rgrc0.Right -= borderX;
                        ncp.rgrc0.Bottom -= borderY;
                    }
                    else
                    {
                        // Keep invisible 7px left/right/bottom resize shadows, remove top caption completely
                        ncp.rgrc0.Left += borderX;
                        ncp.rgrc0.Right -= borderX;
                        ncp.rgrc0.Bottom -= borderY;
                    }
                }

                Marshal.StructureToPtr(ncp, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == NativeMethods.WM_NCACTIVATE)
            {
                m.Result = new IntPtr(1);
                return;
            }

            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == NativeMethods.HTCLIENT)
                {
                    int x = unchecked((short)(long)m.LParam);
                    int y = unchecked((short)((long)m.LParam >> 16));
                    Point pt = PointToClient(new Point(x, y));

                    if (_resizable && WindowState == FormWindowState.Normal)
                    {
                        bool top = pt.Y < ResizeGrab;
                        bool bottom = pt.Y >= ClientSize.Height - ResizeGrab;
                        bool left = pt.X < ResizeGrab;
                        bool right = pt.X >= ClientSize.Width - ResizeGrab;

                        if (top && left) { m.Result = (IntPtr)NativeMethods.HTTOPLEFT; return; }
                        if (top && right) { m.Result = (IntPtr)NativeMethods.HTTOPRIGHT; return; }
                        if (bottom && left) { m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT; return; }
                        if (bottom && right) { m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT; return; }
                        if (top) { m.Result = (IntPtr)NativeMethods.HTTOP; return; }
                        if (bottom) { m.Result = (IntPtr)NativeMethods.HTBOTTOM; return; }
                        if (left) { m.Result = (IntPtr)NativeMethods.HTLEFT; return; }
                        if (right) { m.Result = (IntPtr)NativeMethods.HTRIGHT; return; }
                    }

                    if (pt.Y >= 0 && pt.Y < TitleHeight && _titleHost != null && !_titleHost.IsDisposed)
                    {
                        Control child = GetChildAtPoint(pt, GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Transparent);
                        if (child == _titleHost || child == null)
                        {
                            Point titlePt = _titleHost.PointToClient(new Point(x, y));
                            Control titleChild = _titleHost.GetChildAtPoint(titlePt, GetChildAtPointSkip.Invisible);
                            if (titleChild == null || !(titleChild is CaptionButton || titleChild is ModernButton) && !IsInteractiveTitleControl(titleChild))
                            {
                                m.Result = (IntPtr)NativeMethods.HTCAPTION;
                                return;
                            }
                        }
                    }
                }
                return;
            }

            base.WndProc(ref m);
        }

        protected virtual bool IsInteractiveTitleControl(Control c)
        {
            if (c == null) return false;
            if (c is Button || c is CaptionButton || c is ModernButton) return true;
            if (c.HasChildren)
            {
                Point local = c.PointToClient(Cursor.Position);
                Control nested = c.GetChildAtPoint(local, GetChildAtPointSkip.Invisible);
                if (nested != null && nested != c) return IsInteractiveTitleControl(nested);
            }
            return false;
        }

        protected void SetTitleBarColor(Color color)
        {
            if (_titleHost != null)
            {
                _titleHost.BackColor = color;
                if (_btnCluster != null) _btnCluster.BackColor = color;
                _titleHost.Invalidate(true);
            }
        }

        private enum CaptionAction
        {
            Minimize,
            Maximize,
            Close
        }

        private sealed class CaptionButton : Control
        {
            private readonly CaptionAction _action;
            private bool _hover;
            private bool _pressed;

            public bool IsRestored { get; set; }

            public CaptionButton(CaptionAction action)
            {
                _action = action;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;

                Color baseBg = (Parent != null && Parent.BackColor != Color.Transparent)
                    ? Parent.BackColor
                    : ((Parent != null && Parent.Parent != null) ? Parent.Parent.BackColor : UiTheme.BgCanvas);
                Color bg = baseBg;
                Color fg = UiTheme.TextSecondary;
                if (_action == CaptionAction.Close)
                {
                    if (_pressed) { bg = Color.FromArgb(196, 43, 28); fg = Color.White; }
                    else if (_hover) { bg = Color.FromArgb(232, 17, 35); fg = Color.White; }
                }
                else
                {
                    if (_pressed) { bg = UiTheme.BgElevated; fg = UiTheme.TextPrimary; }
                    else if (_hover) { bg = UiTheme.BgSurfaceHover; fg = UiTheme.TextPrimary; }
                }

                // Clear opaquely before AntiAlias so x=0 never anti-aliases against underlying surface
                g.Clear(bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int cx = Width / 2;
                int cy = Height / 2;
                using (Pen p = new Pen(fg, 1.25f))
                {
                    if (_action == CaptionAction.Minimize)
                    {
                        g.SmoothingMode = SmoothingMode.None;
                        g.DrawLine(p, cx - 5, cy, cx + 5, cy);
                    }
                    else if (_action == CaptionAction.Maximize)
                    {
                        g.SmoothingMode = SmoothingMode.None;
                        if (IsRestored)
                        {
                            g.DrawRectangle(p, cx - 3, cy - 5, 8, 8);
                            using (SolidBrush fill = new SolidBrush(bg))
                            {
                                g.FillRectangle(fill, cx - 5, cy - 3, 8, 8);
                            }
                            g.DrawRectangle(p, cx - 5, cy - 3, 8, 8);
                        }
                        else
                        {
                            g.DrawRectangle(p, cx - 5, cy - 5, 10, 10);
                        }
                    }
                    else
                    {
                        g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                    }
                }
            }
        }
    }
}
