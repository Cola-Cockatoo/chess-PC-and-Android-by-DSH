using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>
    /// 人机对战难度选择：ELO 100 ~ 2000，每 100 分一档，共 20 档。
    /// 逻辑画布 940×700，按窗口等比缩放。
    /// </summary>
    public class DifficultyScreen : UserControl
    {
        private const float LogicalW = 940f;
        private const float LogicalH = 700f;

        private readonly MainForm _main;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _hint;
        private readonly Label _note;
        private readonly Label _sideCaption;
        private readonly FlatButton _sideWhite;
        private readonly FlatButton _sideBlack;
        private readonly FlatButton _back;
        private readonly FlatButton _btnFullscreen;
        private readonly List<FlatButton> _tiles = new List<FlatButton>();

        /// <summary>玩家选择的执子（默认执白，保持原有行为）。</summary>
        private int _humanSide = Board.White;

        public DifficultyScreen(MainForm main)
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

            _title = Ui.MakeLabel("选择难度", 30f, FontStyle.Bold, Theme.Text,
                new Rectangle(0, 40, 940, 46), ContentAlignment.MiddleCenter);
            Controls.Add(_title);
            _subtitle = Ui.MakeLabel("ELO 等级分 100 – 2000 · 每 100 分一档 · 共 20 档", 13.5f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 90, 940, 24), ContentAlignment.MiddleCenter);
            Controls.Add(_subtitle);

            // ---- 执白 / 执黑（放在难度档位下方，点击档位即按此执子开局）----
            _sideCaption = Ui.MakeLabel("你的执子", 13.5f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 536, 940, 22), ContentAlignment.MiddleCenter);
            Controls.Add(_sideCaption);

            _sideWhite = new FlatButton();
            _sideWhite.ButtonStyle = FlatButtonStyle.Secondary;
            _sideWhite.Text = "执白 · 先行";
            _sideWhite.Click += delegate { SetHumanSide(Board.White); };
            Controls.Add(_sideWhite);

            _sideBlack = new FlatButton();
            _sideBlack.ButtonStyle = FlatButtonStyle.Secondary;
            _sideBlack.Text = "执黑 · 后行";
            _sideBlack.Click += delegate { SetHumanSide(Board.Black); };
            Controls.Add(_sideBlack);

            _hint = Ui.MakeLabel("点击任意档位开始对局 · 你执白先行", 13.5f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 612, 940, 26), ContentAlignment.MiddleCenter);
            Controls.Add(_hint);
            _note = Ui.MakeLabel("档位越高，电脑算得越深、失误越少", 13f, FontStyle.Regular, Theme.TextMuted,
                new Rectangle(0, 650, 940, 22), ContentAlignment.MiddleCenter);
            Controls.Add(_note);

            for (int i = 0; i < SkillTable.Levels.Length; i++)
            {
                SkillLevel skill = SkillTable.Levels[i];
                FlatButton b = new FlatButton();
                b.ButtonStyle = FlatButtonStyle.Tile;
                b.CornerRadius = 12;
                b.Text = skill.Elo.ToString();
                b.SubText = skill.Tier;
                SkillLevel captured = skill;
                b.Click += delegate { _main.StartGame(GameMode.HumanVsAi, captured, _humanSide); };
                b.MouseEnter += delegate
                {
                    _hint.Text = "ELO " + captured.Elo + " · " + captured.Tier + " — " + captured.Desc;
                    _hint.ForeColor = Theme.AccentDark;
                };
                b.MouseLeave += delegate
                {
                    _hint.Text = HintText();
                    _hint.ForeColor = Theme.TextMuted;
                };
                Controls.Add(b);
                _tiles.Add(b);
            }

            SetHumanSide(_humanSide);
            ApplyLayout();
        }

        private string HintText()
        {
            return _humanSide == Board.White
                ? "点击任意档位开始对局 · 你执白先行"
                : "点击任意档位开始对局 · 你执黑后行";
        }

        private void SetHumanSide(int side)
        {
            _humanSide = side == Board.Black ? Board.Black : Board.White;
            if (_sideWhite != null) _sideWhite.Highlighted = _humanSide == Board.White;
            if (_sideBlack != null) _sideBlack.Highlighted = _humanSide == Board.Black;
            if (_hint != null)
            {
                _hint.Text = HintText();
                _hint.ForeColor = Theme.TextMuted;
            }
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
            UiScale.Font(_sideCaption, 13.5f, FontStyle.Regular);
            UiScale.Font(_sideWhite, 15f, FontStyle.Regular);
            UiScale.Font(_sideBlack, 15f, FontStyle.Regular);
            UiScale.Font(_hint, 13.5f, FontStyle.Regular);
            UiScale.Font(_note, 13f, FontStyle.Regular);
            UiScale.Font(_back, 14.5f, FontStyle.Regular);
            UiScale.Font(_btnFullscreen, 14f, FontStyle.Regular);
            _btnFullscreen.Text = _main.IsFullscreen ? "退出全屏" : "全屏";

            _back.Bounds = UiScale.R(22, 20, 96, 36);
            _btnFullscreen.Bounds = UiScale.R(816, 20, 104, 34);
            _title.Bounds = UiScale.R(0, 40, 940, 46);
            _subtitle.Bounds = UiScale.R(0, 90, 940, 24);
            _sideCaption.Bounds = UiScale.R(0, 536, 940, 22);
            _sideWhite.Bounds = UiScale.R(308, 562, 156, 42);
            _sideBlack.Bounds = UiScale.R(476, 562, 156, 42);
            _hint.Bounds = UiScale.R(0, 612, 940, 26);
            _note.Bounds = UiScale.R(0, 650, 940, 22);

            const float tileW = 158f, tileH = 86f, gap = 16f;
            const int cols = 5;
            float gridW = cols * tileW + (cols - 1) * gap;
            float x0 = (LogicalW - gridW) / 2f;
            const float y0 = 138f;

            for (int i = 0; i < _tiles.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                _tiles[i].Bounds = UiScale.R(x0 + col * (tileW + gap), y0 + row * (tileH + gap), tileW, tileH);
                UiScale.Font(_tiles[i], 15f, FontStyle.Bold);
            }

            Invalidate();
        }
    }
}
