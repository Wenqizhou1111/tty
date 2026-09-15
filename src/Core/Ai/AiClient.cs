using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MyIme.Shared;

namespace MyIme.Ai
{
    /// <summary>一次 AI 调用的结果。永远不抛异常，失败时看 Ok / Error。</summary>
    public class AiResult
    {
        /// <summary>成功为 true。</summary>
        public bool Ok;
        /// <summary>成功时是结果正文。</summary>
        public string Text = "";
        /// <summary>失败时是给人看的中文说明。</summary>
        public string Error = "";
        /// <summary>HTTP 状态码，0 表示没走到 HTTP（本地校验失败 / 连不上 / 超时）。</summary>
        public int StatusCode;
        /// <summary>原始响应（排错用，已经过打码和截断）。</summary>
        public string RawBody = "";

        public static AiResult Success(string text)
        {
            AiResult r = new AiResult();
            r.Ok = true;
            r.Text = text == null ? "" : text;
            return r;
        }

        public static AiResult Fail(string message)
        {
            AiResult r = new AiResult();
            r.Ok = false;
            r.Error = message == null ? "未知错误" : message;
            return r;
        }

        public override string ToString()
        {
            if (Ok) return "OK(" + (Text == null ? 0 : Text.Length) + " 字)";
            return "失败: " + Error;
        }
    }

    /// <summary>流式回调：每收到一段就把"累积到目前的全文"传进来。</summary>
    public delegate void AiDeltaHandler(string accumulatedText);

    /// <summary>
    /// 各种 AI 接口的统一客户端。支持三类协议：
    ///   openai    —— /chat/completions（OpenAI、DeepSeek、Kimi、GLM、千问、豆包、Ollama…）
    ///   anthropic —— /messages（x-api-key + anthropic-version）
    ///   custom    —— 请求体模板自己写，占位符 model / system / user / temperature / max_tokens / stream
    ///
    /// 所有公开方法都是同步阻塞的（调用方放到后台线程跑即可），
    /// 并且保证不往外抛异常：任何失败都变成 AiResult.Error 里一句人话。
    /// </summary>
    public class AiClient
    {
        public const string DefaultCustomTemplate = AiProviderPresets.DefaultCustomTemplate;
        public const string AnthropicVersion = "2023-06-01";

        static readonly HttpClient Http = CreateHttp();

        readonly object _gate = new object();
        AiSettings _settings;
        string _apiKey;
        CancellationTokenSource _cts;
        volatile bool _userCancelled;

        public AiClient(AiSettings settings, string apiKey)
        {
            _settings = settings == null ? new AiSettings() : settings;
            _apiKey = apiKey == null ? "" : apiKey.Trim();
        }

        /// <summary>当前使用的设置（只读引用）。</summary>
        public AiSettings Settings
        {
            get { return _settings; }
        }

        static HttpClient CreateHttp()
        {
            HttpClient c = new HttpClient();
            // 超时全部走 CancellationTokenSource，这样 Cancel() 才能立刻中断
            c.Timeout = TimeSpan.FromMilliseconds(-1);
            return c;
        }

        // ==================== 对外接口 ====================

        /// <summary>普通（非流式）请求。阻塞直到拿到完整结果。</summary>
        public AiResult Complete(string systemPrompt, string userPrompt)
        {
            try
            {
                return Run(systemPrompt, userPrompt, false, null);
            }
            catch (Exception ex)
            {
                Log.Error("AI 请求异常", ex);
                return AiResult.Fail("请求出错：" + Sanitize(RootMessage(ex), _apiKey));
            }
        }

        /// <summary>流式请求。每收到一段就回调 onDelta（传累积全文），结束后同样返回完整结果。</summary>
        public AiResult CompleteStreaming(string systemPrompt, string userPrompt, AiDeltaHandler onDelta)
        {
            try
            {
                return Run(systemPrompt, userPrompt, true, onDelta);
            }
            catch (Exception ex)
            {
                Log.Error("AI 流式请求异常", ex);
                return AiResult.Fail("请求出错：" + Sanitize(RootMessage(ex), _apiKey));
            }
        }

