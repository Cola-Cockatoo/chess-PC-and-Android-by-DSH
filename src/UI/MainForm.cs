using System;
using System.Drawing;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>主窗口：在主页 / 难度选择 / 对局三个界面之间切换，并支持全屏。</summary>
    public class MainForm : Form
    {
        private Control _screen;
        private bool _fullscreen;
        private bool _analysisPanel;
        private Rectangle _windowedBounds;
        private FormBorderStyle _windowedBorder;
        private bool _windowedMaximizeBox;

        /// <summary>测试用：伪造 DPI 缩放（例如 1.5 表示 150%），便于在没有高分屏的机器上核对排版。</summary>
        public static float ForcedDpiScale;

        /// <summary>逻辑画布尺寸（设备像素 = 逻辑像素 × DPI 缩放）。</summary>
        private const int LogicalW = 940;
        private const int LogicalWAnalysis = 1264;
        private const int LogicalH = 700;

        /// <summary>当前显示器的 DPI 缩放（96 DPI = 1.0）。声明了 DPI 感知后这里会返回真实值。
        /// 注意：构造函数里句柄还没建好，此时用屏幕 DC（不会提前创建窗口句柄，否则 StartPosition 会失效）。</summary>
        private float DpiScale()
        {
            if (ForcedDpiScale > 0.1f) return ForcedDpiScale;
            try
            {
                if (IsHandleCreated)
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        if (g.DpiX > 1f) return g.DpiX / 96f;
                    }
                }
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (g.DpiX > 1f) return g.DpiX / 96f;
                }
            }
            catch
            {
            }
            return 1f;
        }

        /// <summary>当前 DPI 缩放（自检/截图工具用来算期望尺寸）。</summary>
        public float Dpi
        {
            get { return DpiScale(); }
        }

        private int Scaled(int logical)
        {
            return (int)Math.Round(logical * DpiScale());
        }

        public MainForm()
        {
            Text = "国际象棋";
            // 逻辑 940×700，按显示器 DPI 换算成设备像素：150% 缩放下就是 1410×1050
            ClientSize = new Size(Scaled(LogicalW), Scaled(LogicalH));
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(Scaled(760), Scaled(560));      // 再小就缩得看不清了
            MinimizeBox = true;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            KeyPreview = true;
            Font = Theme.Font(14.5f, FontStyle.Regular);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            ShowMenu();
        }

        /// <summary>当前是否全屏。</summary>
        public bool IsFullscreen
        {
            get { return _fullscreen; }
        }

        /// <summary>F11 切换全屏、Esc 退出全屏。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullscreen();
                return true;
            }
            if (keyData == Keys.Escape && _fullscreen)
            {
                SetFullscreen(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ToggleFullscreen()
        {
            SetFullscreen(!_fullscreen);
        }

        public void SetFullscreen(bool on)
        {
            if (_fullscreen == on) return;
            _fullscreen = on;

            if (on)
            {
                _windowedBounds = Bounds;
                _windowedBorder = FormBorderStyle;
                _windowedMaximizeBox = MaximizeBox;

                // 无边框 + 最大化：铺满整块屏幕（含任务栏区域）
                FormBorderStyle = FormBorderStyle.None;
                MaximizeBox = false;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                WindowState = FormWindowState.Normal;
                FormBorderStyle = _windowedBorder;
                MaximizeBox = _windowedMaximizeBox;
                Bounds = _windowedBounds;
                ApplyWindowedSize();
            }

            if (_screen != null) _screen.Invalidate(true);
        }

        private void SetScreen(Control c)
        {
            SuspendLayout();
            Control old = _screen;
            _screen = c;
            c.Dock = DockStyle.Fill;
            Controls.Add(c);
            c.BringToFront();
            if (old != null)
            {
                Controls.Remove(old);
                old.Dispose();
            }
            ResumeLayout(true);
        }

        public void ShowMenu()
        {
            SetScreen(new MenuScreen(this));
        }

        /// <summary>滚轮：转发给定式学习界面，让点评面板能滚动
        /// （子控件不处理滚轮时消息会冒泡到这里，作为焦点不在面板上时的保险）。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            OpeningStudyScreen study = _screen as OpeningStudyScreen;
            if (study != null && study.ScrollPanel(e.Delta)) return;
            base.OnMouseWheel(e);
        }

        /// <summary>对局结束后进入复盘。</summary>
        public void StartReview(ChessGame game, GameMode mode, int humanSide)
        {
            if (game == null || game.MoveHistory.Count == 0) return;
            SetScreen(new ReviewScreen(this, game, mode, humanSide));
        }

        /// <summary>定式学习：开局列表。</summary>
        public void ShowOpeningList()
        {
            SetScreen(new OpeningListScreen(this));
        }

        /// <summary>进入某个开局的学习界面。</summary>
        public void StartStudy(Opening opening)
        {
            SetScreen(new OpeningStudyScreen(this, opening));
        }

        public void ShowDifficulty()
        {
            SetScreen(new DifficultyScreen(this));
        }

        public void StartGame(GameMode mode, SkillLevel skill)
        {
            StartGame(mode, skill, Board.White);
        }

        /// <summary>开始对局；humanSide 指定人机对战时玩家执哪一方（人人对战忽略）。</summary>
        public void StartGame(GameMode mode, SkillLevel skill, int humanSide)
        {
            GameScreen screen = new GameScreen(this, mode, skill, humanSide);
            SetScreen(screen);
            screen.StartIfAiMovesFirst();     // 玩家执黑时，开局由电脑（白方）先走
        }

        /// <summary>AI 解析面板在窗口化模式下需要更宽的窗口；全屏时由界面自己排版。</summary>
        public void SetAnalysisPanelVisible(bool visible)
        {
            _analysisPanel = visible;
            if (_fullscreen) return;
            ApplyWindowedSize();
        }

        /// <summary>窗口化时该有的客户区尺寸（含解析面板时更宽；超出工作区则等比缩小）。
        /// ApplyWindowedSize 与自检共用它，避免两处算出不同结果。</summary>
        public Size WindowedSize
        {
            get
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int w = Scaled(_analysisPanel ? LogicalWAnalysis : LogicalW);
                int h = Scaled(LogicalH);
                if (w > wa.Width - 40) w = Math.Max(Scaled(760), wa.Width - 40);
                if (h > wa.Height - 40) h = Math.Max(Scaled(560), wa.Height - 40);
                return new Size(w, h);
            }
        }

        /// <summary>窗口化模式下的客户区尺寸（逻辑 940×700 或 1264×700，按 DPI 换算）；
        /// 屏幕放不下时自动缩小。用户自己最大化/缩放过的窗口不去动它。</summary>
        private void ApplyWindowedSize()
        {
            if (WindowState != FormWindowState.Normal) return;
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            Size target = WindowedSize;
            if (ClientSize.Width == target.Width && ClientSize.Height == target.Height) return;
            ClientSize = target;

            int x = Location.X;
            int y = Location.Y;
            if (x + Width > wa.Right - 4) x = wa.Right - Width - 4;
            if (y + Height > wa.Bottom - 4) y = wa.Bottom - Height - 4;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            Location = new Point(x, y);
        }
    }
}
