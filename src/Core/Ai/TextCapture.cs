using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using MyIme.Shared;

namespace MyIme.Ai
{
    /// <summary>
    /// 读 / 替换"当前焦点输入框"里的文字。这是"自动读取修改"模式的基础。
    ///
    /// 两条路按顺序试：
    ///   1. UI Automation（首选）：ValuePattern 能读能写；TextPattern 能读（含选区）。
    ///   2. 剪贴板兜底：备份剪贴板 -> Ctrl+A / Ctrl+C 读，Ctrl+A / Ctrl+V 写 -> 恢复剪贴板。
    ///
    /// 这些操作会动到用户正在编辑的内容，所以每个方法都返回 bool，
    /// 失败时 error 里给一句中文原因，绝不静默返回成功。
    /// </summary>
    public static class TextCapture
    {
        // ---------- 虚拟键 ----------
        const ushort VK_CONTROL = 0x11;
        const ushort VK_A = 0x41;
        const ushort VK_C = 0x43;
        const ushort VK_V = 0x56;
        const ushort VK_RETURN = 0x0D;

        // ---------- 等待时间（60~120 毫秒，等目标程序反应） ----------
        const int SettleMs = 80;       // 写完剪贴板之后
        const int KeyDelayMs = 70;     // 组合键内部两次按键之间
        const int CopyWaitMs = 120;    // Ctrl+C 之后等目标程序放入剪贴板
        const int PasteWaitMs = 120;   // Ctrl+V 之后等目标程序粘贴
        const int RetryWaitMs = 60;    // 剪贴板被占用时重试的间隔

        static int _clipboardBusy;

        /// <summary>当前是不是正在操作剪贴板（此时不要去碰用户的剪贴板）。</summary>
        public static bool ClipboardBusy
        {
            get { return Interlocked.CompareExchange(ref _clipboardBusy, 0, 0) != 0; }
        }

        // ==================== 读取 ====================

        /// <summary>
        /// 读取当前焦点输入框里的文字。source 说明用了哪条路（UIA 还是剪贴板）。
        /// 返回 true 但 text 为空，说明输入框本来就是空的。
        /// </summary>
        public static bool TryReadFocusedText(out string text, out string source)
        {
            text = null;
            source = null;
            try
            {
                AutomationElement el = Focused();
                if (el != null)
                {
                    string v;
                    if (ReadByValuePattern(el, out v))
                    {
                        text = v;
                        source = "UI Automation（ValuePattern）";
                        Log.Info("读取输入框文字成功：" + source + "，" + Describe(v));
                        return true;
                    }
                    if (ReadByTextPattern(el, out v))
                    {
                        text = v;
                        source = "UI Automation（TextPattern）";
                        Log.Info("读取输入框文字成功：" + source + "，" + Describe(v));
                        return true;
                    }
                }

                string copied;
                string err;
                if (CopyViaClipboard(true, out copied, out err))
                {
                    text = copied == null ? "" : copied;
                    source = "剪贴板（Ctrl+A / Ctrl+C）";
                    Log.Info("读取输入框文字成功：" + source + "，" + Describe(text));
                    return true;
                }
                Log.Warn("读取焦点输入框失败: " + (err == null ? "未知原因" : err));
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("TryReadFocusedText 异常", ex);
                return false;
            }
        }

        /// <summary>读取当前选中的文字。没有选中内容时返回 false。</summary>
        public static bool TryReadSelection(out string text, out string source)
        {
            text = null;
            source = null;
            try
            {
                AutomationElement el = Focused();
                if (el != null)
                {
                    string v;
                    if (ReadSelectionByUia(el, out v) && v.Length > 0)
                    {
                        text = v;
                        source = "UI Automation（TextPattern 选区）";
                        Log.Info("读取选中文字成功：" + source + "，" + Describe(v));
                        return true;
                    }
                }

                string copied;
                string err;
                if (CopyViaClipboard(false, out copied, out err))
                {
                    if (!string.IsNullOrEmpty(copied))
                    {
                        text = copied;
                        source = "剪贴板（Ctrl+C）";
                        Log.Info("读取选中文字成功：" + source + "，" + Describe(copied));
                        return true;
                    }
                    Log.Info("剪贴板兜底没取到选中文字（可能本来就没选中）");
                    return false;
                }
                Log.Warn("读取选中文字失败: " + (err == null ? "未知原因" : err));
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("TryReadSelection 异常", ex);
                return false;
            }
        }

