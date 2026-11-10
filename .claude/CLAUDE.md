# 이 포크만의 규칙

> 루트 `CLAUDE.md`는 `/portfolio-update`가 템플릿 원본으로 바꿉니다. 이 파일(`.claude/CLAUDE.md`)은 업데이트가 건드리지 않으므로, 이 포크에서만 쓰는 규칙은 여기에 둡니다.

## report 구조

- **루트 `report.md` = 요약판.** 사용자가 오랜만에 오거나 정신없을 때 이것만 보고 다시 시작할 수 있게, 다음 네 절만 짧게 씁니다.
  1. 마지막으로 한 일 (날짜, 커밋 여부)
  2. 다시 시작할 때 할 것 (3줄 안팎, 어느 프로젝트 report의 어느 절을 보면 되는지)
  3. 리마인드 — 메모리에 "나중에 하기로 한 것"으로 남은 일(시연 영상·기간 보완·블로그 링크 등)
  4. 프로젝트별 report 표 (파일 경로 · 한 줄 상태)
- **`report/<src 폴더 이름>.md` = 프로젝트별 report.** 그 프로젝트의 할 일·"확인해 주세요"·결정 기록·질문 답변·점검 결과를 모두 여기에 둡니다. 새 내용은 맨 위 "이제 할 일"에 쓰고, 지난 것은 아래 "이전 기록"으로 내립니다.
- `report/`와 `report.md`는 둘 다 `.gitignore`에 있어 공개되지 않습니다.

## 스킬이 루트 report.md를 덮어쓸 때

`/portfolio-build`·`/portfolio-check`·`/portfolio-edit`는 루트 `report.md`에 씁니다. 스킬을 고치지 않고 다음 순서로 맞춥니다.

1. 스킬을 실행하기 **전에** 루트 `report.md`의 내용이 `report/`에 다 옮겨져 있는지 확인합니다(요약판이면 그대로 두어도 됨 — 요약판 내용은 프로젝트 report와 메모리로 다시 만들 수 있음).
2. 스킬이 끝나면 루트 `report.md`에 생긴 점검 결과를 프로젝트별로 나눠 `report/<폴더>.md` 맨 위 "이제 할 일" 아래에 옮깁니다. 이때 기존 "확인해 주세요" 줄과 사용자가 단 답(들여쓰기 줄)을 지우지 않습니다.
3. 루트 `report.md`를 위 요약판 형식으로 다시 씁니다.

## 로컬 미리보기

- `preview.cmd`(더블클릭) 또는 `.\preview.ps1 [all|portfolio|deck|doc|report|server|stop] [프로젝트 폴더]`로 엽니다. 편집 서버(`edit-server.py`, 포트 4600)를 켜고 페이지를 브라우저로 엽니다.
- 4600 포트를 `python -m http.server`가 쓰고 있으면 편집 모드가 꺼지므로 `.\preview.ps1 stop` 뒤 다시 켭니다. 사용자에게 미리보기를 안내할 때도 이 스크립트를 먼저 알려 줍니다.

## 포트폴리오 문서 편집 모드 (이 포크 전용)

- 편집 서버(`edit-server.py`)로 연 포트폴리오 문서(`index.html`) 오른쪽 아래 [✎ 편집 모드] → 글을 누르면 아래에 입력창이 뜨고, 저장할 곳(`저장 위치: …`)을 먼저 보여 줍니다. 저장하면 소스와 `data/`를 함께 고치고 새로고침합니다.
- 저장 위치 규칙(`.claude/skills/portfolio-build/portfolio-edits.py`가 정함, 편집 서버는 요청만 넘김):
  1. 프로필(이름·영문 이름·한 줄 정체성·이메일·GitHub·자기소개)·기술 이름 → `profile.md`의 그 줄(기술 이름은 「기술 스택 메모」에 적힌 것만)
  2. 기술 문서에 같은 글자가 한 번만 그대로 있는 글(한 줄 소개·역할·기간 같은 개요 표 값·그대로 옮긴 개발 의도 등) → 그 기술 문서 줄. 제목 줄·코드 블록은 빼고, 입력창의 [포트폴리오에만 저장]을 고르면 3으로
  3. 그 밖(Claude가 줄인 글·태그·프로젝트 이름·기술 근거 문장) → `src/<폴더>/deck-edits.json`의 `"portfolio"` 목록. 빌드 글(`from`)과 같게 고치면 그 줄이 지워집니다(되돌리기)
- **`/portfolio-build` 뒤에는 반드시** `deck-edits.py apply --all` 다음에 `python .claude/skills/portfolio-build/portfolio-edits.py apply --all`을 실행합니다(3의 글을 data에 다시 덮음, 멱등). 빌드 스킬 §5에도 적었지만 `/portfolio-update`가 스킬을 덮어쓰면 그 줄이 사라지므로 이 규칙이 기준입니다.
- `/portfolio-update`가 템플릿 원본으로 덮는 이 기능의 파일: `index.html`(편집 CSS 2줄·스크립트 1줄), `.claude/skills/portfolio-build/SKILL.md`·`schema.md`·`deck-edits.py`(설명 몇 줄). 업데이트 뒤 `index.html`의 세 줄을 다시 넣어야 편집 모드가 켜집니다. 새 파일(`assets/portfolio-edit.js`·`assets/portfolio-edit.css`·`portfolio-edits.py`)과 `edit-server.py`는 남습니다.
- 편집 서버를 이 기능 전에 켜 두었으면 저장 때 "편집 서버가 이 기능 전의 것입니다"가 뜹니다 → `.\preview.ps1 stop` 뒤 다시 켭니다.
