// TextService.cpp —— 文本服务主体实现
#include "TextService.h"

#include "DisplayAttribute.h"
#include "DllModule.h"
#include "EditSession.h"
#include "Guids.h"

#include <new>

namespace myime {

namespace {

// 把 ActivateEx 的 dwFlags 翻译成可读文字，方便在 DebugView 里一眼看出
// 自己是在什么环境里被激活的（安全桌面？无 COM？仅 UI 元素模式？）。
// 这些标志对应注册时声明的能力分类：声明了某个 GUID_TFCAT_TIPCAP_*，
// 系统才可能在对应的环境下这样激活你。
std::wstring DescribeActivateFlags(DWORD flags) {
    struct Entry {
        DWORD bit;
        const wchar_t* name;
    };
    static const Entry kEntries[] = {
        {TF_TMAE_NOACTIVATETIP, L"NOACTIVATETIP"},
        {TF_TMAE_SECUREMODE, L"SECUREMODE"},
        {TF_TMAE_UIELEMENTENABLEDONLY, L"UIELEMENTENABLEDONLY"},
        {TF_TMAE_COMLESS, L"COMLESS"},
        {TF_TMAE_WOW16, L"WOW16"},
        {TF_TMAE_NOACTIVATEKEYBOARDLAYOUT, L"NOACTIVATEKEYBOARDLAYOUT"},
    };

    std::wstring text;
    for (const auto& entry : kEntries) {
        if ((flags & entry.bit) == 0) continue;
        if (!text.empty()) text += L"|";
        text += entry.name;
    }
    return text.empty() ? std::wstring(L"NONE") : text;
}

}  // namespace

CTextService::CTextService() {
    m_dwOwnerThreadId = ::GetCurrentThreadId();
    // 每创建一个对象就给 DLL 记一笔：只要还有对象活着，DllCanUnloadNow 就必须答「不能卸」
    ModuleAddRef();
}

CTextService::~CTextService() {
    // 正常流程：TSF 会先调 OnDeactivate 再释放对象。
    // 走到这里 m_pThreadMgr 还在，说明流程异常，做最小限度的清理并记日志。
    if (m_pThreadMgr != nullptr) {
        LogDebug(L"[MyIME] 析构时线程管理器仍未释放（未收到 OnDeactivate？）");
        m_pThreadMgr->Release();
        m_pThreadMgr = nullptr;
    }
}

// ---------------------------------------------------------------------------
// IUnknown
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;

    // TSF 就是靠这里拿到各个事件槽接口的：请求什么就返回什么
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfTextInputProcessor) ||
        IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    } else if (IsEqualIID(riid, IID_ITfThreadMgrEventSink)) {
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfThreadFocusSink)) {
        *ppv = static_cast<ITfThreadFocusSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfKeyEventSink)) {
        *ppv = static_cast<ITfKeyEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfCompositionSink)) {
        *ppv = static_cast<ITfCompositionSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfDisplayAttributeProvider)) {
        *ppv = static_cast<ITfDisplayAttributeProvider*>(this);
    } else {
        return E_NOINTERFACE;
    }

    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) CTextService::AddRef() {
    return static_cast<ULONG>(::InterlockedIncrement(&m_cRef));
}

STDMETHODIMP_(ULONG) CTextService::Release() {
    const LONG count = ::InterlockedDecrement(&m_cRef);
    if (count == 0) {
        ModuleRelease();   // 与构造函数里的 ModuleAddRef 配对
        delete this;
        return 0;
    }
    return static_cast<ULONG>(count);
}

// ---------------------------------------------------------------------------
// 生命周期
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::OnActivate(ITfThreadMgr* ptim, TfClientId tid) {
    return OnActivateEx(ptim, tid, 0);
}