        // ==================== 写回 ====================

        /// <summary>
        /// 把 newText 写回当前焦点输入框。
        /// selectAllFirst = true 时替换整段（Ctrl+A 再粘贴），false 时只在光标处插入。
        /// </summary>
        public static bool TryReplaceFocusedText(string newText, bool selectAllFirst, out string error)
        {
            error = null;
            try
            {
                if (newText == null) newText = "";

                if (!selectAllFirst) return TryInsertText(newText, out error);

                AutomationElement el = Focused();
                if (el != null)
                {
                    ValuePattern vp;
                    if (TryGetValuePattern(el, out vp))
                    {
                        bool readOnly = true;
                        try { readOnly = vp.Current.IsReadOnly; }
                        catch (Exception ex) { Log.Debug("读 IsReadOnly 失败: " + ex.Message); }

                        if (!readOnly)
                        {
                            try
                            {
                                vp.SetValue(newText);
                                Sleep(SettleMs);
                                Log.Info("替换输入框内容成功：UI Automation（ValuePattern.SetValue），" + Describe(newText));
                                return true;
                            }
                            catch (Exception ex)
                            {
                                Log.Warn("ValuePattern.SetValue 失败，改用剪贴板粘贴: " + ex.Message);
                            }
                        }
                        else
                        {
                            Log.Info("目标控件是只读的，改用剪贴板粘贴");
                        }
                    }
                }

                return PasteViaClipboard(newText, true, out error);
            }
            catch (Exception ex)
            {
                Log.Error("TryReplaceFocusedText 异常", ex);
                error = "替换失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>在光标处插入文字（不动其它内容）。</summary>
        public static bool TryInsertText(string text, out string error)
        {
            error = null;
            try
            {
                if (text == null) text = "";
                if (text.Length == 0) return true;

                string err;
                if (PasteViaClipboard(text, false, out err))
                {
                    Log.Info("插入文字成功：剪贴板粘贴，" + Describe(text));
                    return true;
                }

                Log.Warn("剪贴板插入失败，改用 SendInput 逐字输入: " + err);
                string err2;
                if (TypeUnicode(text, out err2))
                {
                    Log.Info("插入文字成功：SendInput 逐字输入，" + Describe(text));
                    error = null;
                    return true;
                }

                error = (err == null ? "插入失败" : err) + "；逐字输入也失败：" + (err2 == null ? "未知原因" : err2);
                Log.Warn("插入文字失败: " + error);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("TryInsertText 异常", ex);
                error = "插入失败：" + ex.Message;
                return false;
            }
        }

        // ==================== UI Automation ====================

        static AutomationElement Focused()
        {
            try
            {
                return AutomationElement.FocusedElement;
            }
            catch (Exception ex)
            {
                // 很多控件会抛 ElementNotAvailableException，这是正常的，不用当错误
                Log.Debug("取焦点元素失败: " + ex.Message);
                return null;
            }
        }

        static bool TryGetValuePattern(AutomationElement el, out ValuePattern pattern)
        {
            pattern = null;
            if (el == null) return false;
            try
            {
                object pat;
                if (!el.TryGetCurrentPattern(ValuePattern.Pattern, out pat)) return false;
                pattern = pat as ValuePattern;
                return pattern != null;
            }
            catch (Exception ex)
            {
                Log.Debug("ValuePattern 不可用: " + ex.Message);
                return false;
            }
        }

        static bool TryGetTextPattern(AutomationElement el, out TextPattern pattern)
        {
            pattern = null;
            if (el == null) return false;
            try
            {
                object pat;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pat)) return false;
                pattern = pat as TextPattern;
                return pattern != null;
            }
            catch (Exception ex)
            {
                Log.Debug("TextPattern 不可用: " + ex.Message);
                return false;
            }
        }

        static bool ReadByValuePattern(AutomationElement el, out string text)
        {
            text = null;
            ValuePattern vp;
            if (!TryGetValuePattern(el, out vp)) return false;
            try
            {
                string v = vp.Current.Value;
                if (v == null) return false;
                text = v;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("ValuePattern 读值失败: " + ex.Message);
                return false;
            }
        }

