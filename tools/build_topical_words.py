# -*- coding: utf-8 -*-
u"""从 THUOCL（MIT）生成领域词库 data/words_topical.txt。

来源: https://github.com/thunlp/THUOCL   MIT License, Copyright (c) 2017 THUNLP
注音: pypinyin（词组级多音字消歧）
"""
import io, os, re, collections
from pypinyin import lazy_pinyin, Style

DATA = r"D:\输入法\data"
SRC  = os.path.join(DATA, "_gen", "thuocl")

# 类别 -> (取前 N 条, 权重上限, 权重下限)
# 只收 2~4 字：输入法里"词"基本都是这个长度，再长没人当词打。
# poem 全是 5 字以上的诗句，对输入法没用，直接不收。
# (文件名, 显示名, 取前 N 条, 最长字数, 权重上限, 权重下限)
# 科技/医学/法律的专业词本身就是 5 个字（时间复杂度、正则表达式、中级人民法院），
# 只收 2~4 字会把这类最有价值的词全过滤掉，所以这三类放到 5 字。
PLAN = [
    ("chengyu",      "成语",     8500, 5, 5200, 4000),
    ("diming",       "地名",     9000, 4, 4200, 3100),
    ("food",         "饮食",     5500, 4, 4200, 3100),
    ("IT",           "科技",     4500, 5, 4200, 3100),
    ("medical",      "医学",     4500, 5, 4200, 3000),
    ("lishimingren", "人名",     2500, 4, 4000, 3000),
    ("animal",       "动植物",   3500, 4, 4000, 3000),
    ("caijing",      "财经",     2200, 5, 4200, 3100),
    ("law",          "法律",     2200, 5, 4000, 3000),
    ("car",          "汽车",     1200, 5, 4000, 3000),
]

syl = set()
for ln in io.open(os.path.join(DATA, "syllables.txt"), encoding="utf-8"):
    ln = ln.strip()
    if ln and not ln.startswith("#"): syl.add(ln)

existing = set()
for fn in ("words.txt","words_extra.txt","words_daily.txt","words_web.txt"):
    p = os.path.join(DATA, fn)
    if not os.path.exists(p): continue
    for ln in io.open(p, encoding="utf-8"):
        ln = ln.strip()
        if not ln or ln.startswith("#"): continue
        parts = ln.split()
        if len(parts) >= 3: existing.add(parts[-2])

CJK = re.compile(u"^[\u4e00-\u9fff]+$")
lines = []
stats = []
seen = set()
bad_syl = collections.Counter()

for key, label, cap, maxlen, wmax, wmin in PLAN:
    p = os.path.join(SRC, "THUOCL_%s.txt" % key)
    if not os.path.exists(p):
        stats.append(u"  %-6s 文件缺失" % label); continue
    rows = []
    for ln in io.open(p, encoding="utf-8", errors="ignore"):
        ln = ln.strip().replace(u"\ufeff", "")
        if not ln or "\t" not in ln: continue
        w, f = ln.split("\t", 1)
        w = w.strip()
        if not CJK.match(w) or not (2 <= len(w) <= maxlen): continue
        if w in existing or w in seen: continue
        try: f = int(f.strip())
        except ValueError: continue
        rows.append((w, f))
    rows.sort(key=lambda x: (-x[1], len(x[0])))
    rows = rows[:cap]
    n = len(rows)
    added = 0
    for i, (w, f) in enumerate(rows):
        py = lazy_pinyin(w, style=Style.NORMAL)
        if len(py) != len(w): continue
        ok = True; fixed = []
        for s in py:
            s = s.lower()
            if s not in syl:
                alt = s.replace("ue", "ve")
                if alt in syl: s = alt
                else: bad_syl[s] += 1; ok = False; break
            fixed.append(s)
        if not ok: continue
        seen.add(w)
        wt = int(wmax - (wmax - wmin) * (i / max(1, n - 1)))
        lines.append(u"%s %s %d" % (u" ".join(fixed), w, wt))
        added += 1
    stats.append(u"  %-6s 取 %5d / 收录 %5d   (最长 %d 字)" % (label, n, added, maxlen))

hdr = [
 u"# 松林1234输入法 领域词库（自动生成，请勿手工编辑）",
 u"# 来源: THUOCL (THU Open Chinese Lexicon)  https://github.com/thunlp/THUOCL",
 u"#       MIT License, Copyright (c) 2017 THUNLP",
 u"# 注音: pypinyin（词组级多音字消歧）",
 u"# 权重: 3000~5200，低于日常用词，只作为「打得出专业词」的补充",
 u"# 重新生成: python tools/build_topical_words.py",
 u"",
]
io.open(os.path.join(DATA, "words_topical.txt"), "w", encoding="utf-8", newline="").write(
    u"\n".join(hdr + lines) + u"\n")

out = [u"=== 各类别收录 ==="] + stats
out.append(u"")
out.append(u"总计新增: %d 条" % len(lines))
out.append(u"已有的词条(去重用): %d" % len(existing))
out.append(u"被丢弃的音节(前10): %s" % (bad_syl.most_common(10) if bad_syl else u"无"))
out.append(u"")
out.append(u"=== 样例 ===")
for l in lines[:5] + lines[len(lines)//2:len(lines)//2+5]: out.append(u"   " + l)
io.open(r"D:\输入法\_gen2.txt","w",encoding="utf-8").write(u"\n".join(out))
print("done")
