// EditSession.h —— 编辑会话
//
// TSF 里改文档内容必须走「编辑会话」：
//   1) 构造一个 ITfEditSession
//   2) ITfContext::RequestEditSession 把它排进文档的编辑队列
//   3) TSF 在合适的时机回调 DoEditSession(ec)，此时才持有文档锁
// 这是输入法开发里最容易出错的一环，因为：
//   * 你拿不到 TfEditCookie 就不能读写文档，硬来会得到 TS_E_NOLOCK
//   * 请求可能被拒绝或延后（异步），必须有「这次没成功」的兜底
//   * 会话执行时键盘事件早已返回，不能假设 UI 状态没变
#pragma once

#include "Common.h"

namespace myime {

class CTextService;

// 会话要做什么。每加一种文档操作，就在这里加一项。
enum class EditAction {
    StartComposition,    // 开始组合（把当前选区变成组合范围）
    UpdateComposition,   // 刷新组合文字（用户还在敲）
    CommitComposition,   // 上屏：写入最终文字并结束组合
    CancelComposition,   // 取消：清空组合文字并结束组合
    QueryAnchor,         // 只读：计算组合位置，用于摆放候选窗
};

enum class EditSessionMode {
    SyncWrite,    // 同步 + 可写（上屏、取消这类必须立刻生效）
    AsyncWrite,   // 异步 + 可写（刷新组合文字，不阻塞应用）
    SyncRead,     // 同步 + 只读（读位置/读文字）
};

class CEditSession : public ITfEditSession {
public:
    CEditSession(CTextService* service, ITfContext* context, EditAction action,
                 const std::wstring& text);
    ~CEditSession();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfEditSession
    STDMETHODIMP DoEditSession(TfEditCookie ec) override;

private:
    LONG m_cRef;
    CTextService* m_pService;   // 持有引用：保证会话执行期间服务对象不被销毁
    ComPtr<ITfContext> m_pContext;
    EditAction m_action;
    std::wstring m_text;
};

// 发起编辑会话。返回 S_OK 表示会话已成功执行完。
// 返回 TS_E_NOLOCK / 其它失败时调用方应保持输入状态、下次按键再试（不要丢用户输入）。
HRESULT RequestEditSession(ITfContext* context, TfClientId tid, CEditSession* session,
                           EditSessionMode mode);

const wchar_t* EditActionName(EditAction action);

}  // namespace myime
