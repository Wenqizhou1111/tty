using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>返回 true 表示这个键被输入法吃掉了（不再传给目标程序）。</summary>
    public delegate bool KeyHandler(int vk, bool isDown, bool shift, bool ctrl, bool alt);

    /// <summary>
    /// 全局低级键盘钩子（WH_KEYBOARD_LL）。
    ///
    /// 两条铁律：
    ///  1. 回调里**绝不能**做慢操作（联网、弹窗、等待）。Windows 有 LowLevelHooksTimeout
    ///     （默认 300ms），超时会把钩子直接摘掉，表现为"输入法突然失灵"。
    ///     所以 AI 调用一律丢到后台线程，这里只做查表 + 显示候选窗。
    ///  2. 回调里绝不能抛异常穿出去。全部 try/catch。
    /// </summary>
    public sealed class KeyboardHook : IDisposable
    {
        readonly LowLevelKeyboardProc _proc;   // 必须用字段保引用，否则会被 GC 回收
        IntPtr _hook = IntPtr.Zero;

        public KeyHandler OnKey;
        public Action<string> OnError;

        /// <summary>
        /// 是否处理"注入"的按键。默认 false。
        /// 打开（环境变量 MYIME_ACCEPT_INJECTED=1）只给自动化测试用，正常使用别开。
        /// </summary>
        public static readonly bool AcceptInjected =
            Environment.GetEnvironmentVariable("MYIME_ACCEPT_INJECTED") == "1";

        public KeyboardHook()
        {
            _proc = Callback;
        }

        public bool IsInstalled { get { return _hook != IntPtr.Zero; } }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                IntPtr hmod = Native.GetModuleHandle(null);
                _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, hmod, 0);
                if (_hook == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    Raise("安装键盘钩子失败，Win32 错误码 " + err);
                    return false;
                }
                Log.Info("键盘钩子已安装");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("安装键盘钩子异常", ex);
                Raise("安装键盘钩子异常: " + ex.Message);
                return false;
            }
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            try
            {
                Native.UnhookWindowsHookEx(_hook);
                Log.Info("键盘钩子已卸载");
            }
            catch (Exception ex) { Log.Error("卸载键盘钩子异常", ex); }
            finally { _hook = IntPtr.Zero; }
        }

        public void Dispose() { Uninstall(); }

        void Raise(string msg)
        {
            if (OnError != null) { try { OnError(msg); } catch { } }
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode < 0) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

                int msg = wParam.ToInt32();
                bool isDown = (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN);
                bool isUp = (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP);
                if (!isDown && !isUp) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

                KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(KBDLLHOOKSTRUCT));

                // 注入事件直接放行：既包括我们自己 SendInput 上屏的，也包括其它工具注入的。
                // 这条判断同时解决了"自己注入的键又被自己处理"的死循环
                // （AI 上屏、SendInput 打字都会被这条挡住，不会二次进入组合状态）。
                // 自动化测试需要反过来，所以留了 MYIME_ACCEPT_INJECTED=1 这个开关。
                if (!AcceptInjected && (data.flags & Native.LLKHF_INJECTED) != 0)
                    return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

                // VK_PACKET 只可能来自 KEYEVENTF_UNICODE 注入（真实键盘永远不会产生它）。
                // 我们自己上屏的中文就是这种事件，必须无条件放行，否则会被当成按键处理。
                if (data.vkCode == 0xE7)
                    return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

                int vk = (int)data.vkCode;
                bool shift = IsDown(0x10, vk);
                bool ctrl = IsDown(0x11, vk);
                bool alt = IsDown(0x12, vk);

                KeyHandler handler = OnKey;
                if (handler != null)
                {
                    bool handled = handler(vk, isDown, shift, ctrl, alt);
                    if (handled) return (IntPtr)1;   // 吃掉这个键
                }
            }
            catch (Exception ex)
            {
                // 回调里绝不能抛异常，否则钩子会被系统摘掉
                Log.Error("键盘钩子回调异常", ex);
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>判断修饰键是否按下。当前按下的键本身是修饰键时也算按下。</summary>
        static bool IsDown(int modifierVk, int currentVk)
        {
            if (currentVk == modifierVk) return true;
            if (modifierVk == 0x10 && (currentVk == 0xA0 || currentVk == 0xA1)) return true;
            if (modifierVk == 0x11 && (currentVk == 0xA2 || currentVk == 0xA3)) return true;
            if (modifierVk == 0x12 && (currentVk == 0xA4 || currentVk == 0xA5)) return true;
            return (Native.GetAsyncKeyState(modifierVk) & 0x8000) != 0;
        }

        // ---------- 按键 -> 字符（按美式键盘布局，这是中文用户的主流布局） ----------

        public static readonly int[] LetterVks = BuildLetters();
        public static readonly int[] DigitVks = BuildDigits();

        static int[] BuildLetters()
        {
            List<int> list = new List<int>();
            for (int vk = 0x41; vk <= 0x5A; vk++) list.Add(vk);
            return list.ToArray();
        }

        static int[] BuildDigits()
        {
            List<int> list = new List<int>();
            for (int vk = 0x30; vk <= 0x39; vk++) list.Add(vk);
            return list.ToArray();
        }

        public static bool IsLetter(int vk) { return vk >= 0x41 && vk <= 0x5A; }
        public static bool IsDigit(int vk) { return vk >= 0x30 && vk <= 0x39; }

        /// <summary>虚拟键码 -> 小写字母；不是字母返回 '\0'。</summary>
        public static char LetterOf(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return (char)('a' + (vk - 0x41));
            return '\0';
        }

        /// <summary>虚拟键码 -> 数字字符。</summary>
        public static char DigitOf(int vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return (char)vk;
            return '\0';
        }

        /// <summary>标点键 -> 字符（美式布局）。返回 '\0' 表示不是标点。</summary>
        public static char PunctuationOf(int vk, bool shift)
        {
            switch (vk)
            {
                case 0xBA: return shift ? ':' : ';';
                case 0xBB: return shift ? '+' : '=';
                case 0xBC: return shift ? '<' : ',';
                case 0xBD: return shift ? '_' : '-';
                case 0xBE: return shift ? '>' : '.';
                case 0xBF: return shift ? '?' : '/';
                case 0xC0: return shift ? '~' : (char)0x60;   // 反引号
                case 0xDB: return shift ? '{' : '[';
                case 0xDC: return shift ? '|' : '\\';
                case 0xDD: return shift ? '}' : ']';
                case 0xDE: return shift ? '"' : '\'';
                case 0x6A: return '*';
                case 0x6B: return '+';
                case 0x6D: return '-';
                case 0x6E: return '.';
                case 0x6F: return '/';
                default: return '\0';
            }
        }

        /// <summary>0x60 是反引号，单独给个常量避免源码里出现反引号字符。</summary>
        public const char Backtick = (char)0x60;
    }
}
