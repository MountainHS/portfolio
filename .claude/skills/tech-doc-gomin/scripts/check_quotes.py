"""대응표 발췌 대조: 결과 md의 표 행에서 `별칭:L시작[-끝]` 위치와 "…" 발췌를 찾아
원천 파일 해당 줄 ±2 안에 발췌 조각이 모두 있는지 본다. 읽기만 한다.
사용: python check_quotes.py <결과.md> <별칭표.tsv>
별칭표.tsv: 별칭<TAB>절대경로
"""
import re, sys, unicodedata

def norm(s):
    s = unicodedata.normalize("NFC", s)
    s = s.replace(chr(92) + "|", "|")
    return re.sub(r"\s+", " ", s).strip()

res_path, alias_path = sys.argv[1], sys.argv[2]
aliases = {}
for line in open(alias_path, encoding="utf-8"):
    if line.strip():
        a, p = line.rstrip("\n").split("\t")
        aliases[a] = p
cache = {}
def lines_of(alias):
    if alias not in cache:
        cache[alias] = [norm(l) for l in open(aliases[alias], encoding="utf-8")]
    return cache[alias]

loc_re = re.compile(r"`([A-Za-z0-9가-힣_\-]+):L(\d+)(?:-(\d+))?`")
quote_re = re.compile(r"“([^”]+)”")
bad = ok = 0
for no, row in enumerate(open(res_path, encoding="utf-8"), 1):
    if not row.startswith("|"):
        continue
    locs = loc_re.findall(row)
    quotes = quote_re.findall(row)
    if not locs or not quotes:
        continue
    for q in quotes:
        frags = [norm(f) for f in q.split("…") if norm(f)]
        found = False
        for alias, a, b in locs:
            if alias not in aliases:
                print(f"[별칭없음] 결과 {no}행: {alias}"); continue
            L = lines_of(alias)
            s, e = int(a), int(b or a)
            window = " ".join(L[max(0, s - 3): min(len(L), e + 2)])
            if all(f in window for f in frags):
                found = True; break
        if found:
            ok += 1
        else:
            bad += 1
            print(f"[불일치] 결과 {no}행 {locs} → “{q[:60]}”")
print(f"일치 {ok} / 불일치 {bad}")
