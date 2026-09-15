using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 把文字/按键送进目标程序。
    /// 用 SendInput + KEYEVENTF_UNICODE，这条路对中文、emoji、任何输入法都通用。
    /// </summary>
    public static class TextInjector
    {
        const int ChunkSize = 40;   // SendInput 一次别塞太多，分批发更稳

        /// <summary>发送一段文本（随便什么字符，包括中文和 emoji）。</summary>
        public static bool SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            try
            {
                List<INPUT> batch = new List<INPUT>(ChunkSize * 2);
                foreach (char ch in text)
                {
                    batch.Add(MakeUnicode(ch, false));
                    batch.Add(MakeUnicode(ch, true));
                    if (batch.Count >= ChunkSize * 2)
                    {
                        if (!Flush(batch)) return false;
                        batch.Clear();
                    }
                }
                return Flush(batch);
            }
            catch (Exception ex)
            {
                Log.Error("注入文本失败", ex);
                return false;
            }
        }

        static bool Flush(List<INPUT> batch)
        {
            if (batch.Count == 0) return true;
            INPUT[] arr = batch.ToArray();
            uint sent = Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
            if (sent != arr.Length)
            {
                Log.Warn("SendInput 只送出 " + sent + "/" + arr.Length + " 个事件，错误码 "
                         + Marshal.GetLastWin32Error());
                return false;
            }
            return true;
        }

        static INPUT MakeUnicode(char ch, bool up)
        {
            INPUT input = new INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = 0;
            input.u.ki.wScan = ch;
            input.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        static INPUT MakeVk(int vk, bool up)
        {
            INPUT input = new INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = 0;
            input.u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
            input.u.ki.time = 0;
            input.u.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        /// <summary>发一个普通虚拟键。</summary>
        public static void SendVk(int vk)
        {
            List<INPUT> b = new List<INPUT>();
            b.Add(MakeVk(vk, false));
            b.Add(MakeVk(vk, true));
            Flush(b);
        }

        /// <summary>发退格键 count 次。</summary>
        public static void SendBackspace(int count)
        {
            if (count <= 0) return;
            List<INPUT> b = new List<INPUT>(count * 2);
            for (int i = 0; i < count; i++)
            {
                b.Add(MakeVk(0x08, false));
                b.Add(MakeVk(0x08, true));
            }
            Flush(b);
        }

        const int VK_CONTROL = 0x11;
        const int VK_SHIFT = 0x10;
        const int VK_MENU = 0x12;

        /// <summary>发组合键，例如 SendCombo(VK_CONTROL, 0x41) = Ctrl+A。</summary>
        public static void SendCombo(int modifierVk, int vk)
        {
            List<INPUT> b = new List<INPUT>();
            b.Add(MakeVk(modifierVk, false));
            b.Add(MakeVk(vk, false));
            b.Add(MakeVk(vk, true));
            b.Add(MakeVk(modifierVk, true));
            Flush(b);
        }

        public static void SendCtrl(int vk) { SendCombo(VK_CONTROL, vk); }

        /// <summary>全选（Ctrl+A）。</summary>
        public static void SendSelectAll() { SendCombo(VK_CONTROL, 0x41); }

        /// <summary>复制（Ctrl+C）。</summary>
        public static void SendCopy() { SendCombo(VK_CONTROL, 0x43); }

        /// <summary>粘贴（Ctrl+V）。</summary>
        public static void SendPaste() { SendCombo(VK_CONTROL, 0x56); }

        /// <summary>撤销（Ctrl+Z）。</summary>
        public static void SendUndo() { SendCombo(VK_CONTROL, 0x5A); }

        /// <summary>等目标程序把剪贴板/选区处理完。</summary>
        public static void Settle(int ms)
        {
            try { Thread.Sleep(ms); } catch { }
        }
    }
}
