// Dictionary.cpp
#include "Dictionary.h"

#include <mutex>
#include <cstdlib>   // _wtoi
#include <cwctype>   // iswspace

namespace myime {

namespace {

// 标准普通话无调音节表（约 410 个）。用于把连续的拼音串切成音节。
// 如果你做的是五笔/双拼等码表输入法，这张表不参与核心逻辑，
// 只影响「什么时候认为一个键序列敲完了」。
const wchar_t kSyllableTable[] =
    L"a ai an ang ao "
    L"ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu "
    L"ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi chong chou "
    L"chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo "
    L"da dai dan dang dao de dei den deng di dia dian diao die ding diu dong dou du duan dui dun duo "
    L"e ei en eng er "
    L"fa fan fang fei fen feng fo fou fu "
    L"ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo "
    L"ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo "
    L"ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun "
    L"ka kai kan kang kao ke ken keng kong kou ku kua kuai kuan kuang kui kun kuo "
    L"la lai lan lang lao le lei leng li lia lian liang liao lie lin ling liu lo long lou lu luan lue lun luo lv lve "
    L"ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu "
    L"na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou nu nuan nue nuo nv nve "
    L"o ou "
    L"pa pai pan pang pao pei pen peng pi pian piao pie pin ping po pou pu "
    L"qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun "
    L"ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo "
    L"sa sai san sang sao se sen seng sha shai shan shang shao she shei shen sheng shi shou "
    L"shu shua shuai shuan shuang shui shun shuo si song sou su suan sui sun suo "
    L"ta tai tan tang tao te teng ti tian tiao tie ting tong tou tu tuan tui tun tuo "
    L"wa wai wan wang wei wen weng wo wu "
    L"xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun "
    L"ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun "
    L"za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng zhi zhong zhou "
    L"zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui zun zuo";

// 内置兜底词典：dictionary.txt 缺失时也能立刻看到效果。
// 覆盖常用单字 + 少量词组，足以验证「敲键 → 组合 → 候选 → 上屏」整条链路。
const wchar_t* const kBuiltinEntryLines[] = {
    L"ni 你 5000",        L"ni 尼 800",        L"ni 泥 600",        L"ni 拟 500",
    L"ni hao 你好 9000",
    L"hao 好 5000",       L"hao 号 2000",      L"hao 豪 500",
    L"shi 是 9000",       L"shi 时 4000",      L"shi 事 3000",      L"shi 十 2000",
    L"shi 使 1500",       L"shi 市 1500",      L"shi 试 1200",
    L"shu 输 1500",       L"shu 数 2000",      L"shu 书 1800",      L"shu 树 900",
    L"ru 入 1500",
    L"fa 法 2000",        L"fa 发 1800",
    L"shu ru 输入 6000",
    L"shu ru fa 输入法 12000",
    L"zhong 中 8000",     L"zhong 种 2000",    L"zhong 重 1800",
    L"guo 国 6000",       L"guo 过 3000",      L"guo 果 800",
    L"zhong guo 中国 9000",
    L"wen 文 3000",       L"wen 问 2000",      L"wen 温 800",
    L"zi 字 3000",        L"zi 自 3000",       L"zi 子 2500",
    L"wen zi 文字 5000",
    L"ji 机 3000",        L"ji 几 1500",       L"ji 记 1200",       L"ji 计 1200",
    L"qi 器 2000",        L"qi 起 2500",       L"qi 气 1500",
    L"ji qi 机器 4000",
    L"ce 测 2000",        L"ce 策 800",        L"ce shi 测试 5000",
    L"men 们 4000",       L"men 门 2500",      L"ni men 你们 6000",
    L"wo 我 9000",        L"wo 握 400",
    L"ai 爱 3000",        L"ai 矮 300",
    L"xie 写 2000",       L"xie 谢 1500",      L"xie 些 1800",      L"xie xie 谢谢 6000",
    L"zai 在 9000",       L"zai 再 3000",      L"zai jian 再见 6000",
    L"jian 见 3000",      L"jian 件 2000",     L"jian 建 1800",     L"jian 简 1200",
    L"ma 吗 3000",        L"ma 妈 2000",       L"ma 马 1500",
    L"de 的 9000",        L"de 得 2000",       L"de 地 1800",
    L"le 了 9000",        L"le 乐 1500",
    L"you 有 9000",       L"you 又 2500",      L"you 右 1200",      L"you 由 1500",
    L"he 和 8000",        L"he 合 2000",       L"he 河 900",
    L"bu 不 9000",        L"bu 部 2500",       L"bu 步 1500",
    L"shang 上 7000",     L"shang 商 1200",
    L"xia 下 7000",       L"xia 夏 900",
    L"da 大 8000",        L"da 打 3000",
    L"xiao 小 7000",      L"xiao 笑 1200",     L"xiao 校 1500",
    L"dian 电 4000",      L"dian 点 3500",     L"dian 店 900",
    L"nao 脑 2500",       L"nao 闹 500",
    L"dian nao 电脑 6000",
    L"hello hello 3000",
    L"windows Windows 3000",
};

std::vector<std::wstring> SplitBySpace(const std::wstring& text) {
    std::vector<std::wstring> parts;
    size_t i = 0;
    while (i < text.size()) {
        while (i < text.size() && iswspace(text[i])) ++i;
        const size_t begin = i;
        while (i < text.size() && !iswspace(text[i])) ++i;
        if (i > begin) parts.push_back(text.substr(begin, i - begin));
    }
    return parts;
}

std::wstring JoinCodes(const std::vector<std::wstring>& parts) {
    std::wstring joined;
    for (const auto& part : parts) joined += part;
    return joined;
}

bool IsAllDigits(const std::wstring& s) {
    if (s.empty()) return false;
    for (wchar_t c : s) {
        if (c < L'0' || c > L'9') return false;
    }
    return true;
}

// 「码 token」的判定：纯 ASCII 字母/数字/单引号。
// 汉字、标点等一律视为词的开始 —— 这是区分 "码" 与 "词" 的关键规则。
bool IsCodeToken(const std::wstring& s) {
    if (s.empty()) return false;
    for (wchar_t c : s) {
        const bool ok = (c >= L'a' && c <= L'z') || (c >= L'A' && c <= L'Z') ||
                        (c >= L'0' && c <= L'9') || c == L'\'';
        if (!ok) return false;
    }
    return true;
}

// 统一的解析规则（词典文件与内置词典共用，保证行为一致）：
//   <码(可含多个空格分隔的键)> <词> [权重]
//   若整行都是码 token，则最后一个 token 视为词（支持 "hello hello 3000" 这种写法）
bool ParseEntryLine(const std::wstring& line, std::vector<std::wstring>& keys, std::wstring& word,
                    int& weight) {
    std::vector<std::wstring> tokens = SplitBySpace(line);
    if (tokens.size() < 2) return false;

    if (IsAllDigits(tokens.back())) {
        weight = _wtoi(tokens.back().c_str());
        tokens.pop_back();
    }
    if (tokens.size() < 2) return false;

    // 前导的码 token
    size_t split = 0;
    while (split < tokens.size() && IsCodeToken(tokens[split])) ++split;

    if (split == tokens.size()) {
        // 整行都是码 → 最后一个留作词
        split = tokens.size() - 1;
    }
    if (split < 1) return false;

    keys.assign(tokens.begin(), tokens.begin() + static_cast<long>(split));
    word.clear();
    for (size_t i = split; i < tokens.size(); ++i) {
        if (!word.empty()) word += L' ';
        word += tokens[i];
    }
    return !word.empty();
}

Dictionary BuildBuiltinDictionary() {
    Dictionary dict;
    dict.AddSyllableList(kSyllableTable);
    for (const wchar_t* line : kBuiltinEntryLines) {
        std::vector<std::wstring> keys;
        std::wstring word;
        int weight = 0;
        if (ParseEntryLine(line, keys, word, weight)) {
            dict.AddEntry(keys, word, weight);
        }
    }
    return dict;
}

}  // namespace

void Dictionary::Clear() {
    entries_.clear();
    index_.clear();
    syllables_.clear();
}

void Dictionary::AddSyllableList(const std::wstring& spaceSeparated) {
    for (const auto& token : SplitBySpace(spaceSeparated)) {
        if (!token.empty()) syllables_.insert(token);
    }
}

bool Dictionary::IsSyllable(const std::wstring& token) const {
    return syllables_.find(token) != syllables_.end();
}

void Dictionary::AddEntry(const std::vector<std::wstring>& codeKeys, const std::wstring& word,
                          int weight) {
    if (codeKeys.empty() || word.empty()) return;

    Entry entry;
    entry.codeKeys = codeKeys;
    entry.codeJoined = JoinCodes(codeKeys);
    entry.word = word;
    entry.weight = weight;
    if (entry.codeJoined.empty()) return;

    const size_t index = entries_.size();
    entries_.push_back(std::move(entry));
    index_[entries_[index].codeJoined].push_back(index);

    // 自定义码表：出现过的键自动进入键集合，Engine 才能据此切分
    for (const auto& key : codeKeys) syllables_.insert(key);
}

void Dictionary::LoadFromString(const std::string& utf8) {
    if (utf8.empty()) return;

    const std::wstring content = Utf8ToWide(utf8);
    size_t pos = 0;
    while (pos < content.size()) {
        size_t end = content.find(L'\n', pos);
        if (end == std::wstring::npos) end = content.size();

        const std::wstring line = Trim(content.substr(pos, end - pos));
        pos = end + 1;

        if (line.empty() || line[0] == L'#') continue;

        std::vector<std::wstring> keys;
        std::wstring word;
        int weight = 0;
        if (ParseEntryLine(line, keys, word, weight)) {
            AddEntry(keys, word, weight);
        }
    }
}

bool Dictionary::LoadFromFile(const std::wstring& path) {
    HANDLE file = ::CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                                FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;

    LARGE_INTEGER size = {};
    if (!::GetFileSizeEx(file, &size) || size.QuadPart <= 0 || size.QuadPart > (64LL << 20)) {
        ::CloseHandle(file);
        return false;
    }

    std::string buffer(static_cast<size_t>(size.QuadPart), '\0');
    DWORD read = 0;
    const BOOL ok = ::ReadFile(file, &buffer[0], static_cast<DWORD>(buffer.size()), &read, nullptr);
    ::CloseHandle(file);
    if (!ok) return false;
    buffer.resize(read);

    LoadFromString(buffer);
    return !Empty();
}

void Dictionary::LookupExact(const std::wstring& codeJoined, std::vector<Candidate>& out,
                             size_t maxCount) const {
    const auto it = index_.find(codeJoined);
    if (it == index_.end()) return;

    const size_t begin = out.size();
    for (size_t index : it->second) {
        const Entry& entry = entries_[index];
        Candidate candidate;
        candidate.text = entry.word;
        candidate.comment = entry.codeJoined;
        candidate.score = entry.weight;
        out.push_back(std::move(candidate));
    }

    // 只对本次追加的区间排序；out 里原有的内容由调用方负责
    std::stable_sort(out.begin() + static_cast<std::ptrdiff_t>(begin), out.end(),
                     [](const Candidate& a, const Candidate& b) { return a.score > b.score; });
    if (out.size() > begin + maxCount) out.resize(begin + maxCount);
}

void Dictionary::LookupPrefix(const std::wstring& prefix, std::vector<Candidate>& out,
                              size_t maxCount) const {
    if (prefix.empty()) return;

    const size_t begin = out.size();

    // std::map 有序：lower_bound 定位到第一个 >= prefix 的码，再顺序扫到不再是前缀为止。
    size_t scanned = 0;
    const size_t kScanLimit = 512;  // 骨架用线性扫描；加个上限防止极端输入卡顿

    for (auto it = index_.lower_bound(prefix); it != index_.end() && scanned < kScanLimit;
         ++it, ++scanned) {
        if (it->first.size() < prefix.size() || it->first.compare(0, prefix.size(), prefix) != 0) {
            break;
        }
        for (size_t index : it->second) {
            const Entry& entry = entries_[index];
            Candidate candidate;
            candidate.text = entry.word;
            candidate.comment = entry.codeJoined;
            // 候选窗顺序：码越短（离打完越近）越靠前，权重次之
            candidate.score =
                entry.weight - static_cast<int>(entry.codeJoined.size() - prefix.size()) * 200;
            out.push_back(std::move(candidate));
        }
    }

    std::stable_sort(out.begin() + static_cast<std::ptrdiff_t>(begin), out.end(),
                     [](const Candidate& a, const Candidate& b) { return a.score > b.score; });
    if (out.size() > begin + maxCount) out.resize(begin + maxCount);
}

const Dictionary& SharedDictionary() {
    static std::once_flag once;
    static Dictionary dict;

    std::call_once(once, []() {
        dict.AddSyllableList(kSyllableTable);

        // 词典查找顺序：
        //   1) DLL 同目录 dictionary.txt（安装脚本会把 data 目录内容拷到这儿）
        //   2) DLL 上一级的 data\dictionary.txt（开发时直接跑构建产物）
        //   3) 内置兜底词典
        const std::wstring dir = GetModuleDirectory();
        const std::wstring paths[] = {dir + L"dictionary.txt", dir + L"..\\data\\dictionary.txt"};

        bool loaded = false;
        for (const auto& path : paths) {
            if (dict.LoadFromFile(path)) {
                LogDebug(L"[MyIME] 词典已加载: %s（%zu 条）", path.c_str(), dict.EntryCount());
                loaded = true;
                break;
            }
        }
        if (!loaded) {
            dict = BuildBuiltinDictionary();
            LogDebug(L"[MyIME] 未找到 dictionary.txt，使用内置兜底词典（%zu 条）", dict.EntryCount());
        }
    });
    return dict;
}

}  // namespace myime
