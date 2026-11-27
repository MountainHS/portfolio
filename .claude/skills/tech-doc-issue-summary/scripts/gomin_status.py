"""고민 분류 파일 상태 세기: 남은 질문·보류·답 줄·ID를 세고 어긋난 곳을 알린다. 읽기만 한다.

왜: 차수 요약과 INDEX.md의 "남은 n개 · 보류 n개"를 손으로 세다 틀리기 쉽다.
본문(§6)의 🟡·⏸ 번호와 §1 고민 상자의 질문 번호가 어긋나도 눈으로는 못 찾는다.

쓰는 법:
  python gomin_status.py <분류 파일.md> [...]

보는 것:
  - §1(첫 "## 1." ~ 다음 "## ")의 상자 줄 `🟡 **Q-n` = 남은 질문, `⏸ **Q-n` = 보류.
    제목에 "보존"이 들어간 소절(예: "1-5. 1차 질문과 답 원문 (보존용)")은 세지 않는다.
  - 본문의 `🟡 구체화 필요 (Q-n)` · `⏸ 보류 (Q-n)` 번호 → §1 번호와 비교.
  - 답 줄: 줄 맨 앞이 탭이거나 `>` 뒤 탭(콜아웃 안 답). 바로 위의 Q-번호를 짝으로 보여 준다.
  - 고민 ID: `### <접두어>-D-<nnn>.` 제목. 중복과 빈 번호.
종료 코드: 어긋남이 있으면 1, 없으면 0.
"""
import re
import sys
from collections import Counter

Q_BOX = re.compile(r"(🟡|⏸)\s*\*\*Q-(\d+)")
Q_BODY = re.compile(r"(🟡 구체화 필요|⏸ 보류) \((?:Q-)?(\d+(?:[·,]\s*(?:Q-)?\d+)*)\)")
Q_ANY = re.compile(r"\bQ-(\d+)")
ID_HEAD = re.compile(r"^###\s+([A-Za-z]+)-D-(\d+)\.")
ANSWER = re.compile(r"^(>\s*)?\t")


def check(path):
    lines = open(path, encoding="utf-8").read().split("\n")
    in1 = skip = False
    box = {"🟡": set(), "⏸": set()}
    body = {"🟡": set(), "⏸": set()}
    ids, answers = [], []
    last_q = None
    for no, line in enumerate(lines, 1):
        if line.startswith("## "):
            in1 = bool(re.match(r"##\s+1\.", line))
            skip = False
        elif in1 and line.startswith("### "):
            skip = "보존" in line
        m = ID_HEAD.match(line)
        if m:
            ids.append((m.group(1), int(m.group(2)), no))
        qs = Q_ANY.findall(line)
        if qs and not ANSWER.match(line):
            last_q = qs[0]
        if ANSWER.match(line) and line.strip(" >\t"):
            answers.append((no, last_q))
        if in1:
            if not skip:
                for mark, n in Q_BOX.findall(line):
                    box[mark].add(int(n))
        else:
            for mark, nums in Q_BODY.findall(line):
                key = "🟡" if mark.startswith("🟡") else "⏸"
                for n in re.findall(r"\d+", nums):
                    body[key].add(int(n))

    problems = []
    for key, name in (("🟡", "남은 질문"), ("⏸", "보류")):
        only_body = body[key] - box[key]
        only_box = box[key] - body[key]
        if only_body:
            problems.append(f"{name}: 본문에만 있음(§1 상자에 없음) Q-{sorted(only_body)}")
        if only_box:
            problems.append(f"{name}: §1 상자에만 있음(본문 칸에 없음) Q-{sorted(only_box)} — 상자만 쓰는 질문이면 무시")
    both = box["🟡"] & box["⏸"]
    if both:
        problems.append(f"같은 번호가 🟡와 ⏸ 둘 다: Q-{sorted(both)}")

    dup = [k for k, c in Counter((p, n) for p, n, _ in ids).items() if c > 1]
    if dup:
        problems.append("ID 중복: " + ", ".join(f"{p}-D-{n:03d}" for p, n in dup))
    gaps = []
    for prefix in sorted({p for p, _, _ in ids}):
        nums = sorted({n for p, n, _ in ids if p == prefix})
        gaps += [f"{prefix}-D-{n:03d}" for n in range(nums[0], nums[-1] + 1) if n not in nums]

    levels = Counter()
    for _, _, no in ids:
        for line in lines[no: no + 4]:
            m = re.search(r"\*\*분류:\*\*\s*Level\s*(\d)", line)
            if m:
                levels[m.group(1)] += 1
                break

    print(f"== {path}")
    other = len(ids) - sum(levels.values())
    print(f"고민 {len(ids)}개 (L3 {levels['3']} · L2 {levels['2']} · L1 {levels['1']}" + (f" · 분류 줄 없음 {other} — 다른 파일로 옮긴 고민 등" if other else "") + ")")
    print(f"남은 질문 {len(box['🟡'])}개 {fmt(box['🟡'])} · 보류 {len(box['⏸'])}개 {fmt(box['⏸'])}")
    print(f"답 줄(탭 들여쓰기) {len(answers)}줄" + (": " + ", ".join(f"L{no}→Q-{q}" for no, q in answers[:15]) + (" …" if len(answers) > 15 else "") if answers else ""))
    if gaps:
        print(f"[참고] 빈 ID 번호(옮기거나 합친 고민이면 정상): {', '.join(gaps)}")
    for p in problems:
        print(f"[어긋남] {p}")
    return 1 if any(not p.endswith("무시") for p in problems) else 0


def fmt(s):
    return "(" + "·".join(f"Q-{n:02d}" for n in sorted(s)) + ")" if s else ""


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(max(check(p) for p in sys.argv[1:]))
