// Dictionary.h —— 码表/词典
//
// 设计要点：
//   1) 词典本身就是「码表」：code（按键序列）→ word（上屏文字）。
//      拼音只是 code = 拼音串 的特例；换成五笔/双拼/自定义码表，
//      只要把 code 换成对应的键序列，本类与 Engine 的流程完全复用。
//   2) 文件里 code 用空格分隔音节（"ni hao 你好 100"），
//      内存里同时保存 joined 形式（"nihao"）与音节切分，便于：
//        - 用完整输入串直接查整词
//        - 用音节序列做组合候选
//   3) 查询用 std::map 的 lower_bound 做前缀扫描：骨架够用，
//      词库上百万条时应换成 Trie（见设计文档「引擎扩展」）。
#pragma once

#include "Common.h"

namespace myime {

// 一个候选词
struct Candidate {
    std::wstring text;      // 上屏文字
    std::wstring comment;   // 候选窗里的备注（拼音、来源等）
    int score = 0;          // 排序分：越大越靠前
};

class Dictionary {
public:
    void Clear();

    // 从 UTF-8 文本文件加载；文件缺失返回 false（调用方决定是否用内置兜底）
    bool LoadFromFile(const std::wstring& path);

    // 从内存中的 UTF-8 文本加载。
    // 行格式（# 开头为注释，空行忽略）：
    //     code...  word  [weight]
    //   code 可以是一个或多个空格分隔的键（拼音音节 / 码表字母）
    //   例：ni 你 1000
    //       ni hao 你好 2000
    void LoadFromString(const std::string& utf8);

    // 直接添加词条（供内置兜底词典使用）
    void AddEntry(const std::vector<std::wstring>& codeKeys, const std::wstring& word, int weight);

    // 追加音节表（空格分隔的一长串）
    void AddSyllableList(const std::wstring& spaceSeparated);

    bool IsSyllable(const std::wstring& token) const;
    const std::set<std::wstring>& Syllables() const { return syllables_; }

    // 精确匹配：codeJoined 是首尾相接的完整码（"nihao"）
    void LookupExact(const std::wstring& codeJoined, std::vector<Candidate>& out, size_t maxCount) const;

    // 前缀匹配：码以 prefix 开头。用于「音节还没打完」时的补全候选。
    // 返回顺序：码短者优先（更可能是当前音节的单字），其次权重高者优先。
    void LookupPrefix(const std::wstring& prefix, std::vector<Candidate>& out, size_t maxCount) const;

    size_t EntryCount() const { return entries_.size(); }
    bool Empty() const { return entries_.empty(); }

private:
    struct Entry {
        std::wstring codeJoined;              // "nihao"
        std::vector<std::wstring> codeKeys;   // {"ni","hao"}
        std::wstring word;                    // "你好"
        int weight = 0;
    };

    std::vector<Entry> entries_;
    std::map<std::wstring, std::vector<size_t>> index_;  // codeJoined -> entry 下标
    std::set<std::wstring> syllables_;
};

// 进程级共享词典：文本服务是「每个线程一个实例」，词典只读，加载一次即可。
const Dictionary& SharedDictionary();

}  // namespace myime
