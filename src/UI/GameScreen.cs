using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>对局界面：棋盘 + 信息面板（人机 / 人人）。</summary>
    public class GameScreen : UserControl
    {
        /// <summary>玩家执哪一方（人机对战；由难度页的"执白/执黑"决定）。</summary>
        private readonly int _humanSide;
        private readonly int _aiSide;

        /// <summary>电脑每步至少"思考"这么久，避免低难度下瞬间落子（毫秒）。</summary>
        private const int AiMinThinkMs = 600;

        private readonly MainForm _main;
        private readonly GameMode _mode;
        private readonly SkillLevel _skill;
        private readonly ChessGame _game = new ChessGame();
        private readonly BoardView _board = new BoardView();
        private readonly MoveListView _moveList = new MoveListView();
        private readonly FlatButton _undoBtn = new FlatButton();
    private readonly FlatButton _reviewBtn = new FlatButton();
        private readonly FlatButton _restartBtn = new FlatButton();
        private readonly Label _turnLabel;
        private readonly Label _moveNoLabel;
        private readonly Label _statusLabel;
        private readonly System.Windows.Forms.Timer _dots = new System.Windows.Forms.Timer();

        private bool _aiThinking;
        private int _dotCount;
        private int _generation;
        private int _turnDotSide = Board.White;

        // ---- AI 解析 ----
        private bool _analysisOn;
        private AnalysisPanel _analysisPanel;
        private FlatButton _analysisToggle;
        private readonly List<PositionAnalysis> _analyses = new List<PositionAnalysis>();   // 索引 = ply
        private readonly List<MoveAnnotation> _annotations = new List<MoveAnnotation>();    // 索引 = 手数
        private readonly Queue<AnalysisJob> _liveQueue = new Queue<AnalysisJob>();
        private readonly Queue<AnalysisJob> _backlogQueue = new Queue<AnalysisJob>();
        private readonly HashSet<int> _queuedPlies = new HashSet<int>();
        private readonly object _analysisLock = new object();
        private Thread _analysisThread;
        private volatile bool _analysisStop;
        private int _analysisGeneration;
        private readonly SkillLevel _analysisProfile = Analyzer.Profile(6, 500);
        private readonly Random _analysisRng = new Random(20260101);

        private class AnalysisJob
        {
            public int Ply;
            public Move[] Prefix;
        }

        private FlatButton _backBtn;
        private FlatButton _fullscreenBtn;
        private FlatButton _controlBtn;          // 显示控制格（人机 / 人人都有）
        private bool _controlOn;
        private Label _titleLabel;
        private Label _subtitleLabel;

        // 绘制用的设备坐标矩形（在 ApplyLayout 里按缩放算好）
        private Rectangle _cardRect = new Rectangle(620, 100, 290, 560);
        private Rectangle _analysisRect = new Rectangle(932, 96, 304, 578);

        private const float LogicalW = 940f;
        private const float LogicalWAnalysis = 1264f;
        private const float LogicalH = 700f;

        public GameScreen(MainForm main, GameMode mode, SkillLevel skill, int humanSide)
        {
            _main = main;
            _mode = mode;
            _skill = skill;
            _humanSide = humanSide == Board.Black ? Board.Black : Board.White;
            _aiSide = _humanSide == Board.White ? Board.Black : Board.White;

            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            _backBtn = new FlatButton();
            _backBtn.ButtonStyle = FlatButtonStyle.Ghost;
            _backBtn.Text = "← 返回";
            _backBtn.Font = Theme.Font(14.5f, FontStyle.Regular);
            _backBtn.Bounds = new Rectangle(22, 20, 96, 36);
            _backBtn.Click += delegate { ConfirmLeave(); };
            Controls.Add(_backBtn);

            string title = mode == GameMode.HumanVsAi ? "人机对战" : "人人对战（本地）";
            _titleLabel = Ui.MakeLabel(title, 18f, FontStyle.Bold, Theme.Text,
                new Rectangle(132, 18, 520, 26), ContentAlignment.MiddleLeft);
            Controls.Add(_titleLabel);
            string sub = mode == GameMode.HumanVsAi
                ? "ELO " + skill.Elo + " · " + skill.Tier + " — " + skill.Desc + " · " + (_humanSide == Board.White ? "你执白先行" : "你执黑后行")
                : "双方轮流操作同一台电脑 · 每走一步棋盘自动翻转";
            _subtitleLabel = Ui.MakeLabel(sub, 11.5f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(134, 46, 520, 22), ContentAlignment.MiddleLeft);
            Controls.Add(_subtitleLabel);

            // 全屏开关（两种模式都有）
            _fullscreenBtn = new FlatButton();
            _fullscreenBtn.ButtonStyle = FlatButtonStyle.Ghost;
            _fullscreenBtn.Text = "全屏";
            _fullscreenBtn.Click += delegate { _main.ToggleFullscreen(); };
            Controls.Add(_fullscreenBtn);

            // 显示控制格开关（人机、人人都有；默认关闭）
            _controlBtn = new FlatButton();
            _controlBtn.ButtonStyle = FlatButtonStyle.Secondary;
            _controlBtn.Text = "控制格 · 关";
            _controlBtn.Font = Theme.Font(14f, FontStyle.Regular);
            _controlBtn.Bounds = new Rectangle(658, 22, 108, 34);
            _controlBtn.Click += delegate { ToggleControl(); };
            Controls.Add(_controlBtn);

            // AI 解析开关（仅人机对战；默认关闭）
            if (mode == GameMode.HumanVsAi)
            {
                _analysisToggle = new FlatButton();
                _analysisToggle.ButtonStyle = FlatButtonStyle.Secondary;
                _analysisToggle.Font = Theme.Font(14f, FontStyle.Regular);
                _analysisToggle.Bounds = new Rectangle(752, 22, 158, 34);
                _analysisToggle.Click += delegate { ToggleAnalysis(); };
                Controls.Add(_analysisToggle);
                UpdateToggleVisual();
            }

            _board.Game = _game;
            _board.Bounds = new Rectangle(350, 100, 560, 560);
            _board.HumanSide = mode == GameMode.HumanVsAi ? _humanSide : 0;
            _board.ShowControl = _controlOn;
            _board.WhiteAtBottom = mode == GameMode.HumanVsHuman || _humanSide == Board.White;   // 人机时己方永远在下方
            _board.MoveRequested += BoardMoveRequested;
            _board.RestartRequested += delegate { RestartGame(); };
            _board.MenuRequested += delegate { _main.ShowMenu(); };
            Controls.Add(_board);

            _turnLabel = Ui.MakeLabel("", 15f, FontStyle.Bold, Theme.Text,
                new Rectangle(68, 114, 150, 28), ContentAlignment.MiddleLeft);
            Controls.Add(_turnLabel);

            _moveNoLabel = Ui.MakeLabel("", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(220, 116, 84, 24), ContentAlignment.MiddleRight);
            Controls.Add(_moveNoLabel);

            _statusLabel = Ui.MakeLabel("", 13.5f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(50, 148, 254, 22), ContentAlignment.MiddleLeft);
            Controls.Add(_statusLabel);

            _moveList.Bounds = new Rectangle(50, 188, 254, 384);
            Controls.Add(_moveList);

            _undoBtn.ButtonStyle = FlatButtonStyle.Secondary;
            _undoBtn.Text = "悔棋";
            _undoBtn.Font = Theme.Font(15f, FontStyle.Regular);
            _undoBtn.Bounds = new Rectangle(50, 588, 122, 42);
            _undoBtn.Click += delegate { UndoClicked(); };
            Controls.Add(_undoBtn);

            _restartBtn.ButtonStyle = FlatButtonStyle.Secondary;
            _restartBtn.Text = "新局";
            _restartBtn.Font = Theme.Font(15f, FontStyle.Regular);
            _restartBtn.Bounds = new Rectangle(182, 588, 122, 42);
            _restartBtn.Click += delegate { RestartGame(); };
            Controls.Add(_restartBtn);

            // 终局后才出现：进复盘（多邻国式：自动走棋 + 讲解 + 臭棋重下）
            _reviewBtn.ButtonStyle = FlatButtonStyle.Primary;
            _reviewBtn.Text = "复盘";
            _reviewBtn.Font = Theme.Font(15f, FontStyle.Regular);
            _reviewBtn.Visible = false;
            _reviewBtn.Click += delegate { OpenReview(); };
            Controls.Add(_reviewBtn);

            _dots.Interval = 420;
            _dots.Tick += delegate
            {
                _dotCount = (_dotCount + 1) % 4;
                UpdateStatus();
            };
            _dots.Start();

            ApplyLayout();
            UpdateStatus();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyLayout();
        }

        /// <summary>按窗口尺寸重新排版：对局界面逻辑画布 940(或 1266)×700，等比缩放并居中。</summary>
        private void ApplyLayout()
        {
            if (_board == null || Width <= 0) return;
            float logicalW = _analysisOn ? LogicalWAnalysis : LogicalW;
            UiScale.Fit(Width, Height, logicalW, LogicalH);

            _cardRect = UiScale.R(30, 100, 290, 560);
            _analysisRect = UiScale.R(930, 96, 304, 578);

            UiScale.Font(_titleLabel, 18f, FontStyle.Bold);
            UiScale.Font(_subtitleLabel, 13.5f, FontStyle.Regular);
            UiScale.Font(_turnLabel, 15f, FontStyle.Bold);
            UiScale.Font(_moveNoLabel, 13f, FontStyle.Regular);
            UiScale.Font(_statusLabel, 13.5f, FontStyle.Regular);
            UiScale.Font(_undoBtn, 15f, FontStyle.Regular);
            UiScale.Font(_restartBtn, 15f, FontStyle.Regular);
            UiScale.Font(_fullscreenBtn, 14f, FontStyle.Regular);
            if (_analysisToggle != null) UiScale.Font(_analysisToggle, 14f, FontStyle.Regular);

            _backBtn.Bounds = UiScale.R(22, 20, 96, 36);
            UiScale.Font(_backBtn, 14.5f, FontStyle.Regular);
            _titleLabel.Bounds = UiScale.R(132, 18, 520, 26);
            _subtitleLabel.Bounds = UiScale.R(134, 46, 520, 22);
            _fullscreenBtn.Bounds = UiScale.R(566, 22, 84, 34);
            _fullscreenBtn.Text = _main.IsFullscreen ? "退出" : "全屏";
            UiScale.Font(_controlBtn, 14f, FontStyle.Regular);
            _controlBtn.Bounds = UiScale.R(658, 22, 108, 34);
            if (_analysisToggle != null) _analysisToggle.Bounds = UiScale.R(774, 22, 158, 34);
            _board.Bounds = UiScale.R(350, 100, 560, 560);
            _turnLabel.Bounds = UiScale.R(68, 114, 150, 28);
            _moveNoLabel.Bounds = UiScale.R(220, 116, 84, 24);
            _statusLabel.Bounds = UiScale.R(50, 148, 254, 22);
            _moveList.Bounds = UiScale.R(50, 188, 254, 384);
            _undoBtn.Bounds = UiScale.R(50, 588, 122, 42);
            _restartBtn.Bounds = UiScale.R(182, 588, 122, 42);
            _reviewBtn.Bounds = UiScale.R(50, 588, 122, 42);
            if (_analysisPanel != null) _analysisPanel.Bounds = _analysisRect;

            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dots.Stop();
                _dots.Dispose();
                _analysisStop = true;
                lock (_analysisLock) { Monitor.PulseAll(_analysisLock); }
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // 绘制面板卡片
        // ------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            using (GraphicsPath path = Theme.RoundRect(_cardRect, UiScale.Px(14)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
            }

            // 棋盘外框
            Rectangle frame = UiScale.R(29, 99, 561, 561);
            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.2f)))
            {
                g.DrawRectangle(p, frame);
            }

            // 轮到哪一方的小圆点
            Color dotFill = _turnDotSide == Board.White ? Color.White : Theme.BlackPiece;
            Rectangle dot = UiScale.R(50, 122, 13, 13);
            using (SolidBrush b = new SolidBrush(dotFill)) g.FillEllipse(b, dot);
            using (Pen p = new Pen(Theme.WithAlpha(Theme.Text, 120), UiScale.Pxf(1.2f))) g.DrawEllipse(p, dot);

            // 分隔线
            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1f)))
            {
                g.DrawLine(p, _cardRect.X + UiScale.Px(20), UiScale.Ry(178), _cardRect.Right - UiScale.Px(20), UiScale.Ry(178));
            }
        }

        // ------------------------------------------------------------------
        // 控制格开关
        // ------------------------------------------------------------------
        /// <summary>显示 / 隐藏控制格（双方棋子的攻击范围）。</summary>
        private void ToggleControl()
        {
            _controlOn = !_controlOn;
            _board.ShowControl = _controlOn;
            _controlBtn.Text = _controlOn ? "控制格 · 开" : "控制格 · 关";
            _controlBtn.Highlighted = _controlOn;
            _board.Invalidate();
        }

        /// <summary>供自检与截图工具调用（与点按钮完全等价）。</summary>
        public void ToggleControlSquares()
        {
            ToggleControl();
        }

        /// <summary>当前是否显示控制格。</summary>
        public bool ControlSquaresVisible
        {
            get { return _controlOn; }
        }

        // ------------------------------------------------------------------
        // 走子
        // ------------------------------------------------------------------
        private void BoardMoveRequested(object sender, MoveRequestEventArgs e)
        {
            if (_game.IsOver || _aiThinking) return;
            if (_mode == GameMode.HumanVsAi && _game.Position.Side != _humanSide) return;

            List<Chess.Move> candidates = new List<Chess.Move>();
            List<Chess.Move> legal = _game.LegalMoves();
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].From == e.From && legal[i].To == e.To) candidates.Add(legal[i]);
            }
            if (candidates.Count == 0) return;

            Chess.Move chosen = candidates[0];
            if (candidates.Count > 1)
            {
                int piece = PromotionDialog.Ask(this, _game.Position.Side);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Promotion == piece) { chosen = candidates[i]; break; }
                }
            }
            ApplyMove(chosen);
        }

        private void ApplyMove(Chess.Move m)
        {
            if (!_game.Play(m)) return;
            _board.SetLastMove(m);
            _board.ClearSelection();
            if (_mode == GameMode.HumanVsHuman)
            {
                _board.WhiteAtBottom = _game.Position.Side == Board.White;   // 每步翻转棋盘
            }
            _moveList.SetMoves(_game.SanHistory, _analysisOn ? _annotations : null);
            UpdateStatus();
            AfterMoveAnalysis();

            if (_game.IsOver)
            {
                ShowGameOver();
                return;
            }
            if (_mode == GameMode.HumanVsAi && _game.Position.Side == _aiSide)
            {
                StartAiTurn();
            }
        }

        private void ShowGameOver()
        {
            string title, sub;
            if (_game.State == GameState.Checkmate)
            {
                if (_mode == GameMode.HumanVsAi)
                    title = _game.Winner == _humanSide ? "你赢了" : "电脑获胜";
                else
                    title = _game.Winner == Board.White ? "白方获胜" : "黑方获胜";
                sub = "将杀 · 共 " + ((_game.MoveHistory.Count + 1) / 2) + " 回合";
            }
            else
            {
                title = "和棋";
                switch (_game.State)
                {
                    case GameState.Stalemate: sub = "无子可动 · 逼和"; break;
                    case GameState.DrawFiftyMove: sub = "50 回合规则"; break;
                    case GameState.DrawRepetition: sub = "三次重复局面"; break;
                    case GameState.DrawInsufficientMaterial: sub = "子力不足"; break;
                    default: sub = "对局结束"; break;
                }
            }
            _board.ShowResult(title, sub);
            _board.Interactive = false;
            ShowReviewButton();
            UpdateStatus();
        }

        // ------------------------------------------------------------------
        // 电脑走子
        // ------------------------------------------------------------------
        private void StartAiTurn()
        {
            if (_aiThinking || _game.IsOver) return;
            _aiThinking = true;
            _board.Interactive = false;
            _undoBtn.Enabled = false;
            UpdateStatus();

            Board snapshot = _game.Position.Clone();
            List<ulong> keys = new List<ulong>(_game.Keys);
            SkillLevel skill = _skill;
            int gen = _generation;
            Random rng = new Random(unchecked(Environment.TickCount * 397 + _game.MoveHistory.Count * 31));

            Thread t = new Thread(new ThreadStart(delegate
            {
                Stopwatch watch = Stopwatch.StartNew();
                Chess.Move move = Chess.Move.Null;
                try
                {
                    Engine engine = new Engine();
                    SearchResult res = engine.Search(snapshot, keys, skill, rng);
                    if (res != null) move = res.Move;
                }
                catch
                {
                    move = Chess.Move.Null;
                }

                // 固定的一小段"落子延迟"：低难度每步思考只有一两百毫秒，
                // 会让电脑看起来是瞬间应手；这里保证每步至少用 AiMinThinkMs。
                int remain = AiMinThinkMs - (int)watch.ElapsedMilliseconds;
                if (remain > 0)
                {
                    try { Thread.Sleep(remain); }
                    catch { }
                }

                Chess.Move result = move;
                Post(delegate { OnAiDone(gen, result); });
            }));
            t.IsBackground = true;
            t.Name = "chess-ai";
            t.Start();
        }

        private void Post(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch
            {
            }
        }

        private void OnAiDone(int generation, Chess.Move move)
        {
            if (generation != _generation || IsDisposed) return;
            _aiThinking = false;
            _board.Interactive = true;
            _undoBtn.Enabled = true;
            if (_game.IsOver) { UpdateStatus(); return; }

            List<Chess.Move> legal = _game.LegalMoves();
            if (legal.Count == 0) { UpdateStatus(); return; }

            Chess.Move chosen = Chess.Move.Null;
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].From == move.From && legal[i].To == move.To && legal[i].Promotion == move.Promotion)
                {
                    chosen = legal[i];
                    break;
                }
            }
            if (chosen.IsNull) chosen = legal[0];      // 兜底：绝不因为引擎异常而卡住

            ApplyMove(chosen);
        }

        // ------------------------------------------------------------------
        // AI 解析
        // ------------------------------------------------------------------
        private void UpdateToggleVisual()
        {
            if (_analysisToggle == null) return;
            _analysisToggle.Text = _analysisOn ? "AI 解析 · 开" : "AI 解析 · 关";
            _analysisToggle.ButtonStyle = _analysisOn ? FlatButtonStyle.Primary : FlatButtonStyle.Secondary;
        }

        /// <summary>开启/关闭 AI 解析（默认关闭）。</summary>
        private void ToggleAnalysis()
        {
            _analysisOn = !_analysisOn;
            UpdateToggleVisual();
            _main.SetAnalysisPanelVisible(_analysisOn);

            _analysisGeneration++;
            lock (_analysisLock)
            {
                _liveQueue.Clear();
                _backlogQueue.Clear();
                _queuedPlies.Clear();
            }

            if (_analysisOn)
            {
                AnalysisPanel p = EnsureAnalysisPanel();
                p.Visible = true;
                p.BringToFront();
                StartAnalysisWorker();
                EnqueueMissingAnalyses();
            }
            else if (_analysisPanel != null)
            {
                _analysisPanel.Visible = false;
            }
            ApplyLayout();          // 逻辑画布宽度在 940 / 1266 之间切换，需要重排版
            RefreshAnalysisViews();
        }

        private AnalysisPanel EnsureAnalysisPanel()
        {
            if (_analysisPanel == null)
            {
                _analysisPanel = new AnalysisPanel();
                _analysisPanel.Bounds = _analysisRect;
                _analysisPanel.ProfileDepth = _analysisProfile.MaxDepth;
                _analysisPanel.ProfileMs = _analysisProfile.TimeMs;
                _analysisPanel.Visible = false;
                Controls.Add(_analysisPanel);
            }
            return _analysisPanel;
        }

        private void StartAnalysisWorker()
        {
            if (_analysisThread != null) return;
            _analysisStop = false;
            _analysisThread = new Thread(new ThreadStart(AnalysisLoop));
            _analysisThread.IsBackground = true;
            _analysisThread.Name = "chess-analysis";
            _analysisThread.Start();
        }

        private void AnalysisLoop()
        {
            while (!_analysisStop)
            {
                AnalysisJob job = null;
                lock (_analysisLock)
                {
                    if (_liveQueue.Count > 0) job = _liveQueue.Dequeue();
                    else if (_backlogQueue.Count > 0) job = _backlogQueue.Dequeue();
                    else
                    {
                        Monitor.Wait(_analysisLock, 150);
                        continue;
                    }
                }

                int gen = _analysisGeneration;
                PositionAnalysis pa = null;
                try
                {
                    pa = ComputeAnalysis(job);
                }
                catch
                {
                    pa = null;
                }

                if (pa == null)
                {
                    int plyDrop = job.Ply;
                    lock (_analysisLock) { _queuedPlies.Remove(plyDrop); }
                    continue;
                }
                pa.Ply = job.Ply;

                AnalysisJob done = job;
                Post(delegate { OnAnalysisReady(gen, done, pa); });
            }
        }

        /// <summary>重放历史得到该 ply 的局面并分析（在后台线程执行）。</summary>
        private PositionAnalysis ComputeAnalysis(AnalysisJob job)
        {
            Board b = Board.FromFen(Board.StartFen);
            List<ulong> keys = new List<ulong>(job.Prefix.Length + 1);
            keys.Add(b.Key);
            for (int i = 0; i < job.Prefix.Length; i++)
            {
                if (!b.TryMake(job.Prefix[i])) return null;
                keys.Add(b.Key);
            }
            return Analyzer.Analyze(b, keys, _analysisProfile, _analysisRng);
        }

        private bool HasAnalysis(int ply)
        {
            return ply >= 0 && ply < _analyses.Count && _analyses[ply] != null;
        }

        private void EnqueueMissingAnalyses()
        {
            for (int ply = 0; ply <= _game.MoveHistory.Count; ply++)
            {
                if (HasAnalysis(ply)) continue;
                EnqueueAnalysis(ply, ply == _game.MoveHistory.Count);
            }
        }

        private void EnqueueAnalysis(int ply, bool live)
        {
            if (!_analysisOn || ply < 0 || ply > _game.MoveHistory.Count) return;
            lock (_analysisLock)
            {
                if (_queuedPlies.Contains(ply) || HasAnalysis(ply)) return;
                AnalysisJob job = new AnalysisJob();
                job.Ply = ply;
                job.Prefix = _game.MoveHistory.GetRange(0, ply).ToArray();
                _queuedPlies.Add(ply);
                if (live) _liveQueue.Enqueue(job);
                else _backlogQueue.Enqueue(job);
                Monitor.Pulse(_analysisLock);
            }
        }

        private void OnAnalysisReady(int generation, AnalysisJob job, PositionAnalysis pa)
        {
            if (IsDisposed || generation != _analysisGeneration) return;
            lock (_analysisLock) { _queuedPlies.Remove(job.Ply); }
            SetAnalysis(job.Ply, pa);

            // 回填"从该局面走出的那一手棋"的评价
            if (job.Ply < _game.MoveHistory.Count)
            {
                int mover = (job.Ply % 2 == 0) ? Board.White : Board.Black;
                Move played = _game.MoveHistory[job.Ply];
                SetAnnotation(job.Ply, Analyzer.Judge(pa, played, mover));
            }
            RefreshAnalysisViews();
        }

        private void SetAnalysis(int ply, PositionAnalysis pa)
        {
            while (_analyses.Count <= ply) _analyses.Add(null);
            _analyses[ply] = pa;
        }

        private PositionAnalysis GetAnalysis(int ply)
        {
            if (ply < 0 || ply >= _analyses.Count) return null;
            return _analyses[ply];
        }

        private void SetAnnotation(int index, MoveAnnotation ma)
        {
            while (_annotations.Count <= index) _annotations.Add(new MoveAnnotation());
            _annotations[index] = ma;
        }

        /// <summary>走子后：定级这一手棋，并安排新局面的分析。</summary>
        private void AfterMoveAnalysis()
        {
            if (!_analysisOn) return;
            int idx = _game.MoveHistory.Count - 1;
            if (idx < 0) return;

            PositionAnalysis pa = GetAnalysis(idx);
            if (pa != null)
            {
                int mover = (idx % 2 == 0) ? Board.White : Board.Black;
                SetAnnotation(idx, Analyzer.Judge(pa, _game.MoveHistory[idx], mover));
            }
            else
            {
                SetAnnotation(idx, new MoveAnnotation());      // 占位：分析完成后回填
                EnqueueAnalysis(idx, false);
            }
            EnqueueAnalysis(_game.MoveHistory.Count, true);
            RefreshAnalysisViews();
        }

        /// <summary>悔棋 / 新局后同步解析数据。</summary>
        private void TrimAnalysis()
        {
            int plies = _game.MoveHistory.Count;
            while (_analyses.Count > plies + 1) _analyses.RemoveAt(_analyses.Count - 1);
            while (_annotations.Count > plies) _annotations.RemoveAt(_annotations.Count - 1);
            if (!_analysisOn) return;
            EnqueueMissingAnalyses();
            RefreshAnalysisViews();
        }

        private int PendingAnalysisCount()
        {
            lock (_analysisLock)
            {
                return _liveQueue.Count + _backlogQueue.Count;
            }
        }

        private void RefreshAnalysisViews()
        {
            _moveList.SetMoves(_game.SanHistory, _analysisOn ? _annotations : null);
            if (_analysisPanel == null) return;
            _analysisPanel.Analyses = _analyses;
            _analysisPanel.MoveGrades = _annotations;
            _analysisPanel.Sans = _game.SanHistory;
            _analysisPanel.CurrentPly = _game.MoveHistory.Count;
            _analysisPanel.Pending = PendingAnalysisCount();
            _analysisPanel.Invalidate();
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------
        /// <summary>终局后把「悔棋」换成「复盘」。</summary>
        private void ShowReviewButton()
        {
            _undoBtn.Visible = false;
            _reviewBtn.Visible = true;
            _reviewBtn.BringToFront();
        }

        /// <summary>回到可走状态时把按钮换回来。</summary>
        private void HideReviewButton()
        {
            _undoBtn.Visible = true;
            _reviewBtn.Visible = false;
        }

        /// <summary>进入复盘。</summary>
        private void OpenReview()
        {
            if (_game == null || _game.MoveHistory.Count == 0) return;
            _main.StartReview(_game, _mode, _humanSide);
        }

        private void UndoClicked()
        {
            if (_aiThinking) return;
            if (_game.MoveHistory.Count == 0) return;

            int n = _mode == GameMode.HumanVsAi ? 2 : 1;
            if (n > _game.MoveHistory.Count) n = _game.MoveHistory.Count;
            _game.UndoPlies(n);
            if (_mode == GameMode.HumanVsAi && _game.MoveHistory.Count > 0 && _game.Position.Side != _humanSide)
            {
                _game.UndoLast();
            }

            _board.HideResult();
            _board.Interactive = true;
            _board.ClearSelection();
            _board.SetLastMove(_game.MoveHistory.Count > 0 ? _game.MoveHistory[_game.MoveHistory.Count - 1] : Chess.Move.Null);
            if (_mode == GameMode.HumanVsHuman)
            {
                _board.WhiteAtBottom = _game.Position.Side == Board.White;
            }
            _moveList.SetMoves(_game.SanHistory, _analysisOn ? _annotations : null);
            TrimAnalysis();
            UpdateStatus();
            // 悔棋回到开局时（玩家执黑的情况）该轮到电脑走，这里补上触发，否则会卡住
            StartIfAiMovesFirst();
        }

        private void RestartGame()
        {
            HideReviewButton();
            _generation++;
            _aiThinking = false;
            _game.NewGame();
            _board.HideResult();
            _board.ClearSelection();
            _board.SetLastMove(Chess.Move.Null);
            _board.Interactive = true;
            _board.WhiteAtBottom = _mode == GameMode.HumanVsHuman || _humanSide == Board.White;
            _undoBtn.Enabled = true;
            ResetAnalysisAll();
            _moveList.SetMoves(_game.SanHistory, _analysisOn ? _annotations : null);
            UpdateStatus();
            StartIfAiMovesFirst();
        }

        /// <summary>玩家执黑时，开局（或新局）由电脑先走一步。执白时什么也不做。</summary>
        public void StartIfAiMovesFirst()
        {
            if (_mode != GameMode.HumanVsAi) return;
            if (_game.IsOver || _aiThinking) return;
            if (_game.Position.Side != _aiSide) return;
            StartAiTurn();
        }

        /// <summary>新局时清空全部分析数据。</summary>
        private void ResetAnalysisAll()
        {
            _analysisGeneration++;
            lock (_analysisLock)
            {
                _liveQueue.Clear();
                _backlogQueue.Clear();
                _queuedPlies.Clear();
            }
            _analyses.Clear();
            _annotations.Clear();
            if (_analysisOn)
            {
                EnqueueMissingAnalyses();
                RefreshAnalysisViews();
            }
        }

        private void ConfirmLeave()
        {
            if (_game.MoveHistory.Count > 0 && !_game.IsOver)
            {
                DialogResult r = MessageBox.Show(this, "确定返回主页吗？当前对局将结束。", "国际象棋",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            _generation++;
            _main.ShowMenu();
        }

        // ------------------------------------------------------------------
        // 状态显示
        // ------------------------------------------------------------------
        private void UpdateStatus()
        {
            _turnDotSide = _game.Position.Side;
            string turn;
            if (_aiThinking)
            {
                string dots = new string('.', _dotCount);
                turn = "电脑思考中" + dots;
            }
            else if (_game.IsOver)
            {
                turn = _game.State == GameState.Checkmate
                    ? (_game.Winner == Board.White ? "白方获胜" : "黑方获胜")
                    : "和棋";
            }
            else
            {
                turn = _game.Position.Side == Board.White ? "白方走棋" : "黑方走棋";
                if (_mode == GameMode.HumanVsAi)
                {
                    turn += _game.Position.Side == _humanSide ? "（你）" : "（电脑）";
                }
            }
            _turnLabel.Text = turn;
            _moveNoLabel.Text = "第 " + _game.Position.Fullmove + " 回合";

            string status = "";
            Color color = Theme.TextMuted;
            if (_game.IsOver)
            {
                status = _game.StateReason;
                color = _game.State == GameState.Checkmate ? Theme.AccentDark : Theme.TextMuted;
            }
            else if (!_aiThinking && _game.Position.InCheck(_game.Position.Side))
            {
                status = "将军！";
                color = Theme.Danger;
            }
            _statusLabel.Text = status;
            _statusLabel.ForeColor = color;

            Invalidate(new Rectangle(_cardRect.X - 2, _cardRect.Y - 2, _cardRect.Width + 4, UiScale.Px(82)));
        }
    }
}
