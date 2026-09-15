using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MyIme.Shared;

namespace MyIme.Settings
{
    /// <summary>设置界面里用的服务商预设（和 MyIme.Ai.AiProviderPreset 同构）。</summary>
    public class ProviderInfo
    {
        public string Id = "";
        public string Name = "";
        public string Provider = "openai";
        public string BaseUrl = "";
        public string DefaultModel = "";
        public string DocUrl = "";
        public string Note = "";
        public bool Local = false;
    }

    /// <summary>
    /// AI 服务商预设。
    ///
    /// 说明：真正的预设表在 src\Core\Ai\AiProviderPreset.cs，编译进 MyIme.Core.exe。
    /// 而设置界面是用 tools\check.ps1 / build.ps1 单独编译的（只引用 MyIme.Shared.dll），
    /// 所以这里**不能在编译期引用 MyIme.Ai 命名空间**——那样设置界面就编不过了。
    /// 做法是：运行时用反射去 MyIme.Core.exe 里读 AiProviderPresets.All，
    /// 读不到（主程序还没编译 / 换版本了）就用下面这份内置表兜底。
    /// 两边的数据保持一致，改预设时记得一起改。
    /// </summary>
    public static class ProviderCatalog
    {
        static List<ProviderInfo> _all;

        public static List<ProviderInfo> All
        {
            get
            {
                if (_all == null) _all = Load();
                return _all;
            }
        }

        public static ProviderInfo Custom
        {
            get
            {
                List<ProviderInfo> list = All;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Id == "custom") return list[i];
                return list[list.Count - 1];
            }
        }

        public static ProviderInfo Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            List<ProviderInfo> list = All;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        public static int IndexOf(string id)
        {
            List<ProviderInfo> list = All;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return list.Count - 1;
        }

        static List<ProviderInfo> Load()
        {
            List<ProviderInfo> fromCore = TryLoadFromCore();
            if (fromCore != null && fromCore.Count > 0) return fromCore;
            return Builtin();
        }

        /// <summary>反射读 MyIme.Core.exe 里的 AiProviderPresets.All。失败返回 null。</summary>
        static List<ProviderInfo> TryLoadFromCore()
        {
            try
            {
                string exe = AppPaths.CoreExe;
                if (!File.Exists(exe)) return null;
                Assembly asm = Assembly.LoadFrom(exe);
                Type t = asm.GetType("MyIme.Ai.AiProviderPresets", false);
                if (t == null) return null;
                PropertyInfo pi = t.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
                if (pi == null) return null;
                IEnumerable src = pi.GetValue(null, null) as IEnumerable;
                if (src == null) return null;

                List<ProviderInfo> list = new List<ProviderInfo>();
                foreach (object o in src)
                {
                    if (o == null) continue;
                    ProviderInfo p = new ProviderInfo();
                    p.Id = ReadField(o, "Id");
                    p.Name = ReadField(o, "Name");
                    p.Provider = ReadField(o, "Provider");
                    p.BaseUrl = ReadField(o, "BaseUrl");
                    p.DefaultModel = ReadField(o, "DefaultModel");
                    p.DocUrl = ReadField(o, "DocUrl");
                    p.Note = ReadField(o, "Note");
                    object local = ReadFieldObj(o, "Local");
                    p.Local = (local is bool) && (bool)local;
                    if (p.Id.Length == 0) continue;
                    if (p.Provider.Length == 0) p.Provider = "openai";
                    list.Add(p);
                }
                if (list.Count == 0) return null;
                Log.Info("已从 MyIme.Core.exe 读取 " + list.Count + " 个 AI 服务商预设");
                return list;
            }
            catch (Exception ex)
            {
                Log.Warn("读取 MyIme.Core.exe 里的服务商预设失败，改用设置界面内置列表: " + ex.Message);
                return null;
            }
        }

