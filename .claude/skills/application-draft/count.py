"""지원서 글자 수 세기 (표준 라이브러리만).

사용법:
    python count.py <md 파일>              # ```text 블록마다 셈
    python count.py <md 파일> --sections   # 제목 줄(#)로 나눈 절마다 셈

세 가지 수를 냅니다.
    공백 포함: 빈 줄·줄바꿈을 1자로 셈 (지원서 화면 대부분)
    공백 제외: 공백·줄바꿈을 모두 뺌
    줄바꿈 2자: 줄바꿈 하나를 2자로 세는 지원서 칸 대비
"""

import re
import sys


def count(text: str) -> tuple[int, int, int]:
    body = text.strip("\n")
    with_space = len(body)
    without_space = len(re.sub(r"\s", "", body))
    newline_as_two = with_space + body.count("\n")
    return with_space, without_space, newline_as_two


def text_blocks(source: str) -> list[tuple[str, str]]:
    blocks = []
    for match in re.finditer(r"```text\n(.*?)```", source, re.S):
        body = match.group(1)
        label = body.strip().splitlines()[0][:30] if body.strip() else "(빈 블록)"
        blocks.append((label, body))
    return blocks


def heading_sections(source: str) -> list[tuple[str, str]]:
    sections = []
    label, lines = None, []
    for line in source.splitlines():
        if line.startswith("#"):
            if label is not None and "".join(lines).strip():
                sections.append((label, "\n".join(lines)))
            label, lines = line.lstrip("# ").strip(), []
        else:
            lines.append(line)
    if label is not None and "".join(lines).strip():
        sections.append((label, "\n".join(lines)))
    return sections


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    with open(sys.argv[1], encoding="utf-8") as file:
        source = file.read()
    parts = heading_sections(source) if "--sections" in sys.argv else text_blocks(source)
    print("| 글 | 공백 포함 | 공백 제외 | 줄바꿈 2자 |")
    print("|---|---|---|---|")
    for label, body in parts:
        a, b, c = count(body)
        print(f"| {label} | {a} | {b} | {c} |")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
