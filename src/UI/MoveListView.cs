using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>着法记录列表（自动滚动到最新一步，可选显示着法评价符号）。</summary>
    public class MoveListView : Control
    {
        private List<string> _moves = new List<string>();
        private List<MoveAnnotation> _annotations = new List<MoveAnnotation>();
        private const int LogicalRowHeight = 30;

        public MoveListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.Font(14.5f, FontStyle.Regular);
        }

        public void SetMoves(IList<string> moves)
        {
            SetMoves(moves, null);
        }

        public void SetMoves(IList<string> moves, IList<MoveAnnotation> annotations)
        {
            _moves = new List<string>(moves);
            _annotations = annotations == null ? new List<MoveAnnotation>() : new List<MoveAnnotation>(annotations);
            Invalidate();
        }

        public void ClearMoves()
        {
            _moves.Clear();
            _annotations.Clear();
            Invalidate();
        }

        private MoveAnnotation AnnotationAt(int index)
        {
            if (index < 0 || index >= _annotations.Count) return null;
            return _annotations[index];
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);
            using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, ClientRectangle);

            int rowHeight = UiScale.Px(LogicalRowHeight);
            int rows = (_moves.Count + 1) / 2;
            int visible = Math.Max(1, Height / Math.Max(1, rowHeight));
            int first = Math.Max(0, rows - visible);

            if (rows == 0)
            {
                Theme.DrawText(g, "对局尚未开始", Theme.Font(13.5f, FontStyle.Regular), Theme.TextMuted,
                    ClientRectangle, ContentAlignment.MiddleCenter);
                return;
            }

            Font numFont = Theme.Font(13f, FontStyle.Regular);
            for (int i = first; i < rows; i++)
            {
                int y = (i - first) * rowHeight;
                bool last = (i == rows - 1);
                if (last)
                {
                    using (SolidBrush b = new SolidBrush(Theme.AccentSoft))
                    {
                        g.FillRectangle(b, new Rectangle(0, y + 1, Width, rowHeight - 2));
                    }
                }
                Theme.DrawText(g, (i + 1) + ".", numFont, Theme.TextMuted,
                    new Rectangle(UiScale.Px(6), y, UiScale.Px(30), rowHeight), ContentAlignment.MiddleLeft);

                string white = _moves[i * 2];
                string black = (i * 2 + 1 < _moves.Count) ? _moves[i * 2 + 1] : "";
                int pad = UiScale.Px(44);
                int colW = (Width - pad) / 2;
                Rectangle wRect = new Rectangle(UiScale.Px(40), y, colW - UiScale.Px(18), rowHeight);
                Rectangle bRect = new Rectangle(UiScale.Px(40) + colW, y, colW - UiScale.Px(22), rowHeight);
                Theme.DrawText(g, white, Font, Theme.Text, wRect, ContentAlignment.MiddleLeft);
                Theme.DrawText(g, black, Font, Theme.Text, bRect, ContentAlignment.MiddleLeft);

                // 着法评价符号（AI 解析开启时显示）
                DrawAnnotation(g, AnnotationAt(i * 2), wRect, y);
                DrawAnnotation(g, AnnotationAt(i * 2 + 1), bRect, y);
            }
        }

        private void DrawAnnotation(Graphics g, MoveAnnotation ma, Rectangle textRect, int y)
        {
            if (ma == null || !ma.Ready || ma.Grade == null) return;
            Rectangle r = new Rectangle(textRect.Right, y, UiScale.Px(22), textRect.Height);
            Theme.DrawText(g, ma.Grade.Symbol, Theme.Font(13.5f, FontStyle.Bold),
                Theme.AnnotationColor(ma.Grade.Symbol), r, ContentAlignment.MiddleLeft);
        }
    }
}
