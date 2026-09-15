// CandidateWindow.cpp
#include "CandidateWindow.h"

namespace myime {

namespace {

const wchar_t kWindowClassName[] = L"MyImeCandidateWindow";

const COLORREF kBackground = RGB(255, 255, 255);
const COLORREF kBorder = RGB(150, 150, 150);
const COLORREF kTextColor = RGB(30, 30, 30);
const COLORREF kHintColor = RGB(150, 150, 150);
const COLORREF kSelectedBackground = RGB(0x33, 0x99, 0xFF);
const COLORREF kSelectedTextColor = RGB(255, 255, 255);

const wchar_t kFontFace[] = L"Microsoft YaHei UI";

int SystemDpi() {
    HDC screen = ::GetDC(nullptr);
    if (screen == nullptr) return 96;
    const int dpi = ::GetDeviceCaps(screen, LOGPIXELSY);
    ::ReleaseDC(nullptr, screen);
    return dpi > 0 ? dpi : 96;
}

}  // namespace

CandidateWindow::~CandidateWindow() {
    DestroyWindowIfAny();
}

bool CandidateWindow::EnsureWindowClass() {
    static bool registered = false;
    if (registered) return true;

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &CandidateWindow::WindowProc;
    wc.hInstance = GetSelfModule();
    wc.hCursor = ::LoadCursorW(nullptr, IDC_ARROW);
    wc.lpszClassName = kWindowClassName;

    if (::RegisterClassExW(&wc) == 0) {
        if (::GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
            LogDebug(L"[MyIME] RegisterClassExW 失败: %lu", ::GetLastError());
            return false;
        }
    }
    registered = true;
    return true;
}

bool CandidateWindow::EnsureWindow() {
    if (hwnd_ != nullptr) {
        // 窗口可能已经被销毁（例如拥有它的宿主窗口关掉了）。
        // 句柄值不会自动清空，必须用 IsWindow 验证，否则后续调用会全部失败。
        if (!::IsWindow(hwnd_)) {
            LogDebug(L"[MyIME] 候选窗句柄已失效（宿主窗口被销毁？），重新创建");
            hwnd_ = nullptr;
        } else {
            return true;
        }
    }
    if (!EnsureWindowClass()) return false;

    dpi_ = SystemDpi();
    ownerThreadId_ = ::GetCurrentThreadId();
    font_ = ::CreateFontW(-::MulDiv(12, dpi_, 72), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                          DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                          CLEARTYPE_QUALITY, DEFAULT_PITCH, kFontFace);
    padding_ = ::MulDiv(6, dpi_, 96);

    // owner_ 是宿主应用的焦点窗口（可能为 null）。
    // 传 owner 而不是 parent：WS_POPUP + owner 让候选窗浮在应用窗口之上，
    // 并在应用窗口销毁时被一起销毁（微软 SampleIME 也是这么做的）。
    hwnd_ = ::CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                              kWindowClassName, L"", WS_POPUP, 0, 0, 10, 10, owner_, nullptr,
                              GetSelfModule(), this);
    if (hwnd_ == nullptr) {
        LogDebug(L"[MyIME] 创建候选窗失败: %lu", ::GetLastError());
        return false;
    }
    return true;
}

void CandidateWindow::SetOwner(HWND owner) {
    if (owner_ == owner) return;
    owner_ = owner;
    if (hwnd_ != nullptr && ::IsWindow(hwnd_)) {
        // GWLP_HWNDPARENT 改的是 owner（对 WS_POPUP 而言不是 parent）
        ::SetWindowLongPtrW(hwnd_, GWLP_HWNDPARENT, reinterpret_cast<LONG_PTR>(owner_));
    }
}

