using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>程序图标（不依赖 .ico 文件，运行时画出来）。</summary>
    public static class AppIcon
    {
        static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null) return _cached;
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            int d = 18;
                            path.AddArc(1, 1, d, d, 180, 90);
                            path.AddArc(31 - d - 1, 1, d, d, 270, 90);
                            path.AddArc(31 - d - 1, 31 - d - 1, d, d, 0, 90);
                            path.AddArc(1, 31 - d - 1, d, d, 90, 90);
                            path.CloseFigure();
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(0x07, 0xC1, 0x60)))
                                g.FillPath(b, path);
                        }
                        using (Font f = new Font("Microsoft YaHei", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush w = new SolidBrush(Color.White))
                        {
                            StringFormat sf = new StringFormat();
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("墨", f, w, new RectangleF(0, 0, 32, 32), sf);
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    _cached = Icon.FromHandle(h);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("生成图标失败，用系统默认图标: " + ex.Message);
                _cached = SystemIcons.Application;
            }
            return _cached;
        }
    }

    /// <summary>托盘图标 + 右键菜单。</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly NotifyIcon _icon;
        readonly ToolStripMenuItem _modeItem;
        readonly ToolStripMenuItem _lockItem;

        public event Action ToggleModeRequested;
        public event Action ToggleLockRequested;
        public event Action SettingsRequested;
        public event Action AiPanelRequested;
        public event Action ExitRequested;
        public event Action LogRequested;

        public TrayIcon()
        {
            _icon = new NotifyIcon();
            _icon.Icon = AppIcon.Get();
            _icon.Visible = true;
            _icon.Text = AppPaths.AppNameCn;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            _modeItem = new ToolStripMenuItem("切换到英文模式");
            _modeItem.Click += delegate { Raise(ToggleModeRequested); };
            menu.Items.Add(_modeItem);

            _lockItem = new ToolStripMenuItem("锁定输入法（游戏模式）");
            _lockItem.Click += delegate { Raise(ToggleLockRequested); };
            menu.Items.Add(_lockItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem ai = new ToolStripMenuItem("AI 写作助手…");
            ai.Click += delegate { Raise(AiPanelRequested); };
            menu.Items.Add(ai);

            ToolStripMenuItem settings = new ToolStripMenuItem("设置…");
            settings.Click += delegate { Raise(SettingsRequested); };
            menu.Items.Add(settings);

            ToolStripMenuItem log = new ToolStripMenuItem("打开日志文件夹");
            log.Click += delegate { Raise(LogRequested); };
            menu.Items.Add(log);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Raise(ExitRequested); };
            menu.Items.Add(exit);

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += delegate { Raise(SettingsRequested); };
        }

        static void Raise(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception ex) { Log.Error("托盘菜单异常", ex); }
        }

        public void Update(bool english, bool locked)
        {
            try
            {
                _modeItem.Text = english ? "切换到中文模式" : "切换到英文模式";
                _lockItem.Text = locked ? "解锁输入法（恢复输入）" : "锁定输入法（游戏模式）";
                _lockItem.Checked = locked;
                string state = locked ? "已锁定" : (english ? "英文" : "中文");
                string t = AppPaths.AppNameCn + " · " + state;
                _icon.Text = t.Length > 62 ? t.Substring(0, 62) : t;
            }
            catch { }
        }

        public void Balloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(2500, title, text, ToolTipIcon.Info); }
            catch { }
        }

        public void Dispose()
        {
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
        }
    }
}
