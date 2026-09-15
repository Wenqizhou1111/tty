// dllmain.cpp —— DLL 入口 + COM 类厂 + 导出函数
//
// 这个文件是「DLL 能被 TSF 当成输入法加载」的最小外壳：
//   DllMain            记录模块句柄
//   DllGetClassObject  TSF 通过它创建文本服务对象
//   DllCanUnloadNow    告诉系统现在能不能卸载
//   DllRegisterServer  注册（regsvr32 MyIme.dll）
//   DllUnregisterServer 反注册（regsvr32 /u MyIme.dll）
#include "Common.h"
#include "DllModule.h"
#include "Guids.h"
#include "Register.h"
#include "TextService.h"

#include <new>

namespace myime {

namespace {
LONG g_cRefDll = 0;
HINSTANCE g_hInstance = nullptr;
}  // namespace

void ModuleAddRef() {
    ::InterlockedIncrement(&g_cRefDll);
}

void ModuleRelease() {
    ::InterlockedDecrement(&g_cRefDll);
}

bool ModuleCanUnload() {
    return ::InterlockedCompareExchange(&g_cRefDll, 0, 0) == 0;
}

void SetModuleInstance(HINSTANCE instance) {
    g_hInstance = instance;
}

HINSTANCE GetModuleInstance() {
    return g_hInstance;
}

// COM 类厂：TSF 调 CreateInstance 拿到的就是文本服务对象
class CClassFactory : public IClassFactory {
public:
    CClassFactory() : m_cRef(1) {}

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory)) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override {
        return static_cast<ULONG>(::InterlockedIncrement(&m_cRef));
    }
    STDMETHODIMP_(ULONG) Release() override {
        const LONG count = ::InterlockedDecrement(&m_cRef);
        if (count == 0) {
            delete this;
            return 0;
        }
        return static_cast<ULONG>(count);
    }

    // IClassFactory
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;
        if (outer != nullptr) return CLASS_E_NOAGGREGATION;   // 文本服务不支持聚合

        CTextService* service = new (std::nothrow) CTextService();
        if (service == nullptr) return E_OUTOFMEMORY;

        const HRESULT hr = service->QueryInterface(riid, ppv);
        service->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) {
            ModuleAddRef();
        } else {
            ModuleRelease();
        }
        return S_OK;
    }

private:
    LONG m_cRef;
};

}  // namespace myime

// ---------------------------------------------------------------------------
// 导出函数（用 MyIme.def 导出，名字必须与 def 文件一致）
// ---------------------------------------------------------------------------

BOOL APIENTRY DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            myime::SetModuleInstance(instance);
            // 我们不需要线程附加/分离通知
            ::DisableThreadLibraryCalls(instance);
            break;
        case DLL_PROCESS_DETACH:
            break;
        default:
            break;
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;

    if (!IsEqualCLSID(rclsid, myime::kClsidTextService)) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) myime::CClassFactory();
    if (factory == nullptr) return E_OUTOFMEMORY;

    const HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow() {
    return myime::ModuleCanUnload() ? S_OK : S_FALSE;
}

STDAPI DllRegisterServer() {
    // regsvr32 不保证初始化过 COM，而我们要用 TSF 的 COM 接口，所以自己初始化
    const HRESULT hrInit = ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool needUninit = SUCCEEDED(hrInit);

    const HRESULT hr = myime::RegisterTextService();

    if (needUninit) ::CoUninitialize();
    return hr;
}

STDAPI DllUnregisterServer() {
    const HRESULT hrInit = ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool needUninit = SUCCEEDED(hrInit);

    const HRESULT hr = myime::UnregisterTextService();

    if (needUninit) ::CoUninitialize();
    return hr;
}