        /// <summary>从别的线程中断正在进行的请求（不会抛异常）。</summary>
        public void Cancel()
        {
            try
            {
                CancellationTokenSource cts;
                lock (_gate)
                {
                    _userCancelled = true;
                    cts = _cts;
                }
                if (cts != null) cts.Cancel();
                Log.Info("AI 请求已被用户取消");
            }
            catch (Exception ex)
            {
                Log.Warn("取消 AI 请求失败: " + ex.Message);
            }
        }

        /// <summary>设置摘要，给设置界面显示用。绝不包含 API Key 内容。</summary>
        public static string DescribeSettings(AiSettings s, bool hasKey)
        {
            try
            {
                if (s == null) return "AI 设置为空";
                StringBuilder sb = new StringBuilder();
                sb.Append("协议：").Append(ProviderDisplayName(s.Provider)).Append('\n');
                sb.Append("接口地址：").Append(string.IsNullOrEmpty(s.BaseUrl) ? "（未填）" : MaskUrl(s.BaseUrl.Trim())).Append('\n');
                sb.Append("模型：").Append(string.IsNullOrEmpty(s.Model) ? "（未填）" : s.Model.Trim()).Append('\n');
                sb.Append("温度：").Append(s.Temperature.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append("　最大长度：").Append(s.MaxTokens.ToString(CultureInfo.InvariantCulture)).Append(" token").Append('\n');
                sb.Append("超时：").Append(ClampTimeout(s.TimeoutSeconds).ToString(CultureInfo.InvariantCulture)).Append(" 秒");
                sb.Append("　流式：").Append(s.Stream ? "开" : "关").Append('\n');
                sb.Append("API Key：").Append(hasKey ? "已设置" : "未设置");
                if (s.ExtraHeaders != null && s.ExtraHeaders.Count > 0)
                    sb.Append('\n').Append("附加请求头：").Append(s.ExtraHeaders.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个");
                if (!string.IsNullOrEmpty(s.ResponsePath))
                    sb.Append('\n').Append("结果字段路径：").Append(s.ResponsePath.Trim());
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "设置摘要生成失败：" + ex.Message;
            }
        }

        public static string ProviderDisplayName(string provider)
        {
            string p = NormalizeProvider(provider);
            if (p == "anthropic") return "Anthropic 原生（/messages）";
            if (p == "custom") return "自定义请求体模板";
            return "OpenAI 兼容（/chat/completions）";
        }

        // ==================== 主流程 ====================

        AiResult Run(string systemPrompt, string userPrompt, bool stream, AiDeltaHandler onDelta)
        {
            AiResult r = new AiResult();
            string url = "";
            int timeoutSeconds = 40;
            try
            {
                if (_settings == null)
                {
                    r.Error = "AI 设置为空，请先在设置里配置 AI 接口";
                    return r;
                }

                string provider = NormalizeProvider(_settings.Provider);
                string baseUrl = _settings.BaseUrl == null ? "" : _settings.BaseUrl.Trim();
                string model = _settings.Model == null ? "" : _settings.Model.Trim();

                if (baseUrl.Length == 0)
                {
                    r.Error = "还没有填 API 地址，请到「设置 → AI」里选一个服务商，或手工填写接口地址";
                    return r;
                }
                if (model.Length == 0 && provider != "custom")
                {
                    r.Error = "还没有填模型名，请到「设置 → AI」里填写（例如 deepseek-chat）";
                    return r;
                }

                timeoutSeconds = ClampTimeout(_settings.TimeoutSeconds);
                url = BuildUrl(baseUrl, provider);
                string sys = string.IsNullOrEmpty(systemPrompt) ? PromptLibrary.DefaultSystemPrompt : systemPrompt;
                string usr = userPrompt == null ? "" : userPrompt;

                string bodyJson = BuildBody(provider, sys, usr, stream);

                Log.Info("AI 请求 => " + ProviderDisplayName(provider) + " " + MaskUrl(url) +
                         " model=" + (model.Length == 0 ? "(沿用模板)" : model) +
                         " key=" + MaskKey(_apiKey) + " stream=" + stream +
                         " 输入 " + usr.Length + " 字");

                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    lock (_gate)
                    {
                        _cts = cts;
                        _userCancelled = false;
                    }
                    try
                    {
                        cts.CancelAfter(timeoutSeconds * 1000);
                        SendAndRead(r, url, provider, bodyJson, stream, onDelta, timeoutSeconds, cts);
                    }
                    catch (OperationCanceledException)
                    {
                        if (_userCancelled) r.Error = "已取消";
                        else r.Error = "请求超时（已超过 " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " 秒）";
                        Log.Warn("AI 请求被取消/超时: " + r.Error);
                    }
                    catch (HttpRequestException ex)
                    {
                        r.Error = "连不上服务器（" + HostOf(url) + "）：" + Sanitize(RootMessage(ex), _apiKey);
                        Log.Error("AI 请求连不上", ex);
                    }
                    catch (WebException ex)
                    {
                        r.Error = "连不上服务器（" + HostOf(url) + "）：" + Sanitize(RootMessage(ex), _apiKey);
                        Log.Error("AI 请求网络错误", ex);
                    }
                    catch (Exception ex)
                    {
                        r.Error = "请求出错：" + Sanitize(RootMessage(ex), _apiKey);
                        Log.Error("AI 请求失败", ex);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (_cts == cts) _cts = null;
                        }
                    }
                }
                return r;
            }
            catch (Exception ex)
            {
                Log.Error("AI 请求准备阶段失败", ex);
                r.Ok = false;
                r.Error = "请求出错：" + Sanitize(RootMessage(ex), _apiKey);
                return r;
            }
        }

