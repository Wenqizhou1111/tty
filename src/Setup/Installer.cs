using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using MyIme.Shared;

namespace MyIme.Setup
{
    /// <summary>安装/卸载选项。全部用 public 字段，方便外部直接填。</summary>
    public class InstallOptions
    {
        public string TargetDir;
        public bool DesktopShortcut = false;
        public bool StartMenuShortcut = true;
        public bool AutoStart = true;
        public bool LaunchAfter = true;
        public bool AllUsers = false;
        public bool RemoveUserData = false;   // 卸载时用

        public InstallOptions Clone()
        {
            InstallOptions o = new InstallOptions();
            o.TargetDir = TargetDir;
            o.DesktopShortcut = DesktopShortcut;
            o.StartMenuShortcut = StartMenuShortcut;
            o.AutoStart = AutoStart;
            o.LaunchAfter = LaunchAfter;
            o.AllUsers = AllUsers;
            o.RemoveUserData = RemoveUserData;
            return o;
        }
    }

    /// <summary>
    /// 真正的安装 / 卸载逻辑。图形界面和静默模式都走这里，不放在 Form 里。
    /// 默认是"当前用户安装"，全程不碰需要管理员权限的位置。
    /// </summary>
    public class Installer
    {
        // ==================== 常量 ====================

        public const string ProductNameCn = "代龙";
        public const string ProductNameEn = "MyIME";
        public const string Publisher = "代龙 (MyIME)";

        public const string CoreExeName = "MyIme.Core.exe";
        public const string SettingsExeName = "MyIme.Settings.exe";
        public const string SetupExeName = "tty.exe";
        public const string SharedDllName = "MyIme.Shared.dll";

        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MyIME";
        public const string AppKeyPath = @"Software\MyIME";
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "MyIME";

        public const string StartMenuFolderName = "代龙";
        public const string MainShortcutName = "代龙.lnk";
        public const string SettingsShortcutName = "设置.lnk";
        public const string UninstallShortcutName = "卸载.lnk";

        /// <summary>要结束的进程名（不含 .exe）。</summary>
        public static readonly string[] RunningProcessNames = new string[] { "MyIme.Core", "MyIme.Settings" };

        /// <summary>桌面上可能存在的快捷方式名。</summary>
        public static readonly string[] DesktopShortcutNames = new string[]
        {
            "代龙.lnk", "代龙 设置.lnk", "MyIME.lnk"
        };

        /// <summary>安装包里必须有的文件（缺了只警告，不阻断）。</summary>
        static readonly string[] RequiredPayloadFiles = new string[] { CoreExeName, SetupExeName };

        // ==================== 基本信息 ====================

        /// <summary>产品版本（优先用程序集版本，没写就退回 AppPaths.Version）。</summary>
        public static string GetVersion()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                object[] info = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (info != null && info.Length > 0)
                {
                    string s = ((AssemblyInformationalVersionAttribute)info[0]).InformationalVersion;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                Version v = asm.GetName().Version;
                if (v != null && v.Major > 0) return v.ToString();
            }
            catch { }
            return AppPaths.Version;
        }

        /// <summary>默认安装目录（当前用户 → %LOCALAPPDATA%\Programs\代龙）。</summary>
        public static string DefaultTargetDir(bool allUsers)
        {
            if (allUsers)
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetEnvironmentVariable("ProgramFiles");
                if (string.IsNullOrEmpty(pf)) pf = @"C:\Program Files";
                return Path.Combine(pf, ProductNameCn);
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
            {
                local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local");
            }
            return Path.Combine(Path.Combine(local, "Programs"), ProductNameCn);
        }

