"""PostToolUse 훅: 소개 영상 자막 파일(video/*-subtitles.tsv)을 고치면 퇴고 스킬을 돌리라고 Claude에게 알림.

왜: 퇴고 스킬(video-subtitle-revise)은 Claude가 판단해서 부르는 것이라, 2026-11-21에 자막을 고치고도
마무리 때까지 미뤄진 일이 있었음. 고친 그 턴에 돌도록 알림을 강제로 넣음.
"""
import json
import re
import sys

try:
    data = json.load(sys.stdin)
except ValueError:
    sys.exit(0)
path = (data.get("tool_input") or {}).get("file_path", "").replace("\\", "/")
if re.search(r"(^|/)video/[^/]+-subtitles\.tsv$", path):
    # Windows 콘솔 기본 cp949로 쓰면 한글이 깨지므로 UTF-8 바이트로 씀
    sys.stdout.buffer.write(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": (
                f"자막 파일({path})을 고쳤습니다. 사용자에게 결과를 내기 전에 이 턴 안에서 "
                "video-subtitle-revise 스킬을 실행하세요(검사기만 돌리는 것으로 대신하지 않음)."
            ),
        }
    }, ensure_ascii=False).encode("utf-8"))
sys.exit(0)
