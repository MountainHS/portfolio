"""report 맨 위 옵시디언 목차 블록을 넣거나 갱신한다.

구조(2026-11-26):
  report/<이름>.md            메인. 사람이 보는 곳. 목차 블록(✍ 할 일 + 작업 기록 링크) + 짧은 머리글만.
  report/<이름>/YYYY-MM-DD-<작업>.md   작업 하나 = 파일 하나. 첫 줄 `# 제목`. 할 일이면 제목에 ✍, 끝나면 ✔.

사용:
  python tools/report-toc.py report/5-hive-purge.md [...]        목차 갱신(작업 폴더를 읽어 링크 목록을 다시 만듦)
  python tools/report-toc.py --split report/5-hive-purge.md      메인의 작업 절을 작업 폴더 파일로 나눈 뒤 목차
  python tools/report-toc.py --archive report/x.md                「이전 기록」 절을 report/archive/<이름>-이전기록.md로 옮긴 뒤 목차
  python tools/report-toc.py --check report/*.md                  목차가 낡았으면 exit 1

새 작업은 작업 폴더에 파일을 새로 쓰고 이 스크립트를 돌린다. 메인을 손으로 고치지 않는다.
목차 블록은 H1 바로 아래 `<!-- toc:start -->` ~ `<!-- toc:end -->` 사이에 둔다. 수동으로 고치지 않는다.
"""
import re
import sys
from pathlib import Path

START, END = "<!-- toc:start -->", "<!-- toc:end -->"
ARCHIVE_TITLE = re.compile(r"^#{1,2} 이전 기록\s*$")
TODO_PARENT = re.compile(r"^## 이제 할 일\s*$")
BAD = re.compile(r"[\[\]|#^]")
FILE_BAD = re.compile(r"[\\/:*?\"<>|\[\]#^`'✍✔]")
DATE = re.compile(r"(\d{4}-\d{2}-\d{2})")


def strip_toc(lines):
    if START in lines:
        s = lines.index(START)
        e = lines.index(END, s)
        del lines[s:e + 1]
        while s < len(lines) and s > 0 and lines[s] == "" and lines[s - 1] == "":
            del lines[s]
    return lines


def headings(lines, levels=(2, 3)):
    out, fence = [], False
    for i, ln in enumerate(lines):
        if ln.lstrip().startswith("```"):
            fence = not fence
            continue
        m = None if fence else re.match(r"^(#{1,6}) (.+?)\s*$", ln)
        if m and len(m.group(1)) in levels:
            out.append((i, len(m.group(1)), m.group(2)))
    return out


def link_target(text):
    return BAD.sub("", text).strip()


def task_files(path):
    folder = path.parent / path.stem
    if not folder.is_dir():
        return []
    out = []
    for f in folder.glob("*.md"):
        first = f.read_text(encoding="utf-8").splitlines()[:1]
        title = first[0][2:].strip() if first and first[0].startswith("# ") else f.stem
        n = len(f.read_text(encoding="utf-8").splitlines())
        out.append((f.stem, title, n))
    return sorted(out, key=lambda t: t[0], reverse=True)


def build_toc(path, lines, archive_link=None):
    hs = headings(lines)
    tasks = task_files(path)
    rows = [START, ""]
    todo = [f"> - [[#{link_target(t)}]]" for _, _, t in hs if "✍" in t]
    todo += [f"> - [[{path.stem}/{stem}|{BAD.sub('', title).strip()}]]" for stem, title, _ in tasks if "✍" in title]
    if todo:
        rows += ["> [!todo] ✍ 내가 할 일 (제목에 ✍가 있는 작업. 끝나면 ✍를 ✔로)"] + todo + [""]
    if tasks:
        rows += [f"**작업 기록** (`report/{path.stem}/` 파일 하나 = 작업 하나, 최신순. 자동 생성: `python tools/report-toc.py`)", ""]
        rows += [f"- [[{path.stem}/{stem}|{BAD.sub('', title).strip()}]] · {n}줄" for stem, title, n in tasks]
        rows += [""]
    if hs:
        rows += ["**이 파일 목차** (필요한 절만 `Grep \"^## 제목\"`으로 찾아 읽기)", ""]
        for k, (i, lvl, text) in enumerate(hs):
            nxt = next((j for j, l2, _ in hs[k + 1:] if l2 <= lvl), len(lines))
            indent = "  " if lvl == 3 else ""
            rows.append(f"{indent}- [[#{link_target(text)}]] · {nxt - i}줄")
    if archive_link:
        rows.append(f"- 이전 기록: [[{archive_link}]]")
    rows += ["", END]
    return rows


def insert_toc(lines, toc):
    h1 = next((i for i, l in enumerate(lines) if l.startswith("# ")), -1)
    pos = h1 + 1
    return lines[:pos] + [""] + toc + lines[pos:]