STDMETHODIMP CTextService::OnActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags) {
    if (ptim == nullptr) return E_INVALIDARG;
    if (m_pThreadMgr != nullptr) {
        LogDebug(L"[MyIME] OnActivateEx 被重复调用，忽略");
        return S_OK;
    }

    m_pThreadMgr = ptim;
    m_pThreadMgr->AddRef();
    m_tid = tid;
    m_dwOwnerThreadId = ::GetCurrentThreadId();

    // 词典是进程级只读数据，第一次用到时加载
    m_engine.SetDictionary(&SharedDictionary());
    m_engine.SetPageSize(7);

    // 1) 线程管理器事件槽：焦点变化 + 上下文栈
    {
        ComPtr<ITfSource> source;
        if (SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfSource, source.PutVoid()))) {
            const HRESULT hrFocus = source->AdviseSink(
                IID_ITfThreadMgrEventSink, static_cast<ITfThreadMgrEventSink*>(this),
                &m_dwThreadMgrEventSinkCookie);
            const HRESULT hrThread = source->AdviseSink(
                IID_ITfThreadFocusSink, static_cast<ITfThreadFocusSink*>(this), &m_dwThreadFocusCookie);
            if (FAILED(hrFocus) || FAILED(hrThread)) {
                LogDebug(L"[MyIME] AdviseSink 失败 focus=0x%08X thread=0x%08X", hrFocus, hrThread);
            }
        } else {
            LogDebug(L"[MyIME] 取 ITfSource 失败：焦点事件收不到");
        }
    }

    // 2) 键盘事件槽：输入法的入口。第三个参数 TRUE 表示只在前台时收键。
    {
        ComPtr<ITfKeystrokeMgr> keystrokeMgr;
        if (SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfKeystrokeMgr, keystrokeMgr.PutVoid()))) {
            const HRESULT hr = keystrokeMgr->AdviseKeyEventSink(
                m_tid, static_cast<ITfKeyEventSink*>(this), TRUE);
            if (FAILED(hr)) LogDebug(L"[MyIME] AdviseKeyEventSink 失败: 0x%08X", hr);
        } else {
            LogDebug(L"[MyIME] 取 ITfKeystrokeMgr 失败：敲键不会有反应");
        }
    }

    // 3) 把显示属性 GUID 注册进 TSF，换回 atom（SetGUID 要用 atom）
    {
        ComPtr<ITfCategoryMgr> categoryMgr;
        if (GetCategoryManager(categoryMgr)) {
            categoryMgr->RegisterGUID(kGuidDisplayAttrInput, &m_atomInput);
            categoryMgr->RegisterGUID(kGuidDisplayAttrConverted, &m_atomConverted);
        } else {
            LogDebug(L"[MyIME] 取 ITfCategoryMgr 失败：组合文字不会有虚线下划线");
        }
    }

    // 4) 注册热键（保留键）
    RegisterPreservedKeys();

    LogDebug(L"[MyIME] 已激活 tid=%u 线程=%lu 环境标志=%s(0x%08X)", m_tid, m_dwOwnerThreadId,
             DescribeActivateFlags(dwFlags).c_str(), dwFlags);
    return S_OK;
}

bool CTextService::GetCategoryManager(ComPtr<ITfCategoryMgr>& categoryMgr) {
    // 方式一：从线程管理器 QI。
    // 优点：不需要 COM 已经初始化，因此在 TF_TMAE_COMLESS（无 COM 环境）下也能用。
    if (m_pThreadMgr != nullptr &&
        SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfCategoryMgr, categoryMgr.PutVoid())) &&
        categoryMgr.Get() != nullptr) {
        return true;
    }

    // 方式二：微软 SampleIME 的路径——直接 CoCreateInstance(CLSID_TF_CategoryMgr)。
    if (SUCCEEDED(::CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                                     IID_ITfCategoryMgr, categoryMgr.PutVoid())) &&
        categoryMgr.Get() != nullptr) {
        return true;
    }

    categoryMgr.Reset();
    return false;
}

STDMETHODIMP CTextService::OnDeactivate() {
    if (m_pThreadMgr == nullptr) return S_OK;

    // 组合还开着就先收尾，否则文档里会残留没上屏的字母
    ResetCompositionState();
    HideCandidateWindow();
    UnregisterPreservedKeys();

    if (m_dwThreadMgrEventSinkCookie != TF_INVALID_COOKIE ||
        m_dwThreadFocusCookie != TF_INVALID_COOKIE) {
        ComPtr<ITfSource> source;
        if (SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfSource, source.PutVoid()))) {
            if (m_dwThreadMgrEventSinkCookie != TF_INVALID_COOKIE) {
                source->UnadviseSink(m_dwThreadMgrEventSinkCookie);
                m_dwThreadMgrEventSinkCookie = TF_INVALID_COOKIE;
            }
            if (m_dwThreadFocusCookie != TF_INVALID_COOKIE) {
                source->UnadviseSink(m_dwThreadFocusCookie);
                m_dwThreadFocusCookie = TF_INVALID_COOKIE;
            }
        }
    }

    {
        ComPtr<ITfKeystrokeMgr> keystrokeMgr;
        if (SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfKeystrokeMgr, keystrokeMgr.PutVoid()))) {
            keystrokeMgr->UnadviseKeyEventSink(m_tid);
        }
    }

    m_pContext.Reset();
    m_pThreadMgr->Release();
    m_pThreadMgr = nullptr;
    m_tid = TF_CLIENTID_NULL;
    LogDebug(L"[MyIME] 已去激活");
    return S_OK;
}

// ---------------------------------------------------------------------------
// ITfThreadMgrEventSink：焦点与上下文
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::OnInitDocumentMgr(ITfDocumentMgr* pdim) {
    return S_OK;
}

STDMETHODIMP CTextService::OnUninitDocumentMgr(ITfDocumentMgr* pdim) {
    return S_OK;
}

STDMETHODIMP CTextService::OnSetFocus(ITfDocumentMgr* pdimFocus, ITfDocumentMgr* pdimPrevFocus) {
    if (pdimFocus == pdimPrevFocus) return S_OK;

    // 注意顺序：先用「旧上下文」把组合收掉，再切到新上下文
    ResetCompositionState();
    HideCandidateWindow();

    m_pContext.Reset();
    if (pdimFocus != nullptr) {
        if (FAILED(pdimFocus->GetTop(m_pContext.Put()))) {
            m_pContext.Reset();
        }
    }
    return S_OK;
}

