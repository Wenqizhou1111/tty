#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""lint_structure.py —— 在没有 C++ 编译器的机器上做结构性自检。

它不是编译器，只查这几类高频低级错误：
  1. 括号/花括号/方括号不配对
  2. .cpp 里用到的 m_ 成员变量在对应 .h 里没声明
  3. .cpp 里定义的 CTextService::Xxx / 其它类方法在头文件里没有声明
  4. 头文件里声明的成员函数没有实现（仅检查我们自己的类）
  5. CMakeLists.txt 里列出的源文件是否都存在
  6. 头文件是否有 #pragma once

用法: python tools/lint_structure.py
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
TESTS = ROOT / "tests"

problems: list[str] = []
notes: list[str] = []


def strip_comments_and_strings(text: str) -> str:
    """去掉注释与字符串字面量，避免括号统计被它们干扰。"""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                if text[i] == "\\":
                    i += 1
                i += 1
            i += 1
            continue
        if c == "'":
            # 字符字面量（含 L'x' 已被处理成 'x'）
            i += 1
            while i < n and text[i] != "'":
                if text[i] == "\\":
                    i += 1
                i += 1
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def check_balance(path: Path) -> None:
    raw = path.read_text(encoding="utf-8", errors="replace")
    text = strip_comments_and_strings(raw)
    pairs = {")": "(", "]": "[", "}": "{"}
    stack = []
    line = 1
    for ch in text:
        if ch == "\n":
            line += 1
        elif ch in "([{":
            stack.append((ch, line))
        elif ch in ")]}":
            if not stack:
                problems.append(f"{path.name}:{line} 多余的 '{ch}'")
                return
            open_ch, open_line = stack.pop()
            if open_ch != pairs[ch]:
                problems.append(
                    f"{path.name}:{line} '{ch}' 与第 {open_line} 行的 '{open_ch}' 不匹配"
                )
                return
    if stack:
        ch, open_line = stack[-1]
        problems.append(f"{path.name} 文件结束时仍有未闭合的 '{ch}'（第 {open_line} 行开始）")


def members_declared(header_text: str) -> set[str]:
    return set(re.findall(r"\b(m_[A-Za-z0-9_]+)\b", header_text))


def members_used(cpp_text: str) -> set[str]:
    return set(re.findall(r"\b(m_[A-Za-z0-9_]+)\b", cpp_text))


def methods_defined(cpp_text: str, class_name: str) -> set[str]:
    # STDMETHODIMP CTextService::Foo( / HRESULT CTextService::Foo( / void CTextService::Foo(
    pattern = re.compile(
        r"\b(?:\w[\w:*<>\s]*?\s+)?" + re.escape(class_name) + r"::(~?\w+)\s*\("
    )
    return set(pattern.findall(cpp_text))


def methods_declared(header_text: str) -> set[str]:
    # 行内形如  ReturnType Name( ... );   （排除 if/for/while/switch/return）
    skip = {"if", "for", "while", "switch", "return", "sizeof", "catch", "static_cast"}
    found = set()
    for match in re.finditer(r"([A-Za-z_]\w*)\s*\(", header_text):
        name = match.group(1)
        if name in skip:
            continue
        found.add(name)
    return found


def check_pair(header: Path, cpp: Path, class_name: str) -> None:
    if not header.exists() or not cpp.exists():
        return
    h_text = header.read_text(encoding="utf-8")
    c_text = cpp.read_text(encoding="utf-8")

    declared = members_declared(strip_comments_and_strings(h_text))
    used = members_used(strip_comments_and_strings(c_text))
    missing = sorted(used - declared)
    if missing:
        problems.append(
            f"{cpp.name} 用到但 {header.name} 未声明的成员: {', '.join(missing)}"
        )

    defined = methods_defined(strip_comments_and_strings(c_text), class_name)
    declared_methods = methods_declared(strip_comments_and_strings(h_text))
    undefined = sorted(m for m in defined if m not in declared_methods and not m.startswith("~"))
    if undefined:
        problems.append(
            f"{cpp.name} 定义了但 {header.name} 未声明的方法: {', '.join(undefined)}"
        )

    implemented = defined
    declared_ours = {
        m
        for m in declared_methods
        if re.search(r"\b" + re.escape(m) + r"\s*\(", strip_comments_and_strings(h_text))
    }
    notes.append(
        f"{header.name}: 声明方法 {len(declared_ours)} 个 / {cpp.name} 实现 {len(implemented)} 个"
    )


def check_cmake_sources() -> None:
    cmake = ROOT / "CMakeLists.txt"
    if not cmake.exists():
        problems.append("CMakeLists.txt 不存在")
        return
    text = cmake.read_text(encoding="utf-8")
    for rel in re.findall(r"(src/[\w./]+|tests/[\w./]+)", text):
        if not (ROOT / rel).exists():
            problems.append(f"CMakeLists.txt 引用了不存在的文件: {rel}")


def check_pragma_once() -> None:
    for path in sorted(SRC.glob("*.h")):
        if "#pragma once" not in path.read_text(encoding="utf-8"):
            problems.append(f"{path.name} 缺少 #pragma once")


def check_includes() -> None:
    """每个 .cpp 应当包含本工程头文件（TsfGuids.cpp 例外），且 include 的路径要存在。"""
    # TsfGuids.cpp 有意只包含 SDK 头：它唯一的职责是展开 initguid.h 的 GUID 定义
    allowlist = {"TsfGuids.cpp"}
    for path in sorted(SRC.glob("*.cpp")):
        text = path.read_text(encoding="utf-8")
        if path.name not in allowlist and not re.search(r'#include\s+"', text):
            problems.append(f"{path.name} 似乎没有包含任何本工程头文件")

        # 本工程头文件的路径必须真实存在
        for rel in re.findall(r'#include\s+"([^"]+)"', text):
            if "/" in rel or "\\" in rel:
                target = (path.parent / rel).resolve()
                if not target.exists():
                    problems.append(f"{path.name} 包含了不存在的头文件: {rel}")


def main() -> int:
    files = sorted(list(SRC.glob("*.cpp")) + list(SRC.glob("*.h")) + list(TESTS.glob("*.cpp")))
    if not files:
        print("没有找到源文件")
        return 1

    for path in files:
        check_balance(path)

    check_pair(SRC / "TextService.h", SRC / "TextService.cpp", "CTextService")
    check_pair(SRC / "Engine.h", SRC / "Engine.cpp", "Engine")
    check_pair(SRC / "Dictionary.h", SRC / "Dictionary.cpp", "Dictionary")
    check_pair(SRC / "CandidateWindow.h", SRC / "CandidateWindow.cpp", "CandidateWindow")
    check_pair(SRC / "EditSession.h", SRC / "EditSession.cpp", "CEditSession")
    check_pair(SRC / "DisplayAttribute.h", SRC / "DisplayAttribute.cpp", "CDisplayAttributeInfo")

    check_cmake_sources()
    check_pragma_once()
    check_includes()

    print(f"检查了 {len(files)} 个源文件")
    for note in notes:
        print("  -", note)
    print()
    if problems:
        print(f"发现 {len(problems)} 个问题：")
        for p in problems:
            print("  [问题]", p)
        return 1
    print("结构性自检通过（注意：这不等于能编译通过，只排除括号/成员/声明这类低级错误）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
