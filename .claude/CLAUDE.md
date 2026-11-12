# 이 포크만의 규칙

> 루트 `CLAUDE.md`는 `/portfolio-update`가 템플릿 원본으로 바꿉니다. 이 파일(`.claude/CLAUDE.md`)은 업데이트가 건드리지 않으므로, 이 포크에서만 쓰는 규칙은 여기에 둡니다.

## 글쓰기 기본 지침

기술 문서·발표 덱·포트폴리오 문서에 모두 적용합니다. 기술 문서를 같이 쓰거나 "고쳐 줘"를 받을 때, 빌드가 장면 글을 고를 때 이 기준을 따릅니다.

- **독자는 채용 담당자·팀장입니다.** 사용자가 아닌 사람이 처음 읽어도 술술 읽혀야 합니다.
- **핵심 먼저, 부가 내용은 뒤로.** 한 문단의 첫 문장이 핵심입니다. 이유·예시·조건은 그 뒤에 둡니다.
- **"A가 아니라 B"로 쓰지 않습니다.** 하려던 것(B)을 바로 씁니다. 비교 대상(A)은 꼭 필요할 때만 뒤에 짧게 둡니다.
  - 나쁜 예: "단순히 플레이어를 따라다니며 공격하는 적이 아니라, 지능적으로 포위하거나 돌진·독 장판 같은 다양한 패턴으로 공격하는 적"
  - 좋은 예: "다양한 패턴으로 공격하는 몬스터를 만들려고 했다." 다음 줄부터 `- 포위: …`, `- 돌진: …` 목록
- **목적 글의 틀:** 핵심 문장(무엇을 하려 했나) + 프로젝트 핵심 경험과 잇는 말 → 아래에 예시를 unordered list로 둡니다.
- **"왜 만들었나"(목적) 절·장면에는 플레이어 경험과 설계 의도만 씁니다.** 클래스·상태 머신·Animator 같은 구현은 구조·핵심 기능 장에서 다룹니다.
- 기술 문서의 목적 절에 구현이 섞여 있으면, 빌드는 그 구현 글을 목적 장면 카드로 쓰지 않고 구조 장 앞쪽 장면으로 나눈 뒤 리포트에 알립니다.
- **시각화·장면 구성 규칙**(식별자 줄바꿈·상태 머신·코드 분할·세부 수준·계층 구조·AI 활용 과정)은 `.claude/rules/portfolio-writing.md`에 있습니다. 항상 적용하고, `/portfolio-update` 뒤에도 남습니다. 기술 문서 쓰기·점검·장면 고르기를 하위 에이전트에게 맡길 때는 브리프에 그 파일 경로와 해당 규칙 절을 붙입니다.

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

## 발표 덱 장 표지 키워드 (이 포크 전용)

- 장 표지(장면 4개 이하) 목록은 장면마다 `deck.slides[].keywords`(낱말 2~3개)를 ` · `로 이어 한 줄로 보여 줍니다. 도입 문장(`lead`)은 쓰지 않습니다(길이가 들쭉날쭉해서). `keywords`가 비면 설명 줄이 없습니다.
- `/portfolio-update`가 덮으면 다시 넣어야 하는 곳: `assets/project-deck.js`의 `keywordLine`과 `chapterScene`의 `roomy ? keywordLine(x.sl)`, `assets/deck-theme.css`의 `.ch-list.roomy .toc-list .ld` 한 줄 처리, `schema.md`·`SKILL.md`의 `keywords` 규칙, `deck-edits.py`의 `SLIDE_FIELDS`·검사·"확인해 주세요" 출력.

## 발표 덱 편집 모드 (이 포크 전용)

- 편집 서버(`edit-server.py`)로 연 발표 덱(`project.html?p=<폴더>`) 오른쪽 아래 [✎ 편집 모드] → 덱 위 글·그림을 누르면 아래에 입력창이 뜨고, 저장하면 새로고침해 같은 장면으로 돌아옵니다. 덱 데이터(`deck`)로 그린 덱에서만 켜지고, 예전 데이터 자동 구성 덱·GitHub Pages·`python -m http.server`에서는 버튼이 없습니다.
- 누른 것에 따라 저장 길이 둘입니다(`assets/deck-edit.js`가 나눔).
  1. 표·그림·코드·다이어그램(기술 문서 원문 블록) → 그 블록의 md 원문 줄 → `POST /__edit/save`(상세 문서 편집과 같은 길, 기술 문서만 고침). 덱이 블록을 찾는 지문(표 머리 칸·그림 경로·코드·다이어그램 첫 줄)을 바꾸면 `/portfolio-build`를 다시 할 때까지 그 장면에 띠가 뜹니다(입력창에 안내). data의 스크린샷 목록에서 온 그림(`ref.direct`)과 원문을 찾지 못한 그림(빨간 띠)은 누를 수 없습니다.
  2. 장면 제목·도입·요점 카드·장 표지 키워드 → `POST /__edit/deck` → `portfolio-edits.py`의 `save_deck_text`. 고친 글은 늘 `src/<폴더>/deck-edits.json`의 `"deckText"` 목록(`{slide, field, n?, value, from}`)에 남고, 기술 문서에 같은 글이 한 번만 그대로 있으면(제목 줄·코드 블록 제외) 그 줄도 고칩니다(입력창의 [발표 덱에만 저장]을 고르면 문서는 그대로). 키워드는 늘 덱에만. data는 `deck.slides`와 `deck.base`(있으면) 둘 다 고치고 `deck.applied` 해시를 맞추며, 글 조각의 `verbatim`은 `false`로 바꿉니다. 빌드 글(`from`)과 같게 고치면 그 줄이 지워집니다(되돌리기).
