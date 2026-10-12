using System;
using System.Threading;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Settings
{
    /// <summary>
    /// MyIme.Settings.exe 入口。独立进程，改完设置写 settings.json，
    /// 并靠 SettingsStore 里的命名事件通知在跑的 MyIme.Core.exe 立刻重载配置。
    /// 不杀进程、不重启主程序。
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Native.SetProcessDPIAware(); }
            catch { }

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            string page = ParsePage(args);
            Log.Info("设置界面启动（初始页：" + page + "）");

            try
            {
                Application.Run(new SettingsForm(page));
            }
            catch (Exception ex)
            {
                Log.Error("设置界面异常退出", ex);
                ShowError("设置界面出错了：\r\n\r\n" + ex.Message + "\r\n\r\n详细信息已经写进日志文件。");
            }
        }

        /// <summary>支持 --page ai / --page=ai / -p ai。</summary>
        static string ParsePage(string[] args)
        {
            if (args == null) return "general";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;
                string v = null;
                if (a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase)) v = a.Substring(7);
                else if (a.StartsWith("-p=", StringComparison.OrdinalIgnoreCase)) v = a.Substring(3);
                else if ((a == "--page" || a == "-p" || a == "/page") && i + 1 < args.Length) v = args[i + 1];
                if (v == null) continue;
                v = SettingsForm.NormalizePageKey(v);
                if (v != null) return v;
            }
            return "general";
        }

        static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Error("界面线程异常", e == null ? null : e.Exception);
            ShowError("设置界面遇到一个小问题：\r\n\r\n" +
                      (e == null || e.Exception == null ? "未知错误" : e.Exception.Message) +
                      "\r\n\r\n已经有记录写进日志，可以继续操作。");
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.ExceptionObject as Exception;
            Log.Error("未处理的异常", ex);
            ShowError("设置界面遇到严重错误，即将退出：\r\n\r\n" +
                      (ex == null ? "未知错误" : ex.Message) +
                      "\r\n\r\n请把日志文件发给开发者。");
        }

        static void ShowError(string text)
        {
            try
            {
                MessageBox.Show(text, "松林1234输入法 · 设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