def move_archive(path, lines):
    idx = next((i for i, l in enumerate(lines) if ARCHIVE_TITLE.match(l)), None)
    if idx is None:
        return lines, None
    name = f"{path.stem}-이전기록"
    dest = path.parent / "archive" / f"{name}.md"
    dest.parent.mkdir(exist_ok=True)
    body = lines[idx + 1:]
    head = [f"# {path.stem} — 이전 기록", "", f"원본: [[{path.stem}]] (report-toc.py --archive로 옮김)", ""]
    if dest.exists():
        old = dest.read_text(encoding="utf-8").splitlines()
        dest.write_text("\n".join(old + ["", "---", ""] + body) + "\n", encoding="utf-8")
    else:
        dest.write_text("\n".join(head + body) + "\n", encoding="utf-8")
    kept = lines[:idx] + ["## 이전 기록", "", f"[[archive/{name}]]로 옮김."]
    return kept, f"archive/{name}"


def file_name(title, date, used):
    rest = DATE.sub("", re.sub(r"\([^)]*\)", "", title))
    slug = FILE_BAD.sub("", rest).strip(" -—·").replace(" ", "-")
    slug = re.sub(r"-{2,}", "-", slug).strip("-")[:40].rstrip("-") or "기록"
    base = f"{date}-{slug}" if date else slug
    name, n = base, 2
    while name in used:
        name, n = f"{base}-{n}", n + 1
    used.add(name)
    return name


def split(path, lines, nl):
    """「이제 할 일」 아래 ### 절과 그 밖의 ## 절(「이전 기록」 제외)을 report/<이름>/ 파일로 옮긴다."""
    hs = headings(lines, levels=(2, 3))
    units, parent = [], None
    for k, (i, lvl, text) in enumerate(hs):
        if lvl == 2:
            parent = text
            if TODO_PARENT.match("## " + text) or ARCHIVE_TITLE.match("## " + text):
                continue
            end = next((j for j, l2, _ in hs[k + 1:] if l2 <= 2), len(lines))
            units.append((i, end, 2, text))
        elif parent is not None and TODO_PARENT.match("## " + parent):
            end = next((j for j, l2, _ in hs[k + 1:] if l2 <= 3), len(lines))
            units.append((i, end, 3, text))
    if not units:
        return lines
    folder = path.parent / path.stem
    folder.mkdir(exist_ok=True)
    used = {f.stem for f in folder.glob("*.md")}
    top, sub, files, date = {}, {}, [], ""
    for i, end, lvl, text in units:
        m = DATE.search(text)
        date = m.group(1) if m else date
        name = file_name(text, date, used)
        top[link_target(text)] = name
        body = lines[i:end]
        for _, _, t in headings(body, levels=(4, 5, 6) if lvl == 3 else (3, 4, 5, 6)):
            sub.setdefault(link_target(t), name)
        files.append((name, lvl, body))

    def relink(s):
        def rep(m):
            h = m.group(1).strip()
            if h in top:
                return f"[[{path.stem}/{top[h]}"
            if h in sub:
                return f"[[{path.stem}/{sub[h]}#{h}"
            return m.group(0)
        return re.sub(r"\[\[#([^\]|]+)", rep, s)

    for name, lvl, body in files:
        out, fence = [], False
        for ln in body:
            if ln.lstrip().startswith("```"):
                fence = not fence
            m = None if fence else re.match(r"^(#{2,6}) (.*)$", ln)
            if m:
                ln = "#" * (len(m.group(1)) - (lvl - 1)) + " " + m.group(2)
            out.append(relink(ln))
        out.insert(1, "")
        out.insert(2, f"메인: [[{path.stem}]]")
        while out and out[-1].strip() in ("", "---"):
            out.pop()
        (folder / f"{name}.md").write_text(nl.join(out) + nl, encoding="utf-8", newline="")

    moved = set()
    for i, end, _, _ in units:
        moved.update(range(i, end))
    kept = [relink(l) for k, l in enumerate(lines) if k not in moved and not TODO_PARENT.match(l)]
    while len(kept) > 1 and kept[-1] == "" and kept[-2] == "":
        kept.pop()
    print(f"{path}: 작업 파일 {len(files)}개 → {folder}")
    return kept


def process(path, archive, check, do_split):
    raw = path.read_text(encoding="utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"
    lines = strip_toc(raw.splitlines())
    if do_split:
        lines = split(path, lines, nl)
    link = None
    if archive:
        lines, link = move_archive(path, lines)
    if link is None:
        old = re.search(r"- 이전 기록: \[\[(.+?)\]\]", raw)
        link = old.group(1) if old else None
    new = nl.join(insert_toc(lines, build_toc(path, lines, link))) + nl
    if check:
        return new == raw
    path.write_text(new, encoding="utf-8", newline="")
    return True


def main(argv):
    archive, check, do_split = "--archive" in argv, "--check" in argv, "--split" in argv
    files = [Path(a) for a in argv if not a.startswith("--")]
    stale = [str(f) for f in files if not process(f, archive, check, do_split)]
    if stale:
        print("목차 낡음:", *stale, sep="\n  ")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
