using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MyIme.Shared
{
    /// <summary>一个可注册的全局热键。Key == 0 表示未设置。</summary>
    [Serializable]
    public class HotkeyBinding
    {
        public uint Modifiers;
        public int Key;

        public HotkeyBinding() { }

        public HotkeyBinding(uint modifiers, int key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public bool IsSet { get { return Key != 0; } }

        public override string ToString()
        {
            return Native.HotkeyText(Modifiers, Key);
        }

        public HotkeyBinding Copy()
        {
            return new HotkeyBinding(Modifiers, Key);
        }

        public bool SameAs(HotkeyBinding other)
        {
            if (other == null) return false;
            return Modifiers == other.Modifiers && Key == other.Key;
        }
    }

    public class GeneralSettings
    {
        public bool StartWithWindows = true;
        public bool ShowTrayIcon = true;
        public bool SoundOnModeSwitch = false;
        public bool ShowNotificationOnLock = true;
        public string Theme = "auto";              // auto | light | dark
    }

    public class InputSettings
    {
        public int PageSize = 5;                   // 候选条每页候选数（5~9）
        public bool ShowPinyinInBar = true;        // 输入条上显示拼音
        public bool SmartPunctuation = true;       // 中文标点（，。？！）
        public bool Jianpin = true;                // 简拼（首字母）
        public bool AutoCommitUnique = false;      // 唯一候选自动上屏
        public bool AutoCommitOnSpace = true;      // 空格上屏首候选
        public int MaxWordLength = 6;              // 整词匹配最长汉字数
        public bool PredictEnabled = true;         // 上屏后联想
        public int PredictCount = 4;
        public int HistoryBoost = 300;             // 用户词频加权

        // 模糊音
        public bool FuzzyZhZ = false;              // zh <-> z
        public bool FuzzyChC = false;              // ch <-> c
        public bool FuzzyShS = false;              // sh <-> s
        public bool FuzzyNL = false;               // n <-> l
        public bool FuzzyAnAng = false;            // an <-> ang
        public bool FuzzyInIng = false;            // in <-> ing
        public bool FuzzyUanUang = false;          // uan <-> uang

        // 英文模式
        public bool StartInEnglish = false;
        public bool EnglishCompletion = true;      // 英文单词本地补全
        public int EnglishMinPrefix = 2;
        public int EnglishMaxSuggestions = 5;
        public bool EnglishAutoCapitalize = true;
    }

    public class GameLockSettings
    {
        /// <summary>锁定/解锁开关热键。锁定后钩子会被整体摘掉，游戏完全不受干扰。</summary>
        public HotkeyBinding ToggleKey = new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, 0x4C); // Ctrl+Alt+L
        public bool AutoLockFullscreen = true;     // 前台全屏（游戏）时自动锁定
        public bool AutoLockGames = true;          // 命中游戏进程名单时自动锁定
        public bool KeepUnlockHotkey = true;       // 锁定时仍保留解锁热键
        public bool NotifyOnAutoLock = false;
        public List<string> GameProcesses = new List<string>(new string[] {
            "league of legends", "cs2", "csgo", "valorant", "dota2", "pubg", "genshinimpact",
            "yuanshen", "starrail", "bf2042", "r5apex", "rainbowsix", "overwatch", "wow",
            "minecraft", "terraria", "stardew valley", "hollow_knight", "eldenring",
            "sekiro", "darksoulsiii", "rdr2", "gta5", "cyberpunk2077", "forzahorizon5"
        });
    }

    public class HotkeySettings
    {
        public HotkeyBinding ToggleChineseEnglish = new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, 0x45); // Ctrl+Alt+E
        public HotkeyBinding ToggleAiPanel = new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, 0x41);        // Ctrl+Alt+A
        public HotkeyBinding OpenSettings = new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, 0x53);         // Ctrl+Alt+S
        public HotkeyBinding AiPolishSelection = new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, 0x50);    // Ctrl+Alt+P
        public bool ShiftTapTogglesChinese = true; // 轻按 Shift 切中英（输入法惯用）
    }

    /// <summary>润色风格预设（设置界面里的"各种润色效果"）。</summary>
    public class AiSettings
    {
        public bool Enabled = false;                       // AI 功能总开关
        public bool ShowButtonInBar = true;                // 输入条上显示 AI 按钮
        public string Provider = "openai";                 // openai | anthropic | custom
        public string BaseUrl = "https://api.deepseek.com/v1";
        public string ApiKeyProtected = "";                // DPAPI 加密后的 base64，绝不存明文
        public string Model = "deepseek-chat";
        public double Temperature = 0.6;
        public int MaxTokens = 800;
        public int TimeoutSeconds = 40;
        public bool Stream = true;
        public string SystemPrompt = "";
        public Dictionary<string, string> ExtraHeaders = new Dictionary<string, string>();
        public string CustomBodyTemplate = "";             // Provider=custom 时的请求体模板
        public string ResponsePath = "";                   // 结果字段路径，空 = 自动嗅探

        // 模式一：自动读取修改
        public bool AutoReadMode = true;                   // 允许"自动读取输入框内容并修改"
        public string AutoPreset = "concise";              // 默认润色风格
        public bool AutoReplaceAll = true;                 // true = 替换整段；false = 只在光标处插入
        public bool ConfirmBeforeReplace = true;           // 替换前先给预览确认

        // 模式二：指令模式
        public bool CommandMode = true;                    // 允许在输入条里输入要求

        public List<string> FavoriteActions = new List<string>(new string[] { "polish", "expand", "continue", "fix" });
        public Dictionary<string, string> CustomPresets = new Dictionary<string, string>();
    }

    public class UiSettings
    {
        public int FontSize = 16;                  // 候选条字号
        public int BarOpacity = 98;                // 0~100
        public string AccentColor = "#07C160";     // 微信绿
        public bool ShowModeLabel = true;          // 显示 [中]/[英] 标记
        public bool ShowPageIndicator = true;
        public bool ShowIndex = true;              // 候选前的 1. 2. 3.
        public bool RoundedCorners = true;
        public bool FollowDarkMode = true;
        public int BarMaxWidth = 960;
        public bool Vertical = false;              // 竖排候选
        public int BarOffsetY = 26;                // 相对光标的垂直偏移
    }

    /// <summary>完整设置。全部用 public 字段，这样反射映射最省事。</summary>
    public class AppSettings
    {
        public int Version = 1;
        public GeneralSettings General = new GeneralSettings();
        public InputSettings Input = new InputSettings();
        public GameLockSettings GameLock = new GameLockSettings();
        public HotkeySettings Hotkeys = new HotkeySettings();
        public AiSettings Ai = new AiSettings();
        public UiSettings Ui = new UiSettings();

        public AppSettings Clone()
        {
            return Json.Clone(this);
        }
    }

    /// <summary>设置的读写 + 变更通知（跨进程用命名事件）。</summary>
    public static class SettingsStore
    {
        public const string ChangedEventName = "MyIME.SettingsChanged.v1";

        static readonly object Gate = new object();
        static AppSettings _current;

        /// <summary>本进程最近一次读到的设置。</summary>
        public static AppSettings Current
        {
            get
            {
                lock (Gate)
                {
                    if (_current == null) _current = Load();
                    return _current;
                }
            }
        }

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                string path = AppPaths.SettingsFile;
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    object json = Json.TryParse(text);
                    if (json is Dictionary<string, object>)
                    {
                        s = new AppSettings();
                        Json.Apply(s, json);
                    }
                    else
                    {
                        Log.Warn("settings.json 解析失败，使用默认设置");
                        BackupCorrupt(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取设置失败", ex);
            }
            if (s == null) s = new AppSettings();
            lock (Gate) { _current = s; }
            return s;
        }

        static void BackupCorrupt(string path)
        {
            try { File.Copy(path, path + ".bad", true); } catch { }
        }

        public static void Save(AppSettings s)
        {
            if (s == null) return;
            lock (Gate)
            {
                try
                {
                    string path = AppPaths.SettingsFile;
                    AppPaths.EnsureDir(Path.GetDirectoryName(path));
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, Json.StringifyPretty(s), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                    _current = s;
                    Log.Info("设置已保存: " + path);
                }
                catch (Exception ex)
                {
                    Log.Error("保存设置失败", ex);
                    throw;
                }
            }
            SignalChanged();
        }

        /// <summary>通知其它进程设置变了。</summary>
        public static void SignalChanged()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ChangedEventName, out ev))
                {
                    using (ev) { ev.Set(); }
                }
            }
            catch { }
        }

        /// <summary>在后台线程等设置变更通知，收到就回调（并重新 Load）。</summary>
        public static void ListenForChanges(Action onChanged)
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, ChangedEventName))
                    {
                        while (true)
                        {
                            if (!ev.WaitOne()) continue;
                            try
                            {
                                AppSettings fresh = Load();
                                if (onChanged != null) onChanged();
                                Log.Info("检测到设置变更，已重新加载");
                            }
                            catch (Exception ex) { Log.Error("重载设置失败", ex); }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("设置监听线程异常", ex); }
            });
            t.IsBackground = true;
            t.Name = "SettingsWatcher";
            t.Start();
        }
    }
}
