using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MyIme.Shared
{
    public class WordEntry
    {
        public string Word;
        public string Code;        // 空格分隔的音节，如 "ni hao"
        public string Initials;    // 简拼，如 "nh"
        public int Weight;
        public int SyllableCount;

        public override string ToString() { return Word + "(" + Code + "," + Weight + ")"; }
    }

    /// <summary>
    /// 词库：单字表 + 词表 + 英文词表 + 联想索引。
    /// 全部本地加载，不联网。
    /// </summary>
    public class Lexicon
    {
        // 音节 -> 按常用度排序的汉字
        readonly Dictionary<string, List<string>> _chars = new Dictionary<string, List<string>>();
        // "ni hao" -> 词条
        readonly Dictionary<string, List<WordEntry>> _byCode = new Dictionary<string, List<WordEntry>>();
        // "nh" -> 词条（简拼）
        readonly Dictionary<string, List<WordEntry>> _byInitials = new Dictionary<string, List<WordEntry>>();
        // 首字 -> 以该字开头的词（联想）
        readonly Dictionary<string, List<WordEntry>> _predict = new Dictionary<string, List<WordEntry>>();
        // 简拼的所有前缀（"nh" 是 "nhm" 的前缀），用来判断"用户还在敲简拼"
        readonly HashSet<string> _initialsPrefixes = new HashSet<string>(StringComparer.Ordinal);

        // 英文
        // 按**字母序**排列（前缀二分查找要求），_enRank 是并行的常用度排名
        string[] _enWords = new string[0];
        int[] _enRank = new int[0];
        readonly Dictionary<string, int> _userEnglish = new Dictionary<string, int>();

        // 用户学到的词频： code -> (word -> 次数)
        readonly Dictionary<string, Dictionary<string, int>> _learned = new Dictionary<string, Dictionary<string, int>>();

        // 汉字 -> 最常用读音（rank 最小的那个音节）
        readonly Dictionary<char, string> _charSyllable = new Dictionary<char, string>();
        readonly Dictionary<char, int> _charBestRank = new Dictionary<char, int>();

        public int CharCount { get; private set; }
        public int SyllableCount { get { return _chars.Count; } }
        public int WordCount { get; private set; }
        public int EnglishCount { get { return _enWords.Length; } }
        public List<string> LoadWarnings = new List<string>();

        public bool HasSyllable(string s) { return _chars.ContainsKey(s); }

        /// <summary>取一个音节的候选字（按常用度）。</summary>
        public List<string> CharsOf(string syllable)
        {
            List<string> list;
            if (_chars.TryGetValue(syllable, out list)) return list;
            return null;
        }

        public List<WordEntry> WordsOfCode(string code)
        {
            List<WordEntry> list;
            if (_byCode.TryGetValue(code, out list)) return list;
            return null;
        }

        public List<WordEntry> WordsOfInitials(string initials)
        {
            List<WordEntry> list;
            if (_byInitials.TryGetValue(initials, out list)) return list;
            return null;
        }

        /// <summary>所有词条（测试基准和统计用，不参与运行时逻辑）。</summary>
        public List<WordEntry> AllWords()
        {
            List<WordEntry> r = new List<WordEntry>();
            foreach (KeyValuePair<string, List<WordEntry>> kv in _byCode) r.AddRange(kv.Value);
            return r;
        }

        public List<WordEntry> PredictFrom(string firstChar)
        {
            List<WordEntry> list;
            if (_predict.TryGetValue(firstChar, out list)) return list;
            return null;
        }

        public Dictionary<string, Dictionary<string, int>> Learned { get { return _learned; } }

        // ==================== 加载 ====================

        public void LoadAll()
        {
            LoadChars();
            LoadWords();
            LoadEnglish();
            LoadUserWords();
            RebuildIndexes();
            Log.Info("词库加载完成: 音节=" + SyllableCount + " 单字=" + CharCount
                     + " 词=" + WordCount + " 英文=" + EnglishCount);
        }

        void LoadChars()
        {
            string path = AppPaths.ResolveDataFile("chars.txt");
            if (path == null) { LoadWarnings.Add("找不到 chars.txt（单字表）"); return; }
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string syllable = parts[0].ToLowerInvariant();
                List<string> list;
                if (!_chars.TryGetValue(syllable, out list))
                {
                    list = new List<string>();
                    _chars[syllable] = list;
                }
                for (int i = 1; i < parts.Length; i++)
                {
                    string ch = parts[i];
                    if (ch.Length == 0) continue;
                    if (ch.Length != 1) continue;          // 只收单字
                    if (list.Contains(ch)) continue;
                    int rank = list.Count;
                    list.Add(ch);
                    CharCount++;
                    NoteReading(ch[0], syllable, rank);
                }
            }
        }

        /// <summary>记录"某字读某音"；同一个字只保留 rank 最小（最常用）的那次。</summary>
        void NoteReading(char ch, string syllable, int rank)
        {
            int best;
            if (_charBestRank.TryGetValue(ch, out best) && best <= rank) return;
            _charBestRank[ch] = rank;
            _charSyllable[ch] = syllable;
        }

        /// <summary>某个字最常用的读音；查不到返回 null。</summary>
        public string SyllableOf(char ch)
        {
            string s;
            if (_charSyllable.TryGetValue(ch, out s)) return s;
            return null;
        }

        public IEnumerable<string> AllSyllables() { return _chars.Keys; }

        /// <summary>音节 -> 候选字（只读，给引擎用）。</summary>
        public Dictionary<string, List<string>> CharIndex { get { return _chars; } }

        /// <summary>
        /// 词表：words.txt 是自动生成的，words_extra.txt 和 words_daily.txt 是人工精选的补充，都读。
        /// words_daily.txt 专门收日常口语词（聊天、生活场景），用来补内置词库偏书面语的短板。
        /// words_web.txt 是从网络词频表挖出来的 5000+ 常用词（tools/build_web_words.py 生成）。
        /// words_topical.txt 是从 THUOCL（MIT）转来的领域词：成语/地名/饮食/科技/医学/人名等
        /// （tools/build_topical_words.py 生成）。
        /// 分成多个文件是为了让用户能直接往"自己那份"里加词，不用重跑任何脚本，
        /// 程序升级覆盖内置文件时也不会把用户加的词冲掉。
        /// </summary>
        void LoadWords()
        {
            string[] names = new string[] { "words.txt", "words_extra.txt", "words_daily.txt",
                                            "words_web.txt", "words_topical.txt" };
            bool any = false;
            foreach (string name in names)
            {
                string path = AppPaths.ResolveDataFile(name);
                if (path == null) continue;
                any = true;
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    WordEntry e = ParseWordLine(line);
                    if (e == null) continue;
                    // 只有真的新增了才计数，否则同一个词在两份文件里会让规模虚高
                    if (AddWord(_byCode, e.Code, e)) WordCount++;
                }
            }
            if (!any) LoadWarnings.Add("找不到词表文件（words*.txt）");
        }

        /// <summary>解析 "ni hao 你好 9600"。从行尾往前切，避免拼音和词粘连。</summary>
        public static WordEntry ParseWordLine(string line)
        {
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;
            int weight = 0;
            int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out weight);
            string word = parts[parts.Length - 2];
            if (word.Length == 0) return null;
            if (parts.Length - 2 <= 0) return null;
            StringBuilder code = new StringBuilder();
            StringBuilder initials = new StringBuilder();
            for (int i = 0; i < parts.Length - 2; i++)
            {
                if (i > 0) code.Append(' ');
                code.Append(parts[i]);
                initials.Append(parts[i][0]);
            }
            WordEntry e = new WordEntry();
            e.Word = word;
            e.Code = code.ToString();
            e.Initials = initials.ToString();
            e.Weight = weight;
            e.SyllableCount = parts.Length - 2;
            return e;
        }

        /// <summary>
        /// 往索引里加一个词。**同一个码下的同一个词只留一条**，返回是否真的新增了。
        ///
        /// 词库分成好几个文件（words.txt / words_extra / words_daily / words_web / words_topical），
        /// 同一个词被写进两个文件是很正常的（手机 在 words.txt 和 words_daily.txt 里都有）。
        /// 以前直接 list.Add，于是候选条里会出现两个一模一样的「手机」。
        /// 这里按词去重，权重取高的那条。
        /// </summary>
        static bool AddWord(Dictionary<string, List<WordEntry>> map, string key, WordEntry e)
        {
            List<WordEntry> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<WordEntry>();
                map[key] = list;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Word, e.Word, StringComparison.Ordinal))
                {
                    if (e.Weight > list[i].Weight) list[i] = e;
                    return false;
                }
            }
            list.Add(e);
            return true;
        }

        void LoadEnglish()
        {
            List<string> words = new List<string>();
            List<int> ranks = new List<int>();
            // english.txt 是自动生成的；english_extra.txt 是人工精选的补充，
            // 用来补主表漏掉的高频词（information / hello / email 这类）。
            // 两个都读，重复的词在下面排序时按更靠前的排名去重。
            bool anyEn = false;
            foreach (string name in new string[] { "english.txt", "english_extra.txt" })
            {
                string path = AppPaths.ResolveDataFile(name);
                if (path == null) continue;
                anyEn = true;
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1) continue;
                    string w = parts[0].ToLowerInvariant();
                    if (w.Length == 0) continue;
                    int rank = 3000;
                    if (parts.Length > 1) int.TryParse(parts[1], out rank);
                    words.Add(w);
                    ranks.Add(rank == 0 ? 3000 : rank);
                }
            }
            if (!anyEn) LoadWarnings.Add("找不到 english.txt / english_extra.txt（英文词表）");

            // 【必须按字母序排】EnglishPrefix 是用二分查找找前缀的，
            // 之前这里按 rank 排序，二分查找就在一个无序数组上乱跳 ——
            // 表现是 th/tak/wor/inf/bea 全查不到，而 hel/rec/go 又能出结果（纯属撞运气）。
            // 正确的做法：按字母序存（供二分查找），rank 跟着走（供排序输出）。
            int n = words.Count;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, delegate (int a, int b)
            {
                int c = string.CompareOrdinal(words[a], words[b]);
                if (c != 0) return c;
                return ranks[a].CompareTo(ranks[b]);
            });

            List<string> sw = new List<string>(n);
            List<int> sr = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                string w = words[order[i]];
                int r = ranks[order[i]];
                // 同一个词重复出现时只留最常用的那条
                if (sw.Count > 0 && string.Equals(sw[sw.Count - 1], w, StringComparison.Ordinal))
                {
                    if (r < sr[sr.Count - 1]) sr[sr.Count - 1] = r;
                    continue;
                }
                sw.Add(w);
                sr.Add(r);
            }
            _enWords = sw.ToArray();
            _enRank = sr.ToArray();

            LoadUserEnglish();
        }

        void LoadUserEnglish()
        {
            string path = AppPaths.ResolveDataFile("user-english.txt");
            if (path == null) return;
            try
            {
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1) continue;
                    int c = 1;
                    if (parts.Length > 1) int.TryParse(parts[1], out c);
                    string w = parts[0].ToLowerInvariant();
                    int old;
                    _userEnglish.TryGetValue(w, out old);
                    _userEnglish[w] = old + c;
                }
            }
            catch (Exception ex) { Log.Warn("读取 user-english.txt 失败: " + ex.Message); }
        }

        public void LoadUserWords()
        {
            _learned.Clear();
            string path = AppPaths.UserWordsFile;
            if (!File.Exists(path)) return;
            try
            {
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;
                    // 格式: 码 \t 词 \t 次数
                    int count;
                    if (!int.TryParse(parts[parts.Length - 1], out count)) continue;
                    string word = parts[parts.Length - 2];
                    string code = string.Join(" ", parts, 0, parts.Length - 2);
                    AddLearned(code, word, count);
                }
            }
            catch (Exception ex) { Log.Warn("读取用户词库失败: " + ex.Message); }
        }

        void AddLearned(string code, string word, int count)
        {
            Dictionary<string, int> bucket;
            if (!_learned.TryGetValue(code, out bucket))
            {
                bucket = new Dictionary<string, int>();
                _learned[code] = bucket;
            }
            int old;
            bucket.TryGetValue(word, out old);
            bucket[word] = old + count;
        }

        /// <summary>记住一个上屏的词/短语，自动学音节码。</summary>
        public void Learn(string code, string word)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(word)) return;
            AddLearned(code, word, 1);
        }

        public int LearnedCount(string code, string word)
        {
            Dictionary<string, int> bucket;
            if (!_learned.TryGetValue(code, out bucket)) return 0;
            int c;
            bucket.TryGetValue(word, out c);
            return c;
        }

        public void SaveUserWords()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 松林1234输入法 用户词库（自动生成，可手工编辑）");
                sb.AppendLine("# 格式: 音节码 <TAB> 词 <TAB> 使用次数");
                foreach (KeyValuePair<string, Dictionary<string, int>> kv in _learned)
                {
                    foreach (KeyValuePair<string, int> w in kv.Value)
                    {
                        sb.Append(kv.Key).Append('\t').Append(w.Key).Append('\t').Append(w.Value).Append('\n');
                    }
                }
                File.WriteAllText(AppPaths.UserWordsFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("保存用户词库失败", ex); }
        }

        // ==================== 手工加词（设置界面用） ====================

        /// <summary>
        /// 手工往用户词库里加一个词。
        /// 会先校验：音节数必须和字数一致，每个音节都得是词库里认识的字音。
        /// count 给大一点，让它一加进去就排在前面，不用先"用几次"。
        /// </summary>
        public bool AddUserWord(string code, string word, int count)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(word)) return false;
            string[] syls = code.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (syls.Length != word.Length) return false;
            for (int i = 0; i < syls.Length; i++)
                if (!_chars.ContainsKey(syls[i])) return false;
            AddLearned(string.Join(" ", syls), word, count);
            return true;
        }

        /// <summary>从用户词库删掉一个词（所有音节码下的都删）。</summary>
        public int RemoveUserWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return 0;
            int n = 0;
            List<string> empty = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, int>> kv in _learned)
            {
                if (kv.Value.Remove(word)) n++;
                if (kv.Value.Count == 0) empty.Add(kv.Key);
            }
            for (int i = 0; i < empty.Count; i++) _learned.Remove(empty[i]);
            return n;
        }

        /// <summary>列出用户词库，返回 "音节码\t词\t次数" 这样的一行行文本（按词排好序）。</summary>
        public List<string> ListUserWords()
        {
            List<string> rows = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, int>> kv in _learned)
                foreach (KeyValuePair<string, int> w in kv.Value)
                    rows.Add(kv.Key + "\t" + w.Key + "\t" + w.Value);
            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        Dictionary<string, string> _primarySyl;

        /// <summary>
        /// 给一个汉字找它"最常用"的读音。用于手工加词时自动注音：
        /// 同一个字可能出现在多个音节下（多音字），取排在**最靠前**的那个 ——
        /// chars.txt 里每个音节下的字是按常用度排的，越靠前越常用。
        /// </summary>
        public string PrimarySyllableOf(string ch)
        {
            if (string.IsNullOrEmpty(ch)) return null;
            if (_primarySyl == null)
            {
                _primarySyl = new Dictionary<string, string>();
                Dictionary<string, int> best = new Dictionary<string, int>();
                foreach (KeyValuePair<string, List<string>> kv in _chars)
                {
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        string c = kv.Value[i];
                        int old;
                        if (best.TryGetValue(c, out old) && old <= i) continue;
                        best[c] = i;
                        _primarySyl[c] = kv.Key;
                    }
                }
            }
            string s;
            return _primarySyl.TryGetValue(ch, out s) ? s : null;
        }

        /// <summary>把一个词整词自动注音；有任何一个字不认识就返回 null。</summary>
        public string AutoPinyin(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            List<string> parts = new List<string>();
            for (int i = 0; i < word.Length; i++)
            {
                string s = PrimarySyllableOf(word.Substring(i, 1));
                if (s == null) return null;
                parts.Add(s);
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>这串字母是不是某个词的简拼前缀（"n"、"nh" 都是 你好 的前缀）。</summary>
        public bool HasInitialsPrefix(string letters)
        {
            if (string.IsNullOrEmpty(letters)) return false;
            return _initialsPrefixes.Contains(letters);
        }

        void RebuildIndexes()
        {
            _byInitials.Clear();
            _predict.Clear();
            _initialsPrefixes.Clear();

            foreach (KeyValuePair<string, List<WordEntry>> kv in _byCode)
            {
                List<WordEntry> list = kv.Value;
                list.Sort(delegate (WordEntry a, WordEntry b) { return b.Weight.CompareTo(a.Weight); });
                foreach (WordEntry e in list)
                {
                    if (e.SyllableCount >= 2 && e.SyllableCount <= 6)
                        AddWord(_byInitials, e.Initials, e);

                    string first = e.Word.Substring(0, 1);
                    AddWord(_predict, first, e);
                }
            }
            foreach (KeyValuePair<string, List<WordEntry>> kv in _predict)
                kv.Value.Sort(delegate (WordEntry a, WordEntry b) { return b.Weight.CompareTo(a.Weight); });
            foreach (KeyValuePair<string, List<WordEntry>> kv in _byInitials)
                kv.Value.Sort(delegate (WordEntry a, WordEntry b) { return b.Weight.CompareTo(a.Weight); });

            foreach (string initials in _byInitials.Keys)
                for (int len = 1; len <= initials.Length; len++)
                    _initialsPrefixes.Add(initials.Substring(0, len));
        }

        // ==================== 英文查词 ====================

        /// <summary>前缀补全。返回按 rank 排序的候选。</summary>
        public List<KeyValuePair<string, int>> EnglishPrefix(string prefix, int max)
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(prefix)) return result;
            prefix = prefix.ToLowerInvariant();

            // 字母序连续段就是全部同前缀的词，一个都不能漏：
            // 之前只取前 max*3 个就收手，会漏掉字母序靠后但更常用的词。
            int lo = LowerBound(prefix);
            for (int i = lo; i < _enWords.Length; i++)
            {
                if (!_enWords[i].StartsWith(prefix, StringComparison.Ordinal)) break;
                result.Add(new KeyValuePair<string, int>(_enWords[i], _enRank[i]));
                if (result.Count > 3000) break;   // 纯粹防御，正常远到不了
            }
            // 用户自己敲过的英文单词优先级最高
            foreach (KeyValuePair<string, int> kv in _userEnglish)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key != prefix)
                    result.Add(new KeyValuePair<string, int>(kv.Key, -kv.Value));
            }
            result.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return a.Value.CompareTo(b.Value);
            });
            if (result.Count > max) result.RemoveRange(max, result.Count - max);
            return result;
        }

        int LowerBound(string prefix)
        {
            int lo = 0, hi = _enWords.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (string.CompareOrdinal(_enWords[mid], prefix) < 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
