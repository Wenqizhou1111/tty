// DllModule.h —— DLL 级引用计数
//
// DllCanUnloadNow 必须准确回答「现在能不能把这个 DLL 从宿主进程里卸掉」。
// 只要还有任何一个 COM 对象活着（TSF 或者应用持有引用），就不能卸。
#pragma once

#include "Common.h"

namespace myime {

void ModuleAddRef();
void ModuleRelease();
bool ModuleCanUnload();

// 记录 DllMain 拿到的模块句柄（注册窗口类、定位数据文件都要用）
void SetModuleInstance(HINSTANCE instance);
HINSTANCE GetModuleInstance();

}  // namespace myime