        static object ReadFieldObj(object o, string name)
        {
            try
            {
                FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) return null;
                return f.GetValue(o);
            }
            catch { return null; }
        }

        static string ReadField(object o, string name)
        {
            object v = ReadFieldObj(o, name);
            return v == null ? "" : v.ToString();
        }

        static ProviderInfo P(string id, string name, string provider, string baseUrl, string model, string docUrl, string note)
        {
            ProviderInfo p = new ProviderInfo();
            p.Id = id; p.Name = name; p.Provider = provider; p.BaseUrl = baseUrl;
            p.DefaultModel = model; p.DocUrl = docUrl; p.Note = note;
            return p;
        }

        /// <summary>兜底列表：内容和 src\Core\Ai\AiProviderPreset.cs 一致。</summary>
        static List<ProviderInfo> Builtin()
        {
            List<ProviderInfo> list = new List<ProviderInfo>();

            list.Add(P("openai", "OpenAI（ChatGPT）", "openai",
                "https://api.openai.com/v1", "gpt-4o-mini",
                "https://platform.openai.com/api-keys",
                "官方接口；国内访问可能需要代理。"));

            list.Add(P("anthropic", "Anthropic Claude", "anthropic",
                "https://api.anthropic.com/v1", "claude-sonnet-4-5",
                "https://console.anthropic.com/settings/keys",
                "用的是 Anthropic 原生协议（x-api-key）。也可填 claude-3-5-sonnet-latest 等。"));

            list.Add(P("deepseek", "DeepSeek（深度求索）", "openai",
                "https://api.deepseek.com/v1", "deepseek-chat",
                "https://platform.deepseek.com/api_keys",
                "便宜好用，中文润色效果不错；推理模型是 deepseek-reasoner。"));

            list.Add(P("moonshot", "月之暗面 Kimi", "openai",
                "https://api.moonshot.cn/v1", "moonshot-v1-8k",
                "https://platform.moonshot.cn/console/api-keys",
                "长文本见长；上下文越长单价越高，可换 moonshot-v1-32k。"));

            list.Add(P("zhipu", "智谱 GLM（智谱清言）", "openai",
                "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
                "https://open.bigmodel.cn/usercenter/apikeys",
                "glm-4-flash 免费额度大；更强的可换 glm-4-plus。"));

            list.Add(P("dashscope", "通义千问（阿里云百炼）", "openai",
                "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus",
                "https://bailian.console.aliyun.com/",
                "百炼的 OpenAI 兼容模式；也可用 qwen-turbo（更便宜）。"));

            list.Add(P("qianfan", "百度文心千帆（v2）", "openai",
                "https://qianfan.baidubce.com/v2", "ernie-4.0-8k",
                "https://console.bce.baidu.com/qianfan/ais/console/applicationConsole/application",
                "千帆 v2 的 OpenAI 兼容接口；Key 用新版控制台里的「应用 API Key」。"));

            list.Add(P("ark", "火山方舟（豆包）", "openai",
                "https://ark.cn-beijing.volces.com/api/v3", "doubao-pro-32k",
                "https://console.volcengine.com/ark",
                "模型名可填 doubao-pro-32k，也可填控制台里创建的接入点 ID（ep- 开头）。"));

            list.Add(P("minimax", "MiniMax（稀宇科技）", "openai",
                "https://api.minimax.chat/v1", "abab6.5s-chat",
                "https://platform.minimaxi.com/user-center/basic-information/interface-key",
                "新接口（chatcompletion_v2）可能要在地址后面带上 ?GroupId=你的组ID。"));

            list.Add(P("lingyiwanwu", "零一万物 Yi", "openai",
                "https://api.lingyiwanwu.com/v1", "yi-lightning",
                "https://platform.lingyiwanwu.com/apikeys",
                "yi-lightning 快且便宜；长文本可用 yi-large。"));

            list.Add(P("stepfun", "阶跃星辰 StepFun", "openai",
                "https://api.stepfun.com/v1", "step-1-8k",
                "https://platform.stepfun.com/interface-key",
                "也可用 step-2-16k / step-1-32k 等更大的模型。"));

            list.Add(P("hunyuan", "腾讯混元", "openai",
                "https://api.hunyuan.cloud.tencent.com/v1", "hunyuan-turbos-latest",
                "https://console.cloud.tencent.com/hunyuan/api-key",
                "OpenAI 兼容接口；便宜的可换 hunyuan-lite。"));

            list.Add(P("siliconflow", "硅基流动 SiliconFlow", "openai",
                "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct",
                "https://cloud.siliconflow.cn/account/ak",
                "一个 Key 用很多开源模型；模型名要写全称，如 deepseek-ai/DeepSeek-V3。"));

            ProviderInfo ollama = P("ollama", "Ollama（本地）", "openai",
                "http://localhost:11434/v1", "qwen2.5:7b",
                "https://ollama.com/download",
                "本地跑，不用 Key；先 ollama pull qwen2.5:7b，模型名要和本地一致。");
            ollama.Local = true;
            list.Add(ollama);

            ProviderInfo lmstudio = P("lmstudio", "LM Studio（本地）", "openai",
                "http://localhost:1234/v1", "local-model",
                "https://lmstudio.ai/",
                "先在 LM Studio 里加载模型并启动本地服务器；模型名可留 local-model。");
            lmstudio.Local = true;
            list.Add(lmstudio);

            ProviderInfo custom = P("custom", "自定义（任何兼容接口）", "openai", "", "",
                "",
                "自己填接口地址和模型名；协议可选 openai / anthropic / custom（请求体模板）。");
            list.Add(custom);

            return list;
        }
    }
}
