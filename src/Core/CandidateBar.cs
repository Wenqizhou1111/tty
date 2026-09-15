using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Core
{
    public class BarEntry
    {
        public string Index;      // "1"
        public string Text;
        public string Note;       // 右侧小字，例如"联想"
    }

    /// <summary>
    /// 候选输入条（参考微信输入法的悬浮条）。
    ///
    /// 关键点：这个窗口**永远不能抢焦点**，否则用户打字会中断。
    /// 做法是三层保险：
    ///   1. 扩展样式 WS_EX_NOACTIVATE
    ///   2. 重写 ShowWithoutActivation 返回 true
    ///   3. 拦 WM_MOUSEACTIVATE 返回 MA_NOACTIVATE（点候选也不会激活）
    /// </summary>
    public sealed class CandidateBar : Form
    {
        // ---- Win32 常量 ----
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_TOPMOST = 0x00000008;
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;

        // ---- 内容状态 ----
        string _badge = "中";
        Color _badgeColor = Color.FromArgb(0x07, 0xC1, 0x60);
        string _buffer = "";
        List<BarEntry> _items = new List<BarEntry>();
        int _selected = 0;
        int _page = 0;
        int _pageCount = 1;
        bool _showBuffer = true;

        // ---- 样式 ----
        Color _bg = Color.White;
        Color _border = Color.FromArgb(0xE2, 0xE2, 0xE2);
        Color _textColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
        Color _subColor = Color.FromArgb(0x99, 0x99, 0x99);
        Color _selBg = Color.FromArgb(0xE8, 0xF8, 0xEF);
        int _fontSize = 16;
        int _barMaxWidth = 960;
        bool _showIndex = true;
        bool _showPage = true;
        bool _rounded = true;
        int _offsetY = 26;

        float _dpiScale = 1.0f;

        // ---- 命中测试 ----
        readonly List<Rectangle> _hitRects = new List<Rectangle>();
        readonly List<int> _hitIndexes = new List<int>();

        public event Action<int> CandidateClicked;      // 传全局候选序号
        public event Action<int> PageRequested;         // +1 / -1
        public event Action AiButtonClicked;            // 点了输入条上的 AI 按钮

        /// <summary>输入条最右侧是否显示「AI」按钮（AI 总开关打开且用户没关这个显示项）。</summary>
        public bool ShowAiButton = false;

        Rectangle _aiRect = Rectangle.Empty;

        public CandidateBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = _bg;
            Text = "MyIME 输入条";
            Size = new Size(300, 44);
            Visible = false;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // 点我别抢焦点
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // ==================== 外观设置 ====================

        public void ApplyUiSettings(UiSettings ui)
        {
            if (ui == null) return;
            _fontSize = Math.Max(10, Math.Min(40, ui.FontSize));
            _barMaxWidth = Math.Max(300, ui.BarMaxWidth);
            _showIndex = ui.ShowIndex;
            _showPage = ui.ShowPageIndicator;
            _rounded = ui.RoundedCorners;
            _offsetY = ui.BarOffsetY;
            _badgeColor = ParseColor(ui.AccentColor, Color.FromArgb(0x07, 0xC1, 0x60));
            _selBg = BlendWith(_badgeColor, Color.White, 0.90f);
            ApplyTheme(ui.FollowDarkMode ? DetectDark() : false);
        }

        public static bool DetectDark()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v is int) return ((int)v) == 0;
            }
            catch { }
            return false;
        }

        public void ApplyTheme(bool dark)
        {
            if (dark)
            {
                _bg = Color.FromArgb(0x2B, 0x2B, 0x2B);
                _border = Color.FromArgb(0x44, 0x44, 0x44);
                _textColor = Color.FromArgb(0xF2, 0xF2, 0xF2);
                _subColor = Color.FromArgb(0x8A, 0x8A, 0x8A);
                _selBg = BlendWith(_badgeColor, Color.FromArgb(0x2B, 0x2B, 0x2B), 0.78f);
            }
            else
            {
                _bg = Color.White;
                _border = Color.FromArgb(0xE2, 0xE2, 0xE2);
                _textColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
                _subColor = Color.FromArgb(0x99, 0x99, 0x99);
                _selBg = BlendWith(_badgeColor, Color.White, 0.90f);
            }
            BackColor = _bg;
            Invalidate();
        }

        static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                string h = hex.Trim().TrimStart('#');
                if (h.Length == 6)
                {
                    int r = Convert.ToInt32(h.Substring(0, 2), 16);
                    int g = Convert.ToInt32(h.Substring(2, 2), 16);
                    int b = Convert.ToInt32(h.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { }
            return fallback;
        }

        static Color BlendWith(Color a, Color b, float amountOfB)
        {
            float t = Math.Max(0f, Math.Min(1f, amountOfB));
            return Color.FromArgb(
                (int)(a.R * (1 - t) + b.R * t),
                (int)(a.G * (1 - t) + b.G * t),
                (int)(a.B * (1 - t) + b.B * t));
        }

        // ==================== 显示内容 ====================

        public void SetContent(string badge, string buffer, List<BarEntry> items,
                               int selected, int page, int pageCount, bool showBuffer)
        {
            _badge = badge;
            _buffer = buffer == null ? "" : buffer;
            _items = items == null ? new List<BarEntry>() : items;
            _selected = selected;
            _page = page;
            _pageCount = pageCount;
            _showBuffer = showBuffer;
            LayoutAndPaint();
        }

        void LayoutAndPaint()
        {
            _dpiScale = GetDpiScale();
            Font font = CreateFont(_fontSize);
            Font indexFont = CreateFont(Math.Max(9, _fontSize - 4));
            Font badgeFont = CreateFont(Math.Max(9, _fontSize - 4), true);
            Font noteFont = CreateFont(Math.Max(8, _fontSize - 5));

            using (Graphics g = CreateGraphics())
            {
                int padX = (int)(14 * _dpiScale);
                int padY = (int)(9 * _dpiScale);
                int gap = (int)(16 * _dpiScale);

                int width = padX * 2;
                int needed = 0;

                Size badgeSize = TextRenderer.MeasureText(g, _badge, badgeFont);
                int badgeW = badgeSize.Width + (int)(14 * _dpiScale);
                int badgeH = badgeSize.Height + (int)(6 * _dpiScale);
                width += badgeW + (int)(8 * _dpiScale);

                if (_showBuffer && _buffer.Length > 0)
                {
                    Size bsz = TextRenderer.MeasureText(g, _buffer, font);
                    width += bsz.Width + gap;
                    needed = Math.Max(needed, bsz.Height + 12);
                }

                for (int i = 0; i < _items.Count; i++)
                {
                    BarEntry e = _items[i];
                    string label = _showIndex ? (e.Index + " ") : "";
                    Size isz = TextRenderer.MeasureText(g, label, indexFont);
                    Size tsz = TextRenderer.MeasureText(g, e.Text, font);
                    int w = isz.Width + tsz.Width;
                    if (!string.IsNullOrEmpty(e.Note))
                        w += TextRenderer.MeasureText(g, e.Note, noteFont).Width + (int)(6 * _dpiScale);
                    width += w + gap;
                    needed = Math.Max(needed, tsz.Height + 12);
                }

                if (_showPage && _pageCount > 1)
                {
                    Size psz = TextRenderer.MeasureText(g, (_page + 1) + "/" + _pageCount, noteFont);
                    width += psz.Width + gap;
                    needed = Math.Max(needed, psz.Height + 12);
                }

                if (ShowAiButton)
                {
                    Size asz = TextRenderer.MeasureText(g, "AI", badgeFont);
                    width += asz.Width + (int)(16 * _dpiScale) + gap;
                    needed = Math.Max(needed, asz.Height + 12);
                }

                width += padX;
                int height = Math.Max(needed, badgeH + (int)(6 * _dpiScale)) + padY * 2;

                int maxW = (int)(_barMaxWidth * _dpiScale);
                if (width > maxW) width = maxW;
                if (height < (int)(40 * _dpiScale)) height = (int)(40 * _dpiScale);

                Size = new Size(width, height);

                font.Dispose(); indexFont.Dispose(); badgeFont.Dispose(); noteFont.Dispose();
            }

            Reposition();
            ApplyRoundRegion();
            Invalidate();
        }

        Font CreateFont(int size, bool bold = false)
        {
            string[] names = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun" };
            foreach (string n in names)
            {
                try
                {
                    Font f = new Font(n, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                    if (f != null) return f;
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        float GetDpiScale()
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(Handle);
                if (dpi >= 72 && dpi <= 480) return dpi / 96.0f;
            }
            catch { }
            return 1.0f;
        }

        void ApplyRoundRegion()
        {
            if (!_rounded) return;
            try
            {
                int r = (int)(10 * _dpiScale);
                IntPtr hRgn = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r * 2, r * 2);
                if (hRgn != IntPtr.Zero) Native.SetWindowRgn(Handle, hRgn, true);
            }
            catch { }
        }

        /// <summary>贴到光标下方，并保证不跑出屏幕。</summary>
        public void Reposition()
        {
            try
            {
                RECT caret;
                int x, y;
                if (CaretLocator.TryGetCaretRect(out caret))
                {
                    x = caret.Left;
                    y = caret.Bottom + (int)(_offsetY * _dpiScale) - (int)(16 * _dpiScale);
                }
                else
                {
                    POINT p = CaretLocator.FallbackPoint();
                    x = p.X;
                    y = p.Y;
                }

                RECT monitor, work;
                if (!CaretLocator.TryGetMonitorInfo(x, y, out monitor, out work))
                {
                    work.Left = 0; work.Top = 0;
                    work.Right = Screen.PrimaryScreen.Bounds.Right;
                    work.Bottom = Screen.PrimaryScreen.Bounds.Bottom;
                }

                if (x + Width > work.Right) x = work.Right - Width - 4;
                if (x < work.Left) x = work.Left + 4;
                if (y + Height > work.Bottom) y = y - Height - (int)(34 * _dpiScale);
                if (y < work.Top) y = work.Top + 4;

                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, Width, Height, 0x0010 | 0x0040); // NOACTIVATE|SHOWWINDOW
                Location = new Point(x, y);
            }
            catch (Exception ex)
            {
                Log.Warn("候选条定位失败: " + ex.Message);
            }
        }

        public void HideBar()
        {
            try { if (Visible) Hide(); } catch { }
        }

        // ==================== 绘制 ====================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            _hitRects.Clear();
            _hitIndexes.Clear();

            int radius = _rounded ? (int)(10 * _dpiScale) : 0;
            Rectangle full = new Rectangle(0, 0, Width - 1, Height - 1);

            using (SolidBrush bg = new SolidBrush(_bg)) g.FillRectangle(bg, full);
            using (Pen pen = new Pen(_border, 1f))
            {
                if (radius > 0) DrawRoundRect(g, pen, full, radius);
                else g.DrawRectangle(pen, full);
            }

            Font font = CreateFont(_fontSize, false);
            Font indexFont = CreateFont(Math.Max(9, _fontSize - 4), false);
            Font badgeFont = CreateFont(Math.Max(9, _fontSize - 4), true);
            Font noteFont = CreateFont(Math.Max(8, _fontSize - 5), false);

            int padX = (int)(14 * _dpiScale);
            int gap = (int)(16 * _dpiScale);
            int cy = Height / 2;

            int x = padX;

            // ---- 中/英 徽标 ----
            Size badgeText = TextRenderer.MeasureText(g, _badge, badgeFont);
            int bw = badgeText.Width + (int)(14 * _dpiScale);
            int bh = badgeText.Height + (int)(5 * _dpiScale);
            Rectangle badgeRect = new Rectangle(x, cy - bh / 2, bw, bh);
            using (GraphicsPath path = RoundPath(badgeRect, (int)(5 * _dpiScale)))
            using (SolidBrush sb = new SolidBrush(_badgeColor))
                g.FillPath(sb, path);
            TextRenderer.DrawText(g, _badge, badgeFont, badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += bw + (int)(8 * _dpiScale);

            // ---- 拼音/英文缓存 ----
            if (_showBuffer && _buffer.Length > 0)
            {
                Size bs = TextRenderer.MeasureText(g, _buffer, font);
                Rectangle br = new Rectangle(x, cy - bs.Height / 2, bs.Width + 4, bs.Height);
                TextRenderer.DrawText(g, _buffer, font, br, _textColor, TextFormatFlags.NoPadding | TextFormatFlags.Left);
                x += bs.Width + gap;
            }

            // ---- 候选 ----
            for (int i = 0; i < _items.Count; i++)
            {
                BarEntry item = _items[i];
                bool selected = (i == _selected);
                string indexLabel = _showIndex ? (item.Index + " ") : "";

                Size isz = TextRenderer.MeasureText(g, indexLabel, indexFont);
                Size tsz = TextRenderer.MeasureText(g, item.Text, font);
                Size nsz = string.IsNullOrEmpty(item.Note)
                    ? new Size(0, 0)
                    : TextRenderer.MeasureText(g, item.Note, noteFont);

                int cellW = isz.Width + tsz.Width + (nsz.Width > 0 ? nsz.Width + (int)(6 * _dpiScale) : 0);
                if (selected)
                {
                    Rectangle hl = new Rectangle(x - (int)(5 * _dpiScale), cy - (tsz.Height / 2) - (int)(5 * _dpiScale),
                                                 cellW + (int)(10 * _dpiScale), tsz.Height + (int)(10 * _dpiScale));
                    using (GraphicsPath path = RoundPath(hl, (int)(6 * _dpiScale)))
                    using (SolidBrush sb = new SolidBrush(_selBg))
                        g.FillPath(sb, path);
                }

                int ix = x;
                if (_showIndex)
                {
                    Rectangle ir = new Rectangle(ix, cy - isz.Height / 2, isz.Width, isz.Height);
                    TextRenderer.DrawText(g, indexLabel, indexFont, ir,
                        selected ? _badgeColor : _subColor, TextFormatFlags.NoPadding | TextFormatFlags.Left);
                    ix += isz.Width;
                }

                Rectangle tr = new Rectangle(ix, cy - tsz.Height / 2, tsz.Width, tsz.Height);
                TextRenderer.DrawText(g, item.Text, font, tr,
                    selected ? _badgeColor : _textColor, TextFormatFlags.NoPadding | TextFormatFlags.Left);

                if (nsz.Width > 0)
                {
                    int nx = ix + tsz.Width + (int)(6 * _dpiScale);
                    Rectangle nr = new Rectangle(nx, cy - nsz.Height / 2, nsz.Width, nsz.Height);
                    TextRenderer.DrawText(g, item.Note, noteFont, nr, _subColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.Left);
                }

                _hitRects.Add(new Rectangle(x - 5, 0, cellW + 10, Height));
                _hitIndexes.Add(i);

                x += cellW + gap;
            }

            // ---- 右侧：AI 按钮 + 页码 ----
            _aiRect = Rectangle.Empty;
            int rightEdge = Width - padX;

            if (ShowAiButton)
            {
                Size asz = TextRenderer.MeasureText(g, "AI", badgeFont);
                int aw = asz.Width + (int)(16 * _dpiScale);
                int ah = asz.Height + (int)(6 * _dpiScale);
                _aiRect = new Rectangle(rightEdge - aw, cy - ah / 2, aw, ah);
                using (GraphicsPath path = RoundPath(_aiRect, ah / 2))
                using (SolidBrush sb = new SolidBrush(_selBg))
                    g.FillPath(sb, path);
                TextRenderer.DrawText(g, "AI", badgeFont, _aiRect, _badgeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                rightEdge = _aiRect.Left - gap;
            }

            if (_showPage && _pageCount > 1)
            {
                string pg = (_page + 1) + "/" + _pageCount;
                Size ps = TextRenderer.MeasureText(g, pg, noteFont);
                Rectangle pr = new Rectangle(rightEdge - ps.Width, cy - ps.Height / 2, ps.Width, ps.Height);
                TextRenderer.DrawText(g, pg, noteFont, pr, _subColor, TextFormatFlags.NoPadding | TextFormatFlags.Left);
            }

            font.Dispose(); indexFont.Dispose(); badgeFont.Dispose(); noteFont.Dispose();
        }

        static void DrawRoundRect(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using (GraphicsPath path = RoundPath(r, radius)) g.DrawPath(pen, path);
        }

        static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ==================== 鼠标 ====================

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (ShowAiButton && _aiRect != Rectangle.Empty && _aiRect.Contains(e.X, e.Y))
            {
                Action h2 = AiButtonClicked;
                if (h2 != null) { try { h2(); } catch (Exception ex) { Log.Error("AI 按钮异常", ex); } }
                return;
            }

            for (int i = 0; i < _hitRects.Count; i++)
            {
                if (_hitRects[i].Contains(e.X, e.Y))
                {
                    Action<int> h = CandidateClicked;
                    if (h != null) { try { h(_hitIndexes[i]); } catch (Exception ex) { Log.Error("点选候选异常", ex); } }
                    return;
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Action<int> h = PageRequested;
            if (h != null && _pageCount > 1) h(e.Delta > 0 ? -1 : 1);
        }

        /// <summary>只显示不激活地弹出来。</summary>
        public void ShowNoActivate()
        {
            try
            {
                if (!Visible)
                {
                    Native.SetWindowPos(Handle, IntPtr.Zero, Location.X, Location.Y, Width, Height,
                        0x0010 | 0x0040 | 0x0002); // NOACTIVATE | SHOWWINDOW | NOMOVE
                }
            }
            catch (Exception ex) { Log.Warn("显示候选条失败: " + ex.Message); }
        }
    }
}
