using System;
using System.Collections.Generic;

namespace MyIme.Ai
{
    /// <summary>
    /// 一家可接入的 AI 服务商预设。设置界面里"一键选择"用这个。
    /// 全部用 public 字段，方便设置界面直接绑定，也方便 Json 反射映射。
    /// </summary>
    public class AiProviderPreset
    {
        /// <summary>稳定标识，例如 "deepseek"。存进设置文件里，不要随便改。</summary>
        public string Id = "";

        /// <summary>界面上显示的名字，例如 "DeepSeek（深度求索）"。</summary>
        public string Name = "";

        /// <summary>协议类型："openai" / "anthropic" / "custom"。</summary>
        public string Provider = "openai";

        /// <summary>接口根地址（不带 /chat/completions），例如 "https://api.deepseek.com/v1"。</summary>
        public string BaseUrl = "";

        /// <summary>默认模型名，例如 "deepseek-chat"。</summary>
        public string DefaultModel = "";

        /// <summary>申请 / 管理 API Key 的网址。</summary>
        public string DocUrl = "";

        /// <summary>一句话说明（本地模型、注意事项等）。</summary>
        public string Note = "";

        /// <summary>是不是本地服务（本地服务通常不需要 API Key）。</summary>
        public bool Local = false;

        public AiProviderPreset() { }

        public AiProviderPreset(string id, string name, string provider, string baseUrl, string defaultModel, string docUrl, string note)
        {
            Id = id;
            Name = name;
            Provider = provider;
            BaseUrl = baseUrl;
            DefaultModel = defaultModel;
            DocUrl = docUrl;
            Note = note;
        }
    }

    /// <summary>
    /// 内置的服务商预设列表。BaseUrl 和默认模型名都按各家官方文档抄写，
    /// 用户只要挑一项、填 Key 就能用；用不了的也可以在"自定义"里自己改。
    /// </summary>
    public static class AiProviderPresets
    {
        static readonly object Gate = new object();
        static List<AiProviderPreset> _all;
        static AiProviderPreset _custom;

        /// <summary>全部预设（含"自定义"，它总是最后一项）。</summary>
        public static List<AiProviderPreset> All
        {
            get
            {
                lock (Gate)
                {
                    if (_all == null) _all = Build();
                    return _all;
                }
            }
        }

        /// <summary>"自定义"那一项：协议自己选、地址自己填。</summary>
        public static AiProviderPreset Custom
        {
            get
            {
                lock (Gate)
                {
                    if (_custom == null) _custom = MakeCustom();
                    return _custom;
                }
            }
        }

