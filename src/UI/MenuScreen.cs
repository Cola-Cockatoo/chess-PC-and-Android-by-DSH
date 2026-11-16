using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>主页：人机对战 / 人人对战。逻辑画布 940×700，按窗口等比缩放。</summary>
    public class MenuScreen : UserControl
    {
        private const float LogicalW = 940f;
        private const float LogicalH = 700f;

        private readonly MainForm _main;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _hint1;
        private readonly Label _hint2;
        private readonly FlatButton _btnAi;
        private readonly FlatButton _btnPvp;
        private readonly FlatButton _btnStudy;
        private readonly FlatButton _btnFullscreen;

        public MenuScreen(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            _title = Ui.MakeLabel("国际象棋", 46f, FontStyle.Bold, Theme.Text,
                new Rectangle(0, 186, 940, 66), ContentAlignment.MiddleCenter);
            Controls.Add(_title);
            _subtitle = Ui.MakeLabel("简约 · 本地对局 · 无需联网", 14f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 288, 940, 24), ContentAlignment.MiddleCenter);
            Controls.Add(_subtitle);

            _btnAi = new FlatButton();
            _btnAi.ButtonStyle = FlatButtonStyle.Primary;
            _btnAi.Text = "人机对战";
            _btnAi.Click += delegate { _main.ShowDifficulty(); };
            Controls.Add(_btnAi);

            _btnPvp = new FlatButton();
            _btnPvp.ButtonStyle = FlatButtonStyle.Secondary;
            _btnPvp.Text = "人人对战（本地）";
            _btnPvp.Click += delegate { _main.StartGame(GameMode.HumanVsHuman, null); };
            Controls.Add(_btnPvp);

            _btnStudy = new FlatButton();
            _btnStudy.ButtonStyle = FlatButtonStyle.Secondary;
            _btnStudy.Text = "定式学习";
            _btnStudy.Click += delegate { _main.ShowOpeningList(); };
            Controls.Add(_btnStudy);

            _hint1 = Ui.MakeLabel("每走一步自动翻转棋盘 · 双方轮流操作同一台电脑", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 508, 940, 22), ContentAlignment.MiddleCenter);
            Controls.Add(_hint1);
            _hint2 = Ui.MakeLabel("ELO 100 – 2000 · 共 20 档难度", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 640, 940, 22), ContentAlignment.MiddleCenter);
            Controls.Add(_hint2);

            _btnFullscreen = new FlatButton();
            _btnFullscreen.ButtonStyle = FlatButtonStyle.Ghost;
            _btnFullscreen.Text = "全屏";
            _btnFullscreen.Click += delegate { _main.ToggleFullscreen(); };
            Controls.Add(_btnFullscreen);

            ApplyLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyLayout();
        }

        /// <summary>按当前客户区尺寸重新排版（全屏时整体放大）。</summary>
        private void ApplyLayout()
        {
            if (_title == null || Width <= 0) return;
            UiScale.Fit(Width, Height, LogicalW, LogicalH);

            UiScale.Font(_title, 46f, FontStyle.Bold);
            UiScale.Font(_subtitle, 14f, FontStyle.Regular);
            UiScale.Font(_hint1, 13f, FontStyle.Regular);
            UiScale.Font(_hint2, 13f, FontStyle.Regular);

            _title.Bounds = UiScale.R(0, 186, 940, 66);
            _subtitle.Bounds = UiScale.R(0, 288, 940, 24);
            _btnAi.Bounds = UiScale.R(320, 352, 300, 62);
            _btnPvp.Bounds = UiScale.R(320, 434, 300, 62);
            _btnStudy.Bounds = UiScale.R(320, 516, 300, 62);
            _hint1.Bounds = UiScale.R(0, 592, 940, 22);
            _hint2.Bounds = UiScale.R(0, 640, 940, 22);
            _btnFullscreen.Bounds = UiScale.R(816, 20, 104, 34);

            UiScale.Font(_btnAi, 17f, FontStyle.Regular);
            UiScale.Font(_btnPvp, 16f, FontStyle.Regular);
            UiScale.Font(_btnStudy, 16f, FontStyle.Regular);
            UiScale.Font(_btnFullscreen, 14f, FontStyle.Regular);
            _btnFullscreen.Text = _main.IsFullscreen ? "退出全屏" : "全屏";

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            // 顶部装饰棋子
            using (Font f = new Font(Theme.PieceFontFamily, 104f * UiScale.Factor, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(Theme.WithAlpha(Theme.Accent, 60)))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("\u265E", f, b, (RectangleF)UiScale.R(0, 40, 940, 130), sf);
                sf.Dispose();
            }

            // 标题下的短线
            using (Brush b = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(b, UiScale.Rx(470) - UiScale.Px(30), UiScale.Ry(268), UiScale.Px(60), UiScale.Px(3));
            }
        }
    }
}
