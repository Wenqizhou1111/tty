using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Setup
{
    /// <summary>
    /// 图形化安装 / 卸载向导。单窗口、自绘分区（顶部标题区 / 中间内容区 / 底部按钮区），
    /// 用面板切换步骤，不新开 Form。
    /// </summary>
    public class SetupForm : Form
    {
        public const int FormW = 560;
        public const int FormH = 400;
        const int HeaderH = 64;
        const int FooterH = 60;
        const int ContentH = FormH - HeaderH - FooterH;
        const int Gap = 24;

        /// <summary>0 = 成功，1 = 失败。</summary>
        public int ExitCode = 0;

        // ---------- 输入 ----------
        SetupArgs _args;
        bool _uninstall;
        InstallOptions _options;

        // ---------- 布局 ----------
        HeadPanel _header;
        Panel _content;
        FooterPanel _footer;
        Panel _pageWelcome, _pageOptions, _pageProgress, _pageDone;

        FlatButton _primary, _secondary;
        FlatProgress _bar;
        StatusIcon _icon;
        LabelEx _progressTitle, _status, _doneTitle, _doneHint, _errLabel;
        FieldBox _welcomePath, _confirmPath;
        FieldPanel _dirHost;
        TextBox _dirBox;
        FlatButton _browse;
        FlatCheckBox _cbDesktop, _cbStartMenu, _cbAutoStart, _cbLaunch, _cbAllUsers, _cbRemoveData;

        // ---------- 状态 ----------
        int _step = 0;
        bool _busy = false;
        bool _failed = false;
        string _errorText = null;

        public SetupForm(SetupArgs args)
        {
            _args = (args == null) ? new SetupArgs() : args;
            _uninstall = _args.Uninstall;

            bool admin = Installer.IsAdmin;
            _options = _args.ToOptions();
            if (string.IsNullOrEmpty(_args.Dir))
            {
                _options.AllUsers = admin;                       // 管理员默认"为所有用户安装"
                _options.TargetDir = Installer.DefaultTargetDir(admin);
            }
            else
            {
                _options.TargetDir = Installer.NormalizeDir(_args.Dir);
            }

            Text = _uninstall ? (Installer.ProductNameCn + " 卸载") : (Installer.ProductNameCn + " 安装程序");
            Font = UiFonts.Body;
            BackColor = UiTheme.PageBack;
            ForeColor = UiTheme.TextPrimary;
            ClientSize = new Size(FormW, FormH);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;

            BuildHeader();
            BuildContent();
            BuildFooter();
            BuildPages();

            AcceptButton = _primary;
            ShowStep(0);
        }

        // ==================== 界面搭建 ====================

        void BuildHeader()
        {
            _header = new HeadPanel();
            _header.SetBounds(0, 0, FormW, HeaderH);
            _header.Title = Installer.ProductNameCn;
            _header.SubTitle = "MyIME · Windows 拼音输入法     版本 " + Installer.GetVersion();
            Controls.Add(_header);
        }

        void BuildContent()
        {
            _content = new Panel();
            _content.SetBounds(0, HeaderH, FormW, ContentH);
            _content.BackColor = UiTheme.PageBack;
            Controls.Add(_content);
        }

        void BuildFooter()
        {
            _footer = new FooterPanel();
            _footer.SetBounds(0, HeaderH + ContentH, FormW, FooterH);

            _primary = new FlatButton();
            _primary.Primary = true;
            _primary.Size = new Size(104, 32);
            _primary.Location = new Point(FormW - Gap - _primary.Width, (FooterH - _primary.Height) / 2);
            _primary.Click += new EventHandler(OnPrimaryClick);

            _secondary = new FlatButton();
            _secondary.Primary = false;
            _secondary.Size = new Size(96, 32);
            _secondary.Location = new Point(_primary.Left - 12 - _secondary.Width, _primary.Top);
            _secondary.Click += new EventHandler(OnSecondaryClick);

            _footer.Controls.Add(_secondary);
            _footer.Controls.Add(_primary);
            Controls.Add(_footer);
        }

        void BuildPages()
        {
            _pageWelcome = _uninstall ? BuildUninstallWelcomePage() : BuildInstallWelcomePage();
            _pageOptions = BuildOptionsPage();
            _pageProgress = BuildProgressPage();
            _pageDone = BuildDonePage();

            _content.Controls.Add(_pageDone);
            _content.Controls.Add(_pageProgress);
            _content.Controls.Add(_pageOptions);
            _content.Controls.Add(_pageWelcome);
        }

        Panel NewPage()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = UiTheme.PageBack;
            p.Visible = false;
            return p;
        }

        LabelEx NewLabel(string text, Font font, Color color, bool wrap)
        {
            LabelEx l = new LabelEx();
            l.Text = text;
            if (font != null) l.Font = font;
            l.ForeColor = color;
            l.Wrap = wrap;
            return l;
        }

        FlatCheckBox NewCheck(string text, bool value, int y)
        {
            FlatCheckBox c = new FlatCheckBox();
            c.Text = text;
            c.Checked = value;
            c.SetBounds(Gap, y, FormW - Gap * 2, 24);
            return c;
        }

        Panel BuildInstallWelcomePage()
        {
            Panel p = NewPage();

            LabelEx head = NewLabel("准备就绪，可以开始安装。", UiFonts.H1, UiTheme.TextPrimary, false);
            head.SetBounds(Gap, 26, FormW - Gap * 2, 30);
            p.Controls.Add(head);

            LabelEx n1 = NewLabel("安装程序会把「" + Installer.ProductNameCn + "」装到下面的位置，并创建开始菜单快捷方式。",
                UiFonts.Body, UiTheme.TextSecondary, true);
            n1.SetBounds(Gap, 66, FormW - Gap * 2, 20);
            n1.FitHeight();
            p.Controls.Add(n1);

            LabelEx n2 = NewLabel("整个安装过程不需要管理员权限，也不会改动系统的输入法设置。",
                UiFonts.Body, UiTheme.TextSecondary, true);
            n2.SetBounds(Gap, 92, FormW - Gap * 2, 20);
            n2.FitHeight();
            p.Controls.Add(n2);

            LabelEx cap = NewLabel("安装位置", UiFonts.BodyBold, UiTheme.TextPrimary, false);
            cap.SetBounds(Gap, 138, 200, 20);
            p.Controls.Add(cap);

            _welcomePath = new FieldBox();
            _welcomePath.SetBounds(Gap, 160, FormW - Gap * 2, 30);
            _welcomePath.Text = _options.TargetDir;
            p.Controls.Add(_welcomePath);

            LabelEx n3 = NewLabel("想换个目录、改快捷方式或者不要开机自启？点右下角的「自定义」。",
                UiFonts.Small, UiTheme.TextSecondary, true);
            n3.SetBounds(Gap, 202, FormW - Gap * 2, 18);
            n3.FitHeight();
            p.Controls.Add(n3);

            return p;
        }

        Panel BuildUninstallWelcomePage()
        {
            Panel p = NewPage();

            LabelEx head = NewLabel("确定要卸载「" + Installer.ProductNameCn + "」吗？", UiFonts.H1, UiTheme.TextPrimary, false);
            head.SetBounds(Gap, 26, FormW - Gap * 2, 30);
            p.Controls.Add(head);

            string dir = Installer.InstalledDir;
            if (string.IsNullOrEmpty(dir)) dir = _options.TargetDir;

            LabelEx n1 = NewLabel("卸载会结束正在运行的程序，并删除程序文件和快捷方式。",
                UiFonts.Body, UiTheme.TextSecondary, true);
            n1.SetBounds(Gap, 66, FormW - Gap * 2, 20);
            n1.FitHeight();
            p.Controls.Add(n1);

            LabelEx n2 = NewLabel("你的设置和用户词库保存在 " + Installer.UserDataDir() + "，默认会保留下来。",
                UiFonts.Body, UiTheme.TextSecondary, true);
            n2.SetBounds(Gap, 92, FormW - Gap * 2, 34);
            n2.FitHeight();
            p.Controls.Add(n2);

            LabelEx cap = NewLabel("程序位置", UiFonts.BodyBold, UiTheme.TextPrimary, false);
            cap.SetBounds(Gap, 138, 200, 20);
            p.Controls.Add(cap);

            _confirmPath = new FieldBox();
            _confirmPath.SetBounds(Gap, 160, FormW - Gap * 2, 30);
            _confirmPath.Text = dir;
            p.Controls.Add(_confirmPath);

            _cbRemoveData = NewCheck("同时删除我的设置和用户词库（不可恢复）", _options.RemoveUserData, 204);
            p.Controls.Add(_cbRemoveData);

            return p;
        }

        Panel BuildOptionsPage()
        {
            Panel p = NewPage();

            LabelEx cap1 = NewLabel("安装位置", UiFonts.BodyBold, UiTheme.TextPrimary, false);
            cap1.SetBounds(Gap, 18, 200, 20);
            p.Controls.Add(cap1);

            int boxW = FormW - Gap * 2 - 116;
            _dirHost = new FieldPanel();
            _dirHost.SetBounds(Gap, 42, boxW, 28);
            p.Controls.Add(_dirHost);

            _dirBox = new TextBox();
            _dirBox.BorderStyle = BorderStyle.None;
            _dirBox.BackColor = UiTheme.FieldBack;
            _dirBox.ForeColor = UiTheme.TextPrimary;
            _dirBox.Font = UiFonts.Body;
            _dirBox.SetBounds(9, 5, boxW - 18, 18);
            _dirBox.Text = _options.TargetDir;
            _dirBox.TextChanged += new EventHandler(OnDirTextChanged);
            _dirHost.Controls.Add(_dirBox);

            _browse = new FlatButton();
            _browse.Primary = false;
            _browse.Text = "浏览…";
            _browse.Size = new Size(104, 28);
            _browse.Location = new Point(FormW - Gap - 104, 42);
            _browse.Click += new EventHandler(OnBrowseClick);
            p.Controls.Add(_browse);

            LabelEx cap2 = NewLabel("快捷方式和启动选项", UiFonts.BodyBold, UiTheme.TextPrimary, false);
            cap2.SetBounds(Gap, 84, 300, 20);
            p.Controls.Add(cap2);

            _cbDesktop = NewCheck("创建桌面快捷方式", _options.DesktopShortcut, 110);
            _cbStartMenu = NewCheck("创建开始菜单快捷方式", _options.StartMenuShortcut, 136);
            _cbAutoStart = NewCheck("开机自动启动", _options.AutoStart, 162);
            _cbLaunch = NewCheck("安装完成后立即启动" + Installer.ProductNameCn, _options.LaunchAfter, 188);
            p.Controls.Add(_cbDesktop);
            p.Controls.Add(_cbStartMenu);
            p.Controls.Add(_cbAutoStart);
            p.Controls.Add(_cbLaunch);

            if (Installer.IsAdmin)
            {
                _cbAllUsers = NewCheck("为所有用户安装（安装到 Program Files）", _options.AllUsers, 214);
                _cbAllUsers.CheckedChanged += new EventHandler(OnAllUsersChanged);
                p.Controls.Add(_cbAllUsers);
            }

            return p;
        }

        Panel BuildProgressPage()
        {
            Panel p = NewPage();

            _progressTitle = NewLabel(_uninstall ? ("正在卸载" + Installer.ProductNameCn + "，请稍候…") : ("正在安装" + Installer.ProductNameCn + "，请稍候…"),
                UiFonts.H1, UiTheme.TextPrimary, false);
            _progressTitle.SetBounds(Gap, 62, FormW - Gap * 2, 30);
            p.Controls.Add(_progressTitle);

            _bar = new FlatProgress();
            _bar.SetBounds(Gap, 112, FormW - Gap * 2, 8);
            Ui.MakeRounded(_bar, 4);
            p.Controls.Add(_bar);

            _status = NewLabel("正在准备…", UiFonts.Body, UiTheme.TextSecondary, true);
            _status.SetBounds(Gap, 134, FormW - Gap * 2, 40);
            p.Controls.Add(_status);

            LabelEx tip = NewLabel("处理过程中请不要关闭这个窗口。", UiFonts.Small, UiTheme.TextSecondary, false);
            tip.SetBounds(Gap, 210, FormW - Gap * 2, 20);
            p.Controls.Add(tip);

            return p;
        }

        Panel BuildDonePage()
        {
            Panel p = NewPage();

            _icon = new StatusIcon();
            _icon.Size = new Size(48, 48);
            _icon.Location = new Point((FormW - 48) / 2, 44);
            p.Controls.Add(_icon);

            _doneTitle = NewLabel("", UiFonts.H1, UiTheme.TextPrimary, false);
            _doneTitle.Center = true;
            _doneTitle.SetBounds(Gap, 106, FormW - Gap * 2, 30);
            p.Controls.Add(_doneTitle);

            _doneHint = NewLabel("", UiFonts.Body, UiTheme.TextSecondary, true);
            _doneHint.Center = true;
            _doneHint.SetBounds(Gap, 146, FormW - Gap * 2, 44);
            p.Controls.Add(_doneHint);

            _errLabel = NewLabel("", UiFonts.Body, UiTheme.Error, true);
            _errLabel.Center = true;
            _errLabel.Visible = false;
            _errLabel.SetBounds(Gap, 196, FormW - Gap * 2, 60);
            p.Controls.Add(_errLabel);

            return p;
        }

        // ==================== 步骤切换 ====================

        void ShowStep(int s)
        {
            _step = s;
            _pageWelcome.Visible = (s == 0);
            _pageOptions.Visible = (s == 1);
            _pageProgress.Visible = (s == 2);
            _pageDone.Visible = (s == 3);

            if (s == 2)
            {
                _primary.Text = _uninstall ? "正在卸载…" : "正在安装…";
                _primary.Enabled = false;
                _secondary.Visible = false;
            }
            else if (s == 1)
            {
                _primary.Text = "开始安装";
                _primary.Enabled = true;
                _secondary.Text = "上一步";
                _secondary.Visible = true;
            }
            else if (s == 3)
            {
                ApplyDoneState();
            }
            else
            {
                if (_uninstall)
                {
                    _primary.Text = "开始卸载";
                    _secondary.Text = "取消";
                }
                else
                {
                    _primary.Text = "开始安装";
                    _secondary.Text = "自定义";
                }
                _primary.Enabled = true;
                _secondary.Visible = true;
                RefreshPathText();
            }
        }

        void ApplyDoneState()
        {
            _primary.Text = "完成";
            _primary.Enabled = true;
            _secondary.Text = "重试";
            _secondary.Visible = _failed;
            _icon.Error = _failed;
            _icon.Invalidate();

            if (_failed)
            {
                _doneTitle.Text = _uninstall ? "卸载未能完成" : "安装失败";
                _errLabel.Text = "错误原因：" + (_errorText == null ? "未知错误" : _errorText);
                _errLabel.Visible = true;
                _doneHint.Text = "请确认安装目录里的文件没有被其它程序占用，然后重试。也可以用管理员身份重新运行安装程序。";
            }
            else
            {
                _doneTitle.Text = _uninstall ? "卸载完成" : "安装完成";
                _errLabel.Visible = false;
                _errLabel.Text = "";
                _doneHint.Text = DoneHintText();
            }
            _doneHint.FitHeight();
        }

        string DoneHintText()
        {
            StringBuilder sb = new StringBuilder();
            if (_uninstall)
            {
                sb.Append("程序文件和快捷方式已经移除。");
                if (_options.RemoveUserData) sb.Append("你的设置和用户词库也已删除。");
                else sb.Append("你的设置和用户词库保留在 " + Installer.UserDataDir() + "。");
            }
            else
            {
                sb.Append("已安装到 " + _options.TargetDir + "。");
                if (_options.AutoStart) sb.Append("已设置为开机自动启动。");
                sb.Append("按 Ctrl+Alt+E 切换中英文，Ctrl+Alt+A 打开 AI 面板。");
            }
            return sb.ToString();
        }

        void RefreshPathText()
        {
            string dir = _options.TargetDir;
            if (_welcomePath != null) _welcomePath.Text = dir;
            if (_confirmPath != null && !string.IsNullOrEmpty(dir)) _confirmPath.Text = dir;
        }

        // ==================== 交互 ====================

        void OnPrimaryClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_step == 3)
            {
                Close();
                return;
            }
            if (_step == 0 || _step == 1)
            {
                StartWork();
            }
        }

        void OnSecondaryClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_step == 0)
            {
                if (_uninstall) Close();
                else ShowStep(1);
            }
            else if (_step == 1)
            {
                ShowStep(0);
            }
            else if (_step == 3)
            {
                ShowStep(_uninstall ? 0 : 1);
            }
        }

        void OnDirTextChanged(object sender, EventArgs e)
        {
            if (_dirBox == null) return;
            string t = _dirBox.Text;
            if (t != null) _options.TargetDir = t.Trim();
            RefreshPathText();
        }

        void OnAllUsersChanged(object sender, EventArgs e)
        {
            if (_cbAllUsers == null || _dirBox == null) return;

            string before = Installer.DefaultTargetDir(_options.AllUsers);
            _options.AllUsers = _cbAllUsers.Checked;
            string after = Installer.DefaultTargetDir(_options.AllUsers);

            string cur = _dirBox.Text == null ? "" : _dirBox.Text.Trim();
            if (cur.Length == 0 || Installer.SamePath(cur, before))
            {
                _dirBox.Text = after;
                _options.TargetDir = after;
            }
            else
            {
                _options.TargetDir = cur;
            }
            RefreshPathText();
        }

        void OnBrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择" + Installer.ProductNameCn + "的安装目录";
                dlg.ShowNewFolderButton = true;
                string cur = _dirBox.Text == null ? "" : _dirBox.Text.Trim();
                if (cur.Length > 0 && Directory.Exists(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    _dirBox.Text = dlg.SelectedPath;
                    _options.TargetDir = dlg.SelectedPath;
                    RefreshPathText();
                }
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && !_busy)
            {
                if (_step == 3 || (_step == 0 && _uninstall)) Close();
                else if (_step == 1) ShowStep(0);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (_status != null) _status.Text = "正在处理中，完成后才能关闭窗口…";
                return;
            }
            base.OnFormClosing(e);
        }

        // ==================== 安装 / 卸载执行 ====================

        void CollectOptions()
        {
            if (_dirBox != null)
            {
                string t = _dirBox.Text == null ? "" : _dirBox.Text.Trim().Trim('"');
                if (t.Length > 0) _options.TargetDir = t;
            }
            if (_cbDesktop != null) _options.DesktopShortcut = _cbDesktop.Checked;
            if (_cbStartMenu != null) _options.StartMenuShortcut = _cbStartMenu.Checked;
            if (_cbAutoStart != null) _options.AutoStart = _cbAutoStart.Checked;
            if (_cbLaunch != null) _options.LaunchAfter = _cbLaunch.Checked;
            if (_cbAllUsers != null) _options.AllUsers = _cbAllUsers.Checked;
            if (_cbRemoveData != null) _options.RemoveUserData = _cbRemoveData.Checked;
        }

        string ValidateOptions()
        {
            if (_uninstall)
            {
                if (string.IsNullOrEmpty(_options.TargetDir)) return null;   // 让卸载逻辑自己从注册表找
                return null;
            }

            string dir = _options.TargetDir;
            if (string.IsNullOrEmpty(dir) || dir.Trim().Length == 0) return "请填写安装目录。";

            try
            {
                string full = Path.GetFullPath(dir);
                string root = Path.GetPathRoot(full);
                if (root != null && full.TrimEnd(new char[] { '\\', '/' }).Length == root.TrimEnd(new char[] { '\\', '/' }).Length)
                {
                    return "不能把" + Installer.ProductNameCn + "直接装到磁盘根目录，请换一个文件夹。";
                }
                _options.TargetDir = Installer.NormalizeDir(full);
            }
            catch (Exception ex)
            {
                return "安装目录无效：" + ex.Message;
            }

            if (_options.AllUsers && !Installer.IsAdmin)
            {
                return "为所有用户安装需要管理员权限。请取消勾选这一项，或者以管理员身份重新运行安装程序。";
            }
            return null;
        }

        void StartWork()
        {
            CollectOptions();
            string err = ValidateOptions();
            if (err != null)
            {
                MessageBox.Show(this, err, Installer.ProductNameCn + " 安装程序",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _failed = false;
            _errorText = null;
            _busy = true;
            ExitCode = 0;
            _bar.SetValue(0);
            _status.Text = "正在准备…";
            ShowStep(2);

            Thread t = new Thread(new ThreadStart(WorkThread));
            t.IsBackground = true;
            t.Name = "MyImeSetupWork";
            t.Start();
        }

        void WorkThread()
        {
            string err = null;
            try
            {
                Action<int, string> progress = new Action<int, string>(OnProgress);
                if (_uninstall) Installer.Uninstall(_options, progress);
                else Installer.Install(_options, progress);
            }
            catch (Exception ex)
            {
                err = ex.Message;
                Log.Error("安装/卸载失败", ex);
            }

            try
            {
                BeginInvoke(new Action(delegate () { OnWorkFinished(err); }));
            }
            catch { }
        }

        void OnProgress(int pct, string msg)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new Action(delegate ()
                {
                    if (IsDisposed) return;
                    _bar.SetValue(pct);
                    if (!string.IsNullOrEmpty(msg)) _status.Text = msg;
                }));
            }
            catch { }
        }

        void OnWorkFinished(string err)
        {
            if (IsDisposed) return;
            _busy = false;
            _failed = (err != null);
            _errorText = err;
            ExitCode = _failed ? 1 : 0;
            if (!_failed) _bar.SetValue(100);

            _welcomePath.Text = _options.TargetDir;
            if (_confirmPath != null) _confirmPath.Text = _options.TargetDir;
            ShowStep(3);
        }

        // ==================== 自绘分区 ====================

        /// <summary>顶部标题区：绿色圆角 Logo + 产品名 + 副标题 + 分隔线。</summary>
        private class HeadPanel : Panel
        {
            public string Title = "";
            public string SubTitle = "";

            static readonly Font TitleFont = UiFonts.Make(UiFonts.Body, 14f, FontStyle.Bold);
            static readonly Font SubFont = UiFonts.Make(UiFonts.Body, 8.25f, FontStyle.Regular);

            public HeadPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = UiTheme.PageBack;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(UiTheme.PageBack)) g.FillRectangle(b, ClientRectangle);

                Rectangle logo = new Rectangle(20, 16, 32, 32);
                Ui.FillRound(g, logo, 8, UiTheme.Primary);
                TextRenderer.DrawText(g, "松", UiFonts.Logo, logo, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                Rectangle tr = new Rectangle(64, 14, Width - 84, 26);
                TextRenderer.DrawText(g, Title, TitleFont, tr, UiTheme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                Rectangle sr = new Rectangle(64, 38, Width - 84, 18);
                TextRenderer.DrawText(g, SubTitle, SubFont, sr, UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                using (Pen pen = new Pen(UiTheme.Border))
                {
                    g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            }
        }

        /// <summary>底部按钮区：浅灰底 + 顶部分隔线。</summary>
        private class FooterPanel : Panel
        {
            public FooterPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = UiTheme.FooterBack;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(UiTheme.FooterBack)) e.Graphics.FillRectangle(b, ClientRectangle);
                using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }
        }

        /// <summary>圆角输入框容器（给没有边框的 TextBox 当外壳）。</summary>
        private class FieldPanel : Panel
        {
            public int Radius = 4;

            public FieldPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = UiTheme.FieldBack;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 0 || r.Height <= 0) return;
                Ui.FillRound(g, r, Radius, UiTheme.FieldBack);
                Ui.DrawRound(g, r, Radius, UiTheme.Border, 1f);
            }
        }
    }
}
