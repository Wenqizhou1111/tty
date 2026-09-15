# CMakeLists.txt —— MyIME 构建脚本
#
# 产物：
#   MyIme.dll    文本服务（x64 / Win32 各编一份）
#   EngineTest.exe 引擎单元测试（不依赖 TSF，随便在哪台机器上都能跑）
#
# 用法（详见 README.md）：
#   cmake -B build/x64 -A x64
#   cmake --build build/x64 --config Release
cmake_minimum_required(VERSION 3.20)
project(MyIme LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    # /utf-8 是必须的：源码里有中文字面量，没有它 MSVC 会按本机 ANSI 代码页解释
    add_compile_options(/utf-8 /W4 /EHsc /permissive-)
    add_compile_definitions(UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
endif()

set(MYIME_SOURCES
    src/Common.cpp
    src/Guids.cpp
    src/Dictionary.cpp
    src/Engine.cpp
    src/CandidateWindow.cpp
    src/DisplayAttribute.cpp
    src/EditSession.cpp
    src/TextService.cpp
    src/Register.cpp
    src/TsfGuids.cpp
    src/dllmain.cpp
)

add_library(MyIme SHARED ${MYIME_SOURCES} src/MyIme.def)

target_include_directories(MyIme PRIVATE src)

# msctf = TSF（部分 SDK 里 TSF 的 GUID 符号来自这里）；
# ole32/oleaut32 = COM 与 BSTR；user32/gdi32 = 候选窗；advapi32 = 注册表
# 说明：TSF 的 GUID 符号由 src/TsfGuids.cpp 用 initguid.h 在本模块内定义，
#       即使某个 SDK 的 msctf.lib 不导出它们也能链接通过。
target_link_libraries(MyIme PRIVATE msctf ole32 oleaut32 user32 gdi32 advapi32)

# 只在需要时生成导入库（.lib 用不上，但默认会生成，无所谓）
set_target_properties(MyIme PROPERTIES
    OUTPUT_NAME "MyIme"
    PREFIX ""
)

# 词典 / 音节表之类的外部数据：构建后拷到产物旁边，方便直接注册测试
add_custom_command(TARGET MyIme POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${CMAKE_CURRENT_SOURCE_DIR}/data/dictionary.txt"
            "$<TARGET_FILE_DIR:MyIme>/dictionary.txt"
    COMMENT "复制 dictionary.txt 到输出目录"
)

# ---------------------------------------------------------------------------
# 引擎单元测试：只依赖 Engine/Dictionary/Common，不需要 TSF
# ---------------------------------------------------------------------------
add_executable(EngineTest
    tests/EngineTest.cpp
    src/Engine.cpp
    src/Dictionary.cpp
    src/Common.cpp
)
target_include_directories(EngineTest PRIVATE src)
set_target_properties(EngineTest PROPERTIES
    OUTPUT_NAME "EngineTest"
    # 控制台程序（入口是 wmain，不需要额外设置入口点）
    WIN32_EXECUTABLE FALSE
)