STDMETHODIMP CTextService::OnPushContext(ITfContext* pic) {
    return S_OK;
}

STDMETHODIMP CTextService::OnPopContext(ITfContext* pic) {
    return S_OK;
}

// ---------------------------------------------------------------------------
// ITfThreadFocusSink：线程级焦点（例如用户切到别的输入法/别的应用）
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::OnSetThreadFocus() {
    return S_OK;
}

STDMETHODIMP CTextService::OnKillThreadFocus() {
    // 线程失去焦点：一定要把组合收掉并关掉候选窗，
    // 否则下次切回来会看到上次残留的字母和候选。
    ResetCompositionState();
    HideCandidateWindow();
    return S_OK;
}

// ---------------------------------------------------------------------------
// ITfKeyEventSink：键盘
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::OnSetFocus(BOOL fForeground) {
    if (!fForeground) {
        HideCandidateWindow();
    }
    return S_OK;
}

STDMETHODIMP CTextService::OnTestKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam,
                                        BOOL* pfEaten) {
    if (pfEaten == nullptr) return E_INVALIDARG;
    *pfEaten = FALSE;
    if (m_pThreadMgr == nullptr) return S_OK;

    TrackContext(pic);

    const KeyInput key = MakeKeyInput(wParam);
    if (!ShouldEatKey(key)) {
        return S_OK;   // 不消费：应用照常收到这个键
    }

    // 同步处理按键时，只要这里返回 TRUE，框架随后就会调用 OnKeyDown。
    //
    // ⚠️ 常见错误（本骨架第一版就踩了）：
    //    在这里调用 ITfKeystrokeMgr::PreserveKey「保证 OnKeyDown 送达」。
    //    PreserveKey 注册的是「保留键」，是热键机制 —— 触发后走的是
    //    OnPreservedKey，压根不经过 OnKeyDown。对普通字母键调它，
    //    相当于把字母注册成热键，行为完全错乱（见 11 章坑清单）。
    //    真正需要 PreserveKey 的场景只有两类：
    //      1) 注册热键（本骨架在 OnActivateEx 里注册了 Ctrl+Space）
    //      2) 键要异步处理，需要把键「按住」不让应用看到
    *pfEaten = TRUE;
    return S_OK;
}

STDMETHODIMP CTextService::OnKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam,
                                    BOOL* pfEaten) {
    if (pfEaten == nullptr) return E_INVALIDARG;
    *pfEaten = FALSE;
    if (m_pThreadMgr == nullptr) return S_OK;

    TrackContext(pic);

    // 上一次没取消成功的组合，借这次机会重试
    if (m_fCancelPending && m_pComposition != nullptr) {
        CancelComposition();
    }

    const KeyInput key = MakeKeyInput(wParam);
    if (!ShouldEatKey(key)) return S_OK;

    const StepResult result = m_engine.Step(key);
    if (!result.eaten) return S_OK;

    *pfEaten = TRUE;
    ApplyStepResult(result);
    return S_OK;
}

STDMETHODIMP CTextService::OnTestKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam,
                                      BOOL* pfEaten) {
    if (pfEaten == nullptr) return E_INVALIDARG;
    // 抬键不处理：本骨架只在按下时动作。
    // 想做到「按键状态完全对称」（有些游戏/快捷键应用会依赖抬键），
    // 就在这里对已消费的键返回 TRUE。
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP CTextService::OnKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) {
    if (pfEaten == nullptr) return E_INVALIDARG;
    *pfEaten = FALSE;
    return S_OK;
}

STDMETHODIMP CTextService::OnPreservedKey(ITfContext* pic, REFGUID rguid, BOOL* pfEaten) {
    if (pfEaten == nullptr) return E_INVALIDARG;
    *pfEaten = FALSE;

    if (!IsEqualGUID(rguid, kGuidPreservedKey)) return S_OK;

    // 保留键（热键）被按下。骨架用它做「中英切换」：
    // 改写 GUID_COMPARTMENT_KEYBOARD_OPENCLOSE，语言栏/系统的输入法状态会跟着变，
    // 而 ShouldEatKey 读的也是同一个 compartment，所以切换立刻生效。
    if (ToggleKeyboardOpen()) {
        LogDebug(L"[MyIME] 中英切换 → %s", IsKeyboardOpen() ? L"中文" : L"英文");
        *pfEaten = TRUE;
    }
    return S_OK;
}

// ---------------------------------------------------------------------------
// 按键判定与状态机驱动
// ---------------------------------------------------------------------------

KeyInput CTextService::MakeKeyInput(WPARAM wParam) const {
    KeyInput key;
    key.vk = static_cast<unsigned>(wParam);
    key.ctrl = (::GetKeyState(VK_CONTROL) & 0x8000) != 0;
    key.alt = (::GetKeyState(VK_MENU) & 0x8000) != 0;
    key.shift = (::GetKeyState(VK_SHIFT) & 0x8000) != 0;
    return key;
}

