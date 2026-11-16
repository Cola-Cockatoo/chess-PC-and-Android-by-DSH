using System;
using System.Drawing;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 定式学习 · 开局列表：序号 + 名称 + 执子，点一行进入学习界面。
    /// 逻辑画布 940×700，按窗口等比缩放。
    /// </summary>
    public class OpeningListScreen : UserControl
    {
        private const float LogicalW = 940f;
        private const float LogicalH = 700f;

        private readonly MainForm _main;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _hint;
        private readonly FlatButton _back;
        private readonly FlatButton _btnFullscreen;
        private readonly Panel _scroll = new Panel();
        private readonly FlatButton[] _rows;

        public OpeningListScreen(MainForm main)
        {
            _main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            DoubleBuffered = true;

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

            _title = Ui.MakeLabel("定式学习", 30f, FontStyle.Bold, Theme.Text,
                new Rectangle(0, 30, 940, 46), ContentAlignment.MiddleCenter);
            Controls.Add(_title);
            _subtitle = Ui.MakeLabel("常用开局与主要变化 · 跟着箭头走，每一步都有点评", 13.5f, FontStyle.Regular,
                Theme.TextMuted, new Rectangle(0, 80, 940, 24), ContentAlignment.MiddleCenter);
            Controls.Add(_subtitle);

            _scroll.BackColor = Theme.Background;
            _scroll.AutoScroll = true;
            _scroll.BorderStyle = BorderStyle.None;
            Controls.Add(_scroll);

            Opening[] all = Openings.All();
            _rows = new FlatButton[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                Opening op = all[i];
                FlatButton b = new FlatButton();
                b.ButtonStyle = FlatButtonStyle.Secondary;
                b.CornerRadius = 12;
                b.Text = (i + 1) + "  " + op.Name + "   （" + (op.Side == "w" ? "执白" : "执黑") + "）";
                b.SubText = op.English + " · " + op.Eco + " · " + op.Level;
                Opening captured = op;
                b.Click += delegate { _main.StartStudy(captured); };
                b.MouseEnter += delegate
                {
                    _hint.Text = "【" + captured.Level + "】" + captured.Summary;
                    _hint.ForeColor = Theme.AccentDark;
                };
                b.MouseLeave += delegate { _hint.Text = HintText(); _hint.ForeColor = Theme.TextMuted; };
                _scroll.Controls.Add(b);
                _rows[i] = b;
            }

            _hint = Ui.MakeLabel(HintText(), 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(30, 656, 880, 30), ContentAlignment.MiddleCenter);
            Controls.Add(_hint);

            ApplyLayout();
        }

        private string HintText()
        {
            return "共 " + _rows.Length + " 个开局 · 鼠标移到某一行可预览思路与难度";
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_title == null || Width <= 0) return;
            UiScale.Fit(Width, Height, LogicalW, LogicalH);

            UiScale.Font(_title, 30f, FontStyle.Bold);
            UiScale.Font(_subtitle, 13.5f, FontStyle.Regular);
            UiScale.Font(_hint, 13f, FontStyle.Regular);
            UiScale.Font(_back, 14.5f, FontStyle.Regular);
            UiScale.Font(_btnFullscreen, 14f, FontStyle.Regular);
            _btnFullscreen.Text = _main.IsFullscreen ? "退出" : "全屏";

            _back.Bounds = UiScale.R(22, 20, 96, 36);
            _btnFullscreen.Bounds = UiScale.R(834, 20, 84, 34);
            _title.Bounds = UiScale.R(0, 30, 940, 46);
            _subtitle.Bounds = UiScale.R(0, 80, 940, 24);
            _hint.Bounds = UiScale.R(30, 656, 880, 30);
            _scroll.Bounds = UiScale.R(30, 116, 880, 528);

            int rowH = UiScale.Px(56);
            int gap = UiScale.Px(8);
            int w = _scroll.ClientSize.Width - UiScale.Px(2);
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i].Bounds = new Rectangle(0, i * (rowH + gap), w, rowH);
                UiScale.Font(_rows[i], 16f, FontStyle.Bold);
            }
            Invalidate();
        }
    }
}