void CandidateWindow::DestroyWindowIfAny() {
    if (hwnd_ != nullptr) {
        if (!::IsWindow(hwnd_)) {
            hwnd_ = nullptr;   // 已经被宿主窗口带走了
        } else if (ownerThreadId_ == ::GetCurrentThreadId()) {
            ::DestroyWindow(hwnd_);
            hwnd_ = nullptr;
        } else {
            // 窗口只能在创建它的线程上销毁。TSF 的 OnDeactivate 有可能在别的线程调用，
            // 这种情况下宁可留着窗口（进程退出时系统会回收），也不要跨线程 DestroyWindow。
            LogDebug(L"[MyIME] 候选窗在非创建线程上析构，跳过 DestroyWindow（线程 %lu → %lu）",
                     ownerThreadId_, ::GetCurrentThreadId());
            hwnd_ = nullptr;
        }
    }
    if (font_ != nullptr) {
        ::DeleteObject(font_);
        font_ = nullptr;
    }
}

LRESULT CALLBACK CandidateWindow::WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    CandidateWindow* self =
        reinterpret_cast<CandidateWindow*>(::GetWindowLongPtrW(hwnd, GWLP_USERDATA));

    switch (message) {
        case WM_NCCREATE: {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            self = reinterpret_cast<CandidateWindow*>(create->lpCreateParams);
            if (self != nullptr) {
                ::SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
                self->hwnd_ = hwnd;
            }
            return TRUE;
        }
        case WM_MOUSEACTIVATE:
            // 点击候选窗时不要激活它，否则文档会失去焦点
            return MA_NOACTIVATE;
        case WM_ERASEBKGND:
            return 1;   // WM_PAINT 里整块重画，不需要系统擦背景（避免闪烁）
        case WM_PAINT: {
            PAINTSTRUCT ps = {};
            HDC hdc = ::BeginPaint(hwnd, &ps);
            if (self != nullptr) self->Paint(hdc);
            ::EndPaint(hwnd, &ps);
            return 0;
        }
        default:
            break;
    }
    return ::DefWindowProcW(hwnd, message, wParam, lParam);
}

void CandidateWindow::Layout(HDC hdc) {
    HGDIOBJ oldFont = ::SelectObject(hdc, font_);

    TEXTMETRICW metrics = {};
    ::GetTextMetricsW(hdc, &metrics);
    fontHeight_ = metrics.tmHeight;

    const int gap = ::MulDiv(12, dpi_, 96);
    const int headerHeight = header_.empty() ? 0 : fontHeight_ + padding_ / 2;

    int maxWidth = ::MulDiv(680, dpi_, 96);
    const int screenWidth = ::GetSystemMetrics(SM_CXSCREEN);
    if (screenWidth > 0 && maxWidth > screenWidth * 9 / 10) maxWidth = screenWidth * 9 / 10;
    if (maxWidth < 200) maxWidth = 200;

    int x = padding_;
    int y = padding_ + headerHeight;
    int contentRight = 0;

    for (auto& item : items_) {
        const std::wstring full = item.label + item.text;
        SIZE size = {};
        ::GetTextExtentPoint32W(hdc, full.c_str(), static_cast<int>(full.size()), &size);

        if (x > padding_ && x + size.cx + padding_ > maxWidth) {
            x = padding_;
            y += fontHeight_ + padding_ / 2;
        }
        item.rect.left = x;
        item.rect.top = y;
        item.rect.right = x + size.cx;
        item.rect.bottom = y + fontHeight_;
        x += size.cx + gap;
        if (item.rect.right > contentRight) contentRight = item.rect.right;
    }

    width_ = contentRight + padding_;
    height_ = y + fontHeight_ + padding_;

    ::SelectObject(hdc, oldFont);
}

