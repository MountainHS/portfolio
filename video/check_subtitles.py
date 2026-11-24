"""소개 영상 자막 검사기.

사용법:  python video/check_subtitles.py hive              (video/hive-subtitles.tsv 검사)
         python video/check_subtitles.py hive <자막.tsv>   (다른 자막 파일 검사, 예: 초안)
퇴고:    .claude/skills/video-subtitle-revise/SKILL.md (자막을 고칠 때마다 렌더 전에 쓰는 스킬)
단어표:  video/subtitle-words.tsv (금지 / 주의 / 허용 — 사용자가 고침)

오류가 하나라도 있으면 종료 코드 1을 돌려줍니다. render.py는 이때 렌더를 멈춥니다.
검사기는 단어·길이·시각만 봅니다. "화면에 보이는가", "자막이 필요한가"는 사람이 판단합니다.
"""
import re
import sys
from pathlib import Path

from render import ROOT, build_cuts, is_short, parse_time, read_tsv

# ── 기준값 (바꾸려면 여기만) ──
MAX_CHARACTERS_PER_LINE = 32          # 이보다 길면 화면에서 두 줄로 넘어감
MAX_CHARACTERS_PER_LINE_SHORT = 16    # 쇼츠(세로 1080 폭, 60px) 한 줄 — 넘으면 \N으로 나누기
MAX_CHARACTERS_PER_SECOND = 10        # 공백 뺀 글자 수 / 실제 표시 초
MIN_DISPLAY_SECONDS = 1.5             # 배속 반영한 실제 표시 시간
NEEDS_SCREEN_CHECK = "화면 확인 필요"
SKIPPED = "자막 생략"


def main() -> None:
    """자막 파일을 읽어 줄마다 검사하고, 오류·경고·확인 과제를 출력합니다."""
    if len(sys.argv) < 2:
        sys.exit("사용법: python video/check_subtitles.py <hive|wgo> [자막.tsv]")
    project = sys.argv[1]
    subtitle_path = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / f"{project}-subtitles.tsv"
    error_count = check_project(project, subtitle_path)
    sys.exit(1 if error_count > 0 else 0)


def check_project(project: str, subtitle_path: Path) -> int:
    """한 프로젝트의 자막 파일을 검사해 결과를 출력합니다.

    render.py도 렌더 전에 이 함수를 부릅니다.
    project: "hive" 또는 "wgo" — 컷 표(<project>-cuts.tsv)와 review 파일을 찾는 데 씀.
    subtitle_path: 검사할 자막 TSV 경로.
    반환: 오류 개수(0이면 렌더해도 됨).
    """
    cuts_by_id = {cut["id"]: cut for cut in build_cuts(project)}
    words = read_tsv(ROOT / "subtitle-words.tsv")
    reviewed_cut_ids = read_reviewed_cut_ids(project)
    errors, warnings, screen_checks = [], [], []

    for line_number, subtitle in enumerate(read_tsv(subtitle_path), start=2):
        label = f"{line_number}줄 {subtitle['cut'].strip()}"
        text = subtitle["text"].strip()
        status = subtitle.get("from", "").strip()
        if text == "":
            continue
        cut = cuts_by_id.get(subtitle["cut"].strip())
        if cut is None:
            errors.append(f"{label}: cut이 컷 표에 없음")
            continue

        # 쇼츠는 뒷부분에 기술·내 담당을 넣으므로(2026-11-24 사용자 결정) 단어표의 금지·주의 낱말을 보지 않음
        checked_words = [] if is_short(project) else words
        errors += [f"{label}: {problem}" for problem in find_word_errors(text, checked_words, words)]
        warnings += [f"{label}: {problem}" for problem in find_word_warnings(text, checked_words)]
        timing_problem, display_seconds = measure_display_time(subtitle, cut)
        if timing_problem:
            errors.append(f"{label}: {timing_problem}")
            continue
        max_per_line = MAX_CHARACTERS_PER_LINE_SHORT if is_short(project) else MAX_CHARACTERS_PER_LINE
        warnings += [f"{label}: {problem}" for problem in find_length_warnings(text, display_seconds, max_per_line)]
        if NEEDS_SCREEN_CHECK in status:
            screen_checks.append(f"{label} ({subtitle['start']}~{subtitle['end']}): {text}")
            if subtitle["cut"].strip() not in reviewed_cut_ids:
                warnings.append(f"{label}: '{NEEDS_SCREEN_CHECK}'인데 {project}-subtitles-review.tsv에 없음")

    print_section("오류 (렌더 멈춤)", errors)
    print_section("경고", warnings)
    print_section("영상에서 확인할 줄 (확인 전에는 단정하지 않음)", screen_checks)
    print(f"검사: {subtitle_path.name} — 오류 {len(errors)} · 경고 {len(warnings)} · 확인 과제 {len(screen_checks)}")
    return len(errors)