bool CTextService::ShouldEatKey(const KeyInput& key) {
    // 带 Ctrl / Alt 的组合键留给应用（Ctrl+C、Alt+Tab…）
    // 注意 AltGr = Ctrl+Alt，所以这一条也顺带挡住了 AltGr 组合
    if (key.ctrl || key.alt) return false;

    // TSF 的「中/英」开关：关闭时（英文输入状态）一个键都不该吃
    if (!IsKeyboardOpen()) return false;

    // 宿主/系统明确禁用了键盘：
    if (IsKeyboardDisabled()) return false;

    if (key.vk >= 'A' && key.vk <= 'Z') return true;

    // 组合中要吃掉这些「控制类」按键，否则应用会自己处理（比如回车换行）
    if (m_engine.IsActive()) {
        switch (key.vk) {
            case VK_ESCAPE:
            case VK_BACK:
            case VK_SPACE:
            case VK_RETURN:
            case VK_LEFT:
            case VK_RIGHT:
            case VK_UP:
            case VK_DOWN:
            case VK_PRIOR:
            case VK_NEXT:
            case VK_HOME:
            case VK_END:
            case VK_OEM_MINUS:
            case VK_OEM_PLUS:
            case VK_OEM_7:
                return true;
            default:
                break;
        }
        if (key.vk >= '0' && key.vk <= '9') return true;
    }
    return false;
}

bool CTextService::IsKeyboardOpen() {
    if (m_pThreadMgr == nullptr) return true;

    // GUID_COMPARTMENT_KEYBOARD_OPENCLOSE 是 TSF 维护的「输入法开/关」状态，
    // 由系统的中英切换（Win+Space / 语言栏）改写。
    // 生产实现应该再挂一个 ITfCompartmentEventSink 来缓存它，避免每次按键都查询。
    ComPtr<ITfCompartmentMgr> compartmentMgr;
    if (FAILED(m_pThreadMgr->QueryInterface(IID_ITfCompartmentMgr, compartmentMgr.PutVoid()))) {
        return true;
    }
    ComPtr<ITfCompartment> compartment;
    if (FAILED(compartmentMgr->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE,
                                              compartment.Put()))) {
        return true;
    }

    VARIANT value;
    ::VariantInit(&value);
    bool open = true;
    if (SUCCEEDED(compartment->GetValue(m_tid, &value))) {
        if (value.vt == VT_I4) {
            open = (value.lVal != 0);
        } else if (value.vt == VT_BOOL) {
            open = (value.boolVal != VARIANT_FALSE);
        }
    }
    ::VariantClear(&value);
    return open;
}

bool CTextService::IsKeyboardDisabled() {
    if (m_pThreadMgr == nullptr) return false;

    // GUID_COMPARTMENT_KEYBOARD_DISABLED：由系统/宿主设置的「键盘已禁用」标志。
    // 置位时输入法不应该吃掉任何按键（微软 SampleIME 的 _IsKeyEaten 也检查它）。
    ComPtr<ITfCompartmentMgr> compartmentMgr;
    if (FAILED(m_pThreadMgr->QueryInterface(IID_ITfCompartmentMgr, compartmentMgr.PutVoid()))) {
        return false;
    }
    ComPtr<ITfCompartment> compartment;
    if (FAILED(compartmentMgr->GetCompartment(GUID_COMPARTMENT_KEYBOARD_DISABLED,
                                              compartment.Put()))) {
        return false;
    }

    VARIANT value;
    ::VariantInit(&value);
    bool disabled = false;
    if (SUCCEEDED(compartment->GetValue(m_tid, &value))) {
        if (value.vt == VT_I4) {
            disabled = (value.lVal != 0);
        } else if (value.vt == VT_BOOL) {
            disabled = (value.boolVal != VARIANT_FALSE);
        }
    }
    ::VariantClear(&value);
    return disabled;
}

bool CTextService::ToggleKeyboardOpen() {
    if (m_pThreadMgr == nullptr) return false;

    ComPtr<ITfCompartmentMgr> compartmentMgr;
    if (FAILED(m_pThreadMgr->QueryInterface(IID_ITfCompartmentMgr, compartmentMgr.PutVoid()))) {
        return false;
    }
    ComPtr<ITfCompartment> compartment;
    if (FAILED(compartmentMgr->GetCompartment(GUID_COMPARTMENT_KEYBOARD_OPENCLOSE,
                                              compartment.Put()))) {
        return false;
    }

    const bool open = IsKeyboardOpen();
    VARIANT value;
    ::VariantInit(&value);
    value.vt = VT_I4;
    value.lVal = open ? 0 : 1;   // 中英互换

    const HRESULT hr = compartment->SetValue(m_tid, &value);
    ::VariantClear(&value);
    if (FAILED(hr)) LogDebug(L"[MyIME] SetValue(OPENCLOSE) 失败 0x%08X", hr);
    return SUCCEEDED(hr);
}