        static bool ReadByTextPattern(AutomationElement el, out string text)
        {
            text = null;
            TextPattern tp;
            if (!TryGetTextPattern(el, out tp)) return false;
            try
            {
                TextPatternRange range = tp.DocumentRange;
                if (range == null) return false;
                string v = range.GetText(-1);
                if (v == null) return false;
                text = v;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("TextPattern 读文档失败: " + ex.Message);
                return false;
            }
        }

        static bool ReadSelectionByUia(AutomationElement el, out string text)
        {
            text = null;
            TextPattern tp;
            if (!TryGetTextPattern(el, out tp)) return false;
            try
            {
                TextPatternRange[] ranges = tp.GetSelection();
                if (ranges == null || ranges.Length == 0) return false;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ranges.Length; i++)
                {
                    if (ranges[i] == null) continue;
                    string s = ranges[i].GetText(-1);
                    if (string.IsNullOrEmpty(s)) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(s);
                }
                text = sb.ToString();
                return text.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Debug("TextPattern 读选区失败: " + ex.Message);
                return false;
            }
        }

        // ==================== 剪贴板兜底 ====================

        /// <summary>备份剪贴板文本 -> 可选 Ctrl+A -> Ctrl+C -> 读剪贴板 -> 恢复剪贴板。</summary>
        static bool CopyViaClipboard(bool selectAll, out string copied, out string error)
        {
            copied = null;
            error = null;

            string old;
            bool hadOld;
            BackupClipboard(out old, out hadOld);

            bool entered = false;
            try
            {
                EnterClipboard();
                entered = true;

                if (!ClipboardSetText(""))
                {
                    error = "清空剪贴板失败（可能被其它程序占用）";
                    return false;
                }
                Sleep(SettleMs);

                if (selectAll)
                {
                    if (!SendCtrlCombo(VK_A))
                    {
                        error = "发送 Ctrl+A 失败（目标程序可能不接受模拟按键）";
                        return false;
                    }
                    Sleep(KeyDelayMs);
                }

                if (!SendCtrlCombo(VK_C))
                {
                    error = "发送 Ctrl+C 失败（目标程序可能不接受模拟按键）";
                    return false;
                }
                Sleep(CopyWaitMs);

                string now;
                if (!ClipboardGetText(out now))
                {
                    error = "读剪贴板失败（可能被其它程序占用）";
                    return false;
                }
                copied = now == null ? "" : now;
                return true;
            }
            catch (Exception ex)
            {
                error = "剪贴板读取出错：" + ex.Message;
                Log.Error("CopyViaClipboard 异常", ex);
                return false;
            }
            finally
            {
                if (entered) LeaveClipboard();
                RestoreClipboard(old, hadOld);
            }
        }

        /// <summary>把文字放进剪贴板 -> （可选）Ctrl+A -> Ctrl+V。</summary>
        static bool PasteViaClipboard(string newText, bool selectAll, out string error)
        {
            error = null;

            string old;
            bool hadOld;
            BackupClipboard(out old, out hadOld);

            bool entered = false;
            bool pasted = false;
            try
            {
                EnterClipboard();
                entered = true;

                if (!ClipboardSetText(newText))
                {
                    error = "写入剪贴板失败（可能被其它程序占用），请稍后再试";
                    return false;
                }
                Sleep(SettleMs);

                if (selectAll)
                {
                    if (!SendCtrlCombo(VK_A))
                    {
                        error = "发送 Ctrl+A 失败（目标程序可能不接受模拟按键）";
                        return false;
                    }
                    Sleep(KeyDelayMs);
                }

                if (!SendCtrlCombo(VK_V))
                {
                    error = "发送 Ctrl+V 失败（目标程序可能不接受模拟按键）";
                    return false;
                }
                Sleep(PasteWaitMs);
                pasted = true;
            }
            catch (Exception ex)
            {
                error = "剪贴板写入出错：" + ex.Message;
                Log.Error("PasteViaClipboard 异常", ex);
                return false;
            }
            finally
            {
                if (entered) LeaveClipboard();
                RestoreClipboard(old, hadOld);
            }

            if (!pasted) return false;
            if (!selectAll) return true;      // 插入没法可靠校验，粘贴已发出就算成功
            return VerifyReplace(newText, out error);
        }

