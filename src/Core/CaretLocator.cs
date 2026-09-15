using System;
using System.Runtime.InteropServices;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 找"插入光标"在屏幕上的位置，用来把候选窗贴在光标下方。
    ///
    /// 优先用 GetGUIThreadInfo 的 rcCaret —— 这是最可靠的一条路（浏览器、记事本、Office 都行）。
    /// 拿不到就退回 GetCaretPos + ClientToScreen，再不行就退回鼠标位置。
    /// 注意：查的是**前台线程**的 GUI 信息，不是我们自己这个线程。
    /// </summary>
    public static class CaretLocator
    {
        public static bool TryGetCaretRect(out RECT rect)
        {
            rect = new RECT();
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;

                uint pid;
                uint tid = Native.GetWindowThreadProcessId(fg, out pid);
                if (tid == 0) return false;

                // 别把自己算进去
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;

                GUITHREADINFO gti = new GUITHREADINFO();
                gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (!Native.GetGUIThreadInfo(tid, ref gti)) return false;

                if (gti.hwndCaret != IntPtr.Zero
                    && (gti.rcCaret.Right > gti.rcCaret.Left || gti.rcCaret.Bottom > gti.rcCaret.Top))
                {
                    POINT origin = new POINT();
                    origin.X = gti.rcCaret.Left;
                    origin.Y = gti.rcCaret.Top;
                    if (Native.ClientToScreen(gti.hwndCaret, ref origin))
                    {
                        rect.Left = origin.X;
                        rect.Top = origin.Y;
                        rect.Right = origin.X + Math.Max(1, gti.rcCaret.Right - gti.rcCaret.Left);
                        rect.Bottom = origin.Y + Math.Max(1, gti.rcCaret.Bottom - gti.rcCaret.Top);
                        return true;
                    }
                }

                // 退路：GetCaretPos 是相对于焦点窗口客户区的
                if (gti.hwndFocus != IntPtr.Zero)
                {
                    POINT pt;
                    if (Native.GetCaretPos(out pt) && Native.ClientToScreen(gti.hwndFocus, ref pt))
                    {
                        rect.Left = pt.X;
                        rect.Top = pt.Y;
                        rect.Right = pt.X + 2;
                        rect.Bottom = pt.Y + 20;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("取光标位置失败: " + ex.Message);
            }
            return false;
        }

        /// <summary>最终退路：鼠标位置。</summary>
        public static POINT FallbackPoint()
        {
            POINT p = new POINT();
            try
            {
                Native.GetCursorPos(out p);
                p.Y += 16;
            }
            catch { p.X = 100; p.Y = 100; }
            return p;
        }

        /// <summary>取光标所在显示器的信息（用来做多屏边界处理）。</summary>
        public static bool TryGetMonitorInfo(int x, int y, out RECT monitor, out RECT work)
        {
            monitor = new RECT();
            work = new RECT();
            try
            {
                POINT p = new POINT();
                p.X = x; p.Y = y;
                IntPtr mon = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero) return false;
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!Native.GetMonitorInfo(mon, ref mi)) return false;
                monitor = mi.rcMonitor;
                work = mi.rcWork;
                return true;
            }
            catch { return false; }
        }
    }
}
