using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MyIme.Ai;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>微信风格的胶囊按钮/标签。既当"风格选择"用，也当按钮用。</summary>
    public sealed class Pill : Control
    {
        public string PillId = "";
        public bool Selected;
        public bool Primary;
        public bool Small;
        public Color Accent = Color.FromArgb(0x07, 0xC1, 0x60);
        public Color CardBg = Color.White;
        public Color NormalBg = Color.FromArgb(0xF2, 0xF2, 0xF2);
        public Color NormalFg = Color.FromArgb(0x33, 0x33, 0x33);
        public Color SubFg = Color.FromArgb(0x8C, 0x8C, 0x8C);
        public Color LineColor = Color.FromArgb(0xE5, 0xE5, 0xE5);
        public bool Enabled2 = true;

        bool _hover;
        bool _down;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Height = 30;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        public void AutoWidth()
        {
            using (Graphics g = CreateGraphics())
            {
                Size s = TextRenderer.MeasureText(g, Text, Font);
                Width = s.Width + (Small ? 22 : 30);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, fg;
            if (!Enabled2) { bg = NormalBg; fg = SubFg; }
            else if (Primary)
            {
                bg = Accent; fg = Color.White;
                if (_down) bg = Darken(Accent, 0.12f);
                else if (_hover) bg = Lighten(Accent, 0.08f);
            }
            else if (Selected)
            {
                bg = Accent; fg = Color.White;
                if (_down) bg = Darken(Accent, 0.12f);
            }
            else
            {
                bg = NormalBg; fg = NormalFg;
                if (_hover) bg = Darken(NormalBg, 0.05f);
                if (_down) bg = Darken(NormalBg, 0.10f);
            }

            using (GraphicsPath p = Round(r, Height / 2))
            using (SolidBrush b = new SolidBrush(bg))
                g.FillPath(b, p);

            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
        }

        public static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }
    }

    /// <summary>
    /// AI 面板：输入条上弹出来的那个"向 AI 提要求"的窗口。
    ///
    /// 两个模式：
    ///   自动润色 —— 自动读取当前输入框的文字，选一个润色风格直接改
    ///   指令输入 —— 用户自己写一句要求（"改正式点"/"再写两句"），对框内文字做 润色/扩写/续写
    /// </summary>
    public sealed class AiPanel : Form
    {
        public const string ModeAuto = "auto";
        public const string ModeCommand = "command";

        // ---- 事件 ----
        public event Action<string, string, string> RunRequested;   // mode, id(预设或动作), requirement
        public event Action<bool> ApplyRequested;                   // true=替换原文, false=插入
        public event Action PanelClosedClicked;

        // ---- 配色 ----
        Color _card = Color.White;
        Color _bg = Color.FromArgb(0xF7, 0xF7, 0xF7);
        Color _fg = Color.FromArgb(0x1A, 0x1A, 0x1A);
        Color _sub = Color.FromArgb(0x8C, 0x8C, 0x8C);
        Color _line = Color.FromArgb(0xE8, 0xE8, 0xE8);
        Color _accent = Color.FromArgb(0x07, 0xC1, 0x60);

        // ---- 控件 ----
        Label _title;
        Pill _closeBtn;
        Pill _tabAuto;
        Pill _tabCmd;
        Panel _autoPage;
        Panel _cmdPage;
        Label _srcLabel;
        Label _srcPreview;
        Label _presetLabel;
        TextBox _requirement;
        Label _actionLabel;
        Label _resultLabel;
        TextBox _result;
        Label _status;
        Pill _runBtn;
        Pill _applyBtn;
        Pill _insertBtn;
        Pill _copyBtn;
        Pill _retryBtn;

        readonly List<Pill> _presetPills = new List<Pill>();
        readonly List<Pill> _actionPills = new List<Pill>();

        string _mode = ModeAuto;
        string _selectedPreset = "concise";
        string _selectedAction = "polish";
        string _sourceText = "";
        bool _busy;
        string _lastRequirement = "";

        public AiPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "AI 写作助手";
            Size = new Size(580, 430);
            BackColor = _bg;
            KeyPreview = true;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            BuildUi();
        }

        // ==================== 界面 ====================

        void BuildUi()
        {
            _title = new Label();
            _title.Text = "AI 写作助手";
            _title.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
            _title.ForeColor = _fg;
            _title.AutoSize = true;
            _title.Location = new Point(18, 16);
            Controls.Add(_title);

            _closeBtn = new Pill();
            _closeBtn.Text = "关闭";
            _closeBtn.Small = true;
            _closeBtn.Accent = _accent;
            _closeBtn.NormalBg = _line;
            _closeBtn.Height = 26;
            _closeBtn.Click += delegate { RaiseClosed(); };
            Controls.Add(_closeBtn);

            _tabAuto = new Pill();
            _tabAuto.Text = "自动读取修改";
            _tabAuto.PillId = ModeAuto;
            _tabAuto.Selected = true;
            _tabAuto.Accent = _accent;
            _tabAuto.NormalBg = _line;
            _tabAuto.Click += delegate { SetMode(ModeAuto); };
            Controls.Add(_tabAuto);

            _tabCmd = new Pill();
            _tabCmd.Text = "输入要求改写";
            _tabCmd.PillId = ModeCommand;
            _tabCmd.Accent = _accent;
            _tabCmd.NormalBg = _line;
            _tabCmd.Click += delegate { SetMode(ModeCommand); };
            Controls.Add(_tabCmd);

            // ---- 自动模式页 ----
            _autoPage = new Panel();
            _autoPage.BackColor = _card;
            Controls.Add(_autoPage);

            _srcLabel = MakeLabel("读取到的原文", _sub, 9f, false);
            _autoPage.Controls.Add(_srcLabel);

            _srcPreview = MakeLabel("", _fg, 9.5f, false);
            _autoPage.Controls.Add(_srcPreview);

            _presetLabel = MakeLabel("选择润色效果", _sub, 9f, false);
            _autoPage.Controls.Add(_presetLabel);

            // ---- 指令模式页 ----
            _cmdPage = new Panel();
            _cmdPage.BackColor = _card;
            _cmdPage.Visible = false;
            Controls.Add(_cmdPage);

            _requirement = new TextBox();
            _requirement.Multiline = true;
            _requirement.BorderStyle = BorderStyle.FixedSingle;
            _requirement.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            _requirement.ScrollBars = ScrollBars.Vertical;
            _requirement.Text = "";
            _cmdPage.Controls.Add(_requirement);

            _actionLabel = MakeLabel("要对原文做什么", _sub, 9f, false);
            _cmdPage.Controls.Add(_actionLabel);

            // ---- 结果区 ----
            _resultLabel = MakeLabel("AI 结果", _sub, 9f, false);
            Controls.Add(_resultLabel);

            _result = new TextBox();
            _result.Multiline = true;
            _result.ReadOnly = true;
            _result.BorderStyle = BorderStyle.None;
            _result.BackColor = _card;
            _result.ForeColor = _fg;
            _result.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            _result.ScrollBars = ScrollBars.Vertical;
            Controls.Add(_result);

            // ---- 底部 ----
            _status = MakeLabel("", _sub, 9f, false);
            Controls.Add(_status);

            _runBtn = MakeButton("开始润色", true);
            _runBtn.Click += delegate { Run(); };
            Controls.Add(_runBtn);

            _retryBtn = MakeButton("重试", false);
            _retryBtn.Click += delegate { Run(); };
            Controls.Add(_retryBtn);

            _applyBtn = MakeButton("替换原文", false);
            _applyBtn.Click += delegate { RaiseApply(true); };
            Controls.Add(_applyBtn);

            _insertBtn = MakeButton("插入", false);
            _insertBtn.Click += delegate { RaiseApply(false); };
            Controls.Add(_insertBtn);

            _copyBtn = MakeButton("复制", false);
            _copyBtn.Click += delegate { CopyResult(); };
            Controls.Add(_copyBtn);

            foreach (Pill p in _presetPills) p.Dispose();
            _presetPills.Clear();
            foreach (PolishPreset preset in PromptLibrary.Presets)
            {
                PolishPreset captured = preset;
                Pill p = new Pill();
                p.Text = preset.Name;
                p.PillId = preset.Id;
                p.Small = true;
                p.Height = 28;
                p.Accent = _accent;
                p.NormalBg = _line;
                p.Selected = (preset.Id == _selectedPreset);
                p.Click += delegate { _selectedPreset = captured.Id; RefreshPresetSelection(); };
                _autoPage.Controls.Add(p);
                _presetPills.Add(p);
            }

            foreach (Pill p in _actionPills) p.Dispose();
            _actionPills.Clear();
            foreach (AiAction action in PromptLibrary.Actions)
            {
                AiAction captured = action;
                Pill p = new Pill();
                p.Text = action.Name;
                p.PillId = action.Id;
                p.Small = true;
                p.Height = 28;
                p.Accent = _accent;
                p.NormalBg = _line;
                p.Selected = (action.Id == _selectedAction);
                p.Click += delegate { _selectedAction = captured.Id; RefreshActionSelection(); };
                _cmdPage.Controls.Add(p);
                _actionPills.Add(p);
            }

            DoLayout();
        }

        Label MakeLabel(string text, Color color, float size, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        Pill MakeButton(string text, bool primary)
        {
            Pill p = new Pill();
            p.Text = text;
            p.Primary = primary;
            p.Accent = _accent;
            p.NormalBg = _line;
            return p;
        }

        public void ApplyTheme(UiSettings ui)
        {
            bool dark = ui != null && ui.FollowDarkMode && CandidateBar.DetectDark();
            if (dark)
            {
                _card = Color.FromArgb(0x2B, 0x2B, 0x2B);
                _bg = Color.FromArgb(0x1F, 0x1F, 0x1F);
                _fg = Color.FromArgb(0xF2, 0xF2, 0xF2);
                _sub = Color.FromArgb(0x9A, 0x9A, 0x9A);
                _line = Color.FromArgb(0x3A, 0x3A, 0x3A);
            }
            else
            {
                _card = Color.White;
                _bg = Color.FromArgb(0xF7, 0xF7, 0xF7);
                _fg = Color.FromArgb(0x1A, 0x1A, 0x1A);
                _sub = Color.FromArgb(0x8C, 0x8C, 0x8C);
                _line = Color.FromArgb(0xE8, 0xE8, 0xE8);
            }
            BackColor = _bg;
            if (ui != null) _accent = ParseColor(ui.AccentColor, Color.FromArgb(0x07, 0xC1, 0x60));

            _title.ForeColor = _fg;
            _srcLabel.ForeColor = _sub;
            _srcPreview.ForeColor = _fg;
            _presetLabel.ForeColor = _sub;
            _actionLabel.ForeColor = _sub;
            _resultLabel.ForeColor = _sub;
            _result.BackColor = _card;
            _result.ForeColor = _fg;
            _autoPage.BackColor = _card;
            _cmdPage.BackColor = _card;
            _requirement.BackColor = _card;
            _requirement.ForeColor = _fg;

            foreach (Pill p in _presetPills) { p.Accent = _accent; p.NormalBg = _line; p.NormalFg = _fg; p.Invalidate(); }
            foreach (Pill p in _actionPills) { p.Accent = _accent; p.NormalBg = _line; p.NormalFg = _fg; p.Invalidate(); }
            foreach (Pill p in new Pill[] { _closeBtn, _runBtn, _retryBtn, _applyBtn, _insertBtn, _copyBtn, _tabAuto, _tabCmd })
            { p.Accent = _accent; p.NormalBg = _line; p.NormalFg = _fg; p.Invalidate(); }
        }

        static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                string h = hex.Trim().TrimStart('#');
                if (h.Length == 6)
                    return Color.FromArgb(
                        Convert.ToInt32(h.Substring(0, 2), 16),
                        Convert.ToInt32(h.Substring(2, 2), 16),
                        Convert.ToInt32(h.Substring(4, 2), 16));
            }
            catch { }
            return fallback;
        }

        void RefreshPresetSelection()
        {
            foreach (Pill p in _presetPills) { p.Selected = (p.PillId == _selectedPreset); p.Invalidate(); }
        }

        void RefreshActionSelection()
        {
            foreach (Pill p in _actionPills) { p.Selected = (p.PillId == _selectedAction); p.Invalidate(); }
            _runBtn.Text = _selectedAction == "expand" ? "开始扩写"
                         : _selectedAction == "continue" ? "开始续写"
                         : "开始处理";
            _runBtn.AutoWidth();
        }

        void DoLayout()
        {
            int pad = 18;
            _title.Location = new Point(pad, 16);

            _tabAuto.AutoWidth();
            _tabCmd.AutoWidth();
            _tabAuto.Location = new Point(pad, 48);
            _tabCmd.Location = new Point(_tabAuto.Right + 8, 48);

            _closeBtn.AutoWidth();
            _closeBtn.Location = new Point(Width - pad - _closeBtn.Width - 8, 46);

            int contentTop = 90;
            int contentW = Width - pad * 2;

            _autoPage.Location = new Point(pad, contentTop);
            _autoPage.Size = new Size(contentW, 120);

            _srcLabel.Location = new Point(14, 10);
            _srcPreview.Location = new Point(14, 32);
            _srcPreview.MaximumSize = new Size(contentW - 28, 40);
            _presetLabel.Location = new Point(14, 74);

            int px = 14, py = 92;
            int lineH = 34;
            foreach (Pill p in _presetPills)
            {
                p.AutoWidth();
                if (px + p.Width > contentW - 8) { px = 14; py += lineH; }
                p.Location = new Point(px, py);
                p.Height = 28;
                px += p.Width + 8;
            }

            _cmdPage.Location = new Point(pad, contentTop);
            _cmdPage.Size = new Size(contentW, 120);

            _requirement.Location = new Point(14, 10);
            _requirement.Size = new Size(contentW - 28, 46);
            _actionLabel.Location = new Point(14, 62);

            px = 14; py = 80;
            foreach (Pill p in _actionPills)
            {
                p.AutoWidth();
                if (px + p.Width > contentW - 8) { px = 14; py += lineH; }
                p.Location = new Point(px, py);
                p.Height = 28;
                px += p.Width + 8;
            }

            int resultTop = contentTop + 130;
            _resultLabel.Location = new Point(pad, resultTop);
            _result.Location = new Point(pad, resultTop + 22);
            _result.Size = new Size(contentW, Height - resultTop - 22 - 66);

            int footY = Height - 46;
            _status.Location = new Point(pad, footY + 8);
            _status.MaximumSize = new Size(200, 20);

            int bx = Width - pad;
            foreach (Pill p in new Pill[] { _copyBtn, _insertBtn, _applyBtn, _retryBtn, _runBtn })
            {
                p.AutoWidth();
                p.Height = 32;
                bx -= p.Width;
                p.Location = new Point(bx, footY);
                bx -= 8;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_title != null) DoLayout();
        }

        // ==================== 使用 ====================

        public void Prepare(bool commandMode, string sourceText, AiSettings ai, UiSettings ui)
        {
            _sourceText = sourceText == null ? "" : sourceText;
            _lastRequirement = "";
            _result.Text = "";
            _status.Text = "";
            _busy = false;

            if (ai != null && !string.IsNullOrEmpty(ai.AutoPreset)) _selectedPreset = ai.AutoPreset;

            string preview = _sourceText.Length == 0
                ? "(没有读到输入框里的文字，可以直接在下面写要求)"
                : (_sourceText.Length > 160 ? _sourceText.Substring(0, 160) + "…" : _sourceText);
            _srcPreview.Text = preview;

            ApplyTheme(ui);
            RefreshPresetSelection();
            RefreshActionSelection();
            SetMode(commandMode ? ModeCommand : ModeAuto);
            UpdateButtons();

            _requirement.Text = "";
            if (commandMode) { try { _requirement.Focus(); } catch { } }
        }

        public void SetMode(string mode)
        {
            _mode = mode;
            bool auto = (mode == ModeAuto);
            _tabAuto.Selected = auto;
            _tabCmd.Selected = !auto;
            _tabAuto.Invalidate();
            _tabCmd.Invalidate();
            _autoPage.Visible = auto;
            _cmdPage.Visible = !auto;
            _runBtn.Text = auto ? "开始润色" : (_selectedAction == "expand" ? "开始扩写"
                            : _selectedAction == "continue" ? "开始续写" : "开始处理");
            _runBtn.AutoWidth();
            DoLayout();
        }

        public string CurrentMode { get { return _mode; } }

        void Run()
        {
            if (_busy) return;
            string requirement = _mode == ModeCommand ? _requirement.Text.Trim() : "";
            if (_mode == ModeCommand && requirement.Length == 0)
            {
                SetStatus("请先写一句要求，比如“改得更正式一点”", true);
                try { _requirement.Focus(); } catch { }
                return;
            }
            _lastRequirement = requirement;
            string id = (_mode == ModeAuto) ? _selectedPreset : _selectedAction;
            Action<string, string, string> h = RunRequested;
            if (h != null) h(_mode, id, requirement);
        }

        public void BeginResult()
        {
            _busy = true;
            _result.Text = "";
            SetStatus("正在请求 AI…", false);
            UpdateButtons();
        }

        public void AppendResult(string text)
        {
            _result.Text = text;
            try
            {
                _result.SelectionStart = _result.TextLength;
                _result.ScrollToCaret();
            }
            catch { }
        }

        public void EndResult(AiResult r, string providerNote)
        {
            _busy = false;
            if (r != null && r.Ok)
            {
                SetStatus("完成 · " + providerNote, false);
            }
            else
            {
                SetStatus(r == null ? "请求失败" : r.Error, true);
            }
            UpdateButtons();
        }

        public void SetStatus(string text, bool error)
        {
            _status.Text = text == null ? "" : text;
            _status.ForeColor = error ? Color.FromArgb(0xE0, 0x43, 0x3B) : _sub;
            _status.MaximumSize = new Size(Width - 340, 20);
        }

        void UpdateButtons()
        {
            bool has = _result.TextLength > 0 && !_busy;
            _applyBtn.Enabled2 = has;
            _insertBtn.Enabled2 = has;
            _copyBtn.Enabled2 = has;
            _retryBtn.Enabled2 = !_busy;
            _runBtn.Enabled2 = !_busy;
            foreach (Pill p in new Pill[] { _applyBtn, _insertBtn, _copyBtn, _retryBtn, _runBtn }) p.Invalidate();
        }

        void CopyResult()
        {
            try
            {
                if (_result.TextLength == 0) return;
                Clipboard.SetText(_result.Text);
                SetStatus("已复制到剪贴板", false);
            }
            catch (Exception ex) { SetStatus("复制失败：" + ex.Message, true); }
        }

        void RaiseApply(bool replace)
        {
            if (_result.TextLength == 0) return;
            Action<bool> h = ApplyRequested;
            if (h != null) h(replace);
        }

        void RaiseClosed()
        {
            Action h = PanelClosedClicked;
            if (h != null) h();
            else Close();
        }

        public string ResultText { get { return _result.Text; } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.Handled = true; RaiseClosed(); }
            else if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                if (_mode == ModeCommand) Run();
            }
        }

        protected override bool ShowWithoutActivation { get { return false; } }
    }
}
