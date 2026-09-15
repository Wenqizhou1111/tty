// Guids.h —— 本输入法的身份标识（CLSID / Profile GUID / 显示属性 GUID）
//
// ⚠️ 做你自己的输入法时，请把这些 GUID 全部换成新生成的随机值，
//    否则会与本骨架的其它安装副本冲突。生成方法：
//      PowerShell: [guid]::NewGuid()
//    唯一性由你自己负责，Windows 不会替你检查。
#pragma once

#include "Common.h"

namespace myime {

// 文本服务 COM 对象 CLSID：{D3F1A7C2-5B4E-4A9F-8C61-7E2D4B9A3F10}
extern const GUID kClsidTextService;

// 语言档案(TIP Profile)GUID：一个 TIP 可以有多个 profile（拼音/双拼/五笔…）
// {7C4E1D92-3A8B-4F6E-B5D2-91C0A7E4F3B8}
extern const GUID kGuidProfile;

// 显示属性：正在输入的未上屏文字（通常画虚线下划线）
// {2F1A6C3D-8B94-4E27-A1F5-6D3C8B9E4A70}
extern const GUID kGuidDisplayAttrInput;

// 显示属性：已转换/选中态（通常加下划线 + 反白）
// {5B8E2D41-7C36-4A19-B0E4-2F9A6C3D8B95}
extern const GUID kGuidDisplayAttrConverted;

// 按键保留(ITfKeystrokeMgr::PreserveKey)用的标识 GUID。
// 它只是个标识，不参与注册；但必须是本输入法自己专属的值。
// {9A3C7E15-4D62-4B8F-8E71-3C5A9B2D6F04}
extern const GUID kGuidPreservedKey;

// 最终用户看到的名字（出现在 Windows 语言列表里）
extern const wchar_t kTextServiceDescription[];
extern const wchar_t kProfileDescription[];

// 注册到哪个语言下：0x0804 = zh-CN(简体中文)
// 换语言只改这里 + 注册脚本里的 LanguageProfile 子键名。
extern const LANGID kLangId;

}  // namespace myime
