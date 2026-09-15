// Register.cpp —— 注册表结构 + TSF 语言档案/分类注册
//
// 注册结构不是猜的：下面每一项都对照本机真实注册的 TIP 核对过
// （HKLM\SOFTWARE\Microsoft\CTF\TIP 下的微软拼音 / 表驱动输入法 / 日韩 TIP）。
//
// 一个进程内 TSF 输入法必须写齐三块：
//
//   [1] COM 注册（否则 TSF 找不到 DLL）
//       HKCR\CLSID\{CLSID}
//           (默认)          = 描述（COM 类名，可选）
//           InProcServer32
//               (默认)          = <DLL 完整路径>
//               ThreadingModel  = Apartment        ← 必须是 Apartment
//
//   [2] TSF 文本服务
//       HKLM\SOFTWARE\Microsoft\CTF\TIP\{CLSID}
//           Enable = 1 (DWORD)
//           LanguageProfile\0x00000804\{ProfileGUID}
//               Description / IconFile / IconIndex / Enable
//       ⚠️ TIP 根键的 (默认) 值【不是】必须的：本机所有系统 TIP 的该值都是
//          「未设置」，官方文档也没要求。显示名放在 LanguageProfile 的
//          Description 里，别往 TIP 根键的默认值上写。
//
//   [3] 分类（不写 Category 就不会出现在语言列表里）
//       HKLM\SOFTWARE\Microsoft\CTF\TIP\{CLSID}\Category\Category\{CATID}\{CLSID}
//       HKLM\SOFTWARE\Microsoft\CTF\TIP\{CLSID}\Category\Item\{CLSID}\{CATID}
//
// 三块的关系与推荐做法：
//   * 官方推荐的安装顺序是「COM 注册 → 用 ITfInputProcessorProfileMgr::RegisterProfile
//     登记语言档案 → 用 ITfCategoryMgr::RegisterCategory 登记分类」，
//     这三步都是机器级（HKLM）、都需要管理员权限。
//   * 本文件两条路都做：先走 COM 接口（让 TSF 自己的登记表正确），
//     再写一遍注册表键（本机实测结构，同时也是接口调用失败时的兜底）。
//   * 只写注册表不调接口，经常出现「键都在、语言列表里看不到」；
//     只调接口不写注册表，则难以手工排查。两条都做最省事。
#include "Register.h"

#include "Guids.h"

#include <cwchar>
#include <string>

