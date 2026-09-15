// DisplayAttribute.h —— 显示属性：告诉 TSF「组合中的文字该怎么画」
//
// 没有这一步，组合文字会以「普通文字」的样子出现在文档里，用户看不出
// 哪些字还没上屏（更糟的是有些应用会把它当成已提交文字）。
// TSF 的流程是：
//   1) 文本服务实现 ITfDisplayAttributeProvider，TSF 用 QI 从文本服务上取到它
//   2) 文本服务把属性 GUID 注册进 ITfCategoryMgr::RegisterGUID，换回一个 TfGuidAtom
//   3) 用 ITfRange::SetGUID(ec, GUID_PROP_ATTRIBUTE, atom) 把属性挂到组合范围上
#pragma once

#include "Common.h"

namespace myime {

// 单个显示属性的描述（颜色、下划线样式）
class CDisplayAttributeInfo : public ITfDisplayAttributeInfo {
public:
    CDisplayAttributeInfo(const GUID& guid, const wchar_t* description, bool converted);
    ~CDisplayAttributeInfo() = default;

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfDisplayAttributeInfo
    STDMETHODIMP GetGUID(GUID* pguid) override;
    STDMETHODIMP GetDescription(BSTR* pbstrDesc) override;
    STDMETHODIMP GetAttributeInfo(TF_DISPLAYATTRIBUTE* pda) override;
    STDMETHODIMP SetAttributeInfo(const TF_DISPLAYATTRIBUTE* pda) override;
    STDMETHODIMP Reset() override;

private:
    LONG m_cRef = 1;
    GUID m_guid;
    std::wstring m_description;
    bool m_converted = false;
};

// 枚举器：把两个（输入中 / 已转换）显示属性交给 TSF
class CDisplayAttributeInfoEnumerator : public IEnumTfDisplayAttributeInfo {
public:
    CDisplayAttributeInfoEnumerator(ITfDisplayAttributeInfo** items, ULONG count);
    ~CDisplayAttributeInfoEnumerator();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // IEnumTfDisplayAttributeInfo
    STDMETHODIMP Clone(IEnumTfDisplayAttributeInfo** ppEnum) override;
    STDMETHODIMP Next(ULONG ulCount, ITfDisplayAttributeInfo** rgInfo, ULONG* pcFetched) override;
    STDMETHODIMP Reset() override;
    STDMETHODIMP Skip(ULONG ulCount) override;

private:
    LONG m_cRef = 1;
    std::vector<ITfDisplayAttributeInfo*> m_items;
    size_t m_index = 0;
};

// 便捷构造函数：返回本输入法的两个显示属性对象
void CreateDisplayAttributeInfos(ITfDisplayAttributeInfo** input, ITfDisplayAttributeInfo** converted);

}  // namespace myime
