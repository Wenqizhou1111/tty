using System;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using MyIme.Ai;
using MyIme.Shared;

namespace MyIme.Core
{
    /// <summary>
    /// AI 写作助手的调度层。
    ///
    /// 流程（顺序很重要）：
    ///   1. 按下热键时**先记下前台窗口**，再读它的文字 —— 因为面板一弹出来就会抢焦点
    ///   2. 弹面板，用户选风格或写要求
    ///   3. 请求 AI（后台线程，流式回显）
    ///   4. 用户点"替换原文"：把面板藏起来，焦点还给原来那个窗口，再动它的文字
    /// </summary>
    public sealed class AiAssistant : IDisposable
    {
        readonly ImeController _ime;
        AppSettings _cfg;

        AiPanel _panel;
        AiClient _client;
        IntPtr _prevHwnd = IntPtr.Zero;
        string _sourceText = "";
        string _sourceHow = "";
        volatile bool _busy;

        public AiAssistant(ImeController ime, AppSettings cfg)
        {
            _ime = ime;
            _cfg = cfg;
        }

        public void ApplySettings(AppSettings cfg) { _cfg = cfg; }

        public bool Busy { get { return _busy; } }

        /// <summary>自动读取时能接受的最大字数。超过就判定"读到的不是输入框"。</summary>
        public const int MaxAutoReadChars = 5000;

        /// <summary>"替换原文"能接受的最大选中字数，超过一律拒绝。</summary>
        public const int MaxReplaceChars = 5000;

        // ==================== 入口 ====================

