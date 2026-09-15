using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Setup
{
    /// <summary>微信风格的配色。主色 #07C160。</summary>
    public static class UiTheme
    {
        public static readonly Color Primary          = Color.FromArgb(0x07, 0xC1, 0x60);
        public static readonly Color PrimaryHover     = Color.FromArgb(0x06, 0xAD, 0x56);
        public static readonly Color PrimaryPressed   = Color.FromArgb(0x05, 0x9B, 0x4D);
        public static readonly Color PrimaryDisabled  = Color.FromArgb(0x9E, 0xE0, 0xBC);
        public static readonly Color PageBack         = Color.White;
        public static readonly Color FooterBack       = Color.FromArgb(0xF7, 0xF7, 0xF7);
        public static readonly Color FieldBack        = Color.FromArgb(0xFA, 0xFA, 0xFA);
        public static readonly Color TextPrimary      = Color.FromArgb(0x19, 0x19, 0x19);
        public static readonly Color TextSecondary    = Color.FromArgb(0x88, 0x88, 0x88);
        public static readonly Color TextDisabled     = Color.FromArgb(0xBB, 0xBB, 0xBB);
        public static readonly Color Border           = Color.FromArgb(0xE5, 0xE5, 0xE5);
        public static readonly Color SecondaryBack    = Color.FromArgb(0xF2, 0xF2, 0xF2);
        public static readonly Color SecondaryHover   = Color.FromArgb(0xE7, 0xE7, 0xE7);
        public static readonly Color SecondaryPressed = Color.FromArgb(0xDC, 0xDC, 0xDC);
        public static readonly Color Error            = Color.FromArgb(0xE6, 0x43, 0x39);
    }

    /// <summary>统一字体（用系统字体，中文环境下就是微软雅黑 UI）。</summary>
    public static class UiFonts
    {
        public static readonly Font Body     = Make(SystemFonts.MessageBoxFont, 9f,   FontStyle.Regular);
        public static readonly Font BodyBold = Make(SystemFonts.MessageBoxFont, 9f,   FontStyle.Bold);
        public static readonly Font H1       = Make(SystemFonts.MessageBoxFont, 12f,  FontStyle.Bold);
        public static readonly Font H2       = Make(SystemFonts.MessageBoxFont, 10f,  FontStyle.Bold);
        public static readonly Font Small    = Make(SystemFonts.MessageBoxFont, 8.25f, FontStyle.Regular);
        public static readonly Font Logo     = Make(SystemFonts.MessageBoxFont, 14f,  FontStyle.Bold);

        public static Font Make(Font basis, float size, FontStyle style)
        {
            try
            {
                if (basis != null) return new Font(basis.FontFamily, size, style);
            }
            catch { }
            try { return new Font(FontFamily.GenericSansSerif, size, style); }
            catch { return null; }
        }
    }

    /// <summary>绘图与窗体小工具。</summary>
    public static class Ui
    {
        /// <summary>圆角矩形路径（radius 为圆角半径）。</summary>
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (radius <= 0 || r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, p);
            }
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, p);
            }
        }

        /// <summary>用父容器的背景色擦除自己的背景，避免透明控件出现黑边/闪烁。</summary>
        public static void EraseBackground(Control c, PaintEventArgs e)
        {
            Color back = UiTheme.PageBack;
            Control p = c == null ? null : c.Parent;
            if (p != null) back = p.BackColor;
            using (SolidBrush b = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(b, c.ClientRectangle);
            }
        }

        /// <summary>把控件裁剪成圆角（CreateRoundRectRgn + SetWindowRgn）。</summary>
        public static void MakeRounded(Control c, int radius)
        {
            if (c == null || radius <= 0) return;
            c.HandleCreated += delegate { ApplyRegion(c, radius); };
            c.Resize += delegate { ApplyRegion(c, radius); };
            if (c.IsHandleCreated) ApplyRegion(c, radius);
        }

        static void ApplyRegion(Control c, int radius)
        {
            try
            {
                if (c.Width <= 0 || c.Height <= 0) return;
                IntPtr rgn = Native.CreateRoundRectRgn(0, 0, c.Width + 1, c.Height + 1, radius * 2, radius * 2);
                if (rgn == IntPtr.Zero) return;
                if (Native.SetWindowRgn(c.Handle, rgn, true) == 0)
                {
                    Native.DeleteObject(rgn);
                }
            }
            catch { }
        }
    }

    /// <summary>自绘圆角按钮：主按钮微信绿，次按钮灰底描边。</summary>
    public class FlatButton : Button
    {
        public bool Primary = true;
        public int Radius = 4;

        bool _hover;
        bool _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = UiFonts.Body;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(96, 32);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill;
            Color fore;
            if (!Enabled)
            {
                fill = Primary ? UiTheme.PrimaryDisabled : UiTheme.SecondaryBack;
                fore = Primary ? Color.White : UiTheme.TextDisabled;
            }
            else if (Primary)
            {
                fill = _down ? UiTheme.PrimaryPressed : (_hover ? UiTheme.PrimaryHover : UiTheme.Primary);
                fore = Color.White;
            }
            else
            {
                fill = _down ? UiTheme.SecondaryPressed : (_hover ? UiTheme.SecondaryHover : UiTheme.SecondaryBack);
                fore = UiTheme.TextPrimary;
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width <= 0 || r.Height <= 0) return;
            Ui.FillRound(g, r, Radius, fill);
            if (!Primary) Ui.DrawRound(g, r, Radius, UiTheme.Border, 1f);
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6));
            }

            TextRenderer.DrawText(g, Text, Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate(); base.OnEnabledChanged(e);
        }
    }

    /// <summary>自绘复选框：圆角方块 + 对勾。</summary>
    public class FlatCheckBox : Control
    {
        public event EventHandler CheckedChanged;

        bool _checked;
        bool _hover;

        public FlatCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = UiFonts.Body;
            ForeColor = UiTheme.TextPrimary;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = 24;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int box = 16;
            int y = (Height - box) / 2;
            if (y < 0) y = 0;
            Rectangle r = new Rectangle(1, y, box, box);

            if (_checked)
            {
                Ui.FillRound(g, r, 3, Enabled ? UiTheme.Primary : UiTheme.PrimaryDisabled);
                using (Pen pen = new Pen(Color.White, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    Point[] pts = new Point[]
                    {
                        new Point(r.Left + 4, r.Top + 8),
                        new Point(r.Left + 7, r.Top + 11),
                        new Point(r.Left + 12, r.Top + 5)
                    };
                    g.DrawLines(pen, pts);
                }
            }
            else
            {
                Ui.FillRound(g, r, 3, Color.White);
                Ui.DrawRound(g, r, 3, (_hover && Enabled) ? UiTheme.Primary : UiTheme.Border, 1.4f);
            }

            Rectangle tr = new Rectangle(box + 9, 0, Width - box - 10, Height);
            if (tr.Width > 0)
            {
                TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : UiTheme.TextDisabled,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(box + 9, 2, Width - box - 12, Height - 4));
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Focus();
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate(); base.OnTextChanged(e);
        }
    }

    /// <summary>自绘进度条（圆角药丸形状）。</summary>
    public class FlatProgress : Control
    {
        public int Value = 0;

        public FlatProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 8;
        }

        public void SetValue(int v)
        {
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            if (v == Value) return;
            Value = v;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            if (track.Width <= 0 || track.Height <= 0) return;

            int radius = Height / 2;
            Ui.FillRound(g, track, radius, UiTheme.SecondaryBack);
            Ui.DrawRound(g, track, radius, UiTheme.Border, 1f);

            int w = (int)Math.Round((double)track.Width * Value / 100.0);
            if (Value <= 0 || w <= 0) return;
            if (w < Height) w = Height;
            if (w > track.Width) w = track.Width;
            Ui.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), radius, UiTheme.Primary);
        }
    }

    /// <summary>自绘文字标签（支持换行/居中/次要色）。</summary>
    public class LabelEx : Control
    {
        public bool Wrap = false;
        public bool Center = false;

        bool _secondary;

        public LabelEx()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = UiFonts.Body;
            ForeColor = UiTheme.TextPrimary;
        }

        public bool Secondary
        {
            get { return _secondary; }
            set
            {
                _secondary = value;
                ForeColor = value ? UiTheme.TextSecondary : UiTheme.TextPrimary;
                Invalidate();
            }
        }

        /// <summary>按当前宽度算出自适应高度（换行标签用）。</summary>
        public void FitHeight()
        {
            if (Width <= 0) return;
            int h;
            if (Wrap)
            {
                Size sz = TextRenderer.MeasureText(Text, Font, new Size(Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                h = sz.Height + 2;
            }
            else
            {
                h = Font.Height + 2;
            }
            if (h > 0 && h != Height) Height = h;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.NoPrefix;
            flags |= Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            if (Wrap)
            {
                flags |= TextFormatFlags.WordBreak | TextFormatFlags.Top;
            }
            else
            {
                flags |= TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor, flags);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate(); base.OnTextChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            Invalidate(); base.OnFontChanged(e);
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            Invalidate(); base.OnForeColorChanged(e);
        }
    }

    /// <summary>只读信息框：圆角、浅灰底、长路径自动省略。</summary>
    public class FieldBox : Control
    {
        public int Radius = 4;

        public FieldBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = UiFonts.Body;
            ForeColor = UiTheme.TextPrimary;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width <= 0 || r.Height <= 0) return;
            Ui.FillRound(g, r, Radius, UiTheme.FieldBack);
            Ui.DrawRound(g, r, Radius, UiTheme.Border, 1f);
            Rectangle tr = new Rectangle(r.X + 10, r.Y, r.Width - 20, r.Height);
            if (tr.Width <= 0) return;
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate(); base.OnTextChanged(e);
        }
    }

    /// <summary>完成页的圆形图标（对勾 / 叉）。</summary>
    public class StatusIcon : Control
    {
        public bool Error = false;

        public StatusIcon()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(48, 48);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.EraseBackground(this, e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = Math.Min(Width, Height) - 2;
            if (d < 8) d = 8;
            Rectangle r = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);

            using (SolidBrush b = new SolidBrush(Error ? UiTheme.Error : UiTheme.Primary))
            {
                g.FillEllipse(b, r);
            }

            using (Pen pen = new Pen(Color.White, Math.Max(2f, d / 12f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (!Error)
                {
                    Point[] pts = new Point[]
                    {
                        new Point(r.Left + d * 28 / 100, r.Top + d * 52 / 100),
                        new Point(r.Left + d * 43 / 100, r.Top + d * 67 / 100),
                        new Point(r.Left + d * 74 / 100, r.Top + d * 33 / 100)
                    };
                    g.DrawLines(pen, pts);
                }
                else
                {
                    g.DrawLine(pen, r.Left + d * 33 / 100, r.Top + d * 33 / 100, r.Right - d * 33 / 100, r.Bottom - d * 33 / 100);
                    g.DrawLine(pen, r.Right - d * 33 / 100, r.Top + d * 33 / 100, r.Left + d * 33 / 100, r.Bottom - d * 33 / 100);
                }
            }
        }
    }
}