def read_reviewed_cut_ids(project: str) -> set[str]:
    """화면 확인 목록(<project>-subtitles-review.tsv)에 올라 있는 cut ID를 모읍니다. 파일이 없으면 빈 집합."""
    review_path = ROOT / f"{project}-subtitles-review.tsv"
    if review_path.exists() == False:
        return set()
    return {row["cut"].strip() for row in read_tsv(review_path)}


def find_word_errors(text: str, words: list[dict], all_words: list[dict]) -> list[str]:
    """금지어와 허용 목록 밖의 영문 단어를 찾습니다.

    words: 금지어를 볼 단어표 줄(쇼츠면 빈 목록). all_words: 허용 영문을 찾을 전체 단어표.
    반환: 문제 설명 목록(없으면 빈 목록).
    """
    problems = []
    for word in words:
        if word["kind"].strip() == "금지" and word["word"] in text:
            problems.append(f"금지어 '{word['word']}' — {word['reason']}")
    allowed_english = {word["word"] for word in all_words if word["kind"].strip() == "허용"}
    for english_word in re.findall(r"[A-Za-z]+", text.replace("\\N", " ")):
        if english_word not in allowed_english:
            problems.append(f"영문 '{english_word}' — 비개발자가 바로 읽는 한국어로, 필요 없으면 문장째 삭제")
    return problems


def find_word_warnings(text: str, words: list[dict]) -> list[str]:
    """주의어와 화살표 도식 문장을 찾습니다. 반환: 문제 설명 목록."""
    problems = []
    for word in words:
        if word["kind"].strip() == "주의" and word["word"] in text:
            problems.append(f"주의어 '{word['word']}' — {word['reason']}")
    if "→" in text:
        problems.append("화살표 도식 문장 — 기술문서식 요약. 화면에서 일어나는 일을 문장으로")
    return problems


def measure_display_time(subtitle: dict, cut: dict) -> tuple[str, float]:
    """자막이 실제 영상에 보이는 시간(배속 반영)을 잽니다.

    반환: (문제 설명 또는 "", 표시 초). 자막 시각이 컷 구간 밖이면 문제 설명을 돌려줍니다.
    render.py와 같은 방식으로 컷 구간에 맞춰 자릅니다.
    """
    start = max(parse_time(subtitle["start"]), cut["t0"])
    end = min(parse_time(subtitle["end"]), cut["t1"])
    if end <= start:
        return f"시각 {subtitle['start']}~{subtitle['end']}이 컷 구간 {cut['start']}~{cut['end']} 밖", 0.0
    return "", (end - start) / cut["spd"]


def find_length_warnings(text: str, display_seconds: float, max_per_line: int) -> list[str]:
    """글자 수·읽기 속도·표시 시간을 기준값과 비교합니다.

    max_per_line: 한 줄 최대 글자 수 (가로 영상 32, 쇼츠 16).
    반환: 문제 설명 목록.
    """
    problems = []
    lines = text.split("\\N")  # \N = ASS 줄바꿈 (자막을 두 줄로)
    character_count = len("".join(lines).replace(" ", ""))
    longest = max(len(line) for line in lines)
    if longest > max_per_line:
        problems.append(f"{longest}자 — 한 줄 {max_per_line}자 넘음, 줄이기")
    if display_seconds < MIN_DISPLAY_SECONDS:
        problems.append(f"표시 {display_seconds:.1f}초 — {MIN_DISPLAY_SECONDS}초 미만, 시각 늘리거나 생략")
    elif character_count / display_seconds > MAX_CHARACTERS_PER_SECOND:
        problems.append(
            f"초당 {character_count / display_seconds:.1f}자 ({display_seconds:.1f}초에 {character_count}자) — "
            f"{MAX_CHARACTERS_PER_SECOND}자 넘음, 줄이거나 시각 늘리기"
        )
    return problems


def print_section(title: str, lines: list[str]) -> None:
    """검사 결과 한 묶음을 제목과 함께 출력합니다. 빈 묶음은 건너뜁니다."""
    if len(lines) == 0:
        return
    print(f"[{title}]")
    for line in lines:
        print(f"  - {line}")


if __name__ == "__main__":
    main()