        /// <summary>commandMode=true 打开"输入要求改写"，false 打开"自动读取修改"。</summary>
        public void ShowPanel(bool commandMode)
        {
            try
            {
                if (_cfg == null || !_cfg.Ai.Enabled)
                {
                    Log.Info("AI 未启用，忽略 AI 面板请求");
                    return;
                }

                // 1) 先抓住前台窗口
                _prevHwnd = Native.GetForegroundWindow();

                // 2) 再读文字（此时焦点还在目标程序上）
                _sourceText = "";
                _sourceHow = "";
                if (_cfg.Ai.AutoReadMode)
                {
                    // 先用 TextCapture（UIA 优先、剪贴板兜底，回落链更完整），
                    // 它拿不到再用本类内置的读取逻辑兜一次。
                    try
                    {
                        string t, how;
                        if (TextCapture.TryReadFocusedText(out t, out how) && !string.IsNullOrEmpty(t))
                        {
                            _sourceText = t;
                            _sourceHow = "TextCapture/" + how;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("TextCapture 读取失败，改用内置回落: " + ex.Message);
                    }

                    if (string.IsNullOrEmpty(_sourceText))
                        _sourceText = ReadFocusedText(out _sourceHow);

                    Log.Info("AI 读取到 " + _sourceText.Length + " 字，来源=" + _sourceHow);

                    // 长度兜底：正常的输入框不可能有几千字。读到这么长，基本可以断定是
                    // TextCapture 退到了 TextPattern.DocumentRange.GetText(-1)，
                    // 把整个网页 / 文档正文当成"输入框内容"读了回来。
                    // 这种内容绝不能拿去做"替换原文" —— Ctrl+A 会把整篇选中再被结果盖掉。
                    int readLen = _sourceText.Length;
                    if (readLen > MaxAutoReadChars)
                    {
                        Log.Warn("AI 读到 " + readLen + " 字，超过上限 " + MaxAutoReadChars
                                 + "，判定为不是输入框，已忽略。来源=" + _sourceHow);
                        _sourceText = "";
                        _sourceHow = "读到 " + readLen + " 字（超上限，已忽略）";
                        Balloon("没有找到输入框",
                                "读到的内容有 " + readLen + " 字，不像输入框里的文字，已忽略以免误改。\r\n"
                                + "把光标点进输入框再试一次，或者改用「输入要求改写」模式。");
                    }
                }

                EnsurePanel();
                _panel.Prepare(commandMode, _sourceText, _cfg.Ai, _cfg.Ui);
                _panel.Show();
                _panel.BringToFront();
                try { _panel.Activate(); } catch { }
            }
            catch (Exception ex)
            {
                Log.Error("打开 AI 面板失败", ex);
                Balloon("AI 面板打开失败", ex.Message);
            }
        }

        void EnsurePanel()
        {
            if (_panel != null && !_panel.IsDisposed) return;
            _panel = new AiPanel();
            _panel.RunRequested += OnRunRequested;
            _panel.ApplyRequested += OnApplyRequested;
            _panel.PanelClosedClicked += delegate { HidePanel(); };
            _panel.FormClosed += delegate { _panel = null; };
        }

        public void HidePanel()
        {
            try
            {
                if (_busy && _client != null) _client.Cancel();
                if (_panel != null && !_panel.IsDisposed) _panel.Hide();
            }
            catch (Exception ex) { Log.Warn("关闭 AI 面板失败: " + ex.Message); }
        }

        // ==================== 读取焦点输入框 ====================

        /// <summary>先试 UI Automation，再退回剪贴板。</summary>
        public string ReadFocusedText(out string how)
        {
            how = "";
            try
            {
                string t = ReadViaUia();
                if (!string.IsNullOrEmpty(t)) { how = "UI Automation"; return t; }
            }
            catch (Exception ex) { Log.Debug("UIA 读取失败: " + ex.Message); }

            try
            {
                string t = ReadViaClipboard(out how);
                if (t != null) return t;
            }
            catch (Exception ex) { Log.Debug("剪贴板读取失败: " + ex.Message); }

            how = "未读到";
            return "";
        }

        /// <summary>
        /// UIA 在个别程序上会卡住很久，所以放到子线程里跑并设超时，
        /// 不能让输入法的主线程被它拖住。
        /// </summary>
        string ReadViaUia()
        {
            string result = null;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    AutomationElement fe = AutomationElement.FocusedElement;
                    if (fe == null) return;
                    object pattern;
                    if (fe.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                    {
                        ValuePattern vp = (ValuePattern)pattern;
                        result = vp.Current.Value;
                        return;
                    }
                    if (fe.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                    {
                        TextPattern tp = (TextPattern)pattern;
                        result = tp.DocumentRange.GetText(-1);
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "AiUiaRead";
            t.Start();
            if (!t.Join(1200)) return null;
            return result;
        }

        /// <summary>剪贴板兜底：备份 -> Ctrl+A -> Ctrl+C -> 读 -> 还原。</summary>
        string ReadViaClipboard(out string how)
        {
            how = "剪贴板";
            string backup = null;
            bool hadBackup = false;
            try
            {
                if (Clipboard.ContainsText())
                {
                    backup = Clipboard.GetText();
                    hadBackup = true;
                }
            }
            catch { }

            try
            {
                Clipboard.Clear();
                TextInjector.SendSelectAll();
                TextInjector.Settle(90);
                TextInjector.SendCopy();
                TextInjector.Settle(140);

                string text = null;
                for (int i = 0; i < 6 && text == null; i++)
                {
                    try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
                    catch { }
                    if (text == null) TextInjector.Settle(60);
                }
                return text;
            }
            finally
            {
                if (hadBackup)
                {
                    try { Clipboard.SetText(backup); } catch { }
                }
            }
        }

        // ==================== 写回 ====================

        void OnApplyRequested(bool replace)
        {
            string text = _panel == null ? "" : _panel.ResultText;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                if (_panel != null) _panel.Hide();
                TextInjector.Settle(60);

                // 焦点还给原来那个窗口，否则会打进我们自己的面板里
                if (_prevHwnd != IntPtr.Zero)
                {
                    Native.SetForegroundWindow(_prevHwnd);
                    TextInjector.Settle(200);
                }

                if (replace) ReplaceFocusedText(text);
                else InsertFocusedText(text);
            }
            catch (Exception ex)
            {
                Log.Error("写回文字失败", ex);
                Balloon("写回失败", ex.Message);
            }
        }

        void InsertFocusedText(string text)
        {
            TextInjector.SendText(text);
            Log.Info("AI 结果已插入 " + text.Length + " 字");
        }

        /// <summary>
        /// 替换原文。做之前先"验一下选区"，防止在浏览器里把整个网页选中后覆盖掉。
        /// 只有全选出来的内容和当初读到的内容基本一致，才敢往下写。
        /// </summary>
        void ReplaceFocusedText(string text)
        {
            string backup = null;
            bool hadBackup = false;
            try
            {
                if (Clipboard.ContainsText()) { backup = Clipboard.GetText(); hadBackup = true; }
            }
            catch { }

            bool restored = false;
            try
            {
                Clipboard.Clear();
                TextInjector.SendSelectAll();
                TextInjector.Settle(90);
                TextInjector.SendCopy();
                TextInjector.Settle(160);

                string selected = null;
                for (int i = 0; i < 6 && string.IsNullOrEmpty(selected); i++)
                {
                    try { if (Clipboard.ContainsText()) selected = Clipboard.GetText(); }
                    catch { }
                    if (string.IsNullOrEmpty(selected)) TextInjector.Settle(60);
                }

                if (string.IsNullOrEmpty(selected))
                {
                    Balloon("没能替换", "读不到当前选中的内容，已放弃替换（可以改用“插入”）。");
                    return;
                }

                // 硬上限：不论当初读到什么，选中内容过长一律不替换。
                // 这是防"读回整个网页 -> Ctrl+A 全选 -> 被 AI 结果覆盖"的最后一道闸。
                if (selected.Length > MaxReplaceChars)
                {
                    Log.Warn("替换已中止：选中 " + selected.Length + " 字，超过上限 " + MaxReplaceChars);
                    Balloon("替换已中止", "选中了 " + selected.Length + " 字，远超输入框的正常长度，"
                            + "为避免误删已放弃（可以改用“插入”）。");
                    return;
                }

                // 安全检查：选中的内容应该和当初读到的原文差不多
                if (!string.IsNullOrEmpty(_sourceText))
                {
                    string a = Normalize(selected);
                    string b = Normalize(_sourceText);
                    bool ok = a == b
                              || (b.Length > 0 && a.Length > 0
                                  && (a.IndexOf(b, StringComparison.Ordinal) >= 0
                                      || b.IndexOf(a, StringComparison.Ordinal) >= 0));
                    if (!ok)
                    {
                        Log.Warn("替换中止：选区内容与原文不一致。选区 " + a.Length + " 字，原文 " + b.Length + " 字");
                        Balloon("替换已中止", "选中的内容和你当初编辑的不是同一段，为避免误删已放弃。可以改用“插入”。");
                        return;
                    }
                }

                // 选区已经选好了，直接打字覆盖它
                TextInjector.SendText(text);
                restored = true;
                Log.Info("AI 结果已替换原文 " + text.Length + " 字");
            }
            finally
            {
                if (hadBackup)
                {
                    try { Clipboard.SetText(backup); } catch { }
                }
            }
            if (!restored) Log.Warn("替换流程未完成");
        }

        static string Normalize(string s)
        {
            if (s == null) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        // ==================== 请求 AI ====================

        void OnRunRequested(string mode, string id, string requirement)
        {
            if (_busy) return;
            if (_panel == null) return;

            if (string.IsNullOrEmpty(_sourceText) && string.IsNullOrEmpty(requirement))
            {
                _panel.SetStatus("没有可处理的文字：先在任意输入框里打几个字，或直接写一句要求", true);
                return;
            }

            string apiKey = SecretStore.GetApiKey(_cfg.Ai);
            if (string.IsNullOrEmpty(apiKey) && !IsLocalProvider(_cfg.Ai.BaseUrl))
            {
                _panel.SetStatus("还没填 API Key，请到设置里填（本地模型可以留空）", true);
                return;
            }

            string system = PromptLibrary.BuildSystemPrompt(_cfg.Ai);
            string user = (mode == AiPanel.ModeAuto)
                ? PromptLibrary.BuildAutoPrompt(id, _sourceText)
                : PromptLibrary.BuildCommandPrompt(id, requirement, _sourceText);

            AiSettings snapshot = Json.Clone(_cfg.Ai);
            AiClient client = new AiClient(snapshot, apiKey);
            _client = client;
            _busy = true;
            _panel.BeginResult();

            string note = AiClient.DescribeSettings(snapshot, !string.IsNullOrEmpty(apiKey));
            bool stream = snapshot.Stream;

            Thread worker = new Thread(delegate ()
            {
                AiResult r;
                try
                {
                    if (stream)
                    {
                        r = client.CompleteStreaming(system, user, delegate (string acc)
                        {
                            Post(delegate { if (_panel != null) _panel.AppendResult(acc); });
                        });
                    }
                    else
                    {
                        r = client.Complete(system, user);
                        if (r != null && r.Ok) Post(delegate { if (_panel != null) _panel.AppendResult(r.Text); });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("AI 请求线程异常", ex);
                    r = AiResult.Fail("请求异常：" + ex.Message);
                }
                AiResult final = r;
                Post(delegate
                {
                    _busy = false;
                    if (_panel != null) _panel.EndResult(final, note);
                });
            });
            worker.IsBackground = true;
            worker.Name = "AiRequest";
            worker.Start();
        }

        static bool IsLocalProvider(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.ToLowerInvariant();
            return u.IndexOf("localhost", StringComparison.Ordinal) >= 0
                   || u.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0
                   || u.IndexOf("0.0.0.0", StringComparison.Ordinal) >= 0;
        }

        void Post(MethodInvoker action)
        {
            try
            {
                if (_panel == null || _panel.IsDisposed) return;
                if (_panel.InvokeRequired) _panel.BeginInvoke(action);
                else action();
            }
            catch (Exception ex) { Log.Debug("回主线程失败: " + ex.Message); }
        }

        void Balloon(string title, string text)
        {
            try
            {
                if (_ime != null) _ime.Notify(title, text);
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_client != null) _client.Cancel();
                if (_panel != null && !_panel.IsDisposed) _panel.Dispose();
            }
            catch { }
        }
    }
}
