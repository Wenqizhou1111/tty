using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Settings
{
    // ====================================================================
    //  图标：全部用几何图形现场画，不依赖任何图片资源
    // ====================================================================

    public static class Icons
    {
        public const int General = 0;
        public const int Input = 1;
        public const int Hotkey = 2;
        public const int Game = 3;
        public const int Ai = 4;
        public const int Lexicon = 5;
        public const int About = 6;

        /// <summary>在 box 里画一个 16x16 的图标。</summary>
        public static void Draw(Graphics g, int kind, Rectangle box, Color c)
        {
            Theme.Prep(g);
            using (Pen pen = new Pen(c, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                int x = box.X, y = box.Y, w = box.Width, h = box.Height;

                switch (kind)
                {
                    case General:   // 三条滑杆
                        for (int i = 0; i < 3; i++)
                        {
                            int ly = y + 3 + i * 5;
                            g.DrawLine(pen, x + 1, ly, x + w - 1, ly);
                            int kx = x + 3 + (i % 2 == 0 ? 0 : 5);
                            using (SolidBrush b = new SolidBrush(c))
                                g.FillEllipse(b, kx, ly - 2, 4, 4);
                        }
                        break;

                    case Input:     // 键盘
                        g.DrawRectangle(pen, x + 1, y + 4, w - 3, h - 8);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            for (int i = 0; i < 3; i++) g.FillRectangle(b, x + 4 + i * 4, y + 7, 2, 2);
                            g.FillRectangle(b, x + 4, y + 11, w - 9, 2);
                        }
                        break;

                    case Hotkey:    // 键帽 + 闪电
                        g.DrawRectangle(pen, x + 1, y + 2, w - 3, h - 5);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            Point[] bolt = new Point[] {
                                new Point(x + 9, y + 4), new Point(x + 5, y + 9),
                                new Point(x + 8, y + 9), new Point(x + 6, y + 13),
                                new Point(x + 12, y + 7), new Point(x + 9, y + 7)
                            };
                            g.FillPolygon(b, bolt);
                        }
                        break;

                    case Game:      // 手柄
                        g.DrawArc(pen, x + 1, y + 5, w - 3, h - 7, 180, 180);
                        g.DrawLine(pen, x + 1, y + 9, x + 1, y + 12);
                        g.DrawLine(pen, x + w - 2, y + 9, x + w - 2, y + 12);
                        g.DrawArc(pen, x + 1, y + 8, w - 3, 8, 0, 180);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            g.FillRectangle(b, x + 4, y + 10, 5, 2);
                            g.FillRectangle(b, x + 5, y + 9, 2, 4);
                            g.FillEllipse(b, x + w - 8, y + 9, 3, 3);
                        }
                        break;

                    case Ai:        // 四角星
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            Point[] star = new Point[] {
                                new Point(x + 7, y + 2), new Point(x + 9, y + 7),
                                new Point(x + 14, y + 9), new Point(x + 9, y + 11),
                                new Point(x + 7, y + 15), new Point(x + 5, y + 11),
                                new Point(x + 1, y + 9), new Point(x + 5, y + 7)
                            };
                            g.FillPolygon(b, star);
                            g.FillEllipse(b, x + 11, y + 1, 3, 3);
                        }
                        break;

                    case Lexicon:   // 书
                        g.DrawRectangle(pen, x + 2, y + 2, w - 5, h - 4);
                        g.DrawLine(pen, x + 5, y + 2, x + 5, y + h - 3);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            g.FillRectangle(b, x + 7, y + 5, 5, 1);
                            g.FillRectangle(b, x + 7, y + 8, 5, 1);
                            g.FillRectangle(b, x + 7, y + 11, 3, 1);
                        }
                        break;

                    case About:     // 圆圈 i
                        g.DrawEllipse(pen, x + 1, y + 1, w - 3, h - 3);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            g.FillEllipse(b, x + 7, y + 4, 2, 2);
                            g.FillRectangle(b, x + 7, y + 7, 2, 6);
                        }
                        break;
                }
            }
        }
    }

    // ====================================================================
    //  跟着主题变色的普通容器（横排放几个统计卡片之类）
    // ====================================================================

    public class BlockPanel : Panel, IThemed
    {
        public BlockPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }
    }

    // ====================================================================
    //  会自动跟着主题变色的文字标签
    // ====================================================================

    public class ThemedLabel : Label, IThemed
    {
        public const int RoleBody = 0;      // 正文
        public const int RoleSub = 1;       // 次要说明
        public const int RoleTitle = 2;     // 卡片标题
        public const int RoleAccent = 3;    // 强调
        public const int RoleDanger = 4;    // 危险
        public const int RoleBig = 5;       // 大号数字

        public int Role = RoleBody;

        public ThemedLabel()
        {
            BackColor = Color.Transparent;
            AutoSize = true;
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            switch (Role)
            {
                case RoleSub:
                    ForeColor = Theme.SubText;
                    Font = Theme.UI(8.25f, FontStyle.Regular);
                    break;
                case RoleTitle:
                    ForeColor = Theme.Text;
                    Font = Theme.UI(10.5f, FontStyle.Bold);
                    break;
                case RoleAccent:
                    ForeColor = Theme.Accent;
                    Font = Theme.UI(9f, FontStyle.Regular);
                    break;
                case RoleDanger:
                    ForeColor = Theme.Danger;
                    Font = Theme.UI(9f, FontStyle.Regular);
                    break;
                case RoleBig:
                    ForeColor = Theme.Text;
                    Font = Theme.UI(15f, FontStyle.Bold);
                    break;
                default:
                    ForeColor = Theme.Text;
                    Font = Theme.UI(9.5f, FontStyle.Regular);
                    break;
            }
            BackColor = Color.Transparent;
            Invalidate();
        }
    }

    // ====================================================================
    //  复选框
    // ====================================================================

    public class FlatCheck : FlatControl, IThemed
    {
        bool _checked;
        bool _hover;

        public event EventHandler CheckedChanged;

        public FlatCheck()
        {
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(200, 24);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetCheckedSilent(bool value) { _checked = value; Invalidate(); }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Enabled) Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            int bx = 1;
            int by = (Height - 18) / 2;
            Rectangle box = new Rectangle(bx, by, 18, 18);

            if (_checked)
            {
                Color fill = Enabled ? Theme.Accent : Theme.Disabled(Theme.Accent);
                if (Enabled && _hover) fill = Theme.Lighten(fill, 0.1);
                Theme.FillRounded(e.Graphics, box, 5, fill);
                Theme.CheckMark(e.Graphics, new Rectangle(box.X + 4, box.Y + 5, 10, 8), Color.White, 1.9f);
            }
            else
            {
                Color border = Theme.Border;
                if (Enabled && _hover) border = Theme.Accent;
                if (!Enabled) border = Theme.Divider;
                Theme.RoundRect(e.Graphics, box, 5, Enabled ? Theme.Card : Theme.Bg, border);
            }

            Color tc = Enabled ? Theme.Text : Theme.SubText;
            Rectangle tr = new Rectangle(box.Right + 8, 0, Math.Max(0, Width - box.Right - 8), Height);
            Theme.LeftText(e.Graphics, Text, Font, tr, tc);
        }
    }

    // ====================================================================
    //  输入框（自己画圆角边框，内嵌无边框 TextBox）
    // ====================================================================

    public class InputBox : FlatControl, IThemed
    {
        readonly TextBox _box;
        bool _focused;
        string _hint = "";
        bool _showingHint;

        public InputBox()
        {
            _box = new TextBox();
            BackColor = Theme.Card;
            Size = new Size(240, 34);

            _box.BorderStyle = BorderStyle.None;
            _box.Font = Theme.UI(9.5f, FontStyle.Regular);
            _box.BackColor = Theme.InputBg;
            _box.ForeColor = Theme.Text;

            _box.GotFocus += delegate { _focused = true; HideHint(); Invalidate(); };
            _box.LostFocus += delegate { _focused = false; ShowHintIfEmpty(); Invalidate(); };
            _box.TextChanged += delegate { Invalidate(); OnTextChanged(EventArgs.Empty); };
            Controls.Add(_box);
        }

        public TextBox Inner { get { return _box; } }

        public override string Text
        {
            get { if (_showingHint) return ""; return _box.Text; }
            set
            {
                _showingHint = false;
                _box.ForeColor = Theme.Text;
                _box.Text = value == null ? "" : value;
                Invalidate();
            }
        }

        public string Hint
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; ShowHintIfEmpty(); Invalidate(); }
        }

        public bool Password
        {
            get { return _box.PasswordChar != (char)0; }
            set { _box.PasswordChar = value ? (char)0x25CF : (char)0; Invalidate(); }
        }

        public bool Multiline
        {
            get { return _box.Multiline; }
            set
            {
                _box.Multiline = value;
                _box.WordWrap = value;
                if (value)
                {
                    _box.AcceptsReturn = true;
                    _box.ScrollBars = ScrollBars.Vertical;
                }
                LayoutBox();
                Invalidate();
            }
        }

        public bool ReadOnlyText
        {
            get { return _box.ReadOnly; }
            set { _box.ReadOnly = value; _box.BackColor = value ? Theme.Bg : Theme.InputBg; }
        }

        public void FocusInner() { _box.Focus(); }

        void ShowHintIfEmpty()
        {
            if (_hint.Length == 0 || _box.Multiline) return;
            if (_box.Focused || _box.Text.Length > 0) return;
            _showingHint = true;
            _box.ForeColor = Theme.SubText;
            _box.Text = _hint;
        }

        void HideHint()
        {
            if (!_showingHint) return;
            _showingHint = false;
            _box.Text = "";
            _box.ForeColor = Theme.Text;
        }

        void LayoutBox()
        {
            int padX = _box.Multiline ? 8 : 10;
            if (_box.Multiline)
            {
                _box.SetBounds(padX, 7, Math.Max(12, Width - padX * 2), Math.Max(12, Height - 14));
            }
            else
            {
                int h = _box.PreferredHeight;
                _box.SetBounds(padX, Math.Max(2, (Height - h) / 2), Math.Max(12, Width - padX * 2), h);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutBox();
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            _box.BackColor = _box.ReadOnly ? Theme.Bg : Theme.InputBg;
            _box.ForeColor = _showingHint ? Theme.SubText : Theme.Text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = _focused ? Theme.Accent : Theme.Border;
            if (!Enabled) border = Theme.Divider;
            Theme.RoundRect(e.Graphics, r, 6, Enabled ? Theme.InputBg : Theme.Bg, border);
        }
    }

    // ====================================================================
    //  数字输入框（右侧上下小箭头）
    // ====================================================================

    public class NumberBox : FlatControl, IThemed
    {
        readonly TextBox _box;
        int _value;
        int _min;
        int _max;
        int _step;
        bool _focused;
        int _hotZone;    // 1=上 2=下 0=无

        public event EventHandler ValueChanged;

        public NumberBox()
        {
            _box = new TextBox();
            _min = 0; _max = 100; _step = 1; _value = 0;
            Size = new Size(100, 34);
            BackColor = Theme.Card;

            _box.BorderStyle = BorderStyle.None;
            _box.Font = Theme.UI(9.5f, FontStyle.Regular);
            _box.BackColor = Theme.InputBg;
            _box.ForeColor = Theme.Text;
            _box.TextAlign = HorizontalAlignment.Left;
            _box.Text = "0";
            _box.GotFocus += delegate { _focused = true; Invalidate(); };
            _box.LostFocus += delegate { _focused = false; NormalizeText(); Invalidate(); };
            _box.KeyPress += BoxKeyPress;
            _box.TextChanged += BoxTextChanged;
            Controls.Add(_box);
        }

        public int Min { get { return _min; } set { _min = value; if (_max < _min) _max = _min; } }
        public int Max { get { return _max; } set { _max = value; if (_min > _max) _min = _max; } }
        public int Step { get { return _step; } set { _step = value <= 0 ? 1 : value; } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Clamp(value);
                if (v == _value) { NormalizeText(); return; }
                _value = v;
                NormalizeText();
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        int Clamp(int v)
        {
            if (v < _min) return _min;
            if (v > _max) return _max;
            return v;
        }

        void NormalizeText()
        {
            string s = _value.ToString();
            if (_box.Text != s) _box.Text = s;
        }

        void BoxKeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (!char.IsDigit(e.KeyChar)) e.Handled = true;
        }

        void BoxTextChanged(object sender, EventArgs e)
        {
            int v;
            if (int.TryParse(_box.Text, out v))
            {
                if (v >= _min && v <= _max && v != _value)
                {
                    _value = v;
                    Invalidate();
                    EventHandler h = ValueChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            Invalidate();
        }

        void LayoutBox()
        {
            int h = _box.PreferredHeight;
            _box.SetBounds(10, Math.Max(2, (Height - h) / 2), Math.Max(12, Width - 34), h);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutBox();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = ZoneAt(e.Location);
            if (z != _hotZone) { _hotZone = z; Cursor = z == 0 ? Cursors.Default : Cursors.Hand; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotZone != 0) { _hotZone = 0; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            int z = ZoneAt(e.Location);
            if (z == 1) Value = _value + _step;
            else if (z == 2) Value = _value - _step;
        }

        int ZoneAt(Point p)
        {
            if (p.X < Width - 26) return 0;
            return p.Y < Height / 2 ? 1 : 2;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            _box.BackColor = Theme.InputBg;
            _box.ForeColor = Theme.Text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = _focused ? Theme.Accent : Theme.Border;
            if (!Enabled) border = Theme.Divider;
            Theme.RoundRect(e.Graphics, r, 6, Enabled ? Theme.InputBg : Theme.Bg, border);

            Color arrow = Enabled ? Theme.SubText : Theme.Divider;
            int cx = Width - 14;
            int cy = Height / 2;
            if (_hotZone == 1) arrow = Theme.Accent;
            DrawArrow(e.Graphics, new Point(cx, cy - 6), -1, arrow);
            arrow = Enabled ? Theme.SubText : Theme.Divider;
            if (_hotZone == 2) arrow = Theme.Accent;
            DrawArrow(e.Graphics, new Point(cx, cy + 6), 1, arrow);
        }

        void DrawArrow(Graphics g, Point center, int dir, Color c)
        {
            using (Pen pen = new Pen(c, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int s = 3;
                if (dir < 0)
                {
                    g.DrawLine(pen, center.X - s, center.Y + s / 2, center.X, center.Y - s / 2);
                    g.DrawLine(pen, center.X, center.Y - s / 2, center.X + s, center.Y + s / 2);
                }
                else
                {
                    g.DrawLine(pen, center.X - s, center.Y - s / 2, center.X, center.Y + s / 2);
                    g.DrawLine(pen, center.X, center.Y + s / 2, center.X + s, center.Y - s / 2);
                }
            }
        }
    }

    // ====================================================================
    //  滑块
    // ====================================================================

    public class Slider : FlatControl, IThemed
    {
        double _min;
        double _max;
        double _value;
        double _step;
        bool _dragging;
        bool _hover;

        public event EventHandler ValueChanged;

        public Slider()
        {
            _min = 0; _max = 1; _value = 0.5; _step = 0.05;
            Size = new Size(200, 30);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
        }

        public double Min { get { return _min; } set { _min = value; } }
        public double Max { get { return _max; } set { _max = value; } }
        public double Step { get { return _step; } set { _step = value <= 0 ? 0.01 : value; } }

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Snap(value);
                if (Math.Abs(v - _value) < 0.0001) return;
                _value = v;
                Invalidate();
                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetValueSilent(double v) { _value = Snap(v); Invalidate(); }

        double Snap(double v)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            if (_step > 0)
            {
                double n = Math.Round((v - _min) / _step);
                v = _min + n * _step;
            }
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            return v;
        }

        int ThumbX()
        {
            int pad = 8;
            int w = Math.Max(1, Width - pad * 2);
            double t = (_max - _min) <= 0 ? 0 : (_value - _min) / (_max - _min);
            return pad + (int)Math.Round(w * t);
        }

        void SetFromMouse(int x)
        {
            int pad = 8;
            int w = Math.Max(1, Width - pad * 2);
            double t = (double)(x - pad) / w;
            Value = _min + t * (_max - _min);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            _dragging = true;
            Capture = true;
            SetFromMouse(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromMouse(e.X);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            int cy = Height / 2;
            int pad = 8;
            Rectangle track = new Rectangle(pad, cy - 2, Math.Max(2, Width - pad * 2), 4);
            Color baseColor = Enabled ? Theme.Track : Theme.Disabled(Theme.Track);
            Theme.FillRounded(e.Graphics, track, 2, baseColor);

            int tx = ThumbX();
            Rectangle filled = new Rectangle(track.X, track.Y, Math.Max(2, tx - track.X), track.Height);
            Color accent = Enabled ? Theme.Accent : Theme.Disabled(Theme.Accent);
            Theme.FillRounded(e.Graphics, filled, 2, accent);

            int size = (_hover || _dragging) ? 16 : 14;
            Rectangle thumb = new Rectangle(tx - size / 2, cy - size / 2, size, size);
            using (SolidBrush shadow = new SolidBrush(Theme.Alpha(Color.Black, 30)))
                e.Graphics.FillEllipse(shadow, new Rectangle(thumb.X, thumb.Y + 1, thumb.Width, thumb.Height));
            using (SolidBrush b = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(b, thumb);
            using (Pen pen = new Pen(accent, 1.4f))
                e.Graphics.DrawEllipse(pen, thumb);
        }
    }

    // ====================================================================
    //  热键捕获框
    // ====================================================================

    public class HotkeyBox : FlatControl, IThemed
    {
        HotkeyBinding _binding;
        bool _capturing;
        bool _focused;
        bool _hover;
        public bool Conflict = false;

        public event EventHandler BindingChanged;

        public HotkeyBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            _binding = new HotkeyBinding(0, 0);
            Size = new Size(210, 34);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public HotkeyBinding Binding
        {
            get { return _binding; }
            set
            {
                _binding = value == null ? new HotkeyBinding(0, 0) : value;
                Invalidate();
            }
        }

        public void SetBindingSilent(HotkeyBinding b)
        {
            _binding = b == null ? new HotkeyBinding(0, 0) : b;
            Invalidate();
        }

        public void Clear()
        {
            SetBinding(new HotkeyBinding(0, 0));
        }

        void SetBinding(HotkeyBinding b)
        {
            _binding = b;
            Conflict = false;
            Invalidate();
            EventHandler h = BindingChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public static uint ModsFromModifierKeys(Keys m)
        {
            uint mods = 0;
            if ((m & Keys.Control) == Keys.Control) mods |= Native.MOD_CONTROL;
            if ((m & Keys.Alt) == Keys.Alt) mods |= Native.MOD_ALT;
            if ((m & Keys.Shift) == Keys.Shift) mods |= Native.MOD_SHIFT;
            return mods;
        }

        /// <summary>Keys -> Win32 虚拟键码。</summary>
        public static int VkFromKeys(Keys k)
        {
            int v = (int)k;
            if (v >= (int)Keys.A && v <= (int)Keys.Z) return 0x41 + (v - (int)Keys.A);
            if (v >= (int)Keys.D0 && v <= (int)Keys.D9) return 0x30 + (v - (int)Keys.D0);
            if (v >= (int)Keys.NumPad0 && v <= (int)Keys.NumPad9) return 0x60 + (v - (int)Keys.NumPad0);
            if (v >= (int)Keys.F1 && v <= (int)Keys.F12) return 0x70 + (v - (int)Keys.F1);
            return v;
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            _capturing = true;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { _focused = true; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _focused = false; _capturing = false; Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            _capturing = true;
            Keys k = e.KeyCode;

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (k == Keys.Escape) { _capturing = false; Invalidate(); return; }
            if (k == Keys.Back || k == Keys.Delete) { _capturing = false; Clear(); return; }
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
                k == Keys.LWin || k == Keys.RWin || k == Keys.Apps || k == Keys.None)
                return;   // 只按了修饰键，继续等真正的键

            uint mods = ModsFromModifierKeys(ModifierKeys);
            int vk = VkFromKeys(k);
            _capturing = false;
            SetBinding(new HotkeyBinding(mods, vk));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = Theme.Border;
            if (!Enabled) border = Theme.Divider;
            else if (_capturing) border = Theme.Accent;
            else if (Conflict) border = Theme.Danger;
            else if (_focused || _hover) border = Theme.Mix(Theme.Border, Theme.Accent, 0.5);

            Color fill = Enabled ? Theme.InputBg : Theme.Bg;
            if (_capturing) fill = Theme.Selected;
            Theme.RoundRect(e.Graphics, r, 6, fill, border);

            string text;
            Color tc;
            if (_capturing)
            {
                text = "请按下组合键…";
                tc = Theme.Accent;
            }
            else if (_binding == null || !_binding.IsSet)
            {
                text = "未设置";
                tc = Theme.SubText;
            }
            else
            {
                text = Native.HotkeyText(_binding.Modifiers, _binding.Key);
                tc = Enabled ? Theme.Text : Theme.SubText;
            }
            Theme.CenterText(e.Graphics, text, Theme.UI(9.5f, FontStyle.Regular),
                new Rectangle(0, 0, Width, Height), tc);
        }
    }

    // ====================================================================
    //  左侧导航项
    // ====================================================================

    public class NavItem : FlatControl, IThemed
    {
        bool _hover;
        bool _selected;

        public int Icon = Icons.General;

        public NavItem()
        {
            Size = new Size(166, 40);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Font = Theme.UI(9.5f, FontStyle.Regular);
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Invalidate(); }
        }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_selected)
            {
                Theme.FillRounded(e.Graphics, r, 6, Theme.Selected);
                Rectangle bar = new Rectangle(0, (Height - 18) / 2, 3, 18);
                Theme.FillRounded(e.Graphics, bar, 2, Theme.Accent);
            }
            else if (_hover)
            {
                Theme.FillRounded(e.Graphics, r, 6, Theme.Hover);
            }

            Color iconColor = _selected ? Theme.Accent : (_hover ? Theme.Text : Theme.SubText);
            Rectangle ibox = new Rectangle(16, (Height - 16) / 2, 16, 16);
            Icons.Draw(e.Graphics, Icon, ibox, iconColor);

            Color tc = _selected ? Theme.Accent : Theme.Text;
            Font f = _selected ? Theme.UI(9.5f, FontStyle.Bold) : Font;
            Theme.LeftText(e.Graphics, Text, f, new Rectangle(44, 0, Math.Max(0, Width - 50), Height), tc);
        }
    }

    // ====================================================================
    //  统计卡片（词库页的大数字）
    // ====================================================================

    public class StatCard : FlatControl, IThemed
    {
        public string Value = "-";
        public string Caption = "";
        public string Unit = "";

        public StatCard()
        {
            Size = new Size(130, 92);
            BackColor = Theme.Bg;
        }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.RoundRect(e.Graphics, r, 8, Theme.Card, Theme.Dark ? Theme.Divider : Color.Empty);

            string v = Value;
            if (Unit.Length > 0) v = v + " " + Unit;
            Theme.CenterText(e.Graphics, v, Theme.UI(19f, FontStyle.Bold),
                new Rectangle(0, 16, Width, 32), Theme.Accent);
            Theme.CenterText(e.Graphics, Caption, Theme.Small(),
                new Rectangle(0, 52, Width, 22), Theme.SubText);
        }
    }

    // ====================================================================
    //  候选预览条（输入页的"测试输入"）
    // ====================================================================

    public class CandidateStrip : FlatControl, IThemed
    {
        public string Code = "";
        public string Footer = "";
        public bool ShowIndex = true;
        public readonly List<string> Items = new List<string>();

        public CandidateStrip()
        {
            Size = new Size(560, 96);
            BackColor = Theme.Card;
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        public int MeasureHeight(int width)
        {
            int rows = RowCount(width);
            return 30 + Math.Max(1, rows) * 32 + (Footer.Length > 0 ? 22 : 0);
        }

        int RowCount(int width)
        {
            if (Items.Count == 0) return 1;
            int x = 0, rows = 1;
            for (int i = 0; i < Items.Count; i++)
            {
                int w = ChipWidth(i) + 8;
                if (x + w > width && x > 0) { rows++; x = 0; }
                x += w;
            }
            return rows;
        }

        int ChipWidth(int i)
        {
            string label = ShowIndex ? (i + 1).ToString() + "  " + Items[i] : Items[i];
            Size sz = TextRenderer.MeasureText(label, Theme.UI(9.5f, FontStyle.Regular));
            return sz.Width + 22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            if (Code.Length > 0)
            {
                Theme.LeftText(e.Graphics, "拼音：" + Code, Theme.UI(9f, FontStyle.Regular),
                    new Rectangle(0, 2, Width, 22), Theme.SubText);
            }

            int y = 30;
            int x = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                int w = ChipWidth(i);
                if (x + w > Width && x > 0) { x = 0; y += 32; }
                Rectangle chip = new Rectangle(x, y, w, 26);
                Color fill = i == 0 ? Theme.Selected : Theme.Hover;
                Color border = i == 0 ? Theme.Alpha(Theme.Accent, 120) : Color.Empty;
                Theme.RoundRect(e.Graphics, chip, 6, fill, border);
                string label = ShowIndex ? (i + 1).ToString() + "  " + Items[i] : Items[i];
                Theme.CenterText(e.Graphics, label, Theme.UI(9.5f, FontStyle.Regular), chip,
                    i == 0 ? Theme.Accent : Theme.Text);
                x += w + 8;
            }

            if (Items.Count == 0 && Footer.Length > 0)
            {
                Theme.LeftText(e.Graphics, Footer, Theme.Small(),
                    new Rectangle(0, 34, Width, 22), Theme.SubText);
            }
            else if (Footer.Length > 0)
            {
                Theme.LeftText(e.Graphics, Footer, Theme.Small(),
                    new Rectangle(0, y + 32, Width, 20), Theme.SubText);
            }
        }
    }

    // ====================================================================
    //  游戏进程名单里的一行
    // ====================================================================

    public class ProcessRow : FlatControl, IThemed
    {
        public string ProcessName = "";
        bool _hover;
        bool _hoverDelete;

        public event EventHandler DeleteClicked;

        public ProcessRow()
        {
            Height = 36;
            BackColor = Theme.Card;
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverDelete = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hd = e.X > Width - 60;
            if (hd != _hoverDelete) { _hoverDelete = hd; Cursor = hd ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.X > Width - 60)
            {
                EventHandler h = DeleteClicked;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 1, Width - 1, Height - 3);
            if (_hover) Theme.FillRounded(e.Graphics, r, 6, Theme.Hover);

            Theme.LeftText(e.Graphics, ProcessName, Theme.UI(9.5f, FontStyle.Regular),
                new Rectangle(12, 0, Math.Max(10, Width - 76), Height), Theme.Text);

            Color dc = _hoverDelete ? Theme.Danger : Theme.SubText;
            Theme.CenterText(e.Graphics, "删除", Theme.Small(),
                new Rectangle(Width - 56, 0, 44, Height), dc);
        }
    }

    // ====================================================================
    //  页面内提示条
    // ====================================================================

    public class NoticeBar : FlatControl, IThemed
    {
        public const int KindInfo = 0;
        public const int KindWarn = 1;
        public const int KindError = 2;
        public const int KindSuccess = 3;

        public int Kind = KindInfo;
        string _message = "";
        bool _shown;
        readonly Timer _timer;

        public NoticeBar()
        {
            BackColor = Theme.Bg;
            Height = 40;
            _timer = new Timer();
            _timer.Interval = 4000;
            _timer.Tick += delegate { _timer.Stop(); _shown = false; Visible = false; OnVisibility(false); };
        }

        /// <summary>提示条显示/隐藏后要重新排版页面。</summary>
        public event EventHandler VisibilityChanged;

        void OnVisibility(bool visible)
        {
            EventHandler h = VisibilityChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>自己的显示意图。注意不能用 Control.Visible —— 那是"有效可见性"，
        /// 父窗口还没显示的时候它恒为 false，会让页面排版全部塌掉。</summary>
        public bool Shown { get { return _shown; } }

        public void Show(string message, int kind, int autoHideMs)
        {
            _message = message == null ? "" : message;
            Kind = kind;
            _timer.Stop();
            _shown = true;
            Visible = true;
            if (autoHideMs > 0) { _timer.Interval = autoHideMs; _timer.Start(); }
            OnVisibility(true);
            Invalidate();
        }

        public void HideBar()
        {
            _timer.Stop();
            _shown = false;
            Visible = false;
            OnVisibility(false);
        }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }

        Color AccentOf()
        {
            if (Kind == KindWarn) return Theme.WarnColor;
            if (Kind == KindError) return Theme.Danger;
            if (Kind == KindSuccess) return Theme.Accent;
            return Theme.Accent;
        }

        public int MeasureHeight(int width)
        {
            int textWidth = Math.Max(60, width - 60);
            Size sz = TextRenderer.MeasureText(_message, Theme.UI(9f, FontStyle.Regular),
                new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(38, sz.Height + 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Color accent = AccentOf();
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.RoundRect(e.Graphics, r, 6, Theme.Mix(Theme.Card, accent, Theme.Dark ? 0.18 : 0.10),
                Theme.Alpha(accent, 90));

            using (SolidBrush b = new SolidBrush(accent))
                e.Graphics.FillEllipse(b, new Rectangle(14, Height / 2 - 4, 8, 8));

            TextRenderer.DrawText(e.Graphics, _message, Theme.UI(9f, FontStyle.Regular),
                new Rectangle(32, 8, Math.Max(40, Width - 46), Height - 16), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // ====================================================================
    //  右下角的"已保存"提示
    // ====================================================================

    public class ToastLabel : FlatControl, IThemed
    {
        public const int KindSuccess = 0;
        public const int KindError = 1;

        readonly Timer _timer;
        bool _visibleToast;

        public int Kind = KindSuccess;

        public ToastLabel()
        {
            BackColor = Theme.Bg;
            Size = new Size(220, 30);
            Visible = false;
            _timer = new Timer();
            _timer.Interval = 2000;
            _timer.Tick += delegate { _timer.Stop(); _visibleToast = false; Visible = false; };
        }

        public void ShowToast(string text, int ms)
        {
            Text = text;
            _visibleToast = true;
            Visible = true;
            _timer.Stop();
            _timer.Interval = ms;
            _timer.Start();
            Invalidate();
        }

        public bool IsShowing { get { return _visibleToast; } }

        public void ApplyTheme() { BackColor = Theme.Bg; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);

            Color accent = Kind == KindError ? Theme.Danger : Theme.Accent;

            Size sz = TextRenderer.MeasureText(Text, Font);
            int total = sz.Width + 26;
            int x = Width - total;
            if (x < 0) x = 0;

            using (SolidBrush b = new SolidBrush(accent))
                e.Graphics.FillEllipse(b, new Rectangle(x, Height / 2 - 8, 16, 16));
            if (Kind == KindError)
            {
                using (Pen pen = new Pen(Color.White, 1.7f))
                {
                    e.Graphics.DrawLine(pen, x + 5, Height / 2 - 4, x + 11, Height / 2 + 2);
                    e.Graphics.DrawLine(pen, x + 11, Height / 2 - 4, x + 5, Height / 2 + 2);
                }
            }
            else
            {
                Theme.CheckMark(e.Graphics, new Rectangle(x + 4, Height / 2 - 5, 9, 7), Color.White, 1.7f);
            }
            Theme.LeftText(e.Graphics, Text, Font, new Rectangle(x + 22, 0, sz.Width + 4, Height), accent);
        }
    }

    // ====================================================================
    //  下拉框（自绘 + 扁平菜单）
    // ====================================================================

    public class FlatColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuItemSelected { get { return Theme.Selected; } }
        public override Color MenuItemBorder { get { return Theme.Selected; } }
        public override Color MenuBorder { get { return Theme.Divider; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
    }

    public class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        public FlatMenuRenderer() : base(new FlatColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Theme.Card);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Divider))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Theme.Prep(e.Graphics);
            if (!e.Item.Selected && !e.Item.Pressed) return;
            Rectangle r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            Theme.FillRounded(e.Graphics, r, 5, Theme.Selected);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled) e.TextColor = Theme.SubText;
            else if (e.Item.Selected) e.TextColor = Theme.Accent;
            else e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }
    }

    public class SelectBox : FlatControl, IThemed
    {
        readonly List<string> _items = new List<string>();
        int _index;
        bool _hover;
        bool _open;
        ContextMenuStrip _menu;

        public event EventHandler SelectedIndexChanged;

        public SelectBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(240, 34);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void SetItems(List<string> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            Invalidate();
        }

        public int ItemCount { get { return _items.Count; } }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (value < 0) value = 0;
                if (value >= _items.Count) value = Math.Max(0, _items.Count - 1);
                if (value == _index) { Invalidate(); return; }
                _index = value;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get { if (_index < 0 || _index >= _items.Count) return ""; return _items[_index]; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            OpenMenu();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.Down) { SelectedIndex = _index + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = _index - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OpenMenu(); e.Handled = true; }
        }

        public void OpenMenu()
        {
            if (_items.Count == 0) return;
            CloseMenu();

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new FlatMenuRenderer();
            menu.BackColor = Theme.Card;
            menu.ForeColor = Theme.Text;
            menu.Font = Theme.UI(9.5f, FontStyle.Regular);
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(4);
            menu.MaximumSize = new Size(Math.Max(Width, 220), 400);

            for (int i = 0; i < _items.Count; i++)
            {
                string label = (i == _index ? "✓ " : "   ") + _items[i];
                ToolStripMenuItem item = new ToolStripMenuItem(label);
                item.Tag = i;
                item.Font = Theme.UI(9.5f, FontStyle.Regular);
                item.ForeColor = Theme.Text;
                item.Click += MenuItemClick;
                menu.Items.Add(item);
            }

            _menu = menu;
            _open = true;
            Invalidate();
            menu.Closed += delegate { _open = false; Invalidate(); };
            menu.Show(this, new Point(0, Height + 4));
        }

        void MenuItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null) return;
            SelectedIndex = (int)item.Tag;
        }

        void CloseMenu()
        {
            if (_menu == null) return;
            ContextMenuStrip m = _menu;
            _menu = null;
            try { m.Close(); m.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseMenu();
            base.Dispose(disposing);
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = Theme.Border;
            if (!Enabled) border = Theme.Divider;
            else if (_open || Focused) border = Theme.Accent;
            else if (_hover) border = Theme.Mix(Theme.Border, Theme.Accent, 0.5);
            Theme.RoundRect(e.Graphics, r, 6, Enabled ? Theme.InputBg : Theme.Bg, border);

            Color tc = Enabled ? Theme.Text : Theme.SubText;
            Theme.LeftText(e.Graphics, SelectedText, Theme.UI(9.5f, FontStyle.Regular),
                new Rectangle(11, 0, Math.Max(10, Width - 40), Height), tc);
            Theme.ChevronDown(e.Graphics, new Point(Width - 16, Height / 2), 9, Enabled ? Theme.SubText : Theme.Divider);
        }
    }

    // ====================================================================
    //  分段控件（主题选择用）
    // ====================================================================

    public class SegmentedControl : FlatControl, IThemed
    {
        string[] _items = new string[0];
        int _index;
        int _hoverIndex = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(240, 32);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
        }

        public void SetItems(string[] items)
        {
            _items = items == null ? new string[0] : items;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (value < 0 || value >= _items.Length) return;
                if (value == _index) return;
                _index = value;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetIndexSilent(int value)
        {
            if (value >= 0 && value < _items.Length) { _index = value; Invalidate(); }
        }

        int IndexAt(Point p)
        {
            if (_items.Length == 0) return -1;
            int seg = Math.Max(1, Width / _items.Length);
            int i = p.X / seg;
            if (i < 0) i = 0;
            if (i >= _items.Length) i = _items.Length - 1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != _hoverIndex) { _hoverIndex = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            SelectedIndex = IndexAt(e.Location);
        }

        public void ApplyTheme() { BackColor = Theme.Card; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prep(e.Graphics);
            e.Graphics.Clear(BackColor);
            if (_items.Length == 0) return;

            Rectangle all = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(e.Graphics, all, 6, Enabled ? Theme.Hover : Theme.Bg);

            int seg = Width / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                Rectangle cell = new Rectangle(i * seg, 0, seg, Height);
                if (i == _index)
                {
                    Rectangle pill = new Rectangle(cell.X + 2, 2, cell.Width - 4, Height - 5);
                    Theme.RoundRect(e.Graphics, pill, 5, Enabled ? Theme.Card : Theme.Bg,
                        Theme.Alpha(Theme.Accent, Enabled ? 110 : 40));
                }
                Color tc = !Enabled ? Theme.SubText : (i == _index ? Theme.Text : Theme.SubText);
                Font f = i == _index ? Theme.UI(9f, FontStyle.Bold) : Theme.UI(9f, FontStyle.Regular);
                Theme.CenterText(e.Graphics, _items[i], f, cell, tc);
            }
        }
    }
}
