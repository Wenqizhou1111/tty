using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using MyIme.Shared;

namespace MyIme.Settings
{
    // ====================================================================
    //  杂项工具
    // ====================================================================

    public static class AppUtil
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "MyIME";

        public static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { Log.Warn("打开文件夹失败: " + ex.Message); }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                Process.Start(url);
            }
            catch (Exception ex) { Log.Warn("打开网址失败: " + ex.Message); }
        }

        public static bool CoreRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("MyIme.Core");
                bool any = ps != null && ps.Length > 0;
                if (ps != null) for (int i = 0; i < ps.Length; i++) ps[i].Dispose();
                return any;
            }
            catch { return false; }
        }

        /// <summary>读注册表里的开机启动项。</summary>
        public static bool ReadAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValueName);
                    string s = v as string;
                    return !string.IsNullOrEmpty(s);
                }
            }
            catch { return false; }
        }

        /// <summary>写/删注册表里的开机启动项：值形如 "D:\...\MyIme.Core.exe"（带引号）。</summary>
        public static bool ApplyAutoStart(bool on, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) { error = "打不开注册表的启动项"; return false; }
                    if (on)
                    {
                        k.SetValue(RunValueName, "\"" + AppPaths.CoreExe + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warn("写开机启动项失败: " + ex.Message);
                return false;
            }
        }

        public static int CountUserWords()
        {
            try
            {
                string path = AppPaths.UserWordsFile;
                if (!File.Exists(path)) return 0;
                int n = 0;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string s = lines[i].Trim();
                    if (s.Length == 0 || s[0] == '#') continue;
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }
    }

    // ====================================================================
    //  页面基类
    // ====================================================================

    public class PageBase : UserControl, IThemed
    {
        public SettingsForm Host;
        public AppSettings Cfg;

        protected readonly List<Control> Blocks = new List<Control>();
        bool _built;

        public const int Gap = 14;
        public const int CardWidth = 620;

        public PageBase()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            Font = Theme.UI(9f, FontStyle.Regular);
        }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }

        /// <summary>建界面。</summary>
        public virtual void Build() { }

        /// <summary>把界面上的值读回 AppSettings（不落盘）。</summary>
        public virtual void Collect() { }

        /// <summary>设置保存成功之后的钩子（写注册表之类的副作用）。</summary>
        public virtual void OnSaved() { }

        protected void FinishBuild()
        {
            _built = true;
            RelayoutNow();
        }

        /// <summary>竖直堆叠所有块，并算出页面高度。</summary>
        public void RelayoutNow()
        {
            if (!_built) return;
            int w = ClientSize.Width;
            if (w <= 60) w = CardWidth;
            int y = 0;
            for (int i = 0; i < Blocks.Count; i++)
            {
                Control c = Blocks[i];
                NoticeBar nb = c as NoticeBar;
                // 用 Shown 而不是 Visible：窗口还没显示时 Visible 拿到的是"有效可见性"，恒为 false
                if (nb != null && !nb.Shown) continue;
                c.Left = 0;
                c.Width = w;
                if (nb != null) c.Height = nb.MeasureHeight(w);
                c.Top = y;
                y += c.Height + Gap;
            }
            Height = Math.Max(160, y + 6);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RelayoutNow();
        }

        protected void Add(Control c)
        {
            Blocks.Add(c);
            Controls.Add(c);
        }

        protected void Notify()
        {
            if (Host != null) Host.NotifyChanged();
        }

        protected void PageChanged(object sender, EventArgs e)
        {
            Notify();
        }

        // ---------------- 基础排布 ----------------

        protected CardPanel Card()
        {
            CardPanel c = new CardPanel();
            c.Width = CardWidth;
            c.Height = 60;
            Add(c);
            return c;
        }

        protected ThemedLabel Title(Control p, string text, int y)
        {
            ThemedLabel l = new ThemedLabel();
            l.Role = ThemedLabel.RoleTitle;
            l.Text = text;
            l.Location = new Point(18, y);
            p.Controls.Add(l);
            return l;
        }

        protected new ThemedLabel Text(Control p, string text, int x, int y)
        {
            ThemedLabel l = new ThemedLabel();
            l.Role = ThemedLabel.RoleBody;
            l.Text = text;
            l.Location = new Point(x, y);
            p.Controls.Add(l);
            return l;
        }

        protected ThemedLabel SubText(Control p, string text, int x, int y)
        {
            ThemedLabel l = new ThemedLabel();
            l.Role = ThemedLabel.RoleSub;
            l.Text = text;
            l.Location = new Point(x, y);
            p.Controls.Add(l);
            return l;
        }

        /// <summary>会自动换行的次要说明。</summary>
        protected ThemedLabel SubWrap(Control p, string text, int x, int y, int width)
        {
            ThemedLabel l = SubText(p, text, x, y);
            l.MaximumSize = new Size(Math.Max(60, width), 0);
            return l;
        }

        /// <summary>右对齐的定宽标签（AutoSize 关掉，锚右边）。</summary>
        protected ThemedLabel RightLabel(Control p, string text, int y, int width, int rightMargin)
        {
            ThemedLabel l = new ThemedLabel();
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.Text = text;
            l.Size = new Size(width, 22);
            PlaceRight(p, l, rightMargin);
            l.Top = y;
            return l;
        }

        protected void HLine(CardPanel c, int y)
        {
            Divider d = new Divider();
            d.SetBounds(18, y, Math.Max(20, c.Width - 36), 1);
            d.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            c.Controls.Add(d);
        }

        protected void PlaceRight(Control parent, Control child, int rightMargin)
        {
            child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            child.Left = Math.Max(0, parent.Width - rightMargin - child.Width);
            parent.Controls.Add(child);
        }

        protected void Stretch(Control parent, Control child, int x, int y, int height, int rightMargin)
        {
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            child.SetBounds(x, y, Math.Max(60, parent.Width - x - rightMargin), height);
            parent.Controls.Add(child);
        }

        // ---------------- 常用行 ----------------

        protected int RowSwitch(CardPanel c, int y, string title, string sub, bool value,
            EventHandler changed, out Switch sw)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 14));
            if (hasSub) SubWrap(c, sub, 18, y + 30, c.Width - 180);

            Switch s = new Switch();
            s.SetCheckedSilent(value);
            PlaceRight(c, s, 18);
            s.Top = y + 11;
            if (changed != null) s.CheckedChanged += changed;
            s.CheckedChanged += PageChanged;
            sw = s;
            return y + (hasSub ? 58 : 46);
        }

        protected int RowCheck(CardPanel c, int y, string text, string sub, bool value,
            EventHandler changed, out FlatCheck chk)
        {
            FlatCheck f = new FlatCheck();
            f.Text = text;
            f.SetCheckedSilent(value);
            f.SetBounds(18, y, Math.Max(120, c.Width - 36), 24);
            f.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            c.Controls.Add(f);
            if (changed != null) f.CheckedChanged += changed;
            f.CheckedChanged += PageChanged;
            chk = f;
            y += 26;
            if (!string.IsNullOrEmpty(sub))
            {
                SubWrap(c, sub, 45, y, c.Width - 80);
                y += 26;
            }
            return y;
        }

        protected int RowNumber(CardPanel c, int y, string title, string sub, int value,
            int min, int max, int step, EventHandler changed, out NumberBox box)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 14));
            if (hasSub) SubWrap(c, sub, 18, y + 30, c.Width - 200);

            NumberBox nb = new NumberBox();
            nb.Min = min;
            nb.Max = max;
            nb.Step = step;
            nb.Size = new Size(104, 34);
            nb.Value = value;
            PlaceRight(c, nb, 18);
            nb.Top = y + 6;
            if (changed != null) nb.ValueChanged += changed;
            nb.ValueChanged += PageChanged;
            box = nb;
            return y + (hasSub ? 58 : 46);
        }

        protected int RowSlider(CardPanel c, int y, string title, string sub, double value,
            double min, double max, double step, string format, EventHandler changed,
            out Slider slider, out ThemedLabel valueLabel)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 12));
            if (hasSub) SubWrap(c, sub, 18, y + 30, c.Width - 330);

            ThemedLabel vl = new ThemedLabel();
            vl.AutoSize = false;
            vl.TextAlign = ContentAlignment.MiddleRight;
            vl.Text = value.ToString(format);
            vl.Size = new Size(56, 30);
            PlaceRight(c, vl, 18);
            vl.Top = y + 8;

            Slider sl = new Slider();
            sl.Min = min;
            sl.Max = max;
            sl.Step = step;
            sl.Size = new Size(190, 30);
            sl.SetValueSilent(value);
            PlaceRight(c, sl, 18 + 56 + 8);
            sl.Top = y + 8;

            Slider capturedSlider = sl;
            ThemedLabel capturedLabel = vl;
            sl.ValueChanged += delegate
            {
                capturedLabel.Text = capturedSlider.Value.ToString(format);
            };
            if (changed != null) sl.ValueChanged += changed;
            sl.ValueChanged += PageChanged;

            slider = sl;
            valueLabel = vl;
            return y + (hasSub ? 58 : 48);
        }

        protected int RowInput(CardPanel c, int y, string title, string sub, string value, string hint,
            int boxWidth, bool password, EventHandler changed, out InputBox box)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 14));
            if (hasSub) SubWrap(c, sub, 18, y + 30, c.Width - boxWidth - 46);

            InputBox ib = new InputBox();
            ib.Size = new Size(boxWidth, 34);
            ib.Text = value == null ? "" : value;
            ib.Password = password;
            ib.Hint = hint;
            PlaceRight(c, ib, 18);
            ib.Top = y + 6;
            if (changed != null) ib.TextChanged += changed;
            ib.TextChanged += PageChanged;
            box = ib;
            return y + (hasSub ? 58 : 46);
        }

        protected int RowSelect(CardPanel c, int y, string title, string sub, List<string> items,
            int index, int boxWidth, EventHandler changed, out SelectBox box)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 14));
            if (hasSub) SubWrap(c, sub, 18, y + 30, c.Width - boxWidth - 46);

            SelectBox sb = new SelectBox();
            sb.Size = new Size(boxWidth, 34);
            sb.SetItems(items);
            sb.SelectedIndex = index;
            PlaceRight(c, sb, 18);
            sb.Top = y + 6;
            if (changed != null) sb.SelectedIndexChanged += changed;
            sb.SelectedIndexChanged += PageChanged;
            box = sb;
            return y + (hasSub ? 58 : 46);
        }

        protected int RowHotkey(CardPanel c, int y, string title, string sub, HotkeyBinding binding,
            EventHandler changed, out HotkeyBox box, out GhostButton clear)
        {
            bool hasSub = !string.IsNullOrEmpty(sub);
            Text(c, title, 18, y + (hasSub ? 8 : 16));
            if (hasSub) SubWrap(c, sub, 18, y + 33, c.Width - 330);

            GhostButton cl = new GhostButton();
            cl.Text = "清除";
            cl.Size = new Size(58, 30);
            PlaceRight(c, cl, 18);
            cl.Top = y + (hasSub ? 10 : 12);

            HotkeyBox hb = new HotkeyBox();
            hb.Size = new Size(200, 34);
            hb.SetBindingSilent(binding == null ? new HotkeyBinding(0, 0) : binding);
            PlaceRight(c, hb, 18 + 58 + 10);
            hb.Top = y + (hasSub ? 8 : 10);
            if (changed != null) hb.BindingChanged += changed;
            hb.BindingChanged += PageChanged;

            HotkeyBox capturedBox = hb;
            cl.Click += delegate { capturedBox.Clear(); };

            box = hb;
            clear = cl;
            return y + (hasSub ? 60 : 56);
        }

        protected void OpenFolderOrToast(string path, string what)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                if (Host != null) Host.ShowToast("找不到" + what, ToastLabel.KindError);
                return;
            }
            AppUtil.OpenFolder(path);
        }
    }

    // ====================================================================
    //  1. 常规
    // ====================================================================

    public class GeneralPage : PageBase
    {
        Switch _autoStart;
        Switch _tray;
        Switch _sound;
        Switch _notify;
        SegmentedControl _theme;
        ThemedLabel _autoStartPath;

        public override void Build()
        {
            // ---------------- 启动与托盘 ----------------
            CardPanel c1 = Card();
            int y = 16;
            Title(c1, "启动与托盘", y);
            y = 50;

            Switch sw;
            y = RowSwitch(c1, y, "开机自动启动",
                "写进注册表的启动项（项名 MyIME），值就是下面这个路径。",
                AppUtil.ReadAutoStart(), null, out sw);
            _autoStart = sw;
            _autoStart.CheckedChanged += StartupChanged;

            _autoStartPath = SubText(c1, StartupText(), 40, y - 24);

            y = RowSwitch(c1, y, "显示托盘图标", null, Cfg.General.ShowTrayIcon, null, out sw);
            _tray = sw;
            y = RowSwitch(c1, y, "切换模式时提示音", null, Cfg.General.SoundOnModeSwitch, null, out sw);
            _sound = sw;
            y = RowSwitch(c1, y, "锁定或自动锁定时弹提示",
                "输入法被锁定、或从锁定里恢复时，在右下角弹一个气泡。",
                Cfg.General.ShowNotificationOnLock, null, out sw);
            _notify = sw;
            c1.Height = y + 12;

            // ---------------- 外观 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "外观", y);
            y = 50;

            Text(c2, "主题", 18, y + 14);
            SegmentedControl seg = new SegmentedControl();
            seg.Size = new Size(240, 32);
            seg.SetItems(new string[] { "跟随系统", "浅色", "深色" });
            seg.SetIndexSilent(ThemeIndex(Cfg.General.Theme));
            PlaceRight(c2, seg, 18);
            seg.Top = y + 8;
            seg.SelectedIndexChanged += ThemeChanged;
            seg.SelectedIndexChanged += PageChanged;
            _theme = seg;
            y += 48;

            SubWrap(c2, "「跟随系统」= 跟着 Windows 的深色模式走（设置 → 个性化 → 颜色）。换主题立刻生效，点右下角「保存」后长期记住。",
                18, y, c2.Width - 36);
            y += 34;
            c2.Height = y + 12;

            FinishBuild();
        }

        string StartupText()
        {
            bool on = _autoStart != null && _autoStart.Checked;
            return on ? "当前：" + AppPaths.CoreExe : "当前：未设置开机启动";
        }

        void StartupChanged(object sender, EventArgs e)
        {
            if (_autoStartPath != null) _autoStartPath.Text = StartupText();
        }

        static int ThemeIndex(string mode)
        {
            string m = Theme.Normalize(mode);
            if (m == "light") return 1;
            if (m == "dark") return 2;
            return 0;
        }

        static string ThemeKey(int index)
        {
            if (index == 1) return "light";
            if (index == 2) return "dark";
            return "auto";
        }

        void ThemeChanged(object sender, EventArgs e)
        {
            if (_theme == null || Host == null) return;
            Host.ApplyThemeNow(ThemeKey(_theme.SelectedIndex));
        }

        public override void Collect()
        {
            if (Cfg == null) return;
            Cfg.General.StartWithWindows = _autoStart != null && _autoStart.Checked;
            Cfg.General.ShowTrayIcon = _tray != null && _tray.Checked;
            Cfg.General.SoundOnModeSwitch = _sound != null && _sound.Checked;
            Cfg.General.ShowNotificationOnLock = _notify != null && _notify.Checked;
            Cfg.General.Theme = _theme == null ? "auto" : ThemeKey(_theme.SelectedIndex);
        }

        public override void OnSaved()
        {
            bool want = Cfg.General.StartWithWindows;
            if (want == AppUtil.ReadAutoStart()) return;
            string err;
            if (!AppUtil.ApplyAutoStart(want, out err))
            {
                if (Host != null) Host.ShowToast("开机启动设置失败：" + err, ToastLabel.KindError);
            }
        }
    }

    // ====================================================================
    //  2. 输入
    // ====================================================================

    public class InputPage : PageBase
    {
        NumberBox _pageSize;
        NumberBox _fontSize;
        NumberBox _predictCount;
        NumberBox _enMin;
        NumberBox _enMax;

        Switch _showPinyin;
        Switch _showIndex;
        Switch _showPage;
        Switch _smartPunct;
        Switch _jianpin;
        Switch _autoUnique;
        Switch _spaceCommit;
        Switch _predict;

        Switch _enStart;
        Switch _enCompletion;

        readonly FlatCheck[] _fuzzy = new FlatCheck[7];

        InputBox _test;
        CandidateStrip _strip;
        Lexicon _lex;
        PinyinEngine _engine;

        static readonly string[] FuzzyNames = new string[] {
            "zh 对 z", "ch 对 c", "sh 对 s", "n 对 l",
            "an 对 ang", "in 对 ing", "uan 对 uang"
        };

        public override void Build()
        {
            Switch sw;
            NumberBox nb;

            // ---------------- 候选 ----------------
            CardPanel c1 = Card();
            int y = 16;
            Title(c1, "候选窗", y);
            y = 50;

            y = RowNumber(c1, y, "每页候选个数", "输入条上一屏显示几个候选，5 到 9 个。",
                Cfg.Input.PageSize, 5, 9, 1, null, out nb);
            _pageSize = nb;

            y = RowNumber(c1, y, "候选窗字号", "候选条上汉字的磅值，12 到 28。",
                Cfg.Ui.FontSize, 12, 28, 1, null, out nb);
            _fontSize = nb;

            y = RowSwitch(c1, y, "显示拼音", "在输入条上显示正在输入的拼音串。",
                Cfg.Input.ShowPinyinInBar, null, out sw);
            _showPinyin = sw;

            y = RowSwitch(c1, y, "显示序号", "候选前面显示 1. 2. 3.，方便数字键选字。",
                Cfg.Ui.ShowIndex, null, out sw);
            _showIndex = sw;

            y = RowSwitch(c1, y, "显示页码", "候选多于一页时，显示「第 1/3 页」。",
                Cfg.Ui.ShowPageIndicator, null, out sw);
            _showPage = sw;

            y = RowSwitch(c1, y, "中文标点", "把 , . ? ! 自动换成 ，。？！；引号成对。",
                Cfg.Input.SmartPunctuation, null, out sw);
            _smartPunct = sw;
            c1.Height = y + 12;

            // ---------------- 输入习惯 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "输入习惯", y);
            y = 50;

            y = RowSwitch(c2, y, "简拼", "打首字母就能出词，比如 nh → 你好。",
                Cfg.Input.Jianpin, null, out sw);
            _jianpin = sw;

            y = RowSwitch(c2, y, "唯一候选自动上屏", "只有一个候选时直接上屏，不用再敲空格。",
                Cfg.Input.AutoCommitUnique, null, out sw);
            _autoUnique = sw;

            y = RowSwitch(c2, y, "空格上屏首候选", "敲空格就把第一个候选送上去。",
                Cfg.Input.AutoCommitOnSpace, null, out sw);
            _spaceCommit = sw;

            y = RowSwitch(c2, y, "上屏后联想", "上屏之后，接着给几个常用的后续词。",
                Cfg.Input.PredictEnabled, null, out sw);
            _predict = sw;

            y = RowNumber(c2, y, "联想词个数", null, Cfg.Input.PredictCount, 1, 9, 1, null, out nb);
            _predictCount = nb;
            c2.Height = y + 12;

            // ---------------- 模糊音 ----------------
            CardPanel c3 = Card();
            y = 16;
            Title(c3, "模糊音", y);
            SubWrap(c3, "普通话不分的音可以在这里打开，开了以后两种拼法都能打出同一个字。",
                18, y + 26, c3.Width - 36);
            y = 60;

            for (int i = 0; i < 7; i++)
            {
                int col = i % 2;
                int rowIdx = i / 2;
                FlatCheck chk = new FlatCheck();
                chk.Text = FuzzyNames[i];
                chk.SetBounds(18 + col * 288, y + rowIdx * 30, 272, 24);
                chk.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                chk.SetCheckedSilent(GetFuzzy(Cfg.Input, i));
                chk.CheckedChanged += PageChanged;
                c3.Controls.Add(chk);
                _fuzzy[i] = chk;
            }
            y += 4 * 30 + 4;
            c3.Height = y + 12;

            // ---------------- 英文模式 ----------------
            CardPanel c4 = Card();
            y = 16;
            Title(c4, "英文模式", y);
            y = 50;

            y = RowSwitch(c4, y, "启动即为英文", "输入法起来的时候是英文状态。",
                Cfg.Input.StartInEnglish, null, out sw);
            _enStart = sw;

            y = RowSwitch(c4, y, "英文单词补全", "打英文时用本地词库补全，不联网。",
                Cfg.Input.EnglishCompletion, null, out sw);
            _enCompletion = sw;

            y = RowNumber(c4, y, "最少几个字母开始补全", null,
                Cfg.Input.EnglishMinPrefix, 1, 5, 1, null, out nb);
            _enMin = nb;

            y = RowNumber(c4, y, "最多几个建议", null,
                Cfg.Input.EnglishMaxSuggestions, 1, 10, 1, null, out nb);
            _enMax = nb;
            c4.Height = y + 12;

            // ---------------- 测试输入 ----------------
            CardPanel c5 = Card();
            y = 16;
            Title(c5, "测试输入", y);
            SubWrap(c5, "在下面敲拼音，立刻能看到候选长什么样 —— 用的就是上面这些设置（候选数、序号、页码、简拼、模糊音全都算数）。这里只是预览，不会真的上屏。",
                18, y + 26, c5.Width - 36);
            y = 72;

            InputBox box = new InputBox();
            box.Hint = "在这里敲拼音，比如 nihao / zhongguo / nh";
            Stretch(c5, box, 18, y, 36, 18);
            box.TextChanged += delegate { UpdatePreview(); };
            _test = box;
            y += 46;

            CandidateStrip strip = new CandidateStrip();
            Stretch(c5, strip, 18, y, 128, 18);
            _strip = strip;
            y += 132;

            c5.Height = y + 12;

            FinishBuild();
            UpdatePreview();
        }

        static bool GetFuzzy(InputSettings s, int i)
        {
            switch (i)
            {
                case 0: return s.FuzzyZhZ;
                case 1: return s.FuzzyChC;
                case 2: return s.FuzzyShS;
                case 3: return s.FuzzyNL;
                case 4: return s.FuzzyAnAng;
                case 5: return s.FuzzyInIng;
                default: return s.FuzzyUanUang;
            }
        }

        static void SetFuzzy(InputSettings s, int i, bool on)
        {
            switch (i)
            {
                case 0: s.FuzzyZhZ = on; break;
                case 1: s.FuzzyChC = on; break;
                case 2: s.FuzzyShS = on; break;
                case 3: s.FuzzyNL = on; break;
                case 4: s.FuzzyAnAng = on; break;
                case 5: s.FuzzyInIng = on; break;
                default: s.FuzzyUanUang = on; break;
            }
        }

        void EnsureEngine()
        {
            if (_engine != null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                _lex = new Lexicon();
                _lex.LoadAll();
                _engine = new PinyinEngine(_lex, Cfg.Input);
            }
            catch (Exception ex)
            {
                Log.Error("测试输入：词库加载失败", ex);
                _engine = null;
            }
            finally { Cursor = Cursors.Default; }
        }

        void UpdatePreview()
        {
            if (_strip == null) return;
            Collect();

            string raw = _test == null ? "" : _test.Text.Trim();
            _strip.Items.Clear();

            if (raw.Length == 0)
            {
                _strip.Code = "";
                _strip.Footer = "敲几个字母试试，候选会实时出现在这里。";
                _strip.Invalidate();
                return;
            }

            EnsureEngine();
            if (_engine == null)
            {
                _strip.Code = "";
                _strip.Footer = "词库没能加载，测试输入暂时用不了（不影响输入法本身）。";
                _strip.Invalidate();
                return;
            }

            _engine.UpdateSettings(Cfg.Input);
            Composition comp = _engine.Compose(raw);
            _strip.Code = comp.SyllableText;
            _strip.ShowIndex = Cfg.Ui.ShowIndex;

            int n = Math.Min(comp.Candidates.Count, Math.Max(1, Cfg.Input.PageSize));
            for (int i = 0; i < n; i++) _strip.Items.Add(comp.Candidates[i].Text);

            StringBuilder sb = new StringBuilder();
            sb.Append("候选 ").Append(comp.Candidates.Count).Append(" 个");
            if (Cfg.Ui.ShowPageIndicator)
                sb.Append(" · 第 1/").Append(Math.Max(1, comp.PageCount)).Append(" 页");
            if (Cfg.Input.PredictEnabled) sb.Append(" · 联想开");
            _strip.Footer = sb.ToString();
            _strip.Invalidate();
        }

        public override void Collect()
        {
            if (Cfg == null) return;

            InputSettings s = Cfg.Input;
            if (_pageSize != null) s.PageSize = _pageSize.Value;
            if (_showPinyin != null) s.ShowPinyinInBar = _showPinyin.Checked;
            if (_smartPunct != null) s.SmartPunctuation = _smartPunct.Checked;
            if (_jianpin != null) s.Jianpin = _jianpin.Checked;
            if (_autoUnique != null) s.AutoCommitUnique = _autoUnique.Checked;
            if (_spaceCommit != null) s.AutoCommitOnSpace = _spaceCommit.Checked;
            if (_predict != null) s.PredictEnabled = _predict.Checked;
            if (_predictCount != null) s.PredictCount = _predictCount.Value;
            if (_enStart != null) s.StartInEnglish = _enStart.Checked;
            if (_enCompletion != null) s.EnglishCompletion = _enCompletion.Checked;
            if (_enMin != null) s.EnglishMinPrefix = _enMin.Value;
            if (_enMax != null) s.EnglishMaxSuggestions = _enMax.Value;
            for (int i = 0; i < 7; i++)
                if (_fuzzy[i] != null) SetFuzzy(s, i, _fuzzy[i].Checked);

            UiSettings u = Cfg.Ui;
            if (_fontSize != null) u.FontSize = _fontSize.Value;
            if (_showIndex != null) u.ShowIndex = _showIndex.Checked;
            if (_showPage != null) u.ShowPageIndicator = _showPage.Checked;
        }
    }

    // ====================================================================
    //  3. 快捷键
    // ====================================================================

    public class HotkeyPage : PageBase
    {
        FlatCheck _shiftTap;
        HotkeyBox _hkSwitch;
        HotkeyBox _hkAi;
        HotkeyBox _hkSettings;
        HotkeyBox _hkPolish;
        HotkeyBox _hkLock;
        NoticeBar _conflict;

        public override void Build()
        {
            FlatCheck chk;
            HotkeyBox hb;
            GhostButton cl;

            // ---------------- 输入法 ----------------
            CardPanel c1 = Card();
            int y = 16;
            Title(c1, "输入法", y);
            y = 50;
            y = RowCheck(c1, y, "轻按 Shift 切换中英文",
                "输入法的老习惯：单独轻按一下 Shift 就切中英，按住 Shift 打字不受影响。",
                Cfg.Hotkeys.ShiftTapTogglesChinese, null, out chk);
            _shiftTap = chk;
            c1.Height = y + 12;

            // ---------------- 快捷键 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "全局快捷键", y);
            SubWrap(c2, "点一下右边的框，然后直接按下想用的组合键就录进去了；按 Esc 取消，按退格键清空。建议都带上 Ctrl 或 Alt，免得和别的软件打架。",
                18, y + 26, c2.Width - 36);
            y = 76;

            y = RowHotkey(c2, y, "切换中英文", "中英文状态来回切。",
                Cfg.Hotkeys.ToggleChineseEnglish, ConflictChanged, out hb, out cl);
            _hkSwitch = hb;

            y = RowHotkey(c2, y, "打开 AI 面板", "直接把 AI 写作面板叫出来。",
                Cfg.Hotkeys.ToggleAiPanel, ConflictChanged, out hb, out cl);
            _hkAi = hb;

            y = RowHotkey(c2, y, "打开设置", "就是现在这个窗口。",
                Cfg.Hotkeys.OpenSettings, ConflictChanged, out hb, out cl);
            _hkSettings = hb;

            y = RowHotkey(c2, y, "AI 润色选中文字", "选中一段文字，按一下就润色并替换。",
                Cfg.Hotkeys.AiPolishSelection, ConflictChanged, out hb, out cl);
            _hkPolish = hb;

            y = RowHotkey(c2, y, "锁定／解锁输入法", "和「游戏锁定」那页是同一个键，按一下锁，再按一下解。",
                Cfg.GameLock.ToggleKey, ConflictChanged, out hb, out cl);
            _hkLock = hb;

            c2.Height = y + 12;

            NoticeBar nb = new NoticeBar();
            nb.Visible = false;
            nb.VisibilityChanged += delegate { RelayoutNow(); };
            Add(nb);
            _conflict = nb;

            FinishBuild();
            CheckConflicts();
        }

        void ConflictChanged(object sender, EventArgs e)
        {
            CheckConflicts();
        }

        void CheckConflicts()
        {
            if (Cfg == null) return;
            Collect();

            string[] names = new string[] {
                "切换中英文", "打开 AI 面板", "打开设置", "AI 润色选中文字", "锁定／解锁输入法"
            };
            HotkeyBinding[] keys = new HotkeyBinding[] {
                Cfg.Hotkeys.ToggleChineseEnglish, Cfg.Hotkeys.ToggleAiPanel, Cfg.Hotkeys.OpenSettings,
                Cfg.Hotkeys.AiPolishSelection, Cfg.GameLock.ToggleKey
            };
            HotkeyBox[] boxes = new HotkeyBox[] { _hkSwitch, _hkAi, _hkSettings, _hkPolish, _hkLock };

            bool[] bad = new bool[5];
            List<string> msgs = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                for (int j = i + 1; j < 5; j++)
                {
                    if (keys[i] == null || keys[j] == null) continue;
                    if (!keys[i].IsSet || !keys[j].IsSet) continue;
                    if (!keys[i].SameAs(keys[j])) continue;
                    bad[i] = true;
                    bad[j] = true;
                    msgs.Add("「" + names[i] + "」和「" + names[j] + "」都是 "
                        + Native.HotkeyText(keys[i].Modifiers, keys[i].Key));
                }
            }

            for (int i = 0; i < 5; i++)
            {
                if (boxes[i] == null) continue;
                boxes[i].Conflict = bad[i];
                boxes[i].Invalidate();
            }

            if (_conflict == null) return;
            if (msgs.Count > 0)
            {
                _conflict.Show("快捷键冲突：" + string.Join("；", msgs.ToArray())
                    + "。同一个组合键只能给一个功能用，请换一个。", NoticeBar.KindWarn, 0);
            }
            else
            {
                _conflict.HideBar();
            }
        }

        public override void Collect()
        {
            if (Cfg == null) return;
            if (_shiftTap != null) Cfg.Hotkeys.ShiftTapTogglesChinese = _shiftTap.Checked;
            if (_hkSwitch != null) Cfg.Hotkeys.ToggleChineseEnglish = _hkSwitch.Binding;
            if (_hkAi != null) Cfg.Hotkeys.ToggleAiPanel = _hkAi.Binding;
            if (_hkSettings != null) Cfg.Hotkeys.OpenSettings = _hkSettings.Binding;
            if (_hkPolish != null) Cfg.Hotkeys.AiPolishSelection = _hkPolish.Binding;
            if (_hkLock != null) Cfg.GameLock.ToggleKey = _hkLock.Binding;
        }
    }

    // ====================================================================
    //  4. 游戏锁定
    // ====================================================================

    public class GameLockPage : PageBase
    {
        NoticeBar _intro;
        HotkeyBox _lockKey;
        Switch _autoFullscreen;
        Switch _autoGames;
        Switch _keepUnlock;
        Switch _notify;

        CardPanel _listCard;
        InputBox _addBox;
        GhostButton _addButton;
        ThemedLabel _listHint;
        ThemedLabel _listCount;
        ThemedLabel _coreState;
        readonly List<ProcessRow> _rows = new List<ProcessRow>();
        int _listTop = 74;

        public override void Build()
        {
            Switch sw;
            HotkeyBox hb;
            GhostButton cl;

            // ---------------- 开头把话说清楚 ----------------
            NoticeBar intro = new NoticeBar();
            intro.Show("锁定后输入法的键盘钩子会被整体卸载，所有按键原样传给游戏，输入法完全不介入 —— " +
                       "不会卡出候选框，也不会被反作弊误判。解锁之后再装回来，中间不会残留任何东西。",
                NoticeBar.KindInfo, 0);
            intro.VisibilityChanged += delegate { RelayoutNow(); };
            Add(intro);
            _intro = intro;

            // ---------------- 锁定热键 ----------------
            CardPanel c1 = Card();
            int y = 16;
            Title(c1, "锁定热键", y);
            y = 56;
            y = RowHotkey(c1, y, "锁定／解锁输入法", "默认 Ctrl+Alt+L。按一下锁定，再按一下解锁。",
                Cfg.GameLock.ToggleKey, null, out hb, out cl);
            _lockKey = hb;
            SubWrap(c1, "这个键和「快捷键」那页里的是同一个。锁定时如果勾了下面的「保留解锁热键」，它照样管用；不勾就只能右键托盘图标解锁。",
                18, y - 6, c1.Width - 36);
            y += 40;
            c1.Height = y + 12;

            // ---------------- 自动锁定 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "自动锁定", y);
            y = 50;
            y = RowSwitch(c2, y, "检测到全屏游戏时自动锁定",
                "前台窗口铺满整块屏幕时就自动锁定，退出全屏自动恢复。",
                Cfg.GameLock.AutoLockFullscreen, null, out sw);
            _autoFullscreen = sw;
            y = RowSwitch(c2, y, "检测到常见游戏进程时自动锁定",
                "命中下面名单里的进程就自动锁定。",
                Cfg.GameLock.AutoLockGames, null, out sw);
            _autoGames = sw;
            c2.Height = y + 12;

            // ---------------- 游戏进程名单 ----------------
            _listCard = Card();
            y = 16;
            Title(_listCard, "游戏进程名单", y);
            SubWrap(_listCard, "写进程名就行（不带 .exe 也认），比如 cs2、valorant、genshinimpact。名单里的游戏一起来，输入法就自动让路。",
                18, y + 26, _listCard.Width - 200);
            _listCount = RightLabel(_listCard, "", 16, 150, 18);
            _listTop = 74;

            _listHint = SubText(_listCard, "（名单现在是空的，不会因为进程名而自动锁定）", 18, _listTop);
            _listHint.Visible = false;

            InputBox addBox = new InputBox();
            addBox.Size = new Size(240, 34);
            addBox.Hint = "输入进程名，比如 cs2";
            PlaceRight(_listCard, addBox, 18 + 76 + 8);
            _addBox = addBox;

            GhostButton addBtn = new GhostButton();
            addBtn.Text = "添加";
            addBtn.Size = new Size(76, 32);
            PlaceRight(_listCard, addBtn, 18);
            addBtn.Click += delegate { AddProcess(); };
            _addButton = addBtn;

            _addBox.Inner.KeyDown += AddBoxKeyDown;

            // ---------------- 锁定期间 ----------------
            CardPanel c4 = Card();
            y = 16;
            Title(c4, "锁定期间", y);
            y = 50;
            y = RowSwitch(c4, y, "锁定时保留解锁热键",
                "锁定以后这个热键仍然有效，随时能解锁。关了的话只能从托盘图标解锁。",
                Cfg.GameLock.KeepUnlockHotkey, null, out sw);
            _keepUnlock = sw;
            y = RowSwitch(c4, y, "自动锁定时弹提示",
                "被自动锁定时弹一个气泡，免得你以为输入法坏了。",
                Cfg.GameLock.NotifyOnAutoLock, null, out sw);
            _notify = sw;
            c4.Height = y + 12;

            // ---------------- 当前状态 ----------------
            CardPanel c5 = Card();
            y = 16;
            Title(c5, "当前状态", y);

            GhostButton refresh = new GhostButton();
            refresh.Text = "刷新";
            refresh.Size = new Size(72, 30);
            PlaceRight(c5, refresh, 18);
            refresh.Top = 14;
            refresh.Click += delegate { RefreshCoreState(); };

            y = 50;
            SubWrap(c5, "锁定状态由输入法主程序自己维护（钩子装卸都在那边），设置界面不去碰它，所以这里只做说明：",
                18, y, c5.Width - 36);
            y += 42;

            SubWrap(c5, "· 未锁定：键盘钩子在工作，中文输入一切正常。\r\n" +
                        "· 已锁定：钩子被整体卸载，按键原样进游戏，候选窗不会出现。\r\n" +
                        "· 解锁：再按一次锁定热键，或者右键托盘图标选「解锁输入法」。",
                18, y, c5.Width - 36);
            y += 74;

            HLine(c5, y);
            y += 12;
            _coreState = Text(c5, "", 18, y);
            y += 30;
            c5.Height = y + 06;

            FinishBuild();
            RefreshCoreState();
            RebuildProcessList();
        }

        void AddBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AddProcess();
            }
        }

        void RefreshCoreState()
        {
            if (_coreState == null) return;
            bool running = AppUtil.CoreRunning();
            _coreState.Text = running
                ? "输入法主程序：正在运行 —— 这里的设置保存后立刻生效。"
                : "输入法主程序：没在运行 —— 设置会先存下来，下次启动输入法时生效。";
            _coreState.ForeColor = running ? Theme.Accent : Theme.SubText;
        }

        void AddProcess()
        {
            if (_addBox == null || Cfg == null) return;
            string name = _addBox.Text.Trim().ToLowerInvariant();
            if (name.Length == 0) return;

            int slash = name.LastIndexOf('\\');
            if (slash >= 0) name = name.Substring(slash + 1).Trim();
            if (name.EndsWith(".exe", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 4).Trim();
            if (name.Length == 0) return;

            if (Cfg.GameLock.GameProcesses.Contains(name))
            {
                if (Host != null) Host.ShowToast("名单里已经有 " + name + " 了", ToastLabel.KindError);
                return;
            }

            Cfg.GameLock.GameProcesses.Add(name);
            _addBox.Text = "";
            RebuildProcessList();
            Notify();
        }

        void RemoveProcess(string name)
        {
            if (Cfg == null) return;
            if (Cfg.GameLock.GameProcesses.Remove(name))
            {
                RebuildProcessList();
                Notify();
            }
        }

        void RebuildProcessList()
        {
            if (_listCard == null || Cfg == null) return;

            for (int i = 0; i < _rows.Count; i++)
            {
                _listCard.Controls.Remove(_rows[i]);
                _rows[i].Dispose();
            }
            _rows.Clear();

            List<string> list = Cfg.GameLock.GameProcesses;
            int y = _listTop;
            for (int i = 0; i < list.Count; i++)
            {
                ProcessRow row = new ProcessRow();
                row.ProcessName = list[i];
                string capturedName = list[i];
                row.DeleteClicked += delegate { RemoveProcess(capturedName); };
                row.SetBounds(18, y, Math.Max(120, _listCard.Width - 36), 36);
                row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _listCard.Controls.Add(row);
                _rows.Add(row);
                y += 36;
            }

            if (_listHint != null) _listHint.Visible = list.Count == 0;
            if (_listCount != null) _listCount.Text = "共 " + list.Count + " 个进程";

            if (list.Count > 0) y += 6;
            if (_addBox != null) _addBox.Top = y;
            if (_addButton != null) _addButton.Top = y + 1;
            y += 34 + 12;

            _listCard.Height = y + 14;
            RelayoutNow();
        }

        public override void Collect()
        {
            if (Cfg == null) return;
            if (_lockKey != null) Cfg.GameLock.ToggleKey = _lockKey.Binding;
            if (_autoFullscreen != null) Cfg.GameLock.AutoLockFullscreen = _autoFullscreen.Checked;
            if (_autoGames != null) Cfg.GameLock.AutoLockGames = _autoGames.Checked;
            if (_keepUnlock != null) Cfg.GameLock.KeepUnlockHotkey = _keepUnlock.Checked;
            if (_notify != null) Cfg.GameLock.NotifyOnAutoLock = _notify.Checked;
        }
    }

    // ====================================================================
    //  5. AI 写作
    // ====================================================================

    public class AiPage : PageBase
    {
        Switch _enabled;
        ThemedLabel _offHint;

        SelectBox _provider;
        ThemedLabel _note;
        LinkText _docLink;
        string _protocol = "openai";
        string _docUrl = "";

        InputBox _baseUrl;
        InputBox _model;
        InputBox _apiKey;
        FlatCheck _showKey;
        ThemedLabel _keyStatus;

        Slider _temp;
        ThemedLabel _tempValue;
        NumberBox _maxTokens;
        NumberBox _timeout;
        Switch _stream;

        InputBox _systemPrompt;
        SelectBox _preset;
        ThemedLabel _presetDesc;

        FlatCheck _showButton;
        FlatCheck _autoRead;
        FlatCheck _commandMode;

        InputBox _headers;
        NoticeBar _notice;

        readonly List<CardPanel> _bodyCards = new List<CardPanel>();

        static readonly string[] PresetNames = new string[] {
            "简洁", "正式", "口语", "专业", "亲切", "书面", "强力纠正", "提炼要点"
        };
        static readonly string[] PresetIds = new string[] {
            "concise", "formal", "casual", "professional", "friendly", "written", "grammar", "summary"
        };
        static readonly string[] PresetDesc = new string[] {
            "删掉啰嗦的地方，只说重点",
            "适合邮件、公文、报告",
            "像平时聊天那样自然",
            "术语准确，逻辑清楚",
            "温和有礼，不冷冰冰",
            "通顺的书面表达，适合文章",
            "只改错，不动风格",
            "压缩成更短的一段话"
        };

        public override void Build()
        {
            Switch sw;
            NumberBox nb;
            InputBox ib;
            SelectBox sb;

            _protocol = Cfg.Ai.Provider;

            // ---------------- 总开关 ----------------
            CardPanel c0 = Card();
            int y = 16;
            y = RowSwitch(c0, y, "启用 AI 写作助手",
                "打开以后，输入条上会出现 AI 按钮，也可以按快捷键叫出面板。关着的时候下面这些设置先存着，不生效。",
                Cfg.Ai.Enabled, MasterChanged, out sw);
            _enabled = sw;
            _offHint = SubText(c0, "", 18, y - 2);
            _offHint.Role = ThemedLabel.RoleSub;
            y += 24;
            c0.Height = y + 10;

            // ---------------- 服务商 ----------------
            CardPanel c1 = Card();
            _bodyCards.Add(c1);
            y = 16;
            Title(c1, "服务商", y);
            y = 50;

            List<ProviderInfo> all = ProviderCatalog.All;
            List<string> names = new List<string>();
            for (int i = 0; i < all.Count; i++) names.Add(all[i].Name);

            y = RowSelect(c1, y, "接口服务商", "选中以后会自动填好接口地址和默认模型，也可以自己改。",
                names, DetectProviderIndex(), 300, ProviderChanged, out sb);
            _provider = sb;

            _note = SubWrap(c1, "", 18, y, c1.Width - 36);
            y += 28;

            LinkText link = new LinkText();
            link.Text = "如何获取 API Key";
            link.Size = new Size(180, 24);
            link.Location = new Point(18, y);
            link.Click += delegate { OnDocLink(); };
            c1.Controls.Add(link);
            _docLink = link;
            y += 32;
            c1.Height = y + 10;

            // ---------------- 账号 ----------------
            CardPanel c2 = Card();
            _bodyCards.Add(c2);
            y = 16;
            Title(c2, "账号", y);
            y = 50;

            y = RowInput(c2, y, "API 地址", "填到 /v1 为止，后面不用带 /chat/completions。",
                Cfg.Ai.BaseUrl, "https://api.deepseek.com/v1", 300, false, null, out ib);
            _baseUrl = ib;

            y = RowInput(c2, y, "模型", "按服务商的文档填，比如 deepseek-chat、glm-4-flash。",
                Cfg.Ai.Model, "deepseek-chat", 300, false, null, out ib);
            _model = ib;

            Text(c2, "API Key", 18, y + 16);
            InputBox key = new InputBox();
            key.Size = new Size(300, 34);
            key.Password = true;
            key.Hint = "sk-...";
            PlaceRight(c2, key, 18);
            key.Top = y + 6;
            key.Text = Host == null ? "" : Host.PendingApiKey;

            FlatCheck show = new FlatCheck();
            show.Text = "显示";
            show.Size = new Size(72, 24);
            PlaceRight(c2, show, 18 + 300 + 10);
            show.Top = y + 11;
            show.CheckedChanged += delegate { key.Password = !show.Checked; };
            _apiKey = key;
            _showKey = show;
            y += 46;

            SubWrap(c2, "Key 用 Windows 的 DPAPI 按当前用户加密后再写进 settings.json，文件里不会出现明文。",
                18, y, c2.Width - 330);
            _keyStatus = RightLabel(c2, "", y + 2, 300, 18);
            y += 32;
            c2.Height = y + 10;

            // ---------------- 生成参数 ----------------
            CardPanel c3 = Card();
            _bodyCards.Add(c3);
            y = 16;
            Title(c3, "生成参数", y);
            y = 50;

            Slider sl;
            ThemedLabel vl;
            y = RowSlider(c3, y, "温度", "越高越发散、越有创意；润色一般 0.3 到 0.8 比较稳。",
                Cfg.Ai.Temperature, 0, 1, 0.05, "0.00", null, out sl, out vl);
            _temp = sl;
            _tempValue = vl;

            y = RowNumber(c3, y, "最大长度", "一次最多生成多少 token，越大越慢也越贵。",
                Cfg.Ai.MaxTokens, 64, 8192, 64, null, out nb);
            _maxTokens = nb;

            y = RowNumber(c3, y, "超时秒数", "超过这个时间还没回完就断开。",
                Cfg.Ai.TimeoutSeconds, 5, 300, 5, null, out nb);
            _timeout = nb;

            y = RowSwitch(c3, y, "流式输出", "边生成边显示，不用干等一整段。",
                Cfg.Ai.Stream, null, out sw);
            _stream = sw;
            c3.Height = y + 10;

            // ---------------- 系统提示词 ----------------
            CardPanel c4 = Card();
            _bodyCards.Add(c4);
            y = 16;
            Title(c4, "系统提示词", y);
            SubWrap(c4, "留空就用内置的默认提示词（针对中文润色调过）。填了就以你的为准，会加在每次请求最前面。",
                18, y + 26, c4.Width - 36);
            y = 78;

            InputBox sp = new InputBox();
            sp.Multiline = true;
            sp.Text = Cfg.Ai.SystemPrompt;
            Stretch(c4, sp, 18, y, 96, 18);
            sp.TextChanged += PageChanged;
            _systemPrompt = sp;
            y += 96 + 14;
            c4.Height = y + 8;

            // ---------------- 润色风格 ----------------
            CardPanel c5 = Card();
            _bodyCards.Add(c5);
            y = 16;
            Title(c5, "默认润色风格", y);
            y = 54;

            List<string> presets = new List<string>(PresetNames);
            y = RowSelect(c5, y, "选中文字的默认改法", "按 AI 润色快捷键时，默认用哪种效果。",
                presets, PresetIndex(Cfg.Ai.AutoPreset), 200, PresetChanged, out sb);
            _preset = sb;

            _presetDesc = SubText(c5, "", 18, y);
            y += 26;
            c5.Height = y + 10;

            // ---------------- 触发方式 ----------------
            CardPanel c6 = Card();
            _bodyCards.Add(c6);
            y = 16;
            Title(c6, "AI 触发方式", y);
            y = 50;

            FlatCheck fc;
            y = RowCheck(c6, y, "在输入条上显示 AI 按钮", null, Cfg.Ai.ShowButtonInBar, null, out fc);
            _showButton = fc;
            y = RowCheck(c6, y, "自动读取修改模式",
                "选中一段文字按下快捷键，AI 直接读走、改完再替换回来。",
                Cfg.Ai.AutoReadMode, null, out fc);
            _autoRead = fc;
            y = RowCheck(c6, y, "指令输入模式",
                "在输入条里直接写要求，比如「把这段改正式一点」。",
                Cfg.Ai.CommandMode, null, out fc);
            _commandMode = fc;
            c6.Height = y + 10;

            // ---------------- 额外请求头 ----------------
            CardPanel c7 = Card();
            _bodyCards.Add(c7);
            y = 16;
            Title(c7, "额外请求头", y);
            SubWrap(c7, "每行一个，格式「名字: 值」，比如 X-Real-IP: 1.2.3.4。留空就只发标准的那几个头。",
                18, y + 26, c7.Width - 36);
            y = 78;

            InputBox hd = new InputBox();
            hd.Multiline = true;
            hd.Text = HeadersToText(Cfg.Ai.ExtraHeaders);
            Stretch(c7, hd, 18, y, 76, 18);
            hd.TextChanged += PageChanged;
            _headers = hd;
            y += 76 + 14;
            c7.Height = y + 8;

            // ---------------- 连接测试 ----------------
            CardPanel c8 = Card();
            _bodyCards.Add(c8);
            y = 16;
            Title(c8, "连接测试", y);
            SubWrap(c8, "为了不重复实现一遍网络逻辑，连接测试放在输入法的 AI 面板里：保存设置后按快捷键（默认 Ctrl+Alt+A）打开面板，点一下「测试连接」，成不成、报什么错都会写在面板上。",
                18, y + 26, c8.Width - 220);

            GhostButton testBtn = new GhostButton();
            testBtn.Text = "连接测试";
            testBtn.Size = new Size(96, 32);
            PlaceRight(c8, testBtn, 18);
            testBtn.Top = 16;
            testBtn.Click += delegate { OnTestClick(); };
            y += 78;
            c8.Height = y + 10;

            NoticeBar notice = new NoticeBar();
            notice.Visible = false;
            notice.VisibilityChanged += delegate { RelayoutNow(); };
            Add(notice);
            _notice = notice;

            FinishBuild();

            ApplyProvider(DetectProviderIndex(), false);
            UpdateKeyStatus();
            SetBodyEnabled(Cfg.Ai.Enabled);
        }

        // ---------------- 事件 ----------------

        void MasterChanged(object sender, EventArgs e)
        {
            bool on = _enabled != null && _enabled.Checked;
            SetBodyEnabled(on);
        }

        void SetBodyEnabled(bool on)
        {
            for (int i = 0; i < _bodyCards.Count; i++) _bodyCards[i].Enabled = on;
            if (_offHint != null)
            {
                _offHint.Text = on ? "" : "AI 现在是关着的，下面这些设置先存着，等开关打开就生效。";
            }
            RelayoutNow();
        }

        void ProviderChanged(object sender, EventArgs e)
        {
            if (_provider == null) return;
            ApplyProvider(_provider.SelectedIndex, true);
        }

        void ApplyProvider(int index, bool overwrite)
        {
            List<ProviderInfo> all = ProviderCatalog.All;
            if (index < 0 || index >= all.Count) return;
            ProviderInfo p = all[index];

            if (p.Id != "custom") _protocol = p.Provider;
            _docUrl = p.DocUrl;

            if (overwrite && p.Id != "custom")
            {
                if (_baseUrl != null) _baseUrl.Text = p.BaseUrl;
                if (_model != null) _model.Text = p.DefaultModel;
            }

            if (_note != null)
            {
                string note = p.Note;
                if (p.Local) note = "本地服务，一般不用填 API Key。" + note;
                _note.Text = note;
            }
            if (_docLink != null)
            {
                if (_docUrl.Length > 0)
                {
                    _docLink.Text = "如何获取 API Key";
                    _docLink.Enabled = true;
                }
                else
                {
                    _docLink.Text = "该服务商没有提供申请地址";
                    _docLink.Enabled = false;
                }
            }
        }

        int DetectProviderIndex()
        {
            List<ProviderInfo> all = ProviderCatalog.All;
            string url = Cfg.Ai.BaseUrl == null ? "" : Cfg.Ai.BaseUrl.Trim();
            if (url.Length > 0)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].BaseUrl.Length == 0) continue;
                    if (string.Equals(all[i].BaseUrl, url, StringComparison.OrdinalIgnoreCase)) return i;
                }
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].BaseUrl.Length == 0) continue;
                    if (string.Equals(all[i].BaseUrl.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            return ProviderCatalog.IndexOf("custom");
        }

        void PresetChanged(object sender, EventArgs e)
        {
            if (_presetDesc == null || _preset == null) return;
            int i = _preset.SelectedIndex;
            if (i < 0 || i >= PresetDesc.Length) return;
            _presetDesc.Text = PresetDesc[i];
        }

        static int PresetIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < PresetIds.Length; i++)
                if (string.Equals(PresetIds[i], id, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        void UpdateKeyStatus()
        {
            if (_keyStatus == null) return;
            string k = Host == null ? "" : Host.PendingApiKey;
            _keyStatus.Text = k.Length == 0 ? "尚未保存 API Key" : "已保存：" + SecretStore.Mask(k);
            _keyStatus.Invalidate();
        }

        void OnDocLink()
        {
            if (_docUrl.Length == 0)
            {
                ShowNotice("这个服务商没有登记申请地址，去它的官网控制台里找「API Key」就行。", NoticeBar.KindInfo);
                return;
            }
            AppUtil.OpenUrl(_docUrl);
        }

        void OnTestClick()
        {
            ShowNotice("连接测试在输入法的 AI 面板里：先点右下角「保存」，再按 Ctrl+Alt+A 打开面板，点「测试连接」。" +
                       "测试要用真实网络请求，所以统一放在主程序里做，设置界面不重复实现一遍。", NoticeBar.KindInfo);
        }

        void ShowNotice(string text, int kind)
        {
            if (_notice != null) _notice.Show(text, kind, 8000);
        }

        // ---------------- 收集 ----------------

        public override void Collect()
        {
            if (Cfg == null) return;
            AiSettings a = Cfg.Ai;

            if (_enabled != null) a.Enabled = _enabled.Checked;
            a.Provider = _protocol;
            if (_baseUrl != null) a.BaseUrl = _baseUrl.Text.Trim();
            if (_model != null) a.Model = _model.Text.Trim();
            if (_temp != null) a.Temperature = Math.Round(_temp.Value, 2);
            if (_maxTokens != null) a.MaxTokens = _maxTokens.Value;
            if (_timeout != null) a.TimeoutSeconds = _timeout.Value;
            if (_stream != null) a.Stream = _stream.Checked;
            if (_systemPrompt != null) a.SystemPrompt = _systemPrompt.Text;
            if (_preset != null && _preset.SelectedIndex >= 0 && _preset.SelectedIndex < PresetIds.Length)
                a.AutoPreset = PresetIds[_preset.SelectedIndex];
            if (_showButton != null) a.ShowButtonInBar = _showButton.Checked;
            if (_autoRead != null) a.AutoReadMode = _autoRead.Checked;
            if (_commandMode != null) a.CommandMode = _commandMode.Checked;
            if (_headers != null) a.ExtraHeaders = ParseHeaders(_headers.Text);

            // API Key 走 Host.PendingApiKey，保存那一刻才加密写进设置
        }

        public static Dictionary<string, string> ParseHeaders(string text)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return map;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                string k = line.Substring(0, c).Trim();
                string v = line.Substring(c + 1).Trim();
                if (k.Length == 0) continue;
                map[k] = v;
            }
            return map;
        }

        public static string HeadersToText(Dictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in map)
                sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
            return sb.ToString().TrimEnd();
        }
    }

    // ====================================================================
    //  6. 词库
    // ====================================================================

    public class LexiconPage : PageBase
    {
        readonly StatCard[] _stats = new StatCard[4];
        ThemedLabel _statHint;
        ThemedLabel _userCount;
        ThemedLabel _builtinInfo;
        NoticeBar _notice;

        static readonly string[] Captions = new string[] { "单字", "词语", "音节", "英文词" };

        public override void Build()
        {
            // ---------------- 词库概况 ----------------
            BlockPanel row = new BlockPanel();
            row.Height = 110;
            for (int i = 0; i < 4; i++)
            {
                StatCard sc = new StatCard();
                sc.SetBounds(i * 158, 0, 148, 92);
                sc.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                sc.Caption = Captions[i];
                sc.Value = "…";
                row.Controls.Add(sc);
                _stats[i] = sc;
            }
            ThemedLabel hint = new ThemedLabel();
            hint.Role = ThemedLabel.RoleSub;
            hint.Text = "正在统计本地词库…";
            hint.Location = new Point(2, 100);
            row.Controls.Add(hint);
            _statHint = hint;
            Add(row);

            // ---------------- 用户词库 ----------------
            CardPanel c1 = Card();
            int y = 16;
            Title(c1, "用户词库", y);
            y = 50;
            SubWrap(c1, "输入法会把你经常用的词悄悄记下来，下次打同样的拼音它就更靠前。文件是纯文本，想手工加词也行。",
                18, y, c1.Width - 36);
            y += 44;

            Text(c1, "文件位置", 18, y + 6);
            GhostButton open = new GhostButton();
            open.Text = "打开所在文件夹";
            open.Size = new Size(132, 30);
            PlaceRight(c1, open, 18);
            open.Top = y;
            open.Click += delegate { OpenFolderOrToast(AppPaths.DataDir, "用户数据目录"); };
            y += 34;

            SubText(c1, AppPaths.UserWordsFile, 18, y);
            y += 30;

            Text(c1, "当前条数", 18, y + 2);
            _userCount = RightLabel(c1, "", y, 220, 18);
            y += 34;

            GhostButton clear = new GhostButton();
            clear.Text = "清空用户词库";
            clear.Danger = true;
            clear.Size = new Size(120, 32);
            PlaceRight(c1, clear, 18);
            clear.Top = y;
            clear.Click += delegate { ClearUserWords(); };
            SubWrap(c1, "清空只删你学到的词，内置词库一个都不动。", 18, y + 6, c1.Width - 200);
            y += 46;
            c1.Height = y + 12;

            // ---------------- 内置词库 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "内置词库", y);
            y = 50;
            SubWrap(c2, "随程序发布的数据文件（chars.txt 单字表、words.txt 词表、syllables.txt 音节表）。升级程序时会被覆盖，别在这里改。",
                18, y, c2.Width - 36);
            y += 44;

            Text(c2, "所在目录", 18, y + 6);
            GhostButton open2 = new GhostButton();
            open2.Text = "打开词库文件夹";
            open2.Size = new Size(132, 30);
            PlaceRight(c2, open2, 18);
            open2.Top = y;
            open2.Click += delegate { OpenFolderOrToast(AppPaths.BuiltinDataDir, "词库目录"); };
            y += 34;

            _builtinInfo = SubText(c2, AppPaths.BuiltinDataDir, 18, y);
            y += 30;
            c2.Height = y + 12;

            NoticeBar notice = new NoticeBar();
            notice.Visible = false;
            notice.VisibilityChanged += delegate { RelayoutNow(); };
            Add(notice);
            _notice = notice;

            FinishBuild();

            UpdateUserCount();
            StartStats();
        }

        void UpdateUserCount()
        {
            if (_userCount == null) return;
            _userCount.Text = AppUtil.CountUserWords() + " 条";
        }

        void ClearUserWords()
        {
            DialogResult r = MessageBox.Show(this,
                "确定要清空用户词库吗？\r\n\r\n你自造的词、调过的词频都会没掉，内置词库不受影响。",
                "墨言输入法 · 清空用户词库", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK) return;

            try
            {
                string path = AppPaths.UserWordsFile;
                if (File.Exists(path)) File.Delete(path);
                UpdateUserCount();
                if (_notice != null) _notice.Show("用户词库已清空。", NoticeBar.KindSuccess, 4000);
            }
            catch (Exception ex)
            {
                Log.Error("清空用户词库失败", ex);
                if (_notice != null) _notice.Show("清空失败：" + ex.Message, NoticeBar.KindError, 8000);
            }
        }

        void StartStats()
        {
            Thread t = new Thread(StatsWorker);
            t.IsBackground = true;
            t.Name = "LexiconStats";
            t.Start();
        }

        void StatsWorker()
        {
            int chars = 0, words = 0, en = 0, syl = 0;
            string warn = null;
            try
            {
                Lexicon lex = new Lexicon();
                lex.LoadAll();
                chars = lex.CharCount;
                words = lex.WordCount;
                en = lex.EnglishCount;
                syl = lex.SyllableCount;
                if (lex.LoadWarnings.Count > 0) warn = string.Join("；", lex.LoadWarnings.ToArray());
            }
            catch (Exception ex)
            {
                warn = ex.Message;
                Log.Error("统计词库失败", ex);
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate { ApplyStats(chars, words, en, syl, warn); });
            }
            catch { }
        }

        void ApplyStats(int chars, int words, int en, int syl, string warn)
        {
            if (IsDisposed) return;
            if (_stats[0] != null) { _stats[0].Value = chars.ToString("N0"); _stats[0].Invalidate(); }
            if (_stats[1] != null) { _stats[1].Value = words.ToString("N0"); _stats[1].Invalidate(); }
            if (_stats[2] != null) { _stats[2].Value = syl.ToString("N0"); _stats[2].Invalidate(); }
            if (_stats[3] != null) { _stats[3].Value = en.ToString("N0"); _stats[3].Invalidate(); }

            if (_statHint != null)
            {
                _statHint.Text = string.IsNullOrEmpty(warn)
                    ? "数据文件都在，统计完成。"
                    : "有文件没读到：" + warn;
            }
        }
    }

    // ====================================================================
    //  7. 关于
    // ====================================================================

    public class AboutPage : PageBase
    {
        NoticeBar _notice;

        public override void Build()
        {
            // ---------------- 关于本程序 ----------------
            CardPanel c1 = Card();
            c1.Paint += LogoPaint;

            int y = 26;
            ThemedLabel name = Text(c1, AppPaths.AppNameCn, 100, y);
            name.Role = ThemedLabel.RoleBig;
            name.ApplyTheme();
            y += 36;

            SubText(c1, "版本 " + AppPaths.Version + "　·　MyIme.Settings.exe", 100, y);
            y += 30;

            SubWrap(c1, "一个用 C# 写的中文输入法：拼音引擎和词库全在本地、不联网，AI 写作助手是可选的。"
                + "整个程序是用 Windows 自带的 csc.exe 编出来的，不需要装任何开发环境。",
                100, y, c1.Width - 130);
            y += 64;

            HLine(c1, y);
            y += 16;

            Text(c1, "设置文件", 18, y);
            SubText(c1, AppPaths.SettingsFile, 130, y);
            y += 26;
            Text(c1, "用户数据", 18, y);
            SubText(c1, AppPaths.DataDir, 130, y);
            y += 26;
            Text(c1, "程序目录", 18, y);
            SubText(c1, AppPaths.BaseDir, 130, y);
            y += 36;
            c1.Height = y + 16;

            // ---------------- 日志与卸载 ----------------
            CardPanel c2 = Card();
            y = 16;
            Title(c2, "日志与卸载", y);
            y = 50;
            SubWrap(c2, "输入法遇到问题时，把日志文件发给开发者就能定位。", 18, y, c2.Width - 220);

            GhostButton openLog = new GhostButton();
            openLog.Text = "打开日志文件夹";
            openLog.Size = new Size(132, 32);
            PlaceRight(c2, openLog, 18);
            openLog.Top = 46;
            openLog.Click += delegate { OpenLogFolder(); };
            y += 44;

            SubText(c2, LogFolder(), 18, y);
            y += 34;

            GhostButton uninstall = new GhostButton();
            uninstall.Text = "卸载墨言输入法";
            uninstall.Danger = true;
            uninstall.Size = new Size(132, 32);
            PlaceRight(c2, uninstall, 18);
            uninstall.Top = y;
            uninstall.Click += delegate { Uninstall(); };
            SubWrap(c2, "会调用同目录下的 tty.exe（带 --uninstall 参数）来卸载。", 18, y + 6, c2.Width - 200);
            y += 48;
            c2.Height = y + 12;

            NoticeBar notice = new NoticeBar();
            notice.Visible = false;
            notice.VisibilityChanged += delegate { RelayoutNow(); };
            Add(notice);
            _notice = notice;

            FinishBuild();
        }

        void LogoPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prep(g);
            Rectangle logo = new Rectangle(26, 24, 56, 56);
            Theme.FillRounded(g, logo, 14, Theme.Accent);
            Theme.CenterText(g, "墨", Theme.UI(21f, FontStyle.Bold),
                new Rectangle(logo.X, logo.Y + 1, logo.Width, logo.Height), Color.White);
        }

        static string LogFolder()
        {
            try
            {
                string p = Log.CurrentPath;
                if (string.IsNullOrEmpty(p)) return "";
                return Path.GetDirectoryName(p);
            }
            catch { return ""; }
        }

        void OpenLogFolder()
        {
            string dir = LogFolder();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                if (_notice != null) _notice.Show("还没有生成过日志文件 —— 输入法正常跑起来之后就会有了。",
                    NoticeBar.KindInfo, 6000);
                return;
            }
            AppUtil.OpenFolder(dir);
        }

        void Uninstall()
        {
            string exe = AppPaths.UninstallExe;
            if (!File.Exists(exe))
            {
                if (_notice != null) _notice.Show("没找到 tty.exe —— 可能这份不是用安装包装的，"
                    + "直接删掉整个程序文件夹就行（用户词库和设置会留在 %APPDATA%\\MyIME）。",
                    NoticeBar.KindWarn, 9000);
                return;
            }

            DialogResult r = MessageBox.Show(this,
                "确定要卸载墨言输入法吗？\r\n\r\n会先关掉正在运行的输入法，再删掉程序文件；"
                + "你的设置和用户词库会保留在 %APPDATA%\\MyIME 里。",
                "墨言输入法 · 卸载", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK) return;

            try
            {
                Process.Start(exe, "--uninstall");
            }
            catch (Exception ex)
            {
                Log.Error("启动卸载程序失败", ex);
                if (_notice != null) _notice.Show("启动卸载程序失败：" + ex.Message, NoticeBar.KindError, 8000);
            }
        }
    }
}