void CTextService::RegisterPreservedKeys() {
    if (m_pThreadMgr == nullptr) return;

    ComPtr<ITfKeystrokeMgr> keystrokeMgr;
    if (FAILED(m_pThreadMgr->QueryInterface(IID_ITfKeystrokeMgr, keystrokeMgr.PutVoid()))) {
        return;
    }

    // 保留键 = 热键。注册成功后，这个组合键不会再进应用，而是回调 OnPreservedKey。
    // 骨架注册 Ctrl+Space（经典的中英切换）。
    // 注意：如果系统/别的输入法已经占用了这个组合，PreserveKey 会返回 ok=FALSE，
    //      此时热键不生效（不是错误），改用 Win+Space 或语言栏切换即可。
    TF_PRESERVEDKEY key = {};
    key.uVKey = VK_SPACE;
    key.uModifiers = TF_MOD_CONTROL;

    BOOL ok = FALSE;
    const HRESULT hr = keystrokeMgr->PreserveKey(m_tid, kGuidPreservedKey, &key, &ok);
    m_fPreservedKeyRegistered = SUCCEEDED(hr) && ok;
    if (m_fPreservedKeyRegistered) {
        m_preservedKey = key;
        LogDebug(L"[MyIME] 已注册热键 Ctrl+Space（中英切换）");
    } else {
        LogDebug(L"[MyIME] Ctrl+Space 注册失败(hr=0x%08X ok=%d)：可能已被系统占用", hr, ok);
    }
}

void CTextService::UnregisterPreservedKeys() {
    if (!m_fPreservedKeyRegistered || m_pThreadMgr == nullptr) return;

    ComPtr<ITfKeystrokeMgr> keystrokeMgr;
    if (SUCCEEDED(m_pThreadMgr->QueryInterface(IID_ITfKeystrokeMgr, keystrokeMgr.PutVoid()))) {
        keystrokeMgr->UnpreserveKey(kGuidPreservedKey, &m_preservedKey);
    }
    m_fPreservedKeyRegistered = false;
}

void CTextService::ApplyStepResult(const StepResult& result) {
    if (!result.commitText.empty()) {
        CommitComposition(result.commitText);
    } else if (result.cancelComposition) {
        CancelComposition();
    } else if (result.changed && m_engine.IsActive()) {
        // 组合文字用「原始按键序列」（nihao），和微软拼音的行为一致
        const std::wstring& text = m_engine.Raw();
        if (m_pComposition != nullptr) {
            UpdateCompositionText(text);
        } else {
            StartComposition(text);
        }
    }

    if (m_engine.IsActive()) {
        UpdateCandidateWindow();
    } else {
        HideCandidateWindow();
    }
}

void CTextService::TrackContext(ITfContext* context) {
    if (context == nullptr) return;
    if (m_pContext.Get() == context) return;
    context->AddRef();
    m_pContext.Attach(context);
}

void CTextService::HideCandidateWindow() {
    m_candidateWindow.Hide();
}

void CTextService::ResetCompositionState() {
    if (m_pComposition != nullptr) {
        CancelComposition();
    }
    m_engine.Reset();
}

// ---------------------------------------------------------------------------
// 组合操作：全部通过编辑会话完成
// ---------------------------------------------------------------------------

HRESULT CTextService::StartComposition(const std::wstring& text) {
    if (m_pContext == nullptr) return E_UNEXPECTED;
    auto* session = new (std::nothrow)
        CEditSession(this, m_pContext.Get(), EditAction::StartComposition, text);
    if (session == nullptr) return E_OUTOFMEMORY;

    ComPtr<CEditSession> holder;
    holder.Attach(session);
    return RequestEditSession(m_pContext.Get(), m_tid, session, EditSessionMode::SyncWrite);
}

HRESULT CTextService::UpdateCompositionText(const std::wstring& text) {
    if (m_pContext == nullptr) return E_UNEXPECTED;
    auto* session = new (std::nothrow)
        CEditSession(this, m_pContext.Get(), EditAction::UpdateComposition, text);
    if (session == nullptr) return E_OUTOFMEMORY;

    ComPtr<CEditSession> holder;
    holder.Attach(session);
    return RequestEditSession(m_pContext.Get(), m_tid, session, EditSessionMode::AsyncWrite);
}

HRESULT CTextService::CommitComposition(const std::wstring& text) {
    if (m_pContext == nullptr) return E_UNEXPECTED;
    auto* session = new (std::nothrow)
        CEditSession(this, m_pContext.Get(), EditAction::CommitComposition, text);
    if (session == nullptr) return E_OUTOFMEMORY;

    ComPtr<CEditSession> holder;
    holder.Attach(session);
    return RequestEditSession(m_pContext.Get(), m_tid, session, EditSessionMode::SyncWrite);
}

