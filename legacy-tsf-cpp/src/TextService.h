// TextService.h —— 文本服务主体
//
// 一个 TSF 输入法的核心就是这一个 COM 对象。它同时实现：
//   ITfTextInputProcessorEx   生命周期入口（激活/去激活）
//   ITfThreadMgrEventSink     焦点、上下文栈变化
//   ITfThreadFocusSink        线程级焦点（切到别的输入法/应用时收尾）
//   ITfKeyEventSink           键盘事件（输入法的入口）
//   ITfCompositionSink        组合被外部结束时的清理
//   ITfDisplayAttributeProvider 组合文字的显示属性
//
// 之所以「一个对象实现所有接口」，是因为 TSF 就是通过 QI 从文本服务对象上
// 发现这些接口的：把一个对象实现得越多，需要维护的生命周期就越简单。
// （SampleIME 选择拆成多个类，好处是生命周期独立，代价是引用计数更啰嗦。）
#pragma once

#include "CandidateWindow.h"
#include "Common.h"
#include "Engine.h"

namespace myime {

enum class EditAction;
enum class EditSessionMode;

class CTextService : public ITfTextInputProcessorEx,
                     public ITfThreadMgrEventSink,
                     public ITfThreadFocusSink,
                     public ITfKeyEventSink,
                     public ITfCompositionSink,
                     public ITfDisplayAttributeProvider {
public:
    CTextService();

    // ---- IUnknown ----
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ---- ITfTextInputProcessor / ITfTextInputProcessorEx ----
    STDMETHODIMP OnActivate(ITfThreadMgr* ptim, TfClientId tid) override;
    STDMETHODIMP OnDeactivate() override;
    STDMETHODIMP OnActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags) override;

    // ---- ITfThreadMgrEventSink ----
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr* pdim) override;
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr* pdim) override;
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* pdimFocus, ITfDocumentMgr* pdimPrevFocus) override;
    STDMETHODIMP OnPushContext(ITfContext* pic) override;
    STDMETHODIMP OnPopContext(ITfContext* pic) override;

    // ---- ITfThreadFocusSink ----
    STDMETHODIMP OnSetThreadFocus() override;
    STDMETHODIMP OnKillThreadFocus() override;

    // ---- ITfKeyEventSink ----
    // 注意：这个 OnSetFocus(BOOL) 与上面 ITfThreadMgrEventSink 的 OnSetFocus 是两个
    // 不同接口的同名方法，C++ 里靠参数列表区分（两个都必须实现）。
    STDMETHODIMP OnSetFocus(BOOL fForeground) override;
    STDMETHODIMP OnTestKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnTestKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
    STDMETHODIMP OnPreservedKey(ITfContext* pic, REFGUID rguid, BOOL* pfEaten) override;

    // ---- ITfCompositionSink ----
    STDMETHODIMP OnCompositionTerminated(TfEditCookie ecWrite, ITfComposition* pComposition) override;

    // ---- ITfDisplayAttributeProvider ----
    STDMETHODIMP EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) override;
    STDMETHODIMP GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) override;

    // ---- 内部：由 CEditSession 回调（编辑会话内部，持有文档锁）----
    HRESULT OnEditSession(EditAction action, TfEditCookie ec, const std::wstring& text);

private:
    ~CTextService();

    // 键盘处理
    KeyInput MakeKeyInput(WPARAM wParam) const;
    bool ShouldEatKey(const KeyInput& key);
    bool IsKeyboardOpen();              // 读 TSF 的「中/英」开关 compartment
    bool IsKeyboardDisabled();          // 读 TSF 的「键盘已禁用」compartment
    bool ToggleKeyboardOpen();          // 改写上面的开关（热键用）
    void RegisterPreservedKeys();       // 注册保留键（热键）
    void UnregisterPreservedKeys();
    void ApplyStepResult(const StepResult& result);

    // 取 ITfCategoryMgr：先从线程管理器 QI（不依赖 COM 初始化），失败再 CoCreateInstance
    bool GetCategoryManager(ComPtr<ITfCategoryMgr>& categoryMgr);

    // 组合（组合的读写一律通过编辑会话，见 TextService.cpp）
    HRESULT StartComposition(const std::wstring& text);
    HRESULT UpdateCompositionText(const std::wstring& text);
    HRESULT CommitComposition(const std::wstring& text);
    HRESULT CancelComposition();

    // 编辑会话内部使用的原始操作（ec 是文档锁，只在 DoEditSession 里有效）
    HRESULT GetSelectionRange(TfEditCookie ec, ITfRange** ppRange);
    HRESULT GetInsertionRange(TfEditCookie ec, ITfRange** ppRange);
    HRESULT SetCompositionText(TfEditCookie ec, const std::wstring& text);
    HRESULT ClearDisplayAttribute(TfEditCookie ec, ITfRange* range);
    HRESULT InsertTextAtSelection(TfEditCookie ec, const std::wstring& text);

    void UpdateCandidateWindow();
    void HideCandidateWindow();
    void ResetCompositionState();
    void TrackContext(ITfContext* context);

    LONG m_cRef = 1;

    ITfThreadMgr* m_pThreadMgr = nullptr;   // 本线程的 TSF 管理器（不跨线程使用）
    TfClientId m_tid = TF_CLIENTID_NULL;
    DWORD m_dwThreadMgrEventSinkCookie = TF_INVALID_COOKIE;
    DWORD m_dwThreadFocusCookie = TF_INVALID_COOKIE;

    ComPtr<ITfContext> m_pContext;          // 当前焦点所在上下文
    ComPtr<ITfComposition> m_pComposition;  // 当前组合（未上屏的文字）

    TfGuidAtom m_atomInput = TF_INVALID_GUIDATOM;
    TfGuidAtom m_atomConverted = TF_INVALID_GUIDATOM;

    // 保留键（热键）。
    // ⚠️ 注意语义：PreserveKey 注册的是「热键」，触发后由 OnPreservedKey 处理，
    //    不会走 OnKeyDown。它不是「保证 OnKeyDown 一定被调用」的手段。
    TF_PRESERVEDKEY m_preservedKey = {};
    bool m_fPreservedKeyRegistered = false;

    // 组合没能及时取消（编辑会话被拒）时置位，下次有机会再收尾
    bool m_fCancelPending = false;

    // 候选窗定位（在只读编辑会话里算出来的屏幕矩形）
    RECT m_anchorRect = {};
    bool m_anchorValid = false;

    Engine m_engine;
    CandidateWindow m_candidateWindow;
    DWORD m_dwOwnerThreadId = 0;
};

}  // namespace myime
