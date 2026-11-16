using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 开发用：把 12 个棋子（6 种 × 黑白）渲染成一张精灵图，供手机版 canvas 直接贴图。
/// 与桌面版完全同一套字形与描边规则（Segoe UI Symbol + 白棋描边），
/// 这样手机端不依赖设备字体是否带国际象棋符号。
///
/// 用法： makepieces.exe 输出目录
/// 产出： pieces.png（6 列 × 2 行，白方在上）与 pieces.js（base64 内联版）
/// </summary>
public static class MakePieces
{
    private const int Cell = 128;

    private static char GlyphOf(int type)
    {
        switch (type)
        {
            case 1: return '\u265F';   // 兵
            case 2: return '\u265E';   // 马
            case 3: return '\u265D';   // 象
            case 4: return '\u265C';   // 车
            case 5: return '\u265B';   // 后
            case 6: return '\u265A';   // 王
        }
        return '\u265F';
    }

    [STAThread]
    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);

        int w = Cell * 6, h = Cell * 2;
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            for (int row = 0; row < 2; row++)          // 0 = 白方, 1 = 黑方
            {
                for (int type = 1; type <= 6; type++)
                {
                    GraphicsPath path = BuildPath(type, Cell * 0.82f);
                    using (Matrix m = new Matrix())
                    {
                        m.Translate(Cell * (type - 1) + Cell / 2f, Cell * row + Cell / 2f + Cell * 0.01f);
                        GraphicsState st = g.Save();
                        g.Transform = m;
                        if (row == 0)
                        {
                            using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, path);
                            using (Pen p = new Pen(Color.FromArgb(0x38, 0x38, 0x35), Cell * 0.032f))
                            {
                                p.LineJoin = LineJoin.Round;
                                g.DrawPath(p, path);
                            }
                        }
                        else
                        {
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(0x33, 0x32, 0x2E))) g.FillPath(b, path);
                        }
                        g.Restore(st);
                    }
                    path.Dispose();
                }
            }

            string png = Path.Combine(outDir, "pieces.png");
            bmp.Save(png, ImageFormat.Png);
            Console.WriteLine("sprite: " + png + "  " + w + "x" + h + "  " +
                new FileInfo(png).Length + " bytes");

            byte[] bytes = File.ReadAllBytes(png);
            string b64 = Convert.ToBase64String(bytes);
            string js = Path.Combine(outDir, "pieces.js");
            File.WriteAllText(js,
                "// 由 tools/MakePieces.cs 生成：6 种棋子 × 黑白两色精灵图（白方在上）\n" +
                "var PIECE_SHEET = 'data:image/png;base64," + b64 + "';\n" +
                "var PIECE_CELL = " + Cell + ";\n");
            Console.WriteLine("inline: " + js + "  " + new FileInfo(js).Length + " bytes");
        }
        return 0;
    }

    /// <summary>把字形转成居中的矢量路径（与桌面版 BoardView.GetPiecePath 一致）。</summary>
    private static GraphicsPath BuildPath(int type, float target)
    {
        GraphicsPath path = new GraphicsPath();
        float em = Cell * 0.9f;
        using (Font f = new Font("Segoe UI Symbol", em, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            StringFormat sf = StringFormat.GenericTypographic;
            sf.FormatFlags |= StringFormatFlags.NoClip;
            path.AddString(GlyphOf(type).ToString(), f.FontFamily, (int)f.Style, em, new PointF(0, 0), sf);
        }
        RectangleF b = path.GetBounds();
        float scale = target / b.Height;
        using (Matrix m = new Matrix())
        {
            m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
            path.Transform(m);
            m.Reset();
            m.Scale(scale, scale);
            path.Transform(m);
        }
        return path;
    }
}
