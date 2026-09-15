using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Setup
{
    /// <summary>命令行参数。</summary>
    public class SetupArgs
    {
        public bool Help = false;
        public bool Uninstall = false;
        public bool Silent = false;
        public string Dir = null;
        public bool Desktop = false;
        public bool NoStartMenu = false;
        public bool NoAutoStart = false;
        public bool NoLaunch = false;
        public bool AllUsers = false;
        public bool RemoveUserData = false;

        public static SetupArgs Parse(string[] args)
        {
            SetupArgs a = new SetupArgs();
            if (args == null) return a;

            for (int i = 0; i < args.Length; i++)
            {
                string s = (args[i] == null) ? "" : args[i].Trim();
                if (s.Length == 0) continue;
                string low = s.ToLowerInvariant();

                if (low == "--help" || low == "-h" || low == "-?" || low == "/?" || low == "/help")
                {
                    a.Help = true;
                    continue;
                }
                if (low == "--uninstall" || low == "/uninstall")
                {
                    a.Uninstall = true;
                    continue;
                }
                if (low == "--silent-install" || low == "/silent-install")
                {
                    a.Silent = true;
                    a.Uninstall = false;
                    continue;
                }
                if (low == "--silent-uninstall" || low == "/silent-uninstall")
                {
                    a.Silent = true;
                    a.Uninstall = true;
                    continue;
                }
                if (low == "--dir" || low == "/dir" || low == "-d")
                {
                    if (i + 1 < args.Length)
                    {
                        i++;
                        a.Dir = args[i];
                    }
                    else
                    {
                        Log.Warn("--dir 后面没有跟路径");
                    }
                    continue;
                }
                if (low.StartsWith("--dir="))
                {
                    a.Dir = s.Substring(6);
                    continue;
                }
                if (low == "--all-users" || low == "/all-users")
                {
                    a.AllUsers = true;
                    continue;
                }
                if (low == "--desktop" || low == "/desktop")
                {
                    a.Desktop = true;
                    continue;
                }
                if (low == "--no-startmenu" || low == "/no-startmenu")
                {
                    a.NoStartMenu = true;
                    continue;
                }
                if (low == "--no-autostart" || low == "/no-autostart")
                {
                    a.NoAutoStart = true;
                    continue;
                }
                if (low == "--no-launch" || low == "/no-launch")
                {
                    a.NoLaunch = true;
                    continue;
                }
                if (low == "--remove-user-data" || low == "/remove-user-data")
                {
                    a.RemoveUserData = true;
                    continue;
                }
                Log.Warn("忽略无法识别的参数: " + s);
            }
            return a;
        }

        public InstallOptions ToOptions()
        {
            InstallOptions o = new InstallOptions();
            o.TargetDir = Dir;
            o.DesktopShortcut = Desktop;
            o.StartMenuShortcut = !NoStartMenu;
            o.AutoStart = !NoAutoStart;
            o.LaunchAfter = !NoLaunch;
            o.AllUsers = AllUsers;
            o.RemoveUserData = RemoveUserData;
            return o;
        }
    }

    /// <summary>tty.exe 入口。</summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try { Application.EnableVisualStyles(); }
            catch { }
            try { Application.SetCompatibleTextRenderingDefault(false); }
            catch { }

            Application.ThreadException += new ThreadExceptionEventHandler(OnThreadException);
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); }
            catch { }
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnDomainException);

            SetupArgs a;
            try
            {
                a = SetupArgs.Parse(args);
            }
            catch (Exception ex)
            {
                Log.Error("解析命令行参数失败", ex);
                a = new SetupArgs();
            }

            try
            {
                if (a.Help)
                {
                    ShowHelp();
                    return 0;
                }
                if (a.Silent)
                {
                    return RunSilent(a);
                }

                using (SetupForm form = new SetupForm(a))
                {
                    Application.Run(form);
                    Log.Info("界面退出，返回码 " + form.ExitCode);
                    return form.ExitCode;
                }
            }
            catch (Exception ex)
            {
                return Fail(a, ex);
            }
        }

        // ==================== 静默模式 ====================

        static int RunSilent(SetupArgs a)
        {
            InstallOptions opt = a.ToOptions();
            Action<int, string> progress = new Action<int, string>(SilentProgress);

            if (a.Uninstall)
            {
                if (string.IsNullOrEmpty(opt.TargetDir)) opt.TargetDir = Installer.InstalledDir;
                Log.Info("静默卸载开始" + (string.IsNullOrEmpty(opt.TargetDir) ? "" : "：" + opt.TargetDir));
                Installer.Uninstall(opt, progress);
                Log.Info("静默卸载完成");
            }
            else
            {
                if (string.IsNullOrEmpty(opt.TargetDir)) opt.TargetDir = Installer.DefaultTargetDir(opt.AllUsers);
                Log.Info("静默安装开始：" + opt.TargetDir);
                Installer.Install(opt, progress);
                Log.Info("静默安装完成：" + opt.TargetDir);
            }
            ReportWarnings();
            return 0;
        }

        /// <summary>
        /// 把"不影响使用、但你可能想知道"的问题打到 stdout 和日志里。
        /// 这些不是失败，退出码仍是 0 —— 程序文件已经装好了。
        /// </summary>
        static void ReportWarnings()
        {
            if (Installer.LastWarnings.Count == 0) return;
            try
            {
                Console.WriteLine();
                Console.WriteLine("以下步骤没有完成（不影响输入法使用，程序文件已就位）：");
                foreach (string w in Installer.LastWarnings) Console.WriteLine("  - " + w);
                Console.WriteLine("详细信息见日志：%APPDATA%\\MyIME\\logs\\");
            }
            catch { }
            foreach (string w in Installer.LastWarnings) Log.Warn("安装警告: " + w);
        }

        static void SilentProgress(int pct, string msg)
        {
            // 静默模式不弹任何窗口，只写日志（%APPDATA%\MyIME\logs）
            Log.Info("[" + pct + "%] " + msg);
        }

        // ==================== 帮助 ====================

        public static string UsageText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("墨言输入法 安装程序 (MyIME Setup)  版本 " + Installer.GetVersion());
            sb.AppendLine();
            sb.AppendLine("用法:");
            sb.AppendLine("  tty.exe [选项]");
            sb.AppendLine();
            sb.AppendLine("  不带任何选项直接双击运行，会打开图形化安装向导。");
            sb.AppendLine("  默认是「当前用户安装」，装到 %LOCALAPPDATA%\\Programs\\墨言输入法，不需要管理员权限。");
            sb.AppendLine();
            sb.AppendLine("选项:");
            sb.AppendLine("  --uninstall            打开图形化卸载向导");
            sb.AppendLine("  --silent-install       静默安装（默认当前用户，不需要管理员权限）");
            sb.AppendLine("  --silent-uninstall     静默卸载（默认保留设置和用户词库）");
            sb.AppendLine("  --dir <路径>           指定安装目录（图形界面会作为默认值，静默模式则直接用它）");
            sb.AppendLine("  --all-users            为所有用户安装（需要管理员权限，装到 %ProgramFiles%\\墨言输入法）");
            sb.AppendLine("  --desktop              创建桌面快捷方式（默认不创建）");
            sb.AppendLine("  --no-startmenu         不创建开始菜单快捷方式（默认创建）");
            sb.AppendLine("  --no-autostart         不设置开机自动启动（默认设置）");
            sb.AppendLine("  --no-launch            安装完成后不自动启动墨言输入法（默认启动）");
            sb.AppendLine("  --remove-user-data     卸载时同时删除设置和用户词库（%APPDATA%\\MyIME，不可恢复）");
            sb.AppendLine("  --help, -h, /?         显示本帮助");
            sb.AppendLine();
            sb.AppendLine("示例:");
            sb.AppendLine("  tty.exe");
            sb.AppendLine("  tty.exe --silent-install");
            sb.AppendLine("  tty.exe --silent-install --dir \"D:\\Apps\\墨言输入法\" --desktop");
            sb.AppendLine("  tty.exe --silent-install --no-autostart --no-launch");
            sb.AppendLine("  tty.exe --uninstall");
            sb.AppendLine("  tty.exe --silent-uninstall");
            sb.AppendLine("  tty.exe --silent-uninstall --remove-user-data");
            sb.AppendLine();
            sb.AppendLine("默认安装目录:");
            sb.AppendLine("  当前用户安装 : %LOCALAPPDATA%\\Programs\\墨言输入法   （默认，免管理员）");
            sb.AppendLine("  所有用户安装 : %ProgramFiles%\\墨言输入法           （需要管理员权限）");
            sb.AppendLine();
            sb.AppendLine("返回码: 0 = 成功, 1 = 失败");
            sb.AppendLine("日志:   %APPDATA%\\MyIME\\logs\\myime-yyyyMMdd.log");
            sb.Append("用户数据: %APPDATA%\\MyIME（设置和用户词库，卸载时默认保留）");
            return sb.ToString();
        }

        static void ShowHelp()
        {
            string usage = UsageText();
            try { Console.WriteLine(usage); }
            catch { }
            Log.Info("显示命令行用法帮助");

            bool redirected = false;
            try { redirected = Console.IsOutputRedirected; }
            catch { redirected = false; }
            if (redirected) return;   // 被重定向时只输出文本，不弹窗

            try
            {
                MessageBox.Show(usage, "墨言输入法 安装程序 - 用法",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        // ==================== 异常处理 ====================

        static int Fail(SetupArgs a, Exception ex)
        {
            Log.Error("安装程序发生异常", ex);
            string text = "安装程序发生错误：\r\n\r\n" + (ex == null ? "未知错误" : ex.Message) +
                          "\r\n\r\n详细信息已经写入日志：\r\n" + Log.CurrentPath;
            if (a == null || !a.Silent)
            {
                try
                {
                    MessageBox.Show(text, "墨言输入法 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
            return 1;
        }

        static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Exception ex = (e == null) ? null : e.Exception;
            Log.Error("界面线程异常", ex);
            try
            {
                MessageBox.Show("安装程序发生错误：\r\n\r\n" + (ex == null ? "未知错误" : ex.Message) +
                                "\r\n\r\n详细信息已经写入日志：\r\n" + Log.CurrentPath,
                    "墨言输入法 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Log.Error("未处理异常: " + (e == null ? "" : Convert.ToString(e.ExceptionObject))); }
            catch { }
        }
    }
}
