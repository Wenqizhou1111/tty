using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MyIme.Shared;

/// <summary>
/// 端到端打字测试：真的开一个窗口、真的发按键、看输入法有没有把拼音变成汉字。
///
/// 前提：MyIme.Core.exe 正在运行，且带着环境变量 MYIME_ACCEPT_INJECTED=1
/// （因为输入法默认会忽略注入的按键，那是为了避免自己上屏的文字又被自己处理）。
/// </summary>
static class TypeTest
{
    static Form _form;
    static TextBox _box;
    static Label _hint;
    static Timer _timer;
    static int _step;
    static int _fail;
    static readonly StringBuilder _report = new StringBuilder();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

    /// <summary>在窗体左上角点一下 —— 那是候选条**之外**的地方。</summary>
    static void ClickAwayFromBar()
    {
        try
        {
            Point p = _form.PointToScreen(new Point(24, 24));
            SetCursorPos(p.X, p.Y);
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);      // LEFTDOWN
            System.Threading.Thread.Sleep(80);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);      // LEFTUP
            Application.DoEvents();
        }
        catch { }
    }

    /// <summary>候选条现在是不是显示着。用它验证"打完就关""Esc 关掉"这些行为。</summary>
    static bool BarVisible()
    {
        try
        {
            IntPtr h = FindWindow(null, "松林1234输入法 输入条");
            return h != IntPtr.Zero && IsWindowVisible(h);
        }
        catch { return false; }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Log.MinLevel = LogLevel.Error;

        _form = new Form();
        _form.Text = "松林1234输入法 · 端到端打字测试";
        _form.Size = new Size(700, 320);
        _form.StartPosition = FormStartPosition.CenterScreen;
        _form.TopMost = true;

        _box = new TextBox();
        _box.Multiline = true;
        _box.Dock = DockStyle.Fill;
        _box.Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Regular, GraphicsUnit.Point);
        _box.ScrollBars = ScrollBars.Vertical;
        _form.Controls.Add(_box);

        _hint = new Label();
        _hint.Dock = DockStyle.Bottom;
        _hint.Height = 28;
        _hint.Text = "测试进行中，请不要动键盘鼠标…";
        _form.Controls.Add(_hint);

        _form.Shown += delegate
        {
            _box.Focus();
            _timer = new Timer();
            _timer.Interval = 1300;
            _timer.Tick += delegate { Step(); };
            _timer.Start();
        };

        Application.Run(_form);

        Console.WriteLine();
        Console.WriteLine("══════════ 端到端打字测试结果 ══════════");
        Console.Write(_report.ToString());
        Console.WriteLine("═══════════════════════════════════════");
        if (_fail == 0) { Console.WriteLine("全部通过"); return 0; }
        Console.WriteLine("失败 " + _fail + " 项");
        return 1;
    }

    static void Step()
    {
        // 每步开始前都重新抢一次焦点：候选条是 TopMost 窗口，
        // 万一焦点被抢走，SendInput 的中文就会打到别的窗口去。
        if (_step > 0) ForwardFocus();

        switch (_step)
        {
            case 0:
                TypeText("nihao"); PressKey(0x20);
                break;
            case 1:
                Check("你好", "敲 nihao + 空格 -> 你好");
                Reset(); TypeText("zhongguo"); PressKey(0x20);
                break;
            case 2:
                Check("中国", "敲 zhongguo + 空格 -> 中国");
                Reset(); TypeText("shurufa"); PressKey(0x20);
                break;
            case 3:
                Check("输入法", "敲 shurufa + 空格 -> 输入法");
                Reset(); TypeText("xiexie"); PressKey(0x20);
                break;
            case 4:
                Check("谢谢", "敲 xiexie + 空格 -> 谢谢");
                Reset(); PressKey(0xBC);
                break;
            case 5:
                Check("，", "按逗号 -> 全角逗号");
                Reset(); TypeText("nihaoma"); PressKey(0x31);
                break;
            case 6:
                Check("你好吗", "敲 nihaoma + 数字1 -> 你好吗");
                Reset(); TypeText("nh"); PressKey(0x20);
                break;
            case 7:
                Check("你好", "简拼 nh + 空格 -> 你好");
                Reset(); TypeText("wo"); PressKey(0x20);
                break;
            case 8:
                Check("我", "敲 wo + 空格 -> 我");
                // 联想：上屏「我」之后候选条应该给出联想词，按 1 选中
                PressKey(0x31);
                break;
            case 9:
                {
                    string t = _box.Text;
                    bool ok = t.Length > 1 && t != "我";
                    Record(ok, "上屏后联想：按 1 得到联想的词（实际「" + t + "」）");
                    Reset(); TypeText("vvv"); PressKey(0x20);
                }
                break;
            case 10:
                Check("vvv", "非拼音原样直通（vvv + 空格 -> vvv）");
                // 回车直通：中文模式下敲拼音按回车，应该原样输出字母
                Reset(); TypeText("nihao"); PressKey(0x0D);
                break;
            case 11:
                Check("nihao", "中文模式下敲 nihao + 回车 -> 原样输出字母（不选候选）");
                // 准备退格测试：先上屏「你好」，再敲 hao 停在缓冲区里不提交
                Reset(); TypeText("nihao"); PressKey(0x20);
                break;
            case 12:
                Check("你好", "（退格测试准备）nihao + 空格 -> 你好");
                TypeText("hao");
                break;
            case 13:
                // 模拟"按住退格不放"：连发 3 次 KEYDOWN，中间不发 KEYUP。
                // Windows 的按键自动重复就是这个形态，专门用来验证连删。
                RepeatKey(0x08, 3);
                break;
            case 14:
                Check("你好", "按住退格连删拼音：3 个字母全删掉，正文「你好」不受影响");
                PressKey(0x08);
                break;
            case 15:
                Check("你", "拼音删空后再按退格 -> 正文最后一个字被删掉");
                // Esc：立刻退出输入框，什么都不上屏
                Reset(); TypeText("nihao"); PressKey(0x1B);
                break;
            case 16:
                Check("", "Esc -> 拼音被丢弃，正文没有上屏");
                Record(!BarVisible(), "Esc 之后输入条已经关闭");
                // 光标定位：敲 hao 然后把光标挪到最前面
                TypeText("hao");
                PressKey(0x25); PressKey(0x25); PressKey(0x25);
                break;
            case 17:
                PressKey(0x08);
                Check("", "光标在拼音最前面时按退格 -> 不会漏到正文里去删字");
                PressKey(0x27); PressKey(0x27); PressKey(0x27);   // 光标挪回末尾
                PressKey(0x08);                                    // 删掉最后一个字母 o
                break;
            case 18:
                PressKey(0x20);                                    // ha -> 哈
                break;
            case 19:
                Check("哈", "光标移到末尾后退格删掉最后一个字母，ha 上屏「哈」");
                Record(!BarVisible(), "拼音全部上屏后输入条自动关闭");
                // 点输入条以外的地方 -> 结束本次输入
                Reset(); TypeText("zhongguo");
                break;
            case 20:
                Record(BarVisible(), "正在拼拼音时输入条是显示着的");
                ClickAwayFromBar();
                break;
            case 21:
                Check("", "点了输入条以外的地方 -> 拼音被丢弃，没有上屏");
                Record(!BarVisible(), "点了别处之后输入条关闭");
                // ===== 回归用例：部分上屏后残留拼音，退格必须还能删 =====
                // 「只吃一部分音节」的候选排在第 9 个以后，而默认每页 5 个，
                // 所以得先翻一页。第 2 页是总第 6~10 个，第 5 个就是总第 10 个 =「尼」，只吃 1 个音节。
                Reset(); TypeText("nihaoma"); PressKey(0x22);      // PgDn 翻到第 2 页
                break;
            case 22:
                PressKey(0x35);                                    // 第 2 页第 5 个
                break;
            case 23:
                Check("尼", "部分上屏：nihaoma 翻页后选「尼」-> 只吃 1 个音节，剩下 hao ma 继续拼");
                // 关键的一条：部分上屏后 _caret 以前没跟着 _buffer 变短，
                // 退格会 Remove 越界抛异常 -> 异常被吞 -> 键漏给应用 -> 正文被删，
                // 同时残留拼音一个字都删不掉。
                PressKey(0x08);
                break;
            case 24:
                Check("尼", "部分上屏后退格 -> 删的是残留拼音，不会漏到正文里删字");
                Record(BarVisible(), "残留拼音还在，输入条还开着");
                PressKey(0x1B);                                    // Esc 收尾
                break;
            case 25:
                Check("尼", "Esc 之后正文依然是「尼」");
                Record(!BarVisible(), "Esc 关闭输入条");
                _timer.Stop();
                _form.BeginInvoke((MethodInvoker)delegate { _form.Close(); });
                break;
        }
        _step++;
        if (_step <= 25) ForwardFocus();
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;

    /// <summary>
    /// 把焦点抢回来。
    /// 光靠 Form.Activate() 是不够的 —— Windows 有"前台锁定"：如果别的程序
    /// （实测微信最典型）正占着前台，Activate() 会**静默失败**，SendInput 就打偏了，
    /// 测试会莫名其妙全挂。这里用 AttachThreadInput 把自己的输入队列临时挂到
    /// 当前前台线程上，此时 SetForegroundWindow 才会真的生效。
    /// </summary>
    static void ForwardFocus()
    {
        try
        {
            IntPtr h = _form.Handle;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != myThread)
                attached = AttachThreadInput(myThread, fgThread, true);
            try
            {
                // 先把窗口"顶到最上层再放下来"：Windows 对 SetForegroundWindow
                // 有一堆拒绝条件，这一步能让它认为我们确实是用户想看的窗口。
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                ShowWindow(h, 9);            // SW_RESTORE
                SetForegroundWindow(h);
                BringWindowToTop(h);
                // 微信这类 Qt 程序会死守前台，SetForegroundWindow 会被静默拒绝；
                // SwitchToThisWindow 是更老的强制切换接口，通常还能得手。
                SwitchToThisWindow(h, true);
                _form.Activate();
                _box.Focus();
            }
            finally
            {
                if (attached) AttachThreadInput(myThread, fgThread, false);
            }
        }
        catch { }
    }

    static void Reset()
    {
        _box.Text = "";
    }

    static void Check(string expected, string what)
    {
        string actual = _box.Text == null ? "" : _box.Text.Trim();
        bool ok = string.Equals(actual, expected, StringComparison.Ordinal);
        Record(ok, what);
        if (!ok)
            _report.Append("       期望「").Append(expected).Append("」  实际「").Append(actual).Append("」\n");
    }

    static void Record(bool ok, string what)
    {
        if (ok) { _report.Append("  [OK]   ").Append(what).Append('\n'); }
        else { _fail++; _report.Append("  [FAIL] ").Append(what).Append('\n'); }
        try { _hint.Text = (ok ? "OK  " : "FAIL  ") + what; } catch { }
    }

    // ---------- 合成按键 ----------

    static void TypeText(string s)
    {
        foreach (char c in s)
        {
            if (c >= 'a' && c <= 'z') PressKey(0x41 + (c - 'a'));
            else if (c >= '0' && c <= '9') PressKey(0x30 + (c - '0'));
        }
    }

    /// <summary>
    /// 模拟"按住不放"：连发 count 次 KEYDOWN（中间不发 KEYUP），最后补一次 KEYUP。
    /// Windows 的按键自动重复正是这个形态 —— 用它才能测出"按住退格只删一个字符"这类 bug。
    /// </summary>
    static void RepeatKey(int vk, int count)
    {
        for (int i = 0; i < count; i++)
        {
            SendVk(vk, false);
            Application.DoEvents();
            System.Threading.Thread.Sleep(40);
        }
        SendVk(vk, true);
        Application.DoEvents();
        System.Threading.Thread.Sleep(40);
    }

    static void PressKey(int vk)
    {
        SendVk(vk, false);
        SendVk(vk, true);
        Application.DoEvents();
        System.Threading.Thread.Sleep(35);
    }

    static void SendVk(int vk, bool up)
    {
        INPUT[] arr = new INPUT[1];
        arr[0].type = Native.INPUT_KEYBOARD;
        arr[0].u.ki.wVk = (ushort)vk;
        arr[0].u.ki.wScan = 0;
        arr[0].u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
        arr[0].u.ki.time = 0;
        arr[0].u.ki.dwExtraInfo = IntPtr.Zero;
        Native.SendInput(1, arr, Marshal.SizeOf(typeof(INPUT)));
    }
}
