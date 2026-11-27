"""검사 도구 자체의 회귀 테스트 (이 포크 전용).

검사기가 ① 돌아가는지 ② 정상 입력을 통과시키는지 ③ 일부러 망친 입력을 잡는지 확인합니다.
검사기(tools/check-*.py·mjs, deck-edits.py)를 고친 뒤 실행합니다.

사용:
    python tools/verify-checks.py [--only A,B,C,D,E,F,G,H,I]
    B·C·D는 로컬 미리보기 서버가 켜져 있어야 합니다(.\\preview.ps1 server, 127.0.0.1:4600).
결과:
    화면에는 케이스마다 한 줄, 끝에 PASS n / FAIL m.
    각 실행의 원본 출력은 output/check/verify/에만 씁니다(대화·report에 붙이지 않음).

케이스:
    A 정적 문법   B 슬라이드 기준선   C 슬라이드 변이(--probe)   D PDF
    E 기술 카드   F 장면 분류기       G 카드 높이 CSS           H deck-edits.py 멱등·오류 검사
    I 문체 검사(voice-lint.py: 한다체·커밋 해시·제3자 시점)
"""

import argparse
import copy
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output" / "check" / "verify"
PROJECTS = sorted(path.stem for path in (ROOT / "data" / "projects").glob("*.json"))
PROBES = ["swapped-layout", "missing-highlight", "missing-code", "wrong-code-text", "wrong-retro-pair", "uneven-link-card", "uneven-screen-cell", "uneven-paired-row", "scene-count-toc"]
PREVIEW_URL = "http://127.0.0.1:4600"
RUN_TIMEOUT_SECONDS = 600

results = []  # (통과 여부, 케이스 이름, 한 줄 설명)


# ── 실행·기록 도우미 ──

def run(command, log_name):
    """명령을 돌리고 stdout·stderr를 output/check/verify/<log_name>.log에 남긴 뒤 (exit, stdout, stderr)를 돌려줍니다."""
    environment = dict(os.environ, PYTHONUTF8="1", PYTHONIOENCODING="utf-8")
    done = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", env=environment, timeout=RUN_TIMEOUT_SECONDS)
    log_text = f"$ {' '.join(map(str, command))}\nexit={done.returncode}\n--- stdout\n{done.stdout}\n--- stderr\n{done.stderr}"
    (OUT / f"{log_name}.log").write_text(log_text, encoding="utf-8")
    return done.returncode, done.stdout, done.stderr


def record(passed, case, detail):
    """케이스 결과 한 줄을 모으고 바로 화면에 찍습니다."""
    results.append((passed, case, detail))
    print(f"{'PASS' if passed else 'FAIL'} | {case:34} | {detail}", flush=True)


def failing_names(deck_summary):
    """check-deck.mjs 요약 JSON에서 실패한 검사 이름만 겹치지 않게 뽑습니다(최대 3개)."""
    names = []
    for project in deck_summary.get("projects", []):
        for finding in project.get("findings", []):
            if finding.get("pass") is False and finding["name"] not in names:
                names.append(finding["name"])
    return names[:3]


def read_json_output(stdout):
    """stdout 전체가 JSON이면 읽고, 아니면 None."""
    try:
        return json.loads(stdout)
    except json.JSONDecodeError:
        return None


def preview_is_running():
    try:
        with urllib.request.urlopen(PREVIEW_URL + "/__edit/ping", timeout=3):
            return True
    except OSError:
        return False


# ── A. 정적 문법 ──

def check_static():
    for script in ["tools/check-deck.mjs", "tools/deck-checks.mjs"]:
        code, _, stderr = run(["node", "--check", script], f"A-{Path(script).stem}")
        record(code == 0, f"A 문법 {script}", f"exit={code} {stderr.strip()[:80]}")
    for script in ["tools/check-deck-pdf.py", "tools/check-capabilities.py", "tools/deck-classify.py",
                   ".claude/skills/portfolio-build/deck-edits.py"]:
        code, _, stderr = run([sys.executable, "-m", "py_compile", script], f"A-{Path(script).stem}")
        record(code == 0, f"A 문법 {script}", f"exit={code} {stderr.strip()[:80]}")


# ── B. 슬라이드 기준선: 지금 덱은 통과해야 함(오탐 확인) ──

