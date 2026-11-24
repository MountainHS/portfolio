"""UserPromptSubmit 훅: 프롬프트에 '자소서' 등 키워드가 있으면 /application-draft 스킬을 실행하라고 알린다.
없으면 아무것도 출력하지 않는다(스킬을 부르지 않음)."""
import json
import sys

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")

try:
    prompt = json.load(sys.stdin).get("prompt", "")
except Exception:
    sys.exit(0)

KEYWORDS = ("자소서", "지원서 초안", "자기소개서 문항", "프로젝트 이력 칸")

if any(k in prompt for k in KEYWORDS) and "/application-draft" not in prompt:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "UserPromptSubmit",
            "additionalContext": "프롬프트에 지원서 키워드(자소서·지원서 초안·자기소개서 문항·프로젝트 이력 칸)가 있습니다. 다른 일보다 먼저 Skill 도구로 application-draft 스킬을 실행하세요(사용자 지시: 이 키워드가 나오면 무조건 실행).",
        }
    }, ensure_ascii=False))
