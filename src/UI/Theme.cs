using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using UltimateAlarm.Core;

namespace UltimateAlarm.UI
{
    internal sealed class ThemePresetInfo
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Tagline;
        public readonly Color SwatchBg;
        public readonly Color SwatchAccent;

        public ThemePresetInfo(string id, string displayName, string tagline, Color swatchBg, Color swatchAccent)
        {
            Id = id;
            DisplayName = displayName;
            Tagline = tagline;
            SwatchBg = swatchBg;
            SwatchAccent = swatchAccent;
        }
    }

    internal static class UiTheme
    {
        public static readonly ThemePresetInfo[] Themes = new ThemePresetInfo[]
        {
            new ThemePresetInfo("Default", "Default", "Slate", Color.FromArgb(29, 33, 43), Color.FromArgb(79, 139, 255)),
            new ThemePresetInfo("MoreDark", "More Dark", "Carbon", Color.FromArgb(19, 22, 28), Color.FromArgb(59, 130, 246)),
            new ThemePresetInfo("UltimateDark", "Ultimate Dark", "OLED", Color.FromArgb(4, 4, 6), Color.FromArgb(62, 112, 232)),
            new ThemePresetInfo("Neon", "Neon", "Cyber", Color.FromArgb(17, 19, 40), Color.FromArgb(0, 229, 255))
        };

        public static string CurrentThemeId { get; private set; }
        public static bool IsNeon { get; private set; }

        // Dynamic Layered Surfaces
        public static Color BgShell;
        public static Color BgCanvas;
        public static Color BgSurface;
        public static Color BgSurfaceHover;
        public static Color BgElevated;
        public static Color BgInput;
        public static Color BgWindowBorder;
        public static Color BgHealthCard;
        public static Color BgSecondaryBtn;
        public static Color BgSecondaryBtnHover;
        public static Color BgDisabled;
        public static Color BgMenu;
        public static Color BgMenuHover;
        public static Color BgNavActive;
        public static Color BgNavHover;
        public static Color BgToggleOff;
        public static Color BgToggleOffHover;

        public static Color BorderSubtle;
        public static Color BorderStrong;

        public static Color TextPrimary;
        public static Color TextSecondary;
        public static Color TextMuted;
        public static Color TextOnAccent;
        public static Color TextOnSuccess;

        public static Color AccentPrimary;
        public static Color AccentHover;
        public static Color AccentPressed;
        public static Color AccentTint;
        public static Color AccentSecondary;

        // Semantic colors
        public static Color SuccessGreen;
        public static Color SuccessBorder;
        public static Color SuccessBtnFill;
        public static Color SuccessBtnHover;
        public static Color SuccessBtnPressed;
        public static Color WarnAmber;
        public static Color DangerRed;
        public static Color DangerDark;
        public static Color DangerBtnFill;
        public static Color DangerBtnHover;
        public static Color DangerBtnPressed;
        public static Color IconGrad1;
        public static Color IconGrad2;

        // Legacy color property aliases so existing references always reflect the active theme
        public static Color BgSidebar { get { return BgShell; } }
        public static Color BgCard { get { return BgSurface; } }
        public static Color BgCardHover { get { return BgSurfaceHover; } }
        public static Color AccentBlue { get { return AccentPrimary; } }
        public static Color AccentBlueHover { get { return AccentHover; } }
        public static Color AccentCyan { get { return AccentPrimary; } }

        static UiTheme()
        {
            ApplyTheme("Default");
        }

        public static string NormalizeThemeId(string themeId)
        {
            if (string.IsNullOrEmpty(themeId)) return "Default";
            for (int i = 0; i < Themes.Length; i++)
            {
                if (string.Equals(Themes[i].Id, themeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Themes[i].DisplayName, themeId, StringComparison.OrdinalIgnoreCase))
                {
                    return Themes[i].Id;
                }
            }
            return "Default";
        }

        public static void ApplyTheme(string themeId)
        {
            string id = NormalizeThemeId(themeId);
            CurrentThemeId = id;
            IsNeon = string.Equals(id, "Neon", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(id, "MoreDark", StringComparison.OrdinalIgnoreCase))
            {
                // Deep Carbon Charcoal
                BgShell = Color.FromArgb(9, 10, 13);
                BgCanvas = Color.FromArgb(13, 15, 19);
                BgSurface = Color.FromArgb(19, 22, 28);
                BgSurfaceHover = Color.FromArgb(26, 30, 38);
                BgElevated = Color.FromArgb(33, 38, 48);
                BgInput = Color.FromArgb(8, 9, 12);
                BgWindowBorder = Color.FromArgb(33, 38, 48);
                BgHealthCard = Color.FromArgb(15, 17, 22);
                BgSecondaryBtn = Color.FromArgb(23, 27, 34);
                BgSecondaryBtnHover = Color.FromArgb(32, 37, 48);
                BgDisabled = Color.FromArgb(18, 21, 27);
                BgMenu = Color.FromArgb(17, 20, 25);
                BgMenuHover = Color.FromArgb(32, 39, 54);
                BgNavActive = Color.FromArgb(22, 28, 41);
                BgNavHover = Color.FromArgb(16, 19, 26);
                BgToggleOff = Color.FromArgb(38, 44, 56);
                BgToggleOffHover = Color.FromArgb(50, 58, 74);

                BorderSubtle = Color.FromArgb(29, 33, 43);
                BorderStrong = Color.FromArgb(46, 53, 69);

                TextPrimary = Color.FromArgb(234, 236, 239);
                TextSecondary = Color.FromArgb(141, 149, 165);
                TextMuted = Color.FromArgb(85, 92, 107);
                TextOnAccent = Color.White;
                TextOnSuccess = Color.White;

                AccentPrimary = Color.FromArgb(59, 130, 246);
                AccentHover = Color.FromArgb(96, 165, 250);
                AccentPressed = Color.FromArgb(37, 99, 235);
                AccentTint = Color.FromArgb(21, 32, 53);
                AccentSecondary = AccentPrimary;

                SuccessGreen = Color.FromArgb(16, 185, 129);
                SuccessBorder = Color.FromArgb(28, 75, 59);
                SuccessBtnFill = Color.FromArgb(16, 138, 76);
                SuccessBtnHover = Color.FromArgb(22, 160, 90);
                SuccessBtnPressed = Color.FromArgb(14, 114, 62);
                WarnAmber = Color.FromArgb(217, 155, 6);
                DangerRed = Color.FromArgb(229, 72, 77);
                DangerDark = Color.FromArgb(42, 14, 18);
                DangerBtnFill = Color.FromArgb(196, 46, 50);
                DangerBtnHover = Color.FromArgb(220, 62, 66);
                DangerBtnPressed = Color.FromArgb(166, 32, 36);
                IconGrad1 = Color.FromArgb(59, 130, 246);
                IconGrad2 = Color.FromArgb(29, 78, 216);
                return;
            }

            if (string.Equals(id, "UltimateDark", StringComparison.OrdinalIgnoreCase))
            {
                // Pure OLED Pitch Black (Zero-glare stealth dark)
                BgShell = Color.FromArgb(0, 0, 0);
                BgCanvas = Color.FromArgb(0, 0, 0);
                BgSurface = Color.FromArgb(9, 9, 12);
                BgSurfaceHover = Color.FromArgb(17, 17, 22);
                BgElevated = Color.FromArgb(24, 24, 31);
                BgInput = Color.FromArgb(3, 3, 5);
                BgWindowBorder = Color.FromArgb(34, 34, 42);
                BgHealthCard = Color.FromArgb(7, 7, 10);
                BgSecondaryBtn = Color.FromArgb(14, 14, 19);
                BgSecondaryBtnHover = Color.FromArgb(23, 23, 31);
                BgDisabled = Color.FromArgb(11, 11, 15);
                BgMenu = Color.FromArgb(9, 9, 13);
                BgMenuHover = Color.FromArgb(25, 26, 36);
                BgNavActive = Color.FromArgb(17, 18, 26);
                BgNavHover = Color.FromArgb(11, 11, 16);
                BgToggleOff = Color.FromArgb(28, 28, 36);
                BgToggleOffHover = Color.FromArgb(40, 40, 52);

                BorderSubtle = Color.FromArgb(26, 26, 34);
                BorderStrong = Color.FromArgb(46, 46, 58);

                TextPrimary = Color.FromArgb(228, 230, 235);
                TextSecondary = Color.FromArgb(132, 137, 150);
                TextMuted = Color.FromArgb(78, 82, 94);
                TextOnAccent = Color.White;
                TextOnSuccess = Color.White;

                AccentPrimary = Color.FromArgb(62, 112, 232);
                AccentHover = Color.FromArgb(85, 132, 242);
                AccentPressed = Color.FromArgb(44, 90, 201);
                AccentTint = Color.FromArgb(15, 23, 42);
                AccentSecondary = AccentPrimary;

                SuccessGreen = Color.FromArgb(22, 163, 74);
                SuccessBorder = Color.FromArgb(20, 62, 38);
                SuccessBtnFill = Color.FromArgb(18, 122, 58);
                SuccessBtnHover = Color.FromArgb(24, 146, 70);
                SuccessBtnPressed = Color.FromArgb(14, 98, 46);
                WarnAmber = Color.FromArgb(202, 138, 4);
                DangerRed = Color.FromArgb(220, 38, 38);
                DangerDark = Color.FromArgb(30, 8, 11);
                DangerBtnFill = Color.FromArgb(178, 30, 34);
                DangerBtnHover = Color.FromArgb(205, 44, 48);
                DangerBtnPressed = Color.FromArgb(148, 22, 26);
                IconGrad1 = Color.FromArgb(72, 132, 255);
                IconGrad2 = Color.FromArgb(148, 186, 255);
                return;
            }

            if (string.Equals(id, "Neon", StringComparison.OrdinalIgnoreCase))
            {
                // Cyber Neon (Deep Void Indigo + Electric Cyan / Neon Mint / Hot Magenta accents)
                BgShell = Color.FromArgb(6, 7, 17);
                BgCanvas = Color.FromArgb(10, 11, 24);
                BgSurface = Color.FromArgb(16, 18, 38);
                BgSurfaceHover = Color.FromArgb(23, 27, 54);
                BgElevated = Color.FromArgb(31, 36, 72);
                BgInput = Color.FromArgb(7, 8, 20);
                BgWindowBorder = Color.FromArgb(24, 112, 134);
                BgHealthCard = Color.FromArgb(12, 14, 30);
                BgSecondaryBtn = Color.FromArgb(20, 24, 50);
                BgSecondaryBtnHover = Color.FromArgb(28, 35, 72);
                BgDisabled = Color.FromArgb(14, 16, 32);
                BgMenu = Color.FromArgb(13, 15, 34);
                BgMenuHover = Color.FromArgb(22, 42, 72);
                BgNavActive = Color.FromArgb(14, 34, 56);
                BgNavHover = Color.FromArgb(16, 20, 44);
                BgToggleOff = Color.FromArgb(36, 42, 76);
                BgToggleOffHover = Color.FromArgb(48, 56, 98);

                BorderSubtle = Color.FromArgb(34, 44, 82);
                BorderStrong = Color.FromArgb(0, 184, 212);

                TextPrimary = Color.FromArgb(237, 246, 255);
                TextSecondary = Color.FromArgb(148, 168, 208);
                TextMuted = Color.FromArgb(92, 108, 148);
                TextOnAccent = Color.FromArgb(4, 16, 26);
                TextOnSuccess = Color.FromArgb(4, 22, 14);

                AccentPrimary = Color.FromArgb(0, 229, 255);
                AccentHover = Color.FromArgb(72, 238, 255);
                AccentPressed = Color.FromArgb(0, 184, 204);
                AccentTint = Color.FromArgb(10, 42, 62);
                AccentSecondary = Color.FromArgb(217, 70, 239);

                SuccessGreen = Color.FromArgb(0, 245, 155);
                SuccessBorder = Color.FromArgb(14, 110, 78);
                SuccessBtnFill = Color.FromArgb(0, 214, 134);
                SuccessBtnHover = Color.FromArgb(38, 238, 162);
                SuccessBtnPressed = Color.FromArgb(0, 176, 110);
                WarnAmber = Color.FromArgb(255, 190, 11);
                DangerRed = Color.FromArgb(255, 42, 109);
                DangerDark = Color.FromArgb(52, 10, 28);
                DangerBtnFill = Color.FromArgb(232, 32, 96);
                DangerBtnHover = Color.FromArgb(255, 58, 120);
                DangerBtnPressed = Color.FromArgb(192, 20, 76);
                IconGrad1 = Color.FromArgb(0, 229, 255);
                IconGrad2 = Color.FromArgb(168, 85, 247);
                return;
            }

            // Default: Obsidian Studio Slate
            BgShell = Color.FromArgb(17, 19, 24);
            BgCanvas = Color.FromArgb(22, 25, 32);
            BgSurface = Color.FromArgb(29, 33, 43);
            BgSurfaceHover = Color.FromArgb(36, 41, 53);
            BgElevated = Color.FromArgb(44, 50, 65);
            BgInput = Color.FromArgb(15, 17, 23);
            BgWindowBorder = Color.FromArgb(46, 52, 66);
            BgHealthCard = Color.FromArgb(22, 25, 33);
            BgSecondaryBtn = Color.FromArgb(34, 39, 51);
            BgSecondaryBtnHover = Color.FromArgb(42, 48, 63);
            BgDisabled = Color.FromArgb(28, 32, 42);
            BgMenu = Color.FromArgb(24, 28, 37);
            BgMenuHover = Color.FromArgb(44, 54, 76);
            BgNavActive = Color.FromArgb(30, 38, 56);
            BgNavHover = Color.FromArgb(24, 28, 38);
            BgToggleOff = Color.FromArgb(52, 60, 78);
            BgToggleOffHover = Color.FromArgb(65, 74, 94);

            BorderSubtle = Color.FromArgb(42, 48, 62);
            BorderStrong = Color.FromArgb(62, 70, 90);

            TextPrimary = Color.FromArgb(240, 242, 246);
            TextSecondary = Color.FromArgb(158, 165, 180);
            TextMuted = Color.FromArgb(102, 110, 128);
            TextOnAccent = Color.White;
            TextOnSuccess = Color.White;

            AccentPrimary = Color.FromArgb(79, 139, 255);
            AccentHover = Color.FromArgb(100, 154, 255);
            AccentPressed = Color.FromArgb(58, 118, 235);
            AccentTint = Color.FromArgb(28, 39, 62);
            AccentSecondary = AccentPrimary;

            SuccessGreen = Color.FromArgb(34, 197, 94);
            SuccessBorder = Color.FromArgb(40, 92, 65);
            SuccessBtnFill = Color.FromArgb(22, 155, 72);
            SuccessBtnHover = Color.FromArgb(26, 175, 82);
            SuccessBtnPressed = Color.FromArgb(21, 128, 61);
            WarnAmber = Color.FromArgb(234, 179, 8);
            DangerRed = Color.FromArgb(239, 68, 68);
            DangerDark = Color.FromArgb(56, 20, 24);
            DangerBtnFill = Color.FromArgb(215, 55, 55);
            DangerBtnHover = Color.FromArgb(235, 75, 75);
            DangerBtnPressed = Color.FromArgb(185, 28, 28);
            IconGrad1 = Color.FromArgb(64, 156, 255);
            IconGrad2 = Color.FromArgb(56, 189, 248);
        }

        // Typography
        public static readonly Font FontDisplay = new Font("Segoe UI Semibold", 22f, FontStyle.Regular);
        public static readonly Font FontClockHero = new Font("Segoe UI Semibold", 24f, FontStyle.Regular);
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Regular);
        public static readonly Font FontSubTitle = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular);
        public static readonly Font FontBody = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font FontBodyBold = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font FontSmallBold = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular);
        public static readonly Font FontMono = new Font("Consolas", 9.25f, FontStyle.Regular);
        public static readonly Font FontClock = new Font("Segoe UI Semibold", 13f, FontStyle.Regular);

        public static GraphicsPath CreateRoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int r = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int d = r * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void StyleTextBox(TextBox tb)
        {
            tb.BackColor = BgInput;
            tb.ForeColor = TextPrimary;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = FontBody;
        }

        public static void StyleComboBox(ComboBox cb)
        {
            cb.BackColor = BgInput;
            cb.ForeColor = TextPrimary;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Font = FontBody;
        }

        public static void StyleNumeric(NumericUpDown num)
        {
            num.BackColor = BgInput;
            num.ForeColor = TextPrimary;
            num.BorderStyle = BorderStyle.FixedSingle;
            num.Font = FontBody;
        }

        public static void ApplyDarkScrollbars(Control ctrl)
        {
            if (ctrl == null) return;
            Action apply = delegate
            {
                try { NativeMethods.SetWindowTheme(ctrl.Handle, "DarkMode_Explorer", null); } catch { }
            };
            if (ctrl.IsHandleCreated) apply();
            ctrl.HandleCreated += delegate { apply(); };
        }

        public static Bitmap CreateAppLogoBitmap(bool ringing, int targetSize)
        {
            return RenderLogoBitmap(
                targetSize,
                ringing,
                ringing ? Color.FromArgb(255, 64, 96) : IconGrad1,
                ringing ? Color.FromArgb(255, 148, 66) : IconGrad2);
        }

        public static Bitmap RenderLogoBitmap(int targetSize, bool ringing, Color accentPrimary, Color accentSecondary)
        {
            int size = Math.Max(16, targetSize);
            int ss = Math.Min(1024, Math.Max(128, size * 4));

            using (Bitmap hi = new Bitmap(ss, ss, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(hi))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                float s = ss / 100f;
                bool tiny = size <= 20;
                bool small = size <= 32;

                // 1. Deep metallic squircle architectural container
                float pad = tiny ? 2.0f : 3.2f;
                RectangleF outer = new RectangleF(pad * s, pad * s, (100f - pad * 2f) * s, (100f - pad * 2f) * s);
                float radius = 23f * s;

                Color bgTop = ringing ? Color.FromArgb(66, 14, 28) : Color.FromArgb(22, 34, 64);
                Color bgBot = ringing ? Color.FromArgb(26, 6, 12) : Color.FromArgb(10, 15, 29);
                Color glowCol = ringing
                    ? Color.FromArgb(135, 255, 48, 86)
                    : Color.FromArgb(108, accentPrimary.R, accentPrimary.G, accentPrimary.B);
                Color accentA = ringing ? Color.FromArgb(255, 76, 106) : accentPrimary;
                Color accentB = ringing ? Color.FromArgb(255, 154, 70) : accentSecondary;

                using (GraphicsPath sq = CreateRoundRectF(outer, radius))
                {
                    using (LinearGradientBrush baseBr = new LinearGradientBrush(outer, bgTop, bgBot, LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillPath(baseBr, sq);
                    }

                    Region oldClip = g.Clip;
                    g.SetClip(sq);

                    // Ambient radial spotlight behind chronometer core
                    using (GraphicsPath glowPath = new GraphicsPath())
                    {
                        glowPath.AddEllipse(6f * s, 10f * s, 88f * s, 88f * s);
                        using (PathGradientBrush pgb = new PathGradientBrush(glowPath))
                        {
                            pgb.CenterPoint = new PointF(50f * s, 54f * s);
                            pgb.CenterColor = glowCol;
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, glowCol.R, glowCol.G, glowCol.B) };
                            g.FillPath(pgb, glowPath);
                        }
                    }

                    // Seamless full-height soft top studio sheen (zero hard cutoff line)
                    using (LinearGradientBrush sheenBr = new LinearGradientBrush(outer, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Positions = new float[] { 0.0f, 0.46f, 1.0f };
                        cb.Colors = new Color[]
                        {
                            Color.FromArgb(28, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(0, 0, 0, 0)
                        };
                        sheenBr.InterpolationColors = cb;
                        g.FillPath(sheenBr, sq);
                    }

                    g.Clip = oldClip;

                    // Crisp metallic rim border
                    float borderW = (tiny ? 2.6f : (small ? 2.1f : 1.55f)) * s;
                    RectangleF inset = new RectangleF(outer.X + borderW * 0.5f, outer.Y + borderW * 0.5f, outer.Width - borderW, outer.Height - borderW);
                    using (GraphicsPath sqIn = CreateRoundRectF(inset, radius - borderW * 0.5f))
                    using (LinearGradientBrush rimBr = new LinearGradientBrush(
                        inset,
                        ringing ? Color.FromArgb(185, 255, 118, 138) : Color.FromArgb(165, 200, 232, 255),
                        Color.FromArgb(150, accentB.R, accentB.G, accentB.B),
                        LinearGradientMode.ForwardDiagonal))
                    using (Pen rimPen = new Pen(rimBr, borderW))
                    {
                        g.DrawPath(rimPen, sqIn);
                    }
                }

                // 2. Precision Alarm Chronometer Geometry
                float cx = 50f * s;
                float cy = 54.5f * s;
                float dialR = (tiny ? 28.0f : (small ? 27.2f : 26.2f)) * s;

                // Twin-Bell Precision Caps (angled curved crowns at 10:30 & 1:30)
                float bellR = dialR + (tiny ? 9.2f : 8.8f) * s;
                float bellStroke = (tiny ? 7.0f : (small ? 6.2f : 5.5f)) * s;
                RectangleF bellBox = new RectangleF(cx - bellR, cy - bellR, bellR * 2f, bellR * 2f);
                using (LinearGradientBrush bellBr = new LinearGradientBrush(bellBox, accentB, accentA, LinearGradientMode.Horizontal))
                using (Pen bellPen = new Pen(bellBr, bellStroke))
                {
                    bellPen.StartCap = LineCap.Round;
                    bellPen.EndCap = LineCap.Round;
                    g.DrawArc(bellPen, bellBox, 210f, 38f);
                    g.DrawArc(bellPen, bellBox, 292f, 38f);
                }

                // Subtle top horological crown anchor at 12 o'clock
                if (!tiny)
                {
                    using (Pen topPen = new Pen(Color.FromArgb(190, 220, 238, 255), 3.3f * s))
                    {
                        topPen.StartCap = LineCap.Round;
                        topPen.EndCap = LineCap.Round;
                        g.DrawLine(topPen, cx - 4.4f * s, 19.2f * s, cx + 4.4f * s, 19.2f * s);
                    }
                }

                // Active ringing sonic wave arcs
                if (ringing)
                {
                    float waveR = dialR + 14.2f * s;
                    RectangleF waveBox = new RectangleF(cx - waveR, cy - waveR, waveR * 2f, waveR * 2f);
                    using (Pen wavePen = new Pen(Color.FromArgb(225, 255, 138, 114), (tiny ? 4.4f : 3.4f) * s))
                    {
                        wavePen.StartCap = LineCap.Round;
                        wavePen.EndCap = LineCap.Round;
                        g.DrawArc(wavePen, waveBox, 152f, 52f);
                        g.DrawArc(wavePen, waveBox, -24f, 52f);
                    }
                }

                // Recessed dark inner dial face
                RectangleF dialRect = new RectangleF(cx - dialR, cy - dialR, dialR * 2f, dialR * 2f);
                using (SolidBrush faceBr = new SolidBrush(ringing ? Color.FromArgb(182, 24, 6, 12) : Color.FromArgb(182, 7, 11, 21)))
                {
                    g.FillEllipse(faceBr, dialRect);
                }

                // Luminous outer bezel ring
                float ringW = (tiny ? 6.8f : (small ? 6.0f : 5.1f)) * s;
                using (LinearGradientBrush ringBr = new LinearGradientBrush(
                    dialRect,
                    Color.White,
                    ringing ? Color.FromArgb(255, 255, 192, 200) : Color.FromArgb(255, 202, 222, 252),
                    LinearGradientMode.Vertical))
                using (Pen ringPen = new Pen(ringBr, ringW))
                {
                    g.DrawEllipse(ringPen, dialRect);
                }

                // Crisp cardinal hour ticks at 12, 3, 6, 9 on medium/large sizes
                if (!small)
                {
                    using (Pen tickPen = new Pen(Color.FromArgb(125, 215, 232, 255), 2.1f * s))
                    {
                        tickPen.StartCap = LineCap.Round;
                        tickPen.EndCap = LineCap.Round;
                        float rOut = dialR - 5.2f * s;
                        float rIn = dialR - 8.4f * s;
                        float[] angles = new float[] { -90f, 0f, 90f, 180f };
                        for (int i = 0; i < angles.Length; i++)
                        {
                            double rad = angles[i] * Math.PI / 180.0;
                            g.DrawLine(tickPen,
                                cx + (float)(Math.Cos(rad) * rIn), cy + (float)(Math.Sin(rad) * rIn),
                                cx + (float)(Math.Cos(rad) * rOut), cy + (float)(Math.Sin(rad) * rOut));
                        }
                    }
                }

                // Luminous hands: Minute/Alarm hand in bright primary accent (2 o'clock) + bold white Hour hand (10 o'clock)
                double hrRad = (-145.0) * Math.PI / 180.0;
                double minRad = (-38.0) * Math.PI / 180.0;
                float hrLen = dialR * 0.47f;
                float minLen = dialR * 0.68f;

                Color minuteHandCol = ringing ? accentB : accentA;
                using (Pen minPen = new Pen(minuteHandCol, (tiny ? 5.4f : (small ? 4.5f : 3.9f)) * s))
                {
                    minPen.StartCap = LineCap.Round;
                    minPen.EndCap = LineCap.Round;
                    g.DrawLine(minPen, cx, cy, cx + (float)(Math.Cos(minRad) * minLen), cy + (float)(Math.Sin(minRad) * minLen));
                }

                using (Pen hrPen = new Pen(Color.White, (tiny ? 6.2f : (small ? 5.3f : 4.6f)) * s))
                {
                    hrPen.StartCap = LineCap.Round;
                    hrPen.EndCap = LineCap.Round;
                    g.DrawLine(hrPen, cx, cy, cx + (float)(Math.Cos(hrRad) * hrLen), cy + (float)(Math.Sin(hrRad) * hrLen));
                }

                // Center precision jewel pivot
                float hubR = (tiny ? 4.8f : 4.3f) * s;
                using (SolidBrush whiteBr = new SolidBrush(Color.White))
                using (SolidBrush coreBr = new SolidBrush(minuteHandCol))
                {
                    g.FillEllipse(whiteBr, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
                    if (!tiny)
                    {
                        float innerR = hubR * 0.48f;
                        g.FillEllipse(coreBr, cx - innerR, cy - innerR, innerR * 2f, innerR * 2f);
                    }
                }

                Bitmap finalBmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics dg = Graphics.FromImage(finalBmp))
                {
                    dg.SmoothingMode = SmoothingMode.HighQuality;
                    dg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    dg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    dg.CompositingQuality = CompositingQuality.HighQuality;
                    dg.Clear(Color.Transparent);
                    dg.DrawImage(hi, new Rectangle(0, 0, size, size), new Rectangle(0, 0, ss, ss), GraphicsUnit.Pixel);
                }
                return finalBmp;
            }
        }

        private static GraphicsPath CreateRoundRectF(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = Math.Max(1f, Math.Min(radius, Math.Min(rect.Width, rect.Height) * 0.5f));
            float d = r * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static Icon CreateAppIcon(bool ringing, int size)
        {
            using (Bitmap bmp = CreateAppLogoBitmap(ringing, size))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }

    internal enum UiGlyph
    {
        None,
        Alarm,
        Speaker,
        Monitor,
        Headphones,
        Shield,
        Logs,
        Plus,
        Play,
        Stop,
        Edit,
        Trash,
        Sliders,
        Refresh,
        Check,
        ChevronDown,
        ChevronLeft,
        ChevronRight,
        Folder,
        Lock
    }

    internal static class UiIcons
    {
        public static void DrawGlyph(Graphics g, UiGlyph glyph, Rectangle bounds, Color color, float strokeWidth = 1.55f)
        {
            if (glyph == UiGlyph.None || bounds.Width <= 0 || bounds.Height <= 0) return;

            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float s = Math.Min(bounds.Width, bounds.Height);
            float x = bounds.X + (bounds.Width - s) * 0.5f;
            float y = bounds.Y + (bounds.Height - s) * 0.5f;

            using (Pen pen = new Pen(color, strokeWidth))
            using (SolidBrush br = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (glyph)
                {
                    case UiGlyph.Alarm:
                        g.DrawArc(pen, x + s * 0.20f, y + s * 0.16f, s * 0.60f, s * 0.62f, 180, 180);
                        g.DrawLine(pen, x + s * 0.20f, y + s * 0.47f, x + s * 0.14f, y + s * 0.68f);
                        g.DrawLine(pen, x + s * 0.80f, y + s * 0.47f, x + s * 0.86f, y + s * 0.68f);
                        g.DrawLine(pen, x + s * 0.12f, y + s * 0.68f, x + s * 0.88f, y + s * 0.68f);
                        g.DrawArc(pen, x + s * 0.39f, y + s * 0.68f, s * 0.22f, s * 0.18f, 0, 180);
                        g.DrawLine(pen, x + s * 0.50f, y + s * 0.08f, x + s * 0.50f, y + s * 0.16f);
                        break;

                    case UiGlyph.Speaker:
                        PointF[] cone = new PointF[]
                        {
                            new PointF(x + s * 0.16f, y + s * 0.38f),
                            new PointF(x + s * 0.32f, y + s * 0.38f),
                            new PointF(x + s * 0.52f, y + s * 0.20f),
                            new PointF(x + s * 0.52f, y + s * 0.80f),
                            new PointF(x + s * 0.32f, y + s * 0.62f),
                            new PointF(x + s * 0.16f, y + s * 0.62f)
                        };
                        g.DrawPolygon(pen, cone);
                        g.DrawArc(pen, x + s * 0.52f, y + s * 0.34f, s * 0.20f, s * 0.32f, -55, 110);
                        g.DrawArc(pen, x + s * 0.58f, y + s * 0.20f, s * 0.28f, s * 0.60f, -55, 110);
                        break;

                    case UiGlyph.Monitor:
                        g.DrawRectangle(pen, x + s * 0.12f, y + s * 0.18f, s * 0.76f, s * 0.50f);
                        g.DrawLine(pen, x + s * 0.50f, y + s * 0.68f, x + s * 0.50f, y + s * 0.84f);
                        g.DrawLine(pen, x + s * 0.30f, y + s * 0.84f, x + s * 0.70f, y + s * 0.84f);
                        break;

                    case UiGlyph.Headphones:
                        g.DrawArc(pen, x + s * 0.16f, y + s * 0.18f, s * 0.68f, s * 0.68f, 180, 180);
                        g.DrawRectangle(pen, x + s * 0.13f, y + s * 0.50f, s * 0.16f, s * 0.30f);
                        g.DrawRectangle(pen, x + s * 0.71f, y + s * 0.50f, s * 0.16f, s * 0.30f);
                        break;

                    case UiGlyph.Shield:
                        PointF[] shield = new PointF[]
                        {
                            new PointF(x + s * 0.50f, y + s * 0.12f),
                            new PointF(x + s * 0.84f, y + s * 0.26f),
                            new PointF(x + s * 0.80f, y + s * 0.62f),
                            new PointF(x + s * 0.50f, y + s * 0.88f),
                            new PointF(x + s * 0.20f, y + s * 0.62f),
                            new PointF(x + s * 0.16f, y + s * 0.26f)
                        };
                        g.DrawPolygon(pen, shield);
                        g.DrawLine(pen, x + s * 0.50f, y + s * 0.34f, x + s * 0.42f, y + s * 0.52f);
                        g.DrawLine(pen, x + s * 0.42f, y + s * 0.52f, x + s * 0.58f, y + s * 0.52f);
                        g.DrawLine(pen, x + s * 0.58f, y + s * 0.52f, x + s * 0.48f, y + s * 0.68f);
                        break;

                    case UiGlyph.Logs:
                        g.DrawRectangle(pen, x + s * 0.14f, y + s * 0.18f, s * 0.72f, s * 0.64f);
                        g.DrawLine(pen, x + s * 0.28f, y + s * 0.38f, x + s * 0.40f, y + s * 0.50f);
                        g.DrawLine(pen, x + s * 0.40f, y + s * 0.50f, x + s * 0.28f, y + s * 0.62f);
                        g.DrawLine(pen, x + s * 0.48f, y + s * 0.62f, x + s * 0.72f, y + s * 0.62f);
                        break;

                    case UiGlyph.Plus:
                        g.DrawLine(pen, x + s * 0.50f, y + s * 0.20f, x + s * 0.50f, y + s * 0.80f);
                        g.DrawLine(pen, x + s * 0.20f, y + s * 0.50f, x + s * 0.80f, y + s * 0.50f);
                        break;

                    case UiGlyph.Play:
                        PointF[] tri = new PointF[]
                        {
                            new PointF(x + s * 0.28f, y + s * 0.18f),
                            new PointF(x + s * 0.82f, y + s * 0.50f),
                            new PointF(x + s * 0.28f, y + s * 0.82f)
                        };
                        g.FillPolygon(br, tri);
                        break;

                    case UiGlyph.Stop:
                        g.FillRectangle(br, x + s * 0.24f, y + s * 0.24f, s * 0.52f, s * 0.52f);
                        break;

                    case UiGlyph.Edit:
                        g.DrawLine(pen, x + s * 0.22f, y + s * 0.78f, x + s * 0.68f, y + s * 0.22f);
                        g.DrawLine(pen, x + s * 0.68f, y + s * 0.22f, x + s * 0.80f, y + s * 0.34f);
                        g.DrawLine(pen, x + s * 0.80f, y + s * 0.34f, x + s * 0.34f, y + s * 0.80f);
                        g.DrawLine(pen, x + s * 0.34f, y + s * 0.80f, x + s * 0.18f, y + s * 0.84f);
                        g.DrawLine(pen, x + s * 0.18f, y + s * 0.84f, x + s * 0.22f, y + s * 0.78f);
                        break;

                    case UiGlyph.Trash:
                        g.DrawLine(pen, x + s * 0.20f, y + s * 0.28f, x + s * 0.80f, y + s * 0.28f);
                        g.DrawLine(pen, x + s * 0.38f, y + s * 0.16f, x + s * 0.62f, y + s * 0.16f);
                        g.DrawLine(pen, x + s * 0.38f, y + s * 0.16f, x + s * 0.38f, y + s * 0.28f);
                        g.DrawLine(pen, x + s * 0.62f, y + s * 0.16f, x + s * 0.62f, y + s * 0.28f);
                        g.DrawLine(pen, x + s * 0.27f, y + s * 0.28f, x + s * 0.32f, y + s * 0.82f);
                        g.DrawLine(pen, x + s * 0.73f, y + s * 0.28f, x + s * 0.68f, y + s * 0.82f);
                        g.DrawLine(pen, x + s * 0.32f, y + s * 0.82f, x + s * 0.68f, y + s * 0.82f);
                        break;

                    case UiGlyph.Sliders:
                        g.DrawLine(pen, x + s * 0.16f, y + s * 0.30f, x + s * 0.84f, y + s * 0.30f);
                        g.DrawLine(pen, x + s * 0.16f, y + s * 0.70f, x + s * 0.84f, y + s * 0.70f);
                        g.FillEllipse(br, x + s * 0.56f, y + s * 0.18f, s * 0.24f, s * 0.24f);
                        g.FillEllipse(br, x + s * 0.24f, y + s * 0.58f, s * 0.24f, s * 0.24f);
                        break;

                    case UiGlyph.Refresh:
                        g.DrawArc(pen, x + s * 0.18f, y + s * 0.18f, s * 0.64f, s * 0.64f, -35, 290);
                        g.DrawLine(pen, x + s * 0.78f, y + s * 0.22f, x + s * 0.82f, y + s * 0.42f);
                        g.DrawLine(pen, x + s * 0.62f, y + s * 0.40f, x + s * 0.82f, y + s * 0.42f);
                        break;

                    case UiGlyph.Check:
                        g.DrawLine(pen, x + s * 0.20f, y + s * 0.52f, x + s * 0.42f, y + s * 0.74f);
                        g.DrawLine(pen, x + s * 0.42f, y + s * 0.74f, x + s * 0.82f, y + s * 0.28f);
                        break;

                    case UiGlyph.ChevronDown:
                        g.DrawLine(pen, x + s * 0.24f, y + s * 0.38f, x + s * 0.50f, y + s * 0.64f);
                        g.DrawLine(pen, x + s * 0.50f, y + s * 0.64f, x + s * 0.76f, y + s * 0.38f);
                        break;

                    case UiGlyph.ChevronLeft:
                        g.DrawLine(pen, x + s * 0.62f, y + s * 0.24f, x + s * 0.36f, y + s * 0.50f);
                        g.DrawLine(pen, x + s * 0.36f, y + s * 0.50f, x + s * 0.62f, y + s * 0.76f);
                        break;

                    case UiGlyph.ChevronRight:
                        g.DrawLine(pen, x + s * 0.38f, y + s * 0.24f, x + s * 0.64f, y + s * 0.50f);
                        g.DrawLine(pen, x + s * 0.64f, y + s * 0.50f, x + s * 0.38f, y + s * 0.76f);
                        break;

                    case UiGlyph.Folder:
                        PointF[] fld = new PointF[]
                        {
                            new PointF(x + s * 0.14f, y + s * 0.24f),
                            new PointF(x + s * 0.40f, y + s * 0.24f),
                            new PointF(x + s * 0.48f, y + s * 0.34f),
                            new PointF(x + s * 0.86f, y + s * 0.34f),
                            new PointF(x + s * 0.86f, y + s * 0.78f),
                            new PointF(x + s * 0.14f, y + s * 0.78f)
                        };
                        g.DrawPolygon(pen, fld);
                        break;

                    case UiGlyph.Lock:
                        g.DrawArc(pen, x + s * 0.30f, y + s * 0.14f, s * 0.40f, s * 0.38f, 180, 180);
                        g.DrawRectangle(pen, x + s * 0.22f, y + s * 0.44f, s * 0.56f, s * 0.40f);
                        break;
                }
            }

            g.Restore(st);
        }
    }
}
