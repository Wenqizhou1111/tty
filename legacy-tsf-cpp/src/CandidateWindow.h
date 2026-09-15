// CandidateWindow.h —— 候选窗（自绘 HWND）
//
// 为什么自己开窗口，而不是用 TSF 的 ITfCandidateListUIElement + ITfUIElementSink？
//   * 自绘窗口：完全可控（皮肤、动画、多行、鼠标点选都好做），
//     缺点是应用窗口全屏置顶/特殊 UI 时可能被压住，需要自己处理 Z 序。
//   * UIElement：交给 TSF 的 UIManager 画，跟系统 UI 一致，
//     缺点是受框架约束，且需要实现 ITfCandidateListUIElement 一族接口。
//   两种做法在真实输入法里都存在；骨架选自绘，便于看清「定位 + 绘制」的完整流程。
//
// 关键点：候选窗绝对不能抢焦点，否则会把输入焦点从文档上夺走 ——
//   所以用 WS_EX_NOACTIVATE + 只调 ShowWindow(SW_SHOWNOACTIVATE)/SetWindowPos，
//   永远不调用 SetFocus/SetActiveWindow。
#pragma once

#include "Common.h"
#include "Dictionary.h"

namespace myime {

class CandidateWindow {
public:
    CandidateWindow() = default;
    ~CandidateWindow();

    CandidateWindow(const CandidateWindow&) = delete;
    CandidateWindow& operator=(const CandidateWindow&) = delete;

    // 显示或更新候选窗。
    //   candidates/pageStart/pageSize/selectedIndex 来自 Engine
    //   segmentationText 显示在顶栏（如 "ni hao"）
    //   anchor 是文档里组合文字所在的屏幕矩形（由 TSF 的 GetTextExt 得到）
    void Show(const std::vector<Candidate>& candidates, int pageStart, int pageSize,
              int selectedIndex, const std::wstring& segmentationText, const RECT& anchor,
              bool anchorValid);

    void Hide();
    bool IsVisible() const;

    // 把候选窗挂到宿主应用的焦点窗口上（ITfContextView::GetWnd）。
    // 好处：候选窗一定浮在该窗口上方，且随它一起销毁。
    void SetOwner(HWND owner);

private:
    struct Item {
        std::wstring label;   // 序号，如 "1."
        std::wstring text;    // 候选文字
        bool selected = false;
        bool hint = false;    // 页码等提示，不参与高亮
        RECT rect = {};
    };

    static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);
    static bool EnsureWindowClass();

    bool EnsureWindow();
    void DestroyWindowIfAny();
    void Layout(HDC hdc);
    void Paint(HDC hdc);
    void MoveToAnchor(const RECT& anchor, bool anchorValid);

    HWND hwnd_ = nullptr;
    HWND owner_ = nullptr;      // 宿主应用的焦点窗口（由 ITfContextView::GetWnd 得到）
    HFONT font_ = nullptr;
    DWORD ownerThreadId_ = 0;   // 创建窗口的线程：窗口只能在同一个线程上销毁
    int dpi_ = 96;
    int fontHeight_ = 16;
    int padding_ = 6;

    std::wstring header_;
    std::vector<Item> items_;
    int width_ = 0;
    int height_ = 0;
};

}  // namespace myime
