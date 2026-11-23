"""보고투 문장 검사: 프로젝트 문서에 '작업 보고' 말투(나에게 하는 보고)가 남았는지 찾습니다.

왜: 2026-11-22 Hive Purge 3장 퇴고에서 독자(채용 담당자)가 아니라 작업자에게 보고하는 문장이
본문에 들어갔음. 실제 사례:
  - "지금 구조가 제출본의 최종 구조입니다."
  - "…3.2.7절에 따로 적었습니다."
  - "이 표와 그림에서는 두 클래스를 '이동' 하나로 묶었습니다."

쓰는 법:
  python tools/voice-lint.py src/5-hive-purge/기술문서.md   # 걸린 줄을 줄 번호와 함께 보여 줌
  python tools/voice-lint.py --hook                          # PostToolUse 훅(.claude/settings.json)이 부름

걸린 줄이 모두 잘못은 아닙니다(예: "브랜치"가 실제 git 이야기일 때). 발표 자리에서 그대로 말해도 자연스러우면 둡니다.
"""
import json
import re
import sys
from pathlib import Path

# 낱말 → 왜 보고투인지(고칠 방향)
PATTERNS = [
    (r"제출본|최종본|최종 구조|이번 버전|지금 구조가", "작업 단계 보고 — 독자는 '제출본'을 모름. 구조 자체를 설명"),
    (r"에 (따로 )?적었습니다|적어 두었|따로 정리", "글쓴이 메모 — '~에서 설명합니다'로"),
    (r"표와 그림에서|이 표에서는|이 그림에서는", "편집 설명 — '그래서 ~로 보여 드립니다' 또는 내용만"),
    (r"이 순서대로 읽|괄호 안 번호", "목차 안내 — 지움"),
    (r"브랜치", "작업 이력 — '최종 빌드' 등 독자 말로"),
    (r"이번에는", "작업 회차 — '이 프로젝트에서는'으로"),
]
COMPILED = [(re.compile(pattern), reason) for pattern, reason in PATTERNS]
CODE_FENCE = re.compile(r"^\s*(```|~~~)")


def find_hits(lines, first_line_number=1):
    """줄 목록에서 보고투 낱말이 든 줄을 찾습니다. 코드 블록 안은 건너뜁니다.

    lines: 검사할 줄들
    first_line_number: lines[0]의 파일 줄 번호(훅에서 고친 조각만 볼 때 위치를 모르면 1)
    returns: (줄 번호, 걸린 낱말, 이유, 줄 글) 목록. 없으면 빈 목록
    """
    hits = []
    in_code = False
    for offset, line in enumerate(lines):
        if CODE_FENCE.match(line):
            in_code = in_code == False
            continue
        # 인용(>)은 건너뛰지 않음: 「아쉬운 점」 같은 본문 상자도 인용 문법이라(2026-11-22 1074줄을 놓침)
        if in_code:
            continue
        for pattern, reason in COMPILED:
            match = pattern.search(line)
            if match:
                hits.append((first_line_number + offset, match.group(0), reason, line.strip()))
    return hits


def is_project_document(path):
    """src/<폴더>/ 바로 아래의 프로젝트 문서 md인지 봅니다(_로 시작하는 폴더·_intake 제외)."""
    parts = path.replace("\\", "/").split("/")
    if "src" not in parts or path.endswith(".md") == False:
        return False
    after_src = parts[parts.index("src") + 1:]
    return len(after_src) == 2 and after_src[0].startswith("_") == False


def run_hook():
    """PostToolUse 훅: 고친 글(Edit의 new_string, Write의 content)만 검사해 걸리면 Claude에게 알립니다.

    파일 전체가 아니라 이번에 넣은 글만 보므로, 이미 남아 있는 줄 때문에 매번 같은 경고가 나지 않습니다.
    걸린 것이 없으면 아무것도 출력하지 않습니다(토큰 0).
    """
    try:
        # Windows 기본 stdin은 cp949라 한글 경로·글이 깨지므로 바이트를 UTF-8로 풂
        data = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    except ValueError:
        return
    tool_input = data.get("tool_input") or {}
    path = tool_input.get("file_path", "")
    if is_project_document(path) == False:
        return
    new_text = tool_input.get("new_string") or tool_input.get("content") or ""
    for edit in tool_input.get("edits") or []:
        new_text += "\n" + edit.get("new_string", "")
    hits = find_hits(new_text.splitlines())
    if len(hits) == 0:
        return
    lines = [f"- 「{word}」 {reason}: {text[:80]}" for _, word, reason, text in hits]
    message = (
        "보고투 검사(tools/voice-lint.py)에 걸린 글을 방금 넣었습니다. "
        "발표 자리에서 그대로 말해도 자연스러운지 보고, 아니면 이 턴 안에 고치세요.\n" + "\n".join(lines)
    )
    # Windows 콘솔 기본 cp949로 쓰면 한글이 깨지므로 UTF-8 바이트로 씀
    sys.stdout.buffer.write(json.dumps({
        "hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": message}
    }, ensure_ascii=False).encode("utf-8"))


def run_files(paths):
    """파일마다 걸린 줄을 출력합니다. 하나라도 걸리면 종료 코드 1."""
    total = 0
    for path in paths:
        text = Path(path).read_text(encoding="utf-8")
        for number, word, reason, line in find_hits(text.splitlines()):
            total += 1
            print(f"{path}:{number}: 「{word}」 {reason}\n    {line[:120]}")
    print(f"보고투 후보 {total}건")
    return 1 if total else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    if sys.argv[1:] == ["--hook"]:
        run_hook()
        sys.exit(0)
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(run_files(sys.argv[1:]))
