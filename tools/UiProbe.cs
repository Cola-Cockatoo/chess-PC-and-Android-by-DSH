using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Chess;
using Chess.UI;

/// <summary>
/// 开发用诊断工具：验证"真实鼠标点击"在界面上的行为。
/// 重点检查一次鼠标点击只触发一次 Click（历史上这里出过一次缺陷：
/// 基类的 StandardClick 与 FlatButton 自己的 OnClick 各抛一次，
/// 导致"AI 解析"开关点了没反应、悔棋一次退两步）。
///
/// 用法：uiprobe.exe
/// </summary>
public static class UiProbe
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private static int _clicks;
    private static int _failures;

    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        MainForm form = new MainForm();
        form.ClientSize = new Size(940, 700);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Show();
        Application.DoEvents();

        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(600));
        Application.DoEvents();

        GameScreen screen = null;
        foreach (Control c in form.Controls) screen = c as GameScreen;
        if (screen == null) { Console.WriteLine("no GameScreen"); return 1; }

        FlatButton toggle = (FlatButton)Field(screen, "_analysisToggle");
        FlatButton undo = (FlatButton)Field(screen, "_undoBtn");
        FlatButton restart = (FlatButton)Field(screen, "_restartBtn");
        ChessGame game = (ChessGame)Field(screen, "_game");

        Console.WriteLine("=== 一次真实点击触发的 Click 次数（都应为 1）===");
        Console.WriteLine("  悔棋   : " + ClickCount(undo));
        Console.WriteLine("  新局   : " + ClickCount(restart));

        Console.WriteLine();
        Console.WriteLine("=== 开关功能（每次点击都同时统计 Click 次数）===");
        Check(!(bool)Field(screen, "_analysisOn"), "初始为关闭（默认关闭）");
        int toggleClicks = ClickCount(toggle);
        Application.DoEvents();
        Console.WriteLine("  开关   : " + toggleClicks + " 次 Click；_analysisOn=" + (bool)Field(screen, "_analysisOn") +
                          "，窗口宽度 " + form.ClientSize.Width + "，按钮文字 '" + toggle.Text + "'");
        Check(toggleClicks == 1, "开关一次点击只触发 1 次 Click");
        Check((bool)Field(screen, "_analysisOn"), "一次点击即可打开解析");
        Check(form.ClientSize.Width == 1266, "窗口加宽到 1266");

        int toggleClicks2 = ClickCount(toggle);
        Application.DoEvents();
        Console.WriteLine("  开关   : " + toggleClicks2 + " 次 Click；_analysisOn=" + (bool)Field(screen, "_analysisOn") +
                          "，窗口宽度 " + form.ClientSize.Width + "，按钮文字 '" + toggle.Text + "'");
        Check(toggleClicks2 == 1, "开关关闭时也只触发 1 次 Click");
        Check(!(bool)Field(screen, "_analysisOn"), "再点一次即可关闭解析");
        Check(form.ClientSize.Width == 940, "窗口恢复 940");

        Console.WriteLine();
        Console.WriteLine("=== 电脑落子延迟（ELO 600，最小 600ms）===");
        BoardView board = (BoardView)Field(screen, "_board");
        DateTime t0 = DateTime.Now;
        MouseClick(board, 315, 455);   // e2
        MouseClick(board, 315, 315);   // e4
        int spent = 0;
        while (game.MoveHistory.Count < 2 && spent < 8000)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(20);
            spent += 20;
        }
        double ms = (DateTime.Now - t0).TotalMilliseconds;
        Console.WriteLine("  人类走子到电脑落子：" + ms.ToString("0") + " ms（PlyCount=" + game.MoveHistory.Count + "）");
        Check(game.MoveHistory.Count == 2, "电脑已应手");
        Check(ms >= 550, "落子不再瞬间完成");

        Console.WriteLine();
        Console.WriteLine("=== 悔棋一次只退两步（人机对战）===");
        int before2 = game.MoveHistory.Count;
        Click(undo);
        Application.DoEvents();
        Console.WriteLine("  悔棋前 " + before2 + " 手 → 悔棋后 " + game.MoveHistory.Count + " 手");
        Check(before2 - game.MoveHistory.Count == 2, "一次悔棋正好退两步");

        Console.WriteLine();
        Console.WriteLine("=== 升变对话框：真实点击选后 ===");
        using (PromotionDialog dlg = new PromotionDialog(Board.White))
        {
            dlg.Show();
            Application.DoEvents();
            FlatButton queenBtn = null;
            foreach (Control c in dlg.Controls)
            {
                FlatButton fb = c as FlatButton;
                if (fb != null && queenBtn == null) queenBtn = fb;   // 第一个就是"后"
            }
            Check(queenBtn != null, "升变对话框有棋子按钮");
            if (queenBtn != null)
            {
                int n = ClickCount(queenBtn);
                Application.DoEvents();
                Console.WriteLine("  Click 次数=" + n + "，选择结果=" + dlg.Chosen + "（" + PT.Queen + " 表示后）");
                Check(n == 1, "升变按钮一次点击只触发 1 次 Click");
                Check(dlg.Chosen == PT.Queen, "点击后选择为后");
            }
            if (dlg.Visible) dlg.Close();
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "UIPROBE PASSED" : ("UIPROBE FAILED (" + _failures + ")"));
        return _failures == 0 ? 0 : 2;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) _failures++;
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    }

    private static void Click(Control c)
    {
        Point p = new Point(c.Width / 2, c.Height / 2);
        IntPtr lp = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
        SendMessage(c.Handle, WM_MOUSEMOVE, IntPtr.Zero, lp);
        SendMessage(c.Handle, WM_LBUTTONDOWN, (IntPtr)1, lp);
        SendMessage(c.Handle, WM_LBUTTONUP, IntPtr.Zero, lp);
    }

    private static int ClickCount(Control c)
    {
        _clicks = 0;
        EventHandler probe = delegate { _clicks++; };
        c.Click += probe;
        Click(c);
        c.Click -= probe;
        return _clicks;
    }

    private static void MouseClick(Control c, int x, int y)
    {
        MethodInfo mi = typeof(Control).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi != null) mi.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
    }

    private static object Field(object obj, string name)
    {
        FieldInfo fi = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return fi == null ? null : fi.GetValue(obj);
    }
}
