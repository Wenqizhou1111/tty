// EditSession.cpp
#include "EditSession.h"

#include "TextService.h"

#include <new>

namespace myime {

CEditSession::CEditSession(CTextService* service, ITfContext* context, EditAction action,
                           const std::wstring& text)
    : m_cRef(1), m_pService(service), m_action(action), m_text(text) {
    if (m_pService != nullptr) m_pService->AddRef();
    if (context != nullptr) {
        context->AddRef();          // 会话执行期间必须保证上下文还活着
        m_pContext.Attach(context);
    }
}

CEditSession::~CEditSession() {
    if (m_pService != nullptr) m_pService->Release();
}

STDMETHODIMP CEditSession::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfEditSession)) {
        *ppv = static_cast<ITfEditSession*>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) CEditSession::AddRef() {
    return static_cast<ULONG>(::InterlockedIncrement(&m_cRef));
}

STDMETHODIMP_(ULONG) CEditSession::Release() {
    const LONG count = ::InterlockedDecrement(&m_cRef);
    if (count == 0) delete this;
    return static_cast<ULONG>(count);
}

STDMETHODIMP CEditSession::DoEditSession(TfEditCookie ec) {
    if (m_pService == nullptr) return E_UNEXPECTED;
    return m_pService->OnEditSession(m_action, ec, m_text);
}

HRESULT RequestEditSession(ITfContext* context, TfClientId tid, CEditSession* session,
                           EditSessionMode mode) {
    if (context == nullptr || session == nullptr) return E_INVALIDARG;

    DWORD flags = TF_ES_READWRITE;
    switch (mode) {
        case EditSessionMode::SyncWrite:
            flags |= TF_ES_SYNC;
            break;
        case EditSessionMode::AsyncWrite:
            flags |= TF_ES_ASYNCDONTCARE;
            break;
        case EditSessionMode::SyncRead:
            flags = TF_ES_READ | TF_ES_SYNC;
            break;
    }

    HRESULT sessionResult = S_OK;
    HRESULT hr = context->RequestEditSession(tid, session, flags, &sessionResult);
    if (FAILED(hr)) {
        LogDebug(L"[MyIME] RequestEditSession 失败: 0x%08X", hr);
        return hr;
    }

    if (FAILED(sessionResult)) {
        LogDebug(L"[MyIME] 编辑会话失败: 0x%08X", sessionResult);

        // 有些文档不支持同步编辑会话（TF_E_SYNCHRONOUS），有些是只读（TF_E_READONLY）。
        // 前者退回异步再试一次——这是微软 SampleIME 用的同一套兜底。
        const bool retryAsync =
            (sessionResult == TF_E_SYNCHRONOUS || sessionResult == TF_E_READONLY);
        if (retryAsync && (mode == EditSessionMode::SyncWrite || mode == EditSessionMode::SyncRead)) {
            LogDebug(L"[MyIME] 改用异步会话重试");
            sessionResult = S_OK;
            hr = context->RequestEditSession(tid, session, TF_ES_READWRITE | TF_ES_ASYNC,
                                             &sessionResult);
            if (FAILED(hr)) return hr;
            if (FAILED(sessionResult)) return sessionResult;
            return S_OK;
        }
        return sessionResult;
    }
    return S_OK;
}

const wchar_t* EditActionName(EditAction action) {
    switch (action) {
        case EditAction::StartComposition:
            return L"StartComposition";
        case EditAction::UpdateComposition:
            return L"UpdateComposition";
        case EditAction::CommitComposition:
            return L"CommitComposition";
        case EditAction::CancelComposition:
            return L"CancelComposition";
        case EditAction::QueryAnchor:
            return L"QueryAnchor";
    }
    return L"Unknown";
}

}  // namespace myime
