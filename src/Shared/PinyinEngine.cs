using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MyIme.Shared
{
    public enum CandidateKind
    {
        Word = 0,        // 词库整词
        Sentence = 1,    // 逐字拼接
        Char = 2,        // 单字
        Jianpin = 3,     // 简拼
        Predict = 4,     // 上屏后联想
        English = 5,     // 英文补全
        Raw = 6,         // 原样（不是合法拼音，兜底直通）
        Punctuation = 7
    }

    public class Candidate
    {
        public string Text;
        public CandidateKind Kind;
        public int Weight;          // 越大越靠前
        public string Code;         // 产生它的音节码（用于学习）
        public int Consumed;        // 消耗了几个完整音节
        public string Note;         // 右侧小字提示

        public Candidate() { }

        public Candidate(string text, CandidateKind kind, int weight)
        {
            Text = text;
            Kind = kind;
            Weight = weight;
        }

        public override string ToString() { return Text; }
    }

    /// <summary>一次组合的完整状态。</summary>
    public class Composition
    {
        public string Raw = "";                  // 用户敲的原串
        public List<string> Syllables = new List<string>();
        public string PartialTail = "";          // 末尾没敲完的音节
        public bool IsValid;                     // 至少能切出一个音节，或者是简拼
        public List<Candidate> Candidates = new List<Candidate>();
        public int PageSize = 5;

        public string SyllableText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Syllables.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(Syllables[i]);
                }
                if (PartialTail.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(PartialTail);
                }
                return sb.ToString();
            }
        }

        public int PageCount
        {
            get
            {
                if (Candidates.Count == 0) return 0;
                return (Candidates.Count + PageSize - 1) / PageSize;
            }
        }

        public List<Candidate> Page(int page)
        {
            List<Candidate> r = new List<Candidate>();
            int start = page * PageSize;
            for (int i = start; i < start + PageSize && i < Candidates.Count; i++) r.Add(Candidates[i]);
            return r;
        }
    }

    /// <summary>
    /// 拼音输入引擎。纯逻辑，不碰 Win32，可以单测。
    ///
    /// 候选来源（按优先级）：
    ///   1. 整串精确匹配的词
    ///   2. 短词 + 剩余音节用首选字补齐   （"nihaoma" 没整词时补出 你好吗）
    ///   3. 逐字拼接
    ///   4. 首音节的单字（后面按首选字补齐）
    ///   5. 简拼（nh -> 你好）
    /// </summary>
    public class PinyinEngine
    {
        readonly Lexicon _lex;
        InputSettings _cfg;

        readonly HashSet<string> _syllables = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _prefixes = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> _fuzzyCache = new Dictionary<string, string[]>();

        public PinyinEngine(Lexicon lex, InputSettings cfg)
        {
            _lex = lex;
            _cfg = cfg;
            LoadSyllables();
        }

        public Lexicon LexiconOf { get { return _lex; } }

        public void UpdateSettings(InputSettings cfg) { _cfg = cfg; }

        void LoadSyllables()
        {
            string path = AppPaths.ResolveDataFile("syllables.txt");
            if (path != null)
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim().ToLowerInvariant();
                    if (line.Length == 0 || line[0] == '#') continue;
                    foreach (string token in line.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                        _syllables.Add(token);
                }
            }
            foreach (string s in _lex.AllSyllables()) _syllables.Add(s);

            foreach (string s in _syllables)
                for (int len = 1; len <= s.Length; len++)
                    _prefixes.Add(s.Substring(0, len));
        }

        public bool IsSyllable(string s) { return s.Length > 0 && _syllables.Contains(s); }
        public bool IsSyllablePrefix(string s) { return s.Length > 0 && _prefixes.Contains(s); }
        public int SyllableInventory { get { return _syllables.Count; } }

        // ==================== 切分 ====================

        public void Segment(string raw, Composition into)
        {
            into.Raw = raw;
            into.Syllables.Clear();
            into.PartialTail = "";

            string lower = raw.ToLowerInvariant();

            if (lower.IndexOf('\'') >= 0)
            {
                string[] parts = lower.Split(new char[] { '\'' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    bool isLast = (p == parts.Length - 1);
                    if (SegmentPlain(parts[p], isLast, into) == null) return;
                }
                return;
            }
            SegmentPlain(lower, true, into);
        }

        /// <summary>DP 最大化 sum(len^2)：天然偏好长音节（xian 而不是 xi+an）。</summary>
        List<string> SegmentPlain(string s, bool isLast, Composition into)
        {
            int n = s.Length;
            if (n == 0) return new List<string>();

            int[] best = new int[n + 1];
            int[] choice = new int[n + 1];
            for (int i = 0; i <= n; i++) { best[i] = -1; choice[i] = 0; }
            best[0] = 0;

            for (int i = 1; i <= n; i++)
            {
                int maxLen = Math.Min(6, i);
                for (int len = 1; len <= maxLen; len++)
                {
                    int prev = i - len;
                    if (best[prev] < 0) continue;
                    string piece = s.Substring(prev, len);
                    if (!_syllables.Contains(piece)) continue;
                    int score = best[prev] + len * len * 10 + len;
                    if (score > best[i]) { best[i] = score; choice[i] = len; }
                }
            }

            int end = n;
            while (end > 0 && best[end] < 0) end--;

            List<string> result = new List<string>();
            int cur = end;
            while (cur > 0)
            {
                int len = choice[cur];
                if (len <= 0) break;
                result.Add(s.Substring(cur - len, len));
                cur -= len;
            }
            result.Reverse();
            foreach (string syl in result) into.Syllables.Add(syl);

            if (end < n)
            {
                string tail = s.Substring(end);
                if (isLast)
                {
                    into.PartialTail = tail;
                }
                else
                {
                    into.Syllables.Clear();
                    into.PartialTail = s;
                    return null;
                }
            }
            return result;
        }

        /// <summary>
        /// 简拼（首字母）匹配的最低词权重。
        /// 简拼是给常用词用的缩写捷径：nh -> 你好。词库变大以后，
        /// 生僻词的首字母会和用户想打的字母串撞车（qqq -> 前驱期），所以卡一条线。
        /// </summary>
        public const int JianpinMinWeight = 6000;

        // ==================== 候选 ====================

        public Composition Compose(string raw)
        {
            Composition c = new Composition();
            c.PageSize = Math.Max(1, _cfg.PageSize);
            Segment(raw, c);

            string letters = OnlyLetters(raw);

            if (c.Syllables.Count == 0)
            {
                bool added = false;
                if (_cfg.Jianpin && letters.Length >= 2)
                {
                    List<WordEntry> byInit = _lex.WordsOfInitials(letters);
                    if (byInit != null)
                    {
                        foreach (WordEntry e in byInit)
                        {
                            if (e.Weight < JianpinMinWeight) continue;
                            c.Candidates.Add(MakeCandidate(e, CandidateKind.Jianpin, 5000));
                        }
                        added = c.Candidates.Count > 0;
                    }
                }
                // 原样直通**永远**加进来，不再"简拼匹配到了就不给"。
                // 否则像 qqq 这种串会被某个生僻词的简拼完全顶掉，用户想打字母都打不出来。
                // 权重给 300：排在常规简拼后面，但只要简拼没命中（或命中的都是生僻词）
                // 它就是第一个 —— 这也正是"打不出拼音就原样上屏"的本意。
                Candidate rawC = new Candidate(raw, CandidateKind.Raw, 300);
                rawC.Code = letters;
                rawC.Consumed = 0;
                c.Candidates.Add(rawC);
                if (added) { /* 简拼候选已在上面加入 */ }
                c.IsValid = true;
                Finish(c);
                return c;
            }

            c.IsValid = true;
            Dictionary<string, Candidate> pool = new Dictionary<string, Candidate>(StringComparer.Ordinal);

            int S = c.Syllables.Count;
            string fullCode = JoinCode(c.Syllables, S);

            // ---- 1. 整词精确匹配 ----
            List<WordEntry> exact = _lex.WordsOfCode(fullCode);
            if (exact != null)
            {
                foreach (WordEntry e in exact)
                {
                    // 单音节输入 + 单字词：排序交给 chars.txt。
                    // 那张表本来就是逐音节按常用度排过的，比词表里顺带收进来的单字权威。
                    // 如果这里照给 +20000，会出现"鸡"压过"几"、"事"压过"时"、"鱼"压过"于"
                    // 这类明显错位 —— 单音节输入是最高频的操作，错位很扎眼。
                    if (S == 1 && e.Word.Length == 1)
                    {
                        int rank = RankInSyllable(c.Syllables[0], e.Word);
                        int w = (rank >= 0) ? (15000 - rank * 30 + 5) : (6000 + e.Weight / 10);
                        Candidate single = new Candidate(e.Word, CandidateKind.Word, w);
                        single.Code = e.Code;
                        single.Consumed = 1;
                        Offer(pool, single);
                        continue;
                    }
                    Offer(pool, MakeCandidate(e, CandidateKind.Word, 20000));
                }
            }

            // ---- 2. 短词 + 尾巴补全 ----
            int maxK = Math.Min(S - 1, Math.Max(1, _cfg.MaxWordLength - 1));
            int tried = 0;
            for (int k = maxK; k >= 1 && tried < 3; k--)
            {
                string code = JoinCode(c.Syllables, k);
                List<WordEntry> heads = _lex.WordsOfCode(code);
                if (heads == null) continue;
                tried++;
                string tail = TailFirstChars(c.Syllables, k, S);
                if (tail == null) continue;
                int n = Math.Min(heads.Count, 8);
                for (int i = 0; i < n; i++)
                {
                    WordEntry e = heads[i];
                    Candidate cand = new Candidate(e.Word + tail, CandidateKind.Word, e.Weight + 10000 - i * 20);
                    // 这个候选把"前 k 个音节组成词 + 后面每个音节补一个字"全都覆盖了，
                    // 所以它消耗的是**全部** S 个音节，不是 k 个。
                    // 写成 k 会导致上屏后缓冲区里还残留后面的音节（"zhongguo" 上屏后剩个 "guo"）。
                    cand.Code = fullCode;
                    cand.Consumed = S;
                    Offer(pool, cand);
                }
            }

            // ---- 3. 逐字拼接 ----
            if (S <= Math.Max(2, _cfg.MaxWordLength))
            {
                StringBuilder sb = new StringBuilder();
                int total = 0;
                bool ok = true;
                for (int i = 0; i < S; i++)
                {
                    string ch = FirstChar(c.Syllables[i]);
                    if (ch == null) { ok = false; break; }
                    sb.Append(ch);
                    total += 100 - i;
                }
                if (ok && sb.Length > 0)
                {
                    Candidate sc = new Candidate(sb.ToString(), CandidateKind.Sentence, 8000 + total);
                    sc.Code = fullCode;
                    sc.Consumed = S;
                    Offer(pool, sc);
                }
            }

            // ---- 4. 首音节候选字 + 后面音节用首选字补齐（覆盖整个缓冲区）----
            List<string> firstChars = CharsOfSyllable(c.Syllables[0]);
            if (firstChars != null)
            {
                string tailAfter = S > 1 ? TailFirstChars(c.Syllables, 1, S) : "";
                int limit = (S == 1) ? Math.Min(firstChars.Count, 60) : 8;
                for (int i = 0; i < limit && i < firstChars.Count; i++)
                {
                    string text = firstChars[i] + (tailAfter == null ? "" : tailAfter);
                    int w = (S == 1 ? 15000 : 9000) - i * 30;
                    Candidate cc = new Candidate(text, CandidateKind.Char, w);
                    cc.Code = (S == 1) ? c.Syllables[0] : fullCode;
                    // 尾巴把后面**所有**音节都补齐了，所以这个候选覆盖整个缓冲区，消耗 S 个音节。
                    // 之前写 1，会导致上屏后缓冲区里残留后面的音节。
                    cc.Consumed = S;
                    Offer(pool, cc);
                }

                // ---- 4b. 只要首音节这一个字，剩下的音节留着继续选 ----
                if (S > 1)
                {
                    int limit2 = Math.Min(firstChars.Count, 8);
                    for (int i = 0; i < limit2; i++)
                    {
                        Candidate only = new Candidate(firstChars[i], CandidateKind.Char, 6000 - i * 30);
                        only.Code = c.Syllables[0];
                        only.Consumed = 1;
                        Offer(pool, only);
                    }
                }
            }

            // ---- 5. 简拼 ----
            // 条件从 "S >= 2" 放宽成"只要输入不是一条完整的拼音"。
            //
            // 原来要求至少切出两个音节，于是 aq / els / zel / dec 这类永远走不到这里：
            // 它们的第一个字母（a、e、o）本身就是合法音节，S 正好是 1，
            // 剩下的字母成了"没敲完的尾巴"。用户想打 安全(an quan)、俄罗斯(e luo si)、
            // 周恩来(zhou en lai)，得到的却只有「啊」「额」——只覆盖了前半截。
            //
            // 判断依据用 PartialTail：它非空就说明这条拼音读不通，简拼才是唯一说得通的解释。
            bool hasLeftover = c.PartialTail != null && c.PartialTail.Length > 0;
            if (_cfg.Jianpin && letters.Length >= 2 && letters.Length == raw.Length)
            {
                List<WordEntry> byInit = _lex.WordsOfInitials(letters);
                if (byInit != null && letters != fullCode.Replace(" ", ""))
                {
                    // 读不通的时候加权更多：否则简拼候选压不过那些只解释了半截的拼读候选
                    // （它们的单字候选权重是 15000 一档）。
                    int bonus = hasLeftover ? 20000 : 5000;
                    int shown = 0;
                    for (int i = 0; i < byInit.Count && shown < 6; i++)
                    {
                        // 简拼是"常用词的缩写快捷键"，生僻词不该靠首字母抢候选。
                        // 加了领域词库之后，像「前驱期」(qqq) 这种词会把用户想打的字母顶掉。
                        if (byInit[i].Weight < JianpinMinWeight) continue;
                        Offer(pool, MakeCandidate(byInit[i], CandidateKind.Jianpin, bonus - shown * 20));
                        shown++;
                    }
                }
            }

            foreach (KeyValuePair<string, Candidate> kv in pool) c.Candidates.Add(kv.Value);
            Finish(c);
            return c;
        }

        void Finish(Composition c)
        {
            if (_cfg.HistoryBoost > 0)
            {
                foreach (Candidate cand in c.Candidates)
                {
                    int learned = _lex.LearnedCount(cand.Code, cand.Text);
                    if (learned > 0) cand.Weight += Math.Min(learned, 20) * _cfg.HistoryBoost;
                }
            }
            c.Candidates.Sort(delegate (Candidate a, Candidate b)
            {
                int r = b.Weight.CompareTo(a.Weight);
                if (r != 0) return r;
                return string.CompareOrdinal(a.Text, b.Text);
            });
        }

        static void Offer(Dictionary<string, Candidate> pool, Candidate cand)
        {
            if (cand == null || string.IsNullOrEmpty(cand.Text)) return;
            Candidate old;
            if (pool.TryGetValue(cand.Text, out old))
            {
                if (cand.Weight > old.Weight) pool[cand.Text] = cand;
                return;
            }
            pool[cand.Text] = cand;
        }

        /// <summary>某个字在指定音节的候选字表里排第几；不在表里返回 -1。</summary>
        int RankInSyllable(string syllable, string ch)
        {
            List<string> list = _lex.CharsOf(syllable);
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], ch, StringComparison.Ordinal)) return i;
            return -1;
        }

        static Candidate MakeCandidate(WordEntry e, CandidateKind kind, int bonus)
        {
            Candidate c = new Candidate(e.Word, kind, e.Weight + bonus);
            c.Code = e.Code;
            c.Consumed = e.SyllableCount;
            return c;
        }

        static string JoinCode(List<string> syllables, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count && i < syllables.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(syllables[i]);
            }
            return sb.ToString();
        }

        string TailFirstChars(List<string> syllables, int from, int to)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < to; i++)
            {
                string ch = FirstChar(syllables[i]);
                if (ch == null) return null;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        string FirstChar(string syllable)
        {
            List<string> list = CharsOfSyllable(syllable);
            if (list == null || list.Count == 0) return null;
            return list[0];
        }

        List<string> CharsOfSyllable(string syllable)
        {
            List<string> main = _lex.CharsOf(syllable);
            string[] variants = FuzzyVariants(syllable);
            if (variants.Length == 0) return main;

            List<string> merged = new List<string>();
            if (main != null) merged.AddRange(main);
            Dictionary<string, int> seen = new Dictionary<string, int>();
            for (int i = 0; i < merged.Count; i++) seen[merged[i]] = i;
            foreach (string v in variants)
            {
                List<string> extra = _lex.CharsOf(v);
                if (extra == null) continue;
                foreach (string ch in extra)
                {
                    if (seen.ContainsKey(ch)) continue;
                    seen[ch] = merged.Count;
                    merged.Add(ch);
                }
            }
            return merged;
        }

        string[] FuzzyVariants(string syllable)
        {
            string[] cached;
            if (_fuzzyCache.TryGetValue(syllable, out cached)) return cached;

            List<string> outList = new List<string>();
            CollectVariants(syllable, outList, 0);
            outList.Remove(syllable);
            cached = outList.ToArray();
            _fuzzyCache[syllable] = cached;
            return cached;
        }

        void CollectVariants(string s, List<string> acc, int depth)
        {
            if (depth > 2 || acc.Count > 8) return;
            if (acc.Contains(s)) return;
            acc.Add(s);
            foreach (string v in OneStepVariants(s))
            {
                if (!_syllables.Contains(v)) continue;
                CollectVariants(v, acc, depth + 1);
            }
        }

        IEnumerable<string> OneStepVariants(string s)
        {
            if (_cfg.FuzzyZhZ)
            {
                if (s.StartsWith("zh", StringComparison.Ordinal)) yield return "z" + s.Substring(2);
                else if (s.StartsWith("z", StringComparison.Ordinal)) yield return "zh" + s.Substring(1);
            }
            if (_cfg.FuzzyChC)
            {
                if (s.StartsWith("ch", StringComparison.Ordinal)) yield return "c" + s.Substring(2);
                else if (s.StartsWith("c", StringComparison.Ordinal)) yield return "ch" + s.Substring(1);
            }
            if (_cfg.FuzzyShS)
            {
                if (s.StartsWith("sh", StringComparison.Ordinal)) yield return "s" + s.Substring(2);
                else if (s.StartsWith("s", StringComparison.Ordinal)) yield return "sh" + s.Substring(1);
            }
            if (_cfg.FuzzyNL)
            {
                if (s.StartsWith("n", StringComparison.Ordinal)) yield return "l" + s.Substring(1);
                else if (s.StartsWith("l", StringComparison.Ordinal)) yield return "n" + s.Substring(1);
            }
            if (_cfg.FuzzyUanUang)
            {
                if (s.EndsWith("uang", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 4) + "uan";
                else if (s.EndsWith("uan", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 3) + "uang";
            }
            if (_cfg.FuzzyAnAng)
            {
                if (s.EndsWith("ang", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 3) + "an";
                else if (s.EndsWith("an", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 2) + "ang";
            }
            if (_cfg.FuzzyInIng)
            {
                if (s.EndsWith("ing", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 3) + "in";
                else if (s.EndsWith("in", StringComparison.Ordinal)) yield return s.Substring(0, s.Length - 2) + "ing";
            }
        }

        // ==================== 联想 ====================

        public List<Candidate> Predict(string committedText, int count)
        {
            List<Candidate> r = new List<Candidate>();
            if (!_cfg.PredictEnabled || count <= 0) return r;
            if (string.IsNullOrEmpty(committedText)) return r;

            string key = committedText.Substring(committedText.Length - 1, 1);
            List<WordEntry> list = _lex.PredictFrom(key);
            if (list == null) return r;
            for (int i = 0; i < list.Count && r.Count < count; i++)
            {
                WordEntry e = list[i];
                if (e.Word.Length <= 1) continue;
                if (e.Word == committedText) continue;
                Candidate cand = new Candidate(e.Word, CandidateKind.Predict, e.Weight + 30000);
                cand.Code = "";
                cand.Consumed = 0;
                cand.Note = "联想";
                r.Add(cand);
            }
            return r;
        }

        // ==================== 英文 ====================

        public List<Candidate> SuggestEnglish(string prefix, int count)
        {
            List<Candidate> r = new List<Candidate>();
            if (!_cfg.EnglishCompletion) return r;
            if (prefix == null) return r;
            int min = Math.Max(1, _cfg.EnglishMinPrefix);
            if (prefix.Length < min) return r;
            if (!IsAllAsciiLetters(prefix)) return r;

            List<KeyValuePair<string, int>> hits = _lex.EnglishPrefix(prefix, Math.Max(count * 2, 10));
            foreach (KeyValuePair<string, int> kv in hits)
            {
                if (r.Count >= count) break;
                if (string.Equals(kv.Key, prefix, StringComparison.OrdinalIgnoreCase)) continue;
                Candidate cand = new Candidate(kv.Key, CandidateKind.English, 20000 - kv.Value);
                cand.Code = prefix;
                cand.Consumed = 0;
                cand.Note = "英文";
                r.Add(cand);
            }
            return r;
        }

        // ==================== 中文标点 ====================

        /// <summary>中文标点映射。中文引号按奇偶交替。返回 null 表示不改。</summary>
        public static string MapPunctuation(char ch, bool shift, ref int quoteState)
        {
            if (shift)
            {
                switch (ch)
                {
                    case '1': return "！";
                    case '2': return "@";
                    case '3': return "#";
                    case '4': return "￥";
                    case '5': return "%";
                    case '6': return "……";
                    case '7': return "&";
                    case '8': return "*";
                    case '9': return "（";
                    case '0': return "）";
                    case '-': return "——";
                    case '=': return "+";
                    case '[': return "【";
                    case ']': return "】";
                    case ';': return "：";
                    case '\'': return "”";
                    case ',': return "《";
                    case '.': return "》";
                    case '/': return "？";
                    case '\u0060': return "～";
                }
                return null;
            }
            switch (ch)
            {
                case ',': return "，";
                case '.': return "。";
                case '?': return "？";
                case '!': return "！";
                case ':': return "：";
                case ';': return "；";
                case '(': return "（";
                case ')': return "）";
                case '<': return "《";
                case '>': return "》";
                case '[': return "【";
                case ']': return "】";
                case '\\': return "、";
                case '/': return "、";
                case '@': return "@";
                case '$': return "￥";
                case '^': return "……";
                case '_': return "——";
                case '~': return "～";
                case '\u0060': return "·";
                case '-': return "-";
                case '\'':
                    quoteState = 1 - quoteState;
                    return quoteState == 1 ? "‘" : "’";
                case '"':
                    quoteState = 1 - quoteState;
                    return quoteState == 1 ? "" + (char)0x201C : "" + (char)0x201D;
                default: return null;
            }
        }

        // ==================== 工具 ====================

        public static string OnlyLetters(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append(c);
                else if (c >= 'A' && c <= 'Z') sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        public static bool IsAllAsciiLetters(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!ok) return false;
            }
            return true;
        }

        public static bool IsComposableKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!letter && c != '\'') return false;
            }
            return true;
        }

        /// <summary>给一段汉字猜拼音码（学用户词用）。有字查不到就返回空串。</summary>
        public string GuessCode(string text)
        {
            List<string> syl = new List<string>();
            foreach (char ch in text)
            {
                string s = _lex.SyllableOf(ch);
                if (s == null) return "";
                syl.Add(s);
            }
            return string.Join(" ", syl.ToArray());
        }
    }
}