namespace myime {

namespace {

const wchar_t kTipKeyRoot[] = L"SOFTWARE\\Microsoft\\CTF\\TIP\\";
const wchar_t kClsidKeyRoot[] = L"SOFTWARE\\Classes\\CLSID\\";
const wchar_t kLanguageProfileKey[] = L"\\LanguageProfile\\";
const wchar_t kCategoryKey[] = L"\\Category\\Category\\";
const wchar_t kCategoryItemKey[] = L"\\Category\\Item\\";

// 我们要登记的能力分类。
//
// 这里刻意只登记「本实现真的做到了」的分类 —— 也是骨架最值得讲清楚的一个设计点：
// 每个 GUID_TFCAT_* 都是一个承诺，声明了就要在对应环境下正确工作。
//
//   GUID_TFCAT_TIP_KEYBOARD          键盘类 TIP，必需
//   GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER
//                                    提供显示属性（我们确实实现了
//                                    ITfDisplayAttributeProvider，虚线下划线靠它）
//   GUID_TFCAT_TIPCAP_COMLESS        没有 COM 的环境也能被加载。本服务的运行期只用
//                                    QueryInterface 获取接口，不依赖 COM 初始化，
//                                    所以这条成立。
//
// 没声明、以及为什么（想加就在对应功能做完之后再加）：
//   GUID_TFCAT_TIPCAP_UIELEMENTENABLED {49D2F9CF-1F5E-11D7-A6D3-00065B84435C}
//       要求实现 ITfCandidateListUIElement 一族接口，把候选交给 TSF 的 UIManager。
//       骨架用自己的 HWND 画候选，没实现那套接口，所以不能声明。
//   GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT {13A016DF-560B-46CD-947A-4C3AF1E0E35D}
//       要求能在 UWP/沉浸式宿主里工作（含触摸键盘遮挡处理）。
//   GUID_TFCAT_TIPCAP_SECUREMODE       {49D2F9CE-1F5E-11D7-A6D3-00065B84435C}
//       要求能在安全桌面（UAC/登录界面）工作，需要专门测试。
//   GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT   {25504FB4-7BAB-4BC1-9C69-CF81890F0EF5}
//       要求支持系统托盘/语言栏的输入法状态显示。
//   GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT {CCF05DD7-4A87-11D7-A6E2-00065B84435C}
//       要求支持输入模式 compartment（全角/半角、标点模式等）。
//
// 微软自己的 SampleIME 声明了 8 个（上面这 4 个 + KEYBOARD + DISPLAYATTRIBUTEPROVIDER
// + COMLESS + UIELEMENTENABLED），因为它把这些功能全都实现了。
struct CategoryEntry {
    const GUID* guid;
    const wchar_t* literal;
    const wchar_t* name;
};

const CategoryEntry kCategories[] = {
    {&GUID_TFCAT_TIP_KEYBOARD, L"{34745C63-B2F0-4784-8B67-5E12C8701A31}",
     L"GUID_TFCAT_TIP_KEYBOARD"},
    {&GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER, L"{046B8C80-1647-40F7-9B21-B93B81AABC1B}",
     L"GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER"},
    {&GUID_TFCAT_TIPCAP_COMLESS, L"{364215D9-75BC-11D7-A6EF-00065B84435C}",
     L"GUID_TFCAT_TIPCAP_COMLESS"},
};

std::wstring GuidToString(REFGUID guid) {
    wchar_t buffer[64] = {};
    ::StringFromGUID2(guid, buffer, _countof(buffer));
    return std::wstring(buffer);
}

std::wstring FormatLangId(LANGID langid) {
    wchar_t buffer[32] = {};
    ::swprintf_s(buffer, _countof(buffer), L"0x%08X", static_cast<unsigned>(langid));
    return std::wstring(buffer);
}

std::wstring DllPath() {
    wchar_t path[MAX_PATH] = {};
    const DWORD length = ::GetModuleFileNameW(GetSelfModule(), path, _countof(path));
    if (length == 0) return std::wstring();
    return std::wstring(path, length);
}

std::wstring DllFileName() {
    const std::wstring path = DllPath();
    const size_t slash = path.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return path;
    return path.substr(slash + 1);
}

HRESULT CreateKey(HKEY root, const std::wstring& subKey) {
    HKEY key = nullptr;
    const LONG result = ::RegCreateKeyExW(root, subKey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr,
                                          &key, nullptr);
    if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);
    ::RegCloseKey(key);
    return S_OK;
}

HRESULT SetString(HKEY root, const std::wstring& subKey, const wchar_t* name,
                  const std::wstring& value) {
    HKEY key = nullptr;
    LONG result = ::RegCreateKeyExW(root, subKey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key,
                                    nullptr);
    if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);

    // 按字节算长度：REG_SZ 的值长度包含结尾的 L'\0'
    const DWORD bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
    result = ::RegSetValueExW(key, name, 0, REG_SZ,
                              reinterpret_cast<const BYTE*>(value.c_str()), bytes);
    ::RegCloseKey(key);
    return (result == ERROR_SUCCESS) ? S_OK : HRESULT_FROM_WIN32(result);
}

HRESULT SetDword(HKEY root, const std::wstring& subKey, const wchar_t* name, DWORD value) {
    HKEY key = nullptr;
    LONG result = ::RegCreateKeyExW(root, subKey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key,
                                    nullptr);
    if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);

    result = ::RegSetValueExW(key, name, 0, REG_DWORD, reinterpret_cast<const BYTE*>(&value),
                              sizeof(value));
    ::RegCloseKey(key);
    return (result == ERROR_SUCCESS) ? S_OK : HRESULT_FROM_WIN32(result);
}

HRESULT DeleteTree(HKEY root, const std::wstring& subKey) {
    const LONG result = ::RegDeleteTreeW(root, subKey.c_str());
    if (result == ERROR_SUCCESS || result == ERROR_FILE_NOT_FOUND) return S_OK;
    return HRESULT_FROM_WIN32(result);
}

