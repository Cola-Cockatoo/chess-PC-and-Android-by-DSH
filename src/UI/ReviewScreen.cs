using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 复盘界面（多邻国式）：自动走棋 + 亲切解说；
    /// 关键步停下深入点评，臭棋退回重下（答对后真的把棋走上去、停下等「继续」）。
    /// 支持「上一个/下一个关键步」跳转、控制格与 AI 解析开关。
    /// </summary>
    public class ReviewScreen : UserControl
    {
        private const float LogicalW = 940f;
        private const float LogicalH = 700f;
        private const int AutoMs = 1150;

        private readonly MainForm _main;
        private readonly GameMode _mode;
        private readonly int _humanSide;
        private readonly ChessGame _game;
        private Review _review;
        private ReviewLesson _lesson;

        private readonly BoardView _board = new BoardView();
        private readonly FlatButton _back;
        private readonly FlatButton _btnFullscreen;
        private readonly FlatButton _btnControl;
        private readonly FlatButton _btnAnalysis;
        private readonly FlatButton _btnContinue;
        private readonly FlatButton _btnPrev;
        private readonly FlatButton _btnNext;
        private readonly FlatButton _btnRestart;
        private readonly Label _title;
        private readonly Label _subtitle;

        private readonly System.Windows.Forms.Timer _auto = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer();

        private volatile bool _building;
        private volatile int _buildDone, _buildTotal;
        private volatile string _buildError;
        private int _scroll, _contentH, _viewH;
        private bool _controlOn;
        private bool _analysisOn;
        private string _flash = "";

        public ReviewScreen(MainForm main, ChessGame game, GameMode mode, int humanSide)
        {
            _main = main;
            _game = game;
            _mode = mode;
            _humanSide = humanSide;

            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);

            _back = new FlatButton();
            _back.ButtonStyle = FlatButtonStyle.Ghost;
            _back.Text = "← 返回";
            _back.Click += delegate { _main.ShowMenu(); };
            Controls.Add(_back);

            _btnFullscreen = new FlatButton();
            _btnFullscreen.ButtonStyle = FlatButtonStyle.Ghost;
            _btnFullscreen.Text = "全屏";
            _btnFullscreen.Click += delegate { _main.ToggleFullscreen(); };
            Controls.Add(_btnFullscreen);

            _btnControl = new FlatButton();
            _btnControl.ButtonStyle = FlatButtonStyle.Secondary;
            _btnControl.Text = "控制格 · 关";
            _btnControl.Click += delegate { SetControl(!_controlOn); };
            Controls.Add(_btnControl);

            _btnAnalysis = new FlatButton();
            _btnAnalysis.ButtonStyle = FlatButtonStyle.Secondary;
            _btnAnalysis.Text = "AI 解析 · 关";
            _btnAnalysis.Click += delegate { SetAnalysis(!_analysisOn); };
            Controls.Add(_btnAnalysis);

            _title = Ui.MakeLabel("对局复盘", 20f, FontStyle.Bold, Theme.Text,
                new Rectangle(120, 16, 380, 30), ContentAlignment.MiddleLeft);
            Controls.Add(_title);
            _subtitle = Ui.MakeLabel("正在准备…", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(122, 46, 520, 22), ContentAlignment.MiddleLeft);
            Controls.Add(_subtitle);

            _btnContinue = new FlatButton();
            _btnContinue.ButtonStyle = FlatButtonStyle.Primary;
            _btnContinue.Text = "继续 ▶";
            _btnContinue.Click += delegate { ContinueClicked(); };
            Controls.Add(_btnContinue);

            _btnPrev = new FlatButton();
            _btnPrev.ButtonStyle = FlatButtonStyle.Secondary;
            _btnPrev.Text = "◀ 上一步";
            _btnPrev.Click += delegate { StepBackOnce(); };
            Controls.Add(_btnPrev);

            _btnNext = new FlatButton();
            _btnNext.ButtonStyle = FlatButtonStyle.Secondary;
            _btnNext.Text = "下一个关键步 ▶";
            _btnNext.Click += delegate { JumpKey(1); };
            Controls.Add(_btnNext);

            _btnRestart = new FlatButton();
            _btnRestart.ButtonStyle = FlatButtonStyle.Secondary;
            _btnRestart.Text = "重新开始";
            _btnRestart.Click += delegate { Restart(); };
            Controls.Add(_btnRestart);

            _board.Interactive = false;
            _board.MoveRequested += BoardMoveRequested;
            Controls.Add(_board);

            _auto.Interval = AutoMs;
            _auto.Tick += delegate { AutoTick(); };
            _poll.Interval = 120;
            _poll.Tick += delegate { PollBuild(); };

            ApplyLayout();
            StartBuild();
        }

        // ---------------- 建复盘 ----------------
        private void StartBuild()
        {
            _building = true;
            _buildDone = 0;
            _buildTotal = Math.Max(1, _game != null ? _game.MoveHistory.Count : 1);
            _buildError = null;
            Thread t = new Thread(delegate()
            {
                try
                {
                    _review = Review.Build(_game, _mode, _humanSide, Analyzer.Profile(4, 45), new Random(20260101),
                        delegate(int done, int total) { _buildDone = done; _buildTotal = total; });
                }
                catch (Exception ex) { _buildError = ex.Message; }
                _building = false;
            });
            t.IsBackground = true;
            t.Start();
            _poll.Start();
            Invalidate();
        }

        private void PollBuild()
        {
            if (_building) { Invalidate(); return; }
            _poll.Stop();
            if (_buildError != null) { _flash = "复盘准备失败：" + _buildError; Invalidate(); return; }
            _lesson = new ReviewLesson(_review);
            _board.Interactive = false;
            SyncBoard();
            Invalidate();
            StartAuto();
        }

        // ---------------- 自动播放 ----------------
        private void StartAuto()
        {
            if (_lesson == null) return;
            if (_lesson.Mode == LessonMode.Auto) _auto.Start(); else _auto.Stop();
            RefreshButtons();
        }

        private void AutoTick()
        {
            if (_lesson == null) { _auto.Stop(); return; }
            if (_lesson.Mode != LessonMode.Auto) { _auto.Stop(); RefreshButtons(); return; }
            bool go = _lesson.Advance();
            _scroll = int.MaxValue;
            SyncBoard();
            Invalidate();
            if (!go || _lesson.Waiting) { _auto.Stop(); RefreshButtons(); }
        }

        private void ContinueClicked()
        {
            if (_lesson == null) return;
            if (_lesson.Mode == LessonMode.Done) { Restart(); return; }
            if (_lesson.Mode == LessonMode.KeyStop || _lesson.Mode == LessonMode.Correct)
            {
                _lesson.Continue();
                _scroll = int.MaxValue;
                SyncBoard();
                Invalidate();
                StartAuto();
                return;
            }
            if (_lesson.Mode == LessonMode.Auto)
            {
                _lesson.Advance();
                SyncBoard();
                Invalidate();
                RefreshButtons();
            }
        }

        /// <summary>回到上一步：停住自动播放，让用户看清刚才那一手。</summary>
        private void StepBackOnce()
        {
            if (_lesson == null) return;
            _auto.Stop();
            if (!_lesson.StepBack()) return;
            _scroll = int.MaxValue;
            SyncBoard();
            Invalidate();
            RefreshButtons();
        }

        private void JumpKey(int dir)
        {
            if (_lesson == null) return;
            _auto.Stop();
            if (!_lesson.JumpToKey(dir)) return;
            _scroll = int.MaxValue;
            SyncBoard();
            Invalidate();
            RefreshButtons();
        }

        private void Restart()
        {
            _auto.Stop();
            _scroll = 0;
            _flash = "";
            if (_review == null) { StartBuild(); return; }
            _lesson = new ReviewLesson(_review);
            _board.Interactive = false;
            SyncBoard();
            Invalidate();
            StartAuto();
        }

        // ---------------- 棋盘同步 / 答题 ----------------
        private void SyncBoard()
        {
            if (_lesson == null || _review == null) return;
            int ply = _lesson.DisplayPly;
            ChessGame shown = _review.GameAt(ply);
            bool showApplied = _lesson.Mode == LessonMode.Correct && !_lesson.QuizApplied.IsNull;
            if (showApplied) shown.Play(_lesson.QuizApplied);

            _board.Game = shown;
            _board.WhiteAtBottom = _mode == GameMode.HumanVsAi
                ? _humanSide == Board.White
                : shown.Position.Side == Board.White;
            _board.HumanSide = shown.Position.Side;
            Chess.Move last = Chess.Move.Null;
            if (showApplied) last = _lesson.QuizApplied;
            else if (ply > 0) last = _review.Moves[ply - 1];
            _board.SetLastMove(last);
            _board.ClearSelection();
            _board.ShowControl = _controlOn;
            _board.Interactive = _lesson.Mode == LessonMode.Quiz;
        }

        private void BoardMoveRequested(object sender, MoveRequestEventArgs e)
        {
            if (_lesson == null || _lesson.Mode != LessonMode.Quiz) return;
            ChessGame shown = _review.GameAt(_lesson.DisplayPly);
            Chess.Move mv = Chess.Move.Null;
            List<Chess.Move> legal = shown.LegalMoves();
            for (int i = 0; i < legal.Count; i++)
                if (legal[i].From == e.From && legal[i].To == e.To) { mv = legal[i]; break; }
            if (mv.IsNull) return;

            string msg;
            QuizVerdict v = _lesson.Submit(mv, out msg);
            _scroll = int.MaxValue;
            SyncBoard();
            Invalidate();
            if (v == QuizVerdict.Correct) { _auto.Stop(); RefreshButtons(); }
            else RefreshButtons();
        }

        // ---------------- 布局 ----------------
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
            UiScale.Font(_btnControl, 13.5f, FontStyle.Regular);
            UiScale.Font(_btnAnalysis, 13.5f, FontStyle.Regular);
            UiScale.Font(_btnContinue, 15.5f, FontStyle.Bold);
            UiScale.Font(_btnPrev, 13.5f, FontStyle.Regular);
            UiScale.Font(_btnNext, 13.5f, FontStyle.Regular);
            UiScale.Font(_btnRestart, 13.5f, FontStyle.Regular);
            _btnFullscreen.Text = _main.IsFullscreen ? "退出" : "全屏";

            _back.Bounds = UiScale.R(22, 20, 96, 36);
            _btnFullscreen.Bounds = UiScale.R(834, 20, 84, 34);
            _btnAnalysis.Bounds = UiScale.R(686, 20, 140, 34);
            _btnControl.Bounds = UiScale.R(566, 20, 114, 34);
            _title.Bounds = UiScale.R(132, 14, 300, 30);
            _subtitle.Bounds = UiScale.R(134, 44, 420, 22);

            _board.Bounds = UiScale.R(30, 92, 470, 470);

            // 按钮全部在文本框下方，绝不压住对话框
            int by = 636;
            _btnContinue.Bounds = UiScale.R(516, by, 196, 44);
            _btnRestart.Bounds = UiScale.R(722, by, 190, 44);
            _btnPrev.Bounds = UiScale.R(516, by - 52, 196, 42);
            _btnNext.Bounds = UiScale.R(722, by - 52, 190, 42);
            Invalidate();
        }

        private void RefreshButtons()
        {
            if (_lesson == null) return;
            bool done = _lesson.Mode == LessonMode.Done;
            bool keyStop = _lesson.Mode == LessonMode.KeyStop;
            bool correct = _lesson.Mode == LessonMode.Correct;
            _btnContinue.Enabled = keyStop || correct || done || _lesson.Mode == LessonMode.Auto;
            _btnContinue.Text = done ? "再来一遍" : (correct ? "继续讲解 ▶" : (keyStop ? "继续 ▶" : "下一步 ▶"));
            _btnPrev.Enabled = _lesson.CanStepBack;
            _btnNext.Enabled = _lesson.CanJump(1);
        }

        // ---------------- 绘制 ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            Rectangle panel = UiScale.R(516, 92, 396, 484);
            using (GraphicsPath path = Theme.RoundRect(panel, UiScale.Px(14)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }

            int x = panel.X + UiScale.Px(14);
            int w = panel.Width - UiScale.Px(28);
            int y = panel.Y + UiScale.Px(12);

            string sub = _lesson != null ? _lesson.ProgressText : "正在准备复盘…";
            if (_lesson != null)
            {
                if (_lesson.Mode == LessonMode.Quiz) sub += " · 该你走了";
                else if (_lesson.Mode == LessonMode.KeyStop || _lesson.Mode == LessonMode.Correct) sub += " · 点「继续」听讲解";
                else if (_lesson.Mode == LessonMode.Done) sub += " · 已讲完";
            }
            _subtitle.Text = sub;
            Theme.DrawText(g, sub, Theme.Font(12.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(x, y, w, UiScale.Px(20)), ContentAlignment.MiddleLeft);
            y += UiScale.Px(24);

            if (_analysisOn) y = DrawAnalysisStrip(g, x, y, w);

            if (_lesson == null)
            {
                int total = Math.Max(1, _buildTotal);
                int done = Math.Min(_buildDone, total);
                Theme.DrawText(g, "正在给这盘棋逐步打分… " + done + " / " + total,
                    Theme.Font(14f, FontStyle.Bold), Theme.Text,
                    new Rectangle(x, y, w, UiScale.Px(24)), ContentAlignment.MiddleLeft);
                y += UiScale.Px(30);
                int barW = w - UiScale.Px(8);
                using (SolidBrush bg = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(bg, x, y, barW, UiScale.Px(8));
                using (SolidBrush fg = new SolidBrush(Theme.Accent)) g.FillRectangle(fg, x, y, (int)(barW * (double)done / total), UiScale.Px(8));
                if (_flash.Length > 0)
                {
                    y += UiScale.Px(18);
                    DrawWrapped(g, _flash, Theme.Font(13f, FontStyle.Bold), Theme.Danger, x, y, w, UiScale.Px(20));
                }
                return;
            }

            DrawLog(g, new Rectangle(x, y, w, panel.Bottom - UiScale.Px(12) - y));
        }

        /// <summary>AI 解析条：当前局面的胜率与这一手的评价。</summary>
        private int DrawAnalysisStrip(Graphics g, int x, int y, int w)
        {
            ReviewStep s = _lesson != null ? _lesson.CurrentStep : null;
            if (s == null && _review != null && _review.Count > 0) s = _review.Steps[0];
            if (s == null) return y;

            double rate = AnalysisMath.WinRate(s.EvalCp);
            string grade = s.Grade != null ? (s.Grade.Symbol + " " + s.Grade.Chinese) : "";
            using (SolidBrush bg = new SolidBrush(Theme.AccentSoft))
            using (GraphicsPath path = Theme.RoundRect(new Rectangle(x, y, w, UiScale.Px(38)), UiScale.Px(9)))
                g.FillPath(bg, path);

            Theme.DrawText(g, "白方胜率 " + Math.Round(rate) + "%",
                Theme.Font(13f, FontStyle.Bold), Theme.AccentDark,
                new Rectangle(x + UiScale.Px(10), y, UiScale.Px(120), UiScale.Px(38)), ContentAlignment.MiddleLeft);
            Theme.DrawText(g, "上一步 " + s.San + (grade.Length > 0 ? "  " + grade : ""),
                Theme.Font(12.5f, FontStyle.Regular), Theme.Text,
                new Rectangle(x + UiScale.Px(126), y, w - UiScale.Px(136), UiScale.Px(38)), ContentAlignment.MiddleLeft);
            return y + UiScale.Px(46);
        }

        private void DrawLog(Graphics g, Rectangle view)
        {
            List<string> log = _lesson.Log;
            int lineH = UiScale.Px(21);
            List<string> lines = new List<string>();
            for (int i = 0; i < log.Count; i++)
            {
                if (lines.Count > 0) lines.Add("");
                lines.AddRange(WrapLines(g, log[i], Theme.Font(13.5f, FontStyle.Regular), view.Width - UiScale.Px(6)));
            }
            _contentH = lines.Count * lineH;
            _viewH = view.Height;
            int maxScroll = Math.Max(0, _contentH - _viewH);
            if (_scroll == int.MaxValue) _scroll = maxScroll;
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;

            GraphicsState st = g.Save();
            g.SetClip(view);
            int yy = view.Y - _scroll;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length > 0)
                {
                    bool last = i >= lines.Count - 2;
                    bool wait = _lesson.Mode == LessonMode.KeyStop || _lesson.Mode == LessonMode.Correct;
                    Theme.DrawText(g, lines[i],
                        Theme.Font(13.5f, (last && wait) ? FontStyle.Bold : FontStyle.Regular),
                        (last && wait) ? Theme.AccentDark : Theme.Text,
                        new Rectangle(view.X, yy, view.Width, lineH), ContentAlignment.MiddleLeft);
                }
                yy += lineH;
            }
            g.Restore(st);

            if (maxScroll > 0)
            {
                int barW = Math.Max(2, UiScale.Px(4));
                int barX = view.Right + UiScale.Px(1);
                int thumbH = Math.Max(UiScale.Px(24), (int)((long)view.Height * _viewH / Math.Max(1, _contentH)));
                int thumbY = view.Y + (view.Height - thumbH) * _scroll / Math.Max(1, maxScroll);
                using (SolidBrush track = new SolidBrush(Theme.WithAlpha(Theme.Border, 120)))
                    g.FillRectangle(track, barX, view.Y, barW, view.Height);
                using (SolidBrush thumb = new SolidBrush(Theme.Accent))
                    g.FillRectangle(thumb, barX, thumbY, barW, thumbH);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int maxScroll = Math.Max(0, _contentH - _viewH);
            if (maxScroll > 0)
            {
                int next = _scroll - e.Delta / 120 * UiScale.Px(52);
                if (next < 0) next = 0;
                if (next > maxScroll) next = maxScroll;
                if (next != _scroll) { _scroll = next; Invalidate(); }
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) { ContinueClicked(); return true; }
            if (keyData == Keys.Left) { StepBackOnce(); return true; }
            if (keyData == Keys.Right) { JumpKey(1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static List<string> WrapLines(Graphics g, string text, Font font, int w)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
            Theme.PrepareText(g);
            string line = "";
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                string piece;
                if (c > 0x2E80) { piece = c.ToString(); i++; }
                else
                {
                    int j = i;
                    while (j < text.Length && text[j] <= 0x2E80) j++;
                    piece = text.Substring(i, j - i);
                    i = j;
                }
                string trial = line + piece;
                if (line.Length > 0 && g.MeasureString(trial, font).Width > w) { lines.Add(line); line = piece.TrimStart(); }
                else line = trial;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        private int DrawWrapped(Graphics g, string text, Font font, Color color, int x, int y, int w, int lineH)
        {
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

        // ---------------- 自检接口 ----------------
        public ReviewLesson Lesson { get { return _lesson; } }

        public bool IsBuilding { get { return _lesson == null && _buildError == null; } }

        public string LogText
        {
            get { return _lesson == null ? "" : string.Join(" ", _lesson.Log.ToArray()); }
        }

        public void WaitForReady(int timeoutMs)
        {
            DateTime t0 = DateTime.UtcNow;
            while (IsBuilding && (DateTime.UtcNow - t0).TotalMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(20);
            }
        }

        public void DriveToStop()
        {
            if (_lesson == null) return;
            int guard = 0;
            while (_lesson.Mode == LessonMode.Auto && guard++ < 500) _lesson.Advance();
            SyncBoard();
            RefreshButtons();
            Invalidate();
        }

        public void PressContinue() { ContinueClicked(); }

        public void PressJump(int dir) { JumpKey(dir); }

        public void ToggleControlSquares() { SetControl(!_controlOn); }

        public void ToggleAnalysis() { SetAnalysis(!_analysisOn); }

        private void SetControl(bool on)
        {
            _controlOn = on;
            _btnControl.Text = on ? "控制格 · 开" : "控制格 · 关";
            _btnControl.Highlighted = on;
            _board.ShowControl = on;
            _board.Invalidate();
            Invalidate();
        }

        private void SetAnalysis(bool on)
        {
            _analysisOn = on;
            _btnAnalysis.Text = on ? "AI 解析 · 开" : "AI 解析 · 关";
            _btnAnalysis.Highlighted = on;
            Invalidate();
        }

        public bool ControlOn { get { return _controlOn; } }

        public bool AnalysisOn { get { return _analysisOn; } }
    }
}