        /// <summary>粘贴之后尽量读回校验一次，读不回来就如实说是"无法校验"。</summary>
        static bool VerifyReplace(string expected, out string error)
        {
            error = null;
            string exp = expected == null ? "" : expected;
            try
            {
                AutomationElement el = Focused();
                if (el == null)
                {
                    Log.Info("替换已发出，但拿不到焦点元素，无法校验");
                    return true;
                }
                string now;
                if (!ReadByValuePattern(el, out now))
                {
                    Log.Info("替换已发出，但控件不支持读回，无法校验");
                    return true;
                }
                if (now == null) return true;

                string a = now.Trim();
                string b = exp.Trim();
                if (a == b)
                {
                    Log.Info("替换后校验通过");
                    return true;
                }
                if (b.Length > 0 && a.Length > 0)
                {
                    int probe = Math.Min(12, b.Length);
                    if (a.IndexOf(b.Substring(0, probe), StringComparison.Ordinal) >= 0)
                    {
                        Log.Info("替换后校验宽松通过（读回与预期略有差异）");
                        return true;
                    }
                }
                error = "已经发出粘贴，但读回的内容和预期不一致，请确认是否替换成功";
                Log.Warn("替换校验不一致：读回 " + Describe(now) + "，预期 " + Describe(exp));
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug("替换后校验出错（忽略）: " + ex.Message);
                return true;
            }
        }

        static void EnterClipboard()
        {
            Interlocked.Exchange(ref _clipboardBusy, 1);
        }

        static void LeaveClipboard()
        {
            Interlocked.Exchange(ref _clipboardBusy, 0);
        }