        void SendAndRead(AiResult r, string url, string provider, string bodyJson, bool stream,
                         AiDeltaHandler onDelta, int timeoutSeconds, CancellationTokenSource cts)
        {
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(bodyJson, new UTF8Encoding(false), "application/json");
                ApplyHeaders(req, provider);

                HttpResponseMessage resp;
                if (stream)
                    resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();
                else
                    resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();

                using (resp)
                {
                    r.StatusCode = (int)resp.StatusCode;
                    bool isStream = stream && IsEventStream(resp);

                    if (!resp.IsSuccessStatusCode)
                    {
                        string errBody = ReadBodySafe(resp);
                        r.RawBody = Truncate(Sanitize(errBody, _apiKey), 4000);
                        r.Error = DescribeHttpError((int)resp.StatusCode, errBody, url, _apiKey);
                        Log.Warn("AI 接口返回错误: " + r.Error);
                        return;
                    }

                    if (isStream)
                    {
                        ReadStream(resp, provider, r, onDelta, timeoutSeconds, cts);
                    }
                    else
                    {
                        string bodyText = ReadBodySafe(resp);
                        r.RawBody = Truncate(Sanitize(bodyText, _apiKey), 4000);
                        object json = Json.TryParse(bodyText);
                        string text = ExtractText(json, _settings.ResponsePath);
                        if (stream && onDelta != null && !string.IsNullOrEmpty(text)) SafeDelta(onDelta, text);
                        FillResult(r, text, bodyText);
                    }
                }
            }
        }

        // ==================== 流式解析 ====================

