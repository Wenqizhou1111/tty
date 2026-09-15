// DisplayAttribute.cpp
#include "DisplayAttribute.h"

#include "Guids.h"

#include <new>   // std::nothrow

namespace myime {

CDisplayAttributeInfo::CDisplayAttributeInfo(const GUID& guid, const wchar_t* description,
                                             bool converted)
    : m_guid(guid), m_description(description != nullptr ? description : L""), m_converted(converted) {}

STDMETHODIMP CDisplayAttributeInfo::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfDisplayAttributeInfo)) {
        *ppv = static_cast<ITfDisplayAttributeInfo*>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) CDisplayAttributeInfo::AddRef() {
    return static_cast<ULONG>(::InterlockedIncrement(&m_cRef));
}

STDMETHODIMP_(ULONG) CDisplayAttributeInfo::Release() {
    const LONG count = ::InterlockedDecrement(&m_cRef);
    if (count == 0) delete this;
    return static_cast<ULONG>(count);
}

STDMETHODIMP CDisplayAttributeInfo::GetGUID(GUID* pguid) {
    if (pguid == nullptr) return E_INVALIDARG;
    *pguid = m_guid;
    return S_OK;
}

STDMETHODIMP CDisplayAttributeInfo::GetDescription(BSTR* pbstrDesc) {
    if (pbstrDesc == nullptr) return E_INVALIDARG;
    *pbstrDesc = ::SysAllocString(m_description.c_str());
    return (*pbstrDesc != nullptr) ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP CDisplayAttributeInfo::GetAttributeInfo(TF_DISPLAYATTRIBUTE* pda) {
    if (pda == nullptr) return E_INVALIDARG;

    // 全部清零后再设字段：没设的字段保持 0，正好等于
    // TF_DA_COLOR_NONE / TF_DA_LINESTYLE_NONE，语义上「不改变」
    ::ZeroMemory(pda, sizeof(*pda));

    pda->crText.type = TF_DA_COLOR_NONE;   // 文字颜色跟随文档
    pda->crBk.type = TF_DA_COLOR_NONE;     // 背景不覆盖

    if (m_converted) {
        pda->lsStyle = TF_DA_LINESTYLE_SOLID;
        pda->fBoldLine = TRUE;
        pda->laInfo = TF_ATTR_TARGET_CONVERTED;
    } else {
        // 未上屏的输入：虚线下划线（中文输入法最常见的样子）
        pda->lsStyle = TF_DA_LINESTYLE_DOTTED;
        pda->fBoldLine = FALSE;
        pda->laInfo = TF_ATTR_INPUT;
    }

    pda->crLine.type = TF_DA_COLOR_RGB;
    // 下划线颜色：未上屏用深灰，已转换用蓝色
    pda->crLine.cr = m_converted ? RGB(0x00, 0x66, 0xCC) : RGB(0x40, 0x40, 0x40);
    return S_OK;
}

STDMETHODIMP CDisplayAttributeInfo::SetAttributeInfo(const TF_DISPLAYATTRIBUTE* pda) {
    // 骨架里显示属性是硬编码的皮肤，不接受外部修改。
    // 要做「可换肤/可配置」就在这里把 pda 存下来，并让 GetAttributeInfo 返回它。
    return (pda == nullptr) ? E_INVALIDARG : S_OK;
}

STDMETHODIMP CDisplayAttributeInfo::Reset() {
    return S_OK;   // 恢复默认外观（本骨架没有可改的配置）
}

// ---------------------------------------------------------------------------

CDisplayAttributeInfoEnumerator::CDisplayAttributeInfoEnumerator(ITfDisplayAttributeInfo** items,
                                                                 ULONG count) {
    for (ULONG i = 0; i < count; ++i) {
        if (items[i] != nullptr) {
            items[i]->AddRef();
            m_items.push_back(items[i]);
        }
    }
}

CDisplayAttributeInfoEnumerator::~CDisplayAttributeInfoEnumerator() {
    for (auto* item : m_items) item->Release();
}

STDMETHODIMP CDisplayAttributeInfoEnumerator::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;
    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumTfDisplayAttributeInfo)) {
        *ppv = static_cast<IEnumTfDisplayAttributeInfo*>(this);
        AddRef();
        return S_OK;
    }
    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) CDisplayAttributeInfoEnumerator::AddRef() {
    return static_cast<ULONG>(::InterlockedIncrement(&m_cRef));
}

STDMETHODIMP_(ULONG) CDisplayAttributeInfoEnumerator::Release() {
    const LONG count = ::InterlockedDecrement(&m_cRef);
    if (count == 0) delete this;
    return static_cast<ULONG>(count);
}

STDMETHODIMP CDisplayAttributeInfoEnumerator::Clone(IEnumTfDisplayAttributeInfo** ppEnum) {
    if (ppEnum == nullptr) return E_INVALIDARG;
    *ppEnum = nullptr;
    auto* clone = new (std::nothrow) CDisplayAttributeInfoEnumerator(
        m_items.empty() ? nullptr : &m_items[0], static_cast<ULONG>(m_items.size()));
    if (clone == nullptr) return E_OUTOFMEMORY;
    clone->m_index = m_index;
    *ppEnum = clone;
    return S_OK;
}

STDMETHODIMP CDisplayAttributeInfoEnumerator::Next(ULONG ulCount,
                                                   ITfDisplayAttributeInfo** rgInfo,
                                                   ULONG* pcFetched) {
    if (rgInfo == nullptr) return E_INVALIDARG;
    if (pcFetched == nullptr && ulCount != 1) return E_INVALIDARG;

    ULONG fetched = 0;
    while (fetched < ulCount && m_index < m_items.size()) {
        rgInfo[fetched] = m_items[m_index];
        rgInfo[fetched]->AddRef();
        ++fetched;
        ++m_index;
    }
    if (pcFetched != nullptr) *pcFetched = fetched;
    return (fetched == ulCount) ? S_OK : S_FALSE;
}

STDMETHODIMP CDisplayAttributeInfoEnumerator::Reset() {
    m_index = 0;
    return S_OK;
}

STDMETHODIMP CDisplayAttributeInfoEnumerator::Skip(ULONG ulCount) {
    const size_t target = m_index + ulCount;
    if (target >= m_items.size()) {
        m_index = m_items.size();
        return S_FALSE;
    }
    m_index = target;
    return S_OK;
}

void CreateDisplayAttributeInfos(ITfDisplayAttributeInfo** input,
                                 ITfDisplayAttributeInfo** converted) {
    if (input != nullptr) {
        *input = new (std::nothrow) CDisplayAttributeInfo(kGuidDisplayAttrInput, L"输入中", false);
    }
    if (converted != nullptr) {
        *converted =
            new (std::nothrow) CDisplayAttributeInfo(kGuidDisplayAttrConverted, L"已转换", true);
    }
}

}  // namespace myime