        /// <summary>当前进程是否已提权（普通用户双击安装时是 false）。</summary>
        public static bool IsAdmin
        {
            get
            {
                try
                {
                    WindowsIdentity id = WindowsIdentity.GetCurrent();
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        /// <summary>注册表里记录的安装目录（HKCU 优先，其次 HKLM）。</summary>
        public static string InstalledDir
        {
            get
            {
                string d = ReadRegString(RegistryHive.CurrentUser, AppKeyPath, "InstallDir");
                if (IsBlank(d)) d = ReadRegString(RegistryHive.LocalMachine, AppKeyPath, "InstallDir");
                if (IsBlank(d)) d = ReadRegString(RegistryHive.CurrentUser, UninstallKeyPath, "InstallLocation");
                if (IsBlank(d)) d = ReadRegString(RegistryHive.LocalMachine, UninstallKeyPath, "InstallLocation");
                return d;
            }
        }

        /// <summary>注册表里记录的版本号。</summary>
        public static string InstalledVersion
        {
            get
            {
                string v = ReadRegString(RegistryHive.CurrentUser, AppKeyPath, "Version");
                if (IsBlank(v)) v = ReadRegString(RegistryHive.LocalMachine, AppKeyPath, "Version");
                if (IsBlank(v)) v = ReadRegString(RegistryHive.CurrentUser, UninstallKeyPath, "DisplayVersion");
                if (IsBlank(v)) v = ReadRegString(RegistryHive.LocalMachine, UninstallKeyPath, "DisplayVersion");
                return v;
            }
        }

        /// <summary>是否已经装过（目录真的存在才算）。</summary>
        public static bool IsInstalled
        {
            get
            {
                string d = InstalledDir;
                if (IsBlank(d)) return false;
                if (!Directory.Exists(d)) return false;
                return File.Exists(Path.Combine(d, CoreExeName)) || File.Exists(Path.Combine(d, SetupExeName));
            }
        }

        /// <summary>安装包内容所在目录（tty.exe 自己所在的目录）。</summary>
        public static string GetPayloadDir()
        {
            string dir = null;
            try { dir = AppDomain.CurrentDomain.BaseDirectory; } catch { }
            if (IsBlank(dir))
            {
                try { dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } catch { }
            }
            if (IsBlank(dir))
            {
                try { dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName); } catch { }
            }
            if (IsBlank(dir)) dir = Directory.GetCurrentDirectory();
            return TrimDir(dir);
        }

        /// <summary>当前 exe 的完整路径。</summary>
        public static string CurrentExePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch { }
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { }
            return Path.Combine(GetPayloadDir(), SetupExeName);
        }

        /// <summary>开始菜单里的「代龙」文件夹。</summary>
        public static string StartMenuFolderPath()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (IsBlank(programs))
            {
                programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs");
            }
            return Path.Combine(programs, StartMenuFolderName);
        }

        // ==================== 结束进程 ====================