- **`/portfolio-build`와 `/portfolio-edit` 뒤에는 반드시** `deck-edits.py apply --all` 다음에 `python .claude/skills/portfolio-build/portfolio-edits.py apply --all`을 실행합니다(`deckText`를 data에 다시 덮음, 멱등). 특히 `/portfolio-edit`로 고정(pin)한 장면은 고치기 파일의 글로 다시 만들어지므로 이 apply가 없으면 덱 편집 모드에서 고친 글이 빠집니다.
- 장면 배치(틀·나누기·합치기·숨기기·순서)와 그림 파일 바꾸기는 편집 모드에 없습니다. 배치는 `/portfolio-edit`, 새 그림은 `src/<폴더>/images/`에 넣은 뒤 그림 줄의 경로를 고칩니다.
- `/portfolio-update`가 템플릿 원본으로 덮으면 다시 넣어야 하는 곳:
  - `project.html`: `<link rel="stylesheet" href="assets/deck-theme.css" />` 바로 다음 CSS 3줄(`assets/doc-edit.css`·`assets/portfolio-edit.css`·`assets/deck-edit.css`), `<script src="assets/project-deck.js"></script>` 바로 다음 스크립트 2줄(`assets/doc-edit.js`·`assets/deck-edit.js`, 이 순서)
  - `assets/project-deck.js`: `fitSlide` 바로 위의 `markForDeckEdit` 함수(장면에 `data-pf-slide`, 원문 블록 그림에 `data-pf-visual`)와, `fitSlide`의 `build` 안 `PF.body(s).innerHTML = …` 다음 줄 `markForDeckEdit(s, sl, vis);`
  - 남는 것(이 포크에만 있는 파일): `assets/deck-edit.js`·`assets/deck-edit.css`·`assets/doc-edit.js`(덱이 `PFDocEdit.mapBlocksToLines`를 빌려 씀)·`assets/doc-edit.css`·`assets/portfolio-edit.css`·`edit-server.py`(`/__edit/deck`)·`portfolio-edits.py`(`save_deck_text`·`apply_deck_text`).

## 발표 덱 장면 수 (이 포크 전용)

- 발표 덱은 **30~40장 권장**이고, 필요하면 넘겨도 됩니다. `/portfolio-build`는 장면 수를 맞추려고 장면(두 번째 코드·보조 표·겹치는 화면)을 **빼지 않습니다.** 40장을 넘을 때만 리포트에 [참고]로 장면 수를 알리고, 줄일지는 사용자가 `/portfolio-edit`로 정합니다.
- `/portfolio-update`가 템플릿 원본으로 덮으면 다시 고칠 곳(원본은 15~30장·30장 초과 시 빼기): `.claude/skills/portfolio-build/SKILL.md` §6 분량·§7 `many-scenes` 행, `schema.md` 「분량」, `deck-edits.py`의 `n1 > 40` 안내, `.claude/skills/portfolio-edit/SKILL.md`의 "40장 넘으면 알림", `assets/project-deck.js`의 `all.length > 40` `many-scenes` 알림, `assets/deck-common.js` 주석, `format/01-portfolio.md`·`format/02-tech-document.md`·`README.md`의 숫자.
- 코드 한 장면 상한은 **24줄**입니다(원본 20줄, 2026-11-11 Hive Purge `MonsterAttack.Tick` 세 단계를 한 장에 띄우려고 바꿈). `/portfolio-update`가 덮으면 `assets/project-deck.js`의 `const MAXL = 24;`와 그 아래 `slideScenes` 위 주석을 다시 고칩니다. 스킬·`schema.md`·`.claude/rules/portfolio-writing.md` 규칙 3의 "20줄" 글은 아직 그대로입니다.

## 명령 패널 (이 포크 전용)

- Claude 데스크톱 앱의 채팅 옆 패널 "포트폴리오 명령"(mod `portfolio-panel`). 이 레포를 연 세션에서 자동으로 뜨고, 닫았으면 `/portfolio-panel`로 다시 엽니다. README 「도구 한눈에」의 명령을 단계별 버튼으로 보여 주고, 맨 위에서 고른 프로젝트(`src/` 폴더, `_`로 시작하는 폴더 제외)를 명령 뒤에 붙입니다.
- 버튼 종류: **run**(`/portfolio-check`·`/portfolio-build` — 바로 실행) · **fill**(✋ 명령과 주소·스크린샷·요청 글이 필요한 명령 — 입력창에 채우기만, Enter는 사용자) · **say**(말 부탁 문장을 사용자 말로 보냄). ✋ 명령은 fill이므로 "사용자가 직접 입력했을 때만" 규칙이 그대로 지켜집니다.
- 파일: `tools/portfolio-panel/`(버튼 표는 `hooks/register.tsx` 맨 위 `BUTTONS` 배열만 고침) · `tools/.claude-plugin/marketplace.json`(로컬 마켓 `portfolio-tools`) · `.claude/settings.json`의 `enabledPlugins`. 설치된 플러그인은 레포 폴더를 그대로 읽으므로 고친 뒤 새 세션부터 반영됩니다.
- 새 PC에서 켜기: 앱에 딸린 `claude.exe`로 `claude plugin marketplace add ./tools` 한 번(사용자 설정에 마켓 등록). `enabledPlugins`는 레포에 있습니다.
- 확인: `claude plugin validate tools/portfolio-panel` · `claude plugin test tools/portfolio-panel`(버튼 run·fill·say가 각각 `command.run`·`prompt.fill`·`prompt.submit`으로 넘어가는지).
- `/portfolio-update`는 `tools/`·`.claude/settings.json`을 건드리지 않습니다.
