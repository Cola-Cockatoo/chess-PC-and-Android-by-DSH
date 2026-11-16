using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 定式学习 · 学习界面：棋盘上出现箭头，学员照着走；走完程序应一手，再出现下一个箭头。
    /// 有分支时同时出现多个箭头，学员自己选一条。
    /// 逻辑画布 940×700（棋盘 560×560 在右侧，说明在左侧）。
    /// </summary>
    public class OpeningStudyScreen : UserControl
    {
        private const float LogicalW = 940f;
        private const float LogicalH = 700f;
        private const int ReplyDelayMs = 600;

        private readonly MainForm _main;
        private readonly StudySession _session;
        private readonly BoardView _board = new BoardView();
        private readonly FlatButton _back;
        private readonly FlatButton _restart;
        private readonly FlatButton _guideBtn;
        private readonly FlatButton _btnFullscreen;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly System.Windows.Forms.Timer _reply = new System.Windows.Forms.Timer();

        private string _comment = "";           // 顶部一句话（结束提示等）
        private string _prompt = "";            // 当前该做什么的提示（不覆盖上面的点评）
        private string _variation = "";         // 当前所处的变化名
        private string _flash = "";             // 临时提示（比如"请按箭头走"）
        private bool _finished;
        private bool _showGuide;          // 左侧卡片是否显示"开局讲解"
        private string _variationNote = "";
        private bool _waitingReply;

        // 「本步点评」的三段内容：对手上一手有什么用 / 我上一手在做什么 / 我下一步为什么走这里
        private string _oppSan = "", _oppComment = "";      // 对手上一步
        private string _mySan = "", _myComment = "";        // 学员上一步
        private readonly List<string[]> _why = new List<string[]>();   // {着法, 变化名, 为什么}
        private bool _studentToMove;

        // 「本步点评」的滚动状态（内容多的时候用滚轮看）
        private int _panelScroll;                 // 已经卷上去的高度（设备像素）
        private int _panelContentH;               // 内容总高度
        private int _panelViewH;                  // 可视高度
        private Rectangle _panelView;             // 可视区域
        private int _paintCount;                  // 重画次数（自检用：验证内容变化后确实重画了）

        private sealed class PanelBlock
        {
            public string Text = "";
            public float Size = 12.5f;
            public bool Bold;
            public Color Color = Theme.Text;
            public int LineH = 19;
            public int GapBefore;
        }

        private static void AddBlock(List<PanelBlock> list, string text, float size, bool bold, Color color,
                                     int lineH, int gapBefore)
        {
            PanelBlock b = new PanelBlock();
            b.Text = text;
            b.Size = size;
            b.Bold = bold;
            b.Color = color;
            b.LineH = lineH;
            b.GapBefore = gapBefore;
            list.Add(b);
        }

        public OpeningStudyScreen(MainForm main, Opening opening)
        {
            _main = main;
            _session = new StudySession(opening);

            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            MouseEnter += delegate { if (!Focused) Focus(); };

            _back = new FlatButton();
            _back.ButtonStyle = FlatButtonStyle.Ghost;
            _back.Text = "← 列表";
            _back.Click += delegate { _main.ShowOpeningList(); };
            Controls.Add(_back);

            _btnFullscreen = new FlatButton();
            _btnFullscreen.ButtonStyle = FlatButtonStyle.Ghost;
            _btnFullscreen.Text = "全屏";
            _btnFullscreen.Click += delegate { _main.ToggleFullscreen(); };
            Controls.Add(_btnFullscreen);

            _restart = new FlatButton();
            _restart.ButtonStyle = FlatButtonStyle.Secondary;
            _restart.Text = "重来";
            _restart.Click += delegate { RestartStudy(); };
            Controls.Add(_restart);

            _guideBtn = new FlatButton();
            _guideBtn.ButtonStyle = FlatButtonStyle.Secondary;
            _guideBtn.Text = "开局讲解";
            _guideBtn.Click += delegate
            {
                _showGuide = !_showGuide;
                _guideBtn.Text = _showGuide ? "本步点评" : "开局讲解";
                _guideBtn.Highlighted = _showGuide;
                _panelScroll = 0;
                Invalidate();
            };
            Controls.Add(_guideBtn);

            _title = Ui.MakeLabel(opening.Name, 20f, FontStyle.Bold, Theme.Text,
                new Rectangle(132, 18, 420, 28), ContentAlignment.MiddleLeft);
            Controls.Add(_title);
            _subtitle = Ui.MakeLabel((opening.Side == "w" ? "你执白" : "你执黑") + " · " + opening.English +
                                     " · " + opening.Eco + " · 跟着箭头走", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(134, 48, 500, 22), ContentAlignment.MiddleLeft);
            Controls.Add(_subtitle);

            _board.Game = _session.Game;
            _board.HumanSide = _session.StudentSide;
            _board.WhiteAtBottom = _session.StudentSide == Board.White;
            _board.MoveRequested += BoardMoveRequested;
            _board.MenuRequested += delegate { _main.ShowOpeningList(); };
            Controls.Add(_board);

            _reply.Interval = ReplyDelayMs;
            _reply.Tick += delegate { _reply.Stop(); DoOpponentReply(); };

            ApplyLayout();
            RefreshStep(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reply.Stop();
                _reply.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // 推进
        // ------------------------------------------------------------------
        /// <summary>刷新棋盘箭头、点评与着法列表。</summary>
        private void RefreshStep(bool first)
        {
            _board.Hints.Clear();
            _board.Allowed.Clear();
            _board.Interactive = false;

            if (_session.Finished)
            {
                _finished = true;
                _waitingReply = false;
                _prompt = "";
                _why.Clear();
                _studentToMove = false;
                _comment = "这条变化走完了。可以点「重来」再走一遍，或回到列表选别的开局。" +
                           (_variation.Length > 0 ? "  （走的是：" + _variation + "）" : "");
                AdvancePanel();
                return;
            }

            _finished = false;
            List<StudyStep> candidates = _session.Candidates();
            if (candidates.Count == 0)
            {
                _comment = "这一步之后没有更多教学变化了。";
                AdvancePanel();
                return;
            }

            if (_session.IsStudentTurn)
            {
                // 学员回合：把候选着法都画成箭头（多个 = 让学员选），
                // 并把"为什么走这里"逐条列出来 —— 这就是「本步点评」的第三段。
                _waitingReply = false;
                _studentToMove = true;
                _comment = "";
                _why.Clear();
                for (int i = 0; i < candidates.Count; i++)
                {
                    StudyStep s = candidates[i];
                    _board.Hints.Add(new BoardView.MoveHint(s.Move.From, s.Move.To));
                    _board.Allowed.Add(s.Move);
                    _why.Add(new string[] { s.San, s.VariationName, s.Comment });
                }
                _board.Interactive = true;
                if (candidates.Count == 1)
                {
                    _prompt = "该你走：照着箭头走这一步。";
                }
                else
                {
                    string names = "";
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i].VariationName.Length > 0)
                        {
                            names += (names.Length > 0 ? " / " : "") + candidates[i].VariationName;
                        }
                    }
                    _prompt = "该你选：这里有 " + candidates.Count + " 条变化（箭头就是它们），选一个走。" +
                               (names.Length > 0 ? "变化名：" + names : "");
                }
            }
            else
            {
                // 程序回合：延迟一下再应，像在思考
                _waitingReply = true;
                _studentToMove = false;
                _why.Clear();
                _prompt = "对手思考中…";
                _reply.Start();
            }
            AdvancePanel();
        }

        /// <summary>面板内容变了：滚回顶部并重画整个界面（只重画棋盘的话，面板看着像没推进）。</summary>
        private void AdvancePanel()
        {
            _panelScroll = 0;
            Invalidate();
            _board.Invalidate();
        }

        private void DoOpponentReply()
        {
            StudyStep s = _session.PlayOpponent();
            if (s != null)
            {
                _oppSan = s.San;
                _oppComment = s.Comment;
                if (s.VariationName.Length > 0)
                {
                    _variation = s.VariationName;
                    if (s.Note.Length > 0) _variationNote = s.Note;
                }
            }
            _board.SetLastMove(s != null ? s.Move : Chess.Move.Null);
            RefreshStep(false);
        }

        private void BoardMoveRequested(object sender, MoveRequestEventArgs e)
        {
            ApplyStudyMove(e.From, e.To);
        }

        /// <summary>走一步教学着法；不在变化里就提示并拒绝。</summary>
        private bool ApplyStudyMove(int from, int to)
        {
            if (_waitingReply || _finished) return false;
            StudyStep s = _session.PlayMove(from, to);
            if (s == null)
            {
                _flash = "这一步不在教学变化里，请按箭头走。";
                _board.ClearSelection();
                AdvancePanel();
                return false;
            }
            _flash = "";
            if (s.VariationName.Length > 0) _variation = s.VariationName;
            _mySan = s.San;
            _myComment = s.Comment;
            if (s.Note.Length > 0) _variationNote = s.Note;
            _board.SetLastMove(s.Move);
            _board.ClearSelection();
            RefreshStep(false);
            return true;
        }

        // ------------------------------------------------------------------
        // 供自检与截图工具使用
        // ------------------------------------------------------------------
        /// <summary>当前箭头数量（> 1 表示学员要选一个变化）。</summary>
        public int HintCount { get { return _board.Hints.Count; } }

        /// <summary>照第一个箭头走一步（等价于在棋盘上点这一步）。</summary>
        public bool PlayFirstCandidate()
        {
            List<StudyStep> cs = _session.Candidates();
            if (cs.Count == 0) return false;
            return ApplyStudyMove(cs[0].Move.From, cs[0].Move.To);
        }

        /// <summary>照第 index 个箭头走一步。</summary>
        public bool PlayCandidate(int index)
        {
            List<StudyStep> cs = _session.Candidates();
            if (index < 0 || index >= cs.Count) return false;
            return ApplyStudyMove(cs[index].Move.From, cs[index].Move.To);
        }

        /// <summary>故意走一步"不在变化里"的着法（自检用来验证会被拒绝）。</summary>
        public bool PlayOffBookMove()
        {
            List<Chess.Move> legal = _session.Game.LegalMoves();
            List<StudyStep> cs = _session.Candidates();
            for (int i = 0; i < legal.Count; i++)
            {
                bool book = false;
                for (int k = 0; k < cs.Count; k++)
                {
                    if (cs[k].Move.From == legal[i].From && cs[k].Move.To == legal[i].To) { book = true; break; }
                }
                if (!book) return ApplyStudyMove(legal[i].From, legal[i].To);
            }
            return false;
        }

        /// <summary>候选着法的名称（含变化名），自检用。</summary>
        public string CandidateNames()
        {
            List<StudyStep> cs = _session.Candidates();
            string s = "";
            for (int i = 0; i < cs.Count; i++)
            {
                s += (i > 0 ? "|" : "") + cs[i].San + (cs[i].VariationName.Length > 0 ? "(" + cs[i].VariationName + ")" : "");
            }
            return s;
        }

        public bool IsWaitingReply { get { return _waitingReply; } }

        /// <summary>界面上正在显示的「本步点评」全文（自检与截图工具用）。</summary>
        public string CurrentComment
        {
            get
            {
                if (_flash.Length > 0) return _flash;
                string s = _comment;
                if (_oppSan.Length > 0) s += "  对手上一步 " + _oppSan + "：" + _oppComment;
                if (_mySan.Length > 0) s += "  你上一步 " + _mySan + "：" + _myComment;
                for (int i = 0; i < _why.Count; i++)
                    s += "  该你走 " + _why[i][0] + "：" + _why[i][2];
                if (_prompt.Length > 0) s += "  " + _prompt;
                return s;
            }
        }

        /// <summary>「该你走」列出的候选条数（自检用）。</summary>
        public int WhyCount { get { return _why.Count; } }

        /// <summary>「该你走」的着法与理由（自检用）。</summary>
        public string WhyText
        {
            get
            {
                string s = "";
                for (int i = 0; i < _why.Count; i++)
                    s += (i > 0 ? " | " : "") + _why[i][0] + "：" + _why[i][2];
                return s;
            }
        }

        /// <summary>对手上一步的着法与用意（自检用）。</summary>
        public string OpponentLast { get { return _oppSan + "：" + _oppComment; } }

        /// <summary>只取顶部那句话（自检用）。</summary>
        public string LastComment { get { return _comment; } }

        public StudySession Session { get { return _session; } }

        private void RestartStudy()
        {
            _reply.Stop();
            _session.Restart();
            _comment = "";
            _prompt = "";
            _variation = "";
            _variationNote = "";
            _flash = "";
            _board.SetLastMove(Chess.Move.Null);
            _board.ClearSelection();
            RefreshStep(true);
        }

        // ------------------------------------------------------------------
        // 布局与绘制
        // ------------------------------------------------------------------
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_title == null || Width <= 0) return;
            UiScale.Fit(Width, Height, LogicalW, LogicalH);

            UiScale.Font(_title, 20f, FontStyle.Bold);
            UiScale.Font(_subtitle, 13f, FontStyle.Regular);
            UiScale.Font(_back, 14.5f, FontStyle.Regular);
            UiScale.Font(_btnFullscreen, 14f, FontStyle.Regular);
            UiScale.Font(_restart, 15f, FontStyle.Regular);
            _btnFullscreen.Text = _main.IsFullscreen ? "退出" : "全屏";

            _back.Bounds = UiScale.R(22, 20, 96, 36);
            _btnFullscreen.Bounds = UiScale.R(834, 20, 84, 34);
            _title.Bounds = UiScale.R(132, 18, 420, 28);
            _subtitle.Bounds = UiScale.R(134, 48, 520, 22);
            _restart.Bounds = UiScale.R(44, 588, 128, 42);
            _guideBtn.Bounds = UiScale.R(182, 588, 124, 42);
            UiScale.Font(_guideBtn, 15f, FontStyle.Regular);
            _board.Bounds = UiScale.R(350, 100, 560, 560);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _paintCount++;
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            Rectangle card = UiScale.R(30, 100, 290, 560);
            using (System.Drawing.Drawing2D.GraphicsPath path = Theme.RoundRect(card, UiScale.Px(14)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }

            int x = card.X + UiScale.Px(18);
            int w = card.Width - UiScale.Px(36);
            int y = card.Y + UiScale.Px(18);

            Theme.DrawText(g, _session.Opening.Name, Theme.Font(17f, FontStyle.Bold), Theme.Text,
                new Rectangle(x, y, w, UiScale.Px(28)), ContentAlignment.MiddleLeft);
            y += UiScale.Px(32);

            // 总体思路（自动折行）
            y = DrawWrapped(g, _session.Opening.Summary, Theme.Font(13f, FontStyle.Regular), Theme.TextMuted,
                            x, y, w, UiScale.Px(20));

            // 「本步点评」/「开局讲解」：内容多时可以滚轮滚动
            y += UiScale.Px(10);
            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1f)))
            {
                g.DrawLine(p, x, y, x + w, y);
            }
            y += UiScale.Px(10);
            _panelView = new Rectangle(x, y, w, UiScale.R(44, 588, 128, 42).Top - UiScale.Px(12) - y);
            DrawPanel(g, _panelView);
        }

        /// <summary>组装当前要显示的内容（本步点评 或 开局讲解）。</summary>
        private List<PanelBlock> BuildPanel()
        {
            List<PanelBlock> list = new List<PanelBlock>();
            if (_showGuide)
            {
                BuildGuideBlocks(list);
                return list;
            }

            if (_flash.Length > 0)
            {
                AddBlock(list, _flash, 14f, true, Theme.Danger, 22, 0);
                return list;
            }
            if (_comment.Length > 0) AddBlock(list, _comment, 14f, true, Theme.Text, 22, 0);

            // ① 对手上一步：它有什么用
            if (_oppSan.Length > 0)
            {
                AddBlock(list, "对手上一步  " + _oppSan, 13f, true, Theme.AccentDark, 21, 8);
                if (_oppComment.Length > 0) AddBlock(list, _oppComment, 12.5f, false, Theme.Text, 19, 0);
            }
            // ② 我上一步：它在做什么
            if (_mySan.Length > 0)
            {
                AddBlock(list, "你上一步  " + _mySan, 13f, true, Theme.TextMuted, 21, 8);
                if (_myComment.Length > 0) AddBlock(list, _myComment, 12.5f, false, Theme.Text, 19, 0);
            }
            // ③ 该我走：为什么走这里（有几条变化就列几条）
            if (_studentToMove && _why.Count > 0)
            {
                AddBlock(list, _why.Count == 1 ? "该你走 · 为什么要走这里" : "该你选 · 每条变化为什么这样走",
                         13f, true, Theme.Text, 22, 10);
                for (int i = 0; i < _why.Count; i++)
                {
                    string head = (_why.Count == 1 ? "· " : (i + 1) + ". ") + _why[i][0];
                    if (_why[i][1].Length > 0) head += "（" + _why[i][1] + "）";
                    AddBlock(list, head, 13f, true, Theme.AccentDark, 20, 6);
                    AddBlock(list, _why[i][2], 12.5f, false, Theme.Text, 19, 0);
                }
            }
            if (_variation.Length > 0)
            {
                AddBlock(list, "当前变化：" + _variation, 13f, false, Theme.AccentDark, 20, 10);
                if (_variationNote.Length > 0) AddBlock(list, _variationNote, 12.5f, false, Theme.TextMuted, 19, 0);
            }
            if (_prompt.Length > 0) AddBlock(list, _prompt, 12.5f, false, Theme.TextMuted, 19, 8);

            // 着法记录（全部列出来，多了就滚动看）
            AddBlock(list, "着法记录", 13f, false, Theme.TextMuted, 20, 14);
            List<string> sans = PlayedSans();
            if (sans.Count == 0)
            {
                AddBlock(list, "（还没走）", 13f, false, Theme.TextMuted, 22, 0);
            }
            else
            {
                for (int i = 0; i < sans.Count; i++)
                {
                    AddBlock(list, (i / 2 + 1) + (i % 2 == 0 ? ". " : "… ") + sans[i],
                             13.5f, false, Theme.Text, 24, 0);
                }
            }
            return list;
        }

        /// <summary>「开局讲解」的内容块。</summary>
        private void BuildGuideBlocks(List<PanelBlock> list)
        {
            Opening op = _session.Opening;
            AddBlock(list, "难度定位：" + op.Level, 13.5f, true, Theme.AccentDark, 21, 0);
            if (op.Plans != null && op.Plans.Length > 0)
            {
                AddBlock(list, "典型计划", 13f, true, Theme.Text, 22, 10);
                for (int i = 0; i < op.Plans.Length; i++)
                    AddBlock(list, (i + 1) + ". " + op.Plans[i], 12.2f, false, Theme.Text, 17, 0);
            }
            if (op.Pitfalls != null && op.Pitfalls.Length > 0)
            {
                AddBlock(list, "常见错误", 13f, true, Theme.Text, 22, 10);
                for (int i = 0; i < op.Pitfalls.Length; i++)
                    AddBlock(list, "· " + op.Pitfalls[i], 12.2f, false, Theme.Text, 17, 0);
            }
            bool any = false;
            for (int i = 0; i < op.Nodes.Length; i++)
            {
                if (op.Nodes[i].VariationName.Length > 0 && op.Nodes[i].Note.Length > 0) { any = true; break; }
            }
            if (!any) return;
            AddBlock(list, "主要变化", 13f, true, Theme.Text, 22, 10);
            for (int i = 0; i < op.Nodes.Length; i++)
            {
                OpeningMove nd = op.Nodes[i];
                if (nd.VariationName.Length == 0 || nd.Note.Length == 0) continue;
                AddBlock(list, "· " + nd.VariationName + "：" + nd.Note, 12f, false, Theme.TextMuted, 16, 0);
            }
        }

        /// <summary>画面板：先量出内容高度，再按滚动位置裁剪绘制。</summary>
        private void DrawPanel(Graphics g, Rectangle view)
        {
            if (view.Height <= 0) return;
            Theme.PrepareText(g);
            List<PanelBlock> blocks = BuildPanel();

            _panelContentH = 0;
            int[] heights = new int[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                Font f = BlockFont(blocks[i]);
                heights[i] = WrappedHeight(g, blocks[i].Text, f, view.Width, UiScale.Px(blocks[i].LineH));
                if (heights[i] <= 0) heights[i] = UiScale.Px(blocks[i].LineH);
                _panelContentH += UiScale.Px(blocks[i].GapBefore) + heights[i];
            }
            _panelViewH = view.Height;
            int maxScroll = Math.Max(0, _panelContentH - _panelViewH);
            if (_panelScroll > maxScroll) _panelScroll = maxScroll;
            if (_panelScroll < 0) _panelScroll = 0;

            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.SetClip(view);
            int yy = view.Y - _panelScroll;
            for (int i = 0; i < blocks.Count; i++)
            {
                yy += UiScale.Px(blocks[i].GapBefore);
                yy = DrawWrapped(g, blocks[i].Text, BlockFont(blocks[i]), blocks[i].Color,
                                 view.X, yy, view.Width, UiScale.Px(blocks[i].LineH));
            }
            g.Restore(state);

            if (maxScroll > 0) DrawScrollBar(g, view, maxScroll);
        }

        private static Font BlockFont(PanelBlock b)
        {
            return Theme.Font(b.Size, b.Bold ? FontStyle.Bold : FontStyle.Regular);
        }

        /// <summary>右侧细滚动条 + "滚轮可滚动"提示。</summary>
        private void DrawScrollBar(Graphics g, Rectangle view, int maxScroll)
        {
            int barW = Math.Max(2, UiScale.Px(4));
            int barX = view.Right + UiScale.Px(3);
            int thumbH = Math.Max(UiScale.Px(26), (int)((long)view.Height * _panelViewH / Math.Max(1, _panelContentH)));
            int thumbY = view.Y + (view.Height - thumbH) * _panelScroll / Math.Max(1, maxScroll);
            using (SolidBrush track = new SolidBrush(Theme.WithAlpha(Theme.Border, 130)))
                g.FillRectangle(track, barX, view.Y, barW, view.Height);
            using (SolidBrush thumb = new SolidBrush(Theme.Accent))
                g.FillRectangle(thumb, barX, thumbY, barW, thumbH);

            string tip = "滚轮可滚动";
            Rectangle chip = new Rectangle(view.X + view.Width - UiScale.Px(86), view.Bottom - UiScale.Px(18),
                                           UiScale.Px(86), UiScale.Px(18));
            using (SolidBrush bg = new SolidBrush(Theme.WithAlpha(Theme.Surface, 220)))
                g.FillRectangle(bg, chip);
            Theme.DrawText(g, tip, Theme.Font(11.5f, FontStyle.Regular), Theme.TextMuted, chip,
                           ContentAlignment.MiddleRight);
        }

        /// <summary>滚轮滚动面板；返回是否真的滚动了（主窗体也会转发到这里）。</summary>
        public bool ScrollPanel(int delta)
        {
            int maxScroll = Math.Max(0, _panelContentH - _panelViewH);
            if (maxScroll <= 0) return false;
            int steps = delta / 120;
            if (steps == 0) steps = delta > 0 ? 1 : -1;
            int next = _panelScroll - steps * UiScale.Px(48);
            if (next < 0) next = 0;
            if (next > maxScroll) next = maxScroll;
            if (next == _panelScroll) return false;
            _panelScroll = next;
            Invalidate();
            return true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (ScrollPanel(e.Delta)) return;
            base.OnMouseWheel(e);
        }

        /// <summary>面板当前滚动位置 / 最大可滚动距离 / 重画次数（自检用）。</summary>
        public int PanelScroll { get { return _panelScroll; } }

        public int PanelMaxScroll { get { return Math.Max(0, _panelContentH - _panelViewH); } }

        public int PaintCount { get { return _paintCount; } }

        /// <summary>一段文字按宽度折行后需要多少高度（用于"整条放不下就不画"）。</summary>
        private int WrappedHeight(Graphics g, string text, Font font, int w, int lineH)
        {
            return WrapLines(g, text, font, w).Count * lineH;
        }

        /// <summary>画一个小标题 + 若干条（带序号的计划 / 带点的提醒）。</summary>
        private int GuideSection(Graphics g, string title, string[] items, bool numbered,
                                 int x, int y, int w, int bottomLimit)
        {
            if (items == null || items.Length == 0) return y;
            if (y + UiScale.Px(26) > bottomLimit) return bottomLimit;
            y += UiScale.Px(6);
            Theme.DrawText(g, title, Theme.Font(13f, FontStyle.Bold), Theme.Text,
                new Rectangle(x, y, w, UiScale.Px(22)), ContentAlignment.MiddleLeft);
            y += UiScale.Px(24);
            for (int i = 0; i < items.Length; i++)
            {
                string prefix = numbered ? (i + 1) + ". " : "· ";
                int need = WrappedHeight(g, prefix + items[i], Theme.Font(12.2f, FontStyle.Regular), w,
                                         UiScale.Px(17));
                if (y + need > bottomLimit) return y;
                y = DrawWrapped(g, prefix + items[i], Theme.Font(12.2f, FontStyle.Regular), Theme.Text,
                                x, y, w, UiScale.Px(17));
            }
            return y;
        }

        /// <summary>供自检用：是否正在显示开局讲解。</summary>
        public bool ShowingGuide { get { return _showGuide; } }

        /// <summary>供自检用：切换讲解视图。</summary>
        public void ToggleGuide()
        {
            _showGuide = !_showGuide;
            _guideBtn.Text = _showGuide ? "本步点评" : "开局讲解";
            _guideBtn.Highlighted = _showGuide;
            Invalidate();
        }

        /// <summary>已经走过的着法（SAN）。</summary>
        private List<string> PlayedSans()
        {
            List<string> list = new List<string>();
            IList<int> path = _session.Path;
            for (int i = 0; i < path.Count; i++)
            {
                list.Add(_session.Opening.Nodes[path[i]].San);
            }
            return list;
        }

        /// <summary>画一段"标签 + 着法 + 用意"的点评（对手上一步 / 你上一步）。</summary>
        private int DrawLabeled(Graphics g, string label, string san, string comment, Color labelColor,
                                int x, int y, int w)
        {
            y += UiScale.Px(4);
            Theme.DrawText(g, label + "  " + san, Theme.Font(13f, FontStyle.Bold), labelColor,
                new Rectangle(x, y, w, UiScale.Px(21)), ContentAlignment.MiddleLeft);
            y += UiScale.Px(22);
            if (comment.Length > 0)
                y = DrawWrapped(g, comment, Theme.Font(12.5f, FontStyle.Regular), Theme.Text,
                                x, y, w, UiScale.Px(19));
            return y;
        }

        /// <summary>按宽度折行画一段文字，返回下一行的 y。</summary>
        private int DrawWrapped(Graphics g, string text, Font font, Color color, int x, int y, int w, int lineH)
        {
            if (string.IsNullOrEmpty(text)) return y;
            Theme.PrepareText(g);
            List<string> lines = WrapLines(g, text, font, w);
            using (Brush b = new SolidBrush(color))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    g.DrawString(lines[i], font, b, new RectangleF(x, y, w, lineH));
                    y += lineH;
                }
            }
            return y;
        }

        /// <summary>把一段文字按宽度折成若干行（中文按字、英文按空格断行）。</summary>
        private static List<string> WrapLines(Graphics g, string text, Font font, int w)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            Theme.PrepareText(g);
            string line = "";
            string[] words = SplitForWrap(text);
            for (int i = 0; i < words.Length; i++)
            {
                string trial = line.Length == 0 ? words[i] : line + words[i];
                SizeF size = g.MeasureString(trial, font);
                if (size.Width > w && line.Length > 0)
                {
                    lines.Add(line);
                    line = words[i];
                }
                else
                {
                    line = trial;
                }
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        /// <summary>中文按字断行，英文按空格断行：把文字切成可拼接的小块。</summary>
        private static string[] SplitForWrap(string text)
        {
            List<string> parts = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c > 0x2E80)
                {
                    parts.Add(c.ToString());       // 汉字/全角标点：一字一块
                    i++;
                }
                else
                {
                    int j = i;
                    while (j < text.Length && text[j] <= 0x2E80 && text[j] != ' ') j++;
                    if (j > i) parts.Add(text.Substring(i, j - i));
                    if (j < text.Length && text[j] == ' ') { parts.Add(" "); j++; }
                    i = j;
                }
            }
            return parts.ToArray();
        }
    }
}
