# -*- coding: utf-8 -*-
u"""标定单字表 data/chars.txt

做两件事：
  1) 补齐缺口 —— 对照《通用规范汉字表》找出缺失的常用字（既/际/健/剪/返/企/肌…）
  2) 重排顺序 —— 每个音节下的字按**真实字频**从高到低

字频来源：jieba 词频表（MIT）。做法是把每个词的词频按字数摊到它的每个字上，
再累加。这样"字有多常用"是语料算出来的，不是人拍的。

《通用规范汉字表》(iDvel/The-Table-of-General-Standard-Chinese-Characters)
只用作两件事：找出缺哪些字、以及给语料里查不到的生僻字一个官方顺序兜底。
"""
import io, os, re, collections, sys
from pypinyin import pinyin, Style

DATA = r"D:\输入法\data"
STD  = os.path.join(DATA, "_gen", "std_chars.txt")
JIEBA= os.path.join(DATA, "_gen", "jieba_dict.txt")
CHARS= os.path.join(DATA, "chars.txt")
CJK  = re.compile(u"^[\u4e00-\u9fff]$")

out = []
# ---------- 1) 官方字表（按官方顺序 = 常用度大致降序）----------
stdRank = {}
for ln in io.open(STD, encoding="utf-8"):
    for ch in ln.strip():
        if CJK.match(ch) and ch not in stdRank:
            stdRank[ch] = len(stdRank) + 1
out.append(u"官方字表: %d 字" % len(stdRank))

# ---------- 2) 从 jieba 词频推字频 ----------
charFreq = collections.Counter()
if os.path.exists(JIEBA):
    for ln in io.open(JIEBA, encoding="utf-8", errors="ignore"):
        p = ln.split()
        if len(p) < 2: continue
        w = p[0]
        if not all(CJK.match(c) for c in w): continue
        try: f = int(p[1])
        except ValueError: continue
        per = float(f) / len(w)
        for c in w: charFreq[c] += per
out.append(u"能从 jieba 推字频的字: %d" % len(charFreq))

# ---------- 2b) hanziDB 字频排名（MIT, ruddfawcett/hanziDB.csv）----------
hanziRank = {}
HDB = os.path.join(DATA, "_gen", "hanziDB.csv")
if os.path.exists(HDB):
    for ln in io.open(HDB, encoding="utf-8", errors="ignore").read().split(chr(10))[1:]:
        p = ln.split(",")
        if len(p) < 2: continue
        try: r = int(p[0])
        except ValueError: continue
        c = p[1].strip()
        if len(c) == 1 and c not in hanziRank: hanziRank[c] = r
out.append(u"hanziDB 字频表: %d 字" % len(hanziRank))

# ---------- 3) 读现有 chars.txt ----------
sylChars = collections.OrderedDict()
sylOrder = []
for ln in io.open(CHARS, encoding="utf-8"):
    ln = ln.strip()
    if not ln or ln.startswith("#"): continue
    parts = ln.split()
    if len(parts) < 2: continue
    s = parts[0]
    if s not in sylChars:
        sylChars[s] = []
        sylOrder.append(s)
    for c in parts[1:]:
        if CJK.match(c) and c not in sylChars[s]: sylChars[s].append(c)
out.append(u"现有音节: %d，单字(含重复音节): %d" % (len(sylOrder), sum(len(v) for v in sylChars.values())))

# ---------- 4) 找缺口，用 pypinyin 定音 ----------
have = set()
for v in sylChars.values(): have.update(v)

sylSet = set(sylOrder)
missing = [c for c in stdRank if c not in have]
added = 0; noSyl = []
for c in missing:
    try:
        readings = pinyin(c, style=Style.NORMAL, heteronym=True)[0]
    except Exception:
        readings = []
    placed = False
    for r in readings:
        r = r.lower()
        if r not in sylSet:
            alt = r.replace("ue", "ve")
            if alt in sylSet: r = alt
            else: continue
        if c not in sylChars[r]:
            sylChars[r].append(c); placed = True
    if placed: added += 1
    else: noSyl.append(c)
out.append(u"补齐: 新增 %d 字（官方表里有、我们原先没有的 %d 个）" % (added, len(missing)))
if noSyl: out.append(u"  有 %d 个字找不到对应音节，跳过: %s" % (len(noSyl), u"".join(noSyl[:40])))

# ---------- 5) 每个音节内按字频重排 ----------
def score(c):
    # 主信号：hanziDB 的字频排名（MIT）。这是真正按"字有多常用"排的，
    #   前 20 是 的一是不了在人有我他这个们中来上大为和国。
    #   之前用 jieba 词频推字频有两个毛病：①新闻语料把「及/即/既」这类书面字抬高；
    #   ②"出现在多少词里"会奖励构词能力强的字（机），而用户打单音节多数是要
    #   那个字**单独成词**的用法（几）。换成字频表就都解决了。
    r = hanziRank.get(c)
    if r: return (2, -r)
    # 没排名的生僻字（884 个）先看 jieba 能不能推出频次
    f = charFreq.get(c, 0.0)
    if f > 0: return (1, f)
    # 都不行就用官方字表顺序兜底
    return (0, -stdRank.get(c, 99999))

changed = 0
for s in sylOrder:
    before = list(sylChars[s])
    sylChars[s].sort(key=lambda c: score(c), reverse=True)
    if before != sylChars[s]: changed += 1
out.append(u"重排: %d/%d 个音节的顺序发生了变化" % (changed, len(sylOrder)))

hdr = [
 u"# 松林1234输入法 拼音单字表",
 u"# 格式: <音节> <汉字1> <汉字2> ...   汉字按**字频**从高到低",
 u"# 字频由 jieba 词频表推出（词频按字数摊到每个字），tools/calibrate_chars.py 生成",
 u"# 缺失的常用字对照《通用规范汉字表》补齐；语料里查不到的生僻字按官方顺序兜底",
 u"#",
]
lines = [u"%s %s" % (s, u" ".join(sylChars[s])) for s in sylOrder]
io.open(CHARS, "w", encoding="utf-8", newline="").write(u"\n".join(hdr + lines) + u"\n")

# ---------- 6) 抽验 ----------
out.append(u"")
out.append(u"=== 抽验（音节下的前 12 个字）===")
for s in ["ji","shi","yi","ni","hao","de","jian","yu","ji"]:
    if s in sylChars: out.append(u"  %-6s %s" % (s, u" ".join(sylChars[s][:12])))
out.append(u"")
out.append(u"=== 之前缺的字现在排第几 ===")
for c in [u"既",u"际",u"健",u"剪",u"返",u"企",u"肌",u"肤"]:
    for s, v in sylChars.items():
        if c in v:
            out.append(u"  %s 在 %-6s 第 %d 位" % (c, s, v.index(c)+1)); break
out.append(u"")
out.append(u"单字表规模: %d 音节 %d 字（去重 %d）" % (
    len(sylOrder), sum(len(v) for v in sylChars.values()),
    len(set(c for v in sylChars.values() for c in v))))
io.open(r"D:\输入法\_cal.txt","w",encoding="utf-8").write(u"\n".join(out))
print("done")
