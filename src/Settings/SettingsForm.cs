using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Settings
{
    /// <summary>
    /// 设置主窗口：左边导航 + 右边内容区，右下角固定保存按钮。
    /// 所有改动先落在内存里的 AppSettings 副本上，点"保存"才写盘。
    /// </summary>
    public class SettingsForm : Form
    {
        public const int NavWidth = 190;
        public const int BottomHeight = 62;

        static readonly string[] NavTitles = new string[] {
            "常规", "输入", "快捷键", "游戏锁定", "AI 写作", "词库", "关于"
        };
        static readonly string[] NavKeys = new string[] {
            "general", "input", "hotkey", "game", "ai", "lexicon", "about"
        };
        static readonly int[] NavIcons = new int[] {
            Icons.General, Icons.Input, Icons.Hotkey, Icons.Game, Icons.Ai, Icons.Lexicon, Icons.About
        };

        // ---------- 数据 ----------
        AppSettings _working;
        AppSettings _original;
        string _originalKey = "";
        string _pendingKey = "";
        bool _keyTouched = false;
        bool _dirty = false;
        bool _loading = true;

        // ---------- 控件 ----------
        Panel _nav;
        Panel _contentHost;
        Panel _bottomBar;
        List<NavItem> _navItems = new List<NavItem>();
        AccentButton _saveButton;
        ToastLabel _toast;
        ThemedLabel _dirtyLabel;
        PageBase _current;
        int _navIndex = 0;
        bool _coreRunning = false;
        Timer _statusTimer;

        public SettingsForm(string initialPage)
        {
            _working = SettingsStore.Load();
            _original = _working.Clone();
            _originalKey = SecretStore.GetApiKey(_working.Ai);
            _pendingKey = _originalKey;

            Theme.Apply(_working.General.Theme);

            Text = "松林1234输入法 · 设置";
            Font = Theme.UI(9f, FontStyle.Regular);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            ClientSize = new Size(880, 620);

            Size sz = Size;
            MinimumSize = new Size(760 + (sz.Width - ClientSize.Width), 520 + (sz.Height - ClientSize.Height));

            BuildChrome();

            int index = IndexOfKey(NormalizePageKey(initialPage));
            SelectPage(index < 0 ? 0 : index);

            _loading = false;
            RefreshStatus();
            _statusTimer = new Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate { RefreshStatus(); };
            _statusTimer.Start();
        }

        // ==================== 参数 / 数据 ====================

        /// <summary>把命令行里的页名归一化，认不出来返回 null。</summary>
        public static string NormalizePageKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().ToLowerInvariant();
            if (k == "general" || k == "常规" || k == "common") return "general";
            if (k == "input" || k == "输入") return "input";
            if (k == "hotkey" || k == "hotkeys" || k == "快捷键") return "hotkey";
            if (k == "game" || k == "gamelock" || k == "游戏锁定" || k == "lock") return "game";
            if (k == "ai" || k == "ai写作" || k == "writing") return "ai";
            if (k == "lexicon" || k == "词库" || k == "dict") return "lexicon";
            if (k == "about" || k == "关于") return "about";
            return null;
        }

        static int IndexOfKey(string key)
        {
            if (key == null) return -1;
            for (int i = 0; i < NavKeys.Length; i++)
                if (NavKeys[i] == key) return i;
            return -1;
        }

        /// <summary>当前正在编辑的设置（内存副本）。</summary>
        public AppSettings Working { get { return _working; } }

        /// <summary>保存前的 API Key 明文（页面重建也不会丢）。</summary>
        public string PendingApiKey { get { return _pendingKey; } }

        public void SetPendingApiKey(string key)
        {
            string k = key == null ? "" : key;
            if (k == _pendingKey) return;
            _pendingKey = k;
            _keyTouched = true;
            NotifyChanged();
        }

        public bool CoreRunning { get { return _coreRunning; } }

        // ==================== 界面骨架 ====================

        // BuildChrome() 里每调一次 Controls.Add() 都会立刻触发 OnLayout -> LayoutChrome()，
        // 而那时后面的控件字段（_contentHost / _bottomBar）还没赋值，会直接 NullReferenceException。
        // 用这个就绪标志把构建途中的布局请求挡掉，等三个区域都建好了再真正排一次。
        bool _chromeReady;

        void BuildChrome()
        {
            _nav = new Panel();
            _nav.BackColor = Theme.Bg;
            _nav.Paint += NavPaint;
            Controls.Add(_nav);

            _contentHost = new Panel();
            _contentHost.BackColor = Theme.Bg;
            _contentHost.AutoScroll = true;
            _contentHost.Padding = new Padding(22, 16, 22, 16);
            Controls.Add(_contentHost);

            _bottomBar = new Panel();
            _bottomBar.BackColor = Theme.Bg;
            _bottomBar.Paint += BottomPaint;
            Controls.Add(_bottomBar);

            _saveButton = new AccentButton();
            _saveButton.Text = "保存";
            _saveButton.Size = new Size(104, 38);
            _saveButton.Muted = true;
            _saveButton.Click += delegate { Save(); };
            _bottomBar.Controls.Add(_saveButton);

            _toast = new ToastLabel();
            _toast.Size = new Size(230, 30);
            _bottomBar.Controls.Add(_toast);

            _dirtyLabel = new ThemedLabel();
            _dirtyLabel.Role = ThemedLabel.RoleSub;
            _dirtyLabel.TextAlign = ContentAlignment.MiddleRight;
            _dirtyLabel.Size = new Size(180, 30);
            _dirtyLabel.Text = "";
            _bottomBar.Controls.Add(_dirtyLabel);

            for (int i = 0; i < NavTitles.Length; i++)
            {
                NavItem item = new NavItem();
                item.Text = NavTitles[i];
                item.Icon = NavIcons[i];
                int captured = i;
                item.Click += delegate { SelectPage(captured); };
                _navItems.Add(item);
                _nav.Controls.Add(item);
            }

            _chromeReady = true;
            LayoutChrome();
        }

        void NavPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prep(g);

            // 右上角的竖分割线
            using (Pen pen = new Pen(Theme.Divider))
                g.DrawLine(pen, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height);

            // 顶部图标 + 名称 + 版本
            Rectangle logo = new Rectangle(20, 26, 38, 38);
            Theme.FillRounded(g, logo, 10, Theme.Accent);
            Theme.CenterText(g, "墨", Theme.UI(15f, FontStyle.Bold),
                new Rectangle(logo.X, logo.Y + 1, logo.Width, logo.Height), Color.White);

            Theme.LeftText(g, AppPaths.AppNameCn, Theme.UI(11.5f, FontStyle.Bold),
                new Rectangle(68, 27, _nav.Width - 76, 24), Theme.Text);
            Theme.LeftText(g, "版本 " + AppPaths.Version, Theme.Small(),
                new Rectangle(68, 50, _nav.Width - 76, 20), Theme.SubText);

            // 左下角主程序状态
            int sy = _nav.Height - 34;
            if (sy > 60)
            {
                Color dot = _coreRunning ? Theme.Accent : Theme.SubText;
                using (SolidBrush b = new SolidBrush(dot))
                    g.FillEllipse(b, new Rectangle(22, sy + 6, 8, 8));
                string text = _coreRunning ? "输入法正在运行" : "输入法未运行";
                Theme.LeftText(g, text, Theme.Small(),
                    new Rectangle(38, sy, _nav.Width - 48, 20), Theme.SubText);
            }
        }

        void BottomPaint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Divider))
                e.Graphics.DrawLine(pen, 0, 0, _bottomBar.Width, 0);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutChrome();
        }

        void LayoutChrome()
        {
            if (!_chromeReady || _nav == null || _contentHost == null || _bottomBar == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int navW = NavWidth;
            int barH = BottomHeight;

            _nav.SetBounds(0, 0, navW, h);
            _contentHost.SetBounds(navW, 0, Math.Max(20, w - navW), Math.Max(20, h - barH));
            _bottomBar.SetBounds(navW, Math.Max(0, h - barH), Math.Max(20, w - navW), barH);

            int y = 88;
            for (int i = 0; i < _navItems.Count; i++)
            {
                _navItems[i].SetBounds(12, y, navW - 24, 40);
                y += 44;
            }

            int bw = _bottomBar.ClientSize.Width;
            _saveButton.SetBounds(bw - 24 - _saveButton.Width, (barH - _saveButton.Height) / 2,
                _saveButton.Width, _saveButton.Height);
            _toast.SetBounds(_saveButton.Left - 14 - _toast.Width, (barH - _toast.Height) / 2,
                _toast.Width, _toast.Height);
            _dirtyLabel.SetBounds(Math.Max(8, _toast.Left - 190), (barH - 30) / 2, 180, 30);
        }

        // ==================== 页面切换 ====================

        public void SelectPage(int index)
        {
            if (index < 0) index = 0;
            if (index >= NavTitles.Length) index = NavTitles.Length - 1;

            if (_current != null)
            {
                _current.Collect();
                _contentHost.Controls.Remove(_current);
                _current.Dispose();
                _current = null;
            }

            _navIndex = index;
            for (int i = 0; i < _navItems.Count; i++) _navItems[i].Selected = (i == index);

            PageBase page = CreatePage(index);
            page.Host = this;
            page.Cfg = _working;
            page.Dock = DockStyle.Top;
            _contentHost.Controls.Add(page);
            page.Build();
            page.RelayoutNow();
            _current = page;

            try { _contentHost.AutoScrollPosition = new Point(0, 0); }
            catch { }

            RefreshDirty();
        }

        PageBase CreatePage(int index)
        {
            switch (index)
            {
                case 0: return new GeneralPage();
                case 1: return new InputPage();
                case 2: return new HotkeyPage();
                case 3: return new GameLockPage();
                case 4: return new AiPage();
                case 5: return new LexiconPage();
                default: return new AboutPage();
            }
        }

        public int CurrentPageIndex { get { return _navIndex; } }

        // ==================== 改动 / 保存 ====================

        /// <summary>页面里任何控件变了都调这个。</summary>
        public void NotifyChanged()
        {
            if (_loading) return;
            RefreshDirty();
        }

        void RefreshDirty()
        {
            if (_current != null) _current.Collect();
            bool dirty = _keyTouched || !SameJson(_working, _original);
            if (dirty == _dirty) return;
            _dirty = dirty;
            UpdateSaveUi();
        }

        static bool SameJson(AppSettings a, AppSettings b)
        {
            try { return Json.Stringify(a) == Json.Stringify(b); }
            catch { return false; }
        }

        void UpdateSaveUi()
        {
            _saveButton.Muted = !_dirty;
            _saveButton.Invalidate();
            _dirtyLabel.Text = _dirty ? "有未保存的改动" : "";
            _dirtyLabel.Invalidate();
        }

        /// <summary>保存：写盘 + 通知在跑的输入法重载。</summary>
        public void Save()
        {
            if (_current != null) _current.Collect();
            ApplyPendingKey();

            try
            {
                SettingsStore.Save(_working);
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                ShowToast("保存失败：" + ex.Message, ToastLabel.KindError);
                return;
            }

            _original = _working.Clone();
            _originalKey = _pendingKey;
            _keyTouched = false;
            _dirty = false;
            UpdateSaveUi();

            if (_current != null) _current.OnSaved();

            RefreshStatus();
            ShowToast(_coreRunning ? "已保存 · 输入法已即时生效" : "已保存 · 下次启动生效", ToastLabel.KindSuccess);
        }

        void ApplyPendingKey()
        {
            // 没动过、又解不开原来的密文，就原样留着（换个 Windows 账户的情况）
            if (!_keyTouched && _pendingKey.Length == 0 && _working.Ai.ApiKeyProtected.Length > 0) return;
            SecretStore.SetApiKey(_working.Ai, _pendingKey);
        }

        public void ShowToast(string text, int kind)
        {
            _toast.Kind = kind;
            _toast.ShowToast(text, 2000);
        }

        public void ShowToast(string text)
        {
            ShowToast(text, ToastLabel.KindSuccess);
        }

        void RefreshStatus()
        {
            bool running = false;
            try
            {
                Process[] ps = Process.GetProcessesByName("MyIme.Core");
                running = ps != null && ps.Length > 0;
                if (ps != null)
                {
                    for (int i = 0; i < ps.Length; i++) ps[i].Dispose();
                }
            }
            catch { }
            if (running != _coreRunning)
            {
                _coreRunning = running;
                if (_nav != null) _nav.Invalidate();
            }
        }

        // ==================== 主题 ====================

        /// <summary>换主题：立刻重绘整个窗口（设置在点保存时才落盘）。</summary>
        public void ApplyThemeNow(string mode)
        {
            Theme.Apply(mode);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            ApplyThemeDeep(this);
            Invalidate(true);
            if (_nav != null) _nav.Invalidate();
            if (_bottomBar != null) _bottomBar.Invalidate();
        }

        static void ApplyThemeDeep(Control root)
        {
            IThemed t = root as IThemed;
            if (t != null) t.ApplyTheme();
            for (int i = 0; i < root.Controls.Count; i++) ApplyThemeDeep(root.Controls[i]);
        }

        // ==================== 关闭 ====================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_current != null) _current.Collect();
            RefreshDirty();
            if (!_dirty) return;

            DialogResult r = MessageBox.Show(this,
                "设置有改动还没保存，现在保存吗？\r\n\r\n" +
                "「是」保存并关闭，「否」放弃改动，「取消」继续编辑。",
                "松林1234输入法 · 设置", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes)
            {
                Save();
                if (_dirty) e.Cancel = true;
            }
        }
    }
}
