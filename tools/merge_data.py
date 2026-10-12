# -*- coding: utf-8 -*-
"""把 data/_gen/ 下各分片合并、校验成正式词库文件。
校验是硬性的：音节必须在白名单里，字必须真是汉字，词必须是纯汉字。"""
import os, sys, re, glob, collections, io

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
GEN = os.path.join(ROOT, 'data', '_gen')
DATA = os.path.join(ROOT, 'data')

CJK = re.compile(r'^[\u4e00-\u9fff]$')
HAN = re.compile(r'^[\u4e00-\u9fff]+$')

def load_syllables():
    syl = set()
    for name in ('syllables.txt',):
        p = os.path.join(DATA, name)
        if not os.path.exists(p):
            p = os.path.join(GEN, name)
        if os.path.exists(p):
            for line in io.open(p, encoding='utf-8'):
                line = line.strip()
                if not line or line.startswith('#'):
                    continue
                for t in re.split(r'[\s,]+', line):
                    if t:
                        syl.add(t)
    return syl

SYL = load_syllables()
ALIAS = {'lue': 'lve', 'nue': 'nve', 'lu:': 'lv', 'nv': 'nv', 'nu:': 'nv'}
def norm_syl(s):
    s = s.strip().lower()
    return ALIAS.get(s, s)

report = []
def log(m):
    report.append(m)
    print(m)

# ---------------- 单字表 ----------------
def merge_chars():
    merged = collections.OrderedDict()   # syllable -> [chars]
    order = []                            # 保持首次出现顺序
    files = sorted(glob.glob(os.path.join(GEN, 'chars_*.txt')))
    stats = []
    bad_lines = 0
    bad_syl = collections.Counter()
    for f in files:
        n_syl = 0; n_ch = 0
        for line in io.open(f, encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            parts = re.split(r'[\s\t]+', line)
            if len(parts) < 2:
                bad_lines += 1; continue
            s = norm_syl(parts[0])
            if s not in SYL:
                bad_syl[s] += 1
                continue
            if s not in merged:
                merged[s] = []
                order.append(s)
            bucket = merged[s]
            got = 0
            for ch in parts[1:]:
                ch = ch.strip()
                if not CJK.match(ch):
                    continue
                if ch in bucket:
                    continue
                bucket.append(ch)
                got += 1
            n_syl += 1; n_ch += got
        stats.append((os.path.basename(f), n_syl, n_ch))
    for name, ns, nc in stats:
        log('  %-16s 音节=%3d 字=%4d' % (name, ns, nc))
    if bad_lines:
        log('  [警告] 格式不合的行: %d' % bad_lines)
    if bad_syl:
        log('  [警告] 不在音节白名单的行: %s' % dict(bad_syl.most_common(20)))

    # 白名单里有、但没人产出的音节 -> 留空会在切分时出问题，必须报出来
    missing = sorted(SYL - set(merged.keys()))
    if missing:
        log('  [警告] 没有任何字的音节 (%d): %s' % (len(missing), ' '.join(missing)))

    out = os.path.join(DATA, 'chars.txt')
    with io.open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(u'# 松林1234输入法 拼音单字表\n')
        fh.write(u'# 格式: <音节> <汉字1> <汉字2> ...   汉字按常用度从高到低\n')
        fh.write(u'# 由 tools/merge_data.py 从 data/_gen/chars_*.txt 合并生成，可手工补充\n')
        for s in order:
            fh.write(s + u' ' + u' '.join(merged[s]) + u'\n')

    allchars = set()
    for s, lst in merged.items():
        allchars.update(lst)
    total_entries = sum(len(v) for v in merged.values())
    log('  合并结果: 音节=%d  条目=%d  去重后不同汉字=%d' % (len(merged), total_entries, len(allchars)))
    return merged, allchars

# ---------------- 词表 ----------------
def merge_words():
    best = {}          # word -> (code, weight)
    bad = 0
    files = sorted(glob.glob(os.path.join(GEN, 'words_*.txt')))
    stats = []
    for f in files:
        n = 0
        for line in io.open(f, encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            parts = re.split(r'[\s\t]+', line)
            if len(parts) < 3:
                bad += 1; continue
            weight = parts[-1]
            word = parts[-2]
            code = [norm_syl(x) for x in parts[:-2]]
            if not weight.lstrip('-').isdigit():
                bad += 1; continue
            if not HAN.match(word):
                bad += 1; continue
            if not code or any(c not in SYL for c in code):
                bad += 1; continue
            if len(code) != len(word):
                bad += 1; continue
            w = int(weight)
            key = word
            if key not in best or w > best[key][1]:
                best[key] = (' '.join(code), w)
            n += 1
        stats.append((os.path.basename(f), n))
    for name, n in stats:
        log('  %-16s 条=%4d' % (name, n))
    if bad:
        log('  [警告] 丢弃的不合格行: %d' % bad)

    out = os.path.join(DATA, 'words.txt')
    items = sorted(best.items(), key=lambda kv: -kv[1][1])
    with io.open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(u'# 松林1234输入法 联想词库\n')
        fh.write(u'# 格式: <音节 空格分隔> <词> <权重>   权重越大越靠前\n')
        fh.write(u'# 由 tools/merge_data.py 从 data/_gen/words_*.txt 合并生成\n')
        for word, (code, w) in items:
            fh.write(u'%s %s %d\n' % (code, word, w))
    log('  合并结果: 去重后词条=%d' % len(items))
    return best

log('== 单字表 ==')
chars, allchars = merge_chars()
log('== 词表 ==')
words = merge_words()

# ---------------- 覆盖率体检 ----------------
log('== 体检 ==')
log('  不同汉字总数 = %d  (目标 4000)' % len(allchars))
log('  词条总数     = %d  (目标 >=1000)' % len(words))
short = [s for s in SYL if s in chars and len(chars[s]) < 3]
log('  候选字少于3个的音节: %d %s' % (len(short), ' '.join(short[:30])))
nofirst = [s for s in SYL if s not in chars]
log('  完全缺失的音节: %d %s' % (len(nofirst), ' '.join(nofirst)))

with io.open(os.path.join(ROOT, 'data', '_gen', 'merge_report.txt'), 'w', encoding='utf-8') as fh:
    fh.write(u'\n'.join(report))
