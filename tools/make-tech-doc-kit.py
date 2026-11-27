"""기술문서 고민 정리 키트를 output/tech-doc-kit/ 폴더와 output/tech-doc-kit.zip으로 묶습니다.

왜: 이 레포에서 쓴 원천 분석 → 고민 분류 → 문서 반영 → 퇴고·피드백 절차를 다른 사람이
자기 레포에 옮겨 쓰게 하려고. 정본은 레포의 스킬·규칙·도구이고, 이 스크립트는 복사·경로 가리기·압축만 합니다.

쓰는 법:
  python tools/make-tech-doc-kit.py

하는 일:
  1. 아래 COPY 목록을 키트 폴더로 복사합니다(키트 문서 README.md·AI-GUIDE.md는 tools/kit-docs/에서).
  2. .claude/CLAUDE.md에서 report 규칙 절만 뽑아 .claude/rules/report-structure.md로 씁니다.
  3. 글 파일의 개인 절대경로·이메일을 자리표시자로 바꾸고, 바꾼 수를 출력합니다.
  4. 남은 개인 경로·이메일이 있으면 목록을 출력하고 종료 코드 1(zip은 만들지 않음).
이 스크립트 자체는 키트에 넣지 않습니다(이 레포 전용).
"""
import json
import re
import shutil
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "output" / "tech-doc-kit"
OUT_ZIP = ROOT / "output" / "tech-doc-kit.zip"
SKILL = ".claude/skills/tech-doc-issue-summary"

# ── 복사 목록: (레포 안 경로, 키트 안 경로) ──
COPY = [
    ("tools/kit-docs/README.md", "README.md"),
    ("tools/kit-docs/AI-GUIDE.md", "AI-GUIDE.md"),
    ("tools/kit-docs/고민-레벨과-구체화-예시.md", "고민-레벨과-구체화-예시.md"),
    (SKILL, SKILL),
    (".claude/rules/portfolio-writing.md", ".claude/rules/portfolio-writing.md"),
    ("tools/voice-lint.py", "tools/voice-lint.py"),
    ("tools/report-toc.py", "tools/report-toc.py"),
    ("report/고민-분류/INDEX.md", "examples/고민-분류/INDEX.md"),
    ("report/고민-분류/WGO.md", "examples/고민-분류/WGO.md"),
    ("report/고민-분류/5-hive-purge.md", "examples/고민-분류/5-hive-purge.md"),
    ("report/고민-분류/WGO-Q22-대화.md", "examples/고민-분류/WGO-Q22-대화.md"),
    ("report/고민-분류/WGO-협업.md", "examples/고민-분류/WGO-협업.md"),
    ("report/WGO/2026-11-09-확인-필요·측정-필요-답변-초안.md", "examples/준비/확인-필요-답변-초안.md"),
    ("report/피드백-반영-계획.md", "examples/퇴고-피드백/피드백-반영-계획.md"),
    ("report/3-mini-ftl/2026-11-19-기존-문서와-개편안을-섞음.md", "examples/퇴고-피드백/개편안-섞기.md"),
    ("report/WGO/2026-11-26-1-3장-연결성-개선-계획.md", "examples/퇴고-피드백/장-연결성-개선-계획.md"),
]

# ── 다른 파일에서 절 하나만 뽑아 쓰는 것: (레포 안 파일, 시작 제목, 키트 안 경로, 머리말) ──
EXCERPTS = [
    ("report/archive/5-hive-purge-이전기록.md", "### 2026-11-11 — 고민점 문서 대조",
     "examples/준비/고민점-문서-대조.md",
     "<!-- 실제 예: Hive Purge report 「이전 기록」에서 이 절만 뽑았습니다. 견본은 templates/doc-compare.md. -->\n\n"),
]
CLAUDE_MD_SECTIONS = ["## report 구조", "## 프로젝트 문서 고민 분류 (이 포크 전용)"]

HOOKS = {
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "Edit|Write|MultiEdit",
                "hooks": [
                    {"type": "command", "command": "python \"$CLAUDE_PROJECT_DIR/tools/voice-lint.py\" --hook"}
                ],
            }
        ]
    }
}

# ── 가릴 개인 정보: 긴 경로부터 바꿈 ──
HOME = "C:/Users/hsgood01"
REPO_NAME = "repos/RawBlog/portfolio-test"
NOTE_ROOT = "repos/RawBlog"
REPLACEMENTS = [
    (f"{HOME}/{REPO_NAME}", "<레포>"),
    (f"{HOME}/{NOTE_ROOT}", "<노트 루트>"),
    (HOME, "<홈>"),
    ("hsgood01", "<사용자>"),
    ("MountainHS", "<사용자>"),
    # 예시 report에 남은 팀원 실명·계정(2026-11-27 grep으로 찾은 것). 새 이름이 보이면 여기에 더함
    ("신성건", "<팀원1>"),
    ("SaGiKun4", "<팀원2 계정>"),
]
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
TEXT_SUFFIXES = {".md", ".py", ".tsv", ".json", ".txt"}
LEFTOVER = re.compile(r"hsgood01|MountainHS|C:[\\/]+Users|신성건|SaGiKun4", re.IGNORECASE)


