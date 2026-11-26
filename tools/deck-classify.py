"""슬라이드 장면 분류기 (이 포크 전용, Claude 없이 돌림).

프로젝트 문서 md를 절 단위로 자르고, 절마다 블록(다이어그램·코드·표·이미지·글)을 세어
장면 틀(layout)을 추천합니다. data/projects/<id>.json의 지금 덱과 비교해
문제 유형별로 묶은 표를 씁니다. 파일을 고치지 않고 읽기만 합니다.

사용:
    python tools/deck-classify.py <입력> [<출력 폴더>]
    <입력>  src/4-wgo 처럼 프로젝트 폴더 하나, 또는 src 폴더(그 아래 프로젝트 전부)
    <출력>  생략하면 output/check/deck-classify
결과:
    <출력>/<폴더>-분류.md   사람이 보는 표
    <출력>/<폴더>-분류.csv  엑셀 정렬용 (UTF-8 BOM)

틀 추천 기준은 .claude/skills/portfolio-build/SKILL.md §2-2 대응표,
유형 기준은 .claude/rules/portfolio-writing.md 지침 1~5를 단순화한 것입니다.
Python 3.8 이상, 표준 라이브러리만.
"""

import csv
import json
import re
import sys
from collections import Counter
from pathlib import Path

CODE_MAX_LINES = 24          # assets/project-deck.js MAXL
WIDE_TABLE_COLS = 5          # 지침 1: 열이 4개를 넘으면 칸이 좁아짐
BIG_MERMAID_EDGES = 12       # 지침 2: 노드 10개 안팎
MANY_TEXT_UNITS = 6          # 글 문단+목록 줄이 이만큼이면 "글 많음"
TOC_TITLES = {"목차", "차례", "contents"}
PROBLEM_WORDS = ("문제", "원인", "해결")
DOC_NAMES = ("프로젝트문서.md", "기술문서.md", "README.md")

# 유형: (코드, 이름, 어디서 고치나)
TYPES = {
    "T1": ("글만 있는 절", "문서: 그림·표·스크린샷이 필요한지 보기, 짧으면 위 절에 합치기"),
    "T2": ("글이 너무 많음", "문서: 핵심 문장 먼저, 나머지는 목록으로 줄이거나 절 나누기"),
    "T3": ("블록 3개 이상", "문서: 절 하나 = 주장 하나, 블록 1~2개로 절 나누기"),
    "T4": ("긴 코드(24줄 초과)", "덱: lines로 핵심 24줄 이하 고르기(/portfolio-edit) 또는 문서에서 흐름 먼저"),
    "T5": ("넓은 표(5열 이상)", "문서: 표를 둘로 나누거나 이름 칸에 <br> (지침 1)"),
    "T6": ("큰 다이어그램", "문서: 기본 흐름 그림 + 예외 표로 나누기 (지침 2)"),
    "T7": ("추천 틀과 덱 틀이 다름", "판단: 덱이 맞으면 그대로, 아니면 /portfolio-edit 또는 다시 빌드"),
    "T8": ("내용이 있는데 덱 장면 없음", "빌드: 다시 빌드하거나 위 장면에 합쳐졌는지 확인"),
    "T9": ("촬영 자리만 있음", "촬영: <!-- 촬영 --> 자리에 스크린샷 넣기"),
}


def norm(text):
    """굵게·기울임·백틱·공백 차이를 무시한 비교용 글자."""
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    return re.sub(r"[\s*_`]", "", text).lower()


def find_doc(folder):
    for name in DOC_NAMES:
        if (folder / name).is_file():
            return folder / name
    docs = [p for p in folder.glob("*.md") if not p.name.startswith("_")]
    return docs[0] if len(docs) == 1 else None


