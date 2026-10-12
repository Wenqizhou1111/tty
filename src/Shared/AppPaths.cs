using System;
using System.IO;
using System.Reflection;

namespace MyIme.Shared
{
    /// <summary>所有路径集中在这里，安装/便携两种模式都能跑。</summary>
    public static class AppPaths
    {
        public const string AppName = "MyIME";
        public const string AppNameCn = "松林1234输入法";
        public const string Version = "1.0.0";

        /// <summary>exe 所在目录（数据文件在 .\data 下）。</summary>
        public static string BaseDir
        {
            get
            {
                string d = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(d)) d = Directory.GetCurrentDirectory();
                return d.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        /// <summary>随程序发布的数据目录（词库）。</summary>
        public static string BuiltinDataDir { get { return Path.Combine(BaseDir, "data"); } }

        /// <summary>
        /// 用户数据目录。默认是 %APPDATA%\MyIME；
        /// 如果设了环境变量 MYIME_DATA_DIR，就用它（便携模式 / 测试都靠这个）。
        /// </summary>
        public static string DataDir
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("MYIME_DATA_DIR");
                if (!string.IsNullOrEmpty(custom))
                {
                    EnsureDir(custom);
                    return custom;
                }
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                EnsureDir(dir);
                return dir;
            }
        }

        /// <summary>兼容旧目录名 %APPDATA%\MyIME（大小写不敏感，无需处理）。</summary>
        public static string UserLexiconDir
        {
            get { string d = Path.Combine(DataDir, "user"); EnsureDir(d); return d; }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string UserWordsFile { get { return Path.Combine(DataDir, "user-words.txt"); } }
        public static string HistoryFile { get { return Path.Combine(DataDir, "history.txt"); } }
        public static string CoreExe { get { return Path.Combine(BaseDir, "MyIme.Core.exe"); } }
        public static string SettingsExe { get { return Path.Combine(BaseDir, "MyIme.Settings.exe"); } }
        public static string UninstallExe { get { return Path.Combine(BaseDir, "tty.exe"); } }

        /// <summary>开始菜单快捷方式目录（用户级，免管理员）。</summary>
        public static string StartMenuDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "松林1234输入法");
            }
        }

        public static string StartupLink
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "松林1234输入法.lnk");
            }
        }

        public static void EnsureDir(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { }
        }

        /// <summary>找一个数据文件：先看用户目录，再看随程序目录。</summary>
        public static string ResolveDataFile(string fileName)
        {
            string user = Path.Combine(UserLexiconDir, fileName);
            if (File.Exists(user)) return user;
            string builtin = Path.Combine(BuiltinDataDir, fileName);
            if (File.Exists(builtin)) return builtin;
            string beside = Path.Combine(BaseDir, fileName);
            if (File.Exists(beside)) return beside;
            return null;
        }
    }
}
