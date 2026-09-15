using System;
using System.Collections.Generic;
using System.Text;
using MyIme.Shared;

/// <summary>
/// 引擎单元测试。不依赖 Win32 / 不装输入法就能跑：
///   .\out\EngineTests.exe
/// </summary>
static class EngineTests
{
    static int _pass;
    static int _fail;
    static string _section = "";

    static void Section(string name)
    {
        _section = name;
        Console.WriteLine();
        Console.WriteLine("── " + name + " " + new string('-', Math.Max(0, 60 - name.Length)));
    }

    static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  [OK]   " + what); }
        else { _fail++; Console.WriteLine("  [FAIL] " + what); }
    }

    static void CheckEq(string actual, string expected, string what)
    {
        bool ok = string.Equals(actual, expected, StringComparison.Ordinal);
        if (ok) { _pass++; Console.WriteLine("  [OK]   " + what + "  ->  " + actual); }
        else
        {
            _fail++;
            Console.WriteLine("  [FAIL] " + what);
            Console.WriteLine("         期望: " + expected);
            Console.WriteLine("         实际: " + actual);
        }
    }

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        Log.MinLevel = LogLevel.Warn;
        Log.ConsoleEcho = false;

        Console.WriteLine("墨言输入法 · 引擎单元测试");
        Console.WriteLine("数据目录: " + AppPaths.BuiltinDataDir);

        Lexicon lex = new Lexicon();
        lex.LoadAll();

        Console.WriteLine();
        Console.WriteLine("词库: 音节=" + lex.SyllableCount + " 单字=" + lex.CharCount
                          + " 词=" + lex.WordCount + " 英文=" + lex.EnglishCount);
        foreach (string w in lex.LoadWarnings) Console.WriteLine("  [警告] " + w);

        InputSettings cfg = new InputSettings();
        PinyinEngine eng = new PinyinEngine(lex, cfg);

        // 候选排序诊断：EngineTests.exe --rank
        // 单音节输入的首选字最容易出问题，单独给一个能直接看的报告。
        if (args != null && args.Length > 0 && args[0] == "--rank")
        {
            RankReport(lex, eng, args.Length > 1 ? args[1] : null);
            return 0;
        }

        // 看英文补全实际给出什么：EngineTests.exe --en th,rec,hel
        if (args != null && args.Length >= 2 && args[0] == "--en")
        {
            foreach (string p in args[1].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string pre = p.Trim();
                List<Candidate> hits = eng.SuggestEnglish(pre, 8);
                List<string> texts = new List<string>();
                foreach (Candidate cd in hits) texts.Add(cd.Text);
                Console.WriteLine(pre.PadRight(7) + "-> " + (texts.Count == 0 ? "(无)" : string.Join(" ", texts.ToArray())));
            }
            return 0;
        }

        // 查某个音节到底收了哪些字：EngineTests.exe --chars xie,xiao,zhao
        if (args != null && args.Length >= 2 && args[0] == "--chars")
        {
            foreach (string syl in args[1].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                List<string> list = lex.CharsOf(syl.Trim());
                Console.WriteLine(syl.Trim().PadRight(7) + (list == null ? "(没有这个音节)" :
                    "共 " + list.Count + " 个: " + string.Join(" ", list.ToArray())));
            }
            return 0;
        }

        Section("词典装载");
        Check(lex.CharCount >= 4000, "单字表 >= 4000 字（实际 " + lex.CharCount + "）");
        Check(lex.WordCount >= 1000, "词库 >= 1000 条（实际 " + lex.WordCount + "）");
        Check(eng.SyllableInventory >= 400, "音节表 >= 400（实际 " + eng.SyllableInventory + "）");
        Check(lex.CharsOf("ni") != null && lex.CharsOf("ni")[0] == "你", "ni 的首选字是「你」");
        Check(lex.CharsOf("hao") != null && lex.CharsOf("hao")[0] == "好", "hao 的首选字是「好」");
        Check(lex.CharsOf("shi") != null && lex.CharsOf("shi")[0] == "是", "shi 的首选字是「是」");

        Section("音节切分");
        Composition c1 = eng.Compose("nihao");
        CheckEq(string.Join("|", c1.Syllables.ToArray()), "ni|hao", "nihao -> ni hao");
        Composition c2 = eng.Compose("xian");
        CheckEq(string.Join("|", c2.Syllables.ToArray()), "xian", "xian 不切成 xi+an");
        Composition c3 = eng.Compose("zhongguo");
        CheckEq(string.Join("|", c3.Syllables.ToArray()), "zhong|guo", "zhongguo -> zhong guo");
        Composition c4 = eng.Compose("ni'hao");
        CheckEq(string.Join("|", c4.Syllables.ToArray()), "ni|hao", "撇号强制分隔 ni'hao");
        Composition c5 = eng.Compose("nih");
        CheckEq(string.Join("|", c5.Syllables.ToArray()), "ni", "nih 的完整部分切出 ni");
        CheckEq(c5.PartialTail, "h", "nih 的尾巴是 h");

        Section("候选生成");
        PrintTop(eng.Compose("nihao"), 5);
        CheckEq(Top(eng.Compose("nihao")), "你好", "nihao 首选 你好");
        CheckEq(Top(eng.Compose("zhongguo")), "中国", "zhongguo 首选 中国");
        CheckEq(Top(eng.Compose("shurufa")), "输入法", "shurufa 首选 输入法");
        CheckEq(Top(eng.Compose("xiexie")), "谢谢", "xiexie 首选 谢谢");
        CheckEq(Top(eng.Compose("ni")), "你", "ni 首选 你");
        CheckEq(Top(eng.Compose("wo")), "我", "wo 首选 我");

        Section("上屏消耗的音节数（回归：曾经上屏后残留拼音）");
        Composition zc = eng.Compose("zhongguo");
        CheckEq(zc.Candidates[0].Text, "中国", "zhongguo 首选 中国");
        Check(zc.Candidates[0].Consumed == 2,
              "首选候选消耗全部 2 个音节（实际 " + zc.Candidates[0].Consumed + "）—— 否则上屏后会残留 guo");
        Composition sc = eng.Compose("shurufa");
        Check(sc.Candidates[0].Consumed == sc.Syllables.Count,
              "shurufa 首选消耗全部 " + sc.Syllables.Count + " 个音节（实际 " + sc.Candidates[0].Consumed + "）");
        Composition nc = eng.Compose("nihaoma");
        CheckEq(nc.Candidates[0].Text, "你好吗", "nihaoma 首选 你好吗");
        Check(nc.Candidates[0].Consumed == 3,
              "nihaoma 首选消耗 3 个音节（实际 " + nc.Candidates[0].Consumed + "）");
        // 单字候选必须只消耗 1 个音节，剩下的留着继续选
        Composition multi = eng.Compose("nihaoma");
        Candidate single = null;
        foreach (Candidate cd in multi.Candidates)
            if (cd.Text.Length == 1 && cd.Kind == CandidateKind.Char) { single = cd; break; }
        Check(single != null && single.Consumed == 1,
              "存在「只吃首音节」的单字候选，剩余音节继续留在缓冲区（实际 "
              + (single == null ? "没有这种候选" : single.Text + "/" + single.Consumed) + "）");

        Section("多音字（每个读音都必须打得出这个字）");
        // 多音字只要漏收一个读音，用户就会撞上"那个音打不出这个字"的死路。
        // 这里成对验证：同一个字在两个不同音节下都必须有候选。
        CheckMulti(eng, "行", "xing", "hang");
        CheckMulti(eng, "重", "zhong", "chong");
        CheckMulti(eng, "长", "chang", "zhang");
        CheckMulti(eng, "了", "le", "liao");
        CheckMulti(eng, "乐", "le", "yue");
        CheckMulti(eng, "都", "du", "dou");
        CheckMulti(eng, "和", "he", "huo");
        CheckMulti(eng, "差", "cha", "chai");
        CheckMulti(eng, "单", "dan", "shan");
        CheckMulti(eng, "朴", "pu", "po");
        CheckMulti(eng, "说", "shuo", "shui");     // 说客、游说
        CheckMulti(eng, "得", "de", "dei");        // 得亏、得你去
        CheckMulti(eng, "地", "de", "di");         // 慢慢地、地方
        CheckMulti(eng, "着", "zhe", "zhuo");      // 看着、着重
        CheckMulti(eng, "会", "hui", "kuai");      // 会议、会计
        CheckMulti(eng, "薄", "bao", "bo");        // 薄纸、薄弱
        CheckMulti(eng, "血", "xue", "xie");       // 血液、流血了
        CheckMulti(eng, "削", "xue", "xiao");      // 剥削、削苹果
        CheckMulti(eng, "爪", "zhua", "zhao");     // 爪子、爪牙
        CheckMulti(eng, "择", "ze", "zhai");       // 选择、择菜
        CheckMulti(eng, "藏", "cang", "zang");     // 隐藏、西藏
        CheckMulti(eng, "着", "zhe", "zhao");      // 看着、着火
        CheckMulti(eng, "率", "lv", "shuai");      // 效率、率领
        CheckMulti(eng, "没", "mei", "mo");        // 没有、没收
        CheckMulti(eng, "露", "lu", "lou");        // 露水、露馅
        CheckMulti(eng, "圈", "quan", "juan");     // 圈子、猪圈
        CheckMulti(eng, "壳", "ke", "qiao");       // 贝壳、地壳
        CheckMulti(eng, "落", "luo", "la");        // 落下、落下东西
        CheckMulti(eng, "龟", "gui", "jun");       // 乌龟、龟裂
        CheckMulti(eng, "嚼", "jiao", "jue");      // 嚼东西、咀嚼
        // 刻意不断言的冷读音（都只在个别生僻词里出现，收进来只会是噪音）：
        //   数 shuò（数见不鲜） / 券 xuàn（拱券） / 撮 zuǒ（一撮毛） / 宿 xiǔ（一宿）
        // 注：这几个其实**都收在词库里**，只是不作为"必须能用"的底线来断言。
        // 刻意不收的冷读音：数(shuò) 只在「数见不鲜」里出现，
        // 主流输入法打 shuo 也不给「数」，收进来只会是噪音。这里显式记录这个取舍。
        if (HasChar(eng, "shuo", "数")) Console.WriteLine("  [提示] shuo 下出现了「数」——如果是有意收录请忽略");

        Section("单音节排序（必须跟 chars.txt 的常用度一致）");
        // chars.txt 里 ji 的顺序是 几 及 级 计 记 机…，鸡排在后面。
        // 曾经因为单字词拿满 +20000 加成，导致「鸡」压过「几」。
        CheckEq(Top(eng.Compose("ji")), "几", "ji 首选「几」（不是「鸡」）");
        Composition shiC = eng.Compose("shi");
        List<string> shiTop = Texts(shiC.Candidates);
        int iShi = shiTop.IndexOf("时");
        int iShiShi = shiTop.IndexOf("事");
        Check(iShi >= 0 && (iShiShi < 0 || iShi < iShiShi),
              "shi 里「时」排在「事」前面（时=" + iShi + " 事=" + iShiShi + "）");
        CheckEq(Top(eng.Compose("yu")), "于", "yu 首选「于」");
        CheckEq(Top(eng.Compose("he")), "和", "he 首选「和」");
        CheckEq(Top(eng.Compose("yao")), "要", "yao 首选「要」");
        // 单音节首选必须和 chars.txt 的第 0 位一致（抽查若干高频音节）
        string[] probes = new string[] { "shi", "ni", "wo", "de", "le", "bu", "yi", "zai", "you", "ren", "qu", "da" };
        int mismatch = 0;
        foreach (string p in probes)
        {
            List<string> chars = lex.CharsOf(p);
            if (chars == null || chars.Count == 0) continue;
            if (Top(eng.Compose(p)) != chars[0]) { mismatch++; Console.WriteLine("       错位: " + p + " 首选=" + Top(eng.Compose(p)) + " chars.txt[0]=" + chars[0]); }
        }
        Check(mismatch == 0, probes.Length + " 个高频音节的单音节首选都等于 chars.txt 的首位（错位 " + mismatch + " 个）");

        Section("同音词（同码不同词，必须都给出来）");
        Composition hy = eng.Compose("laoshi");
        List<string> hyTexts = Texts(hy.Candidates);
        Console.WriteLine("  laoshi: " + string.Join(" ", hyTexts.ToArray()));
        Check(hyTexts.Contains("老师"), "laoshi 给出「老师」");
        Check(hyTexts.Contains("老实"), "laoshi 也给出「老实」（同音异形词不能丢）");
        CheckEq(Top(hy), "老师", "laoshi 首选「老师」（权重更高）");
        bool allConsumed = true;
        foreach (Candidate cd in hy.Candidates)
            if (cd.Kind == CandidateKind.Word && cd.Consumed != 2) allConsumed = false;
        Check(allConsumed, "同音词候选都消耗 2 个音节（选哪个都不会残留拼音）");

        // 同一读音下的两个词，权重高的排前面
        int wLaoShi = WeightOf(hy, "老师");
        int wLaoShi2 = WeightOf(hy, "老实");
        Check(wLaoShi > wLaoShi2, "「老师」权重高于「老实」（" + wLaoShi + " > " + wLaoShi2 + "）");

        // lu: lv shi 律师 / lv shi 履历？用 li shi 历史/理事 更稳
        Composition hy2 = eng.Compose("lishi");
        Console.WriteLine("  lishi: " + string.Join(" ", Texts(hy2.Candidates).ToArray()));
        Check(hy2.Candidates.Count >= 2, "lishi 给出多个同音候选（实际 " + hy2.Candidates.Count + " 个）");

        Section("尾巴补全");
        Composition c6 = eng.Compose("nihaoma");
        PrintTop(c6, 5);
        CheckEq(Top(c6), "你好吗", "nihaoma 未收录整词时补出 你好吗");

        Section("翻页与选字");
        Composition c7 = eng.Compose("shi");
        Check(c7.Candidates.Count > 5, "shi 的候选多于 5 个（实际 " + c7.Candidates.Count + "）");
        Check(c7.PageCount >= 2, "shi 至少两页（实际 " + c7.PageCount + "）");
        List<Candidate> p0 = c7.Page(0);
        List<Candidate> p1 = c7.Page(1);
        Check(p0.Count == 5 || p0.Count == c7.PageSize, "第一页 5 项");
        Check(p1.Count > 0 && p1[0].Text != p0[0].Text, "第二页首项与第一页不同");

        Section("简拼");
        Composition c8 = eng.Compose("nh");
        PrintTop(c8, 5);
        CheckEq(Top(c8), "你好", "简拼 nh -> 你好");

        Section("非拼音兜底");
        Composition c9 = eng.Compose("qqq");
        Check(c9.Candidates.Count >= 1, "无法切分时有候选（原样直通）");
        CheckEq(Top(c9), "qqq", "非拼音原样输出 qqq");

        Section("联想");
        List<Candidate> pred = eng.Predict("我", 4);
        Console.WriteLine("  联想(我): " + string.Join(" ", Texts(pred).ToArray()));
        Check(pred.Count > 0, "「我」能联想到词");

        Section("英文补全");
        List<Candidate> en = eng.SuggestEnglish("hel", 5);
        Console.WriteLine("  英文补全(hel): " + string.Join(" ", Texts(en).ToArray()));
        if (lex.EnglishCount > 0) Check(en.Count > 0, "hel 能补全出英文词");
        else Console.WriteLine("  [跳过] 还没有 english.txt");

        Section("英文补全（回归：曾经因为按 rank 排序导致前缀查找全废）");
        CheckEn(eng, "th", "the");
        CheckEn(eng, "th", "that");
        CheckEn(eng, "tak", "take");
        CheckEn(eng, "tak", "taken");
        CheckEn(eng, "wor", "work");
        CheckEn(eng, "wor", "world");
        CheckEn(eng, "inf", "information");
        CheckEn(eng, "hel", "hello");
        CheckEn(eng, "so", "sorry");
        CheckEn(eng, "you", "your");
        CheckEn(eng, "up", "update");
        List<Candidate> thTop = eng.SuggestEnglish("th", 8);
        if (thTop.Count > 0)
            CheckEq(thTop[0].Text, "the", "英文补全 th 的首选是 the（最常用）");
        // 精确等于前缀的词由空格直接上屏，不作为补全候选
        List<Candidate> goSelf = eng.SuggestEnglish("go", 8);
        bool hasGo = false;
        foreach (Candidate cd in goSelf) if (cd.Text == "go") hasGo = true;
        Check(!hasGo, "英文补全不把前缀本身当候选（go 不补出 go）");

        Section("模糊音");
        InputSettings fuzzy = new InputSettings();
        fuzzy.FuzzyZhZ = true;
        fuzzy.FuzzyAnAng = true;
        PinyinEngine feng = new PinyinEngine(lex, fuzzy);
        Composition f1 = feng.Compose("zang");
        Check(f1.Candidates.Count > 0, "模糊音开启后 zang 有候选");
        InputSettings strict = new InputSettings();
        strict.FuzzyZhZ = false;
        PinyinEngine seng = new PinyinEngine(lex, strict);
        Composition s1 = seng.Compose("zang");
        Check(s1.Candidates.Count > 0, "模糊音关闭时 zang 仍有候选（张/脏 都在）");

        Section("中文标点");
        int q = 0;
        CheckEq(PinyinEngine.MapPunctuation(',', false, ref q), "，", "逗号 -> ，");
        CheckEq(PinyinEngine.MapPunctuation('.', false, ref q), "。", "句号 -> 。");
        CheckEq(PinyinEngine.MapPunctuation('?', false, ref q), "？", "问号 -> ？");
        CheckEq(PinyinEngine.MapPunctuation('1', true, ref q), "！", "Shift+1 -> ！");
        int q2 = 0;
        CheckEq(PinyinEngine.MapPunctuation('"', false, ref q2), "\u201C", "引号第一次 -> 左引号");
        CheckEq(PinyinEngine.MapPunctuation('"', false, ref q2), "\u201D", "引号第二次 -> 右引号");

        Section("用户学习");
        string code = eng.GuessCode("测试");
        Check(code.Length > 0, "能猜到「测试」的拼音码: " + code);
        int beforeWeight = WeightOf(eng.Compose("ceshi"), "测试");
        lex.Learn("ce shi", "测试");
        lex.Learn("ce shi", "测试");
        InputSettings learnedCfg = new InputSettings();
        learnedCfg.HistoryBoost = 5000;
        eng.UpdateSettings(learnedCfg);
        int afterWeight = WeightOf(eng.Compose("ceshi"), "测试");
        Check(afterWeight > beforeWeight, "学习后「测试」权重上升 (" + beforeWeight + " -> " + afterWeight + ")");
        eng.UpdateSettings(cfg);

        Section("性能");
        DateTime t0 = DateTime.Now;
        for (int i = 0; i < 2000; i++) eng.Compose("zhonghuarenmingongheguo");
        double ms = (DateTime.Now - t0).TotalMilliseconds;
        Console.WriteLine("  2000 次长串组合耗时 " + ms.ToString("F1") + " ms（每次 "
                          + (ms / 2000).ToString("F3") + " ms）");
        Check(ms / 2000 < 5.0, "单次组合 < 5ms（键盘钩子里必须够快）");

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════");
        if (_fail == 0)
        {
            Console.WriteLine("  全部通过：" + _pass + " 项");
            return 0;
        }
        Console.WriteLine("  通过 " + _pass + " 项，失败 " + _fail + " 项");
        return 1;
    }

    /// <summary>同一个字在两个音节下都必须能打出来。</summary>
    static void CheckMulti(PinyinEngine eng, string ch, string sylA, string sylB)
    {
        bool okA = HasChar(eng, sylA, ch);
        bool okB = HasChar(eng, sylB, ch);
        string detail = "「" + ch + "」在 " + sylA + "=" + (okA ? "有" : "无") + "  " + sylB + "=" + (okB ? "有" : "无");
        if (okA && okB) { _pass++; Console.WriteLine("  [OK]   多音字 " + detail); }
        else { _fail++; Console.WriteLine("  [FAIL] 多音字 " + detail); }
    }

    static void CheckEn(PinyinEngine eng, string prefix, string mustHave)
    {
        List<Candidate> hits = eng.SuggestEnglish(prefix, 8);
        bool ok = false;
        List<string> texts = new List<string>();
        foreach (Candidate c in hits) { texts.Add(c.Text); if (c.Text == mustHave) ok = true; }
        if (ok) { _pass++; Console.WriteLine("  [OK]   英文补全 " + prefix + " -> " + mustHave + "  (实际: " + string.Join(" ", texts.ToArray()) + ")"); }
        else { _fail++; Console.WriteLine("  [FAIL] 英文补全 " + prefix + " 应含 " + mustHave + "，实际: " + string.Join(" ", texts.ToArray())); }
    }

    static bool HasChar(PinyinEngine eng, string syllable, string ch)
    {
        Composition c = eng.Compose(syllable);
        foreach (Candidate cd in c.Candidates)
            if (string.Equals(cd.Text, ch, StringComparison.Ordinal)) return true;
        return false;
    }

    static void RankReport(Lexicon lex, PinyinEngine eng, string only)
    {
        string[] tests;
        if (!string.IsNullOrEmpty(only))
        {
            tests = only.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            tests = new string[] {
                "yao", "yu", "shi", "ni", "wo", "de", "le", "bu", "yi", "ji",
                "da", "zhong", "zai", "you", "he", "hui", "ren", "shang", "shi", "qu"
            };
        }

        Console.WriteLine("=== 单音节候选排序 ===");
        foreach (string t in tests)
        {
            Composition c = eng.Compose(t.Trim());
            StringBuilder sb = new StringBuilder();
            sb.Append(t.Trim().PadRight(7));
            for (int i = 0; i < 7 && i < c.Candidates.Count; i++)
            {
                Candidate cd = c.Candidates[i];
                sb.Append(' ').Append(i + 1).Append('.').Append(cd.Text)
                  .Append('[').Append(cd.Kind).Append(':').Append(cd.Weight).Append(']');
            }
            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine();
        Console.WriteLine("=== chars.txt 里的音节顺序（前 12 个）===");
        foreach (string s in new string[] { "yao", "yu", "yi", "shi", "zai", "ji", "qu" })
        {
            List<string> list = lex.CharsOf(s);
            StringBuilder sb = new StringBuilder();
            sb.Append(s.PadRight(6));
            for (int i = 0; i < 12 && list != null && i < list.Count; i++) sb.Append(' ').Append(list[i]);
            Console.WriteLine(sb.ToString());
        }
    }

    static string Top(Composition c)
    {
        if (c.Candidates.Count == 0) return "(无候选)";
        return c.Candidates[0].Text;
    }

    static int WeightOf(Composition c, string text)
    {
        foreach (Candidate cand in c.Candidates)
            if (cand.Text == text) return cand.Weight;
        return int.MinValue;
    }

    static List<string> Texts(List<Candidate> list)
    {
        List<string> r = new List<string>();
        foreach (Candidate c in list) r.Add(c.Text);
        return r;
    }

    static void PrintTop(Composition c, int n)
    {
        StringBuilder sb = new StringBuilder("  候选: ");
        for (int i = 0; i < n && i < c.Candidates.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append((i + 1)).Append('.').Append(c.Candidates[i].Text);
        }
        if (c.Candidates.Count == 0) sb.Append("(空)");
        Console.WriteLine(sb.ToString());
    }
}