        static void BackupClipboard(out string oldText, out bool hadText)
        {
            oldText = null;
            hadText = false;
            try
            {
                string t;
                if (ClipboardGetText(out t))
                {
                    oldText = t;
                    hadText = true;
                }
                else
                {
                    Log.Info("剪贴板里没有文本内容（可能是图片等），只做文本级备份");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("备份剪贴板失败: " + ex.Message);
            }
        }

        static void RestoreClipboard(string oldText, bool hadText)
        {
            if (!hadText) return;
            try
            {
                if (!ClipboardSetText(oldText == null ? "" : oldText))
                    Log.Warn("恢复剪贴板失败（原内容已丢失）");
            }
            catch (Exception ex)
            {
                Log.Warn("恢复剪贴板异常: " + ex.Message);
            }
        }

        // ---------- 剪贴板读写：先 Win32，再 WinForms 兜底 ----------

        static bool ClipboardGetText(out string text)
        {
            text = null;
            if (ClipboardGetTextWin32(out text)) return true;

            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    if (System.Windows.Forms.Clipboard.ContainsText())
                    {
                        string s = System.Windows.Forms.Clipboard.GetText();
                        if (s != null) { text = s; return true; }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("WinForms 读剪贴板失败: " + ex.Message);
            }
            return false;
        }

        static bool ClipboardSetText(string text)
        {
            if (text == null) text = "";
            if (ClipboardSetTextWin32(text)) return true;

            try
            {
                if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                {
                    System.Windows.Forms.Clipboard.SetText(text);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("WinForms 写剪贴板失败: " + ex.Message);
            }
            return false;
        }

        static bool ClipboardGetTextWin32(out string text)
        {
            text = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (Native.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        if (!Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT)) return false;
                        IntPtr handle = Native.GetClipboardData(Native.CF_UNICODETEXT);
                        if (handle == IntPtr.Zero) return false;
                        IntPtr p = Native.GlobalLock(handle);
                        if (p == IntPtr.Zero) return false;
                        try { text = Marshal.PtrToStringUni(p); }
                        finally { Native.GlobalUnlock(handle); }
                        if (text == null) text = "";
                        return true;
                    }
                    finally
                    {
                        try { Native.CloseClipboard(); } catch { }
                    }
                }
                Sleep(RetryWaitMs);
            }
            return false;
        }

        static bool ClipboardSetTextWin32(string text)
        {
            if (text == null) text = "";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                IntPtr hMem = IntPtr.Zero;
                int bytes = (text.Length + 1) * 2;
                hMem = Native.GlobalAlloc(Native.GMEM_MOVEABLE, new UIntPtr((uint)bytes));
                if (hMem == IntPtr.Zero)
                {
                    Sleep(RetryWaitMs);
                    continue;
                }

                IntPtr p = Native.GlobalLock(hMem);
                if (p == IntPtr.Zero)
                {
                    Native.GlobalFree(hMem);
                    Sleep(RetryWaitMs);
                    continue;
                }
                try
                {
                    if (text.Length > 0) Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                    Marshal.WriteInt16(p, text.Length * 2, 0);
                }
                finally
                {
                    Native.GlobalUnlock(hMem);
                }

                if (!Native.OpenClipboard(IntPtr.Zero))
                {
                    Native.GlobalFree(hMem);
                    Sleep(RetryWaitMs);
                    continue;
                }

                bool ok = false;
                try
                {
                    Native.EmptyClipboard();
                    ok = Native.SetClipboardData(Native.CF_UNICODETEXT, hMem) != IntPtr.Zero;
                }
                finally
                {
                    try { Native.CloseClipboard(); } catch { }
                }

                if (ok) return true;   // 所有权已交给系统，不能再 GlobalFree
                Native.GlobalFree(hMem);
                Sleep(RetryWaitMs);
            }
            return false;
        }

        // ==================== 模拟按键 ====================

        static bool SendKey(ushort vk, bool up)
        {
            try
            {
                INPUT[] inputs = new INPUT[1];
                inputs[0].type = Native.INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = vk;
                inputs[0].u.ki.wScan = 0;
                inputs[0].u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
                inputs[0].u.ki.time = 0;
                inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;
                uint sent = Native.SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
                return sent == 1;
            }
            catch (Exception ex)
            {
                Log.Warn("SendInput 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>发一个 Ctrl+某键：Ctrl 的按下和抬起各发一次。</summary>
        static bool SendCtrlCombo(ushort vk)
        {
            bool ok = true;
            ok = SendKey(VK_CONTROL, false) && ok;
            Sleep(KeyDelayMs);
            ok = SendKey(vk, false) && ok;
            Sleep(KeyDelayMs);
            ok = SendKey(vk, true) && ok;
            Sleep(KeyDelayMs);
            ok = SendKey(VK_CONTROL, true) && ok;
            return ok;
        }

        /// <summary>逐字输入（剪贴板也不好使时的最后手段）。</summary>
        static bool TypeUnicode(string text, out string error)
        {
            error = null;
            if (text == null) return true;
            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\r') continue;

                    INPUT[] inputs = new INPUT[2];
                    if (c == '\n')
                    {
                        inputs[0].type = Native.INPUT_KEYBOARD;
                        inputs[0].u.ki.wVk = VK_RETURN;
                        inputs[0].u.ki.dwFlags = 0;
                        inputs[1].type = Native.INPUT_KEYBOARD;
                        inputs[1].u.ki.wVk = VK_RETURN;
                        inputs[1].u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
                    }
                    else
                    {
                        inputs[0].type = Native.INPUT_KEYBOARD;
                        inputs[0].u.ki.wVk = 0;
                        inputs[0].u.ki.wScan = c;
                        inputs[0].u.ki.dwFlags = Native.KEYEVENTF_UNICODE;
                        inputs[1].type = Native.INPUT_KEYBOARD;
                        inputs[1].u.ki.wVk = 0;
                        inputs[1].u.ki.wScan = c;
                        inputs[1].u.ki.dwFlags = Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP;
                    }
                    inputs[0].u.ki.time = 0;
                    inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;
                    inputs[1].u.ki.time = 0;
                    inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

                    uint sent = Native.SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (sent != 2)
                    {
                        error = "SendInput 输入第 " + (i + 1).ToString() + " 个字符失败";
                        return false;
                    }
                    if ((i & 15) == 0) Sleep(10);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "逐字输入出错：" + ex.Message;
                Log.Error("TypeUnicode 异常", ex);
                return false;
            }
        }

        // ==================== 小工具 ====================

        static void Sleep(int ms)
        {
            try { Thread.Sleep(ms); } catch { }
        }

        static string Describe(string s)
        {
            if (s == null) return "0 字";
            return s.Length.ToString() + " 字";
        }
    }
}
