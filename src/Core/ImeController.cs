using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// 输入法主控：按键状态机 + 上屏 + 联想 + 游戏锁定 + 热键派发。
    ///
    /// 线程模型：全部跑在 UI 线程（也就是装钩子的那个线程）。
    /// 钩子回调里只做纯计算，真正的 SendInput 通过 PostMessage 排到消息循环里做，
    /// 这样钩子回调永远是微秒级返回，不会被 Windows 判超时摘掉钩子。
    /// </summary>
    public sealed class ImeController : IDisposable
    {
        public const string HkToggleMode = "toggle-mode";
        public const string HkToggleLock = "toggle-lock";
        public const string HkAiPanel = "ai-panel";
        public const string HkAiPolish = "ai-polish";
        public const string HkOpenSettings = "open-settings";

        readonly MessageWindow _msg;
        readonly HotkeyManager _hotkeys;
        readonly KeyboardHook _hook;
        readonly CandidateBar _bar;
        readonly TrayIcon _tray;
        readonly Timer _watch;
        readonly Timer _clickWatch;
        readonly Lexicon _lex;

        bool _lmbDown;              // 上一轮轮询时左键是不是按着的（用来抓"按下沿"）

        AppSettings _cfg;
        PinyinEngine _engine;

        readonly List<Candidate> _candidates = new List<Candidate>();
        List<Candidate> _predictions = new List<Candidate>();
        Composition _comp;

        string _buffer = "";
        int _caret;                 // 光标在 _buffer 里的位置（0..Length），点拼音可以挪
        int _selected;
        int _page;
        bool _english;
        bool _locked;
        bool _autoLocked;
        bool _predicting;
        int _quoteState;

        bool _shiftDown;
        bool _shiftUsed;
        DateTime _shiftDownAt;

        readonly HashSet<int> _swallowed = new HashSet<int>();
        readonly List<string> _injectQueue = new List<string>();
        readonly object _injectGate = new object();

        public AiAssistant Ai;
        public Action RequestSettings;

        public bool IsLocked { get { return _locked; } }
        public bool IsEnglish { get { return _english; } }

        public ImeController(MessageWindow msg, CandidateBar bar, Lexicon lex, AppSettings cfg)
        {
            _msg = msg;
            _bar = bar;
            _lex = lex;
            _cfg = cfg;
            _engine = new PinyinEngine(lex, cfg.Input);

            _msg.MessageReceived += OnWindowMessage;

            _hotkeys = new HotkeyManager(msg);
            _hotkeys.Pressed += OnHotkey;

            _hook = new KeyboardHook();
            _hook.OnKey = OnKey;
            _hook.OnError = delegate (string m) { Log.Error(m); };

            _tray = new TrayIcon();
            _tray.ToggleModeRequested += ToggleMode;
            _tray.ToggleLockRequested += delegate { SetLocked(!_locked, false); };
            _tray.SettingsRequested += delegate { if (RequestSettings != null) RequestSettings(); };
            _tray.AiPanelRequested += delegate { if (Ai != null) Ai.ShowPanel(true); };
            _tray.LogRequested += OpenLogFolder;
            _tray.ExitRequested += delegate { Application.Exit(); };

            _bar.CandidateClicked += OnBarCandidateClicked;
            _bar.PageRequested += delegate (int d) { if (Page(d)) UpdateBar(); };
            _bar.AiButtonClicked += delegate { if (Ai != null) Ai.ShowPanel(true); };
            _bar.BufferClicked += OnBarBufferClicked;
            _bar.ShowAiButton = cfg.Ai.Enabled && cfg.Ai.ShowButtonInBar;

            _watch = new Timer();
            _watch.Interval = 700;
            _watch.Tick += delegate { WatchTick(); };

            // 鼠标左键"按下沿"检测：正在输入时点到别处就结束这次输入。
            // 120ms 轮询足够灵敏，比再装一个全局鼠标钩子安全得多
            // （钩子越多越容易被反作弊盯上，而且这里只需要知道"点没点"）。
            _clickWatch = new Timer();
            _clickWatch.Interval = 120;
            _clickWatch.Tick += delegate { ClickWatchTick(); };
            _clickWatch.Start();

            _tray.Update(_english, _locked);
            _bar.ApplyUiSettings(cfg.Ui);
        }

        // ==================== 生命周期 ====================

        public void Start()
        {
            _english = _cfg.Input.StartInEnglish;
            _hook.Install();
            RegisterHotkeys();
            if (_cfg.GameLock.AutoLockFullscreen || _cfg.GameLock.AutoLockGames) _watch.Start();
            _tray.Update(_english, _locked);
            Log.Info("输入法已启动，模式=" + (_english ? "英文" : "中文"));
        }

        public void ApplySettings(AppSettings cfg)
        {
            _cfg = cfg;
            _engine.UpdateSettings(cfg.Input);
            _bar.ApplyUiSettings(cfg.Ui);
            _bar.ShowAiButton = cfg.Ai.Enabled && cfg.Ai.ShowButtonInBar;
            RegisterHotkeys();

            if (cfg.GameLock.AutoLockFullscreen || cfg.GameLock.AutoLockGames) { if (!_watch.Enabled) _watch.Start(); }
            else _watch.Stop();

            // 名单/开关可能被改了，清掉前台缓存和锁定状态机，下次重新判定
            GameLock.ResetCache();

            if (!_locked && !_hook.IsInstalled) _hook.Install();
            _tray.Update(_english, _locked);
            Log.Info("设置已应用");
        }

        void RegisterHotkeys()
        {
            _hotkeys.Rebind(HkToggleMode, _cfg.Hotkeys.ToggleChineseEnglish);
            _hotkeys.Rebind(HkAiPanel, _cfg.Hotkeys.ToggleAiPanel);
            _hotkeys.Rebind(HkOpenSettings, _cfg.Hotkeys.OpenSettings);
            _hotkeys.Rebind(HkAiPolish, _cfg.Hotkeys.AiPolishSelection);

            // 锁定热键：即使锁定时也要保留，否则用户没法解锁
            HotkeyBinding lockKey = _cfg.GameLock.ToggleKey;
            if (lockKey != null && lockKey.IsSet) _hotkeys.Rebind(HkToggleLock, lockKey);
            else _hotkeys.Unregister(HkToggleLock);
        }

        void OnHotkey(string name)
        {
            Log.Info("热键触发: " + name);
            if (name == HkToggleMode) ToggleMode();
            else if (name == HkToggleLock) SetLocked(!_locked, false);
            else if (name == HkOpenSettings) { if (RequestSettings != null) RequestSettings(); }
            else if (name == HkAiPanel) { if (Ai != null) Ai.ShowPanel(true); }
            else if (name == HkAiPolish) { if (Ai != null) Ai.ShowPanel(false); }
        }

        void OnWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Native.WM_APP_FLUSH) FlushInject();
        }

        // ==================== 锁定（游戏模式） ====================

        public void SetLocked(bool locked, bool auto)
        {
            if (_locked == locked && !auto) return;
            _locked = locked;
            _autoLocked = locked && auto;

            try
            {
                if (locked)
                {
                    CancelAll();
                    // 锁定时把钩子**整体摘掉**：不装钩子 = 游戏里零干扰，也不会被反作弊盯上
                    _hook.Uninstall();
                }
                else
                {
                    _hook.Install();
                }
            }
            catch (Exception ex) { Log.Error("切换锁定状态失败", ex); }

            _tray.Update(_english, _locked);
            if (locked && _cfg.General.ShowNotificationOnLock)
                _tray.Balloon(AppPaths.AppNameCn, auto ? "检测到全屏游戏，已自动锁定输入法" : "输入法已锁定，按键将全部直通");
            Log.Info("锁定状态 -> " + (locked ? "已锁定" : "已解锁") + (auto ? "（自动）" : "（手动）"));
        }

        void WatchTick()
        {
            try
            {
                bool should = GameLock.ShouldAutoLock(_cfg.GameLock);
                if (should && !_locked)
                {
                    Log.Info("自动锁定原因: " + GameLock.DescribeForeground()
                             + " | 名单命中=" + GameLock.IsForegroundInList(_cfg.GameLock.GameProcesses)
                             + " 全屏=" + GameLock.IsForegroundFullscreen()
                             + " D3D=" + GameLock.IsD3DFullscreen()
                             + " 非游戏全屏=" + GameLock.IsNonGameFullscreenApp());
                    SetLocked(true, true);
                }
                else if (!should && _locked && _autoLocked) SetLocked(false, true);
            }
            catch (Exception ex) { Log.Warn("自动锁定检测异常: " + ex.Message); }
        }

        public void ToggleMode()
        {
            _english = !_english;
            CancelAll();
            if (_cfg.General.SoundOnModeSwitch)
            {
                try { Native.MessageBeep(0x00000040); } catch { }
            }
            _tray.Update(_english, _locked);
            Log.Info("中英切换 -> " + (_english ? "英文" : "中文"));
        }

        // ==================== 按键处理（钩子回调里跑，必须快） ====================

        static bool IsShiftKey(int vk) { return vk == 0x10 || vk == 0xA0 || vk == 0xA1; }

        void HandleShiftKey(bool isDown)
        {
            if (isDown)
            {
                _shiftDown = true;
                _shiftUsed = false;
                _shiftDownAt = DateTime.Now;
                return;
            }
            bool tap = _shiftDown && !_shiftUsed
                       && (DateTime.Now - _shiftDownAt).TotalMilliseconds < 450;
            _shiftDown = false;
            if (tap && _cfg.Hotkeys.ShiftTapTogglesChinese) ToggleMode();
        }

        bool OnKey(int vk, bool isDown, bool shift, bool ctrl, bool alt)
        {
            try
            {
                if (_locked) return false;
                if (IsShiftKey(vk)) { HandleShiftKey(isDown); return false; }
                if (isDown && _shiftDown) _shiftUsed = true;

                if (!isDown)
                {
                    // 只吞"按下时被我们吃掉的那个键"对应的那一次抬起
                    if (_swallowed.Remove(vk)) return true;
                    return false;
                }

                // 【不要】在这里检查 _swallowed 来吞掉重复的按下事件。
                // Windows 的按键自动重复是"连发 KEYDOWN、中间不发 KEYUP"。
                // 如果因为集合里已经有这个键就直接吞掉，按住退格键就只会删掉
                // 一个字符，后面的重发全被吃掉 —— 表现为"退格失灵、字删不掉"。
                // 正确的做法：每次按下都正常处理，只在抬起时把对应的那次吞掉。
                if (ctrl || alt) return false;              // Ctrl/Alt 组合键一律放行

                if (Log.MinLevel == LogLevel.Debug)
                    Log.Debug("key vk=0x" + vk.ToString("X2") + " shift=" + shift
                              + " buf=[" + _buffer + "] pred=" + _predictions.Count);

                if (_buffer.Length > 0 || _predictions.Count > 0)
                {
                    if (HandleActive(vk, shift)) { _swallowed.Add(vk); return true; }
                }

                if (KeyboardHook.IsLetter(vk))
                {
                    ClearPredictions();
                    StartBuffer(KeyboardHook.LetterOf(vk).ToString());
                    _swallowed.Add(vk);
                    return true;
                }

                if (!_english && _cfg.Input.SmartPunctuation)
                {
                    char p = KeyboardHook.PunctuationOf(vk, shift);
                    if (p != '\0')
                    {
                        string mapped = PinyinEngine.MapPunctuation(p, shift, ref _quoteState);
                        if (mapped != null)
                        {
                            ClearPredictions();
                            QueueInject(mapped);
                            _swallowed.Add(vk);
                            return true;
                        }
                    }
                }

                if (_predictions.Count > 0) { ClearPredictions(); UpdateBar(); }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("按键处理异常", ex);
                return false;
            }
        }

        /// <summary>处理"正在组合"或"正在联想"状态下的按键。返回 true 表示吃掉这个键。</summary>
        bool HandleActive(int vk, bool shift)
        {
            // Esc：立刻退出输入框
            if (vk == 0x1B) { CancelAll(); return true; }

            // 退格：删光标**前面**那个字母
            if (vk == 0x08)
            {
                if (_buffer.Length > 0)
                {
                    if (_caret > 0)
                    {
                        _buffer = _buffer.Remove(_caret - 1, 1);
                        _caret--;
                    }
                    else
                    {
                        // 光标已经顶在最前面，没东西可删。
                        // 这里必须 return true 把它吃掉 —— 放过去的话会落到应用里
                        // 把正文的字删掉，看起来就像是"退格乱删/删不掉"。
                        return true;
                    }
                    // 字母全删光了：这次输入结束，关掉输入框
                    if (_buffer.Length == 0) { CancelAll(); return true; }
                    Recompute();
                    return true;
                }
                ClearPredictions();
                UpdateBar();
                return false;
            }

            // Delete：删光标**后面**那个字母
            if (vk == 0x2E)
            {
                if (_buffer.Length > 0)
                {
                    if (_caret < _buffer.Length)
                    {
                        _buffer = _buffer.Remove(_caret, 1);
                        if (_buffer.Length == 0) { CancelAll(); return true; }
                        Recompute();
                    }
                    return true;            // 吃掉，别让它跑到正文里去删字
                }
                return false;
            }

            // 方向键/Home/End：在拼音里挪光标（翻页交给 PgUp/PgDn 和 -/=）
            if (vk == 0x25 || vk == 0x27 || vk == 0x24 || vk == 0x23)
            {
                if (_buffer.Length > 0)
                {
                    if (vk == 0x25) SetCaret(_caret - 1);
                    else if (vk == 0x27) SetCaret(_caret + 1);
                    else if (vk == 0x24) SetCaret(0);
                    else SetCaret(_buffer.Length);
                    UpdateBar();
                    return true;
                }
            }

            bool hasCands = _candidates.Count > 0;

            // 数字选字（主键盘 1-9 和小键盘 1-9）
            int digit = DigitIndex(vk);
            if (digit >= 0 && hasCands)
            {
                List<Candidate> pageItems = CurrentPageItems();
                if (digit < pageItems.Count) { SelectCandidate(pageItems[digit]); return true; }
            }

            // 空格上屏
            if (vk == 0x20)
            {
                if (_buffer.Length > 0)
                {
                    List<Candidate> pageItems = CurrentPageItems();
                    if (pageItems.Count > 0) SelectCandidate(pageItems[Math.Min(_selected, pageItems.Count - 1)]);
                    else CommitRaw();
                    return true;
                }
                if (_predictions.Count > 0)
                {
                    if (_selected < _predictions.Count) { SelectCandidate(_predictions[_selected]); return true; }
                }
                return false;
            }

            // 回车：英文原样上屏
            if (vk == 0x0D)
            {
                if (_buffer.Length > 0) { CommitRaw(); return true; }
                if (_predictions.Count > 0) { ClearPredictions(); UpdateBar(); }
                return false;
            }

            // Tab：选第一个
            if (vk == 0x09)
            {
                List<Candidate> list = _buffer.Length > 0 ? CurrentPageItems() : _predictions;
                if (list.Count > 0) { SelectCandidate(list[0]); return true; }
                return false;
            }

            // 翻页
            if (vk == 0x21 || vk == 0x25 || (!_english && vk == 0xBD)) { if (Page(-1)) { UpdateBar(); return true; } }
            if (vk == 0x22 || vk == 0x27 || (!_english && vk == 0xBB)) { if (Page(1)) { UpdateBar(); return true; } }

            // 字母
            if (KeyboardHook.IsLetter(vk))
            {
                char c = KeyboardHook.LetterOf(vk);
                if (_buffer.Length > 0)
                {
                    // 插在光标处，不是一律追加到末尾
                    string next = _buffer.Substring(0, _caret) + c + _buffer.Substring(_caret);
                    if (_english || CanCompose(next))
                    {
                        _buffer = next;
                        _caret++;
                        Recompute();
                        return true;
                    }
                    // 拼音接不下去了：先把当前候选上屏，再用这个字母开新组合
                    List<Candidate> pageItems = CurrentPageItems();
                    if (pageItems.Count > 0) SelectCandidate(pageItems[Math.Min(_selected, pageItems.Count - 1)], true);
                    else CommitRaw(true);
                }
                ClearPredictions();
                StartBuffer(c.ToString());
                return true;
            }

            if (_predictions.Count > 0) { ClearPredictions(); UpdateBar(); }
            return false;
        }

        static int DigitIndex(int vk)
        {
            if (vk >= 0x31 && vk <= 0x39) return vk - 0x31;    // 主键盘 1-9
            if (vk >= 0x61 && vk <= 0x69) return vk - 0x61;    // 小键盘 1-9
            return -1;
        }

        bool CanCompose(string s)
        {
            Composition c = _engine.Compose(s);
            if (c.Syllables.Count > 0) return true;
            if (c.PartialTail.Length > 0 && _engine.IsSyllablePrefix(c.PartialTail)) return true;
            // 简拼：nh 是 你好 的简拼，中间状态也得算"还能继续敲"
            if (_cfg.Input.Jianpin)
            {
                string letters = PinyinEngine.OnlyLetters(s);
                if (letters.Length > 0 && _lex.HasInitialsPrefix(letters)) return true;
            }
            return false;
        }

        void SetCaret(int pos)
        {
            if (pos < 0) pos = 0;
            if (pos > _buffer.Length) pos = _buffer.Length;
            _caret = pos;
        }

        void StartBuffer(string seed)
        {
            _buffer = seed;
            _caret = _buffer.Length;
            if (Log.MinLevel == LogLevel.Debug) Log.Debug("  起新组合 [" + _buffer + "]");
            Recompute();
        }

        // ==================== 候选择 ====================

        void Recompute()
        {
            _page = 0;
            _selected = 0;
            _predicting = false;
            _candidates.Clear();

            if (_buffer.Length == 0) { UpdateBar(); return; }

            if (_english)
            {
                _candidates.AddRange(_engine.SuggestEnglish(_buffer, Math.Max(1, _cfg.Input.EnglishMaxSuggestions)));
            }
            else
            {
                _comp = _engine.Compose(_buffer);
                _candidates.AddRange(_comp.Candidates);
            }
            UpdateBar();
        }

        void SelectCandidate(Candidate cand, bool keepComposing = false)
        {
            if (cand == null) return;
            string text = cand.Text;
            string code = cand.Code;
            int consumed = cand.Consumed;

            if (!string.IsNullOrEmpty(code) && text.Length > 1)
            {
                // 学一下，下次这个词排更前
                string learnCode = code;
                if (consumed > 1 && _comp != null && _comp.Syllables.Count >= consumed)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < consumed; i++) { if (i > 0) sb.Append(' '); sb.Append(_comp.Syllables[i]); }
                    learnCode = sb.ToString();
                }
                _lex.Learn(learnCode, text);
            }

            // 剩余没上屏的音节继续留着
            string remaining = "";
            if (!_english && _comp != null && consumed > 0 && _comp.Syllables.Count > consumed)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = consumed; i < _comp.Syllables.Count; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(_comp.Syllables[i]);
                }
                remaining = sb.ToString();
            }

            QueueInject(text);
            _selected = 0;
            _page = 0;
            _predicting = false;
            _candidates.Clear();

            if (remaining.Length > 0)
            {
                _buffer = remaining;
                _comp = _engine.Compose(_buffer);
                _candidates.AddRange(_comp.Candidates);
                UpdateBar();
                return;
            }

            _buffer = "";
            _caret = 0;
            _comp = null;

            // 拼音已经全部变成字 -> 这次输入完成了，直接把输入条关掉。
            // （"打完就关"和"上屏后联想"是互斥的，想留联想就把这个开关关掉）
            if (!_english && _cfg.Input.CloseAfterCommit) { CancelAll(); return; }

            if (!_english) ShowPredictions(text);
            else UpdateBar();
        }

        void CommitRaw() { CommitRaw(false); }

        void CommitRaw(bool keepComposing)
        {
            if (_buffer.Length > 0) QueueInject(_buffer);
            _buffer = "";
            _caret = 0;
            _comp = null;
            _candidates.Clear();
            _predicting = false;
            _selected = 0;
            _page = 0;

            // 整串拼音原样上屏了，同样算"这次输入完成"。
            // keepComposing 是"马上还要用后面的字母开新组合"，那种情况不能关。
            if (!keepComposing && !_english && _cfg.Input.CloseAfterCommit) { CancelAll(); return; }
            UpdateBar();
        }

        void CancelAll()
        {
            _buffer = "";
            _caret = 0;
            _comp = null;
            _candidates.Clear();
            _predictions.Clear();
            _predicting = false;
            _selected = 0;
            _page = 0;
            // 清掉"吞键"记录：万一某次 KEYUP 因为切焦点/锁定而没送到，
            // 残留的记录会让这个键以后每次按下都被吞掉（永久失灵）。
            _swallowed.Clear();
            _bar.HideBar();
        }

        void ClearPredictions()
        {
            if (_predictions.Count == 0) return;
            _predictions.Clear();
            if (_predicting) { _candidates.Clear(); _predicting = false; }
        }

        void ShowPredictions(string committed)
        {
            _predictions = _engine.Predict(committed, Math.Max(1, _cfg.Input.PredictCount));
            if (_predictions.Count == 0) { UpdateBar(); return; }
            _predicting = true;
            _candidates.Clear();
            _candidates.AddRange(_predictions);
            _selected = 0;
            _page = 0;
            UpdateBar();
        }

        bool Page(int delta)
        {
            if (_candidates.Count == 0) return false;
            int pages = PageCount();
            if (pages <= 1) return false;
            int np = _page + delta;
            if (np < 0) np = pages - 1;
            if (np >= pages) np = 0;
            _page = np;
            _selected = 0;
            return true;
        }

        int PageCount()
        {
            int size = Math.Max(1, _cfg.Input.PageSize);
            if (_candidates.Count == 0) return 0;
            return (_candidates.Count + size - 1) / size;
        }

        List<Candidate> CurrentPageItems()
        {
            List<Candidate> r = new List<Candidate>();
            if (_predicting) { r.AddRange(_predictions); return r; }
            int size = Math.Max(1, _cfg.Input.PageSize);
            int start = _page * size;
            for (int i = start; i < start + size && i < _candidates.Count; i++) r.Add(_candidates[i]);
            return r;
        }

        void OnBarCandidateClicked(int globalIndex)
        {
            List<Candidate> items = CurrentPageItems();
            if (globalIndex < 0 || globalIndex >= items.Count) return;
            SelectCandidate(items[globalIndex]);
        }

        /// <summary>点了输入条上的拼音：把光标挪到点的那一格，之后就能在那儿插入/删除。</summary>
        void OnBarBufferClicked(int index)
        {
            if (_buffer.Length == 0) return;
            SetCaret(index);
            UpdateBar();
            Log.Info("光标移到拼音第 " + _caret + " 个字后面");
        }

        // ==================== 点了别处就结束输入 ====================

        void ClickWatchTick()
        {
            try
            {
                int st = (int)Native.GetAsyncKeyState(0x01);
                bool down = (st & 0x8000) != 0;          // 现在是不是按着
                bool pressed = (st & 0x0001) != 0;       // 上次查询之后有没有按下过

                // 0x0001 这一位很关键：用户"点一下"往往只按 50~100ms，
                // 120ms 轮询很可能采样时已经松开了 —— 只看 0x8000 会漏掉快速点击。
                // 系统会把"自上次查询以来按下过"记在这一位里。
                if ((down && !_lmbDown) || (!down && pressed)) OnGlobalMouseDown();
                _lmbDown = down;
            }
            catch { }
        }

        void OnGlobalMouseDown()
        {
            // 只有"正在输入"的时候才管这件事
            if (_buffer.Length == 0 && _predictions.Count == 0) return;
            try
            {
                POINT p;
                if (!Native.GetCursorPos(out p)) return;
                // 点在输入条自己身上不算"点了别处" —— 那是在选词、挪光标或者拖条子
                if (_bar.IsPointInside(p.X, p.Y)) return;
                Log.Info("点到输入条以外的区域，结束本次输入");
                CancelAll();
            }
            catch (Exception ex) { Log.Warn("点击检测异常: " + ex.Message); }
        }

        // ==================== 输入条 ====================

        void UpdateBar()
        {
            try
            {
                List<Candidate> items = CurrentPageItems();
                if (items.Count == 0 && _buffer.Length == 0)
                {
                    _bar.HideBar();
                    return;
                }

                List<BarEntry> entries = new List<BarEntry>();
                for (int i = 0; i < items.Count; i++)
                {
                    BarEntry e = new BarEntry();
                    e.Index = (i + 1).ToString();
                    e.Text = items[i].Text;
                    e.Note = _predicting ? "联想" : items[i].Note;
                    entries.Add(e);
                }

                string bufferText;
                if (_buffer.Length > 0)
                {
                    bufferText = _english ? _buffer : (_comp != null ? _comp.SyllableText : _buffer);
                    if (!_cfg.Input.ShowPinyinInBar) bufferText = "";
                }
                else bufferText = "";

                string badge = _english ? "英" : "中";
                _bar.SetContent(badge, bufferText, entries, _selected, _page, PageCount(),
                                _cfg.Input.ShowPinyinInBar || _english, _caret);
                _bar.ShowNoActivate();
            }
            catch (Exception ex)
            {
                Log.Error("刷新输入条失败", ex);
            }
        }

        // ==================== 上屏队列 ====================

        /// <summary>把文字排进上屏队列。真正的 SendInput 在消息循环里做，不在钩子回调里做。</summary>
        void QueueInject(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_injectGate) { _injectQueue.Add(text); }
            try { Native.PostMessage(_msg.Handle, Native.WM_APP_FLUSH, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception ex) { Log.Error("投递上屏消息失败", ex); }
        }

        void FlushInject()
        {
            string all;
            lock (_injectGate)
            {
                if (_injectQueue.Count == 0) return;
                all = string.Concat(_injectQueue.ToArray());
                _injectQueue.Clear();
            }
            if (all.Length == 0) return;
            Log.Debug("上屏: " + all);
            TextInjector.SendText(all);
        }

        /// <summary>给 AI 面板用：把文字打进当前焦点程序。</summary>
        public void InjectExternal(string text) { QueueInject(text); }

        /// <summary>给 AI 助手弹个气泡提示。</summary>
        public void Notify(string title, string text)
        {
            try { _tray.Balloon(title, text); }
            catch (Exception ex) { Log.Warn("气泡提示失败: " + ex.Message); }
        }

        void OpenLogFolder()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Log.CurrentPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Log.Error("打开日志目录失败", ex); }
        }

        public void Dispose()
        {
            try { _watch.Stop(); _watch.Dispose(); } catch { }
            try { _clickWatch.Stop(); _clickWatch.Dispose(); } catch { }
            try { _hook.Dispose(); } catch { }
            try { _hotkeys.Dispose(); } catch { }
            try { _tray.Dispose(); } catch { }
            try { _msg.MessageReceived -= OnWindowMessage; } catch { }
        }
    }
}
