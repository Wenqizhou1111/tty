// Register.h —— 注册/反注册入口
//
// DllRegisterServer / DllUnregisterServer 的实现，由 regsvr32 或安装脚本调用。
// 需要管理员权限（要写 HKLM）。
#pragma once

#include "Common.h"

namespace myime {

// 写注册表 + 调 TSF 的 COM 接口把语言档案挂上并启用
HRESULT RegisterTextService();

// 反注册：卸载语言档案，删掉注册表项
HRESULT UnregisterTextService();

}  // namespace myime