def parse_sections(md_path):
    """제목마다 절 하나. 절 범위 = 그 제목부터 다음 제목(수준 무관) 앞까지."""
    lines = md_path.read_text(encoding="utf-8-sig").splitlines()
    sections, stack, cur = [], [], None
    fence = None  # (표시, 언어, 시작 줄)
    in_comment = False

    def new_section(level, title, line_no):
        nonlocal stack
        stack = [s for s in stack if s[0] < level] + [(level, title)]
        return {
            "line": line_no, "level": level, "title": title,
            "path": [t for _, t in stack],
            "mermaid": [], "code": [], "tables": [], "images": 0,
            "paras": 0, "items": 0, "shots": 0, "problem": set(),
        }

    i = 0
    prev_text = False
    while i < len(lines):
        raw = lines[i]
        s = raw.strip()
        if fence:
            if s.startswith(fence[0]) and s.strip("`~") == "":
                body = lines[fence[2] + 1:i]
                if cur is not None:
                    if fence[1] == "mermaid":
                        edges = sum(1 for b in body if re.search(r"-->|-\.->|==>|---", b))
                        cur["mermaid"].append(edges)
                    else:
                        cur["code"].append(len(body))
                fence = None
            i += 1
            continue
        m = re.match(r"^(`{3,}|~{3,})\s*([\w+-]*)", s)
        if m:
            fence = (m.group(1)[0] * 3, m.group(2).lower(), i)
            prev_text = False
            i += 1
            continue
        if in_comment:
            if "-->" in s:
                in_comment = False
            i += 1
            continue
        if s.startswith("<!--"):
            if cur is not None and "촬영" in s:
                cur["shots"] += 1
            if "-->" not in s:
                in_comment = True
            i += 1
            continue
        h = re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", raw)
        if h:
            cur = new_section(len(h.group(1)), h.group(2), i + 1)
            sections.append(cur)
            prev_text = False
            i += 1
            continue
        if cur is None:
            i += 1
            continue
        if s.startswith("|") and i + 1 < len(lines) and re.match(r"^\s*\|?\s*:?-{2,}", lines[i + 1]):
            head = [c.strip() for c in s.strip("|").split("|")]
            j = i + 2
            while j < len(lines) and lines[j].strip().startswith("|"):
                j += 1
            table_text = "\n".join(lines[i:j])
            cur["tables"].append(len(head))
            for w in PROBLEM_WORDS:
                if any(w in c for c in head) or re.search(r"\|\s*\**" + w, table_text):
                    cur["problem"].add(w)
            prev_text = False
            i = j
            continue
        if re.search(r"!\[[^\]]*\]\([^)]+\)|<img\s", s):
            cur["images"] += len(re.findall(r"!\[[^\]]*\]\([^)]+\)|<img\s", s))
            prev_text = False
            i += 1
            continue
        if not s:
            prev_text = False
            i += 1
            continue
        for w in PROBLEM_WORDS:
            if re.search(r"\*\*[^*]*" + w + r"[^*]*\*\*", s):
                cur["problem"].add(w)
        if re.match(r"^([-*+]|\d+[.)])\s+", s):
            cur["items"] += 1
            prev_text = False
        elif not s.startswith(">") or not prev_text:
            if not prev_text:
                cur["paras"] += 1
            prev_text = True
        i += 1
    return sections


def recommend(sec):
    """SKILL.md §2-2 대응표를 단순화. (틀, 이유)"""
    n_mer, n_code, n_tab, n_img = len(sec["mermaid"]), len(sec["code"]), len(sec["tables"]), sec["images"]
    text = sec["paras"] + sec["items"]
    if len(sec["problem"]) >= 2:
        return "problem", "문제·원인·해결 표지"
    if n_code:
        return "code", "코드 블록"
    if n_img >= 2 and not (n_mer or n_tab):
        return "screens", "이미지 2장 이상"
    if n_mer or n_img:
        if text <= 1 and not n_tab:
            return "full", "그림 하나가 주인공(글 거의 없음)"
        return "visual", "그림 + 설명"
    if n_tab:
        return "full", "표가 주인공"
    return "overview", "글만"


def blocks_label(sec):
    parts = []
    if sec["mermaid"]:
        parts.append(f"그림{len(sec['mermaid'])}")
    if sec["code"]:
        parts.append("코드" + "/".join(str(n) for n in sec["code"]) + "줄")
    if sec["tables"]:
        parts.append("표" + "/".join(str(n) for n in sec["tables"]) + "열")
    if sec["images"]:
        parts.append(f"이미지{sec['images']}")
    text = sec["paras"] + sec["items"]
    if text:
        parts.append(f"글{text}")
    if sec["shots"]:
        parts.append(f"촬영자리{sec['shots']}")
    return " ".join(parts) or "-"


def path_matches(slide_path, sec_path):
    """덱 sectionPath: 마지막 칸 = 절 제목, 앞 칸들은 순서대로 조상."""
    if not slide_path or norm(slide_path[-1]) != norm(sec_path[-1]):
        return False
    anc = [norm(t) for t in sec_path[:-1]]
    pos = 0
    for t in (norm(x) for x in slide_path[:-1]):
        while pos < len(anc) and anc[pos] != t:
            pos += 1
        if pos == len(anc):
            return False
        pos += 1
    return True


