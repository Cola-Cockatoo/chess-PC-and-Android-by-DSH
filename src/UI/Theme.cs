using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>简约风格的配色与字体。</summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0xF6, 0xF4, 0xF0);
        public static readonly Color Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color Border = Color.FromArgb(0xE4, 0xE0, 0xD8);
        public static readonly Color Text = Color.FromArgb(0x2C, 0x2C, 0x2A);
        public static readonly Color TextMuted = Color.FromArgb(0x8E, 0x8B, 0x84);
        public static readonly Color Accent = Color.FromArgb(0x4E, 0x7C, 0x6A);
        public static readonly Color AccentDark = Color.FromArgb(0x3C, 0x62, 0x53);
        public static readonly Color AccentSoft = Color.FromArgb(0xE6, 0xEE, 0xE9);
        public static readonly Color LightSquare = Color.FromArgb(0xF1, 0xEC, 0xE2);
        public static readonly Color DarkSquare = Color.FromArgb(0xA7, 0xB6, 0x9D);
        public static readonly Color WhitePiece = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color WhitePieceEdge = Color.FromArgb(0x38, 0x38, 0x35);
        public static readonly Color BlackPiece = Color.FromArgb(0x33, 0x32, 0x2E);
        public static readonly Color Danger = Color.FromArgb(0xC2, 0x5B, 0x4E);

        // 「控制格」视图的四色：黑方控制 → 黑；白方控制 → 白；双方控制 → 浅灰；都没控制 → 浅绿。
        // 浅绿取棋盘原有的深色格（这块绿是棋盘主色），开启后覆盖棋盘原配色。
        public static readonly Color ControlBlack = BlackPiece;
        public static readonly Color ControlWhite = WhitePiece;
        public static readonly Color ControlBoth = Color.FromArgb(0xD8, 0xD6, 0xD0);
        public static readonly Color ControlNone = DarkSquare;
        /// <summary>控制格视图下的淡网格线（同一颜色的相邻格否则会连成一片）。</summary>
        public static readonly Color ControlGrid = Color.FromArgb(0x6E, 0x6C, 0x66);

        public static readonly string UiFontFamily = PickFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimHei");
        public static readonly string PieceFontFamily = PickFamily("Segoe UI Symbol", "MS Gothic", "Arial Unicode MS", "Segoe UI");

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        private static string PickFamily(params string[] names)
        {
            foreach (string n in names)
            {
                try
                {
                    using (FontFamily f = new FontFamily(n))
                    {
                        if (f != null) return n;
                    }
                }
                catch
                {
                }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        public static Font Font(float size, FontStyle style)
        {
            return Font(size, style, UiFontFamily);
        }

        public static Font Font(float size, FontStyle style, string family)
        {
            // 逻辑字号 → 设备字号（全屏时界面整体放大）
            float px = size * UiScale.Factor;
            if (px < 6f) px = 6f;
            string key = family + "|" + px.ToString("0.##") + "|" + (int)style;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;
            f = new Font(family, px, style, GraphicsUnit.Pixel);
            FontCache[key] = f;
            return f;
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void PrepareText(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>着法评价符号的显示颜色（优 → 劣：青绿 → 红）。</summary>
        public static Color AnnotationColor(string symbol)
        {
            switch (symbol)
            {
                case "!!": return Color.FromArgb(0x1B, 0x7F, 0x79);
                case "!": return Color.FromArgb(0x4E, 0x7C, 0x6A);
                case "!?": return Color.FromArgb(0x7A, 0x93, 0x5C);
                case "?!": return Color.FromArgb(0xC0, 0x96, 0x35);
                case "?": return Color.FromArgb(0xD2, 0x76, 0x2F);
                case "??": return Color.FromArgb(0xC2, 0x5B, 0x4E);
            }
            return TextMuted;
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(alpha, c);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle rect, ContentAlignment align)
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = (align == ContentAlignment.MiddleCenter || align == ContentAlignment.TopCenter || align == ContentAlignment.BottomCenter)
                ? StringAlignment.Center
                : (align == ContentAlignment.MiddleRight || align == ContentAlignment.TopRight || align == ContentAlignment.BottomRight
                    ? StringAlignment.Far : StringAlignment.Near);
            sf.LineAlignment = (align == ContentAlignment.MiddleLeft || align == ContentAlignment.MiddleCenter || align == ContentAlignment.MiddleRight)
                ? StringAlignment.Center
                : (align == ContentAlignment.BottomLeft || align == ContentAlignment.BottomCenter || align == ContentAlignment.BottomRight
                    ? StringAlignment.Far : StringAlignment.Near);
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            using (Brush b = new SolidBrush(color))
            {
                g.DrawString(text, font, b, rect, sf);
            }
            sf.Dispose();
        }
    }
}
