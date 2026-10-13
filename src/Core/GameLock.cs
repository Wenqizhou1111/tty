using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 判断"现在是不是在全屏游戏里"。
    /// 这是"打游戏不卡出输入法"的关键：命中就自动锁定，键盘钩子会被整体摘掉。
    ///
    /// 三个容易踩的坑，这里都做了处理：
    ///   1) 全屏 ≠ 游戏。看全屏视频、浏览器 F11、放 PPT 也盖满屏幕，
    ///      如果一律锁掉，用户会突然发现"打不了字了"。→ 非游戏全屏白名单
    ///   2) 进程名不能用子串匹配。名单里写 "cs"，会把 csrss.exe（系统关键进程）
    ///      也算成游戏。→ 只允许精确匹配，或"前缀 + 数字/下划线/横线"
    ///   3) 不能一失焦就解锁。切桌面、弹个窗都会让前台窗口变一下，
    ///      钩子反复装卸既卡顿又更容易被反作弊盯上。→ 解锁延迟
    /// </summary>
    public static class GameLock
    {
        static readonly string[] ShellClasses = new string[] {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
        };

        /// <summary>
        /// 会全屏、但**不是游戏**的程序。命中这些就只走"游戏名单"判断，
        /// 不再因为"盖满屏幕"而自动锁定。
        /// 典型场景：全屏看 B 站 / 放 PPT / 全屏看 PDF，这些时候是要打字的。
        /// </summary>
        static readonly string[] NonGameFullscreen = new string[] {
            // 浏览器（F11 全屏、全屏看文档和视频）
            "chrome", "msedge", "firefox", "iexplore", "opera", "brave", "vivaldi",
            "360se", "360chrome", "sogouexplorer", "qqbrowser", "maxthon", "theworld",
            // 播放器
            "vlc", "potplayer", "potplayermini64", "potplayermini", "mpc-hc", "mpc-be", "mpc-hc64",
            "kmplayer", "mpv", "gomplayer", "pptv", "qvod", "bilibili", "youku", "iqiyi",
            "tencentvideo", "qqlive", "mgtv", "le", "sohuvideo", "wmp", "wmplayer",
            // 办公 / 阅读
            "powerpnt", "wpp", "keynote", "acrobat", "acrord32", "foxitreader", "foxitpdfreader",
            "sumatrapdf", "wpspdf", "pdfxcview", "nitroreader",
            // 会议 / 直播 / 录屏
            "zoom", "teams", "ms-teams", "wemeet", "dingtalk", "skype", "voov", "obs64", "obs32", "obs",
            // 图片 / 系统
            "photos", "photosapp", "mspaint", "explorer", "taskmgr", "regedit",
            // 聊天 / 笔记（全屏看消息、看文档时也得能打字）
            "wechat", "weixin", "wechatapp", "qq", "tim", "wework", "wxwork",
            "feishu", "lark", "notion", "obsidian", "typora", "onenote", "evernote"
        };

        /// <summary>失焦后要连续多久没再命中，才真的解锁（防止钩子反复装卸）。</summary>
        const double UnlockDelayMs = 2500;

        // ---- 状态 ----
        static bool _decided;
        static DateTime _lastHit = DateTime.MinValue;

        // ---- 前台窗口缓存：窗口没换就不用重新算一堆 Win32 ----
        static IntPtr _cacheHwnd = IntPtr.Zero;
        static string _cacheProc = "";
        static string _cacheCls = "";
        static bool _cacheFs;
        static bool _cacheShell;

        /// <summary>用户名单里写 dota，就应该能匹配 dota2；但写 cs 不该匹配 csrss。</summary>
        public static bool ProcessMatches(string name, string pattern)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern)) return false;
            name = name.Trim().ToLowerInvariant();
            pattern = pattern.Trim().ToLowerInvariant();
            if (pattern.EndsWith(".exe", StringComparison.Ordinal))
                pattern = pattern.Substring(0, pattern.Length - 4);
            if (pattern.Length == 0) return false;
            if (name == pattern) return true;
            // 允许 "dota" 命中 "dota2"、"hollow_knight" 命中 "hollow_knight_2"，
            // 但 "cs" 不能命中 "csrss"、也不能命中 "mycs"（必须是前缀）。
            if (name.Length > pattern.Length && name.StartsWith(pattern, StringComparison.Ordinal))
            {
                char c = name[pattern.Length];
                return char.IsDigit(c) || c == '_' || c == '-' || c == '.';
            }
            return false;
        }

        static void RefreshCache()
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == _cacheHwnd && _cacheHwnd != IntPtr.Zero) return;

            _cacheHwnd = hwnd;
            _cacheProc = Native.ForegroundProcessName();
            _cacheCls = Native.ForegroundClassName();
            _cacheFs = ComputeFullscreen(hwnd, _cacheCls);
            _cacheShell = false;
            foreach (string bad in ShellClasses)
                if (string.Equals(_cacheCls, bad, StringComparison.OrdinalIgnoreCase)) { _cacheShell = true; break; }
        }

        static bool ComputeFullscreen(IntPtr hwnd, string cls)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return false;
                if (hwnd == Native.GetDesktopWindow()) return false;

                // 最大化的窗口不算全屏游戏。
                // 任务栏设成"自动隐藏"时，工作区 == 整个显示器，这时候任何最大化的
                // 窗口（浏览器、编辑器、微信…）都会盖满整个显示器，被判成"全屏游戏"
                // 而把输入法锁掉 —— 用户会莫名其妙打不了字。
                // 真正的全屏游戏一般是无边框窗口或独占全屏，不带 WS_MAXIMIZE。
                if (Native.IsZoomed(hwnd)) return false;

                RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) return false;
                if (r.Width < 320 || r.Height < 240) return false;

                RECT monitor, work;
                if (!CaretLocator.TryGetMonitorInfo(r.Left + r.Width / 2, r.Top + r.Height / 2, out monitor, out work))
                    return false;

                // 必须盖住整个显示器（最大化窗口只盖住工作区，不算）
                return r.Left <= monitor.Left && r.Top <= monitor.Top
                       && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
            }
            catch { return false; }
        }

        /// <summary>前台窗口是否铺满整个显示器（无边框全屏 / D3D 独占全屏）。</summary>
        public static bool IsForegroundFullscreen()
        {
            try
            {
                RefreshCache();
                if (_cacheShell) return false;
                return _cacheFs;
            }
            catch (Exception ex)
            {
                Log.Warn("全屏检测失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>系统是否报告正在跑 D3D 独占全屏程序。</summary>
        public static bool IsD3DFullscreen()
        {
            try
            {
                int state;
                if (Native.SHQueryUserNotificationState(out state) != 0) return false;
                return state == Native.QUNS_RUNNING_D3D_FULL_SCREEN;
            }
            catch { return false; }
        }

        /// <summary>前台是不是"会全屏但不是游戏"的那类程序（浏览器、播放器、PPT…）。</summary>
        public static bool IsNonGameFullscreenApp()
        {
            try
            {
                RefreshCache();
                if (_cacheProc.Length == 0) return false;
                foreach (string g in NonGameFullscreen)
                    if (ProcessMatches(_cacheProc, g)) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>前台进程名是否在游戏名单里。</summary>
        public static bool IsForegroundInList(List<string> processNames)
        {
            if (processNames == null || processNames.Count == 0) return false;
            string name;
            try { RefreshCache(); name = _cacheProc; }
            catch { name = Native.ForegroundProcessName(); }
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string g in processNames)
                if (ProcessMatches(name, g)) return true;
            return false;
        }

        /// <summary>不带到"该不该锁"的状态机里，纯粹算一次原始判定。</summary>
        public static bool ComputeRaw(GameLockSettings cfg)
        {
            if (cfg == null) return false;
            // 用户点名加进名单的，无条件算数（哪怕它也在"非游戏"白名单里）
            if (cfg.AutoLockGames && IsForegroundInList(cfg.GameProcesses)) return true;
            if (cfg.AutoLockFullscreen)
            {
                if (IsNonGameFullscreenApp()) return false;   // 全屏视频/PPT/浏览器 -> 不锁
                if (IsForegroundFullscreen() || IsD3DFullscreen()) return true;
            }
            return false;
        }

        /// <summary>
        /// 综合判断：该不该因为游戏而自动锁定。
        /// 命中立刻锁，但**解锁要等 2.5 秒**没有再命中 —— 切一下桌面、
        /// 弹一个窗，不该马上把钩子装回来。
        /// </summary>
        public static bool ShouldAutoLock(GameLockSettings cfg)
        {
            bool raw = ComputeRaw(cfg);
            DateTime now = DateTime.UtcNow;

            if (raw)
            {
                _lastHit = now;
                _decided = true;
                return true;
            }
            if (_decided && (now - _lastHit).TotalMilliseconds < UnlockDelayMs) return true;
            _decided = false;
            return false;
        }

        /// <summary>设置界面改了名单之后调用，清掉缓存让下次重新判定。</summary>
        public static void ResetCache()
        {
            _cacheHwnd = IntPtr.Zero;
            _decided = false;
            _lastHit = DateTime.MinValue;
        }

        /// <summary>给日志/界面用的一句话描述。</summary>
        public static string DescribeForeground()
        {
            try
            {
                RefreshCache();
                string extra = IsNonGameFullscreenApp() ? "（非游戏全屏）" : "";
                return "前台=" + (_cacheProc.Length == 0 ? "?" : _cacheProc)
                       + " 类=" + _cacheCls + " 全屏=" + (_cacheFs ? "是" : "否") + extra;
            }
            catch { return "前台=? "; }
        }
    }
}