def section(text, heading):
    """heading 줄부터 같은 깊이 이상의 다음 제목 전까지를 돌려줍니다. 없으면 빈 글."""
    lines = text.split("\n")
    depth = len(heading) - len(heading.lstrip("#"))
    start = next((i for i, line in enumerate(lines) if line.startswith(heading)), None)
    if start is None:
        return ""
    end = len(lines)
    for i in range(start + 1, len(lines)):
        line = lines[i]
        is_heading = line.startswith("#")
        if is_heading and len(line) - len(line.lstrip("#")) <= depth:
            end = i
            break
    return "\n".join(lines[start:end]).rstrip() + "\n"


def anonymize(text):
    """경로 구분자를 통일한 뒤 개인 경로·이메일을 자리표시자로 바꿉니다. (새 글, 바꾼 수)."""
    count = 0
    for backslash_form in {"\\\\", "\\"}:
        for old, _ in REPLACEMENTS[:3]:
            text, n = re.subn(re.escape(old.replace("/", backslash_form)), old, text, flags=re.IGNORECASE)
            count += n
    for old, new in REPLACEMENTS:
        text, n = re.subn(re.escape(old), new, text, flags=re.IGNORECASE)
        count += n
    text, n = EMAIL.subn("<이메일>", text)
    return text, count + n


def build_files():
    """키트 폴더를 새로 만들고 파일을 채웁니다."""
    # 폴더 자체는 두고 안만 비움(탐색기·터미널이 이 폴더를 열고 있어도 지울 수 있게)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for child in OUT_DIR.iterdir():
        if child.is_dir():
            shutil.rmtree(child)
        else:
            child.unlink()
    for source, target in COPY:
        src_path, dst_path = ROOT / source, OUT_DIR / target
        dst_path.parent.mkdir(parents=True, exist_ok=True)
        if src_path.is_dir():
            shutil.copytree(src_path, dst_path, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        else:
            shutil.copy2(src_path, dst_path)
    for source, heading, target, header in EXCERPTS:
        body = section((ROOT / source).read_text(encoding="utf-8"), heading)
        if body == "":
            sys.exit(f"[멈춤] {source}에서 '{heading}' 절을 찾지 못했습니다.")
        dst_path = OUT_DIR / target
        dst_path.parent.mkdir(parents=True, exist_ok=True)
        dst_path.write_text(header + body, encoding="utf-8")
    claude_md = (ROOT / ".claude/CLAUDE.md").read_text(encoding="utf-8")
    parts = [section(claude_md, heading) for heading in CLAUDE_MD_SECTIONS]
    rules = ("# report 운영 규칙\n\n> 원래 레포 `.claude/CLAUDE.md`에서 뽑은 두 절입니다. "
             "호칭·경로 예시는 대상 레포에 맞게 고칩니다(AI-GUIDE.md 3-4).\n\n" + "\n".join(parts))
    (OUT_DIR / ".claude/rules/report-structure.md").write_text(rules, encoding="utf-8")
    (OUT_DIR / "settings-hooks.json").write_text(json.dumps(HOOKS, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def scrub():
    """글 파일을 가리고, 남은 개인 정보 줄을 돌려줍니다."""
    total, leftovers = 0, []
    for path in sorted(OUT_DIR.rglob("*")):
        if path.is_file() == False or path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        text, n = anonymize(path.read_text(encoding="utf-8"))
        path.write_text(text, encoding="utf-8")
        total += n
        for no, line in enumerate(text.split("\n"), 1):
            if LEFTOVER.search(line) or EMAIL.search(line):
                leftovers.append(f"{path.relative_to(OUT_DIR).as_posix()}:{no}: {line.strip()[:100]}")
    return total, leftovers


def make_zip():
    """키트 폴더를 tech-doc-kit/ 한 폴더가 든 zip으로 묶습니다."""
    if OUT_ZIP.exists():
        OUT_ZIP.unlink()
    with zipfile.ZipFile(OUT_ZIP, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(OUT_DIR.rglob("*")):
            if path.is_file():
                archive.write(path, Path("tech-doc-kit") / path.relative_to(OUT_DIR))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    build_files()
    replaced, leftovers = scrub()
    file_count = sum(1 for p in OUT_DIR.rglob("*") if p.is_file())
    print(f"키트 파일 {file_count}개, 개인 경로·이메일 {replaced}곳을 가림 → {OUT_DIR}")
    if leftovers:
        print("[멈춤] 남은 개인 정보 줄(zip을 만들지 않음):")
        print("\n".join(leftovers))
        sys.exit(1)
    make_zip()
    print(f"zip {OUT_ZIP.stat().st_size // 1024} KB → {OUT_ZIP}")


if __name__ == "__main__":
    main()
