using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// AI 解析面板：全局胜率变化图 + 每一手棋的好坏 + 右下角的符号说明（优 → 劣）。
    /// 整个面板自绘，避免多层透明控件相互覆盖；坐标全部是"逻辑像素"，由 UiScale 缩放。
    /// </summary>
    public class AnalysisPanel : Control
    {
        /// <summary>按 ply 索引的局面分析（可能含 null，表示尚未算出）。</summary>
        public List<PositionAnalysis> Analyses = new List<PositionAnalysis>();
        /// <summary>按手数索引的着法评价。</summary>
        public List<MoveAnnotation> MoveGrades = new List<MoveAnnotation>();
        /// <summary>按手数索引的 SAN 记谱。</summary>
        public List<string> Sans = new List<string>();
        /// <summary>尚未分析完成的任务数。</summary>
        public int Pending;
        /// <summary>当前局面所在 ply。</summary>
        public int CurrentPly;
        public int ProfileDepth = 6;
        public int ProfileMs = 500;

        // 逻辑尺寸（面板自身 304×564，卡片内缩 2）
        private const float CardX = 2f, CardY = 2f, CardW = 300f, CardH = 560f;

        public AnalysisPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            Rectangle card = UiScale.Rel(CardX, CardY, CardW, CardH);
            using (GraphicsPath path = Theme.RoundRect(card, UiScale.Px(14)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }

            int x = card.X + UiScale.Px(18);
            int w = card.Width - UiScale.Px(36);

            Theme.DrawText(g, "AI 解析", Theme.Font(15f, FontStyle.Bold), Theme.Text,
                new Rectangle(x, card.Y + UiScale.Px(10), UiScale.Px(130), UiScale.Px(26)), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "仅供参考", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(x + w - UiScale.Px(70), card.Y + UiScale.Px(12), UiScale.Px(70), UiScale.Px(22)), ContentAlignment.MiddleRight);

            DrawSummary(g, x, w, card.Y);
            DrawChart(g, new Rectangle(x, card.Y + UiScale.Px(98), w, UiScale.Px(130)));

            Theme.DrawText(g, "着法质量（最近 5 手）", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(x, card.Y + UiScale.Px(234), w, UiScale.Px(18)), ContentAlignment.MiddleLeft);
            DrawQualityList(g, x, w, card.Y + UiScale.Px(256));

            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1f)))
            {
                g.DrawLine(p, x, card.Y + UiScale.Px(394), x + w, card.Y + UiScale.Px(394));
            }

            // 符号说明表：不再显示「符号说明 · 优 → 劣」这行标题（表头本身已说明各列含义）
            DrawLegend(g, x, w, card.Y + UiScale.Px(424));
        }

        // ------------------------------------------------------------------
        // 顶部：当前胜率与评估
        // ------------------------------------------------------------------
        private void DrawSummary(Graphics g, int x, int w, int cardY)
        {
            PositionAnalysis cur = CurrentAnalysis();
            double rate = cur != null ? cur.WhiteWinRate : 50.0;
            int cp = cur != null ? cur.WhiteCp : 0;
            int y = cardY + UiScale.Px(38);
            int h = UiScale.Px(36);

            Theme.DrawText(g, rate.ToString("0") + "%", Theme.Font(24f, FontStyle.Bold), Theme.AccentDark,
                new Rectangle(x, y, UiScale.Px(90), h), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "白方胜率", Theme.Font(13f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(x + UiScale.Px(92), y, UiScale.Px(70), h), ContentAlignment.MiddleLeft);

            string eval = cur != null ? AnalysisMath.FormatScore(cp) : "--";
            Color evalColor = cp > 30 ? Theme.AccentDark : (cp < -30 ? Theme.Danger : Theme.TextMuted);
            Theme.DrawText(g, eval, Theme.Font(14.5f, FontStyle.Bold), evalColor,
                new Rectangle(x + w - UiScale.Px(90), y, UiScale.Px(90), h), ContentAlignment.MiddleRight);

            string caption = "全局胜率变化";
            if (Pending > 0) caption += " · 分析中 " + Pending;
            Theme.DrawText(g, caption, Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(x, cardY + UiScale.Px(78), w, UiScale.Px(18)), ContentAlignment.MiddleLeft);
        }

        private PositionAnalysis CurrentAnalysis()
        {
            if (CurrentPly < Analyses.Count && Analyses[CurrentPly] != null) return Analyses[CurrentPly];
            for (int i = Analyses.Count - 1; i >= 0; i--)
            {
                if (Analyses[i] != null) return Analyses[i];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 胜率变化图
        // ------------------------------------------------------------------
        private void DrawChart(Graphics g, Rectangle plot)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0xFA, 0xF9, 0xF6))) g.FillRectangle(b, plot);
            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1f))) g.DrawRectangle(p, plot);

            float midY = plot.Y + plot.Height / 2f;
            using (Pen p = new Pen(Color.FromArgb(0xEC, 0xE9, 0xE2), UiScale.Pxf(1f)))
            {
                g.DrawLine(p, plot.Left + 1, plot.Y + plot.Height * 0.25f, plot.Right - 1, plot.Y + plot.Height * 0.25f);
                g.DrawLine(p, plot.Left + 1, plot.Y + plot.Height * 0.75f, plot.Right - 1, plot.Y + plot.Height * 0.75f);
            }
            using (Pen p = new Pen(Color.FromArgb(0xD8, 0xD4, 0xCB), UiScale.Pxf(1f)))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawLine(p, plot.Left + 1, midY, plot.Right - 1, midY);
            }

            List<PointF> pts = new List<PointF>();
            int last = 0;
            for (int i = 0; i < Analyses.Count; i++)
            {
                if (Analyses[i] == null) break;
                last = i;
            }
            int span = Math.Max(12, last);
            for (int i = 0; i <= last && i < Analyses.Count; i++)
            {
                PositionAnalysis pa = Analyses[i];
                if (pa == null) break;
                float px = plot.Left + 2 + (plot.Width - 4) * ((float)i / span);
                float py = plot.Bottom - 2 - (plot.Height - 4) * ((float)pa.WhiteWinRate / 100f);
                pts.Add(new PointF(px, py));
            }

            if (pts.Count == 0)
            {
                Theme.DrawText(g, Pending > 0 ? "分析中…" : "尚未开始分析", Theme.Font(13f, FontStyle.Regular),
                    Theme.TextMuted, plot, ContentAlignment.MiddleCenter);
                return;
            }

            for (int i = 0; i + 1 < pts.Count; i++)
            {
                FillSegment(g, pts[i], pts[i + 1], midY, plot);
            }

            float dot = UiScale.Pxf(3f);
            if (pts.Count == 1)
            {
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                {
                    g.FillEllipse(b, pts[0].X - dot, pts[0].Y - dot, dot * 2, dot * 2);
                }
                return;
            }

            using (Pen p = new Pen(Theme.Accent, UiScale.Pxf(1.8f)))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, pts.ToArray());
            }
            PointF lastPt = pts[pts.Count - 1];
            float dot2 = UiScale.Pxf(3.5f);
            using (SolidBrush b = new SolidBrush(Theme.AccentDark))
            {
                g.FillEllipse(b, lastPt.X - dot2, lastPt.Y - dot2, dot2 * 2, dot2 * 2);
            }

            Theme.DrawText(g, "100%", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(plot.Left + UiScale.Px(3), plot.Top + UiScale.Px(1), UiScale.Px(40), UiScale.Px(16)), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "50%", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(plot.Left + UiScale.Px(3), (int)midY - UiScale.Px(8), UiScale.Px(40), UiScale.Px(16)), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "0%", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(plot.Left + UiScale.Px(3), plot.Bottom - UiScale.Px(17), UiScale.Px(40), UiScale.Px(16)), ContentAlignment.MiddleLeft);
        }

        /// <summary>把一段曲线与 50% 基准线之间的区域按优劣势涂色。</summary>
        private void FillSegment(Graphics g, PointF a, PointF b, float midY, Rectangle plot)
        {
            if (Math.Abs(a.Y - b.Y) < 0.01f)
            {
                FillQuad(g, a, b, midY, a.Y <= midY);
                return;
            }
            bool aAbove = a.Y <= midY;
            bool bAbove = b.Y <= midY;
            if (aAbove == bAbove)
            {
                FillQuad(g, a, b, midY, aAbove);
                return;
            }
            float t = (midY - a.Y) / (b.Y - a.Y);
            PointF cross = new PointF(a.X + t * (b.X - a.X), midY);
            if (aAbove)
            {
                FillQuad(g, a, cross, midY, true);
                FillQuad(g, cross, b, midY, false);
            }
            else
            {
                FillQuad(g, a, cross, midY, false);
                FillQuad(g, cross, b, midY, true);
            }
        }

        private void FillQuad(Graphics g, PointF a, PointF b, float midY, bool above)
        {
            PointF[] poly = new PointF[]
            {
                a, b, new PointF(b.X, midY), new PointF(a.X, midY)
            };
            Color c = above ? Theme.WithAlpha(Theme.Accent, 70) : Theme.WithAlpha(Theme.Danger, 55);
            using (SolidBrush brush = new SolidBrush(c))
            {
                g.FillPolygon(brush, poly);
            }
        }

        // ------------------------------------------------------------------
        // 每一手棋的好坏
        // ------------------------------------------------------------------
        private void DrawQualityList(Graphics g, int x, int w, int top)
        {
            int n = MoveGrades.Count;
            if (n == 0)
            {
                Theme.DrawText(g, "走一步棋后这里会显示评价", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                    new Rectangle(x, top, w, UiScale.Px(60)), ContentAlignment.MiddleCenter);
                return;
            }
            int rows = Math.Min(5, n);
            int first = n - rows;
            int rowH = UiScale.Px(26);
            for (int i = 0; i < rows; i++)
            {
                int idx = first + i;
                int y = top + i * rowH;
                int h = UiScale.Px(24);
                MoveAnnotation ma = MoveGrades[idx];
                string san = idx < Sans.Count ? Sans[idx] : "";

                Theme.DrawText(g, (idx / 2 + 1) + (idx % 2 == 0 ? "." : "…"), Theme.Font(12.5f, FontStyle.Regular),
                    Theme.TextMuted, new Rectangle(x, y, UiScale.Px(30), h), ContentAlignment.MiddleLeft);
                Theme.DrawText(g, san, Theme.Font(13.5f, FontStyle.Regular), Theme.Text,
                    new Rectangle(x + UiScale.Px(32), y, UiScale.Px(60), h), ContentAlignment.MiddleLeft);

                if (ma == null || !ma.Ready)
                {
                    Theme.DrawText(g, "…", Theme.Font(13.5f, FontStyle.Bold), Theme.TextMuted,
                        new Rectangle(x + UiScale.Px(94), y, UiScale.Px(26), h), ContentAlignment.MiddleCenter);
                    Theme.DrawText(g, "分析中", Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                        new Rectangle(x + UiScale.Px(122), y, w - UiScale.Px(122), h), ContentAlignment.MiddleRight);
                }
                else
                {
                    Theme.DrawText(g, ma.Grade.Symbol, Theme.Font(13.5f, FontStyle.Bold),
                        Theme.AnnotationColor(ma.Grade.Symbol),
                        new Rectangle(x + UiScale.Px(94), y, UiScale.Px(26), h), ContentAlignment.MiddleCenter);
                    string loss = ma.LossCp <= 5 ? "最佳" : ("-" + (ma.LossCp / 100.0).ToString("0.00"));
                    Color lossColor = ma.LossCp <= 15 ? Theme.TextMuted : Theme.AnnotationColor(ma.Grade.Symbol);
                    Theme.DrawText(g, loss, Theme.Font(12.5f, FontStyle.Regular), lossColor,
                        new Rectangle(x + UiScale.Px(122), y, w - UiScale.Px(122), h), ContentAlignment.MiddleRight);
                }
            }
        }

        // ------------------------------------------------------------------
        // 符号说明（右下角）
        // ------------------------------------------------------------------
        private void DrawLegend(Graphics g, int x, int w, int top)
        {
            int headY = top - UiScale.Px(16);
            int headH = UiScale.Px(17);
            Theme.DrawText(g, "符号", Theme.Font(12.5f, FontStyle.Regular), Theme.WithAlpha(Theme.TextMuted, 180),
                new Rectangle(x, headY, UiScale.Px(32), headH), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "English", Theme.Font(12.5f, FontStyle.Regular), Theme.WithAlpha(Theme.TextMuted, 180),
                new Rectangle(x + UiScale.Px(36), headY, UiScale.Px(94), headH), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "中文", Theme.Font(12.5f, FontStyle.Regular), Theme.WithAlpha(Theme.TextMuted, 180),
                new Rectangle(x + UiScale.Px(134), headY, UiScale.Px(46), headH), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "损失(兵)", Theme.Font(12.5f, FontStyle.Regular), Theme.WithAlpha(Theme.TextMuted, 180),
                new Rectangle(x + UiScale.Px(182), headY, w - UiScale.Px(182), headH), ContentAlignment.MiddleRight);

            string[] thresholds = new string[] { "唯一好棋", "≤0.15", "≤0.50", "≤1.20", "≤3.00", ">3.00" };
            int rowH = UiScale.Px(22);
            int entryH = UiScale.Px(18);
            for (int i = 0; i < Annotations.All.Length; i++)
            {
                Annotation a = Annotations.All[i];
                int y = top + i * rowH;
                Theme.DrawText(g, a.Symbol, Theme.Font(13.5f, FontStyle.Bold), Theme.AnnotationColor(a.Symbol),
                    new Rectangle(x, y, UiScale.Px(32), entryH), ContentAlignment.MiddleLeft);
                Theme.DrawText(g, a.English, Theme.Font(13f, FontStyle.Regular), Theme.Text,
                    new Rectangle(x + UiScale.Px(36), y, UiScale.Px(94), entryH), ContentAlignment.MiddleLeft);
                Theme.DrawText(g, a.Chinese, Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                    new Rectangle(x + UiScale.Px(134), y, UiScale.Px(46), entryH), ContentAlignment.MiddleLeft);
                Theme.DrawText(g, thresholds[i], Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                    new Rectangle(x + UiScale.Px(182), y, w - UiScale.Px(182), entryH), ContentAlignment.MiddleRight);
            }
        }
    }
}
