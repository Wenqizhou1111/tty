using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Core
{
    static class Program
    {
        const string MutexName = "MyIME.SingleInstance.v1";
        const string ShowSettingsEvent = "MyIME.ShowSettings.v1";

        static Mutex _mutex;
        static Lexicon _lex;
        static ImeController _controller;
        static AiAssistant _ai;
        static MessageWindow _msg;
        static CandidateBar _bar;
        static bool _ownsMutex;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out _ownsMutex);
            }
            catch { _ownsMutex = true; }

            if (!_ownsMutex)
            {
                // 已经有一个在跑：让它把设置窗口弹出来，然后自己退出
                try
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(ShowSettingsEvent, out ev))
                    {
                        using (ev) { ev.Set(); }
                    }
                }
                catch { }
                return;
            }

            // DPI 感知必须在任何窗口创建之前设置
            try { Native.SetProcessDPIAware(); }
            catch (Exception ex) { Log.Warn("设置 DPI 感知失败: " + ex.Message); }

            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("未处理异常", e.ExceptionObject as Exception);
            };
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Log.Error("UI 线程异常", e.Exception);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Log.MinLevel = LogLevel.Info;
            string lvl = Environment.GetEnvironmentVariable("MYIME_LOG_LEVEL");
            if (!string.IsNullOrEmpty(lvl))
            {
                try { Log.MinLevel = (LogLevel)Enum.Parse(typeof(LogLevel), lvl, true); }
                catch { }
            }
            Log.Info("================ " + AppPaths.AppNameCn + " v" + AppPaths.Version + " 启动 ================");

            AppSettings cfg = SettingsStore.Load();

            _lex = new Lexicon();
            try { _lex.LoadAll(); }
            catch (Exception ex) { Log.Error("加载词库失败", ex); }

            _msg = new MessageWindow();
            _bar = new CandidateBar();

            _controller = new ImeController(_msg, _bar, _lex, cfg);
            _ai = new AiAssistant(_controller, cfg);
            _controller.Ai = _ai;
            _controller.RequestSettings = OpenSettings;

            // 设置程序改了配置 -> 这边立刻生效
            SettingsStore.ListenForChanges(delegate
            {
                AppSettings fresh = SettingsStore.Current;
                try
                {
                    _controller.ApplySettings(fresh);
                    if (_ai != null) _ai.ApplySettings(fresh);
                    _bar.ApplyUiSettings(fresh.Ui);
                }
                catch (Exception ex) { Log.Error("应用新设置失败", ex); }
            });
            ListenForShowSettings();

            _controller.Start();

            if (args != null && args.Length > 0 && args[0] == "--settings") OpenSettings();

            Application.ApplicationExit += delegate { Shutdown(); };
            try
            {
                Application.Run();
            }
            catch (Exception ex)
            {
                Log.Error("消息循环异常退出", ex);
            }
            Shutdown();
        }

        static void OpenSettings()
        {
            try
            {
                if (!File.Exists(AppPaths.SettingsExe))
                {
                    Log.Warn("找不到设置程序: " + AppPaths.SettingsExe);
                    return;
                }
                System.Diagnostics.Process.Start(AppPaths.SettingsExe);
            }
            catch (Exception ex) { Log.Error("打开设置失败", ex); }
        }

        static void ListenForShowSettings()
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent))
                    {
                        while (true)
                        {
                            ev.WaitOne();
                            try { OpenSettings(); } catch { }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("设置唤起监听异常", ex); }
            });
            t.IsBackground = true;
            t.Name = "ShowSettingsWatcher";
            t.Start();
        }

        static void Shutdown()
        {
            Log.Info("正在退出…");
            try { if (_ai != null) _ai.Dispose(); } catch { }
            try { if (_controller != null) _controller.Dispose(); } catch { }
            try { if (_lex != null) _lex.SaveUserWords(); } catch { }
            try { if (_msg != null) _msg.Dispose(); } catch { }
            try { if (_mutex != null && _ownsMutex) _mutex.ReleaseMutex(); } catch { }
            Log.Info("已退出");
        }
    }
}
