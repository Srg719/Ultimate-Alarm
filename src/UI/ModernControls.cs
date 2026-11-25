using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace UltimateAlarm.UI
{
    internal enum ButtonTone
    {
        Primary,
        Secondary,
        Success,
        Danger,
        Ghost,
        DangerGhost
    }

    internal sealed class ModernButton : Button
    {
        private bool _hover;
        private bool _pressed;
        private ButtonTone _tone = ButtonTone.Secondary;
        private UiGlyph _glyph = UiGlyph.None;

        public ButtonTone Tone
        {
            get { return _tone; }
            set { _tone = value; Invalidate(); }
        }

        public UiGlyph Glyph
        {
            get { return _glyph; }
            set { _glyph = value; Invalidate(); }
        }

        public int CornerRadius { get; set; }

        public ModernButton()
        {
            CornerRadius = 6;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBodyBold;
            ForeColor = UiTheme.TextPrimary;
            Height = 32;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = Parent != null ? Parent.BackColor : UiTheme.BgCanvas;
            g.Clear(parentBg);

            Color fill;
            Color border = Color.Transparent;
            Color textCol = UiTheme.TextPrimary;

            switch (_tone)
            {
                case ButtonTone.Primary:
                    fill = _pressed ? UiTheme.AccentPressed : (_hover ? UiTheme.AccentHover : UiTheme.AccentPrimary);
                    border = UiTheme.IsNeon ? Color.FromArgb(140, UiTheme.AccentHover.R, UiTheme.AccentHover.G, UiTheme.AccentHover.B) : Color.Transparent;
                    textCol = UiTheme.TextOnAccent;
                    break;
                case ButtonTone.Success:
                    fill = _pressed ? UiTheme.SuccessBtnPressed : (_hover ? UiTheme.SuccessBtnHover : UiTheme.SuccessBtnFill);
                    border = UiTheme.IsNeon ? Color.FromArgb(120, UiTheme.SuccessGreen.R, UiTheme.SuccessGreen.G, UiTheme.SuccessGreen.B) : Color.Transparent;
                    textCol = UiTheme.TextOnSuccess;
                    break;
                case ButtonTone.Danger:
                    fill = _pressed ? UiTheme.DangerBtnPressed : (_hover ? UiTheme.DangerBtnHover : UiTheme.DangerBtnFill);
                    border = UiTheme.IsNeon ? Color.FromArgb(120, UiTheme.DangerRed.R, UiTheme.DangerRed.G, UiTheme.DangerRed.B) : Color.Transparent;
                    textCol = Color.White;
                    break;
                case ButtonTone.Ghost:
                    fill = _pressed ? UiTheme.BgElevated : (_hover ? UiTheme.BgSurfaceHover : parentBg);
                    border = _hover ? UiTheme.BorderSubtle : Color.Transparent;
                    textCol = _hover ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                    break;
                case ButtonTone.DangerGhost:
                    fill = _pressed ? UiTheme.DangerDark : (_hover ? Color.FromArgb(Math.Min(255, UiTheme.DangerDark.R + 12), UiTheme.DangerDark.G, UiTheme.DangerDark.B) : parentBg);
                    border = _hover ? Color.FromArgb(130, UiTheme.DangerRed.R, UiTheme.DangerRed.G, UiTheme.DangerRed.B) : Color.Transparent;
                    textCol = _hover ? Color.FromArgb(255, 165, 175) : UiTheme.TextMuted;
                    break;
                default:
                    fill = _pressed ? UiTheme.BgElevated : (_hover ? UiTheme.BgSecondaryBtnHover : UiTheme.BgSecondaryBtn);
                    border = _hover ? UiTheme.BorderStrong : UiTheme.BorderSubtle;
                    break;
            }

            if (!Enabled)
            {
                fill = UiTheme.BgDisabled;
                border = UiTheme.BorderSubtle;
                textCol = UiTheme.TextMuted;
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, CornerRadius))
            using (SolidBrush br = new SolidBrush(fill))
            {
                g.FillPath(br, path);
                if (border != Color.Transparent)
                {
                    using (Pen p = new Pen(border, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
            }

            string txt = Text ?? "";
            if (_glyph != UiGlyph.None)
            {
                int iconSize = 14;
                if (string.IsNullOrEmpty(txt))
                {
                    Rectangle iconRect = new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize);
                    UiIcons.DrawGlyph(g, _glyph, iconRect, textCol, 1.6f);
                }
                else
                {
                    Size textSize = TextRenderer.MeasureText(g, txt, Font, new Size(Width, Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int gap = 6;
                    int totalW = iconSize + gap + textSize.Width;
                    int startX = Math.Max(8, (Width - totalW) / 2);
                    Rectangle iconRect = new Rectangle(startX, (Height - iconSize) / 2, iconSize, iconSize);
                    UiIcons.DrawGlyph(g, _glyph, iconRect, textCol, 1.6f);
                    Rectangle textRect = new Rectangle(startX + iconSize + gap, 0, Width - (startX + iconSize + gap) - 6, Height);
                    TextRenderer.DrawText(g, txt, Font, textRect, textCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                TextRenderer.DrawText(g, txt, Font, rect, textCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class ModernToggleSwitch : Control
    {
        private bool _checked;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public void SetCheckedSilent(bool value)
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
            }
        }

        public ModernToggleSwitch()
        {
            Size = new Size(42, 22);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            Rectangle track = new Rectangle(1, 1, Width - 3, Height - 3);
            Color trackColor = !Enabled ? UiTheme.BgDisabled
                : (_checked ? (_hover ? UiTheme.SuccessBtnHover : UiTheme.SuccessGreen)
                            : (_hover ? UiTheme.BgToggleOffHover : UiTheme.BgToggleOff));

            using (GraphicsPath path = UiTheme.CreateRoundRect(track, track.Height / 2))
            using (SolidBrush br = new SolidBrush(trackColor))
            {
                g.FillPath(br, path);
            }

            int knobSize = track.Height - 4;
            int knobX = _checked ? (track.Right - knobSize - 2) : (track.Left + 2);
            int knobY = track.Top + 2;
            Color knobColor = !Enabled ? UiTheme.TextMuted : (_checked && UiTheme.IsNeon ? UiTheme.TextOnSuccess : Color.White);
            using (SolidBrush knobBr = new SolidBrush(knobColor))
            {
                g.FillEllipse(knobBr, knobX, knobY, knobSize, knobSize);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        private Color _customCardColor = Color.Empty;
        private Color _customBorderColor = Color.Empty;

        public Color CardColor
        {
            get { return _customCardColor.IsEmpty ? UiTheme.BgSurface : _customCardColor; }
            set { _customCardColor = value; BackColor = CardColor; Invalidate(); }
        }

        public Color BorderColor
        {
            get { return _customBorderColor.IsEmpty ? UiTheme.BorderSubtle : _customBorderColor; }
            set { _customBorderColor = value; Invalidate(); }
        }

        public Color LeftAccentColor { get; set; }
        public int CornerRadius { get; set; }

        public CardPanel()
        {
            LeftAccentColor = Color.Transparent;
            CornerRadius = 8;
            BackColor = UiTheme.BgSurface;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void RefreshTheme()
        {
            BackColor = CardColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent != null ? Parent.BackColor : UiTheme.BgCanvas;
            g.Clear(parentBg);

            Color fillCol = CardColor;
            Color bdrCol = BorderColor;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, CornerRadius))
            using (SolidBrush br = new SolidBrush(fillCol))
            using (Pen pen = new Pen(bdrCol, 1f))
            {
                g.FillPath(br, path);
                if (LeftAccentColor != Color.Transparent)
                {
                    Region oldClip = g.Clip;
                    g.SetClip(path);
                    if (UiTheme.IsNeon)
                    {
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(26, LeftAccentColor.R, LeftAccentColor.G, LeftAccentColor.B)))
                        {
                            g.FillRectangle(glow, 0, 0, 12, Height);
                        }
                    }
                    using (SolidBrush acc = new SolidBrush(LeftAccentColor))
                    {
                        g.FillRectangle(acc, 0, 0, 3, Height);
                    }
                    g.Clip = oldClip;
                }
                g.DrawPath(pen, path);
            }
        }
    }

    internal sealed class StatusPill : Control
    {
        private Color _accent = UiTheme.TextMuted;
        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public bool Compact { get; set; }

        public StatusPill()
        {
            Height = 24;
            Width = 140;
            Font = UiTheme.FontSmallBold;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgCanvas);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Compact ? Color.FromArgb(20, _accent.R, _accent.G, _accent.B) : Color.FromArgb(24, _accent.R, _accent.G, _accent.B);
            Color border = Compact ? Color.FromArgb(64, _accent.R, _accent.G, _accent.B) : Color.FromArgb(72, _accent.R, _accent.G, _accent.B);

            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, rect.Height / 2))
            using (SolidBrush br = new SolidBrush(fill))
            using (Pen pen = new Pen(border, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            using (SolidBrush dot = new SolidBrush(_accent))
            {
                g.FillEllipse(dot, 9, (Height - 6) / 2, 6, 6);
            }

            Rectangle textRect = new Rectangle(20, 0, Width - 26, Height);
            TextRenderer.DrawText(g, Text ?? "", Font, textRect, UiTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class NavButton : Control
    {
        private bool _active;
        private bool _hover;
        private UiGlyph _glyph = UiGlyph.None;

        public bool Active
        {
            get { return _active; }
            set { _active = value; Invalidate(); }
        }

        public UiGlyph Glyph
        {
            get { return _glyph; }
            set { _glyph = value; Invalidate(); }
        }

        public NavButton()
        {
            Height = 38;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBody;
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

            if (_active || _hover)
            {
                Color bg = _active ? UiTheme.BgNavActive : UiTheme.BgNavHover;
                using (GraphicsPath path = UiTheme.CreateRoundRect(new Rectangle(10, 1, Width - 20, Height - 2), 6))
                using (SolidBrush br = new SolidBrush(bg))
                {
                    g.FillPath(br, path);
                    if (_active && UiTheme.IsNeon)
                    {
                        using (Pen glowPen = new Pen(Color.FromArgb(90, UiTheme.AccentPrimary.R, UiTheme.AccentPrimary.G, UiTheme.AccentPrimary.B), 1f))
                        {
                            g.DrawPath(glowPen, path);
                        }
                    }
                }
            }

            if (_active)
            {
                using (GraphicsPath bar = UiTheme.CreateRoundRect(new Rectangle(10, 9, 3, Height - 18), 2))
                using (SolidBrush br = new SolidBrush(UiTheme.AccentPrimary))
                {
                    g.FillPath(br, bar);
                }
            }

            Color fg = _active ? UiTheme.TextPrimary : (_hover ? UiTheme.TextPrimary : UiTheme.TextSecondary);
            Color iconCol = _active ? UiTheme.AccentPrimary : fg;

            int textX = 24;
            if (_glyph != UiGlyph.None)
            {
                UiIcons.DrawGlyph(g, _glyph, new Rectangle(22, (Height - 16) / 2, 16, 16), iconCol, 1.55f);
                textX = 48;
            }

            Font f = _active ? UiTheme.FontBodyBold : UiTheme.FontBody;
            TextRenderer.DrawText(g, Text ?? "", f, new Rectangle(textX, 0, Width - textX - 14, Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ModernCheckBox : Control
    {
        private bool _checked;
        private bool _hover;
        private string _subtitle;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public string Subtitle
        {
            get { return _subtitle; }
            set { _subtitle = value; Invalidate(); }
        }

        public Color BadgeColor { get; set; }
        public string BadgeText { get; set; }

        public void SetCheckedSilent(bool value)
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
            }
        }

        public ModernCheckBox()
        {
            Height = 24;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBody;
            ForeColor = UiTheme.TextPrimary;
            BadgeColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            int boxSize = 16;
            int boxY = string.IsNullOrEmpty(_subtitle) ? (Height - boxSize) / 2 : 3;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color boxFill = _checked
                ? (Enabled ? UiTheme.AccentPrimary : UiTheme.BgDisabled)
                : (_hover ? UiTheme.BgSurfaceHover : UiTheme.BgInput);
            Color boxBorder = _checked
                ? boxFill
                : (_hover || Focused ? UiTheme.AccentPrimary : UiTheme.BorderStrong);

            using (GraphicsPath path = UiTheme.CreateRoundRect(boxRect, 4))
            using (SolidBrush br = new SolidBrush(boxFill))
            using (Pen p = new Pen(boxBorder, 1.1f))
            {
                g.FillPath(br, path);
                if (!_checked) g.DrawPath(p, path);
            }

            if (_checked)
            {
                UiIcons.DrawGlyph(g, UiGlyph.Check, new Rectangle(boxRect.X + 2, boxRect.Y + 2, 12, 12), Enabled ? UiTheme.TextOnAccent : UiTheme.TextMuted, 1.9f);
            }

            Color fg = Enabled ? ForeColor : UiTheme.TextMuted;
            int textX = 26;
            if (string.IsNullOrEmpty(_subtitle))
            {
                int rightReserve = 0;
                if (!string.IsNullOrEmpty(BadgeText))
                {
                    Size bSz = TextRenderer.MeasureText(g, BadgeText, UiTheme.FontSmallBold, new Size(200, 20), TextFormatFlags.NoPadding);
                    rightReserve = bSz.Width + 24;
                    Size mainSz = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(Width - textX - rightReserve, Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int badgeX = Math.Min(Width - bSz.Width - 16, textX + mainSz.Width + 10);
                    Rectangle bRect = new Rectangle(badgeX, (Height - 18) / 2, bSz.Width + 12, 18);
                    Color bc = BadgeColor == Color.Transparent ? UiTheme.WarnAmber : BadgeColor;
                    using (GraphicsPath bp = UiTheme.CreateRoundRect(bRect, 4))
                    using (SolidBrush bbr = new SolidBrush(Color.FromArgb(26, bc.R, bc.G, bc.B)))
                    using (Pen bpen = new Pen(Color.FromArgb(90, bc.R, bc.G, bc.B), 1f))
                    {
                        g.FillPath(bbr, bp);
                        g.DrawPath(bpen, bp);
                    }
                    TextRenderer.DrawText(g, BadgeText, UiTheme.FontSmallBold, bRect, bc,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(textX, 0, Math.Max(40, Width - textX - rightReserve), Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(textX, 1, Width - textX - 4, 20), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, _subtitle, UiTheme.FontSmall, new Rectangle(textX, 20, Width - textX - 4, 18), UiTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class ModernRadioButton : Control
    {
        private bool _checked;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    if (_checked && Parent != null)
                    {
                        foreach (Control c in Parent.Controls)
                        {
                            ModernRadioButton rb = c as ModernRadioButton;
                            if (rb != null && rb != this && rb.Checked)
                            {
                                rb.Checked = false;
                            }
                        }
                    }
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernRadioButton()
        {
            Height = 24;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBodyBold;
            ForeColor = UiTheme.TextPrimary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            if (!Checked) Checked = true;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            int size = 16;
            int ry = (Height - size) / 2;
            Rectangle outer = new Rectangle(1, ry, size, size);

            Color ringCol = _checked ? UiTheme.AccentPrimary : (_hover ? UiTheme.TextSecondary : UiTheme.BorderStrong);
            using (SolidBrush bg = new SolidBrush(UiTheme.BgInput))
            using (Pen pen = new Pen(ringCol, 1.5f))
            {
                g.FillEllipse(bg, outer);
                g.DrawEllipse(pen, outer);
            }

            if (_checked)
            {
                using (SolidBrush dot = new SolidBrush(UiTheme.AccentPrimary))
                {
                    g.FillEllipse(dot, outer.X + 4, outer.Y + 4, 8, 8);
                }
            }

            Color fg = Enabled ? ForeColor : UiTheme.TextMuted;
            TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(26, 0, Width - 28, Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ModernSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 50;
        private bool _dragging;
        private bool _hover;

        public event EventHandler ValueChanged;
        public event EventHandler Scroll;

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernSlider()
        {
            Height = 28;
            Width = 240;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                _dragging = true;
                UpdateFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                UpdateFromX(e.X);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = e.Delta > 0 ? 5 : -5;
            int prev = _value;
            Value = _value + step;
            if (_value != prev && Scroll != null) Scroll(this, EventArgs.Empty);
            base.OnMouseWheel(e);
        }

        private void UpdateFromX(int x)
        {
            int pad = 9;
            int trackW = Math.Max(1, Width - pad * 2);
            double ratio = Math.Max(0.0, Math.Min(1.0, (x - pad) / (double)trackW));
            int next = _minimum + (int)Math.Round(ratio * (_maximum - _minimum));
            int prev = _value;
            Value = next;
            if (_value != prev && Scroll != null) Scroll(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            int pad = 9;
            int trackH = 4;
            int trackY = (Height - trackH) / 2;
            int trackW = Math.Max(10, Width - pad * 2);
            Rectangle trackRect = new Rectangle(pad, trackY, trackW, trackH);

            using (GraphicsPath trackPath = UiTheme.CreateRoundRect(trackRect, 2))
            using (SolidBrush br = new SolidBrush(UiTheme.BgElevated))
            {
                g.FillPath(br, trackPath);
            }

            double ratio = _maximum > _minimum ? (_value - _minimum) / (double)(_maximum - _minimum) : 0.0;
            int fillW = (int)Math.Round(trackW * ratio);
            if (fillW > 2)
            {
                Rectangle fillRect = new Rectangle(pad, trackY, fillW, trackH);
                using (GraphicsPath fillPath = UiTheme.CreateRoundRect(fillRect, 2))
                using (SolidBrush br = new SolidBrush(Enabled ? UiTheme.AccentPrimary : UiTheme.TextMuted))
                {
                    g.FillPath(br, fillPath);
                }
            }

            int thumbX = pad + fillW;
            int thumbR = (_hover || _dragging) ? 7 : 6;
            if (_hover || _dragging)
            {
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(45, UiTheme.AccentPrimary.R, UiTheme.AccentPrimary.G, UiTheme.AccentPrimary.B)))
                {
                    g.FillEllipse(halo, thumbX - 11, Height / 2 - 11, 22, 22);
                }
            }
            using (SolidBrush thumbBr = new SolidBrush(Enabled ? Color.White : UiTheme.TextMuted))
            using (Pen thumbPen = new Pen(UiTheme.AccentPrimary, 1.5f))
            {
                g.FillEllipse(thumbBr, thumbX - thumbR, Height / 2 - thumbR, thumbR * 2, thumbR * 2);
                g.DrawEllipse(thumbPen, thumbX - thumbR, Height / 2 - thumbR, thumbR * 2, thumbR * 2);
            }
        }
    }

    internal sealed class ModernNumeric : Control
    {
        private readonly TextBox _tb;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private bool _focused;
        private int _hoverBtn = 0; // 0 none, -1 dec, 1 inc
        private string _suffix = "";

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; }
        }

        public string Suffix
        {
            get { return _suffix; }
            set { _suffix = value ?? ""; LayoutInner(); Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                bool changed = _value != clamped;
                _value = clamped;
                string txt = _value.ToString(CultureInfo.InvariantCulture);
                if (_tb.Text != txt) _tb.Text = txt;
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public ModernNumeric()
        {
            Size = new Size(92, 32);
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.BgInput,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBodyBold,
                TextAlign = HorizontalAlignment.Center,
                Text = "0"
            };
            _tb.GotFocus += delegate { _focused = true; Invalidate(); };
            _tb.LostFocus += delegate { _focused = false; CommitText(); Invalidate(); };
            _tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Value = _value + 1; e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { Value = _value - 1; e.Handled = true; }
            };
            Controls.Add(_tb);
            LayoutInner();
        }

        public void RefreshTheme()
        {
            if (_tb != null)
            {
                _tb.BackColor = UiTheme.BgInput;
                _tb.ForeColor = UiTheme.TextPrimary;
            }
            Invalidate();
        }

        private void CommitText()
        {
            int parsed;
            if (int.TryParse((_tb.Text ?? "").Trim(), out parsed))
            {
                Value = parsed;
            }
            else
            {
                _tb.Text = _value.ToString(CultureInfo.InvariantCulture);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            if (_tb == null) return;
            int btnZone = 22;
            int suffixW = string.IsNullOrEmpty(_suffix) ? 0 : 16;
            int textW = Math.Max(20, Width - btnZone - suffixW - 8);
            _tb.Location = new Point(6, Math.Max(4, (Height - _tb.Height) / 2));
            _tb.Width = textW;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int prev = _hoverBtn;
            if (e.X >= Width - 23)
            {
                _hoverBtn = (e.Y < Height / 2) ? 1 : -1;
                Cursor = Cursors.Hand;
            }
            else
            {
                _hoverBtn = 0;
                Cursor = Cursors.Default;
            }
            if (prev != _hoverBtn) Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverBtn = 0;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X >= Width - 23)
            {
                CommitText();
                Value = (e.Y < Height / 2) ? (_value + 1) : (_value - 1);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = _value + (e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
            using (SolidBrush br = new SolidBrush(UiTheme.BgInput))
            using (Pen p = new Pen(_focused ? UiTheme.AccentPrimary : UiTheme.BorderSubtle, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }

            if (!string.IsNullOrEmpty(_suffix))
            {
                Rectangle sRect = new Rectangle(Width - 24 - 16, 0, 16, Height);
                TextRenderer.DrawText(g, _suffix, UiTheme.FontSmallBold, sRect, UiTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            int bx = Width - 23;
            using (Pen div = new Pen(UiTheme.BorderSubtle, 1f))
            {
                g.DrawLine(div, bx, 4, bx, Height - 5);
                g.DrawLine(div, bx + 2, Height / 2, Width - 4, Height / 2);
            }

            // Up & down chevrons
            Color upCol = _hoverBtn == 1 ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            Color dnCol = _hoverBtn == -1 ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            using (Pen upP = new Pen(upCol, 1.4f))
            using (Pen dnP = new Pen(dnCol, 1.4f))
            {
                upP.StartCap = LineCap.Round; upP.EndCap = LineCap.Round;
                dnP.StartCap = LineCap.Round; dnP.EndCap = LineCap.Round;
                int cx = bx + 11;
                int uy = Height / 4 + 1;
                g.DrawLine(upP, cx - 3, uy + 2, cx, uy - 1);
                g.DrawLine(upP, cx, uy - 1, cx + 3, uy + 2);
                int dy = (Height * 3) / 4 - 1;
                g.DrawLine(dnP, cx - 3, dy - 2, cx, dy + 1);
                g.DrawLine(dnP, cx, dy + 1, cx + 3, dy - 2);
            }
        }
    }

    internal sealed class ModernTextBox : Control
    {
        private readonly TextBox _tb;
        private bool _focused;

        public TextBox Inner { get { return _tb; } }

        public override string Text
        {
            get { return _tb.Text; }
            set { _tb.Text = value ?? ""; }
        }

        public bool UseSystemPasswordChar
        {
            get { return _tb.UseSystemPasswordChar; }
            set { _tb.UseSystemPasswordChar = value; }
        }

        public bool ReadOnly
        {
            get { return _tb.ReadOnly; }
            set { _tb.ReadOnly = value; }
        }

        public bool Multiline
        {
            get { return _tb.Multiline; }
            set { _tb.Multiline = value; LayoutInner(); }
        }

        public ScrollBars ScrollBars
        {
            get { return _tb.ScrollBars; }
            set { _tb.ScrollBars = value; }
        }

        public bool WordWrap
        {
            get { return _tb.WordWrap; }
            set { _tb.WordWrap = value; }
        }

        public HorizontalAlignment TextAlign
        {
            get { return _tb.TextAlign; }
            set { _tb.TextAlign = value; }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (_tb != null) { _tb.Font = value; LayoutInner(); }
            }
        }

        public ModernTextBox()
        {
            Size = new Size(220, 32);
            BackColor = UiTheme.BgInput;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.BgInput,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.FontBody
            };
            UiTheme.ApplyDarkScrollbars(_tb);
            _tb.GotFocus += delegate { _focused = true; Invalidate(); };
            _tb.LostFocus += delegate { _focused = false; Invalidate(); };
            _tb.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            Controls.Add(_tb);
            LayoutInner();
        }

        public void RefreshTheme()
        {
            BackColor = UiTheme.BgInput;
            if (_tb != null)
            {
                _tb.BackColor = UiTheme.BgInput;
                _tb.ForeColor = UiTheme.TextPrimary;
            }
            Invalidate();
        }

        public void Clear() { _tb.Clear(); }
        public void AppendText(string text) { _tb.AppendText(text); }
        public void SelectAll() { _tb.SelectAll(); }
        public void ScrollToEnd()
        {
            _tb.SelectionStart = _tb.TextLength;
            _tb.ScrollToCaret();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        protected override void OnClick(EventArgs e)
        {
            _tb.Focus();
            base.OnClick(e);
        }

        private void LayoutInner()
        {
            if (_tb == null) return;
            if (_tb.Multiline)
            {
                _tb.Location = new Point(10, 8);
                _tb.Size = new Size(Math.Max(20, Width - 20), Math.Max(16, Height - 16));
            }
            else
            {
                int y = Math.Max(4, (Height - _tb.Height) / 2);
                _tb.Location = new Point(10, y);
                _tb.Width = Math.Max(20, Width - 20);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgCanvas);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
            using (SolidBrush br = new SolidBrush(UiTheme.BgInput))
            using (Pen p = new Pen(_focused ? UiTheme.AccentPrimary : UiTheme.BorderSubtle, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }
        }
    }

    internal sealed class ModernComboBox : Control
    {
        private readonly List<object> _items = new List<object>();
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _open;

        public event EventHandler SelectedIndexChanged;

        public ModernComboBox()
        {
            Height = 32;
            Width = 240;
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBody;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public int ItemCount { get { return _items.Count; } }

        public void ClearItems()
        {
            _items.Clear();
            _selectedIndex = -1;
            Invalidate();
        }

        public int AddItem(object item)
        {
            _items.Add(item);
            if (_selectedIndex < 0 && _items.Count == 1) _selectedIndex = 0;
            Invalidate();
            return _items.Count - 1;
        }

        public void AddItems(params object[] items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++) _items.Add(items[i]);
            if (_selectedIndex < 0 && _items.Count > 0) _selectedIndex = 0;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int next = (value >= -1 && value < _items.Count) ? value : -1;
                if (_selectedIndex != next)
                {
                    _selectedIndex = next;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }
        }

        public object SelectedItem
        {
            get { return (_selectedIndex >= 0 && _selectedIndex < _items.Count) ? _items[_selectedIndex] : null; }
            set
            {
                if (value == null) return;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (Equals(_items[i], value) || string.Equals(_items[i].ToString(), value.ToString(), StringComparison.Ordinal))
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled || _items.Count == 0) return;
            Focus();
            ShowPopupMenu();
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled || _items.Count == 0) return;
            if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, _selectedIndex - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { ShowPopupMenu(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void ShowPopupMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.AutoSize = true;
            int popW = Math.Max(Width, 210);
            menu.MaximumSize = new Size(popW, 420);
            menu.BackColor = UiTheme.BgMenu;
            menu.ForeColor = UiTheme.TextPrimary;
            menu.Font = Font;
            menu.Renderer = new DarkMenuRenderer();

            int itemH = 29;
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                string label = _items[i] != null ? _items[i].ToString() : "";
                ToolStripMenuItem mi = new ToolStripMenuItem(label)
                {
                    AutoSize = false,
                    Size = new Size(popW - 6, itemH),
                    Checked = (i == _selectedIndex),
                    ForeColor = (i == _selectedIndex) ? UiTheme.AccentPrimary : UiTheme.TextPrimary
                };
                mi.Click += delegate { SelectedIndex = idx; };
                menu.Items.Add(mi);
            }

            _open = true;
            Invalidate();
            menu.Closed += delegate
            {
                _open = false;
                if (!IsDisposed) Invalidate();
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!menu.IsDisposed) menu.Dispose();
                });
            };
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgSurface);

            Color fill = !Enabled ? UiTheme.BgDisabled : (_hover ? UiTheme.BgSurfaceHover : UiTheme.BgInput);
            Color border = (_open || Focused) ? UiTheme.AccentPrimary : (_hover ? UiTheme.BorderStrong : UiTheme.BorderSubtle);
            Color fg = Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
            using (SolidBrush br = new SolidBrush(fill))
            using (Pen pen = new Pen(border, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            string text = SelectedItem != null ? SelectedItem.ToString() : "";
            Rectangle textRect = new Rectangle(10, 0, Math.Max(20, Width - 34), Height);
            TextRenderer.DrawText(g, text, Font, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            UiIcons.DrawGlyph(g, UiGlyph.ChevronDown, new Rectangle(Width - 24, (Height - 12) / 2, 12, 12),
                Enabled ? UiTheme.TextSecondary : UiTheme.TextMuted, 1.5f);
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.BgMenu))
                {
                    e.Graphics.FillRectangle(br, e.AffectedBounds);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BorderStrong, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
                }
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
                bool isSel = e.Item.Selected;
                bool isChk = mi != null && mi.Checked;
                Color bg = isSel ? UiTheme.BgMenuHover : (isChk ? UiTheme.AccentTint : UiTheme.BgMenu);
                Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                using (GraphicsPath path = UiTheme.CreateRoundRect(r, 4))
                using (SolidBrush br = new SolidBrush(bg))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(br, path);
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
                bool isChk = mi != null && mi.Checked;
                Color fg = isChk ? UiTheme.AccentPrimary : UiTheme.TextPrimary;
                Rectangle rect = new Rectangle(10, 0, e.Item.Width - 16, e.Item.Height);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, rect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class ModernTimePicker : Control
    {
        private int _hour = 7;
        private int _minute = 0;
        private int _activeField = 0; // 0 = hour, 1 = minute
        private string _digitBuffer = "";

        public int Hour
        {
            get { return _hour; }
            set { _hour = ((value % 24) + 24) % 24; Invalidate(); }
        }

        public int Minute
        {
            get { return _minute; }
            set { _minute = ((value % 60) + 60) % 60; Invalidate(); }
        }

        public DateTime Value
        {
            get { return new DateTime(2026, 1, 1, _hour, _minute, 0); }
            set { _hour = value.Hour; _minute = value.Minute; Invalidate(); }
        }

        public ModernTimePicker()
        {
            Size = new Size(128, 36);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Regular);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _digitBuffer = ""; Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _digitBuffer = "";
            int mid = Width - 26;
            if (e.X < mid / 2) _activeField = 0;
            else if (e.X < mid) _activeField = 1;
            else
            {
                StepActive(e.Y < Height / 2 ? 1 : -1);
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            StepActive(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { _activeField = 0; _digitBuffer = ""; Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.OemSemicolon) { _activeField = 1; _digitBuffer = ""; Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { StepActive(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { StepActive(-1); e.Handled = true; }
            else if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) { HandleDigit((char)('0' + (e.KeyCode - Keys.D0))); e.Handled = true; }
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) { HandleDigit((char)('0' + (e.KeyCode - Keys.NumPad0))); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void HandleDigit(char c)
        {
            _digitBuffer += c;
            int val = int.Parse(_digitBuffer, CultureInfo.InvariantCulture);
            if (_activeField == 0)
            {
                if (_digitBuffer.Length == 1)
                {
                    Hour = val;
                    if (val > 2) { _activeField = 1; _digitBuffer = ""; }
                }
                else
                {
                    Hour = Math.Min(23, val);
                    _activeField = 1;
                    _digitBuffer = "";
                }
            }
            else
            {
                if (_digitBuffer.Length == 1)
                {
                    Minute = val;
                    if (val > 5) _digitBuffer = "";
                }
                else
                {
                    Minute = Math.Min(59, val);
                    _digitBuffer = "";
                }
            }
            Invalidate();
        }

        private void StepActive(int delta)
        {
            if (_activeField == 0) Hour += delta;
            else Minute += delta;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgCanvas);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
            using (SolidBrush br = new SolidBrush(UiTheme.BgInput))
            using (Pen p = new Pen(Focused ? UiTheme.AccentPrimary : UiTheme.BorderSubtle, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }

            int contentW = Width - 26;
            int segW = (contentW - 14) / 2;
            Rectangle hrRect = new Rectangle(6, 5, segW, Height - 10);
            Rectangle mnRect = new Rectangle(6 + segW + 10, 5, segW, Height - 10);
            Rectangle colonRect = new Rectangle(6 + segW, 4, 10, Height - 10);

            if (Focused)
            {
                Rectangle activeRect = _activeField == 0 ? hrRect : mnRect;
                using (GraphicsPath ap = UiTheme.CreateRoundRect(activeRect, 4))
                using (SolidBrush abr = new SolidBrush(UiTheme.AccentTint))
                {
                    g.FillPath(abr, ap);
                }
            }

            TextRenderer.DrawText(g, _hour.ToString("00", CultureInfo.InvariantCulture), Font, hrRect, UiTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, ":", Font, colonRect, UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _minute.ToString("00", CultureInfo.InvariantCulture), Font, mnRect, UiTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int bx = Width - 23;
            using (Pen div = new Pen(UiTheme.BorderSubtle, 1f))
            {
                g.DrawLine(div, bx, 5, bx, Height - 6);
                g.DrawLine(div, bx + 2, Height / 2, Width - 4, Height / 2);
            }
            using (Pen cp = new Pen(UiTheme.TextSecondary, 1.4f))
            {
                cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round;
                int cx = bx + 11;
                int uy = Height / 4 + 1;
                g.DrawLine(cp, cx - 3, uy + 2, cx, uy - 1);
                g.DrawLine(cp, cx, uy - 1, cx + 3, uy + 2);
                int dy = (Height * 3) / 4 - 1;
                g.DrawLine(cp, cx - 3, dy - 2, cx, dy + 1);
                g.DrawLine(cp, cx, dy + 1, cx + 3, dy - 2);
            }
        }
    }

    internal sealed class ModernDatePicker : Control
    {
        private DateTime _value = DateTime.Today;

        public DateTime Value
        {
            get { return _value; }
            set { _value = value.Date; Invalidate(); }
        }

        public ModernDatePicker()
        {
            Size = new Size(146, 36);
            Cursor = Cursors.Hand;
            Font = UiTheme.FontBodyBold;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (e.X < 28) Value = _value.AddDays(-1);
            else if (e.X > Width - 28) Value = _value.AddDays(1);
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Enabled) return;
            Value = _value.AddDays(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.BgCanvas);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
            using (SolidBrush br = new SolidBrush(Enabled ? UiTheme.BgInput : UiTheme.BgDisabled))
            using (Pen p = new Pen(UiTheme.BorderSubtle, 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }

            Color fg = Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted;
            Color arrowCol = Enabled ? UiTheme.TextSecondary : UiTheme.TextMuted;

            UiIcons.DrawGlyph(g, UiGlyph.ChevronLeft, new Rectangle(7, (Height - 12) / 2, 12, 12), arrowCol, 1.5f);
            UiIcons.DrawGlyph(g, UiGlyph.ChevronRight, new Rectangle(Width - 19, (Height - 12) / 2, 12, 12), arrowCol, 1.5f);

            string text = _value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);
            TextRenderer.DrawText(g, text, Font, new Rectangle(24, 0, Width - 48, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ThemeOptionButton : Control
    {
        private bool _active;
        private bool _hover;

        public readonly ThemePresetInfo Preset;

        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        public ThemeOptionButton(ThemePresetInfo preset)
        {
            Preset = preset;
            Height = 28;
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

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _active ? UiTheme.BgNavActive : (_hover ? UiTheme.BgNavHover : Color.Transparent);
            Color border = _active ? UiTheme.AccentPrimary : (_hover ? UiTheme.BorderSubtle : Color.Transparent);

            if (fill != Color.Transparent || border != Color.Transparent)
            {
                using (GraphicsPath path = UiTheme.CreateRoundRect(rect, 6))
                {
                    if (fill != Color.Transparent)
                    {
                        using (SolidBrush br = new SolidBrush(fill)) g.FillPath(br, path);
                    }
                    if (border != Color.Transparent)
                    {
                        using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                    }
                }
            }

            // Dual color swatch preview (surface + accent)
            Rectangle sw1 = new Rectangle(9, (Height - 11) / 2, 11, 11);
            Rectangle sw2 = new Rectangle(16, (Height - 11) / 2, 11, 11);
            using (SolidBrush b1 = new SolidBrush(Preset.SwatchBg))
            using (Pen p1 = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
            using (SolidBrush b2 = new SolidBrush(Preset.SwatchAccent))
            {
                g.FillEllipse(b1, sw1);
                g.DrawEllipse(p1, sw1);
                g.FillEllipse(b2, sw2);
            }

            Font nameFont = _active ? UiTheme.FontSmallBold : UiTheme.FontSmall;
            Color nameCol = _active ? UiTheme.TextPrimary : (_hover ? UiTheme.TextPrimary : UiTheme.TextSecondary);
            TextRenderer.DrawText(g, Preset.DisplayName, nameFont, new Rectangle(34, 0, Width - 82, Height), nameCol,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Color tagCol = _active ? UiTheme.AccentPrimary : UiTheme.TextMuted;
            TextRenderer.DrawText(g, Preset.Tagline, UiTheme.FontSmall, new Rectangle(Width - 52, 0, 44, Height), tagCol,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