def check_deck_baseline():
    code, stdout, stderr = run(["node", "tools/check-deck.mjs", "--all", "--output", str(OUT / "B-all.json")], "B-all")
    summary = read_json_output(stdout) or {}
    for project in summary.get("projects", []):
        console_errors = len(project.get("consoleErrors") or [])
        names = failing_names({"projects": [project]})
        passed = project.get("passed") is True and console_errors == 0
        record(passed, f"B 기준선 {project['projectId']}",
               f"장면 {project.get('scenes')} · 콘솔오류 {console_errors} · 실패 {names or '-'}")
    if not summary:
        record(False, "B 기준선 --all", f"exit={code} {stderr.strip()[-120:]}")

    for project_id in PROJECTS:
        report_path = OUT / f"B-print-{project_id}.json"
        code, stdout, stderr = run(["node", "tools/check-deck.mjs", project_id, "--print", "--output", str(report_path)],
                                   f"B-print-{project_id}")
        if report_path.exists() == False:
            record(False, f"B 인쇄 {project_id}", f"exit={code} 보고서 없음 {stderr.strip()[-100:]}")
            continue
        project = json.loads(report_path.read_text(encoding="utf-8"))["projects"][0]
        print_ids = (project.get("print") or {}).get("ids") or []
        scene_count = len(project.get("sceneIds") or [])
        names = failing_names(read_json_output(stdout) or {})
        passed = code == 0 and len(print_ids) == scene_count
        record(passed, f"B 인쇄 {project_id}", f"exit={code} 인쇄쪽 {len(print_ids)} / 장면 {scene_count} · 실패 {names or '-'}")


# ── C. 슬라이드 변이: 브라우저 안에서 망친 덱은 실패해야 함(미탐 확인) ──

def check_deck_probes():
    for probe in PROBES:
        tried = []
        for project_id in PROJECTS:
            code, stdout, stderr = run(["node", "tools/check-deck.mjs", project_id, "--probe", probe,
                                        "--output", str(OUT / f"C-{probe}.json")], f"C-{probe}-{project_id}")
            # 요소가 없으면 check-deck.mjs가 'deck check completed' 실패의 근거(stdout)에 이 문구를 남깁니다
            element_missing = "requires a rendered" in stdout + stderr
            if element_missing:
                tried.append(project_id)
                continue
            names = failing_names(read_json_output(stdout) or {})
            passed = code == 1 and len(names) > 0
            record(passed, f"C 변이 {probe}", f"{project_id} exit={code} 실패 {names or '-'}")
            break
        else:
            record(False, f"C 변이 {probe}", f"적용할 요소가 있는 프로젝트 없음: {tried}")


# ── D. PDF: 맞는 PDF는 통과, 한 쪽 뺀 PDF는 page-count로 실패 ──

def check_pdf():
    from pypdf import PdfReader, PdfWriter

    project_id = "5-hive-purge" if "5-hive-purge" in PROJECTS else PROJECTS[0]
    pdf_path, report_path = OUT / "D-deck.pdf", OUT / "D-print.json"
    code, _, stderr = run(["node", "tools/check-deck.mjs", project_id, "--print", "--pdf", str(pdf_path),
                           "--output", str(report_path)], "D-print")
    if pdf_path.exists() == False:
        record(False, "D PDF 만들기", f"exit={code} {stderr.strip()[-120:]}")
        return

    code, stdout, _ = run([sys.executable, "tools/check-deck-pdf.py", str(pdf_path), str(report_path), project_id], "D-pdf-ok")
    error_types = sorted({error["type"] for error in (read_json_output(stdout) or {}).get("errors", [])})
    record(code == 0, f"D PDF 정상 {project_id}", f"exit={code} 오류 {error_types or '-'}")

    short_path = OUT / "D-deck-short.pdf"
    writer = PdfWriter()
    pages = PdfReader(pdf_path).pages
    for page in pages[:-1]:
        writer.add_page(page)
    with open(short_path, "wb") as short_file:
        writer.write(short_file)
    code, stdout, _ = run([sys.executable, "tools/check-deck-pdf.py", str(short_path), str(report_path), project_id], "D-pdf-short")
    error_types = sorted({error["type"] for error in (read_json_output(stdout) or {}).get("errors", [])})
    record(code == 1 and "page-count" in error_types, "D PDF 변이 마지막 쪽 뺌", f"exit={code} 오류 {error_types[:3]}")


# ── E. 기술 카드: 실제 데이터는 통과, 망친 사본은 실패 ──

