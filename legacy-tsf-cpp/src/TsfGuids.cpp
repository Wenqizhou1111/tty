// TsfGuids.cpp —— 全工程「唯一定义」TSF GUID 符号的翻译单元
//
// 为什么需要这个文件：
//   msctf.h 里的 GUID（GUID_PROP_ATTRIBUTE、CLSID_TF_InputProcessorProfiles、
//   GUID_COMPARTMENT_KEYBOARD_OPENCLOSE、IID_ITfContext…）默认只是「声明」，
//   真正的定义要么来自某个库，要么来自 initguid.h 的 DEFINE_GUID 展开。
//   不同 Windows SDK 版本对「这些符号到底在 msctf.lib 里还是在 uuid.lib 里」
//   并不一致，链接期很容易出现 LNK2001 未解析外部符号。
//
// 解决办法是最经典的那个：**有且只有一个 .cpp** 在包含 msctf.h 之前先包含
// <initguid.h>，让该头里的 DEFINE_GUID 展开成真正的定义（__declspec(selectany)，
// 天然允许重复，不会和库里的定义冲突）。其余文件照常 include msctf.h 即可。
//
// ⚠️ 不要在别的文件里再 include <initguid.h>，也不要删掉本文件。
#include <windows.h>
#include <ole2.h>

#include <initguid.h>
#include <msctf.h>
