using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Chess;
using Chess.UI;

/// <summary>
/// 开发用截图工具（不属于交付程序）：把各个界面离屏渲染成 PNG，便于核对排版与美术效果。
/// 用法：devshot.exe [输出目录]
/// </summary>
public static class DevShot
{
    private static string _outDir;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            string msg = ex.GetType().FullName + ": " + ex.Message + "\n" + ex.StackTrace;
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "devshot_error.txt"), msg); } catch { }
            try { Console.WriteLine("ERROR " + msg); } catch { }
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        _outDir = args.Length > 0 ? args[0] : "shots";
        Directory.CreateDirectory(_outDir);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        MainForm form = new MainForm();
        form.ClientSize = new Size(940, 700);
        form.Show();
        Application.DoEvents();

        Render(form, "01_menu");

        form.ShowDifficulty();
        Render(form, "02_difficulty");

        // 难度页：选中"执黑 · 后行"的样子
        ClickSide(form, "执黑 · 后行");
        Render(form, "02b_difficulty_black_selected");
        ClickSide(form, "执白 · 先行");

        // 人机对战执黑：电脑（白方）先走，棋盘黑方在下方
        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(1200), Chess.Board.Black);
        Thread.Sleep(2600);
        Render(form, "02c_ai_play_black_after_ai_first");
        form.ShowMenu();

        // 人人对战：走几步，检查棋盘翻转（黑方走棋时黑方在下方）
        form.StartGame(GameMode.HumanVsHuman, null);
        GameScreen pvp = (GameScreen)CurrentScreen(form);
        Play(pvp, "e4");
        Render(form, "03_pvp_after1_black_to_move");
        Play(pvp, "e5", "Nf3");
        Render(form, "04_pvp_after3_white_to_move");
        Play(pvp, "Nc6", "Bc4", "Nf6", "Ng5", "d5");
        Render(form, "04b_pvp_after8");

        // 人人对战 + 控制格：此时轮到白方走（白方在下方），黑方在下方时再拍一张，
        // 用来核对"三角永远在格子右上/左上，不随棋盘翻转"
        pvp.ToggleControlSquares();
        Render(form, "04c_pvp_control_white_bottom");
        Play(pvp, "exd5", "c6");            // 走成轮到黑方 → 棋盘翻转成黑方在下方
        Render(form, "04d_pvp_control_black_bottom");
        pvp.ToggleControlSquares();

        // 人机对战（ELO 1200）：人类走 e4，抓"思考中"和电脑应手
        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(1200));
        GameScreen ai = (GameScreen)CurrentScreen(form);
        Play(ai, "e4");
        Thread.Sleep(150);
        Render(form, "05_ai_thinking", false);
        Thread.Sleep(2500);
        Render(form, "06_ai_replied");
        Play(ai, "Nf3", "Nc6", "Bc4");
        Thread.Sleep(2500);
        Render(form, "07_ai_midgame");
        ai.ToggleControlSquares();
        Render(form, "07b_ai_control_on");
        ai.ToggleControlSquares();

        // 结果浮层：愚人杀
        form.StartGame(GameMode.HumanVsHuman, null);
        GameScreen mate = (GameScreen)CurrentScreen(form);
        Play(mate, "f3", "e5", "g4", "Qh4#");
        Render(form, "08_checkmate");

        // AI 解析（默认关闭 → 打开后显示胜率曲线、每手评价与右下角符号说明）
        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(1200));
        GameScreen an = (GameScreen)CurrentScreen(form);
        Render(form, "10_analysis_off");
        string[] humanMoves = new string[] { "e4", "Nf3", "Bc4", "d3", "Nc3", "Be3" };
        for (int i = 0; i < humanMoves.Length; i++)
        {
            if (!PlayQuiet(an, humanMoves[i])) continue;
            WaitPly(an, (i + 1) * 2, 8000);
        }
        SetAnalysis(an, true);
        Pump(1500);
        Render(form, "11_analysis_running");
        Pump(14000);
        Render(form, "12_analysis_done");

        // 升变对话框
        using (PromotionDialog dlg = new PromotionDialog(Board.White))
        {
            dlg.Show();
            Application.DoEvents();
            Shoot(dlg, "09_promotion");
            dlg.Hide();
        }

        // ---------------- 全屏（按真实屏幕分辨率渲染）----------------
        form.SetFullscreen(true);          // 无边框铺满屏幕
        Pump(400);
        Console.WriteLine("  fullscreen client = " + form.ClientSize.Width + "x" + form.ClientSize.Height +
                          "，UiScale=" + UiScale.Factor.ToString("0.00"));
        form.ShowMenu();
        Render(form, "13_fullscreen_menu");
        form.ShowDifficulty();
        Render(form, "14_fullscreen_difficulty");

        form.StartGame(GameMode.HumanVsHuman, null);
        GameScreen fsPvp = (GameScreen)CurrentScreen(form);
        Play(fsPvp, "e4", "e5", "Nf3", "Nc6", "Bc4", "Bf5", "d3", "d6");
        Render(form, "15_fullscreen_pvp");

        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(1400));
        GameScreen fsAi = (GameScreen)CurrentScreen(form);
        Render(form, "16_fullscreen_no_analysis");
        string[] fsMoves = new string[] { "d4", "c4", "Nc3", "Nf3" };
        for (int i = 0; i < fsMoves.Length; i++)
        {
            if (!PlayQuiet(fsAi, fsMoves[i])) continue;
            WaitPly(fsAi, (i + 1) * 2, 8000);
        }
        Console.WriteLine("  开启解析前: IsFullscreen=" + form.IsFullscreen + " border=" + form.FormBorderStyle +
                          " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height);
        SetAnalysis(fsAi, true);           // 全屏时不应改变窗口尺寸
        Console.WriteLine("  开启解析后: IsFullscreen=" + form.IsFullscreen + " border=" + form.FormBorderStyle +
                          " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height);
        Pump(14000);
        Console.WriteLine("  开启解析后 fullscreen client = " + form.ClientSize.Width + "x" + form.ClientSize.Height +
                          "，UiScale=" + UiScale.Factor.ToString("0.00"));
        Render(form, "17_fullscreen_analysis");

        // 退出全屏，再看一个宽屏比例
        form.SetFullscreen(false);
        Pump(300);
        form.ClientSize = new Size(1920, 1080);
        Pump(300);
        form.StartGame(GameMode.HumanVsHuman, null);
        GameScreen wide = (GameScreen)CurrentScreen(form);
        Play(wide, "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "b4", "Bxb4", "c3", "Ba5");
        Render(form, "18_wide_1920x1080");

        // 小屏兼容性：窗口被缩放后界面等比缩小（验证可缩放窗口在小屏上能否完整显示）
        form.SetFullscreen(false);
        Pump(300);
        form.ClientSize = new Size(1024, 600);
        Pump(300);
        form.StartGame(GameMode.HumanVsAi, SkillTable.ForElo(1200));
        GameScreen small1 = (GameScreen)CurrentScreen(form);
        Play(small1, "e4", "e5", "Nf3", "Nc6");
        Render(form, "19_small_1024x600");
        form.ClientSize = new Size(800, 560);
        Pump(400);
        Render(form, "20_small_800x560");

        // 150% 缩放下的原生渲染：窗口 1410×1050，界面按 1.5 倍排版（字由 GDI+ 直接画成 1.5 倍大小，不是被拉伸）
        form.ClientSize = new Size(1410, 1050);
        Pump(300);
        form.ShowDifficulty();
        Pump(300);
        Render(form, "22_difficulty_native_150pct");

        // 定式学习：列表 → 学习界面（箭头）→ 走几步 → 分支处两个箭头
        form.ClientSize = new Size(940, 700);
        Pump(200);
        form.ShowOpeningList();
        Render(form, "26_opening_list");

        form.StartStudy(Openings.ById("italian"));
        Pump(300);
        Render(form, "27_study_first_arrow");

        OpeningStudyScreen st = (OpeningStudyScreen)CurrentScreen(form);
        for (int i = 0; i < 7; i++)
        {
            if (!st.PlayFirstCandidate()) break;
            int guard = 0;
            while (st.IsWaitingReply && guard++ < 40) { Application.DoEvents(); Thread.Sleep(50); }
            if (i == 3) Render(form, "28_study_midway");
        }
        Render(form, "28b_study_progress");

        // 英国式：1.c4 之后程序应 e5，出现两个箭头（学员可选）
        form.StartStudy(Openings.ById("english"));
        Pump(300);
        OpeningStudyScreen en = (OpeningStudyScreen)CurrentScreen(form);
        en.PlayFirstCandidate();
        int g2 = 0;
        while (en.IsWaitingReply && g2++ < 40) { Application.DoEvents(); Thread.Sleep(50); }
        Pump(200);
        Render(form, "29_study_branch_two_arrows");

        // 开局讲解视图（难度 / 典型计划 / 常见错误 / 主要变化）
        form.StartStudy(Openings.ById("sicilian"));
        Pump(300);
        OpeningStudyScreen gs = (OpeningStudyScreen)CurrentScreen(form);
        gs.ToggleGuide();
        Pump(200);
        Render(form, "30_study_guide");

        // 本步点评三段式：我走 e4 → 对手应 e5 → 面板显示"对手上一步/你上一步/该你走为什么"
        form.StartStudy(Openings.ById("italian"));
        Pump(300);
        OpeningStudyScreen pv = (OpeningStudyScreen)CurrentScreen(form);
        pv.PlayFirstCandidate();
        int g3 = 0;
        while (pv.IsWaitingReply && g3++ < 40) { Application.DoEvents(); Thread.Sleep(40); }
        Pump(200);
        Console.WriteLine("  三段点评：opp=[" + pv.OpponentLast + "] why=" + pv.WhyCount);
        Render(form, "33_panel_three_sections");

        // 复盘：造一盘含臭棋的棋（3.Qxf7+ 白丢后），看复盘界面与"该你重下"
        {
            ChessGame rg = new ChessGame();
            string[] rline = { "e4", "e5", "Qh5", "Nc6", "Qxf7+", "Kxf7" };
            foreach (string san in rline)
            {
                List<Chess.Move> lg = rg.LegalMoves();
                Chess.Move mv = Chess.Move.Null;
                for (int i = 0; i < lg.Count; i++) if (rg.Position.San(lg[i]) == san) { mv = lg[i]; break; }
                if (mv.IsNull) break;
                rg.Play(mv, san);
            }
            form.StartReview(rg, GameMode.HumanVsAi, Board.White);
            Pump(400);
            ReviewScreen rs = (ReviewScreen)CurrentScreen(form);
            rs.WaitForReady(8000);
            Pump(400);
            Render(form, "35_review_start");
            rs.DriveToStop();
            Pump(400);
            Render(form, "36_review_quiz");
            rs.PressJump(-1);
            Pump(300);
            Render(form, "37_review_jumpback");
            form.ShowMenu();
        }

        // 意大利开局走到 3...Bc5 之后：白方第 4 手有 c3 / d3 两条变化，箭头与变化名都要标出来
        form.StartStudy(Openings.ById("italian"));
        Pump(300);
        OpeningStudyScreen it2 = (OpeningStudyScreen)CurrentScreen(form);
        for (int i = 0; i < 3; i++)   // 走到 3...Bc5，白方第 4 手就是岔口（c3 / d3）
        {
            if (!it2.PlayFirstCandidate()) break;
            int guard = 0;
            while (it2.IsWaitingReply && guard++ < 40) { Application.DoEvents(); Thread.Sleep(40); }
        }
        Pump(200);
        Console.WriteLine("  意大利岔口箭头数 = " + it2.HintCount + "（应为 2）");
        Render(form, "32_study_branch_names");

        // 点评变长后用滚轮下滚（截图看滚动条与内容）
        form.StartStudy(Openings.ById("italian"));
        Pump(300);
        OpeningStudyScreen sc2 = (OpeningStudyScreen)CurrentScreen(form);
        for (int i = 0; i < 5; i++)
        {
            if (!sc2.PlayFirstCandidate()) break;
            int g4 = 0;
            while (sc2.IsWaitingReply && g4++ < 40) { Application.DoEvents(); Thread.Sleep(40); }
        }
        Pump(200);
        Console.WriteLine("  点评可滚动 " + sc2.PanelMaxScroll + " 像素");
        for (int i = 0; i < 4; i++) sc2.ScrollPanel(-120);
        Pump(200);
        Console.WriteLine("  已滚到 " + sc2.PanelScroll + " 像素");
        Render(form, "34_panel_scrolled");

        // 新开局的列表（鼠标悬停提示也带难度）
        form.ShowOpeningList();
        Pump(200);
        Render(form, "31_opening_list_full");
        form.ShowMenu();

        form.Hide();
        Console.WriteLine("done -> " + Path.GetFullPath(_outDir));
        return 0;
    }

    private static void Render(MainForm form, string name)
    {
        NormalizeWindow(form);
        Render(form, name, true);
    }

    /// <summary>窗口被系统最小化时客户区会变成 0×0，截图前先恢复。</summary>
    private static void NormalizeWindow(MainForm form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
            Application.DoEvents();
            Thread.Sleep(200);
        }
        if (form.ClientSize.Width <= 0 && form.IsFullscreen)
        {
            form.Bounds = System.Windows.Forms.Screen.FromControl(form).Bounds;
            Application.DoEvents();
            Thread.Sleep(200);
        }
    }

    private static void Render(MainForm form, string name, bool pumpMessages)
    {
        Control screen = CurrentScreen(form);
        Console.WriteLine("  screen " + (screen == null ? "<none>" : screen.GetType().Name + " " + screen.Width + "x" + screen.Height));
        Shoot(screen == null ? (Control)form : screen, name, pumpMessages);
    }

    private static Control CurrentScreen(MainForm form)
    {
        foreach (Control c in form.Controls)
        {
            return c;
        }
        return null;
    }

    private static void Play(GameScreen screen, params string[] sans)
    {
        foreach (string san in sans)
        {
            if (!PlayQuiet(screen, san)) return;
        }
    }

    private static bool PlayQuiet(GameScreen screen, string san)
    {
        FieldInfo fi = typeof(GameScreen).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance);
        ChessGame game = (ChessGame)fi.GetValue(screen);
        MethodInfo apply = typeof(GameScreen).GetMethod("ApplyMove", BindingFlags.NonPublic | BindingFlags.Instance);
        List<Move> legal = game.LegalMoves();
        Move found = Move.Null;
        foreach (Move m in legal)
        {
            if (game.Position.San(m) == san) { found = m; break; }
        }
        if (found.IsNull)
        {
            Console.WriteLine("!! move not found: " + san + " (fen " + game.Position.ToFen() + ")");
            return false;
        }
        apply.Invoke(screen, new object[] { found });
        return true;
    }

    /// <summary>反复派发消息，让后台线程的结果回到界面上。</summary>
    private static void Pump(int ms)
    {
        int spent = 0;
        while (spent < ms)
        {
            Application.DoEvents();
            Thread.Sleep(25);
            spent += 25;
        }
    }

    private static int PlyOf(GameScreen screen)
    {
        FieldInfo fi = typeof(GameScreen).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance);
        ChessGame game = (ChessGame)fi.GetValue(screen);
        return game.MoveHistory.Count;
    }

    private static void WaitPly(GameScreen screen, int ply, int timeoutMs)
    {
        int spent = 0;
        while (spent < timeoutMs)
        {
            Application.DoEvents();
            if (PlyOf(screen) >= ply) return;
            Thread.Sleep(50);
            spent += 50;
        }
    }

    private static void SetAnalysis(GameScreen screen, bool on)
    {
        FieldInfo fi = typeof(GameScreen).GetField("_analysisOn", BindingFlags.NonPublic | BindingFlags.Instance);
        bool current = (bool)fi.GetValue(screen);
        if (current == on) return;
        MethodInfo toggle = typeof(GameScreen).GetMethod("ToggleAnalysis", BindingFlags.NonPublic | BindingFlags.Instance);
        toggle.Invoke(screen, null);
    }

    /// <summary>在难度页按文字点一下"执白/执黑"按钮。</summary>
    private static void ClickSide(MainForm form, string text)
    {
        foreach (Control c in CurrentScreen(form).Controls)
        {
            FlatButton b = c as FlatButton;
            if (b != null && b.Text == text)
            {
                MethodInfo click = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
                click.Invoke(b, new object[] { EventArgs.Empty });
                Application.DoEvents();
                return;
            }
        }
        throw new Exception("难度页找不到按钮：" + text);
    }

    private static void Shoot(Control c, string name)
    {
        Shoot(c, name, true);
    }

    private static void Shoot(Control c, string name, bool pumpMessages)
    {
        IntPtr h = c.Handle;
        c.PerformLayout();
        if (pumpMessages) Application.DoEvents();

        using (Bitmap bmp = new Bitmap(Math.Max(1, c.ClientSize.Width), Math.Max(1, c.ClientSize.Height)))
        {
            c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            string path = Path.Combine(_outDir, name + ".png");
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("shot " + path + "  " + bmp.Width + "x" + bmp.Height);
        }
    }
}