        /// <summary>结束正在运行的 MyIme.Core.exe / MyIme.Settings.exe。</summary>
        public static void KillRunning()
        {
            int selfId = -1;
            try { selfId = Process.GetCurrentProcess().Id; } catch { }

            foreach (string name in RunningProcessNames)
            {
                Process[] list = null;
                try { list = Process.GetProcessesByName(name); }
                catch (Exception ex) { Log.Warn("枚举进程失败 " + name + ": " + ex.Message); }
                if (list == null) continue;

                foreach (Process p in list)
                {
                    try
                    {
                        if (selfId > 0 && p.Id == selfId) continue;
                        Log.Info("结束进程 " + name + " (PID " + p.Id + ")");
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("结束进程失败 " + name + ": " + ex.Message);
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            Thread.Sleep(300);
        }

        // ==================== 安装 ====================

        /// <summary>
        /// 上一次安装/卸载里"不影响主流程、但应该告诉用户"的问题。
        /// 比如注册表被组策略挡住、快捷方式建不了 —— 程序文件其实已经就位，
        /// 这种情况必须报成功 + 警告，不能报失败（报失败用户会去卸载重来，白折腾）。
        /// </summary>
        public static readonly List<string> LastWarnings = new List<string>();

        static void WarnStep(string what, Exception ex)
        {
            string msg = what + "失败：" + ex.Message + "  （不影响输入法使用，程序文件已就位）";
            LastWarnings.Add(msg);
            Log.Warn(msg);
        }

        public static void Install(InstallOptions opt, Action<int, string> progress)
        {
            if (opt == null) opt = new InstallOptions();
            string target = opt.TargetDir;
            if (IsBlank(target)) target = DefaultTargetDir(opt.AllUsers);
            target = NormalizeDir(target);
            opt.TargetDir = target;

            if (opt.AllUsers && !IsAdmin)
            {
                throw new InvalidOperationException("为所有用户安装需要管理员权限。请右键选择「以管理员身份运行」，或改用默认的当前用户安装（不需要管理员权限）。");
            }

            LastWarnings.Clear();
            Log.Info("开始安装 " + ProductNameCn + " " + GetVersion() + " -> " + target +
                     " (AllUsers=" + opt.AllUsers + ", Desktop=" + opt.DesktopShortcut +
                     ", StartMenu=" + opt.StartMenuShortcut + ", AutoStart=" + opt.AutoStart + ")");

            Report(progress, 3, "正在结束正在运行的" + ProductNameCn + "…");
            KillRunning();

            Report(progress, 8, "正在准备安装目录…");
            EnsureDirectory(target);
            EnsureWritable(target);

            string payload = GetPayloadDir();
            Report(progress, 12, "正在复制程序文件…");
            CopyResult copied = CopyPayload(payload, target, progress, 12, 62);

            // 从这里往下都是"锦上添花"的步骤：卸载信息、快捷方式、开机自启、启动程序。
            // 它们失败**不应该**让整个安装报失败 —— 程序文件这时候已经装好了，
            // 报"失败"会让用户以为白装了，然后去卸载重来。
            // 所以统一降级成警告，收进 LastWarnings 给界面和静默模式报告。
            Report(progress, 68, "正在写入卸载信息…");
            try { WriteUninstallInfo(opt, target); }
            catch (Exception ex) { WarnStep("写入卸载信息", ex); }

            Report(progress, 78, "正在创建快捷方式…");
            try { CreateShortcuts(opt); }
            catch (Exception ex) { WarnStep("创建快捷方式", ex); }

            Report(progress, 86, opt.AutoStart ? "正在设置开机自动启动…" : "正在清理开机自动启动…");
            try { SetAutoStart(opt.AutoStart, Path.Combine(target, CoreExeName)); }
            catch (Exception ex) { WarnStep("设置开机自动启动", ex); }

            Report(progress, 92, "正在记录安装位置…");
            try { WriteAppKey(target); }
            catch (Exception ex) { WarnStep("记录安装位置", ex); }

            Report(progress, 96, "正在完成安装…");
            if (opt.LaunchAfter)
            {
                try { LaunchProgram(Path.Combine(target, CoreExeName), target); }
                catch (Exception ex) { WarnStep("启动" + ProductNameCn, ex); }
            }

            Report(progress, 100, "安装完成");
            Log.Info("安装完成: " + target + "  文件 " + copied.Files + " 个");
        }

        /// <summary>把安装包目录里的全部内容递归复制到目标目录。</summary>
        static CopyResult CopyPayload(string srcDir, string dstDir, Action<int, string> progress, int fromPct, int toPct)
        {
            CopyResult res = new CopyResult();
            if (IsBlank(srcDir) || !Directory.Exists(srcDir))
            {
                throw new InvalidOperationException("找不到安装包内容目录：" + srcDir);
            }
            if (SamePath(srcDir, dstDir))
            {
                Log.Warn("安装包目录与目标目录相同，跳过复制：" + srcDir);
                return res;
            }

            List<string> files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                Log.Warn("枚举安装包文件出错: " + ex.Message);
                try { files.AddRange(Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly)); }
                catch { }
            }

            // 目标目录恰好嵌在安装包目录里时，别把自己复制进去
            List<string> real = new List<string>();
            foreach (string f in files)
            {
                if (IsUnder(f, dstDir)) continue;
                real.Add(f);
            }

            if (real.Count == 0)
            {
                Log.Warn("安装包里没有可复制的文件：" + srcDir);
            }

            int lastPct = -1;
            for (int i = 0; i < real.Count; i++)
            {
                string src = real[i];
                string rel = src.Substring(srcDir.Length).TrimStart(new char[] { '\\', '/' });
                string dst = Path.Combine(dstDir, rel);
                CopyOne(src, dst);
                res.Files++;

                if (progress != null && real.Count > 0)
                {
                    int pct = fromPct + (int)((long)(toPct - fromPct) * (i + 1) / real.Count);
                    if (pct != lastPct && (i + 1 == real.Count || i % 4 == 0))
                    {
                        lastPct = pct;
                        Report(progress, pct, "正在复制 " + Path.GetFileName(src) + "…");
                    }
                }
            }

            // 关键文件检查（只警告，不阻断，方便只拿一个 setup.exe 做测试）
            foreach (string rf in RequiredPayloadFiles)
            {
                if (!File.Exists(Path.Combine(dstDir, rf)))
                {
                    string msg = "安装包不完整：没有找到 " + rf + "（安装包目录 " + srcDir + "）";
                    Log.Warn(msg);
                    res.Warnings.Add(msg);
                }
            }
            return res;
        }

        static void CopyOne(string src, string dst)
        {
            string dir = Path.GetDirectoryName(dst);
            EnsureDirectory(dir);

            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(dst))
                    {
                        FileAttributes attrs = File.GetAttributes(dst);
                        if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(dst, attrs & ~FileAttributes.ReadOnly);
                    }
                    File.Copy(src, dst, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(150);
                }
            }
            throw new InvalidOperationException("复制文件失败：" + src + " -> " + dst +
                (last == null ? "" : "（" + last.Message + "）"));
        }

        /// <summary>写「应用和功能」里的卸载信息。</summary>
        static void WriteUninstallInfo(InstallOptions opt, string target)
        {
            string version = GetVersion();
            string setupExe = Path.Combine(target, SetupExeName);
            string coreExe = Path.Combine(target, CoreExeName);
            string icon = File.Exists(coreExe) ? coreExe : setupExe;

            string uninstall = "\"" + setupExe + "\" --uninstall";
            string quiet = "\"" + setupExe + "\" --silent-uninstall";
            long bytes = DirSize(target);
            int sizeKb = (int)Math.Max(1, bytes / 1024);

            RegistryHive hive = (opt.AllUsers && IsAdmin) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                using (RegistryKey k = baseKey.CreateSubKey(UninstallKeyPath))
                {
                    if (k == null) throw new InvalidOperationException("无法创建注册表项 " + UninstallKeyPath);
                    k.SetValue("DisplayName", ProductNameCn, RegistryValueKind.String);
                    k.SetValue("DisplayVersion", version, RegistryValueKind.String);
                    k.SetValue("Publisher", Publisher, RegistryValueKind.String);
                    k.SetValue("InstallLocation", target, RegistryValueKind.String);
                    k.SetValue("UninstallString", uninstall, RegistryValueKind.String);
                    k.SetValue("QuietUninstallString", quiet, RegistryValueKind.String);
                    k.SetValue("DisplayIcon", icon, RegistryValueKind.String);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
                Log.Info("已写入卸载信息 (" + hive + "): " + UninstallKeyPath);
            }
            catch (Exception ex)
            {
                Log.Error("写卸载信息失败", ex);
                throw new InvalidOperationException("无法写入注册表卸载信息：" + ex.Message);
            }

            // 换安装范围时，清掉另一个位置的旧记录（尽力而为，失败不影响安装）
            try
            {
                RegistryHive other = (hive == RegistryHive.CurrentUser) ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                DeleteRegKey(other, UninstallKeyPath);
            }
            catch { }
        }

        /// <summary>记录安装目录和版本，升级/卸载都要读。</summary>
        static void WriteAppKey(string target)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(RegistryHive.CurrentUser))
                using (RegistryKey k = baseKey.CreateSubKey(AppKeyPath))
                {
                    if (k == null) return;
                    k.SetValue("InstallDir", target, RegistryValueKind.String);
                    k.SetValue("Version", GetVersion(), RegistryValueKind.String);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("写 " + AppKeyPath + " 失败: " + ex.Message);
            }
        }

        // ==================== 快捷方式 ====================

        public static void CreateShortcuts(InstallOptions opt)
        {
            if (opt == null) return;
            string target = opt.TargetDir;
            if (IsBlank(target)) return;

            string core = Path.Combine(target, CoreExeName);
            string settings = Path.Combine(target, SettingsExeName);
            string setup = Path.Combine(target, SetupExeName);
            string icon = File.Exists(core) ? core : setup;

            if (opt.StartMenuShortcut)
            {
                string dir = StartMenuFolderPath();
                EnsureDirectory(dir);
                CreateLink(Path.Combine(dir, MainShortcutName), core, target, "", ProductNameCn, icon);
                if (File.Exists(settings))
                {
                    CreateLink(Path.Combine(dir, SettingsShortcutName), settings, target, "", ProductNameCn + " 设置", icon);
                }
                if (File.Exists(setup))
                {
                    CreateLink(Path.Combine(dir, UninstallShortcutName), setup, target, "--uninstall", "卸载" + ProductNameCn, icon);
                }
                Log.Info("已创建开始菜单快捷方式: " + dir);
            }
            else
            {
                RemoveStartMenuShortcuts();
            }

            if (opt.DesktopShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!IsBlank(desktop))
                {
                    CreateLink(Path.Combine(desktop, MainShortcutName), core, target, "", ProductNameCn, icon);
                    Log.Info("已创建桌面快捷方式: " + desktop);
                }
            }
            else
            {
                RemoveDesktopShortcuts();
            }
        }

        /// <summary>建立 .lnk。优先用 WScript.Shell，失败则退回 IShellLink。</summary>
        static void CreateLink(string linkPath, string targetPath, string workDir, string arguments, string description, string iconPath)
        {
            if (IsBlank(linkPath) || IsBlank(targetPath)) return;
            try
            {
                EnsureDirectory(Path.GetDirectoryName(linkPath));
            }
            catch { }

            try
            {
                CreateLinkViaWScript(linkPath, targetPath, workDir, arguments, description, iconPath);
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("WScript.Shell 创建快捷方式失败，改用 IShellLink：" + ex.Message);
            }
            CreateLinkViaShellLink(linkPath, targetPath, workDir, arguments, description, iconPath);
        }

        static void CreateLinkViaWScript(string linkPath, string targetPath, string workDir, string arguments, string description, string iconPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("系统没有注册 WScript.Shell");

            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                if (link == null) throw new InvalidOperationException("CreateShortcut 返回空");

                Type lt = link.GetType();
                SetComProp(lt, link, "TargetPath", targetPath);
                if (!IsBlank(workDir)) SetComProp(lt, link, "WorkingDirectory", workDir);
                if (!IsBlank(arguments)) SetComProp(lt, link, "Arguments", arguments);
                if (!IsBlank(description)) SetComProp(lt, link, "Description", description);
                if (!IsBlank(iconPath)) SetComProp(lt, link, "IconLocation", iconPath + ",0");
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }

        static void SetComProp(Type t, object obj, string name, object value)
        {
            t.InvokeMember(name, BindingFlags.SetProperty, null, obj, new object[] { value });
        }

        static void CreateLinkViaShellLink(string linkPath, string targetPath, string workDir, string arguments, string description, string iconPath)
        {
            ShellLinkCoClass link = new ShellLinkCoClass();
            try
            {
                IShellLinkW sl = (IShellLinkW)link;
                sl.SetPath(targetPath);
                if (!IsBlank(workDir)) sl.SetWorkingDirectory(workDir);
                if (!IsBlank(arguments)) sl.SetArguments(arguments);
                if (!IsBlank(description)) sl.SetDescription(description);
                if (!IsBlank(iconPath)) sl.SetIconLocation(iconPath, 0);

                IPersistFile pf = (IPersistFile)link;
                pf.Save(linkPath, true);
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(link); } catch { }
            }
        }

        /// <summary>删除开始菜单、桌面、启动文件夹里的快捷方式。</summary>
        public static void RemoveShortcuts()
        {
            RemoveStartMenuShortcuts();
            RemoveDesktopShortcuts();

            // 登录启动文件夹里可能残留的快捷方式（老版本可能建过）
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!IsBlank(startup))
                {
                    DeleteFileQuiet(Path.Combine(startup, MainShortcutName));
                }
            }
            catch { }

            RemoveFolderQuiet(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), StartMenuFolderName));
            RemoveFolderQuiet(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), MainShortcutName));
        }

        static void RemoveStartMenuShortcuts()
        {
            string dir = StartMenuFolderPath();
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    Log.Info("已删除开始菜单快捷方式: " + dir);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("删除开始菜单文件夹失败（尝试逐个删）: " + ex.Message);
                DeleteFileQuiet(Path.Combine(dir, MainShortcutName));
                DeleteFileQuiet(Path.Combine(dir, SettingsShortcutName));
                DeleteFileQuiet(Path.Combine(dir, UninstallShortcutName));
                RemoveFolderQuiet(dir);
            }
        }

        static void RemoveDesktopShortcuts()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (IsBlank(desktop)) return;
            foreach (string name in DesktopShortcutNames)
            {
                DeleteFileQuiet(Path.Combine(desktop, name));
            }
        }

        // ==================== 开机自启动 ====================

        public static void SetAutoStart(bool on, string exePath)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(RegistryHive.CurrentUser))
                using (RegistryKey k = baseKey.CreateSubKey(RunKeyPath))
                {
                    if (k == null) return;
                    if (on)
                    {
                        if (IsBlank(exePath)) return;
                        k.SetValue(RunValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                        Log.Info("已设置开机自动启动: " + exePath);
                    }
                    else
                    {
                        if (k.GetValue(RunValueName) != null)
                        {
                            k.DeleteValue(RunValueName, false);
                            Log.Info("已取消开机自动启动");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("设置开机自动启动失败: " + ex.Message);
            }
        }

        static void RemoveAutoStartAllUsers()
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(RegistryHive.LocalMachine))
                using (RegistryKey k = baseKey.OpenSubKey(RunKeyPath, true))
                {
                    if (k != null && k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        // ==================== 卸载 ====================

        public static void Uninstall(InstallOptions opt, Action<int, string> progress)
        {
            if (opt == null) opt = new InstallOptions();
            string target = opt.TargetDir;
            if (IsBlank(target)) target = InstalledDir;
            if (!IsBlank(target)) target = NormalizeDir(target);
            opt.TargetDir = target;

            Log.Info("开始卸载 " + ProductNameCn + "，安装目录: " + (IsBlank(target) ? "(注册表里没有记录)" : target) +
                     "，删除用户数据=" + opt.RemoveUserData);

            Report(progress, 3, "正在结束正在运行的" + ProductNameCn + "…");
            KillRunning();

            Report(progress, 12, "正在删除快捷方式…");
            RemoveShortcuts();

            Report(progress, 25, "正在清理注册表…");
            SetAutoStart(false, null);
            RemoveAutoStartAllUsers();
            DeleteRegKey(RegistryHive.CurrentUser, UninstallKeyPath);
            DeleteRegKey(RegistryHive.LocalMachine, UninstallKeyPath);
            DeleteRegKey(RegistryHive.CurrentUser, AppKeyPath);
            DeleteRegKey(RegistryHive.LocalMachine, AppKeyPath);

            DeleteReport rep = new DeleteReport();

            Report(progress, 40, "正在删除程序文件…");
            bool selfInside = false;
            if (!IsBlank(target))
            {
                selfInside = IsUnder(CurrentExePath(), target);
                DeleteTree(target, rep, selfInside);
            }

            Report(progress, 72, opt.RemoveUserData ? "正在删除设置和用户词库…" : "正在保留设置和用户词库…");
            if (opt.RemoveUserData)
            {
                DeleteTree(UserDataDir(), rep, false);
            }

            Report(progress, 88, "正在收尾…");
            if (selfInside)
            {
                ScheduleSelfDelete(target);
                if (rep.Deferred.Count == 0) rep.Deferred.Add(CurrentExePath());
            }

            foreach (string d in rep.Deferred) Log.Info("退出后自动删除: " + d);
            foreach (string f in rep.Failed) Log.Warn("未能删除: " + f);

            string msg = "卸载完成";
            if (rep.Failed.Count > 0) msg = "卸载完成（有 " + rep.Failed.Count + " 项未能删除，详见日志）";
            Report(progress, 100, msg);
            Log.Info("卸载结束: " + msg + "，延后删除 " + rep.Deferred.Count + " 项");
        }

        /// <summary>用户数据目录 %APPDATA%\MyIME（设置 + 用户词库 + 日志）。</summary>
        public static string UserDataDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppPaths.AppName);
        }

        static void DeleteTree(string dir, DeleteReport rep, bool deferLocked)
        {
            if (IsBlank(dir) || !Directory.Exists(dir)) return;

            string[] files;
            try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                Log.Warn("枚举文件失败 " + dir + ": " + ex.Message);
                try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); }
                catch { files = new string[0]; }
            }

            foreach (string f in files)
            {
                try
                {
                    FileAttributes attrs = File.GetAttributes(f);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(f, attrs & ~FileAttributes.ReadOnly);
                    }
                    File.Delete(f);
                }
                catch (Exception ex)
                {
                    if (deferLocked || IsSelfProcessFile(f)) rep.Deferred.Add(f);
                    else rep.Failed.Add(f + "  (" + ex.Message + ")");
                }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories); }
            catch { dirs = new string[0]; }
            Array.Sort(dirs);
            for (int i = dirs.Length - 1; i >= 0; i--)
            {
                try { Directory.Delete(dirs[i], false); }
                catch { }
            }

            try
            {
                Directory.Delete(dir, true);
                Log.Info("已删除目录: " + dir);
            }
            catch (Exception ex)
            {
                if (deferLocked) rep.Deferred.Add(dir);
                else rep.Failed.Add(dir + "  (" + ex.Message + ")");
            }
        }

        /// <summary>卸载时程序自己正跑在目标目录里，退出后由 cmd 收尾。</summary>
        static void ScheduleSelfDelete(string target)
        {
            try
            {
                string cmd = "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Path.Combine(target, SetupExeName) +
                             "\" >nul 2>&1 & rmdir /s /q \"" + target + "\" >nul 2>&1";
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
                Log.Info("已安排退出后删除安装目录: " + target);
            }
            catch (Exception ex)
            {
                Log.Warn("安排自动删除失败: " + ex.Message);
            }
        }

        static bool IsSelfProcessFile(string file)
        {
            try
            {
                if (SamePath(Process.GetCurrentProcess().MainModule.FileName, file)) return true;
            }
            catch { }
            try
            {
                ProcessModuleCollection mods = Process.GetCurrentProcess().Modules;
                foreach (ProcessModule m in mods)
                {
                    if (SamePath(m.FileName, file)) return true;
                }
            }
            catch { }
            return false;
        }

        // ==================== 小工具 ====================

        public static void LaunchProgram(string exe, string workDir)
        {
            try
            {
                if (!File.Exists(exe))
                {
                    Log.Warn("找不到要启动的程序: " + exe);
                    return;
                }
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                if (!IsBlank(workDir)) psi.WorkingDirectory = workDir;
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log.Info("已启动: " + exe);
            }
            catch (Exception ex)
            {
                Log.Warn("启动程序失败: " + ex.Message);
            }
        }

        static void EnsureDirectory(string dir)
        {
            if (IsBlank(dir)) return;
            if (Directory.Exists(dir)) return;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法创建目录 " + dir + "：" + ex.Message);
            }
        }

        /// <summary>确认目录真的可写（Program Files 之类没权限时给个中文提示）。</summary>
        static void EnsureWritable(string dir)
        {
            string probe = Path.Combine(dir, ".myime-write-test.tmp");
            try
            {
                File.WriteAllText(probe, "myime", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("没有写入权限：" + dir + "\r\n请换一个目录（推荐当前用户目录），或右键「以管理员身份运行」。" +
                    "\r\n系统提示：" + ex.Message);
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); }
                catch { }
            }
        }

        static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    try { total += new FileInfo(f).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        static void DeleteFileQuiet(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warn("删除文件失败 " + path + ": " + ex.Message);
            }
        }

        static void RemoveFolderQuiet(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { }
        }

        static void Report(Action<int, string> progress, int pct, string msg)
        {
            Log.Info("[" + pct + "%] " + msg);
            if (progress != null)
            {
                try { progress(pct, msg); }
                catch (Exception ex) { Log.Warn("进度回调异常: " + ex.Message); }
            }
        }

        // ---------- 路径 ----------

        public static string NormalizeDir(string dir)
        {
            if (IsBlank(dir)) return dir;
            string d = dir.Trim().Trim('"');
            try { d = Path.GetFullPath(d); }
            catch { }
            return TrimDir(d);
        }

        static string TrimDir(string d)
        {
            if (IsBlank(d)) return d;
            string r = d.TrimEnd(new char[] { '\\', '/' });
            if (r.Length == 2 && r[1] == ':') r = r + Path.DirectorySeparatorChar;
            if (r.Length == 0) r = d;
            return r;
        }

        /// <summary>两个路径是不是同一个（忽略大小写和结尾斜杠）。</summary>
        public static bool SamePath(string a, string b)
        {
            if (IsBlank(a) || IsBlank(b)) return false;
            string x = TrimDir(a.Trim()).TrimEnd(new char[] { '\\', '/' });
            string y = TrimDir(b.Trim()).TrimEnd(new char[] { '\\', '/' });
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>path 是否位于 dir 之内。</summary>
        static bool IsUnder(string path, string dir)
        {
            if (IsBlank(path) || IsBlank(dir)) return false;
            try
            {
                string p = Path.GetFullPath(path);
                string d = TrimDir(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar;
                return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool IsBlank(string s)
        {
            return s == null || s.Trim().Length == 0;
        }

        // ---------- 注册表 ----------

        /// <summary>用 64 位视图打开注册表，避开 WOW6432Node。</summary>
        static RegistryKey OpenBase(RegistryHive hive)
        {
            try { return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64); }
            catch { return RegistryKey.OpenBaseKey(hive, RegistryView.Default); }
        }

        static string ReadRegString(RegistryHive hive, string path, string name)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name);
                    if (v == null) return null;
                    return Convert.ToString(v, CultureInfo.InvariantCulture);
                }
            }
            catch { return null; }
        }

        static void DeleteRegKey(RegistryHive hive, string path)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                {
                    if (baseKey.OpenSubKey(path, false) == null) return;
                    baseKey.DeleteSubKeyTree(path, false);
                    Log.Info("已删除注册表项 (" + hive + "): " + path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("删除注册表项失败 (" + hive + "): " + path + " - " + ex.Message);
            }
        }

        // ---------- 内部类型 ----------

        class CopyResult
        {
            public int Files = 0;
            public long Bytes = 0;
            public List<string> Warnings = new List<string>();
        }

        class DeleteReport
        {
            public List<string> Failed = new List<string>();
            public List<string> Deferred = new List<string>();
        }
    }

    // ==================== WScript.Shell 不可用时的兜底：IShellLink ====================

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkCoClass
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