HRESULT CTextService::CancelComposition() {
    if (m_pComposition == nullptr) return S_OK;
    if (m_pContext == nullptr) {
        m_pComposition.Reset();
        return S_OK;
    }

    auto* session = new (std::nothrow)
        CEditSession(this, m_pContext.Get(), EditAction::CancelComposition, std::wstring());
    if (session == nullptr) return E_OUTOFMEMORY;

    ComPtr<CEditSession> holder;
    holder.Attach(session);
    const HRESULT hr =
        RequestEditSession(m_pContext.Get(), m_tid, session, EditSessionMode::SyncWrite);
    if (FAILED(hr)) {
        // 没跑成：保留组合指针并记下「待取消」，下次按键重试，
        // 绝不能直接放掉指针——那会让文档里留下一个没人管的组合。
        m_fCancelPending = true;
        LogDebug(L"[MyIME] 取消组合失败(0x%08X)，稍后重试", hr);
        return hr;
    }
    m_fCancelPending = false;
    return S_OK;
}

// ---------------------------------------------------------------------------
// 编辑会话内部实现（此时持有文档锁 ec）
// ---------------------------------------------------------------------------

HRESULT CTextService::OnEditSession(EditAction action, TfEditCookie ec, const std::wstring& text) {
    if (m_pContext == nullptr) return E_UNEXPECTED;

    switch (action) {
        case EditAction::StartComposition: {
            if (m_pComposition == nullptr) {
                ComPtr<ITfRange> range;
                // 用 ITfInsertAtSelection + TF_IAS_QUERYONLY 取插入点范围：
                // 应用没有选区时也能拿到，比单纯依赖 GetSelection 稳。
                HRESULT hr = GetInsertionRange(ec, range.Put());
                if (FAILED(hr)) {
                    // 取不到插入点：文档可能只读、或没有焦点。放弃本次组合，
                    // 下次按键会再试。
                    LogDebug(L"[MyIME] 取插入点失败 0x%08X", hr);
                    return hr;
                }

                ComPtr<ITfContextComposition> contextComposition;
                hr = m_pContext->QueryInterface(IID_ITfContextComposition,
                                                contextComposition.PutVoid());
                if (FAILED(hr)) return hr;

                hr = contextComposition->StartComposition(
                    m_pContext.Get(), range.Get(), static_cast<ITfCompositionSink*>(this),
                    m_pComposition.Put());
                if (FAILED(hr)) {
                    LogDebug(L"[MyIME] StartComposition 失败 0x%08X", hr);
                    return hr;
                }
                // 契约细节：StartComposition 返回 S_OK 但组合指针为空 = 文档宿主拒绝了这次组合。
                // 必须当成失败处理，否则后面 SetCompositionText 会拿到空指针。
                if (m_pComposition.Get() == nullptr) {
                    LogDebug(L"[MyIME] StartComposition 返回成功但组合为空：宿主拒绝了组合");
                    return E_FAIL;
                }
            }
            return SetCompositionText(ec, text);
        }

        case EditAction::UpdateComposition: {
            if (m_pComposition == nullptr) return S_OK;   // 组合已被结束：忽略
            return SetCompositionText(ec, text);
        }

        case EditAction::CommitComposition: {
            if (m_pComposition != nullptr) {
                // 先把组合文字改成最终文字，再结束组合 —— 这样文档里留下的是结果
                SetCompositionText(ec, text);

                ComPtr<ITfRange> range;
                if (SUCCEEDED(m_pComposition->GetRange(range.Put()))) {
                    // 去掉「未上屏」的显示属性（否则结果文字会一直带虚线下划线）
                    ClearDisplayAttribute(ec, range.Get());
                    ComPtr<ITfRange> caret;
                    if (SUCCEEDED(range->Clone(caret.Put()))) {
                        caret->Collapse(ec, TF_ANCHOR_END);
                        ITfRange* selection[1] = {caret.Get()};
                        m_pContext->SetSelection(ec, 1, selection);
                    }
                }
                m_pComposition->EndComposition(ec);
                m_pComposition.Reset();
            } else {
                // 组合已经被应用结束：直接把文字插到光标处
                InsertTextAtSelection(ec, text);
            }
            return S_OK;
        }

        case EditAction::CancelComposition: {
            if (m_pComposition != nullptr) {
                // 取消 = 连文档里的字母一起撤掉，而不是留着字母结束组合
                ComPtr<ITfRange> range;
                if (SUCCEEDED(m_pComposition->GetRange(range.Put()))) {
                    range->SetText(ec, 0, L"", 0);
                    ClearDisplayAttribute(ec, range.Get());
                    range->Collapse(ec, TF_ANCHOR_START);
                    ITfRange* selection[1] = {range.Get()};
                    m_pContext->SetSelection(ec, 1, selection);
                }
                m_pComposition->EndComposition(ec);
                m_pComposition.Reset();
            }
            return S_OK;
        }

        case EditAction::QueryAnchor: {
            // 只读：算出组合位置的屏幕矩形，供候选窗摆位
            m_anchorValid = false;
            ComPtr<ITfContextView> view;
            if (FAILED(m_pContext->GetActiveView(view.Put())) || view.Get() == nullptr) {
                return S_OK;
            }

            ComPtr<ITfRange> range;
            if (m_pComposition != nullptr) {
                if (FAILED(m_pComposition->GetRange(range.Put()))) return S_OK;
            } else if (FAILED(GetInsertionRange(ec, range.Put()))) {
                return S_OK;
            }

            ComPtr<ITfRange> point;
            if (FAILED(range->Clone(point.Put()))) return S_OK;
            if (FAILED(point->Collapse(ec, TF_ANCHOR_START))) return S_OK;

            RECT rect = {};
            BOOL clipped = FALSE;
            if (SUCCEEDED(view->GetTextExt(ec, point.Get(), &rect, &clipped))) {
                // 文档还没渲染 / 窗口最小化 / 文本不在可视区时，GetTextExt 会返回
                // S_OK 但矩形是全 0 —— 这种情况绝不能把候选窗摆到屏幕左上角。
                const bool empty =
                    (rect.left == 0 && rect.top == 0 && rect.right == 0 && rect.bottom == 0);
                if (!empty) {
                    // 折叠范围的矩形宽度可能为 0：给个最小尺寸便于摆放
                    if (rect.right <= rect.left) rect.right = rect.left + 1;
                    if (rect.bottom <= rect.top) rect.bottom = rect.top + 1;
                    m_anchorRect = rect;
                    m_anchorValid = true;
                    return S_OK;
                }
                LogDebug(L"[MyIME] GetTextExt 返回空矩形（文本未渲染？）");
            }

            // 有些应用不实现 GetTextExt（或返回 TF_E_NOLAYOUT）：退化成整个视图区域
            RECT screen = {};
            if (SUCCEEDED(view->GetScreenExt(&screen))) {
                m_anchorRect.left = screen.left;
                m_anchorRect.top = screen.bottom;
                m_anchorRect.right = screen.left + 1;
                m_anchorRect.bottom = screen.bottom + 1;
                m_anchorValid = true;
            }
            return S_OK;
        }
    }
    return E_INVALIDARG;
}