def analyze(folder, repo):
    doc = find_doc(folder)
    if doc is None:
        return None, f"{folder.name}: 프로젝트 문서 md를 찾지 못했습니다(폴더 안 .md가 하나여야 함)"
    sections = parse_sections(doc)

    data_path = repo / "data" / "projects" / f"{folder.name}.json"
    slides, retro_title, has_deck = [], None, False
    if data_path.is_file():
        data = json.loads(data_path.read_text(encoding="utf-8-sig"))
        deck = data.get("deck") or {}
        slides = deck.get("slides") or []
        has_deck = bool(slides)
        retro = data.get("retro") or {}
        retro_title = retro.get("section") if isinstance(retro, dict) else None

    pins = set()
    edits_path = folder / "deck-edits.json"
    if edits_path.is_file():
        edits = json.loads(edits_path.read_text(encoding="utf-8-sig"))
        for p in edits.get("pin") or []:
            pins.add(p.get("key"))
            if p.get("from"):
                pins.add(p["from"].get("key"))
        pins.discard(None)

    # 장면에 쓰지 않는 절: H1, 목차. 회고 장은 렌더러가 따로 그리므로 "장면 없음"(T8)을 묻지 않음
    skip_chapter = None
    in_retro = False
    rows = []
    for sec in sections:
        if sec["level"] == 1:
            continue
        if sec["level"] == 2:
            title_n = norm(re.sub(r"^[\d.★\s]+", "", sec["title"]))
            skip_chapter = sec["title"] if title_n in TOC_TITLES else None
            in_retro = bool(retro_title) and norm(retro_title) == norm(sec["title"])
        if skip_chapter:
            continue
        has_content = any([sec["mermaid"], sec["code"], sec["tables"], sec["images"],
                           sec["paras"], sec["items"]])
        layout, why = recommend(sec)
        matched = [s for s in slides if path_matches(s.get("sectionPath") or [], sec["path"])]
        deck_layouts = [s.get("layout", "?") for s in matched]
        keys = [s.get("key", "") for s in matched]
        types = []
        n_vis = len(sec["mermaid"]) + len(sec["code"]) + len(sec["tables"]) + sec["images"]
        text = sec["paras"] + sec["items"]
        if has_content and n_vis == 0:
            types.append("T1")
        if text >= MANY_TEXT_UNITS:
            types.append("T2")
        if n_vis >= 3:
            types.append("T3")
        if any(n > CODE_MAX_LINES for n in sec["code"]):
            types.append("T4")
        if any(n >= WIDE_TABLE_COLS for n in sec["tables"]):
            types.append("T5")
        if any(n > BIG_MERMAID_EDGES for n in sec["mermaid"]):
            types.append("T6")
        if deck_layouts and layout not in deck_layouts:
            types.append("T7")
        is_worth_scene = n_vis > 0 or text >= 3
        if has_deck and is_worth_scene and not matched and in_retro == False:
            types.append("T8")
        if sec["shots"] and not (sec["images"] or sec["mermaid"]):
            types.append("T9")
        rows.append({
            "line": sec["line"], "level": sec["level"], "title": sec["title"],
            "path": " > ".join(sec["path"]), "blocks": blocks_label(sec),
            "recommend": layout, "why": why, "has_content": has_content,
            "deck": ", ".join(deck_layouts) or "-", "keys": ", ".join(keys) or "-",
            "pinned": any(k in pins for k in keys), "types": types,
        })
    info = {
        "doc": doc, "data": data_path if data_path.is_file() else None,
        "slides": len(slides), "pins": len(pins),
        "deck_dist": Counter(s.get("layout", "?") for s in slides),
        "rec_dist": Counter(r["recommend"] for r in rows if r["has_content"]),
    }
    return (rows, info), None


def esc(text):
    return str(text).replace("|", "\\|").replace("\n", " ")


