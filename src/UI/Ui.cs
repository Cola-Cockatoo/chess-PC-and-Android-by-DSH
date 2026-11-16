using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Chess.UI
{
    /// <summary>界面小工具。</summary>
    public static class Ui
    {
        public static Label MakeLabel(string text, float size, FontStyle style, Color color, Rectangle rect, ContentAlignment align)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Font(size, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.Bounds = rect;
            l.TextAlign = align;
            l.AutoSize = false;
            l.UseMnemonic = false;
            return l;
        }

        public static Label Title(string text, Rectangle rect)
        {
            return MakeLabel(text, 30f, FontStyle.Bold, Theme.Text, rect, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>升变选择对话框（无边框、简约）。</summary>
    public class PromotionDialog : Form
    {
        public int Chosen = PT.Queen;

        public PromotionDialog(int color)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            ClientSize = new Size(UiScale.Px(340), UiScale.Px(132));
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;

            Label title = Ui.MakeLabel("选择升变棋子", 14.5f, FontStyle.Bold, Theme.Text,
                UiScale.Rel(0, 12, 340, 24), ContentAlignment.MiddleCenter);
            Controls.Add(title);

            int[] types = new int[] { PT.Queen, PT.Rook, PT.Bishop, PT.Knight };
            int bw = UiScale.Px(66), gap = UiScale.Px(12);
            int total = bw * 4 + gap * 3;
            int x = (ClientSize.Width - total) / 2;
            for (int i = 0; i < types.Length; i++)
            {
                int t = types[i];
                FlatButton b = new FlatButton();
                b.ButtonStyle = FlatButtonStyle.Tile;
                b.CornerRadius = 10;
                b.Font = Theme.Font(30f, FontStyle.Regular, Theme.PieceFontFamily);
                b.ForeColor = Theme.Text;
                b.Text = GlyphOf(t).ToString();
                b.Bounds = new Rectangle(x + i * (bw + gap), UiScale.Px(46), bw, UiScale.Px(64));
                b.Click += delegate(object s, EventArgs e)
                {
                    Chosen = t;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(b);
            }
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
            }
            return '\u265F';
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border, UiScale.Pxf(1.4f)))
            {
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            base.OnKeyDown(e);
        }

        public static int Ask(IWin32Window owner, int color)
        {
            using (PromotionDialog d = new PromotionDialog(color))
            {
                d.ShowDialog(owner);
                return d.Chosen;
            }
        }
    }
}
