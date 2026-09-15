// Engine.h —— 输入引擎（纯逻辑，不依赖 TSF / Win32）
//
// 这一层是「输入法的大脑」：按键 → 组合状态 → 候选列表 → 上屏文字。
// 它与 TSF 完全解耦，因此：
//   * 可以脱离输入法环境做单元测试（见 tests/EngineTest.cpp）
//   * 换引擎（双拼、五笔、云候选）时不必改 TSF 那一层
//
// 状态机的状态很少，刻意保持简单：
//   raw_        用户敲进来的原始键序列，如 "nihao"
//   candidates_ 当前全部候选（跨页）
//   selected_   全局选中下标
//   pageStart_  当前页第一个候选的全局下标
#pragma once

#include "Common.h"
#include "Dictionary.h"

namespace myime {

// 一次按键输入（已由 TSF 层翻译成虚拟键）
struct KeyInput {
    unsigned vk = 0;
    bool ctrl = false;
    bool alt = false;
    bool shift = false;
};

// 一次按键处理的结果
struct StepResult {
    bool eaten = false;              // 是否消费该键 → 直接映射到 ITfKeyEventSink 的 *pfEaten
    bool changed = false;            // 组合内容或候选有变化 → 需要刷新文档里的组合与候选窗
    bool cancelComposition = false;   // 需要结束组合且不提交（如 Esc、退格删空）
    std::wstring commitText;         // 非空 → 需要把这段文字提交进文档并结束组合
};

class Engine {
public:
    void SetDictionary(const Dictionary* dict) { dict_ = dict; }
    void SetPageSize(int size);  // 每页候选数，取值 1..9（对应数字键 1..9）

    // 清空组合状态（不影响词典）
    void Reset();

    bool IsActive() const { return !raw_.empty(); }
    const std::wstring& Raw() const { return raw_; }
    const std::wstring& SegmentationText() const { return segText_; }
    const std::vector<Candidate>& Candidates() const { return candidates_; }

    int PageSize() const { return pageSize_; }
    int PageCount() const;
    int PageIndex() const;
    int PageStart() const { return pageStart_; }
    int SelectedIndex() const { return selected_; }
    int SelectedInPage() const;

    // 处理一次按键。这是 TSF 层唯一需要调用的入口。
    StepResult Step(const KeyInput& key);

    // 取当前选中候选的上屏文字（并把 Engine 复位）
    std::wstring CommitSelected();
    // 取原始键序列的上屏文字（英文直通 / 无候选兜底）
    std::wstring CommitRaw();

private:
    void BuildCandidates();  // 切分 + 生成候选（候选的四个来源都在这里）
    void Rebuild();          // 重新计算切分与候选，并把选中位置回第一页
    void ClampSelection();
    void MoveSelection(int delta);
    void ChangePage(int delta);

    std::wstring raw_;
    std::wstring segText_;
    std::vector<Candidate> candidates_;
    int pageSize_ = 7;
    int pageStart_ = 0;
    int selected_ = 0;
    const Dictionary* dict_ = nullptr;
};

}  // namespace myime