def check_capabilities():
    real_path = ROOT / "data" / "portfolio.json"
    code, stdout, _ = run([sys.executable, "tools/check-capabilities.py", str(real_path)], "E-real")
    record(code == 0, "E 기술 카드 실제 데이터", f"exit={code} {stdout.strip()[:80]}")

    real = json.loads(real_path.read_text(encoding="utf-8"))
    mutations = {
        "카드 2장": ("카드 수", lambda data: data.update(skills=data["skills"][:2])),
        "제목 중복": ("제목이 중복", lambda data: data["skills"][1].update(name=data["skills"][0]["name"])),
        "없는 프로젝트": ("프로젝트 'no-such-project'", lambda data: data["skills"][0]["evidence"][0].update(project="no-such-project")),
    }
    for label, (expected_message, mutate) in mutations.items():
        broken = copy.deepcopy(real)
        mutate(broken)
        broken_path = OUT / f"E-{label}.json"
        broken_path.write_text(json.dumps(broken, ensure_ascii=False), encoding="utf-8")
        code, stdout, _ = run([sys.executable, "tools/check-capabilities.py", str(broken_path)], f"E-{label}")
        record(code == 1 and expected_message in stdout, f"E 기술 카드 변이 {label}", f"exit={code} {stdout.strip().splitlines()[0][:80] if stdout.strip() else '-'}")


# ── F. 장면 분류기: 돌아가고, 두 번 돌려도 결과가 같음 ──

def folder_hash(folder):
    digest = hashlib.sha256()
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            digest.update(path.name.encode())
            digest.update(path.read_bytes())
    return digest.hexdigest()[:12]


def check_classifier():
    hashes = []
    for attempt in (1, 2):
        out_dir = OUT / f"F-classify-{attempt}"
        code, _, stderr = run([sys.executable, "tools/deck-classify.py", "src", str(out_dir)], f"F-classify-{attempt}")
        if code != 0:
            record(False, "F 분류기 실행", f"exit={code} {stderr.strip()[-100:]}")
            return
        hashes.append(folder_hash(out_dir))
    record(hashes[0] == hashes[1], "F 분류기 두 번 결과 같음", f"{hashes[0]} / {hashes[1]}")


# ── G. 기술 카드 높이: CSS가 같은 높이를 보장하는지 ──

def css_rule(css, selector):
    """첫 번째 `selector { … }` 규칙 본문. 미디어 쿼리 안 규칙은 뒤에 있으므로 첫 번째가 기본 규칙입니다."""
    match = re.search(r"(?m)^\s*" + re.escape(selector) + r"\s*\{([^}]*)\}", css)
    return match.group(1) if match else ""


def check_card_height_css():
    css = (ROOT / "assets" / "portfolio.css").read_text(encoding="utf-8")
    grid_rule, card_rule = css_rule(css, ".skills"), css_rule(css, ".skill")
    rows_are_equal = re.search(r"grid-auto-rows\s*:\s*1fr", grid_rule) is not None
    card_has_fixed_height = re.search(r"(?<![-\w])height\s*:", card_rule) is not None
    record(rows_are_equal and card_has_fixed_height == False, "G 카드 높이 CSS",
           f".skills grid-auto-rows:1fr={rows_are_equal} · .skill 고정 height={card_has_fixed_height}")


# ── H. deck-edits.py: 두 번 적용해도 data가 그대로, 범위 밖 강조는 오류 ──

def git(*arguments):
    return subprocess.run(["git", *arguments], cwd=ROOT, capture_output=True, text=True, encoding="utf-8").stdout


def apply_edits(label):
    for script in ["deck-edits.py", "portfolio-edits.py"]:
        code, _, _ = run([sys.executable, f".claude/skills/portfolio-build/{script}", "apply", "--all"], f"H-{label}-{Path(script).stem}")
        if code != 0:
            return code
    return 0


def find_highlights(node):
    """deck-edits.json 안에서 처음 나오는 highlights 목록."""
    if isinstance(node, dict):
        if isinstance(node.get("highlights"), list) and node["highlights"]:
            return node["highlights"]
        children = node.values()
    elif isinstance(node, list):
        children = node
    else:
        return None
    for child in children:
        found = find_highlights(child)
        if found is not None:
            return found
    return None


