using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 发布前自检（命令行参数 --selftest）：
    /// 在真实的窗口与消息循环下依次验证「主页 → 难度选择 → 人机对战 → 点击走子 → 电脑应手 → 返回主页」，
    /// 结果写入 %TEMP%\chess_selftest.log，并用退出码表示成功（0）或失败（2）。
    /// </summary>
    internal static class SelfTest
    {
        private static MainForm _form;
        private static Timer _timer;
        private static int _step;
        private static int _ticks;
        private static int _failures;
        private static GameScreen _gameScreen;
        private static ChessGame _game;
        private static readonly List<string> Log = new List<string>();

        public static int Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _form = new MainForm();
            _timer = new Timer();
            _timer.Interval = 200;
            _timer.Tick += Tick;
            _timer.Start();
            Application.Run(_form);
            _timer.Stop();

            Say(_failures == 0 ? "SELFTEST PASSED" : ("SELFTEST FAILED (" + _failures + ")"));
            string text = string.Join("\r\n", Log.ToArray());
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "chess_selftest_final.log"), text, Encoding.UTF8);
            }
            catch
            {
            }
            return _failures == 0 ? 0 : 2;
        }

        private static string e2e4Comment()
        {
            Opening it = Openings.ById("italian");
            return it.Nodes[it.Root[0]].Comment;
        }

        /// <summary>跑几轮消息循环（让挂起的重画真的发生）。</summary>
        private static void PumpTicks(int n)
        {
            for (int i = 0; i < n; i++) Application.DoEvents();
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Say((ok ? "  ok   " : "  FAIL ") + what);
        }

        /// <summary>逐行落盘，便于进程异常退出时仍能看到进度。</summary>
        private static void Say(string line)
        {
            Log.Add(line);
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "chess_selftest.log"), line + "\r\n");
            }
            catch
            {
            }
        }

        private static bool _inTick;

        private static void Tick(object sender, EventArgs e)
        {
            // 防重入：Tick 里会调用 Application.DoEvents() 让界面刷新，
            // 这会顺带把已经排队的下一个 Tick 也派发进来，导致同一步骤被反复执行（自检会误报失败）。
            if (_inTick) return;
            _inTick = true;
            try
            {
                switch (_step)
                {
                    case 0:
                        Say("-- selftest: window shown " + _form.ClientSize.Width + "x" + _form.ClientSize.Height + " visible=" + _form.Visible);
                        Check(_form.Visible, "主窗口可见");
                        Check(Screen is MenuScreen, "启动后显示主页");
                        _form.ShowDifficulty();
                        _step = 1;
                        break;

                    case 1:
                        Check(Screen is DifficultyScreen, "进入难度选择页");
                        {
                            int tiles = 0;
                            foreach (Control c in Screen.Controls)
                            {
                                FlatButton b = c as FlatButton;
                                if (b != null && b.SubText.Length > 0) tiles++;
                            }
                            Check(tiles == 20, "难度档位数量 = 20（实际 " + tiles + "）");
                            FlatButton target = null;
                            foreach (Control c in Screen.Controls)
                            {
                                FlatButton b = c as FlatButton;
                                if (b != null && b.Text == "600") { target = b; break; }
                            }
                            Check(target != null, "找到 ELO 600 档位按钮");
                            if (target != null)
                            {
                                int n = RealClickCount(target);
                                Check(n == 1, "难度按钮一次点击只触发 1 次 Click（实际 " + n + " 次）");
                            }
                        }
                        _step = 2;
                        break;

                    case 2:
                        Check(Screen is GameScreen, "点击档位后进入对局界面");
                        _gameScreen = Screen as GameScreen;
                        if (_gameScreen == null) { Finish(); return; }
                        _game = (ChessGame)Field(_gameScreen, "_game");
                        BoardView board = (BoardView)Field(_gameScreen, "_board");
                        Check(_game != null && board != null, "对局对象与棋盘控件就绪");
                        Check(board.HumanSide == Board.White, "人机对战：人类执白");
                        Check(board.WhiteAtBottom, "棋盘方向：白方在下方");
                        Check(!_game.IsOver && _game.Position.Side == Board.White, "开局轮到白方");
                        Check(_game.Position.ToFen() == Board.StartFen, "初始局面为标准开局");

                        // 用真实鼠标事件走 e2-e4
                        MouseBoard(board, 315, 455);
                        MouseBoard(board, 315, 315);
                        Check(_game.PlyCount == 1, "点击 e2/e4 完成一步走子（PlyCount=" + _game.PlyCount + "）");
                        _aiMoveStart = DateTime.Now;
                        _step = 3;
                        _ticks = 0;
                        break;

                    case 3:
                        if (_game.PlyCount >= 2)
                        {
                            Check(_game.PlyCount == 2, "电脑已应手（PlyCount=" + _game.PlyCount + "）");
                            double aiMs = (DateTime.Now - _aiMoveStart).TotalMilliseconds;
                            Check(aiMs >= 500, "电脑落子有最小延迟（实际 " + aiMs.ToString("0") + " ms）");
                            Check(_game.SanHistory.Count >= 2, "棋谱记录：" + string.Join(" ", _game.SanHistory.ToArray()));
                            Check(!_game.Position.InCheck(Board.White), "走子后白方未被将军");
                            // 悔棋：人机对战应回退两步
                            FlatButton undo = (FlatButton)Field(_gameScreen, "_undoBtn");
                            int undoClicks = RealClickCount(undo);
                            Check(undoClicks == 1, "悔棋按钮一次点击只触发 1 次 Click（实际 " + undoClicks + " 次）");
                            Check(_game.PlyCount == 0, "悔棋回到开局（PlyCount=" + _game.PlyCount + "）");

                            // AI 解析：默认关闭
                            Check(!(bool)Field(_gameScreen, "_analysisOn"), "AI 解析默认关闭");
                            FlatButton toggle = (FlatButton)Field(_gameScreen, "_analysisToggle");
                            Check(toggle != null, "存在 AI 解析开关按钮");
                            Check(toggle.Text.IndexOf("关") >= 0, "开关显示为关闭：" + toggle.Text);
                            int toggleClicks = RealClickCount(toggle);
                            Check(toggleClicks == 1, "开关一次点击只触发 1 次 Click（实际 " + toggleClicks + " 次）");
                            _step = 4;
                            _ticks = 0;
                        }
                        else if (_ticks > 60)
                        {
                            Check(false, "等待电脑应手超时");
                            Finish();
                        }
                        break;

                    case 4:
                        Check((bool)Field(_gameScreen, "_analysisOn"), "点击开关后 AI 解析已开启");
                        Check(_form.ClientSize.Width == _form.WindowedSize.Width, "窗口为解析面板加宽（" + _form.ClientSize.Width + "px，期望 " + _form.WindowedSize.Width + "）");
                        {
                            Control panel = (Control)Field(_gameScreen, "_analysisPanel");
                            Check(panel != null && panel.Visible, "解析面板已显示");
                            FlatButton toggle = (FlatButton)Field(_gameScreen, "_analysisToggle");
                            Check(toggle.Text.IndexOf("开") >= 0, "开关显示为开启：" + toggle.Text);
                        }
                        {
                            BoardView board2 = (BoardView)Field(_gameScreen, "_board");
                            // 布局：走棋信息在棋盘左侧，解析面板在右侧，棋盘因此水平居中
                            Control turn = (Control)Field(_gameScreen, "_turnLabel");
                            Check(turn != null && turn.Right <= board2.Left,
                                "走棋信息在棋盘左侧（" + (turn == null ? -1 : turn.Right) + " ≤ " + board2.Left + "）");
                            int boardCenter = board2.Left + board2.Width / 2;
                            int canvasCenter = _gameScreen.Width / 2;
                            Check(Math.Abs(boardCenter - canvasCenter) <= 8,
                                "开启解析后棋盘水平居中（棋盘中心 " + boardCenter + "，画布中心 " + canvasCenter + "）");
                            Control ap = (Control)Field(_gameScreen, "_analysisPanel");
                            Check(ap != null && ap.Left >= board2.Right,
                                "解析面板在棋盘右侧（" + (ap == null ? -1 : ap.Left) + " ≥ " + board2.Right + "）");

                            MouseBoard(board2, 315, 455);   // e2（棋盘内部坐标与位置无关）
                            MouseBoard(board2, 315, 315);   // e4
                            Check(_game.PlyCount == 1, "开启解析后仍可正常走子");
                        }
                        {
                            // 显示控制格：人机对战时也有，点一下开、再点一下关
                            BoardView bv = (BoardView)Field(_gameScreen, "_board");
                            FlatButton cbtn = (FlatButton)Field(_gameScreen, "_controlBtn");
                            Check(cbtn != null, "人机对战有「控制格」按钮");
                            if (cbtn != null)
                            {
                                Check(cbtn.Text.IndexOf("关") >= 0, "控制格默认关闭：" + cbtn.Text);
                                Check(!bv.ShowControl, "棋盘初始不显示控制格");
                                int n1 = RealClickCount(cbtn);
                                Check(n1 == 1, "控制格按钮一次点击只触发 1 次 Click（实际 " + n1 + " 次）");
                                Check(bv.ShowControl, "点击后棋盘显示控制格");
                                Check(cbtn.Text.IndexOf("开") >= 0, "按钮变为开启：" + cbtn.Text);
                                RealClickCount(cbtn);
                                Check(!bv.ShowControl, "再点一次即关闭");
                                RealClickCount(cbtn);      // 后面截图/后续步骤保持开启
                                Check(bv.ShowControl, "再次开启");
                            }
                        }
                        _step = 5;
                        _ticks = 0;
                        break;

                    case 5:
                        {
                            System.Collections.IList analyses = (System.Collections.IList)Field(_gameScreen, "_analyses");
                            System.Collections.IList annos = (System.Collections.IList)Field(_gameScreen, "_annotations");
                            bool ply0 = analyses.Count > 0 && analyses[0] != null;
                            bool graded = annos.Count > 0 && annos[0] != null && ((MoveAnnotation)annos[0]).Ready;
                            if (ply0 && graded && _game.PlyCount >= 2)
                            {
                                PositionAnalysis pa0 = (PositionAnalysis)analyses[0];
                                Check(pa0.Moves.Count == 20, "开局解析覆盖 20 个着法");
                                Check(pa0.WhiteWinRate > 35 && pa0.WhiteWinRate < 65,
                                    "开局白方胜率合理（" + pa0.WhiteWinRate.ToString("0.0") + "%）");
                                MoveAnnotation ma = (MoveAnnotation)annos[0];
                                Check(ma.Ready && ma.Grade != null && ma.Grade.Symbol.Length > 0,
                                    "第一手棋已定级：" + ma.Grade.Symbol + " " + ma.Grade.English +
                                    "（损失 " + (ma.LossCp / 100.0).ToString("0.00") + "）");
                                Check(ma.Grade.Rank >= 0 && ma.Grade.Rank < Annotations.All.Length, "定级符号在符号表内");
                                Check(_game.PlyCount >= 2, "电脑已应手（PlyCount=" + _game.PlyCount + "）");

                                FlatButton toggle2 = (FlatButton)Field(_gameScreen, "_analysisToggle");
                                int offClicks = RealClickCount(toggle2);
                                Check(offClicks == 1, "开关关闭时也只触发 1 次 Click（实际 " + offClicks + " 次）");
                                _step = 6;
                                _ticks = 0;
                                break;
                            }
                            if (_ticks > 120)
                            {
                                Check(false, "等待 AI 解析超时（ply0=" + ply0 + ", graded=" + graded + "）");
                                Finish();
                            }
                        }
                        break;

                    case 6:
                        Check(!(bool)Field(_gameScreen, "_analysisOn"), "再次点击后 AI 解析已关闭");
                        Check(_form.ClientSize.Width == _form.WindowedSize.Width, "关闭后窗口恢复宽度（" + _form.ClientSize.Width + "px，期望 " + _form.WindowedSize.Width + "）");
                        {
                            Control panel = (Control)Field(_gameScreen, "_analysisPanel");
                            Check(panel == null || !panel.Visible, "解析面板已隐藏");
                        }

                        // ---- 全屏 ----
                        {
                            BoardView b3 = (BoardView)Field(_gameScreen, "_board");
                            int normalBoard = b3.Width;
                            FlatButton fsBtn = (FlatButton)Field(_gameScreen, "_fullscreenBtn");
                            Check(fsBtn != null, "对局界面有全屏按钮");
                            int fsClicks = RealClickCount(fsBtn);
                            Application.DoEvents();
                            Check(fsClicks == 1, "全屏按钮一次点击只触发 1 次 Click（实际 " + fsClicks + " 次）");
                            Check(_form.IsFullscreen, "点击后进入全屏");
                            Check(_form.FormBorderStyle == FormBorderStyle.None, "全屏时无边框");
                            Check(UiScale.Factor > 1.01f, "全屏后界面等比放大（UiScale=" + UiScale.Factor.ToString("0.00") + "）");
                            Check(_form.ClientSize.Width >= L(1024), "全屏客户区铺满屏幕（" + _form.ClientSize.Width + "×" + _form.ClientSize.Height + "）");
                            Check(b3.Width > normalBoard, "棋盘跟着放大（" + normalBoard + " → " + b3.Width + "）");
                            Check(fsBtn.Text.IndexOf("退出") >= 0, "按钮文字变为退出：" + fsBtn.Text);
                        }

                        // Esc 退出全屏（走 ProcessCmdKey 真实按键路径）
                        Check(SendKey(Keys.Escape), "Esc 被处理");
                        Application.DoEvents();
                        Check(!_form.IsFullscreen, "Esc 退出全屏");
                        Check(_form.FormBorderStyle == FormBorderStyle.Sizable, "退出后恢复边框");
                        Check(_form.ClientSize.Width == _form.WindowedSize.Width && _form.ClientSize.Height == _form.WindowedSize.Height,
                            "退出后恢复窗口尺寸（" + _form.ClientSize.Width + "×" + _form.ClientSize.Height + "）");
                        Check(Math.Abs(UiScale.Factor - _form.Dpi) < 0.02f,
                                "退出后缩放回到 " + _form.Dpi.ToString("0.00") + "（实际 " + UiScale.Factor.ToString("0.00") + "）");

                        // F11 切换
                        Check(SendKey(Keys.F11), "F11 被处理");
                        Application.DoEvents();
                        Check(_form.IsFullscreen, "F11 进入全屏");
                        Check(SendKey(Keys.F11), "F11 再次被处理");
                        Application.DoEvents();
                        Check(!_form.IsFullscreen, "F11 退出全屏");
                        Check(_form.ClientSize.Width == _form.WindowedSize.Width, "退出后窗口尺寸正常（" + _form.ClientSize.Width + "，期望 " + _form.WindowedSize.Width + "）");

                        _form.ShowMenu();
                        _step = 7;
                        break;

                    case 7:
                        Check(Screen is MenuScreen, "返回主页成功");
                        _form.ShowDifficulty();
                        _step = 8;
                        break;

                    // ---- 执黑开局：难度页选"执黑 · 后行"，开局应由电脑先走 ----
                    case 8:
                        {
                            Check(Screen is DifficultyScreen, "再次进入难度选择页");
                            FlatButton white = null, black = null;
                            foreach (Control c in Screen.Controls)
                            {
                                FlatButton b = c as FlatButton;
                                if (b == null) continue;
                                if (b.Text == "执白 · 先行") white = b;
                                else if (b.Text == "执黑 · 后行") black = b;
                            }
                            Check(white != null && black != null, "难度页有执白/执黑两个按钮");
                            Check(white != null && white.Highlighted, "默认选中执白");
                            Check(black != null && !black.Highlighted, "默认未选中执黑");
                            if (black != null)
                            {
                                int n = RealClickCount(black);
                                Check(n == 1, "执黑按钮一次点击只触发 1 次 Click（实际 " + n + " 次）");
                                Check(black.Highlighted, "点击后选中执黑");
                                Check(white != null && !white.Highlighted, "执白已取消选中");
                            }
                            FlatButton target = null;
                            foreach (Control c in Screen.Controls)
                            {
                                FlatButton b = c as FlatButton;
                                if (b != null && b.Text == "800") { target = b; break; }
                            }
                            Check(target != null, "找到 ELO 800 档位按钮");
                            if (target != null) RealClickCount(target);
                            _aiMoveStart = DateTime.Now;   // 从点档位这一拍开始计时（电脑此刻开始思考）
                        }
                        _step = 9;
                        _ticks = 0;
                        break;

                    case 9:
                        {
                            Check(Screen is GameScreen, "执黑后进入对局界面");
                            _gameScreen = Screen as GameScreen;
                            if (_gameScreen == null) { Finish(); return; }
                            _game = (ChessGame)Field(_gameScreen, "_game");
                            BoardView bd = (BoardView)Field(_gameScreen, "_board");
                            // 计时起点已在上一拍记录，这里不覆盖
                            Check(bd != null && bd.HumanSide == Board.Black, "人机对战：人类执黑");
                            Check(bd != null && !bd.WhiteAtBottom, "棋盘方向：黑方在下方（己方始终在下方）");
                            Check(_game != null && _game.Position.Side == Board.White, "开局轮到白方（电脑）");
                            _step = 10;
                            _ticks = 0;
                        }
                        break;

                    case 10:
                        if (_game.PlyCount >= 1)
                        {
                            Check(_game.PlyCount == 1, "执黑时电脑先走一步（PlyCount=" + _game.PlyCount + "）");
                            double aiMs = (DateTime.Now - _aiMoveStart).TotalMilliseconds;
                            Check(aiMs >= 500, "电脑开局落子同样有最小延迟（实际 " + aiMs.ToString("0") + " ms）");
                            Check(_game.Position.Side == Board.Black, "电脑走完后轮到玩家（黑方）");
                            Check(_game.SanHistory.Count == 1, "棋谱记录：" + string.Join(" ", _game.SanHistory.ToArray()));

                            // 执黑时悔棋会退到开局（此时又轮到电脑），验证电脑会被重新触发而不是卡住
                            FlatButton undo = (FlatButton)Field(_gameScreen, "_undoBtn");
                            RealClickCount(undo);
                            Check(_game.PlyCount == 0, "执黑悔棋回到开局（PlyCount=" + _game.PlyCount + "）");
                            _aiMoveStart = DateTime.Now;
                            _step = 11;
                            _ticks = 0;
                        }
                        else if (_ticks > 60)
                        {
                            Check(false, "等待电脑开局落子超时");
                            Finish();
                        }
                        break;

                    case 11:
                        if (_game.PlyCount >= 1)
                        {
                            Check(_game.PlyCount == 1, "悔棋回开局后电脑重新先走（PlyCount=" + _game.PlyCount + "）");
                            Check(_game.Position.Side == Board.Black, "电脑重走后仍轮到玩家（黑方）");
                            // 再验一次人人对战也有控制格按钮
                            _form.StartGame(GameMode.HumanVsHuman, null);
                            _step = 12;
                        }
                        else if (_ticks > 60)
                        {
                            Check(false, "执黑悔棋后等待电脑重新落子超时");
                            Finish();
                        }
                        break;

                    case 12:
                        {
                            Check(Screen is GameScreen, "进入人人对战界面");
                            GameScreen pvpScreen = Screen as GameScreen;
                            if (pvpScreen == null) { Finish(); return; }
                            BoardView pvpBoard = (BoardView)Field(pvpScreen, "_board");
                            FlatButton cbtn = (FlatButton)Field(pvpScreen, "_controlBtn");
                            Check(cbtn != null, "人人对战也有「控制格」按钮");
                            Check((FlatButton)Field(pvpScreen, "_analysisToggle") == null, "人人对战没有 AI 解析按钮（保持原样）");
                            if (cbtn != null && pvpBoard != null)
                            {
                                Check(!pvpBoard.ShowControl, "人人对战默认不显示控制格");
                                Check(RealClickCount(cbtn) == 1, "人人对战点控制格只触发 1 次 Click");
                                Check(pvpBoard.ShowControl, "人人对战可开启控制格");
                                // 翻转棋盘后控制格标记的角不变（画在屏幕的右上/左上）
                                pvpBoard.WhiteAtBottom = false;
                                Check(pvpBoard.ShowControl, "棋盘翻转后控制格仍然显示");
                                // 控制格视图的四色映射
                                Check(BoardView.ControlSquareColor(false, true) == Theme.ControlBlack,
                                      "控制格：只有黑方控制 → 黑色");
                                Check(BoardView.ControlSquareColor(true, false) == Theme.ControlWhite,
                                      "控制格：只有白方控制 → 白色");
                                Check(BoardView.ControlSquareColor(true, true) == Theme.ControlBoth,
                                      "控制格：双方都控制 → 浅灰");
                                Check(BoardView.ControlSquareColor(false, false) == Theme.ControlNone,
                                      "控制格：都没控制 → 浅绿");
                                Check(Theme.ControlNone == Theme.DarkSquare, "「都没控制」用的就是棋盘主色绿");
                                Check(Theme.ControlBoth != Theme.ControlNone &&
                                      Theme.ControlBoth != Theme.ControlWhite &&
                                      Theme.ControlBoth != Theme.ControlBlack,
                                      "浅灰与另外三色都可区分");
                            }
                            _form.ShowMenu();
                            _step = 13;
                            _ticks = 0;
                        }
                        break;

                    // ---- 定式学习 ----
                    case 13:
                        {
                            Check(Screen is MenuScreen, "回到主页");
                            FlatButton study = null;
                            foreach (Control c in Screen.Controls)
                            {
                                FlatButton b = c as FlatButton;
                                if (b != null && b.Text == "定式学习") { study = b; break; }
                            }
                            Check(study != null, "主页有「定式学习」入口");
                            if (study != null)
                            {
                                int n = RealClickCount(study);
                                Check(n == 1, "定式学习按钮一次点击只触发 1 次 Click（实际 " + n + " 次）");
                            }
                            _step = 14;
                        }
                        break;

                    case 14:
                        {
                            Check(Screen is OpeningListScreen, "进入定式列表页");
                            int rows = 0;
                            FlatButton first = null;
                            foreach (Control c in Screen.Controls)
                            {
                                Panel p = c as Panel;
                                if (p == null) continue;
                                foreach (Control cc in p.Controls)
                                {
                                    FlatButton b = cc as FlatButton;
                                    if (b == null) continue;
                                    if (first == null) first = b;
                                    rows++;
                                }
                            }
                            Check(rows == Openings.Count, "定式列表有 " + rows + " 行（数据 " + Openings.Count + " 个）");
                            Check(Openings.Count >= 24, "开局数已达 " + Openings.Count + " 个");
                            Check(first != null && first.Text.StartsWith("1"), "第一行带序号 1：" + (first == null ? "" : first.Text));
                            Check(first != null && first.Text.Contains("意大利开局"), "第一行是意大利开局");
                            if (first != null) RealClickCount(first);
                            _step = 15;
                            _ticks = 0;
                        }
                        break;

                    case 15:
                        {
                            Check(Screen is OpeningStudyScreen, "进入定式学习界面");
                            OpeningStudyScreen st = Screen as OpeningStudyScreen;
                            if (st == null) { Finish(); return; }
                            _study = st;
                            Check(st.Session.Opening.Id == "italian", "学的是意大利开局");
                            Check(st.HintCount == 1, "起始给出 1 个箭头（实际 " + st.HintCount + "）");
                            Check(st.Session.IsStudentTurn, "开局轮到学员走");
                            Check(st.Session.Opening.Plans.Length >= 2, "开局带典型计划 " +
                                  st.Session.Opening.Plans.Length + " 条");
                            Check(st.Session.Opening.Pitfalls.Length >= 1, "开局带常见错误 " +
                                  st.Session.Opening.Pitfalls.Length + " 条");
                            Check(!st.ShowingGuide, "默认显示本步点评");
                            st.ToggleGuide();
                            Check(st.ShowingGuide, "可以切换到「开局讲解」");
                            st.ToggleGuide();
                            Check(!st.ShowingGuide, "可以切回「本步点评」");

                            // 先故意走一步不在变化里的棋，应当被拒绝
                            bool off = st.PlayOffBookMove();
                            Check(!off, "不在教学变化里的着法会被拒绝");
                            Check(st.CurrentComment.Contains("箭头"), "拒绝时给出提示：" + st.CurrentComment);
                            Check(st.Session.Game.PlyCount == 0, "被拒绝后局面没有变化");

                            // 再照着箭头走（真实鼠标点击棋盘）
                            BoardView b = (BoardView)Field(st, "_board");
                            BoardView.MoveHint h = b.Hints[0];
                            int from = h.From, to = h.To;
                            // MouseBoard 收的是"逻辑棋盘坐标"（固定 560×560），不能传设备像素
                            const float lcell = 560f / 8f;
                            MouseBoard(b, (Squares.File(from) + 0.5f) * lcell, (7 - Squares.Rank(from) + 0.5f) * lcell);
                            Check(b.Selected == from, "点棋盘选中了起点格（Selected=" + b.Selected + "，期望 " + from +
                                  "，棋盘宽 " + b.ClientSize.Width + "，Interactive=" + b.Interactive + "）");
                            Check(b.Targets.Count == 1, "只保留教学着法作为可走目标（目标 " + b.Targets.Count +
                                  " 个，Allowed " + b.Allowed.Count + "，Hints " + b.Hints.Count + "）");
                            MouseBoard(b, (Squares.File(to) + 0.5f) * lcell, (7 - Squares.Rank(to) + 0.5f) * lcell);
                            Check(st.Session.Game.PlyCount == 1, "点棋盘按箭头走子成功（PlyCount=" + st.Session.Game.PlyCount + "）");
                            Check(st.CurrentComment.Contains("e4"), "走后给出这一步的点评：" + st.CurrentComment);
                            // 面板内容变化后必须真的重画（以前只重画棋盘，点评看着像没推进）
                            int paintBefore = st.PaintCount;
                            PumpTicks(2);
                            Check(st.PaintCount > paintBefore, "走后点评面板重新绘制了（重画 " +
                                  paintBefore + " → " + st.PaintCount + " 次）");
                            Check(st.CurrentComment.Contains("你上一步 e4"), "点评里有「你上一步」这段");
                            Check(st.CurrentComment.Contains(e2e4Comment()), "「你上一步」带上了这手棋的作用");
                            Check(st.IsWaitingReply, "轮到对手，程序准备应手");
                            _ticks = 0;
                            _step = 16;
                        }
                        break;

                    case 16:
                        // 等程序应手（有 600ms 延迟）
                        if (!_study.IsWaitingReply)
                        {
                            Check(_study.Session.Game.PlyCount == 2, "程序应了一手（PlyCount=" + _study.Session.Game.PlyCount + "）");
                            Check(_study.HintCount >= 1, "应手后重新出现箭头（" + _study.HintCount + " 个）");
                            Check(_study.CurrentComment.Contains("对手上一步 e5"), "应手后点评里有「对手上一步」这段");
                            Check(_study.CurrentComment.Contains("为什么要走这里") ||
                                  _study.CurrentComment.Contains("为什么这样走") ||
                                  _study.CurrentComment.Contains("该你走"),
                                  "应手后自动给出「我方下一步为什么走这里」：" + _study.CurrentComment);
                            Check(_study.WhyCount >= 1, "自动推进后列出了 " + _study.WhyCount + " 条下一步的理由");
                            // 内容随走棋变多的过程中，点评会长到超出可视区 —— 这时必须能滚轮滚动
                            int more = 0;
                            while (_study.PanelMaxScroll <= 0 && more++ < 24)
                            {
                                if (!_study.PlayFirstCandidate()) break;
                                int g2 = 0;
                                while (_study.IsWaitingReply && g2++ < 60)
                                {
                                    Application.DoEvents();
                                    System.Threading.Thread.Sleep(20);
                                }
                                PumpTicks(2);
                            }
                            Check(_study.PanelMaxScroll > 0, "走了 " + more + " 步后点评超出可视区（可滚动 " +
                                  _study.PanelMaxScroll + " 像素，手数 " + _study.Session.Game.PlyCount + "）");

                            int scrolled = 0;
                            for (int w = 0; w < 8; w++) { if (_study.ScrollPanel(-120)) scrolled++; }
                            Check(scrolled > 0 && _study.PanelScroll > 0, "滚轮向下能滚动点评（滚到 " +
                                  _study.PanelScroll + " 像素）");
                            int afterScroll = _study.PaintCount;
                            PumpTicks(2);
                            Check(_study.PaintCount > afterScroll, "滚动后点评面板也重画了（" +
                                  afterScroll + " → " + _study.PaintCount + " 次）");
                            for (int w = 0; w < 60; w++) _study.ScrollPanel(120);
                            Check(_study.PanelScroll == 0, "滚轮向上能回到顶部");
                            for (int w = 0; w < 900; w++) _study.ScrollPanel(-120);
                            Check(_study.PanelScroll == _study.PanelMaxScroll && _study.PanelMaxScroll > 0,
                                  "向下滚动夹在最大值（" + _study.PanelScroll + " = " + _study.PanelMaxScroll + "）");

                            // 再走一步：面板内容自动推进，并且滚回顶部
                            int beforeAdvance = _study.PaintCount;
                            _study.PlayFirstCandidate();
                            PumpTicks(2);
                            Check(_study.PanelScroll == 0, "内容推进后自动滚回顶部");
                            Check(_study.PaintCount > beforeAdvance, "内容推进后点评面板重画了（" +
                                  beforeAdvance + " → " + _study.PaintCount + " 次）");

                            // 开局讲解内容更长，同样可滚动
                            _study.ToggleGuide();
                            PumpTicks(2);
                            Check(_study.PanelMaxScroll > 0, "开局讲解同样可滚动（" + _study.PanelMaxScroll + " 像素）");
                            _study.ToggleGuide();
                            Check(_study.PanelScroll == 0, "切回本步点评后回到顶部");
                            _step = 17;
                        }
                        else if (_ticks > 40)
                        {
                            Check(false, "等待程序应手超时");
                            Finish();
                        }
                        break;

                    case 17:
                        {
                            // 分支：英国式开局 1.c4 e5 之后，学员有两步可选 → 两个箭头
                            OpeningStudyScreen st2 = new OpeningStudyScreen(_form, Openings.ById("english"));
                            Check(st2.HintCount == 1, "英国式第一步只有 1 个箭头");
                            _study2 = st2;
                            Check(st2.PlayFirstCandidate(), "走出 1.c4");
                            _ticks = 0;
                            _step = 18;
                        }
                        break;

                    case 18:
                        // 等程序应 e5（计时器），然后应当出现两个箭头
                        if (!_study2.IsWaitingReply)
                        {
                            Check(_study2.Session.Game.PlyCount == 2, "程序应手完成（PlyCount=" + _study2.Session.Game.PlyCount + "）");
                            Check(_study2.HintCount == 2, "分支处出现 2 个箭头让学员选（实际 " + _study2.HintCount + "）");
                            Check(_study2.CandidateNames().Contains("Nc3") && _study2.CandidateNames().Contains("g3"),
                                "两个箭头分别是 Nc3 与 g3：" + _study2.CandidateNames());
                            Check(_study2.PlayCandidate(1), "可以选第二条变化走");
                            Check(_study2.Session.Game.PlyCount == 3, "选完后局面前进（PlyCount=" + _study2.Session.Game.PlyCount + "）");
                            _study2.Dispose();
                            Finish();
                        }
                        else if (_ticks > 40)
                        {
                            Check(false, "等待程序应手超时（分支检查）");
                            Finish();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Check(false, "异常：" + ex.GetType().Name + " " + ex.Message);
                Finish();
            }
            finally
            {
                _inTick = false;
            }
        }

        private static void Finish()
        {
            _step = 99;
            _timer.Stop();
            _form.Close();
        }

        /// <summary>逻辑像素 → 当前 DPI 下的设备像素（断言用）。</summary>
        private static int L(int logical)
        {
            float f = _form != null ? _form.Dpi : 1f;
            return (int)Math.Round(logical * f);
        }

        private static Control Screen
        {
            get
            {
                foreach (Control c in _form.Controls) return c;
                return null;
            }
        }

        private static object Field(object obj, string name)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return fi == null ? null : fi.GetValue(obj);
        }

        /// <summary>把按键交给窗体的 ProcessCmdKey（真实快捷键路径）。</summary>
        private static bool SendKey(Keys key)
        {
            MethodInfo pk = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance);
            if (pk == null) return false;
            Message msg = Message.Create(_form.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
            object[] args = new object[] { msg, key };
            object r = pk.Invoke(_form, args);
            return r is bool && (bool)r;
        }

        private static void ClickControl(Control c)
        {
            MethodInfo mi = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null) mi.Invoke(c, new object[] { EventArgs.Empty });
        }

        // ---- 真实鼠标消息点击（与 Windows 真实点击走同一条代码路径）----
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private static int _clickCounter;
        private static DateTime _aiMoveStart;
        private static OpeningStudyScreen _study;
        private static OpeningStudyScreen _study2;

        /// <summary>
        /// 用窗口消息点击控件中心。
        /// 注意：不能直接调用受保护的 OnClick —— 那会绕过基类的 Click 派发，
        /// 正是这个差异让"一次点击触发两次 Click"的缺陷躲过了早期自检。
        /// </summary>
        private static void RealClick(Control c)
        {
            Point p = new Point(c.Width / 2, c.Height / 2);
            IntPtr lp = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
            SendMessage(c.Handle, WM_MOUSEMOVE, IntPtr.Zero, lp);
            SendMessage(c.Handle, WM_LBUTTONDOWN, (IntPtr)1, lp);
            SendMessage(c.Handle, WM_LBUTTONUP, IntPtr.Zero, lp);
        }

        /// <summary>真实点击一次，返回 Click 事件被触发的次数（正常应为 1）。</summary>
        private static int RealClickCount(Control c)
        {
            _clickCounter = 0;
            EventHandler probe = delegate { _clickCounter++; };
            c.Click += probe;
            RealClick(c);
            c.Click -= probe;
            return _clickCounter;
        }

        /// <summary>用按下 + 抬起的鼠标事件点击控件中心（备用路径）。</summary>
        private static void ClickMouse(Control c)
        {
            MethodInfo down = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo up = typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance);
            Point p = new Point(c.Width / 2, c.Height / 2);
            if (down != null) down.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
            if (up != null) up.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
        }

        /// <summary>点击棋盘上的某个逻辑坐标（逻辑棋盘固定 560×560；声明 DPI 感知后控件会按 DPI 变大）。</summary>
        private static void MouseBoard(Control board, float logicalX, float logicalY)
        {
            float f = board.ClientSize.Width / 560f;
            Mouse(board, (int)Math.Round(logicalX * f), (int)Math.Round(logicalY * f));
        }

        private static void Mouse(Control c, int x, int y)
        {
            MethodInfo mi = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null) mi.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
        }
    }
}
