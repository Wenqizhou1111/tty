// Common.h —— 公共头：Windows/TSF 包含、ComPtr、日志、字符串工具
//
// 注意：本文件以及整个工程都用 UTF-8 保存，编译时必须开启 /utf-8
//      （见 CMakeLists.txt），否则源码里的中文字面量会被按 GBK 误解。
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
// windows.h 会定义 min/max 宏，和 std::min/std::max 冲突；骨架里四处用到 std::min
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>
#include <ole2.h>          // WIN32_LEAN_AND_MEAN 会挡掉 windows.h 里的 OLE，必须显式引入
#include <oleauto.h>       // SysAllocString / VARIANT（ITfCompartment::GetValue 要用）
#include <msctf.h>         // TSF(Cicero) 全部接口

#include <string>
#include <vector>
#include <map>
#include <set>
#include <algorithm>
#include <cstdint>

// ---------------------------------------------------------------------------
// TSF 错误码兜底：不同版本 SDK 的 msctf.h 对这组错误码的定义位置不一致。
// 只要 SDK 已经定义过，下面的 #ifndef 就不会生效。
//
// 数值来源（已核对）：TS_E_* 是 TSF 1.0 时代的拼写，TF_E_* 是现在的拼写，
// 两者是同一批数值，例如 NOLOCK 两种名字都是 0x80040201。
// ---------------------------------------------------------------------------
#ifndef TS_E_NOLOCK
#define TS_E_NOLOCK ((HRESULT)0x80040201L)
#endif
#ifndef TF_E_NOLOCK
#define TF_E_NOLOCK ((HRESULT)0x80040201L)
#endif
// 文档不支持同步编辑会话（要退回异步）
#ifndef TF_E_SYNCHRONOUS
#define TF_E_SYNCHRONOUS ((HRESULT)0x80040208L)
#endif
// 文档只读
#ifndef TF_E_READONLY
#define TF_E_READONLY ((HRESULT)0x80040209L)
#endif
// 文本没有布局信息（GetTextExt 常见失败码）
#ifndef TF_E_NOLAYOUT
#define TF_E_NOLAYOUT ((HRESULT)0x80040206L)
#endif
// 编辑会话已建立但异步执行（这是「成功」码，别用 == S_OK 判断）
#ifndef TF_S_ASYNC
#define TF_S_ASYNC ((HRESULT)0x00040300L)
#endif
#ifndef TF_INVALID_COOKIE
#define TF_INVALID_COOKIE ((DWORD)0xFFFFFFFF)
#endif
#ifndef TF_INVALID_GUIDATOM
#define TF_INVALID_GUIDATOM ((TfGuidAtom)0)
#endif
#ifndef TF_CLIENTID_NULL
#define TF_CLIENTID_NULL ((TfClientId)0)
#endif

namespace myime {

// ---------------------------------------------------------------------------
// 极简 ComPtr —— 刻意不依赖 ATL/WIL，减少骨架的第三方依赖。
// 只提供骨架需要的操作；不要指望它和 ATL::CComPtr 完全等价。
// ---------------------------------------------------------------------------
template <typename T>
class ComPtr {
public:
    ComPtr() = default;
    ~ComPtr() { Reset(); }

    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;

    ComPtr(ComPtr&& other) noexcept : p_(other.p_) { other.p_ = nullptr; }

    ComPtr& operator=(ComPtr&& other) noexcept {
        if (this != &other) {
            Reset();
            p_ = other.p_;
            other.p_ = nullptr;
        }
        return *this;
    }

    // 取裸指针（不转移所有权）
    T* Get() const { return p_; }
    T* operator->() const { return p_; }
    explicit operator bool() const { return p_ != nullptr; }

    // 用于 QueryInterface/CoCreateInstance 的输出参数。
    // 调用前必须保证自身为空，否则断言失败——避免最常见的 COM 指针泄漏。
    T** Put() {
        Reset();
        return &p_;
    }
    void** PutVoid() { return reinterpret_cast<void**>(Put()); }

    // 放弃所有权，返回裸指针
    T* Detach() {
        T* t = p_;
        p_ = nullptr;
        return t;
    }

    // 接管一个已经加过引用的裸指针（不再 AddRef）
    void Attach(T* p) {
        Reset();
        p_ = p;
    }

    void Reset() {
        if (p_ != nullptr) {
            p_->Release();
            p_ = nullptr;
        }
    }

    // 便利函数：QueryInterface 并返回 HRESULT
    template <typename U>
    HRESULT As(ComPtr<U>& out) const {
        if (p_ == nullptr) return E_UNEXPECTED;
        return p_->QueryInterface(__uuidof(U), out.PutVoid());
    }

private:
    T* p_ = nullptr;
};

inline void SafeRelease(IUnknown* p) {
    if (p != nullptr) p->Release();
}

// ---------------------------------------------------------------------------
// 日志：走 OutputDebugStringW，用 DebugView / VS 输出窗口看。
// 生产版本建议改成 ETW 或带开关的文件日志（见设计文档"调试"一节）。
// ---------------------------------------------------------------------------
void LogDebug(const wchar_t* format, ...);

// ---------------------------------------------------------------------------
// 字符串工具
// ---------------------------------------------------------------------------

// UTF-8 字节 → UTF-16（TSF 全程使用 UTF-16）
std::wstring Utf8ToWide(const std::string& utf8);

// UTF-16 → UTF-8
std::string WideToUtf8(const std::wstring& wide);

// 去掉首尾空白（含 \r \n \t 空格），用于词典解析
std::wstring Trim(const std::wstring& s);

// 取当前模块(DLL)所在目录，带尾部反斜杠
std::wstring GetModuleDirectory();

// 取当前模块(DLL)句柄：注册窗口类、加载资源都要用它
HINSTANCE GetSelfModule();

}  // namespace myime