def check_deck_edits():
    if git("status", "--porcelain", "--", "src", "data").strip():
        record(False, "H deck-edits 멱등", "건너뜀: src·data에 커밋 안 된 변경이 있음(그 변경이 data에 섞임)")
    else:
        first_code = apply_edits("first")
        first_diff = git("diff", "--stat", "--", "data").strip()
        second_code = apply_edits("second")
        second_diff = git("diff", "--stat", "--", "data").strip()
        (OUT / "H-diff.log").write_text(f"1회 뒤\n{first_diff}\n\n2회 뒤\n{second_diff}\n", encoding="utf-8")
        record(first_code == 0 and first_diff == "", "H deck-edits 커밋된 data와 같음", f"exit={first_code} 변경 {first_diff.splitlines()[-1] if first_diff else '없음'}")
        record(second_code == 0 and second_diff == first_diff, "H deck-edits 두 번째도 같음", f"exit={second_code}")
        if first_diff or second_diff:
            git("checkout", "--", "data")  # 실행 전 깨끗했으므로 커밋 상태로 되돌림

    target = next((project_id for project_id in PROJECTS
                   if find_highlights(json.loads((ROOT / "src" / project_id / "deck-edits.json").read_text(encoding="utf-8")))), None)
    if target is None:
        record(False, "H deck-edits 변이 범위 밖 강조", "highlights가 있는 프로젝트 없음")
        return
    edits_path = ROOT / "src" / target / "deck-edits.json"
    original = edits_path.read_bytes()
    try:
        edits = json.loads(original.decode("utf-8"))
        find_highlights(edits)[0].update(start=9998, end=9999)
        edits_path.write_text(json.dumps(edits, ensure_ascii=False, indent=2), encoding="utf-8")
        code, stdout, _ = run([sys.executable, ".claude/skills/portfolio-build/deck-edits.py", "check", target], "H-bad-highlight")
    finally:
        edits_path.write_bytes(original)
    record(code == 1 and "필수" in stdout, "H deck-edits 변이 범위 밖 강조", f"{target} exit={code} {stdout.strip().splitlines()[-1] if stdout.strip() else '-'}")


# ── I. 문체 검사(voice-lint.py): 잡을 줄은 잡고, 둘 줄은 두는지 ──

VOICE_SAMPLE = """# 제목은 한다체여도 둔다.
구조를 바꿨다.
구조를 바꿨습니다.
커밋 7572c57에서 고쳤습니다.
색 #17628B, 버전 17.3.0, 기간 2026.06.16.
제작자의 회고에서는 좋았습니다.
> 인용한 요청 원문은 둔다.
| 표 칸은 둔다. |
```
int count = 0; // 코드 주석은 둔다.
```
"""
VOICE_EXPECTED_LINES = [2, 4, 6]  # 한다체 · 커밋 해시 · 제3자 시점


def check_voice_lint():
    sample = OUT / "I-voice-sample.md"
    sample.write_text(VOICE_SAMPLE, encoding="utf-8")
    _, stdout, _ = run([sys.executable, "tools/voice-lint.py", str(sample)], "I-voice-lint")
    found = sorted({int(number) for number in re.findall(r"I-voice-sample\.md:(\d+):", stdout)})
    record(found == VOICE_EXPECTED_LINES, "I 문체 검사 잡을 줄만 잡음", f"기대 {VOICE_EXPECTED_LINES} / 실제 {found}")


# ── 실행 ──

CASES = {"A": check_static, "B": check_deck_baseline, "C": check_deck_probes, "D": check_pdf,
         "E": check_capabilities, "F": check_classifier, "G": check_card_height_css, "H": check_deck_edits,
         "I": check_voice_lint}
NEEDS_PREVIEW = {"B", "C", "D"}


def main():
    parser = argparse.ArgumentParser(description="검사 도구 자체의 회귀 테스트")
    parser.add_argument("--only", default=",".join(CASES), help="돌릴 케이스 글자, 쉼표로(예: A,B)")
    selected = [key.strip().upper() for key in parser.parse_args().only.split(",") if key.strip()]
    OUT.mkdir(parents=True, exist_ok=True)

    if NEEDS_PREVIEW & set(selected) and preview_is_running() == False:
        print(f"미리보기 서버가 꺼져 있습니다({PREVIEW_URL}). .\\preview.ps1 server 뒤 다시 실행하세요. B·C·D를 건너뜁니다.")
        selected = [key for key in selected if key not in NEEDS_PREVIEW]

    for key in selected:
        CASES[key]()

    failed = sum(1 for passed, _, _ in results if passed == False)
    print(f"\nPASS {len(results) - failed} / FAIL {failed}   (원본 출력: {OUT.relative_to(ROOT)})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