        void ReadStream(HttpResponseMessage resp, string provider, AiResult r, AiDeltaHandler onDelta,
                        int timeoutSeconds, CancellationTokenSource cts)
        {
            Stream st = null;
            StreamReader sr = null;
            StringBuilder acc = new StringBuilder();
            StringBuilder raw = new StringBuilder();
            bool sawData = false;
            bool timedOut = false;
            string readError = null;

            try
            {
                st = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                sr = new StreamReader(st, Encoding.UTF8, true);
                int idleMs = timeoutSeconds * 1000;

                while (true)
                {
                    if (cts.IsCancellationRequested) break;

                    string line;
                    string err;
                    int rc = ReadLineTimed(sr, idleMs, out line, out err);
                    if (rc == 2) { timedOut = true; break; }
                    if (rc == 3) { readError = err; break; }
                    if (rc == 1) break; // 流结束

                    if (!sawData && raw.Length < 1024 * 1024) raw.Append(line).Append('\n');

                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    if (t[0] == ':') continue;                 // 心跳注释
                    if (!t.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        // 有的网关不带 data: 前缀，直接一行一个 JSON
                        if (t[0] != '{' && t[0] != '[') continue;
                    }
                    else
                    {
                        t = t.Substring(5).Trim();
                    }
                    if (t.Length == 0) continue;
                    if (t == "[DONE]") break;

                    object json = Json.TryParse(t);
                    if (json == null) continue;
                    sawData = true;
                    raw.Length = 0;

                    string apiErr = JsonErrorText(json);
                    if (!string.IsNullOrEmpty(apiErr))
                    {
                        r.Error = "接口返回错误：" + Sanitize(apiErr, _apiKey);
                        Log.Warn("AI 流式响应里带错误: " + r.Error);
                        break;
                    }

                    string delta = ExtractDelta(json, provider == "anthropic");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        acc.Append(delta);
                        SafeDelta(onDelta, acc.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                readError = RootMessage(ex);
                Log.Error("读取 AI 流式响应失败", ex);
            }
            finally
            {
                try { if (sr != null) sr.Dispose(); } catch { }
                try { if (st != null) st.Dispose(); } catch { }
            }

            string text = CleanText(acc.ToString());

            // 服务端没按 SSE 回、而是直接回了整段 JSON 的情况
            if (text.Length == 0 && !sawData && raw.Length > 0)
            {
                object json = Json.TryParse(raw.ToString());
                string whole = ExtractText(json, _settings.ResponsePath);
                if (!string.IsNullOrEmpty(whole))
                {
                    text = CleanText(whole);
                    if (onDelta != null) SafeDelta(onDelta, text);
                }
            }

            if (text.Length > 0)
            {
                if (r.Error != null && r.Error.Length > 0) Log.Warn("AI 流式响应对应错误但已有正文，按正文返回");
                r.Ok = true;
                r.Text = text;
                r.Error = "";
                return;
            }

            if (timedOut)
            {
                r.Error = "读取响应超时（已超过 " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " 秒没有收到新数据）";
                return;
            }
            if (!string.IsNullOrEmpty(readError))
            {
                r.Error = "读取响应失败：" + Sanitize(readError, _apiKey);
                return;
            }
            if (string.IsNullOrEmpty(r.Error))
                r.Error = "接口没有返回任何正文（可能被内容过滤，或模型只回了空内容）";
        }

        /// <summary>带回读超时地读一行：0=读到，1=流结束，2=超时，3=出错。</summary>
        static int ReadLineTimed(StreamReader sr, int timeoutMs, out string line, out string error)
        {
            line = null;
            error = null;
            Task<string> t;
            try
            {
                t = sr.ReadLineAsync();
            }
            catch (Exception ex)
            {
                error = RootMessage(ex);
                return 3;
            }
            if (t == null) return 1;

            bool done;
            try
            {
                done = t.Wait(timeoutMs);
            }
            catch (AggregateException)
            {
                done = true;
            }
            if (!done)
            {
                // 放弃这一行（底层流随后会被 Dispose 掉），并观察异常避免未处理任务异常
                Observe(t);
                return 2;
            }
            if (t.IsFaulted)
            {
                Exception e = t.Exception;
                error = RootMessage(e);
                return 3;
            }
            if (t.IsCanceled) return 1;
            line = t.Result;
            if (line == null) return 1;
            return 0;
        }

        static void Observe(Task<string> t)
        {
            try
            {
                t.ContinueWith(delegate(Task<string> x) { Exception ignored = x.Exception; });
            }
            catch { }
        }

        static string ExtractDelta(object json, bool anthropic)
        {
            if (json == null) return null;
            string type = SelectString(json, "type");

            if (anthropic)
            {
                if (type == "content_block_delta")
                {
                    string d = SelectString(json, "delta.text");
                    if (d != null) return d;
                }
                if (type == "content_block_start") return null;
                if (type == "message_start") return null;
                if (type == "message_delta") return null;
                if (type == "message_stop") return null;
                if (type == "ping") return null;
                // 兼容网关可能不带 type
                return SelectString(json, "delta.text");
            }

            string c = SelectString(json, "choices[0].delta.content");
            if (c != null) return c;
            c = SelectString(json, "choices[0].message.content");
            if (c != null) return c;
            c = SelectString(json, "choices[0].text");
            if (c != null) return c;
            c = SelectString(json, "delta.text");
            if (c != null) return c;
            return null;
        }

        // ==================== 非流式正文提取 ====================

        static readonly string[] AutoPaths = new string[]
        {
            "choices[0].message.content",
            "choices[0].text",
            "content[0].text",
            "output.text",
            "data.message.content",
            "data.choices[0].message.content",
            "result",
            "message.content",
            "text",
            "response"
        };

        static string ExtractText(object root, string configuredPath)
        {
            if (root == null) return null;

            if (!string.IsNullOrEmpty(configuredPath) && configuredPath.Trim().Length > 0)
            {
                string v = SelectString(root, configuredPath.Trim());
                if (v != null) return v;
                // 指定了路径但没取到，继续自动嗅探，尽量不让用户白等
            }

            for (int i = 0; i < AutoPaths.Length; i++)
            {
                string v = SelectString(root, AutoPaths[i]);
                if (v != null && v.Trim().Length > 0) return v;
            }
            // 响应本身就是一段文字
            if (root is string) return (string)root;
            return null;
        }

        void FillResult(AiResult r, string text, string rawBody)
        {
            string clean = CleanText(text);
            if (clean.Length == 0)
            {
                r.Ok = false;
                r.Error = "接口返回成功，但没有找到正文；可在「设置 → AI → 结果字段路径」里手工指定字段";
                if (rawBody != null && rawBody.Trim().Length > 0)
                    r.Error = r.Error + "（原始响应：" + Truncate(Sanitize(StripWhitespace(rawBody), _apiKey), 200) + "）";
                return;
            }
            r.Ok = true;
            r.Text = clean;
            r.Error = "";
        }

        /// <summary>去掉模型爱加的 markdown 围栏和包裹引号，并修剪首尾空白。</summary>
        public static string CleanText(string s)
        {
            if (s == null) return "";
            string t = s.Trim();
            if (t.Length == 0) return "";

            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                int nl = t.IndexOf('\n');
                if (nl >= 0)
                {
                    string rest = t.Substring(nl + 1);
                    int close = rest.LastIndexOf("```", StringComparison.Ordinal);
                    if (close >= 0) rest = rest.Substring(0, close);
                    t = rest.Trim();
                }
            }

            t = StripWrappingQuotes(t);
            return t.Trim();
        }

        static string StripWrappingQuotes(string t)
        {
            if (t.Length < 3) return t;
            char a = t[0];
            char b = t[t.Length - 1];
            bool pair = (a == '"' && b == '"') || (a == '\u201C' && b == '\u201D') || (a == '\u300C' && b == '\u300D');
            if (!pair) return t;
            string inner = t.Substring(1, t.Length - 2);
            if (inner.IndexOf('"') >= 0) return t;
            if (inner.IndexOf('\u201C') >= 0) return t;
            if (inner.IndexOf('\u300C') >= 0) return t;
            return inner.Trim();
        }

        // ==================== 请求组装 ====================

        string BuildBody(string provider, string systemPrompt, string userPrompt, bool stream)
        {
            if (provider == "custom") return BuildCustomBody(systemPrompt, userPrompt, stream);

            string model = _settings.Model == null ? "" : _settings.Model.Trim();
            int maxTokens = _settings.MaxTokens;
            if (maxTokens < 1) maxTokens = 1;
            if (maxTokens > 1000000) maxTokens = 1000000;

            List<object> messages = new List<object>();
            Dictionary<string, object> sysMsg = new Dictionary<string, object>();
            sysMsg["role"] = "system";
            sysMsg["content"] = systemPrompt;
            Dictionary<string, object> userMsg = new Dictionary<string, object>();
            userMsg["role"] = "user";
            userMsg["content"] = userPrompt;

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;

            if (provider == "anthropic")
            {
                // Anthropic：system 是顶层字段，messages 里只有 user
                messages.Add(userMsg);
                body["max_tokens"] = maxTokens;
                body["system"] = systemPrompt;
                body["messages"] = messages;
                double at = _settings.Temperature;
                if (at < 0) at = 0;
                if (at > 1) at = 1;     // Anthropic 的 temperature 只能 0~1
                body["temperature"] = at;
            }
            else
            {
                messages.Add(sysMsg);
                messages.Add(userMsg);
                body["messages"] = messages;
                double t = _settings.Temperature;
                if (t < 0) t = 0;
                if (t > 2) t = 2;
                body["temperature"] = t;
                body["max_tokens"] = maxTokens;
            }
            body["stream"] = stream;
            return Json.Stringify(body);
        }

        string BuildCustomBody(string systemPrompt, string userPrompt, bool stream)
        {
            string tpl = _settings.CustomBodyTemplate;
            if (tpl == null || tpl.Trim().Length == 0) tpl = DefaultCustomTemplate;

            // 字符串占位符连同引号一起替换，值本身做 JSON 转义
            tpl = ReplacePlaceholder(tpl, "model", Json.Stringify(_settings.Model == null ? "" : _settings.Model.Trim()));
            tpl = ReplacePlaceholder(tpl, "system", Json.Stringify(systemPrompt == null ? "" : systemPrompt));
            tpl = ReplacePlaceholder(tpl, "user", Json.Stringify(userPrompt == null ? "" : userPrompt));

            int maxTokens = _settings.MaxTokens < 1 ? 1 : _settings.MaxTokens;
            tpl = ReplacePlaceholder(tpl, "temperature", _settings.Temperature.ToString("0.###", CultureInfo.InvariantCulture));
            tpl = ReplacePlaceholder(tpl, "max_tokens", maxTokens.ToString(CultureInfo.InvariantCulture));
            tpl = ReplacePlaceholder(tpl, "stream", stream ? "true" : "false");
            return tpl;
        }

        /// <summary>支持三种写法：{{name}} / {name} / $name。</summary>
        static string ReplacePlaceholder(string tpl, string name, string value)
        {
            if (tpl == null) return "";
            string t = tpl;
            if (t.IndexOf("{{", StringComparison.Ordinal) >= 0)
                t = t.Replace("{{" + name + "}}", value);
            if (t.IndexOf("{" + name + "}", StringComparison.Ordinal) >= 0)
                t = t.Replace("{" + name + "}", value);
            if (t.IndexOf("$" + name, StringComparison.Ordinal) >= 0)
                t = t.Replace("$" + name, value);
            return t;
        }

        void ApplyHeaders(HttpRequestMessage req, string provider)
        {
            try
            {
                if (provider == "anthropic")
                {
                    if (_apiKey.Length > 0) req.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                    req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                }
                else
                {
                    // 本地服务（Ollama / LM Studio）不需要 Key，留空就不发这个头
                    if (_apiKey.Length > 0)
                        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
                }

                if (_settings.ExtraHeaders != null)
                {
                    foreach (KeyValuePair<string, string> kv in _settings.ExtraHeaders)
                    {
                        if (kv.Key == null || kv.Key.Trim().Length == 0) continue;
                        string name = kv.Key.Trim();
                        string val = kv.Value == null ? "" : kv.Value;
                        try
                        {
                            req.Headers.Remove(name);
                            if (!req.Headers.TryAddWithoutValidation(name, val))
                                Log.Warn("附加请求头加不上（名字或值不合法）: " + name);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("附加请求头失败 " + name + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("设置请求头失败: " + ex.Message);
            }
        }

        // ==================== 错误说明 ====================

        static string DescribeHttpError(int status, string body, string url, string apiKey)
        {
            string detail = "";
            object json = Json.TryParse(body);
            if (json != null) detail = JsonErrorText(json);
            if (string.IsNullOrEmpty(detail)) detail = Truncate(StripWhitespace(body), 200);
            detail = Sanitize(detail, apiKey);
            string tail = string.IsNullOrEmpty(detail) ? "" : "：" + detail;

            if (status == 401 || status == 403)
                return "API Key 无效或没有权限，请到设置里检查（" + status.ToString(CultureInfo.InvariantCulture) + "）" + tail;
            if (status == 404)
                return "接口地址不对（404），请检查 API 地址：" + MaskUrl(url) + tail;
            if (status == 429)
                return "请求太频繁或额度用尽（429）" + tail;
            if (status == 400)
                return "请求被服务端拒绝（400）" + tail;
            if (status == 413)
                return "文字太长，超出了接口限制（413）" + tail;
            if (status == 422)
                return "请求参数不合法（422）" + tail;
            if (status >= 500)
                return "服务端错误（" + status.ToString(CultureInfo.InvariantCulture) + "），稍后再试" + tail;
            return "请求失败（" + status.ToString(CultureInfo.InvariantCulture) + "）" + tail;
        }

        static string JsonErrorText(object json)
        {
            if (json == null) return null;
            string[] paths = new string[]
            {
                "error.message", "error.msg", "error", "message", "msg",
                "error_msg", "error_description", "detail", "base_resp.status_msg"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                string v = SelectString(json, paths[i]);
                if (v != null && v.Trim().Length > 0) return v.Trim();
            }
            return null;
        }

        // ==================== 小工具 ====================

        static string ReadBodySafe(HttpResponseMessage resp)
        {
            try { return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { return "(读取响应体失败：" + RootMessage(ex) + ")"; }
        }

        static bool IsEventStream(HttpResponseMessage resp)
        {
            try
            {
                if (resp.Content == null || resp.Content.Headers == null) return false;
                System.Net.Http.Headers.MediaTypeHeaderValue mt = resp.Content.Headers.ContentType;
                if (mt == null || mt.MediaType == null) return false;
                string m = mt.MediaType.ToLowerInvariant();
                if (m.IndexOf("event-stream", StringComparison.Ordinal) >= 0) return true;
                if (m.IndexOf("text/plain", StringComparison.Ordinal) >= 0) return true;
                return false;
            }
            catch { return false; }
        }

        static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return "openai";
            string p = provider.Trim().ToLowerInvariant();
            if (p == "anthropic" || p == "claude") return "anthropic";
            if (p == "custom") return "custom";
            return "openai";
        }

        static int ClampTimeout(int seconds)
        {
            if (seconds <= 0) return 40;
            if (seconds < 5) return 5;
            if (seconds > 600) return 600;
            return seconds;
        }

        /// <summary>拼 endpoint：BaseUrl 末尾带不带斜杠都行；已经带 /chat/completions 或 /messages 就不重复拼。</summary>
        public static string BuildUrl(string baseUrl, string provider)
        {
            if (string.IsNullOrEmpty(baseUrl)) return "";
            string b = baseUrl.Trim();
            string query = "";
            int q = b.IndexOf('?');
            if (q >= 0)
            {
                query = b.Substring(q);
                b = b.Substring(0, q);
            }
            b = b.TrimEnd('/');

            string p = NormalizeProvider(provider);
            string suffix = p == "anthropic" ? "/messages" : "/chat/completions";
            string lower = b.ToLowerInvariant();
            bool hasEndpoint =
                lower.EndsWith("/messages", StringComparison.Ordinal) ||
                lower.EndsWith("/chat/completions", StringComparison.Ordinal) ||
                lower.EndsWith("/completions", StringComparison.Ordinal) ||
                lower.EndsWith("/responses", StringComparison.Ordinal);
            if (!hasEndpoint) b = b + suffix;
            return b + query;
        }

        static string HostOf(string url)
        {
            try
            {
                Uri u = new Uri(url);
                return u.Host;
            }
            catch { return "服务器"; }
        }

        static string SelectString(object root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            object v = Json.Select(root, path);
            if (v == null) return null;
            return v as string;
        }

        static string RootMessage(Exception ex)
        {
            if (ex == null) return "未知错误";
            Exception cur = ex;
            while (cur.InnerException != null) cur = cur.InnerException;
            string m = cur.Message == null ? "" : cur.Message.Trim();
            if (m.Length == 0) m = ex.GetType().Name;
            return m;
        }

        static string StripWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Regex.Replace(s, "\\s+", " ");
        }

        static string Truncate(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…（已截断）";
        }

        /// <summary>把 API Key 从任何要显示/写日志的文字里抹掉。</summary>
        public static string Sanitize(string s, string apiKey)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s;
            try
            {
                if (!string.IsNullOrEmpty(apiKey) && apiKey.Length >= 6 && t.IndexOf(apiKey, StringComparison.Ordinal) >= 0)
                    t = t.Replace(apiKey, "****");
                t = Regex.Replace(t, "sk-[A-Za-z0-9_-]{6,}", "sk-****");
                t = Regex.Replace(t, "(?i)(bearer\\s+)[A-Za-z0-9_.-]{6,}", "$1****");
                t = Regex.Replace(t, "(?i)((?:api[-_]?key|apikey|token|secret|password|access_key)\\s*[:=]\\s*[\"']?)[A-Za-z0-9_.-]{6,}", "$1****");
                t = Regex.Replace(t, "(?i)([?&](?:key|api_key|apikey|token|access_token|secret|sign|signature|auth)=)[^&\\s]+", "$1****");
            }
            catch { }
            return t;
        }

        /// <summary>Key 只留前 4 位，其余打码（写日志用）。</summary>
        public static string MaskKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return "(未设置)";
            string k = apiKey.Trim();
            if (k.Length <= 4) return "****";
            return k.Substring(0, 4) + "****（共 " + k.Length.ToString(CultureInfo.InvariantCulture) + " 位）";
        }

        /// <summary>把地址里可能带 Key 的查询参数打码。</summary>
        public static string MaskUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(空)";
            string u = url.Trim();
            int q = u.IndexOf('?');
            if (q < 0) return u;
            string head = u.Substring(0, q);
            string[] parts = u.Substring(q + 1).Split('&');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('&');
                string p = parts[i];
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string name = p.Substring(0, eq).ToLowerInvariant();
                    if (name.IndexOf("key", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("token", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("secret", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("sign", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("auth", StringComparison.Ordinal) >= 0 ||
                        name.IndexOf("password", StringComparison.Ordinal) >= 0)
                    {
                        sb.Append(p.Substring(0, eq + 1)).Append("****");
                        continue;
                    }
                }
                sb.Append(p);
            }
            return head + "?" + sb.ToString();
        }

        void SafeDelta(AiDeltaHandler onDelta, string text)
        {
            if (onDelta == null) return;
            try { onDelta(text); }
            catch (Exception ex) { Log.Warn("流式回调抛异常（已忽略）: " + ex.Message); }
        }
    }
}