        /// <summary>按 Id 找预设，找不到返回 null（不抛异常）。</summary>
        public static AiProviderPreset Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string want = id.Trim();
            List<AiProviderPreset> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, want, StringComparison.OrdinalIgnoreCase)) return list[i];
            }
            return null;
        }

        /// <summary>按 Id 找预设，找不到就用"自定义"。</summary>
        public static AiProviderPreset FindOrDefault(string id)
        {
            AiProviderPreset p = Find(id);
            if (p != null) return p;
            return Custom;
        }

        /// <summary>把预设写进设置（地址、模型、协议）。API Key 不在这里管。</summary>
        public static void ApplyTo(AiProviderPreset preset, MyIme.Shared.AiSettings settings)
        {
            if (preset == null || settings == null) return;
            settings.Provider = preset.Provider;
            settings.BaseUrl = preset.BaseUrl;
            if (!string.IsNullOrEmpty(preset.DefaultModel)) settings.Model = preset.DefaultModel;
            WriteCustomBodyTemplate(preset, settings);
        }

        static void WriteCustomBodyTemplate(AiProviderPreset preset, MyIme.Shared.AiSettings settings)
        {
            // 自定义协议才需要请求体模板；内置协议由 AiClient 自己拼，清掉避免混淆
            if (string.Equals(preset.Provider, "custom", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(settings.CustomBodyTemplate))
                    settings.CustomBodyTemplate = DefaultCustomTemplate;
            }
        }

        /// <summary>自定义协议的默认请求体模板（占位符会被 AiClient 替换）。</summary>
        public const string DefaultCustomTemplate =
            "{\"model\": {model}, \"messages\": [{\"role\": \"system\", \"content\": {system}}, " +
            "{\"role\": \"user\", \"content\": {user}}], " +
            "\"temperature\": {temperature}, \"max_tokens\": {max_tokens}, \"stream\": {stream}}";

        static List<AiProviderPreset> Build()
        {
            List<AiProviderPreset> list = new List<AiProviderPreset>();

            // ---------------- 国际 ----------------
            list.Add(new AiProviderPreset(
                "openai", "OpenAI（ChatGPT）", "openai",
                "https://api.openai.com/v1", "gpt-4o-mini",
                "https://platform.openai.com/api-keys",
                "官方接口；国内访问可能需要代理。"));

            list.Add(new AiProviderPreset(
                "anthropic", "Anthropic Claude", "anthropic",
                "https://api.anthropic.com/v1", "claude-sonnet-4-5",
                "https://console.anthropic.com/settings/keys",
                "用的是 Anthropic 原生协议（x-api-key）。也可填 claude-3-5-sonnet-latest 等。"));

            // ---------------- 国内 ----------------
            list.Add(new AiProviderPreset(
                "deepseek", "DeepSeek（深度求索）", "openai",
                "https://api.deepseek.com/v1", "deepseek-chat",
                "https://platform.deepseek.com/api_keys",
                "便宜好用，中文润色效果不错；推理模型是 deepseek-reasoner。"));

            list.Add(new AiProviderPreset(
                "moonshot", "月之暗面 Kimi", "openai",
                "https://api.moonshot.cn/v1", "moonshot-v1-8k",
                "https://platform.moonshot.cn/console/api-keys",
                "长文本见长；上下文越长单价越高，可换 moonshot-v1-32k。"));

            list.Add(new AiProviderPreset(
                "zhipu", "智谱 GLM（智谱清言）", "openai",
                "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
                "https://open.bigmodel.cn/usercenter/apikeys",
                "glm-4-flash 免费额度大；更强的可换 glm-4-plus。"));

            list.Add(new AiProviderPreset(
                "dashscope", "通义千问（阿里云百炼）", "openai",
                "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus",
                "https://bailian.console.aliyun.com/",
                "百炼的 OpenAI 兼容模式；也可用 qwen-turbo（更便宜）。"));

            list.Add(new AiProviderPreset(
                "qianfan", "百度文心千帆（v2）", "openai",
                "https://qianfan.baidubce.com/v2", "ernie-4.0-8k",
                "https://console.bce.baidu.com/qianfan/ais/console/applicationConsole/application",
                "千帆 v2 的 OpenAI 兼容接口；Key 用新版控制台里的「应用 API Key」。"));

            list.Add(new AiProviderPreset(
                "ark", "火山方舟（豆包）", "openai",
                "https://ark.cn-beijing.volces.com/api/v3", "doubao-pro-32k",
                "https://console.volcengine.com/ark",
                "模型名可填 doubao-pro-32k，也可填控制台里创建的接入点 ID（ep- 开头）。"));

            list.Add(new AiProviderPreset(
                "minimax", "MiniMax（稀宇科技）", "openai",
                "https://api.minimax.chat/v1", "abab6.5s-chat",
                "https://platform.minimaxi.com/user-center/basic-information/interface-key",
                "新接口（chatcompletion_v2）可能要在地址后面带上 ?GroupId=你的组ID。"));

            list.Add(new AiProviderPreset(
                "lingyiwanwu", "零一万物 Yi", "openai",
                "https://api.lingyiwanwu.com/v1", "yi-lightning",
                "https://platform.lingyiwanwu.com/apikeys",
                "yi-lightning 快且便宜；长文本可用 yi-large。"));

            list.Add(new AiProviderPreset(
                "stepfun", "阶跃星辰 StepFun", "openai",
                "https://api.stepfun.com/v1", "step-1-8k",
                "https://platform.stepfun.com/interface-key",
                "也可用 step-2-16k / step-1-32k 等更大的模型。"));

            list.Add(new AiProviderPreset(
                "hunyuan", "腾讯混元", "openai",
                "https://api.hunyuan.cloud.tencent.com/v1", "hunyuan-turbos-latest",
                "https://console.cloud.tencent.com/hunyuan/api-key",
                "OpenAI 兼容接口；便宜的可换 hunyuan-lite。"));

            list.Add(new AiProviderPreset(
                "siliconflow", "硅基流动 SiliconFlow", "openai",
                "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct",
                "https://cloud.siliconflow.cn/account/ak",
                "一个 Key 用很多开源模型；模型名要写全称，如 deepseek-ai/DeepSeek-V3。"));

            // ---------------- 本地 ----------------
            AiProviderPreset ollama = new AiProviderPreset(
                "ollama", "Ollama（本地）", "openai",
                "http://localhost:11434/v1", "qwen2.5:7b",
                "https://ollama.com/download",
                "本地跑，不用 Key；先 ollama pull qwen2.5:7b，模型名要和本地一致。");
            ollama.Local = true;
            list.Add(ollama);

            AiProviderPreset lmstudio = new AiProviderPreset(
                "lmstudio", "LM Studio（本地）", "openai",
                "http://localhost:1234/v1", "local-model",
                "https://lmstudio.ai/",
                "先在 LM Studio 里加载模型并启动本地服务器；模型名可留 local-model。");
            lmstudio.Local = true;
            list.Add(lmstudio);

            // ---------------- 自定义 ----------------
            list.Add(Custom);
            return list;
        }

        static AiProviderPreset MakeCustom()
        {
            AiProviderPreset p = new AiProviderPreset(
                "custom", "自定义（任何兼容接口）", "openai",
                "", "",
                "",
                "自己填接口地址和模型名；协议可选 openai / anthropic / custom（请求体模板）。");
            return p;
        }
    }
}
