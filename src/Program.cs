using System;
using System.Threading;
using System.Windows.Forms;
using Chess.UI;

namespace Chess
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // 测试用：--dpi=144 表示按 150% 缩放排版（本机是 96 DPI 时也能核对高分屏下的样子）
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a != null && a.StartsWith("--dpi=", StringComparison.Ordinal))
                    {
                        float v;
                        if (float.TryParse(a.Substring(6), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 96f)
                        {
                            MainForm.ForcedDpiScale = v / 96f;
                        }
                    }
                }
            }

            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                ShowError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                ShowError(e.ExceptionObject as Exception);
            };

            Application.Run(new MainForm());
        }

        private static void ShowError(Exception ex)
        {
            try
            {
                MessageBox.Show("程序出现了一个问题：\n\n" + (ex == null ? "未知错误" : ex.Message),
                    "国际象棋", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
            }
        }
    }
}
