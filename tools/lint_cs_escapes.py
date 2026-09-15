# -*- coding: utf-8 -*-
"""扫描 C# 源码里"非逐字字符串中的非法转义序列"。
csc 只在编译时报 CS1009，而并行开发时很容易漏掉，这里提前查出来。

用法: python tools/lint_cs_escapes.py [目录...]
默认扫 src/
"""
import os, re, sys, io

VALID = set("'\"\\0abfnrtv")   # C# 合法单字符转义（u 和 x 单独处理）

def scan_line(line):
    """返回 [(列号, 内容)] 列表。"""
    out = []
    i = 0
    n = len(line)
    while i < n:
        c = line[i]
        # 逐字字符串 @"..."
        if c == '@' and i + 1 < n and line[i+1] == '"':
            i += 2
            while i < n:
                if line[i] == '"':
                    if i + 1 < n and line[i+1] == '"':
                        i += 2; continue
                    i += 1; break
                i += 1
            continue
        # 行注释
        if c == '/' and i + 1 < n and line[i+1] == '/':
            return out
        # 块注释开始（单行内的粗略处理）
        if c == '/' and i + 1 < n and line[i+1] == '*':
            end = line.find('*/', i + 2)
            if end < 0:
                return out
            i = end + 2
            continue
        # 字符字面量
        if c == "'":
            i += 1
            while i < n:
                if line[i] == '\\':
                    i += 2; continue
                if line[i] == "'":
                    i += 1; break
                i += 1
            continue
        # 普通字符串
        if c == '"':
            i += 1
            while i < n:
                ch = line[i]
                if ch == '\\':
                    if i + 1 >= n:
                        break
                    e = line[i+1]
                    if e == 'u':
                        if not re.match(r'^[0-9a-fA-F]{4}', line[i+2:i+6]):
                            out.append((i+1, line.strip()))
                    elif e == 'x':
                        if not re.match(r'^[0-9a-fA-F]{1,4}', line[i+2:i+6]):
                            out.append((i+1, line.strip()))
                    elif e not in VALID:
                        out.append((i+1, line.strip()))
                    i += 2
                    continue
                if ch == '"':
                    i += 1; break
                i += 1
            continue
        i += 1
    return out

def main():
    roots = sys.argv[1:] or ['src']
    base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    total = 0
    for root in roots:
        full = os.path.join(base, root)
        for dirpath, dirnames, filenames in os.walk(full):
            for fn in sorted(filenames):
                if not fn.endswith('.cs'):
                    continue
                p = os.path.join(dirpath, fn)
                rel = os.path.relpath(p, base)
                try:
                    lines = io.open(p, encoding='utf-8').read().split('\n')
                except Exception as e:
                    print('读取失败 %s: %s' % (rel, e)); continue
                for ln, line in enumerate(lines, 1):
                    for col, text in scan_line(line):
                        total += 1
                        print('%s(%d,%d): 非法转义 -> %s' % (rel, ln, col, text))
    if total == 0:
        print('没有发现非法转义序列')
    else:
        print('共 %d 处' % total)
    return 1 if total else 0

if __name__ == '__main__':
    sys.exit(main())
