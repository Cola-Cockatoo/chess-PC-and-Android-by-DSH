using System;
using System.Drawing;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 界面缩放：所有界面都按"逻辑像素"排版（主页 940×700、对局 940 或 1266 × 700），
    /// 再整体等比缩放到实际窗口尺寸并居中——这样窗口化和全屏共用同一套坐标，
    /// 布局代码不用改两遍，全屏时棋盘与字体一起变大。
    /// </summary>
    public static class UiScale
    {
        /// <summary>逻辑像素 → 设备像素的倍数（窗口化时为 1）。</summary>
        public static float Factor = 1f;

        /// <summary>逻辑画布在窗口客户区里的居中偏移。</summary>
        public static int OffsetX;
        public static int OffsetY;

        public static void Set(float factor, int offsetX, int offsetY)
        {
            if (factor <= 0.1f) factor = 1f;
            Factor = factor;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        /// <summary>按屏幕尺寸算缩放与居中偏移。</summary>
        public static void Fit(int width, int height, float logicalWidth, float logicalHeight)
        {
            if (width <= 0 || height <= 0) { Set(1f, 0, 0); return; }
            float f = Math.Min(width / logicalWidth, height / logicalHeight);
            int ox = (int)Math.Round((width - logicalWidth * f) / 2.0);
            int oy = (int)Math.Round((height - logicalHeight * f) / 2.0);
            Set(f, ox, oy);
        }

        /// <summary>逻辑尺寸 → 设备尺寸（不带偏移，控件内部绘制用）。</summary>
        public static int Px(float v)
        {
            return (int)Math.Round(v * Factor);
        }

        /// <summary>逻辑尺寸 → 设备尺寸，至少 1 像素（线宽等）。</summary>
        public static float Pxf(float v)
        {
            float r = v * Factor;
            return r < 1f ? 1f : r;
        }

        /// <summary>逻辑矩形的点（带居中偏移，界面级排版用）。</summary>
        public static int Rx(float x) { return OffsetX + Px(x); }

        public static int Ry(float y) { return OffsetY + Px(y); }

        public static Rectangle R(float x, float y, float w, float h)
        {
            return new Rectangle(Rx(x), Ry(y), Px(w), Px(h));
        }

        /// <summary>控件内部的逻辑矩形（不带偏移）。</summary>
        public static Rectangle Rel(float x, float y, float w, float h)
        {
            return new Rectangle(Px(x), Px(y), Px(w), Px(h));
        }

        public static Point Pt(float x, float y)
        {
            return new Point(Px(x), Px(y));
        }

        /// <summary>按逻辑字号设置控件字体（会随全屏一起放大）。</summary>
        public static void Font(Control c, float logicalSize, FontStyle style)
        {
            if (c != null) c.Font = Theme.Font(logicalSize, style);
        }
    }
}