HRESULT CTextService::GetSelectionRange(TfEditCookie ec, ITfRange** ppRange) {
    if (ppRange == nullptr) return E_INVALIDARG;
    *ppRange = nullptr;

    ComPtr<ITfRange> range;
    ULONG fetched = 0;
    const HRESULT hr =
        m_pContext->GetSelection(ec, TF_DEFAULT_SELECTION, 1, range.Put(), &fetched);
    if (FAILED(hr)) return hr;
    if (fetched == 0 || range.Get() == nullptr) return E_FAIL;

    *ppRange = range.Detach();
    return S_OK;
}

HRESULT CTextService::GetInsertionRange(TfEditCookie ec, ITfRange** ppRange) {
    if (ppRange == nullptr) return E_INVALIDARG;
    *ppRange = nullptr;

    // 首选 ITfInsertAtSelection + TF_IAS_QUERYONLY：只查询、不改文档，
    // 而且即使应用当前没有选区也能拿到插入点范围。
    ComPtr<ITfInsertAtSelection> insertAtSelection;
    if (SUCCEEDED(m_pContext->QueryInterface(IID_ITfInsertAtSelection,
                                            insertAtSelection.PutVoid()))) {
        ComPtr<ITfRange> range;
        const HRESULT hr = insertAtSelection->InsertTextAtSelection(ec, TF_IAS_QUERYONLY, nullptr, 0,
                                                                   range.Put());
        if (SUCCEEDED(hr) && range.Get() != nullptr) {
            *ppRange = range.Detach();
            return S_OK;
        }
    }

    // 兜底：当前选区（应用有选区时，选区起点才是正确的插入点）
    return GetSelectionRange(ec, ppRange);
}

HRESULT CTextService::ClearDisplayAttribute(TfEditCookie ec, ITfRange* range) {
    if (range == nullptr) return E_INVALIDARG;

    // 清显示属性要用「属性对象 + VT_EMPTY」。
    // ⚠️ ITfRange 没有 ClearGUID 方法（网上有资料这么写，是错的）——
    //    ITfRange 上只有 SetGUID / GetGUID。清除必须走 ITfProperty。
    ComPtr<ITfProperty> property;
    if (FAILED(m_pContext->GetProperty(GUID_PROP_ATTRIBUTE, property.Put()))) return E_FAIL;

    VARIANT value;
    ::VariantInit(&value);   // VT_EMPTY = 清除该属性
    const HRESULT hr = property->SetValue(ec, range, &value);
    ::VariantClear(&value);
    return hr;
}

HRESULT CTextService::SetCompositionText(TfEditCookie ec, const std::wstring& text) {
    if (m_pComposition == nullptr) return E_UNEXPECTED;

    ComPtr<ITfRange> range;
    HRESULT hr = m_pComposition->GetRange(range.Put());
    if (FAILED(hr)) return hr;

    hr = range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size()));
    if (FAILED(hr)) return hr;

    // 挂上「未上屏」显示属性：用户才能看出这些字还没提交
    if (m_atomInput != TF_INVALID_GUIDATOM) {
        range->SetGUID(ec, GUID_PROP_ATTRIBUTE, m_atomInput);
    }

    // 光标移到组合末尾。少了这一步，部分应用会把光标留在组合内部。
    ComPtr<ITfRange> caret;
    if (SUCCEEDED(range->Clone(caret.Put()))) {
        if (SUCCEEDED(caret->Collapse(ec, TF_ANCHOR_END))) {
            ITfRange* selection[1] = {caret.Get()};
            m_pContext->SetSelection(ec, 1, selection);
        }
    }
    return S_OK;
}

