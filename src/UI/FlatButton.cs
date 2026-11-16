using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    public enum FlatButtonStyle
    {
        Primary,     // 实心强调色
        Secondary,   // 白底描边
        Ghost,       // 无底色
        Tile         // 难度砖块
    }

    /// <summary>扁平按钮（圆角、悬停/按下/禁用状态）。</summary>
    public class FlatButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private FlatButtonStyle _style = FlatButtonStyle.Secondary;
        private string _subText = "";
        private int _radius = 10;

        public FlatButton()
        {
            // 注意：必须关掉 StandardClick —— 否则基类在 WmMouseUp 里会自己抛一次 Click，
            // 加上下面 OnMouseUp 里显式抛的一次，一次真实鼠标点击会触发两次 Click
            // （对开关类按钮表现为"点了没反应"，对悔棋表现为一次退两步）。
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardClick, false);
            BackColor = Color.Transparent;
            Font = Theme.Font(15f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public FlatButtonStyle ButtonStyle
        {
            get { return _style; }
            set { _style = value; Invalidate(); }
        }

        public string SubText
        {
            get { return _subText; }
            set { _subText = value == null ? "" : value; Invalidate(); }
        }

        public int CornerRadius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        /// <summary>选中高亮（难度砖块用）。</summary>
        public bool Highlighted { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                _pressed = false;
                Invalidate();
                OnClick(EventArgs.Empty);
            }
            _pressed = false;
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Theme.PrepareText(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, textColor;
            int radius = UiScale.Px(_radius);

            bool on = Enabled;
            switch (_style)
            {
                case FlatButtonStyle.Primary:
                    fill = on ? (_pressed ? Theme.AccentDark : (_hover ? Theme.AccentDark : Theme.Accent)) : Color.FromArgb(0xC9, 0xD3, 0xCE);
                    border = fill;
                    textColor = Color.White;
                    break;
                case FlatButtonStyle.Tile:
                    fill = on ? (_hover || _pressed ? Theme.AccentSoft : Theme.Surface) : Color.FromArgb(0xF0, 0xEE, 0xEA);
                    border = Highlighted ? Theme.Accent : (_hover ? Theme.Accent : Theme.Border);
                    textColor = on ? Theme.Text : Theme.TextMuted;
                    break;
                case FlatButtonStyle.Ghost:
                    fill = _hover ? Color.FromArgb(0xEC, 0xEA, 0xE4) : Color.Transparent;
                    border = Color.Transparent;
                    textColor = on ? Theme.TextMuted : Color.FromArgb(0xD0, 0xCD, 0xC6);
                    break;
                default:
                    // Highlighted：用作"二选一"的选中态（如执白/执黑），选中项填充强调色浅底 + 强调色描边
                    fill = on ? (Highlighted ? Theme.AccentSoft
                        : (_pressed ? Color.FromArgb(0xEC, 0xEA, 0xE4) : (_hover ? Color.FromArgb(0xF3, 0xF1, 0xEC) : Theme.Surface)))
                        : Color.FromArgb(0xF2, 0xF0, 0xEC);
                    border = Highlighted ? Theme.Accent : (_hover && on ? Theme.Accent : Theme.Border);
                    textColor = Highlighted ? Theme.AccentDark : (on ? Theme.Text : Theme.TextMuted);
                    break;
            }

            if (fill.A > 0)
            {
                using (GraphicsPath path = Theme.RoundRect(r, radius))
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (border.A > 0)
                    {
                        using (Pen p = new Pen(border, UiScale.Pxf(1.2f))) g.DrawPath(p, path);
                    }
                }
            }

            if (_subText.Length > 0)
            {
                Rectangle top = new Rectangle(0, UiScale.Px(8), Width, Height / 2);
                Rectangle bottom = new Rectangle(0, Height / 2 + UiScale.Px(2), Width, Height / 2 - UiScale.Px(6));
                Theme.DrawText(g, Text, Theme.Font(15f, FontStyle.Bold), textColor, top, ContentAlignment.MiddleCenter);
                Color sub = _style == FlatButtonStyle.Tile ? Theme.TextMuted : Theme.WithAlpha(textColor, 200);
                Theme.DrawText(g, _subText, Theme.Font(12.5f, FontStyle.Regular), sub, bottom, ContentAlignment.MiddleCenter);
            }
            else
            {
                Theme.DrawText(g, Text, Font, textColor, new Rectangle(0, 0, Width, Height), ContentAlignment.MiddleCenter);
            }
        }
    }
}
