## 편집 모드 (/edit-mode-install로 설치)

> 이 절은 `/edit-mode-install`이 씁니다. 다시 실행하면 표지 주석(`edit-mode-install:start`·`end`) 사이를 새로 바꾸므로, 직접 적을 규칙은 이 절 밖에 둡니다.

- **편집 모드**: 편집 서버(`edit-server.py`, 포트 4600)로 연 세 페이지 오른쪽 아래에 [✎ 편집 모드]가 생깁니다. 글을 누르면 입력창이 뜨고, 저장하면 소스와 `data/`를 함께 고칩니다.
  - 상세 문서(`doc.html`): 누른 블록의 md 원문 줄을 기술 문서에 바로 씁니다.
  - 포트폴리오 문서(`index.html`): 입력창에 `저장 위치: …`를 먼저 보여 줍니다. 저장 위치는 `.claude/skills/portfolio-build/portfolio-edits.py`가 정합니다(프로필·기술 이름 → `profile.md`, 기술 문서에 한 번만 그대로 있는 글 → 그 기술 문서 줄, 그 밖 → `src/<폴더>/deck-edits.json`의 `"portfolio"` 목록).
  - 발표 덱(`project.html`): 표·그림·코드·다이어그램 → 기술 문서의 원문 줄. 장면 제목·도입·요점 카드 → `deck-edits.json`의 `"deckText"` 목록(기술 문서에 같은 글이 한 번만 그대로 있으면 그 줄도). 장면 배치(틀·나누기·합치기·숨기기·순서)는 편집 모드가 아니라 `/portfolio-edit`로 고칩니다.
  - GitHub Pages나 `python -m http.server`로 열면 버튼이 없습니다(배포된 페이지는 그대로).
- **빌드 뒤 반영**: `/portfolio-build`와 `/portfolio-edit`가 끝나면 **반드시** 이 순서로 실행합니다(여러 번 실행해도 결과가 같음). 빠뜨리면 다시 만든 `data/`에서 편집 모드로 고친 글이 빠집니다.
  1. `python .claude/skills/portfolio-build/deck-edits.py apply --all`
  2. `python .claude/skills/portfolio-build/portfolio-edits.py apply --all`
  - `python`이 없으면 `py`로 실행합니다. 1이 "portfolio(포트폴리오 문서 고치기)는 아직 지원하지 않아 무시했습니다"라고 알려도 2가 반영하므로 괜찮습니다.
- **미리보기**: `preview.cmd`(더블클릭) 또는 `.\preview.ps1 [all|portfolio|deck|doc|server|stop] [프로젝트 폴더]`로 엽니다. 편집 서버를 켜고 페이지를 브라우저로 엽니다. 스킬이 미리보기로 `python -m http.server 4600`을 띄우라고 해도 `python edit-server.py`를 띄웁니다.
- **편집 버튼이 안 보이면**: 4600 포트를 `python -m http.server`가 쓰고 있는 것입니다. `.\preview.ps1 stop`으로 끈 뒤 다시 켭니다. 저장할 때 "편집 서버가 이 기능 전의 것입니다"가 떠도 같은 방법입니다.
- **템플릿 업데이트 뒤**: `/portfolio-update`는 `index.html`·`project.html`·`doc.html`·`assets/doc.js`·`assets/project-deck.js`를 템플릿 원본으로 바꿔 편집 모드 줄을 지웁니다. 업데이트가 이 파일들을 "직접 고친 흔적이 있습니다"라고 물으면 **바꾸기**를 고르고, 업데이트를 커밋한 뒤 `/edit-mode-install`을 다시 실행합니다. 새 파일(`edit-server.py`·`preview.*`·`assets/*-edit.*`·`portfolio-edits.py`)과 이 절은 업데이트 뒤에도 남습니다.