HRESULT CTextService::InsertTextAtSelection(TfEditCookie ec, const std::wstring& text) {
    if (text.empty()) return S_OK;

    ComPtr<ITfRange> range;
    HRESULT hr = GetSelectionRange(ec, range.Put());
    if (FAILED(hr)) return hr;

    hr = range->SetText(ec, 0, text.c_str(), static_cast<LONG>(text.size()));
    if (FAILED(hr)) return hr;

    ComPtr<ITfRange> caret;
    if (SUCCEEDED(range->Clone(caret.Put()))) {
        if (SUCCEEDED(caret->Collapse(ec, TF_ANCHOR_END))) {
            ITfRange* selection[1] = {caret.Get()};
            m_pContext->SetSelection(ec, 1, selection);
        }
    }
    return S_OK;
}

// ---------------------------------------------------------------------------
// 候选窗
// ---------------------------------------------------------------------------

void CTextService::UpdateCandidateWindow() {
    if (!m_engine.IsActive() || m_engine.Candidates().empty() || m_pContext == nullptr) {
        HideCandidateWindow();
        return;
    }

    // 位置必须用 GetTextExt 取，而它需要文档锁 → 也放进只读编辑会话里算
    m_anchorValid = false;
    auto* session = new (std::nothrow)
        CEditSession(this, m_pContext.Get(), EditAction::QueryAnchor, std::wstring());
    if (session != nullptr) {
        ComPtr<CEditSession> holder;
        holder.Attach(session);
        RequestEditSession(m_pContext.Get(), m_tid, session, EditSessionMode::SyncRead);
    }

    // 把候选窗挂到宿主应用的焦点窗口上：候选窗会浮在它上方，并随它一起销毁。
    // GetWnd 不需要编辑会话（没有 ec 参数）。
    {
        ComPtr<ITfContextView> view;
        if (SUCCEEDED(m_pContext->GetActiveView(view.Put())) && view.Get() != nullptr) {
            HWND hostWindow = nullptr;
            if (SUCCEEDED(view->GetWnd(&hostWindow)) && hostWindow != nullptr) {
                m_candidateWindow.SetOwner(hostWindow);
            }
        }
    }

    m_candidateWindow.Show(m_engine.Candidates(), m_engine.PageStart(), m_engine.PageSize(),
                           m_engine.SelectedIndex(), m_engine.SegmentationText(), m_anchorRect,
                           m_anchorValid);
}

// ---------------------------------------------------------------------------
// ITfCompositionSink
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::OnCompositionTerminated(TfEditCookie ecWrite,
                                                  ITfComposition* pComposition) {
    // 应用把我们的组合干掉了（例如它自己清空了文档、或用户点了别处）。
    // 这里绝对不能再调用 EndComposition，只能把本地状态清干净。
    LogDebug(L"[MyIME] 组合被外部结束，清理本地状态");

    if (pComposition == nullptr || m_pComposition.Get() == pComposition) {
        m_pComposition.Reset();
    }
    m_engine.Reset();
    m_fCancelPending = false;
    HideCandidateWindow();
    return S_OK;
}

// ---------------------------------------------------------------------------
// ITfDisplayAttributeProvider
// ---------------------------------------------------------------------------

STDMETHODIMP CTextService::EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) {
    if (ppEnum == nullptr) return E_INVALIDARG;
    *ppEnum = nullptr;

    ITfDisplayAttributeInfo* items[2] = {nullptr, nullptr};
    CreateDisplayAttributeInfos(&items[0], &items[1]);

    auto* enumerator = new (std::nothrow) CDisplayAttributeInfoEnumerator(items, 2);
    for (auto* item : items) {
        if (item != nullptr) item->Release();
    }
    if (enumerator == nullptr) return E_OUTOFMEMORY;

    *ppEnum = enumerator;
    return S_OK;
}

STDMETHODIMP CTextService::GetDisplayAttributeInfo(REFGUID guid,
                                                  ITfDisplayAttributeInfo** ppInfo) {
    if (ppInfo == nullptr) return E_INVALIDARG;
    *ppInfo = nullptr;

    if (IsEqualGUID(guid, kGuidDisplayAttrInput)) {
        *ppInfo = new (std::nothrow) CDisplayAttributeInfo(kGuidDisplayAttrInput, L"输入中", false);
    } else if (IsEqualGUID(guid, kGuidDisplayAttrConverted)) {
        *ppInfo =
            new (std::nothrow) CDisplayAttributeInfo(kGuidDisplayAttrConverted, L"已转换", true);
    } else {
        return E_INVALIDARG;
    }
    return (*ppInfo != nullptr) ? S_OK : E_OUTOFMEMORY;
}

}  // namespace myime
