using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyIme.Settings
{
    /// <summary>切换主题时会被整棵控件树递归调用一次。</summary>
    public interface IThemed
    {
        void ApplyTheme();
    }

    /// <summary>
    /// 配色 + 自绘工具。风格参考微信输入法的设置界面：
    /// 浅灰底、白卡片、圆角、微信绿点缀、微软雅黑、留白舒服。
    /// 颜色全部是 public 字段，切主题时整体替换一遍。
    /// </summary>
    public static class Theme
    {
        public static bool Dark = false;

        // ---------- 浅色 ----------
        public static Color Bg = ColorTranslator.FromHtml("#F7F7F7");
        public static Color Card = ColorTranslator.FromHtml("#FFFFFF");
        public static Color Text = ColorTranslator.FromHtml("#1A1A1A");
        public static Color SubText = ColorTranslator.FromHtml("#8C8C8C");
        public static Color Divider = ColorTranslator.FromHtml("#EDEDED");
        public static Color Accent = ColorTranslator.FromHtml("#07C160");
        public static Color Hover = ColorTranslator.FromHtml("#F2F2F2");
        public static Color Selected = ColorTranslator.FromHtml("#E8F8EF");
        public static Color InputBg = ColorTranslator.FromHtml("#FFFFFF");
        public static Color Border = ColorTranslator.FromHtml("#E3E3E3");
        public static Color Track = ColorTranslator.FromHtml("#DCDCDC");
        public static Color Danger = ColorTranslator.FromHtml("#E64340");
        public static Color WarnColor = ColorTranslator.FromHtml("#FA9D3B");

        public const string FontName = "Microsoft YaHei";

        static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        // ==================== 字体 ====================

        /// <summary>取一个缓存过的字体，避免每次重绘都 new Font 泄漏 GDI 句柄。</summary>
        public static Font UI(float size, FontStyle style)
        {
            string key = size.ToString("0.##") + "/" + ((int)style).ToString();
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;
            try { f = new Font(FontName, size, style, GraphicsUnit.Point); }
            catch { f = new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
            FontCache[key] = f;
            return f;
        }

        public static Font Ui() { return UI(9f, FontStyle.Regular); }
        public static Font UiBold() { return UI(9f, FontStyle.Bold); }
        public static Font Small() { return UI(8.25f, FontStyle.Regular); }
        public static Font Title() { return UI(10.5f, FontStyle.Bold); }
        public static Font Big() { return UI(21f, FontStyle.Bold); }

        // ==================== 主题 ====================

        public static string Normalize(string mode)
        {
            if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return "light";
            if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) return "dark";
            return "auto";
        }

        /// <summary>读注册表判断系统是不是深色模式（HKCU\...\Themes\Personalize\AppsUseLightTheme）。</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v is int) return ((int)v) == 0;
            }
            catch { }
            return false;
        }

        public static void Apply(string mode)
        {
            string m = Normalize(mode);
            bool dark = (m == "dark") || (m == "auto" && SystemPrefersDark());
            if (dark) ApplyDark(); else ApplyLight();
        }

        public static void ApplyLight()
        {
            Dark = false;
            Bg = ColorTranslator.FromHtml("#F7F7F7");
            Card = ColorTranslator.FromHtml("#FFFFFF");
            Text = ColorTranslator.FromHtml("#1A1A1A");
            SubText = ColorTranslator.FromHtml("#8C8C8C");
            Divider = ColorTranslator.FromHtml("#EDEDED");
            Accent = ColorTranslator.FromHtml("#07C160");
            Hover = ColorTranslator.FromHtml("#F2F2F2");
            Selected = ColorTranslator.FromHtml("#E8F8EF");
            InputBg = ColorTranslator.FromHtml("#FFFFFF");
            Border = ColorTranslator.FromHtml("#E3E3E3");
            Track = ColorTranslator.FromHtml("#DCDCDC");
            Danger = ColorTranslator.FromHtml("#E64340");
            WarnColor = ColorTranslator.FromHtml("#FA9D3B");
        }

        public static void ApplyDark()
        {
            Dark = true;
            Bg = ColorTranslator.FromHtml("#1F1F1F");
            Card = ColorTranslator.FromHtml("#2B2B2B");
            Text = ColorTranslator.FromHtml("#F2F2F2");
            SubText = ColorTranslator.FromHtml("#9A9A9A");
            Divider = ColorTranslator.FromHtml("#3A3A3A");
            Accent = ColorTranslator.FromHtml("#07C160");
            Hover = ColorTranslator.FromHtml("#363636");
            Selected = ColorTranslator.FromHtml("#1E3A2A");
            InputBg = ColorTranslator.FromHtml("#333333");
            Border = ColorTranslator.FromHtml("#454545");
            Track = ColorTranslator.FromHtml("#4A4A4A");
            Danger = ColorTranslator.FromHtml("#E64340");
            WarnColor = ColorTranslator.FromHtml("#FA9D3B");
        }

        // ==================== 颜色工具 ====================

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, double amount) { return Mix(c, Color.White, amount); }
        public static Color Darken(Color c, double amount) { return Mix(c, Color.Black, amount); }

        public static Color Alpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        /// <summary>禁用态颜色：往背景色上褪。</summary>
        public static Color Disabled(Color c)
        {
            return Mix(c, Bg, 0.55);
        }

        // ==================== 绘制工具 ====================

        public static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        /// <summary>圆角矩形路径。调用方负责 Dispose。</summary>
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (r.Width <= 0 || r.Height <= 0) return p;
            if (d <= 0 || r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void StrokeRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            StrokeRounded(g, r, radius, color, 1f);
        }

        public static void StrokeRounded(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (Pen pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        /// <summary>圆角填充 + 描边，一次搞定。</summary>
        public static void RoundRect(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            if (fill.A > 0) FillRounded(g, r, radius, fill);
            if (border.A > 0) StrokeRounded(g, r, radius, border);
        }

        /// <summary>居中画一行字。</summary>
        public static void CenterText(Graphics g, string text, Font font, Rectangle r, Color color)
        {
            TextRenderer.DrawText(g, text, font, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        /// <summary>左对齐画一行字（垂直居中）。</summary>
        public static void LeftText(Graphics g, string text, Font font, Rectangle r, Color color)
        {
            TextRenderer.DrawText(g, text, font, r, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        /// <summary>画一个向下的小箭头（下拉框用）。</summary>
        public static void ChevronDown(Graphics g, Point center, int size, Color color)
        {
            using (Pen pen = new Pen(color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, center.X - size / 2, center.Y - size / 4, center.X, center.Y + size / 4);
                g.DrawLine(pen, center.X, center.Y + size / 4, center.X + size / 2, center.Y - size / 4);
            }
        }

        /// <summary>画一个对勾。</summary>
        public static void CheckMark(Graphics g, Rectangle r, Color color, float width)
        {
            using (Pen pen = new Pen(color, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new Point[] {
                    new Point(r.Left, r.Top + r.Height / 2),
                    new Point(r.Left + r.Width / 3, r.Bottom - r.Height / 5),
                    new Point(r.Right, r.Top + r.Height / 5)
                });
            }
        }
    }

    // ====================================================================
    //  自绘控件基类
    // ====================================================================

    public class FlatControl : Control
    {
        public FlatControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Ui();
            Cursor = Cursors.Default;
        }
    }

    // ====================================================================
    //  卡片 / 分隔线
    // ====================================================================

    /// <summary>白底圆角卡片。背景涂成页面底色，圆角处才透得出底。</summary>
    public class CardPanel : Panel, IThemed
    {
        public int Radius = 8;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Bg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(Theme.Bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = Theme.Dark ? Theme.Divider : Color.Empty;
            Theme.RoundRect(e.Graphics, r, Radius, Theme.Card, border);
            base.OnPaint(e);
        }
    }

    /// <summary>1 像素分割线。</summary>
    public class Divider : FlatControl, IThemed
    {
        public Divider()
        {
            Height = 1;
            BackColor = Theme.Card;
            TabStop = false;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Card);
            using (Pen pen = new Pen(Theme.Divider, 1f))
                e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    // ====================================================================
    //  按钮
    // ====================================================================

    /// <summary>微信绿实心主按钮。</summary>
    public class AccentButton : FlatControl, IThemed
    {
        bool _hover;
        bool _down;

        /// <summary>true = 没有改动时的"淡"状态（浅绿底 + 绿字）。</summary>
        public bool Muted = false;

        public AccentButton()
        {
            Size = new Size(96, 36);
            Font = Theme.UI(9.5f, FontStyle.Regular);
        }

        public void ApplyTheme() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill;
            Color textColor;

            if (!Enabled)
            {
                fill = Theme.Disabled(Theme.Accent);
                textColor = Theme.Mix(Color.White, Theme.Bg, 0.35);
            }
            else if (Muted)
            {
                fill = Theme.Mix(Theme.Card, Theme.Accent, _hover ? 0.30 : 0.20);
                textColor = Theme.Accent;
            }
            else
            {
                if (_down) fill = Theme.Darken(Theme.Accent, 0.12);
                else if (_hover) fill = Theme.Lighten(Theme.Accent, 0.10);
                else fill = Theme.Accent;
                textColor = Color.White;
            }

            Theme.FillRounded(e.Graphics, r, 6, fill);
            Theme.CenterText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), textColor);
        }
    }

    /// <summary>白底描边按钮（次要操作）。</summary>
    public class GhostButton : FlatControl, IThemed
    {
        bool _hover;
        bool _down;
        public bool Danger = false;

        public GhostButton()
        {
            Size = new Size(80, 32);
        }

        public void ApplyTheme() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color baseColor = Danger ? Theme.Danger : Theme.Text;
            Color fill = Theme.Card;
            if (!Enabled) fill = Theme.Card;
            else if (_down) fill = Theme.Hover;
            else if (_hover) fill = Theme.Mix(Theme.Card, Theme.Accent, 0.06);

            Color border = Enabled ? Theme.Border : Theme.Divider;
            if (Enabled && _hover) border = Danger ? Theme.Danger : Theme.Accent;

            Color textColor = Enabled ? baseColor : Theme.SubText;
            Theme.RoundRect(e.Graphics, r, 6, fill, border);
            Theme.CenterText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), textColor);
        }
    }

    /// <summary>链接样式的文字按钮（微信绿，hover 加下划线）。</summary>
    public class LinkText : FlatControl, IThemed
    {
        bool _hover;

        public LinkText()
        {
            BackColor = Theme.Card;
            Size = new Size(120, 22);
            Cursor = Cursors.Hand;
        }

        public void ApplyTheme() { Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Color c = Enabled ? Theme.Accent : Theme.SubText;
            if (_hover && Enabled) c = Theme.Lighten(Theme.Accent, 0.15);
            Size sz = TextRenderer.MeasureText(Text, Font);
            Rectangle r = new Rectangle(0, 0, Math.Min(Width, sz.Width + 2), Height);
            Theme.LeftText(e.Graphics, Text, Font, r, c);
            if (_hover && Enabled)
            {
                using (Pen pen = new Pen(c, 1f))
                    e.Graphics.DrawLine(pen, 0, Height / 2 + sz.Height / 2 - 1, sz.Width, Height / 2 + sz.Height / 2 - 1);
            }
        }
    }

    // ====================================================================
    //  开关（微信那种胶囊滑块）
    // ====================================================================

    public class Switch : FlatControl, IThemed
    {
        bool _checked;
        bool _hover;
        bool _down;
        float _pos;          // 0..1 动画位置
        readonly Timer _anim;

        public event EventHandler CheckedChanged;

        public Switch()
        {
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(44, 24);
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Card;
            _anim = new Timer();
            _anim.Interval = 15;
            _anim.Tick += AnimTick;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                StartAnim();
                OnCheckedChanged();
            }
        }

        /// <summary>不触发事件地设值（初始化时用）。</summary>
        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            _pos = value ? 1f : 0f;
            Invalidate();
        }

        void StartAnim()
        {
            if (!IsHandleCreated) { _pos = _checked ? 1f : 0f; Invalidate(); return; }
            _anim.Start();
        }

        void AnimTick(object sender, EventArgs e)
        {
            float target = _checked ? 1f : 0f;
            float d = target - _pos;
            if (Math.Abs(d) < 0.08f) { _pos = target; _anim.Stop(); }
            else _pos += d * 0.35f;
            Invalidate();
        }

        protected virtual void OnCheckedChanged()
        {
            EventHandler h = CheckedChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Focus(); Invalidate(); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Enabled)
                Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            int h = Math.Min(Height - 2, 24);
            int w = Math.Min(Width - 2, 46);
            int top = (Height - h) / 2;
            int left = (Width - w) / 2;
            Rectangle track = new Rectangle(left, top, w - 1, h - 1);
            int radius = h / 2;

            Color off = Theme.Track;
            Color on = Theme.Accent;
            if (!Enabled) { off = Theme.Disabled(Theme.Track); on = Theme.Disabled(Theme.Accent); }

            Color trackColor = Theme.Mix(off, on, _pos);
            if (Enabled && _hover) trackColor = Theme.Darken(trackColor, 0.04);
            Theme.FillRounded(e.Graphics, track, radius, trackColor);

            if (Focused && Enabled)
                Theme.StrokeRounded(e.Graphics, new Rectangle(track.X - 2, track.Y - 2, track.Width + 4, track.Height + 4), radius + 2,
                    Theme.Alpha(Theme.Accent, 90));

            int thumbSize = h - 4;
            if (_down) thumbSize -= 2;
            int travel = w - 2 - thumbSize - 2;
            int tx = left + 2 + (int)Math.Round(travel * _pos);
            int ty = top + (h - thumbSize) / 2;
            Rectangle thumb = new Rectangle(tx, ty, thumbSize, thumbSize);

            using (SolidBrush shadow = new SolidBrush(Theme.Alpha(Color.Black, 28)))
                e.Graphics.FillEllipse(shadow, new Rectangle(thumb.X, thumb.Y + 1, thumb.Width, thumb.Height));
            using (SolidBrush b = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(b, thumb);
        }
    }
}