def write_md(out_path, name, rows, info, repo):
    def rel(p):
        try:
            return p.relative_to(repo).as_posix()
        except ValueError:
            return str(p)

    L = [f"# {name} 슬라이드 분류", ""]
    L.append(f"- 문서: `{rel(info['doc'])}` · 덱: `{rel(info['data']) if info['data'] else '없음 — 빌드 전'}`")
    L.append(f"- 덱 장면 {info['slides']}개(렌더러 장면 제외) · 고정(pin) 관련 key {info['pins']}개")
    L.append("- 이 파일은 `tools/deck-classify.py`가 만든 것입니다. 다시 돌리면 덮어씁니다.")
    L.append("")
    L.append("## 1. 틀 분포")
    L.append("")
    L.append("| 틀 | 지금 덱 | 문서 기준 추천 |")
    L.append("|---|---|---|")
    for lay in ["overview", "visual", "code", "full", "screens", "problem"]:
        L.append(f"| `{lay}` | {info['deck_dist'].get(lay, 0)} | {info['rec_dist'].get(lay, 0)} |")
    total = sum(info["deck_dist"].values())
    if total:
        top, cnt = info["deck_dist"].most_common(1)[0]
        if cnt * 2 > total:
            L.append("")
            L.append(f"> 쏠림: 덱 장면의 절반 넘게가 `{top}`입니다({cnt}/{total}).")
    L.append("")
    L.append("## 2. 유형별 묶음 (여기부터 보세요)")
    L.append("")
    L.append("| 유형 | 이름 | 절 수 | 어디서 고치나 |")
    L.append("|---|---|---|---|")
    counts = Counter(t for r in rows for t in r["types"])
    for code, (label, fix) in TYPES.items():
        L.append(f"| {code} | {label} | {counts.get(code, 0)} | {fix} |")
    for code, (label, _) in TYPES.items():
        hit = [r for r in rows if code in r["types"]]
        if not hit:
            continue
        L.append("")
        L.append(f"### {code} {label} ({len(hit)})")
        L.append("")
        for r in hit:
            extra = f" · 덱 `{r['deck']}` / 추천 `{r['recommend']}`" if code == "T7" else ""
            pin = " · 📌고정" if r["pinned"] else ""
            L.append(f"- {r['line']}줄 **{esc(r['title'])}** — {r['blocks']}{extra}{pin}")
    L.append("")
    L.append("## 3. 절 전체")
    L.append("")
    L.append("| 줄 | 절 | 블록 | 추천 틀 | 덱 틀 | 장면 key | 유형 |")
    L.append("|---|---|---|---|---|---|---|")
    for r in rows:
        indent = "　" * max(0, r["level"] - 2)
        pin = " 📌" if r["pinned"] else ""
        L.append(f"| {r['line']} | {indent}{esc(r['title'])} | {r['blocks']} | `{r['recommend']}` | "
                 f"{r['deck']} | {esc(r['keys'])}{pin} | {' '.join(r['types']) or '-'} |")
    L.append("")
    L.append("블록 표기: 그림=mermaid 개수 · 코드=블록별 줄 수 · 표=표별 열 수 · 글=문단+목록 줄 수.")
    out_path.write_text("\n".join(L) + "\n", encoding="utf-8")


def write_csv(out_path, rows):
    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["줄", "수준", "절 경로", "블록", "추천 틀", "추천 이유", "덱 틀", "장면 key", "고정", "유형"])
        for r in rows:
            w.writerow([r["line"], r["level"], r["path"], r["blocks"], r["recommend"], r["why"],
                        r["deck"], r["keys"], "Y" if r["pinned"] else "", " ".join(r["types"])])


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help", "/?"):
        print(__doc__)
        return 1
    target = Path(argv[1]).resolve()
    out_dir = Path(argv[2]).resolve() if len(argv) > 2 else None
    if not target.is_dir():
        print(f"[오류] 폴더가 아닙니다: {target}")
        return 1
    if find_doc(target):
        folders = [target]
    else:
        folders = sorted(p for p in target.iterdir() if p.is_dir() and not p.name.startswith("_") and find_doc(p))
    if not folders:
        print(f"[오류] 프로젝트 문서 md가 있는 폴더를 찾지 못했습니다: {target}")
        return 1
    repo = folders[0].parent.parent  # src/<폴더> 기준 레포 루트
    out_dir = out_dir or repo / "output" / "check" / "deck-classify"
    out_dir.mkdir(parents=True, exist_ok=True)
    for folder in folders:
        result, err = analyze(folder, repo)
        if err:
            print(f"[건너뜀] {err}")
            continue
        rows, info = result
        md_out = out_dir / f"{folder.name}-분류.md"
        write_md(md_out, folder.name, rows, info, repo)
        write_csv(out_dir / f"{folder.name}-분류.csv", rows)
        counts = Counter(t for r in rows for t in r["types"])
        summary = " ".join(f"{k}={counts[k]}" for k in TYPES if counts.get(k))
        print(f"[완료] {folder.name}: 절 {len(rows)}개 · 덱 장면 {info['slides']}개 · {summary or '유형 없음'}")
        print(f"       → {md_out}")
    return 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv))
