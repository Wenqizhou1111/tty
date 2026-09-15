using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 判断"现在是不是在全屏游戏里"。
    /// 这是"打游戏不卡出输入法"的关键：命中就自动锁定，键盘钩子会被整体摘掉。
    /// </summary>
    public static class GameLock
    {
        static readonly string[] ShellClasses = new string[] {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
        };

        /// <summary>前台窗口是否铺满整个显示器（无边框全屏 / D3D 独占全屏）。</summary>
        public static bool IsForegroundFullscreen()
        {
            try
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                if (hwnd == Native.GetDesktopWindow()) return false;

                string cls = Native.ForegroundClassName();
                foreach (string bad in ShellClasses)
                    if (string.Equals(cls, bad, StringComparison.OrdinalIgnoreCase)) return false;

                RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) return false;
                if (r.Width < 320 || r.Height < 240) return false;

                RECT monitor, work;
                if (!CaretLocator.TryGetMonitorInfo(r.Left + r.Width / 2, r.Top + r.Height / 2, out monitor, out work))
                    return false;

                // 必须盖住整个显示器（最大化窗口只盖住工作区，不算）
                bool covers = r.Left <= monitor.Left && r.Top <= monitor.Top
                              && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
                return covers;
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

        /// <summary>前台进程名是否在游戏名单里。</summary>
        public static bool IsForegroundInList(List<string> processNames)
        {
            if (processNames == null || processNames.Count == 0) return false;
            string name = Native.ForegroundProcessName();
            if (name.Length == 0) return false;
            foreach (string g in processNames)
            {
                if (string.IsNullOrEmpty(g)) continue;
                string g2 = g.Trim().ToLowerInvariant();
                if (g2.Length == 0) continue;
                if (g2.EndsWith(".exe", StringComparison.Ordinal)) g2 = g2.Substring(0, g2.Length - 4);
                if (name == g2 || name.IndexOf(g2, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>综合判断：该不该因为游戏而自动锁定。</summary>
        public static bool ShouldAutoLock(GameLockSettings cfg)
        {
            if (cfg == null) return false;
            if (cfg.AutoLockGames && IsForegroundInList(cfg.GameProcesses)) return true;
            if (cfg.AutoLockFullscreen && (IsForegroundFullscreen() || IsD3DFullscreen())) return true;
            return false;
        }

        /// <summary>给日志/界面用的一句话描述。</summary>
        public static string DescribeForeground()
        {
            try
            {
                string proc = Native.ForegroundProcessName();
                string cls = Native.ForegroundClassName();
                bool fs = IsForegroundFullscreen();
                return "前台=" + (proc.Length == 0 ? "?" : proc) + " 类=" + cls + " 全屏=" + (fs ? "是" : "否");
            }
            catch { return "前台=? "; }
        }
    }
}
