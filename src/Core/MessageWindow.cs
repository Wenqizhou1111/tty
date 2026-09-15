using System;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 只收消息、不显示的窗口（HWND_MESSAGE）。
    /// RegisterHotKey 需要一个窗口句柄来接收 WM_HOTKEY，用这个最干净 ——
    /// 它不会出现在任务栏、不会被截图、也不抢焦点。
    /// </summary>
    public sealed class MessageWindow : NativeWindow, IDisposable
    {
        public const int WM_HOTKEY = 0x0312;

        /// <summary>参数依次是: 消息号, wParam, lParam。</summary>
        public event Action<int, IntPtr, IntPtr> MessageReceived;

        public MessageWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "MyIME_MessageWindow";
            cp.Parent = (IntPtr)(-3);      // HWND_MESSAGE
            cp.Style = 0;
            cp.ExStyle = 0;
            cp.X = 0; cp.Y = 0; cp.Width = 0; cp.Height = 0;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                Action<int, IntPtr, IntPtr> h = MessageReceived;
                if (h != null) h(m.Msg, m.WParam, m.LParam);
            }
            catch (Exception ex)
            {
                Log.Error("消息窗口处理异常", ex);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { if (Handle != IntPtr.Zero) DestroyHandle(); }
            catch { }
        }
    }

    /// <summary>全局热键管理：注册、撤销、派发。</summary>
    public sealed class HotkeyManager : IDisposable
    {
        readonly MessageWindow _wnd;
        readonly System.Collections.Generic.Dictionary<int, string> _byId =
            new System.Collections.Generic.Dictionary<int, string>();
        readonly System.Collections.Generic.Dictionary<string, HotkeyBinding> _bindings =
            new System.Collections.Generic.Dictionary<string, HotkeyBinding>();
        int _nextId = 1;

        /// <summary>参数是热键的名字（注册时用的 key）。</summary>
        public event Action<string> Pressed;

        public HotkeyManager(MessageWindow wnd)
        {
            _wnd = wnd;
            _wnd.MessageReceived += OnMessage;
        }

        void OnMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg != MessageWindow.WM_HOTKEY) return;
            int id = wParam.ToInt32();
            string name;
            if (_byId.TryGetValue(id, out name))
            {
                Action<string> h = Pressed;
                if (h != null) { try { h(name); } catch (Exception ex) { Log.Error("热键处理异常 " + name, ex); } }
            }
        }

        /// <summary>注册一个热键。返回是否成功（可能被别的程序占用）。</summary>
        public bool Register(string name, HotkeyBinding binding)
        {
            if (binding == null || !binding.IsSet) return false;
            Unregister(name);
            int id = _nextId++;
            bool ok = Native.RegisterHotKey(_wnd.Handle, id, binding.Modifiers | Native.MOD_NOREPEAT, (uint)binding.Key);
            if (ok)
            {
                _byId[id] = name;
                _bindings[name] = binding;
                Log.Info("热键注册成功: " + name + " = " + binding.ToString());
            }
            else
            {
                Log.Warn("热键注册失败（可能被其它程序占用）: " + name + " = " + binding.ToString()
                         + " 错误码 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
            return ok;
        }

        public void Unregister(string name)
        {
            HotkeyBinding b;
            if (!_bindings.TryGetValue(name, out b)) return;
            foreach (System.Collections.Generic.KeyValuePair<int, string> kv in _byId)
            {
                if (kv.Value == name)
                {
                    Native.UnregisterHotKey(_wnd.Handle, kv.Key);
                    _byId.Remove(kv.Key);
                    break;
                }
            }
            _bindings.Remove(name);
        }

        /// <summary>重新注册全部（设置变更后用）。</summary>
        public void Rebind(string name, HotkeyBinding binding)
        {
            Unregister(name);
            if (binding != null && binding.IsSet) Register(name, binding);
        }

        public void UnregisterAll()
        {
            foreach (System.Collections.Generic.KeyValuePair<int, string> kv in
                     new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>(_byId))
            {
                Native.UnregisterHotKey(_wnd.Handle, kv.Key);
            }
            _byId.Clear();
            _bindings.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_wnd != null) _wnd.MessageReceived -= OnMessage;
        }
    }
}
