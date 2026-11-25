"""Check the portfolio capability index before or after a rebuild."""

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def check(data):
    problems = []
    skills = data.get("skills")
    projects = data.get("projects")
    if not isinstance(skills, list) or not isinstance(projects, list):
        return ["skills와 projects는 배열이어야 합니다."]
    if not 3 <= len(skills) <= 8:
        problems.append(f"카드 수 {len(skills)}개: 3~8개여야 합니다.")

    seen_names = set()
    seen_keywords = set()
    project_ids = set(projects)
    for index, skill in enumerate(skills, 1):
        name = skill.get("name")
        if not isinstance(name, str) or not name.strip():
            problems.append(f"{index}번 카드: 제목이 비었습니다.")
            continue
        if name in seen_names:
            problems.append(f"{name}: 카드 제목이 중복됩니다.")
        seen_names.add(name)
        evidence = skill.get("evidence")
        if not isinstance(evidence, list) or not 2 <= len(evidence) <= 4:
            problems.append(f"{name}: 기술 키워드는 2~4개여야 합니다.")
            continue
        for item in evidence:
            keyword = item.get("text")
            project = item.get("project")
            if not isinstance(keyword, str) or not keyword.strip():
                problems.append(f"{name}: 빈 기술 키워드가 있습니다.")
                continue
            if keyword in seen_keywords:
                problems.append(f"{name}: '{keyword}' 키워드가 다른 카드와 중복됩니다.")
            seen_keywords.add(keyword)
            if (len(keyword) > 40 or "\n" in keyword or "`" in keyword
                    or "→" in keyword or re.search(r"[.!?]$", keyword)):
                problems.append(f"{name}: '{keyword}'는 기술 색인에 맞게 짧은 명사형으로 고쳐야 합니다.")
            if (project not in project_ids
                    or not (ROOT / "data" / "projects" / f"{project}.json").is_file()
                    or not (ROOT / "src" / str(project)).is_dir()):
                problems.append(f"{name}: '{keyword}'의 프로젝트 '{project}'가 projects 목록, 상세 데이터 또는 src 폴더에 없습니다.")
    return problems


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "data" / "portfolio.json"
    problems = check(json.loads(path.read_text(encoding="utf-8")))
    for problem in problems:
        print(f"[오류] {problem}")
    if problems:
        return 1
    print("기술 역량 색인: 구조·중복·형식·프로젝트 연결 통과")
    return 0


if __name__ == "__main__":
    sys.exit(main())
