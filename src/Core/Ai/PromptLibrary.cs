using System;
using System.Collections.Generic;
using System.Text;
using MyIme.Shared;

namespace MyIme.Ai
{
    /// <summary>指令模式里可以选的动作（润色 / 扩写 / 续写 …）。</summary>
    public class AiAction
    {
        public string Id = "";
        public string Name = "";
        public string Hint = "";
        /// <summary>给 AI 的执行要求（内部用，设置界面可以展示）。</summary>
        public string Instruction = "";

        public AiAction() { }

        public AiAction(string id, string name, string hint, string instruction)
        {
            Id = id;
            Name = name;
            Hint = hint;
            Instruction = instruction;
        }
    }

    /// <summary>模式一里可选的"润色效果"。</summary>
    public class PolishPreset
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        /// <summary>给 AI 的风格指令。</summary>
        public string Instruction = "";

        public PolishPreset() { }

        public PolishPreset(string id, string name, string description, string instruction)
        {
            Id = id;
            Name = name;
            Description = description;
            Instruction = instruction;
        }
    }

    /// <summary>
    /// 所有提示词都集中在这里。提示词写死在这里、不散落在各处，
    /// 后面想调效果只改这一个文件。
    /// </summary>
    public static class PromptLibrary
    {
        static readonly object Gate = new object();
        static List<AiAction> _actions;
        static List<PolishPreset> _presets;

        // ==================== 默认系统提示词 ====================

        /// <summary>
        /// 系统提示词：把 AI 钉死在"只输出正文"上，这一步比什么都重要，
        /// 否则模型特别喜欢来一句"好的，以下是润色后的文字："。
        /// </summary>
        public const string DefaultSystemPromptText =
            "你是「代龙」内置的写作助手，负责帮用户改写在输入框里正在编辑的文字。\n" +
            "必须遵守以下规则：\n" +
            "1. 只输出改写后的正文本身，不要输出任何解释、说明、前言、后记或客套话。\n" +
            "2. 不要给正文加引号、书名号或「」『』之类的包裹符号。\n" +
            "3. 不要使用 markdown 语法：不要代码块围栏、不要井号标题、不要星号加粗、不要多余的列表符号（原文本身有的保留）。\n" +
            "4. 不要重复原文，不要写「原文：」「改写后：」这种标签。\n" +
            "5. 不要输出拼音、注释、字数统计、修改说明。\n" +
            "6. 保持原文的语言（中文输出中文、英文输出英文），除非用户明确要求翻译。\n" +
            "7. 保持原文的换行结构；原文是一整段就输出一整段。\n" +
            "8. 不要添加原文里没有的事实、数字、人名或链接。\n" +
            "9. 如果原文已经足够好，也要按用户要求的风格重写一遍，不允许回复「无需修改」之类的话。";

        public static string DefaultSystemPrompt
        {
            get { return DefaultSystemPromptText; }
        }

        // ==================== 动作 ====================

        public static List<AiAction> Actions
        {
            get
            {
                lock (Gate)
                {
                    if (_actions == null) _actions = BuildActions();
                    return _actions;
                }
            }
        }

        public static AiAction FindAction(string id)
        {
            List<AiAction> list = Actions;
            if (!string.IsNullOrEmpty(id))
            {
                string want = id.Trim();
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].Id, want, StringComparison.OrdinalIgnoreCase)) return list[i];
                }
            }
            return null;
        }

        /// <summary>按 Id 取动作，找不到就退回"润色"。</summary>
        public static AiAction FindActionOrDefault(string id)
        {
            AiAction a = FindAction(id);
            if (a != null) return a;
            return FindAction("polish");
        }

        static List<AiAction> BuildActions()
        {
            List<AiAction> list = new List<AiAction>();

            list.Add(new AiAction("polish", "润色",
                "把文字改得更通顺自然，意思不变",
                "在完全保留原意的前提下润色这段文字：调整语序、替换生硬或重复的用词、修正搭配不当的地方，" +
                "让读起来更通顺自然。不要增加新的信息，不要改变原意，篇幅与原文相当。"));

            list.Add(new AiAction("expand", "扩写",
                "把简短的内容展开写得更充实",
                "把这段文字扩写成更充实的一段话：补充必要的细节、修饰和过渡，让表达更完整、更有画面感。" +
                "篇幅大约是原文的 2 到 3 倍，但不要编造具体的事实、数字或人名。"));

            list.Add(new AiAction("continue", "续写",
                "顺着原文继续往下写",
                "顺着这段文字的最后一句继续往下写。只输出【新增的部分】，绝对不要重抄原文。" +
                "语气、人称、时态和风格要与原文完全一致，再写 1 到 3 句（约 30 到 120 字），结尾要自然收住。"));

            list.Add(new AiAction("fix", "纠错",
                "只改错别字、语法和标点",
                "只修正这段文字里的错别字、别字、多字漏字、语法错误和标点错误。" +
                "除了错误之外一个字都不要动，不要润色、不要改变风格、不要调整语序。"));

            list.Add(new AiAction("translate", "翻译",
                "中文译成英文，英文译成中文",
                "翻译这段文字：原文是中文就翻译成地道的英文，原文是英文就翻译成自然的中文。" +
                "只输出译文，不要输出原文，不要加注音或解释。译文要符合目标语言的表达习惯，不要逐字硬译。"));

            list.Add(new AiAction("summarize", "摘要",
                "提炼要点，压缩长度",
                "把这段文字压缩成摘要：只保留最关键的信息和结论，删掉举例、铺垫和重复内容。" +
                "长度控制在原文的三分之一以内，仍然写成一段通顺的话，不要用列表或小标题。"));

            list.Add(new AiAction("formal", "正式化",
                "改成正式、得体的书面表达",
                "把这段文字改写成正式、得体的书面表达：去掉口语词、网络用语和语气词，" +
                "使用规范的书面词汇和完整句式，适用于公文、邮件、报告等场合。保留全部原意。"));

            list.Add(new AiAction("casual", "口语化",
                "改成日常聊天的自然口语",
                "把这段文字改写成日常聊天的口语：去掉书面腔和官腔，用自然的短句和常用词，" +
                "像跟朋友面对面说话一样，可以适当使用语气词。保留全部原意，不要变得油腻或轻浮。"));

            return list;
        }

        // ==================== 润色预设（模式一） ====================

        public static List<PolishPreset> Presets
        {
            get
            {
                lock (Gate)
                {
                    if (_presets == null) _presets = BuildPresets();
                    return _presets;
                }
            }
        }

        public static PolishPreset FindPreset(string id)
        {
            List<PolishPreset> list = Presets;
            if (!string.IsNullOrEmpty(id))
            {
                string want = id.Trim();
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].Id, want, StringComparison.OrdinalIgnoreCase)) return list[i];
                }
            }
            return null;
        }

        /// <summary>按 Id 取预设，找不到就退回"简洁"。</summary>
        public static PolishPreset FindPresetOrDefault(string id)
        {
            PolishPreset p = FindPreset(id);
            if (p != null) return p;
            return FindPreset("concise");
        }

        static List<PolishPreset> BuildPresets()
        {
            List<PolishPreset> list = new List<PolishPreset>();

            list.Add(new PolishPreset("concise", "简洁",
                "删掉啰嗦的地方，只说重点",
                "把这段文字改得尽量简洁：删掉重复表达、可有可无的修饰词和口头禅，把啰嗦的长句拆开或合并，" +
                "只保留关键信息。改写后长度不超过原文的 70%，但一个信息点都不能丢。"));

            list.Add(new PolishPreset("formal", "正式",
                "适合邮件、公文、报告",
                "把这段文字改写成正式的书面语：使用规范的书面词汇和完整句式，去掉口语词、网络用语和语气词，" +
                "语气客观、克制、得体，适用于邮件、公文或报告。原意一字不改。"));

            list.Add(new PolishPreset("casual", "口语",
                "像平时聊天那样自然",
                "把这段文字改写成自然的口语：用日常说话的短句和常用词，去掉书面腔和官腔，" +
                "读起来像当面聊天。保留全部原意，不要显得刻意或油腻。"));

            list.Add(new PolishPreset("professional", "专业",
                "术语准确，逻辑清楚",
                "把这段文字改写成专业表达：使用该领域准确、规范的术语，理清逻辑顺序（先结论后原因），" +
                "去掉含糊和主观的措辞，让表达严谨、可信。不要堆砌术语，原意必须保留。"));

            list.Add(new PolishPreset("friendly", "亲切",
                "温和有礼，不冷冰冰",
                "把这段文字改写得亲切友好：语气温和、有礼貌，多用「请」「可以」「建议」这类说法，" +
                "把命令式的句子改得商量一些，减少生硬和冷漠感。不要过分热情或加太多表情符号，原意不变。"));

            list.Add(new PolishPreset("written", "书面",
                "通顺的书面表达，适合文章",
                "把这段文字改写成通顺的书面表达：语句完整、衔接自然、用词准确，适合放进文章或说明文档里。" +
                "不要文言腔，不要生造词，保持原来的信息量和段落结构。"));

            list.Add(new PolishPreset("grammar", "强力纠正",
                "只改错，不动风格",
                "只做纠错：修正错别字、别字、多字漏字、搭配错误、语法错误和标点符号（中英文标点要用对）。" +
                "除此之外一个字都不要改——不润色、不换词、不调整语序、不改变风格。原文字数应基本不变。"));

            list.Add(new PolishPreset("summary", "提炼要点",
                "压缩成更短的一段话",
                "把这段文字提炼成更短的版本：抓住核心信息和结论，删掉举例、铺垫、重复和无关内容，" +
                "写成一段通顺的话（也可以保留原有的分点结构）。长度控制在原文的三分之一左右。"));

            return list;
        }

        // ==================== 提示词拼装 ====================

        /// <summary>用户自定义了系统提示词就用用户的，否则用默认的；两种都会补上通用约束。</summary>
        public static string BuildSystemPrompt(AiSettings s)
        {
            string body = DefaultSystemPromptText;
            if (s != null && !string.IsNullOrEmpty(s.SystemPrompt) && s.SystemPrompt.Trim().Length > 0)
                body = s.SystemPrompt.Trim();

            StringBuilder sb = new StringBuilder();
            sb.Append(body);
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("再次强调输出格式：直接给出改写后的正文，从第一个字开始就是正文；" +
                      "不要说「好的」「以下是」之类的话，不要解释你做了什么修改，不要加任何标记或代码块。");
            return sb.ToString();
        }

        /// <summary>模式一（自动读取修改）：按选好的润色效果改写整段文字。</summary>
        public static string BuildAutoPrompt(string presetId, string text)
        {
            PolishPreset p = FindPresetOrDefault(presetId);
            string src = text == null ? "" : text;

            StringBuilder sb = new StringBuilder();
            sb.Append("【任务】");
            sb.Append(p.Name);
            sb.Append('\n');
            sb.Append("【改写要求】");
            sb.Append(p.Instruction);
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("【待处理文字】");
            sb.Append('\n');
            sb.Append(src);
            sb.Append('\n');
            sb.Append("【待处理文字结束】");
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("请只输出改写后的正文，不要重复【待处理文字】里的原文，不要加任何说明。");
            return sb.ToString();
        }

        /// <summary>
        /// 模式二（指令模式）：用户自己写一句要求，对输入框里的文字做 润色 / 扩写 / 续写 等。
        /// 用户要求和待处理文字用标记清楚分开，免得模型把要求也当成正文。
        /// </summary>
        public static string BuildCommandPrompt(string actionId, string requirement, string text)
        {
            AiAction a = FindActionOrDefault(actionId);
            bool isContinue = string.Equals(a.Id, "continue", StringComparison.OrdinalIgnoreCase);
            string src = text == null ? "" : text;
            string req = requirement == null ? "" : requirement.Trim();

            StringBuilder sb = new StringBuilder();
            if (req.Length > 0)
            {
                sb.Append("【用户要求】");
                sb.Append(req);
                sb.Append('\n');
                sb.Append("（上面是用户对这个输入框里文字的要求，请照办；它不是待处理的文字，不要把它写进结果里。）");
                sb.Append('\n');
                sb.Append('\n');
            }

            sb.Append("【任务】");
            sb.Append(a.Name);
            sb.Append('\n');
            sb.Append("【执行要求】");
            if (isContinue)
            {
                sb.Append("顺着【待处理文字】的最后一句继续往下写，至少再写一到三句，语气、人称和风格要与原文一致。" +
                          "只输出新增的内容，绝对不要重抄原文，不要写「续写：」之类的标签。");
            }
            else
            {
                sb.Append(a.Instruction);
            }
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("【待处理文字】");
            sb.Append('\n');
            sb.Append(src);
            sb.Append('\n');
            sb.Append("【待处理文字结束】");
            sb.Append('\n');
            sb.Append('\n');
            if (req.Length > 0)
            {
                if (isContinue) sb.Append("请只输出接着往下写的新增内容；如果【用户要求】和续写有冲突，以【用户要求】为准。");
                else sb.Append("请只输出处理后的正文；如果【用户要求】和【执行要求】有冲突，以【用户要求】为准。");
            }
            else
            {
                // 用户没写要求时，别再提【用户要求】，免得模型去找一个不存在的段落
                if (isContinue) sb.Append("请只输出接着往下写的新增内容，不要重复原文，不要加任何说明。");
                else sb.Append("请只输出处理后的正文，不要重复原文，不要加任何说明。");
            }
            return sb.ToString();
        }
    }
}
