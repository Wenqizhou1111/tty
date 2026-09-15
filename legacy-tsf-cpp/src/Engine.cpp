// Engine.cpp
#include "Engine.h"

namespace myime {

namespace {

constexpr size_t kMaxSyllableLen = 6;       // chuang / shuang 是最长音节
constexpr size_t kMaxPerSyllable = 3;       // 组合候选时每个音节取几个单字
constexpr size_t kMaxCandidates = 35;       // 候选池上限（7 个/页 × 5 页）
constexpr size_t kMaxTailCandidates = 8;    // 「还没打完」时的补全候选数
constexpr int kWholeWordBonus = 1000;       // 整串命中整词时的加分

using Segment = std::vector<std::wstring>;

std::wstring JoinAll(const Segment& segment) {
    std::wstring out;
    for (const auto& piece : segment) out += piece;
    return out;
}

std::vector<std::wstring> SplitByChar(const std::wstring& text, wchar_t separator) {
    std::vector<std::wstring> parts;
    std::wstring current;
    for (wchar_t c : text) {
        if (c == separator) {
            parts.push_back(current);
            current.clear();
        } else {
            current += c;
        }
    }
    parts.push_back(current);
    return parts;
}

// 枚举把 s[start..) 完整切成音节的方案，顺序是「长音节优先」。
// limit 限制方案数量：只要有解，第一个解就是贪心最长匹配的结果。
void EnumerateSegmentations(const Dictionary& dict, const std::wstring& s, size_t start,
                            size_t limit, Segment& current, std::vector<Segment>& out) {
    if (out.size() >= limit) return;
    if (start >= s.size()) {
        out.push_back(current);
        return;
    }
    const size_t maxLen = (std::min)(kMaxSyllableLen, s.size() - start);
    for (size_t len = maxLen; len >= 1; --len) {
        const std::wstring token = s.substr(start, len);
        if (!dict.IsSyllable(token)) continue;
        current.push_back(token);
        EnumerateSegmentations(dict, s, start + len, limit, current, out);
        current.pop_back();
        if (out.size() >= limit) return;
    }
}

// 同文本去重：保留最高分（先后出现顺序由 stable_sort 保证）
void AppendUnique(std::vector<Candidate>& list, const std::wstring& text,
                  const std::wstring& comment, int score) {
    if (text.empty()) return;
    for (auto& candidate : list) {
        if (candidate.text == text) {
            if (score > candidate.score) {
                candidate.score = score;
                if (!comment.empty()) candidate.comment = comment;
            }
            return;
        }
    }
    Candidate candidate;
    candidate.text = text;
    candidate.comment = comment;
    candidate.score = score;
    list.push_back(std::move(candidate));
}

}  // namespace

void Engine::SetPageSize(int size) {
    if (size < 1) size = 1;
    if (size > 9) size = 9;   // 数字键只有 1..9
    pageSize_ = size;
    ClampSelection();
}

void Engine::Reset() {
    raw_.clear();
    segText_.clear();
    candidates_.clear();
    pageStart_ = 0;
    selected_ = 0;
}

int Engine::PageCount() const {
    const int count = static_cast<int>(candidates_.size());
    if (count <= 0) return 0;
    return (count + pageSize_ - 1) / pageSize_;
}

int Engine::PageIndex() const {
    if (pageSize_ <= 0) return 0;
    return pageStart_ / pageSize_;
}

int Engine::SelectedInPage() const { return selected_ - pageStart_; }

void Engine::ClampSelection() {
    const int count = static_cast<int>(candidates_.size());
    if (count <= 0) {
        selected_ = 0;
        pageStart_ = 0;
        return;
    }
    if (selected_ < 0) selected_ = 0;
    if (selected_ >= count) selected_ = count - 1;

    const int pageSize = pageSize_ > 0 ? pageSize_ : 1;
    // 选中项必须落在当前页内；否则把页翻到包含它的那一页
    if (selected_ < pageStart_ || selected_ >= pageStart_ + pageSize) {
        pageStart_ = (selected_ / pageSize) * pageSize;
    }
    const int maxStart = ((count - 1) / pageSize) * pageSize;
    if (pageStart_ > maxStart) pageStart_ = maxStart;
    if (pageStart_ < 0) pageStart_ = 0;
}

void Engine::MoveSelection(int delta) {
    const int count = static_cast<int>(candidates_.size());
    if (count <= 0) return;
    selected_ += delta;
    if (selected_ < 0) selected_ = 0;
    if (selected_ >= count) selected_ = count - 1;
    ClampSelection();
}

void Engine::ChangePage(int delta) {
    const int pages = PageCount();
    if (pages <= 0) return;
    int page = PageIndex() + delta;
    if (page < 0) page = 0;
    if (page >= pages) page = pages - 1;
    pageStart_ = page * pageSize_;
    selected_ = pageStart_;
    ClampSelection();
}

void Engine::Rebuild() {
    BuildCandidates();
    pageStart_ = 0;
    selected_ = 0;
    ClampSelection();
}

// 候选生成主流程。刻意写成「可读优先」的骨架实现，
// 每一步对应设计文档「候选怎么来」一节的 4 个来源。
void Engine::BuildCandidates() {
    candidates_.clear();
    segText_.clear();

    if (raw_.empty() || dict_ == nullptr || dict_->Empty()) return;

    // ---- 1. 切分：' 是显式分隔符；只有最后一段允许「没打完」----
    const std::vector<std::wstring> chunks = SplitByChar(raw_, L'\'');

    Segment determined;  // 已经能确定的完整音节
    for (size_t i = 0; i + 1 < chunks.size(); ++i) {
        std::vector<Segment> segmentations;
        Segment current;
        EnumerateSegmentations(*dict_, chunks[i], 0, 1, current, segmentations);
        if (!segmentations.empty()) {
            determined.insert(determined.end(), segmentations[0].begin(), segmentations[0].end());
        }
    }

    const std::wstring& last = chunks.back();
    std::wstring tail = last;  // 默认整段都是「没打完」的尾巴
    for (size_t len = last.size(); len >= 1; --len) {
        std::vector<Segment> segmentations;
        Segment current;
        EnumerateSegmentations(*dict_, last.substr(0, len), 0, 1, current, segmentations);
        if (!segmentations.empty()) {
            determined.insert(determined.end(), segmentations[0].begin(), segmentations[0].end());
            tail = last.substr(len);
            break;
        }
    }

    // ---- 2. 展示用切分文本（候选窗顶栏显示）----
    for (size_t i = 0; i < determined.size(); ++i) {
        if (i > 0) segText_ += L' ';
        segText_ += determined[i];
    }
    if (!tail.empty()) {
        if (!segText_.empty()) segText_ += L' ';
        segText_ += tail;
    }

    const std::wstring determinedCode = JoinAll(determined);

    // ---- 3. 候选：四个来源 ----
    if (tail.empty()) {
        // 3a. 整串精确匹配：词库里的整词最优先（nihao → 你好）
        std::vector<Candidate> exact;
        dict_->LookupExact(determinedCode, exact, kMaxCandidates);
        for (const auto& candidate : exact) {
            AppendUnique(candidates_, candidate.text, candidate.comment,
                         candidate.score + kWholeWordBonus);
        }

        // 3b. 多音节时做「单字组合」：全取首选 + 逐音节单点替换
        if (determined.size() >= 2) {
            std::vector<std::vector<Candidate>> perSyllable;
            bool complete = true;
            for (const auto& syllable : determined) {
                std::vector<Candidate> list;
                dict_->LookupExact(syllable, list, kMaxPerSyllable);
                if (list.empty()) {
                    complete = false;
                    break;
                }
                perSyllable.push_back(std::move(list));
            }

            if (complete) {
                std::wstring base;
                int baseScore = 0;
                for (const auto& list : perSyllable) {
                    base += list[0].text;
                    baseScore += list[0].score;
                }
                AppendUnique(candidates_, base, determinedCode, baseScore);

                for (size_t i = 0; i < perSyllable.size(); ++i) {
                    for (size_t j = 1; j < perSyllable[i].size(); ++j) {
                        std::wstring text;
                        int score = 0;
                        for (size_t k = 0; k < perSyllable.size(); ++k) {
                            const size_t pick = (k == i) ? j : 0;
                            text += perSyllable[k][pick].text;
                            score += perSyllable[k][pick].score;
                        }
                        AppendUnique(candidates_, text, determinedCode, score);
                    }
                }
            }
        }
    } else {
        // 3c. 尾巴还没打完：确定部分先出一个前缀文字，再拼补全候选
        std::wstring prefixText;
        int prefixScore = 0;
        if (!determined.empty()) {
            std::vector<Candidate> whole;
            dict_->LookupExact(determinedCode, whole, 1);
            if (!whole.empty()) {
                prefixText = whole[0].text;
                prefixScore = whole[0].score + kWholeWordBonus;
            } else {
                bool complete = true;
                for (const auto& syllable : determined) {
                    std::vector<Candidate> list;
                    dict_->LookupExact(syllable, list, 1);
                    if (list.empty()) {
                        complete = false;
                        break;
                    }
                    prefixText += list[0].text;
                    prefixScore += list[0].score;
                }
                if (!complete) {
                    prefixText.clear();
                    prefixScore = 0;
                }
            }
        }

        std::vector<Candidate> tailCandidates;
        dict_->LookupPrefix(tail, tailCandidates, kMaxTailCandidates);
        for (const auto& candidate : tailCandidates) {
            AppendUnique(candidates_, prefixText + candidate.text, candidate.comment,
                         prefixScore + candidate.score);
        }
    }

    // ---- 4. 兜底：一个候选都没有时，把原始字母当成候选 ----
    //     这样「敲了乱码 + 空格」仍然能把字母打出去，不会丢输入。
    if (candidates_.empty()) {
        AppendUnique(candidates_, raw_, L"原样", 0);
    }

    std::stable_sort(candidates_.begin(), candidates_.end(),
                     [](const Candidate& a, const Candidate& b) { return a.score > b.score; });
    if (candidates_.size() > kMaxCandidates) candidates_.resize(kMaxCandidates);
}

StepResult Engine::Step(const KeyInput& key) {
    StepResult result;
    const bool active = IsActive();

    // Esc：取消组合
    if (key.vk == VK_ESCAPE) {
        if (active) {
            Reset();
            result.eaten = true;
            result.changed = true;
            result.cancelComposition = true;
        }
        return result;  // 不在组合中时不消费，交给应用
    }

    // 回车：原始字母原样上屏（英文直通，和微软拼音一致）
    if (key.vk == VK_RETURN) {
        if (active) {
            result.commitText = raw_;
            Reset();
            result.eaten = true;
            result.changed = true;
        }
        return result;  // 不在组合中时不消费，让应用自己处理换行
    }

    // 退格：删掉一个字母
    if (key.vk == VK_BACK) {
        if (!active) return result;
        raw_.erase(raw_.size() - 1);
        result.eaten = true;
        if (raw_.empty()) {
            Reset();
            result.changed = true;
            result.cancelComposition = true;  // 删空了 → 组合结束
        } else {
            Rebuild();
            result.changed = true;
        }
        return result;
    }

    if (active && !key.ctrl && !key.alt) {
        // 数字键：选当前页第 N 个候选并上屏。
        // 注意：OnTestKeyDown 已经答应消费这个键，所以哪怕页码越界也必须吃掉，
        // 否则会出现「测试阶段说吃、实际没吃」的不一致，某些应用会因此丢键。
        if (key.vk >= '1' && key.vk <= '9') {
            const int target = pageStart_ + static_cast<int>(key.vk - '1');
            if (target >= 0 && target < static_cast<int>(candidates_.size())) {
                selected_ = target;
                result.commitText = CommitSelected();
                result.changed = true;
            }
            result.eaten = true;
            return result;
        }

        switch (key.vk) {
            case VK_SPACE:
                result.commitText = CommitSelected();
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_LEFT:
            case VK_UP:
                MoveSelection(-1);
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_RIGHT:
            case VK_DOWN:
                MoveSelection(1);
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_PRIOR:
            case VK_OEM_MINUS:
                ChangePage(-1);
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_NEXT:
            case VK_OEM_PLUS:
                ChangePage(1);
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_HOME:
                selected_ = 0;
                ClampSelection();
                result.eaten = true;
                result.changed = true;
                return result;
            case VK_END:
                selected_ = static_cast<int>(candidates_.size()) - 1;
                ClampSelection();
                result.eaten = true;
                result.changed = true;
                return result;
            default:
                break;
        }
    }

    if (!key.ctrl && !key.alt) {
        // 字母键：累积原始输入
        if (key.vk >= 'A' && key.vk <= 'Z') {
            raw_ += static_cast<wchar_t>(key.vk - 'A' + L'a');
            Rebuild();
            result.eaten = true;
            result.changed = true;
            return result;
        }
        // 单引号：显式音节分隔，用来输入 "xi'an" 这类词
        if (key.vk == VK_OEM_7 && active) {
            if (!raw_.empty() && raw_.back() != L'\'') {
                raw_ += L'\'';
                Rebuild();
            }
            result.eaten = true;
            result.changed = true;
            return result;
        }
    }

    return result;  // 其余按键一概不消费
}

std::wstring Engine::CommitSelected() {
    std::wstring text;
    if (selected_ >= 0 && selected_ < static_cast<int>(candidates_.size())) {
        text = candidates_[selected_].text;
    }
    if (text.empty()) text = raw_;
    Reset();
    return text;
}

std::wstring Engine::CommitRaw() {
    const std::wstring text = raw_;
    Reset();
    return text;
}

}  // namespace myime
