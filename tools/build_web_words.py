# -*- coding: utf-8 -*-
u"""从 jieba 词频表里挖出常用词，用 pypinyin 注音，生成 data/words_web.txt。"""
import io, os, re, collections
from pypinyin import lazy_pinyin, Style

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data")
BASE = os.path.normpath(BASE)
SRC  = os.path.join(BASE, "_gen", "jieba_dict.txt")

# 词频表不放进版本库（5 MB，而且能随时重新下），缺了就现下
if not os.path.exists(SRC):
    import urllib.request
    os.makedirs(os.path.dirname(SRC), exist_ok=True)
    url = "https://raw.githubusercontent.com/fxsjy/jieba/master/jieba/dict.txt"
    print(u"下载词频表: " + url)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    open(SRC, "wb").write(urllib.request.urlopen(req).read())
    print(u"  -> %d 字节" % os.path.getsize(SRC))

syl = set()
for ln in io.open(os.path.join(BASE, "syllables.txt"), encoding="utf-8"):
    ln = ln.strip()
    if ln and not ln.startswith("#"): syl.add(ln)

CJK = re.compile(u"^[\u4e00-\u9fff]+$")

# 已有的词，避免重复
existing = set()
for fn in ("words.txt", "words_extra.txt", "words_daily.txt"):
    p = os.path.join(BASE, fn)
    if not os.path.exists(p): continue
    for ln in io.open(p, encoding="utf-8"):
        ln = ln.strip()
        if not ln or ln.startswith("#"): continue
        parts = ln.split()
        if len(parts) >= 3: existing.add(parts[-2])
print(u"已有词条:", len(existing))

# 读词频
items = []
for ln in io.open(SRC, encoding="utf-8", errors="ignore"):
    parts = ln.split()
    if len(parts) < 2: continue
    w = parts[0]
    if not CJK.match(w): continue
    if not (2 <= len(w) <= 4): continue
    try: f = int(parts[1])
    except ValueError: continue
    if f < 2: continue
    items.append((w, f))
print(u"候选中文词(2-4字, 频次>=2):", len(items))

# 按频次降序，同频按词长（短词优先）
items.sort(key=lambda x: (-x[1], len(x[0])))
items = [(w, f) for w, f in items if w not in existing]

bad_syl = collections.Counter()
out = []
seen = set()
TARGET = 5200
for w, f in items:
    if len(out) >= TARGET: break
    if w in seen: continue
    py = lazy_pinyin(w, style=Style.NORMAL)
    if len(py) != len(w): continue
    ok = True
    fixed = []
    for s in py:
        s = s.lower()
        if s not in syl:
            # lue/nue -> lve/nve（syllables.txt 用 v 表示 ü）
            alt = s.replace("ue", "ve")
            if alt in syl: s = alt
            else:
                bad_syl[s] += 1; ok = False; break
        fixed.append(s)
    if not ok: continue
    seen.add(w)
    out.append((u" ".join(fixed), w, f))

print(u"可注音收录:", len(out))
print(u"被丢弃的音节(前 20):", bad_syl.most_common(20))

# 词频 -> 权重：最常用 9000，最低 4200（不盖过人工精选的 9800）
n = len(out)
lines = []
for i, (py, w, f) in enumerate(out):
    wt = int(9000 - 4800.0 * i / max(1, n - 1))
    lines.append(u"%s %s %d" % (py, w, wt))

hdr = [
 u"# 松林1234输入法 网络词频词库（自动生成）",
 u"# 来源: jieba 分词词频表 (github.com/fxsjy/jieba) 前 %d 个常用中文词" % n,
 u"# 注音: pypinyin 0.55（词组级多音字消歧，银行=yin hang、音乐=yin yue）",
 u"# 权重: 按词频从 9000 递减到 4200，不盖过人工精选词条",
 u"# 重新生成: python tools/build_web_words.py",
 u"",
]
io.open(os.path.join(BASE, "words_web.txt"), "w", encoding="utf-8", newline="").write(
    u"\n".join(hdr + lines) + u"\n")
print(u"写出 data/words_web.txt:", n, u"条")
print(u"样例:")
for l in lines[:8]: print(u"   ", l)
