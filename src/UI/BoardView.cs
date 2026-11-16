using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    public class MoveRequestEventArgs : EventArgs
    {
        public int From;
        public int To;
        public MoveRequestEventArgs(int from, int to) { From = from; To = to; }
    }

    /// <summary>棋盘视图：绘制棋盘、棋子、高亮、可走点与结果浮层，并处理点击选子。</summary>
    public class BoardView : Control
    {
        public ChessGame Game;
        public bool WhiteAtBottom = true;
        public bool Interactive = true;
        public int Selected = -1;
        public List<Chess.Move> Targets = new List<Chess.Move>();
        public Chess.Move LastMove = Chess.Move.Null;

        public bool ShowResultOverlay;

        /// <summary>是否叠加显示"控制格"（双方棋子的攻击范围）。</summary>
        public bool ShowControl;

        /// <summary>本次绘制是否处于"控制格视图"（棋盘按控制方整块上色）。</summary>
        private bool _controlMode;

        /// <summary>控制格视图下单个格子的颜色：
        /// 黑方控制 → 黑；白方控制 → 白；双方控制 → 浅灰；都没控制 → 浅绿（棋盘主色）。</summary>
        public static Color ControlSquareColor(bool hasWhite, bool hasBlack)
        {
            if (hasWhite && hasBlack) return Theme.ControlBoth;
            if (hasBlack) return Theme.ControlBlack;
            if (hasWhite) return Theme.ControlWhite;
            return Theme.ControlNone;
        }

        /// <summary>提示箭头（定式学习用）：显示"该走哪一步"，多个表示学员可以选。</summary>
        public readonly List<MoveHint> Hints = new List<MoveHint>();

        public sealed class MoveHint
        {
            public int From;
            public int To;
            public MoveHint(int from, int to) { From = from; To = to; }
        }

        /// <summary>只允许走这些着法（定式学习时用；为空表示不限）。</summary>
        public readonly List<Chess.Move> Allowed = new List<Chess.Move>();
        public string ResultTitle = "";
        public string ResultSubtitle = "";

        public event EventHandler<MoveRequestEventArgs> MoveRequested;
        public event EventHandler RestartRequested;
        public event EventHandler MenuRequested;

        private static readonly Dictionary<int, GraphicsPath> PathCache = new Dictionary<int, GraphicsPath>();
        private int _hoverSquare = -1;
        private int _hoverButton;   // 0 无, 1 再来一局, 2 返回主页
        private Rectangle _restartRect, _menuRect;

        public BoardView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        // ------------------------------------------------------------------
        public void ClearSelection()
        {
            Selected = -1;
            Targets.Clear();
            Invalidate();
        }

        public void SetLastMove(Chess.Move m)
        {
            LastMove = m;
            Invalidate();
        }

        public void ShowResult(string title, string subtitle)
        {
            ShowResultOverlay = true;
            ResultTitle = title;
            ResultSubtitle = subtitle;
            _hoverButton = 0;
            Invalidate();
        }

        public void HideResult()
        {
            ShowResultOverlay = false;
            Invalidate();
        }

        public int CellSize
        {
            get { return Math.Max(20, Width / 8); }
        }

        private Rectangle RectOf(int sq)
        {
            int cell = CellSize;
            int f = Squares.File(sq);
            int r = Squares.Rank(sq);
            int col = WhiteAtBottom ? f : 7 - f;
            int row = WhiteAtBottom ? 7 - r : r;
            return new Rectangle(col * cell, row * cell, cell, cell);
        }

        private int SquareAt(Point p)
        {
            int cell = CellSize;
            int col = p.X / cell;
            int row = p.Y / cell;
            if (col < 0 || col > 7 || row < 0 || row > 7) return -1;
            int f = WhiteAtBottom ? col : 7 - col;
            int r = WhiteAtBottom ? 7 - row : row;
            return Squares.Index(f, r);
        }

        private bool IsTarget(int sq)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i].To == sq) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 交互
        // ------------------------------------------------------------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (ShowResultOverlay)
            {
                int hb = _restartRect.Contains(e.Location) ? 1 : (_menuRect.Contains(e.Location) ? 2 : 0);
                if (hb != _hoverButton)
                {
                    _hoverButton = hb;
                    Cursor = hb != 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
                return;
            }

            if (!Interactive)
            {
                if (_hoverSquare != -1) { _hoverSquare = -1; Invalidate(); }
                return;
            }

            int sq = SquareAt(e.Location);
            int newHover = -1;
            if (sq >= 0 && Game != null && !Game.IsOver)
            {
                int piece = Game.Position.PieceAt(sq);
                if (piece != 0 && Board.ColorOf(piece) == Game.Position.Side && SideIsHuman(Game.Position.Side))
                    newHover = sq;
            }
            if (newHover != _hoverSquare)
            {
                _hoverSquare = newHover;
                Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverSquare = -1;
            if (ShowResultOverlay)
            {
                _hoverButton = 0;
                Cursor = Cursors.Default;
            }
            Invalidate();
            base.OnMouseLeave(e);
        }

        /// <summary>人机对战时人类一方（白）。0 表示两方都由人类操作（人人对战）。</summary>
        public int HumanSide = Board.White;

        private bool SideIsHuman(int side)
        {
            return HumanSide == 0 || HumanSide == side;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (ShowResultOverlay)
            {
                if (_restartRect.Contains(e.Location) && RestartRequested != null) RestartRequested(this, EventArgs.Empty);
                else if (_menuRect.Contains(e.Location) && MenuRequested != null) MenuRequested(this, EventArgs.Empty);
                return;
            }

            if (!Interactive || Game == null || Game.IsOver) return;
            if (!SideIsHuman(Game.Position.Side)) return;

            int sq = SquareAt(e.Location);
            if (sq < 0) return;

            int piece = Game.Position.PieceAt(sq);
            if (piece != 0 && Board.ColorOf(piece) == Game.Position.Side)
            {
                if (Selected == sq)
                {
                    ClearSelection();
                    return;
                }
                Selected = sq;
                Targets = Game.LegalMovesFrom(sq);
                if (Allowed.Count > 0)
                {
                    // 定式学习：只保留"教学变化里允许"的着法，避免乱走
                    for (int i = Targets.Count - 1; i >= 0; i--)
                    {
                        bool ok = false;
                        for (int k = 0; k < Allowed.Count; k++)
                        {
                            if (Allowed[k].From == Targets[i].From && Allowed[k].To == Targets[i].To &&
                                Allowed[k].Promotion == Targets[i].Promotion) { ok = true; break; }
                        }
                        if (!ok) Targets.RemoveAt(i);
                    }
                }
                Invalidate();
                return;
            }

            if (Selected >= 0 && IsTarget(sq))
            {
                int from = Selected;
                ClearSelection();
                if (MoveRequested != null) MoveRequested(this, new MoveRequestEventArgs(from, sq));
                return;
            }

            ClearSelection();
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            int cell = CellSize;
            int board = cell * 8;

            // 「控制格」视图：整块棋盘按"谁控制这一格"重新上色，覆盖原来的深浅格
            _controlMode = ShowControl && Game != null;
            ulong whiteCtl = 0, blackCtl = 0;
            if (_controlMode)
            {
                whiteCtl = Game.Position.ControlledSquares(Board.White);
                blackCtl = Game.Position.ControlledSquares(Board.Black);
            }

            using (SolidBrush bg = new SolidBrush(_controlMode ? Theme.ControlNone : Theme.LightSquare))
            {
                g.FillRectangle(bg, 0, 0, board, board);
            }

            for (int r = 0; r < 8; r++)
            {
                for (int f = 0; f < 8; f++)
                {
                    int sq = Squares.Index(f, r);
                    Rectangle rect = RectOf(sq);
                    bool light = ((f + r) % 2) != 0;
                    Color c;
                    if (_controlMode)
                    {
                        bool hasWhite = (whiteCtl & Board.ControlBit(f, r)) != 0;
                        bool hasBlack = (blackCtl & Board.ControlBit(f, r)) != 0;
                        c = ControlSquareColor(hasWhite, hasBlack);
                    }
                    else
                    {
                        c = light ? Theme.LightSquare : Theme.DarkSquare;
                    }
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        g.FillRectangle(b, rect);
                    }
                }
            }

            // 控制格视图下补一层很淡的网格线，否则同色的相邻格会连成一片
            if (_controlMode)
            {
                using (Pen grid = new Pen(Theme.WithAlpha(Theme.ControlGrid, 38), UiScale.Pxf(1f)))
                {
                    for (int i = 1; i < 8; i++)
                    {
                        g.DrawLine(grid, i * cell, 0, i * cell, board);
                        g.DrawLine(grid, 0, i * cell, board, i * cell);
                    }
                }
            }

            // 上一步高亮（控制格视图下会干扰色块辨识，故不画）
            if (Game != null && !LastMove.IsNull && !_controlMode)
            {
                using (SolidBrush b = new SolidBrush(Theme.WithAlpha(Theme.Accent, 46)))
                {
                    g.FillRectangle(b, RectOf(LastMove.From));
                    g.FillRectangle(b, RectOf(LastMove.To));
                }
            }

            // 被将军的王
            if (Game != null && Game.Position.InCheck(Game.Position.Side))
            {
                int k = Game.Position.KingSq[Game.Position.Side];
                if (k != 0 || Game.Position.PieceAt(0) == Board.Make(PT.King, Game.Position.Side))
                {
                    using (SolidBrush b = new SolidBrush(Theme.WithAlpha(Theme.Danger, 90)))
                    {
                        g.FillRectangle(b, RectOf(k));
                    }
                }
            }

            // 选中格
            if (Selected >= 0)
            {
                using (SolidBrush b = new SolidBrush(Theme.WithAlpha(Theme.Accent, 80)))
                {
                    g.FillRectangle(b, RectOf(Selected));
                }
            }

            // 可走点
            for (int i = 0; i < Targets.Count; i++)
            {
                int to = Targets[i].To;
                Rectangle rect = RectOf(to);
                bool capture = Game.Position.PieceAt(to) != 0 || (Targets[i].Flags & MF.EnPassant) != 0;
                if (capture)
                {
                    Rectangle inner = rect;
                    inner.Inflate(-cell / 12, -cell / 12);
                    using (Pen p = new Pen(Theme.WithAlpha(Theme.AccentDark, 120), Math.Max(2f, cell * 0.055f)))
                    using (GraphicsPath path = Theme.RoundRect(inner, cell / 6))
                    {
                        g.DrawPath(p, path);
                    }
                }
                else
                {
                    float rad = cell * 0.155f;
                    using (SolidBrush b = new SolidBrush(Theme.WithAlpha(Theme.AccentDark, 95)))
                    {
                        g.FillEllipse(b, rect.X + rect.Width / 2f - rad, rect.Y + rect.Height / 2f - rad, rad * 2, rad * 2);
                    }
                }
            }

            // 棋子
            if (Game != null)
            {
                for (int r = 0; r < 8; r++)
                {
                    for (int f = 0; f < 8; f++)
                    {
                        int sq = Squares.Index(f, r);
                        int piece = Game.Position.PieceAt(sq);
                        if (piece == 0) continue;
                        DrawPiece(g, piece, RectOf(sq), sq == _hoverSquare && Selected != sq);
                    }
                }
            }

            DrawCoordinates(g, cell, board);

            // 提示箭头（定式学习）：最后画，永远在最上层
            if (Hints.Count > 0) DrawHints(g);

            if (ShowResultOverlay) DrawResult(g, board);
        }

        /// <summary>画"该走哪一步"的箭头（起点格 → 终点格），多个箭头表示可以任选其一。</summary>
        private void DrawHints(Graphics g)
        {
            int cell = CellSize;
            int w = Math.Max(3, (int)Math.Round(cell * 0.12));
            for (int i = 0; i < Hints.Count; i++)
            {
                MoveHint h = Hints[i];
                Rectangle a = RectOf(Squares.Index(Squares.File(h.From), Squares.Rank(h.From)));
                Rectangle b = RectOf(Squares.Index(Squares.File(h.To), Squares.Rank(h.To)));
                float ax = a.X + a.Width / 2f, ay = a.Y + a.Height / 2f;
                float bx = b.X + b.Width / 2f, by = b.Y + b.Height / 2f;

                // 起点画一个半透明圆，终点画圆环，箭头本身画粗线 + 箭头锚
                using (SolidBrush from = new SolidBrush(Theme.WithAlpha(Theme.Accent, 70)))
                {
                    float r = cell * 0.42f;
                    g.FillEllipse(from, ax - r, ay - r, r * 2, r * 2);
                }
                using (Pen ring = new Pen(Theme.WithAlpha(Theme.AccentDark, 150), UiScale.Pxf(2f)))
                {
                    float r = cell * 0.36f;
                    g.DrawEllipse(ring, bx - r, by - r, r * 2, r * 2);
                }

                using (Pen p = new Pen(Theme.WithAlpha(Theme.AccentDark, 210), w))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.ArrowAnchor;
                    // 终点缩进一点，箭头尖正好落在目标格中心附近
                    float dx = bx - ax, dy = by - ay;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1f) continue;
                    float cut = cell * 0.30f;
                    float ex = bx - dx / len * cut, ey = by - dy / len * cut;
                    g.DrawLine(p, ax, ay, ex, ey);
                }
            }
        }

        /// <summary>
        private void DrawPiece(Graphics g, int piece, Rectangle rect, bool hover)
        {
            GraphicsPath path = GetPiecePath(piece, rect.Width);
            if (path == null) return;
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f + rect.Height * 0.01f;

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            bool white = Board.ColorOf(piece) == Board.White;
            if (hover)
            {
                using (SolidBrush hb = new SolidBrush(Theme.WithAlpha(Theme.Accent, 40)))
                {
                    g.FillEllipse(hb, -rect.Width * 0.42f, -rect.Height * 0.42f, rect.Width * 0.84f, rect.Height * 0.84f);
                }
            }
            if (!white && _controlMode)
            {
                // 黑棋在"黑方控制"的黑格上会糊成一片 —— 先描一圈白边，再填色
                using (Pen halo = new Pen(Theme.WhitePiece, Math.Max(3f, rect.Width * 0.075f)))
                {
                    halo.LineJoin = LineJoin.Round;
                    g.DrawPath(halo, path);
                }
            }
            if (white)
            {
                using (SolidBrush b = new SolidBrush(Theme.WhitePiece)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.WhitePieceEdge, Math.Max(1.4f, rect.Width * 0.032f)))
                {
                    p.LineJoin = LineJoin.Round;
                    g.DrawPath(p, path);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Theme.BlackPiece)) g.FillPath(b, path);
            }
            g.Restore(st);
        }

        private static char GlyphOf(int type)
        {
            switch (type)
            {
                case PT.King: return '\u265A';
                case PT.Queen: return '\u265B';
                case PT.Rook: return '\u265C';
                case PT.Bishop: return '\u265D';
                case PT.Knight: return '\u265E';
                case PT.Pawn: return '\u265F';
            }
            return '\u265F';
        }

        private static GraphicsPath GetPiecePath(int piece, int cell)
        {
            int key = (piece << 12) | cell;
            GraphicsPath cached;
            if (PathCache.TryGetValue(key, out cached)) return cached;

            GraphicsPath path = new GraphicsPath();
            float em = cell * 0.9f;
            try
            {
                using (Font f = new Font(Theme.PieceFontFamily, em, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    StringFormat sf = StringFormat.GenericTypographic;
                    sf.FormatFlags |= StringFormatFlags.NoClip;
                    path.AddString(GlyphOf(Board.TypeOf(piece)).ToString(), f.FontFamily, (int)f.Style, em,
                        new PointF(0, 0), sf);
                }
            }
            catch
            {
                path.Dispose();
                return null;
            }

            RectangleF b = path.GetBounds();
            if (b.Width <= 0.1f || b.Height <= 0.1f)
            {
                path.Dispose();
                return null;
            }
            float target = cell * 0.82f;
            float scale = target / b.Height;
            using (Matrix m = new Matrix())
            {
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                path.Transform(m);
                m.Reset();
                m.Scale(scale, scale);
                path.Transform(m);
            }
            PathCache[key] = path;
            return path;
        }

        private void DrawCoordinates(Graphics g, int cell, int board)
        {
            Font f = Theme.Font(12.5f, FontStyle.Regular);   // 字号随全屏缩放
            for (int i = 0; i < 8; i++)
            {
                // 文件字母（底行）
                int file = WhiteAtBottom ? i : 7 - i;
                Rectangle fr = new Rectangle(i * cell, board - cell, cell, cell);
                int fileSq = Squares.Index(file, WhiteAtBottom ? 0 : 7);
                bool fileLight = (Squares.File(fileSq) + Squares.Rank(fileSq)) % 2 != 0;
                Color c1 = fileLight ? Theme.WithAlpha(Theme.AccentDark, 140) : Theme.WithAlpha(Theme.LightSquare, 170);
                Theme.DrawText(g, ((char)('a' + file)).ToString(), f, c1,
                    new Rectangle(fr.Right - cell / 2, fr.Bottom - cell / 2 - 2, cell / 2 - 4, cell / 2), ContentAlignment.BottomRight);

                // 数字（左列）
                int rank = WhiteAtBottom ? 7 - i : i;
                Rectangle rr = new Rectangle(0, i * cell, cell, cell);
                int rankSq = Squares.Index(WhiteAtBottom ? 0 : 7, rank);
                bool rankLight = (Squares.File(rankSq) + Squares.Rank(rankSq)) % 2 != 0;
                Color c2 = rankLight ? Theme.WithAlpha(Theme.AccentDark, 140) : Theme.WithAlpha(Theme.LightSquare, 170);
                Theme.DrawText(g, ((char)('1' + rank)).ToString(), f, c2,
                    new Rectangle(rr.X + 3, rr.Y + 2, cell / 2, cell / 2), ContentAlignment.TopLeft);
            }
        }

        private void DrawResult(Graphics g, int board)
        {
            using (SolidBrush veil = new SolidBrush(Color.FromArgb(206, Theme.Background)))
            {
                g.FillRectangle(veil, 0, 0, board, board);
            }

            int cw = UiScale.Px(380), ch = UiScale.Px(186);
            Rectangle card = new Rectangle((board - cw) / 2, (board - ch) / 2, cw, ch);

            using (GraphicsPath path = Theme.RoundRect(card, UiScale.Px(16)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(6, 0, 0, 0)))
                {
                    g.FillRectangle(b, card.X, card.Y + UiScale.Px(4), card.Width, card.Height);
                }
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }

            Theme.DrawText(g, ResultTitle, Theme.Font(22f, FontStyle.Bold), Theme.Text,
                new Rectangle(card.X, card.Y + UiScale.Px(26), card.Width, UiScale.Px(34)), ContentAlignment.MiddleCenter);
            Theme.DrawText(g, ResultSubtitle, Theme.Font(13.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(card.X, card.Y + UiScale.Px(62), card.Width, UiScale.Px(24)), ContentAlignment.MiddleCenter);

            int bw = UiScale.Px(152), bh = UiScale.Px(44), gap = UiScale.Px(14);
            int totalW = bw * 2 + gap;
            int bx = card.X + (card.Width - totalW) / 2;
            int by = card.Bottom - bh - UiScale.Px(24);
            _restartRect = new Rectangle(bx, by, bw, bh);
            _menuRect = new Rectangle(bx + bw + gap, by, bw, bh);

            DrawFlatButton(g, _restartRect, "再来一局", true, _hoverButton == 1);
            DrawFlatButton(g, _menuRect, "返回主页", false, _hoverButton == 2);
        }

        private void DrawFlatButton(Graphics g, Rectangle r, string text, bool primary, bool hover)
        {
            Color fill = primary ? (hover ? Theme.AccentDark : Theme.Accent) : (hover ? Color.FromArgb(0xF3, 0xF1, 0xEC) : Theme.Surface);
            Color border = primary ? fill : (hover ? Theme.Accent : Theme.Border);
            Color txt = primary ? Color.White : Theme.Text;
            using (GraphicsPath path = Theme.RoundRect(r, UiScale.Px(10)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen p = new Pen(border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }
            Theme.DrawText(g, text, Theme.Font(15f, FontStyle.Regular), txt, r, ContentAlignment.MiddleCenter);
        }
    }
}