// 写「分类」两侧的键：TSF 靠 Category\Category 找 TIP，靠 Category\Item 显示名字
HRESULT WriteCategoryKeys(const std::wstring& tipKey, const std::wstring& clsidText,
                          const wchar_t* category, const std::wstring& dllFileName) {
    HRESULT hr = CreateKey(HKEY_LOCAL_MACHINE,
                           tipKey + kCategoryKey + category + L"\\" + clsidText);
    if (FAILED(hr)) return hr;
    return SetString(HKEY_LOCAL_MACHINE,
                     tipKey + kCategoryItemKey + clsidText + L"\\" + category, nullptr, dllFileName);
}

}  // namespace

HRESULT RegisterTextService() {
    const std::wstring dllPath = DllPath();
    if (dllPath.empty()) return E_UNEXPECTED;
    const std::wstring dllName = DllFileName();
    const std::wstring clsidText = GuidToString(kClsidTextService);
    const std::wstring profileText = GuidToString(kGuidProfile);
    const std::wstring langText = FormatLangId(kLangId);

    LogDebug(L"[MyIME] 开始注册: clsid=%s profile=%s dll=%s", clsidText.c_str(),
             profileText.c_str(), dllPath.c_str());

    // ---- [1] COM 注册（in-proc server）----
    const std::wstring clsidKey = kClsidKeyRoot + clsidText;
    HRESULT hr = SetString(HKEY_CLASSES_ROOT, clsidKey, nullptr, kTextServiceDescription);
    if (FAILED(hr)) return hr;
    hr = SetString(HKEY_CLASSES_ROOT, clsidKey + L"\\InProcServer32", nullptr, dllPath);
    if (FAILED(hr)) return hr;
    hr = SetString(HKEY_CLASSES_ROOT, clsidKey + L"\\InProcServer32", L"ThreadingModel",
                   L"Apartment");
    if (FAILED(hr)) return hr;

    // ---- [2] TSF 文本服务 + 语言档案的注册表部分 ----
    const std::wstring tipKey = kTipKeyRoot + clsidText;
    hr = SetDword(HKEY_LOCAL_MACHINE, tipKey, L"Enable", 1);
    if (FAILED(hr)) return hr;

    const std::wstring profileKey = tipKey + kLanguageProfileKey + langText + L"\\" + profileText;
    hr = SetString(HKEY_LOCAL_MACHINE, profileKey, L"Description", kProfileDescription);
    if (FAILED(hr)) return hr;
    hr = SetString(HKEY_LOCAL_MACHINE, profileKey, L"IconFile", dllPath);
    if (FAILED(hr)) return hr;
    hr = SetDword(HKEY_LOCAL_MACHINE, profileKey, L"IconIndex", 0);
    if (FAILED(hr)) return hr;
    hr = SetDword(HKEY_LOCAL_MACHINE, profileKey, L"Enable", 1);
    if (FAILED(hr)) return hr;

    // ---- [3] 分类的注册表部分 ----
    for (const auto& category : kCategories) {
        hr = WriteCategoryKeys(tipKey, clsidText, category.literal, dllName);
        if (FAILED(hr)) LogDebug(L"[MyIME] 写分类键失败 %s: 0x%08X", category.name, hr);
    }

    // ---- [4] 用 COM 接口让 TSF 自己登记（官方推荐的安装方式）----
    {
        ComPtr<ITfInputProcessorProfiles> profiles;
        HRESULT hrProfiles = ::CoCreateInstance(
            CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
            IID_ITfInputProcessorProfiles, profiles.PutVoid());

        // 4a. 语言档案：优先 Vista+ 的 ITfInputProcessorProfileMgr::RegisterProfile
        //     （文档明确建议优先于老式的 Register + AddLanguageProfile）。
        //     参数顺序：clsid, langid, profileGuid, 描述, 描述长度, 图标文件, 路径长度,
        //               图标序号, hklsubstitute, dwPreferredLayout(必须为 0),
        //               bEnabledByDefault, dwFlags
        bool registered = false;
        ComPtr<ITfInputProcessorProfileMgr> profileMgr;
        if (SUCCEEDED(::CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr,
                                         CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfileMgr,
                                         profileMgr.PutVoid()))) {
            const HRESULT hrRegister = profileMgr->RegisterProfile(
                kClsidTextService, kLangId, kGuidProfile, kProfileDescription,
                static_cast<ULONG>(wcslen(kProfileDescription)), dllPath.c_str(),
                static_cast<ULONG>(dllPath.size()), 0, nullptr, 0, TRUE, 0);
            if (SUCCEEDED(hrRegister)) {
                registered = true;
            } else {
                LogDebug(L"[MyIME] RegisterProfile 失败 0x%08X，退回老接口", hrRegister);
            }
        }

        if (!registered) {
            if (SUCCEEDED(hrProfiles)) {
                profiles->Register(kClsidTextService);
                const HRESULT hrAdd = profiles->AddLanguageProfile(
                    kClsidTextService, kLangId, kGuidProfile, kProfileDescription,
                    static_cast<ULONG>(wcslen(kProfileDescription)), dllPath.c_str(),
                    static_cast<ULONG>(dllPath.size()), 0);
                if (FAILED(hrAdd)) LogDebug(L"[MyIME] AddLanguageProfile 失败 0x%08X", hrAdd);
                registered = SUCCEEDED(hrAdd);
            } else {
                LogDebug(L"[MyIME] 取 ITfInputProcessorProfiles 失败 0x%08X", hrProfiles);
            }
        }

        // 4b. 显式对「当前用户」启用一次。
        //     注意：不存在 ITfInputProcessorProfileMgr::EnableProfile；启用只能用
        //     EnableLanguageProfile（当前用户）/ EnableLanguageProfileByDefault（所有用户）。
        if (SUCCEEDED(hrProfiles)) {
            const HRESULT hrEnable =
                profiles->EnableLanguageProfile(kClsidTextService, kLangId, kGuidProfile, TRUE);
            if (FAILED(hrEnable)) LogDebug(L"[MyIME] EnableLanguageProfile 失败 0x%08X", hrEnable);
        }

        // 4c. 分类：用 ITfCategoryMgr::RegisterCategory 再登记一遍
        //     （item GUID 就是文本服务自己的 CLSID）
        ComPtr<ITfCategoryMgr> categoryMgr;
        if (SUCCEEDED(::CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                                         IID_ITfCategoryMgr, categoryMgr.PutVoid()))) {
            for (const auto& category : kCategories) {
                const HRESULT hrCategory = categoryMgr->RegisterCategory(
                    kClsidTextService, *category.guid, kClsidTextService);
                if (FAILED(hrCategory)) {
                    LogDebug(L"[MyIME] RegisterCategory(%s) 失败 0x%08X", category.name,
                             hrCategory);
                }
            }
        } else {
            LogDebug(L"[MyIME] 取 ITfCategoryMgr 失败：分类只写了注册表");
        }
    }

    LogDebug(L"[MyIME] 注册完成");
    return S_OK;
}

