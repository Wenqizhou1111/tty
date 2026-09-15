// Common.cpp
#include "Common.h"

#include <cstdarg>
#include <cstdio>

namespace myime {

void LogDebug(const wchar_t* format, ...) {
    wchar_t buffer[1024];
    va_list args;
    va_start(args, format);
    // _vsnwprintf_s 是 MSVC CRT 扩展；_TRUNCATE 保证永远以 0 结尾
    _vsnwprintf_s(buffer, _countof(buffer), _TRUNCATE, format, args);
    va_end(args);
    ::OutputDebugStringW(buffer);
    ::OutputDebugStringW(L"\n");
}

std::wstring Utf8ToWide(const std::string& utf8) {
    if (utf8.empty()) return std::wstring();
    // 跳过 UTF-8 BOM
    const char* begin = utf8.data();
    size_t size = utf8.size();
    if (size >= 3 && static_cast<unsigned char>(begin[0]) == 0xEF &&
        static_cast<unsigned char>(begin[1]) == 0xBB &&
        static_cast<unsigned char>(begin[2]) == 0xBF) {
        begin += 3;
        size -= 3;
    }
    if (size == 0) return std::wstring();

    const int chars = ::MultiByteToWideChar(CP_UTF8, 0, begin, static_cast<int>(size), nullptr, 0);
    if (chars <= 0) return std::wstring();

    std::wstring result(static_cast<size_t>(chars), L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, begin, static_cast<int>(size), &result[0], chars);
    return result;
}

std::string WideToUtf8(const std::wstring& wide) {
    if (wide.empty()) return std::string();
    const int bytes = ::WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()),
                                           nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return std::string();
    std::string result(static_cast<size_t>(bytes), '\0');
    ::WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), &result[0], bytes,
                          nullptr, nullptr);
    return result;
}

std::wstring Trim(const std::wstring& s) {
    const wchar_t* kWhite = L" \t\r\n\f\v";
    const size_t begin = s.find_first_not_of(kWhite);
    if (begin == std::wstring::npos) return std::wstring();
    const size_t end = s.find_last_not_of(kWhite);
    return s.substr(begin, end - begin + 1);
}

HINSTANCE GetSelfModule() {
    // 用 FROM_ADDRESS 取本模块句柄，避免依赖 DllMain 保存的全局 hInstance，
    // 这样在单元测试/静态库里复用本文件也不会拿到错误的模块。
    HMODULE module = nullptr;
    if (!::GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                  GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                              reinterpret_cast<LPCWSTR>(&GetSelfModule), &module)) {
        return nullptr;
    }
    return reinterpret_cast<HINSTANCE>(module);
}

std::wstring GetModuleDirectory() {
    HMODULE module = reinterpret_cast<HMODULE>(GetSelfModule());
    if (module == nullptr) return std::wstring();

    wchar_t path[MAX_PATH] = {};
    const DWORD length = ::GetModuleFileNameW(module, path, _countof(path));
    if (length == 0 || length >= _countof(path)) return std::wstring();

    std::wstring dir(path, length);
    const size_t slash = dir.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return std::wstring();
    return dir.substr(0, slash + 1);
}

}  // namespace myime