void CandidateWindow::MoveToAnchor(const RECT& anchor, bool anchorValid) {
    int left = anchor.left;
    int top = anchor.bottom + ::MulDiv(2, dpi_, 96);

    if (!anchorValid) {
        POINT cursor = {};
        ::GetCursorPos(&cursor);
        left = cursor.x;
        top = cursor.y + ::MulDiv(16, dpi_, 96);
    }

    // 夹到目标显示器的工作区里，避免跑出屏幕
    RECT target = {left, top, left + width_, top + height_};
    HMONITOR monitor = ::MonitorFromRect(&target, MONITOR_DEFAULTTONEAREST);
    MONITORINFO info = {};
    info.cbSize = sizeof(info);
    if (::GetMonitorInfoW(monitor, &info)) {
        if (target.right > info.rcWork.right) left = info.rcWork.right - width_;
        if (target.bottom > info.rcWork.bottom) {
            // 下方放不下就放到光标上方
            top = anchorValid ? anchor.top - height_ - ::MulDiv(2, dpi_, 96)
                              : info.rcWork.bottom - height_;
        }
        if (left < info.rcWork.left) left = info.rcWork.left;
        if (top < info.rcWork.top) top = info.rcWork.top;
    }

    ::SetWindowPos(hwnd_, HWND_TOPMOST, left, top, width_, height_,
                   SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

void CandidateWindow::Paint(HDC hdc) {
    RECT client = {};
    ::GetClientRect(hwnd_, &client);

    HBRUSH background = ::CreateSolidBrush(kBackground);
    ::FillRect(hdc, &client, background);
    ::DeleteObject(background);

    HBRUSH border = ::CreateSolidBrush(kBorder);
    ::FrameRect(hdc, &client, border);
    ::DeleteObject(border);

    ::SetBkMode(hdc, TRANSPARENT);
    HGDIOBJ oldFont = ::SelectObject(hdc, font_);

    if (!header_.empty()) {
        ::SetTextColor(hdc, kHintColor);
        ::TextOutW(hdc, padding_, padding_, header_.c_str(), static_cast<int>(header_.size()));
    }

    for (const auto& item : items_) {
        const std::wstring full = item.label + item.text;
        if (item.selected && !item.hint) {
            RECT highlight = item.rect;
            ::InflateRect(&highlight, padding_ / 2, 1);
            HBRUSH selected = ::CreateSolidBrush(kSelectedBackground);
            ::FillRect(hdc, &highlight, selected);
            ::DeleteObject(selected);
            ::SetTextColor(hdc, kSelectedTextColor);
        } else {
            ::SetTextColor(hdc, item.hint ? kHintColor : kTextColor);
        }
        ::TextOutW(hdc, item.rect.left, item.rect.top, full.c_str(),
                   static_cast<int>(full.size()));
    }

    ::SelectObject(hdc, oldFont);
}

void CandidateWindow::Show(const std::vector<Candidate>& candidates, int pageStart, int pageSize,
                           int selectedIndex, const std::wstring& segmentationText,
                           const RECT& anchor, bool anchorValid) {
    if (candidates.empty()) {
        Hide();
        return;
    }
    if (!EnsureWindow()) return;

    header_ = segmentationText;
    items_.clear();

    const int total = static_cast<int>(candidates.size());
    const int pages = (pageSize > 0) ? (total + pageSize - 1) / pageSize : 1;
    const int page = (pageSize > 0) ? (pageStart / pageSize) : 0;
    const int end = (pageSize > 0) ? (std::min)(pageStart + pageSize, total) : total;

    for (int i = pageStart; i < end; ++i) {
        Item item;
        item.label = std::to_wstring(i - pageStart + 1) + L". ";
        item.text = candidates[static_cast<size_t>(i)].text;
        item.selected = (i == selectedIndex);
        items_.push_back(std::move(item));
    }

    if (pages > 1) {
        Item hint;
        hint.label = (page + 1 < pages) ? L"\u25BC " : L"";   // ▼ 表示还有下一页
        hint.text = std::to_wstring(page + 1) + L"/" + std::to_wstring(pages);
        hint.hint = true;
        items_.push_back(std::move(hint));
    }

    HDC screen = ::GetDC(nullptr);
    if (screen != nullptr) {
        Layout(screen);
        ::ReleaseDC(nullptr, screen);
    }

    MoveToAnchor(anchor, anchorValid);
    ::InvalidateRect(hwnd_, nullptr, FALSE);
}

void CandidateWindow::Hide() {
    if (hwnd_ != nullptr && ::IsWindowVisible(hwnd_)) {
        ::ShowWindow(hwnd_, SW_HIDE);
    }
    items_.clear();
    header_.clear();
}

bool CandidateWindow::IsVisible() const {
    return hwnd_ != nullptr && ::IsWindowVisible(hwnd_) != FALSE;
}

}  // namespace myime