HRESULT UnregisterTextService() {
    const std::wstring clsidText = GuidToString(kClsidTextService);

    LogDebug(L"[MyIME] 开始反注册 clsid=%s", clsidText.c_str());

    // 先撤语言档案与分类（顺序反了会在语言列表里留下空条目）
    {
        ComPtr<ITfInputProcessorProfileMgr> profileMgr;
        if (SUCCEEDED(::CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr,
                                         CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfileMgr,
                                         profileMgr.PutVoid()))) {
            profileMgr->UnregisterProfile(kClsidTextService, kLangId, kGuidProfile, 0);
        }

        ComPtr<ITfInputProcessorProfiles> profiles;
        if (SUCCEEDED(::CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr,
                                         CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfiles,
                                         profiles.PutVoid()))) {
            profiles->EnableLanguageProfile(kClsidTextService, kLangId, kGuidProfile, FALSE);
            profiles->RemoveLanguageProfile(kClsidTextService, kLangId, kGuidProfile);
            profiles->Unregister(kClsidTextService);
        }

        ComPtr<ITfCategoryMgr> categoryMgr;
        if (SUCCEEDED(::CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                                         IID_ITfCategoryMgr, categoryMgr.PutVoid()))) {
            for (const auto& category : kCategories) {
                categoryMgr->UnregisterCategory(kClsidTextService, *category.guid,
                                                kClsidTextService);
            }
        }
    }

    // 再删注册表
    DeleteTree(HKEY_LOCAL_MACHINE, kTipKeyRoot + clsidText);
    DeleteTree(HKEY_CLASSES_ROOT, kClsidKeyRoot + clsidText);

    LogDebug(L"[MyIME] 反注册完成");
    return S_OK;
}

}  // namespace myime
