---
paths:
  - "assets/**"
  - "*.html"
  - "edit-server.py"
  - "preview.ps1"
  - "preview.cmd"
  - "data/**"
  - "src/**/deck-edits.json"
  - "tools/**"
  - ".claude/settings.json"
  - ".claude/skills/portfolio-*/**"
  - ".claude/skills/edit-mode-install/**"
  - ".claude/skills/local-preview/**"
---

# 이 포크 전용 기능 (편집 모드·슬라이드·명령 패널)

> `.claude/CLAUDE.md`에서 옮긴 절입니다. 위 `paths`의 파일을 읽거나 고칠 때만 실립니다(2026-11-16, 세션 시작 문맥 줄이기). 빌드 뒤 apply·장면 수처럼 늘 지켜야 하는 규칙은 `.claude/CLAUDE.md`에 한 줄씩 남아 있습니다.

## 포트폴리오 페이지 편집 모드 (이 포크 전용)

- 편집 서버(`edit-server.py`)로 연 포트폴리오 페이지(`index.html`) 오른쪽 아래 [✎ 편집 모드] → 글을 누르면 아래에 입력창이 뜨고, 저장할 곳(`저장 위치: …`)을 먼저 보여 줍니다. 저장하면 소스와 `data/`를 함께 고치고 새로고침합니다.
- 저장 위치 규칙(`.claude/skills/portfolio-build/portfolio-edits.py`가 정함, 편집 서버는 요청만 넘김):
  1. 프로필(이름·영문 이름·한 줄 정체성·이메일·GitHub·자기소개)·기술 이름 → `profile.md`의 그 줄(기술 이름은 「기술 스택 메모」에 적힌 것만)
  2. 프로젝트 문서에 같은 글자가 한 번만 그대로 있는 글(한 줄 소개·역할·기간 같은 개요 표 값·그대로 옮긴 개발 의도 등) → 그 프로젝트 문서 줄. 제목 줄·코드 블록은 빼고, 입력창의 [포트폴리오에만 저장]을 고르면 3으로
  3. 그 밖(Claude가 줄인 글·태그·프로젝트 이름·기술 근거 문장) → `src/<폴더>/deck-edits.json`의 `"editModePortfolio"` 목록(원본 템플릿의 `"portfolio"` 칸과 겹치지 않게 이름을 따로 둠). 빌드 글(`from`)과 같게 고치면 그 줄이 지워집니다(되돌리기)
- **`/portfolio-build` 뒤에는 반드시** `deck-edits.py apply --all` 다음에 `python .claude/skills/portfolio-build/portfolio-edits.py apply --all`을 실행합니다(3의 글을 data에 다시 덮음, 멱등). 빌드 스킬 §5에도 적었지만 `/portfolio-update`가 스킬을 덮어쓰면 그 줄이 사라지므로 이 규칙이 기준입니다.
- `index.html`의 `</body>` 위 로더 한 줄(`assets/edit-loader.js`)은 디스크에 없습니다. 편집 서버가 보낼 때만 메모리에서 넣습니다(`.claude/skills/edit-mode-install/edit-mode-patch.py`의 `GROUPS`). 편집 CSS·JS는 로더가 편집 서버가 있을 때만 싣습니다(포트폴리오 페이지는 전역 `PFPage`로 알아봄). 그래서 `/portfolio-update` 뒤에도 할 일이 없습니다. 원본이 `</body>` 줄을 바꾸면 서버 창에 `[멈춤]`이 뜨고 그 페이지만 편집 버튼이 없습니다 → `GROUPS`의 기준 줄을 고칩니다.
- `/portfolio-update`가 템플릿 원본으로 덮는 이 기능의 파일: `.claude/skills/portfolio-build/SKILL.md`·`schema.md`·`deck-edits.py`(설명 몇 줄). 새 파일(`assets/edit-loader.js`·`assets/portfolio-edit.js`·`assets/portfolio-edit.css`·`portfolio-edits.py`)과 `edit-server.py`는 남습니다.
- 편집 서버를 이 기능 전에 켜 두었으면 저장 때 "편집 서버가 이 기능 전의 것입니다"가 뜹니다 → `.\preview.ps1 stop` 뒤 다시 켭니다.

## 슬라이드 장 표지 키워드 (이 포크 전용)

- 장 표지(장면 4개 이하) 목록은 장면마다 `deck.slides[].keywords`(낱말 2~3개)를 ` · `로 이어 한 줄로 보여 줍니다. 도입 문장(`lead`)은 쓰지 않습니다(길이가 들쭉날쭉해서). `keywords`가 비면 설명 줄이 없습니다.
- `/portfolio-update`가 덮으면 다시 넣어야 하는 곳: `assets/project-deck.js`의 `keywordLine`과 `chapterScene`의 `roomy ? keywordLine(x.sl)`, `assets/deck-theme.css`의 `.ch-list.roomy .toc-list .ld` 한 줄 처리, `schema.md`·`SKILL.md`의 `keywords` 규칙, `deck-edits.py`의 `SLIDE_FIELDS`·검사·"확인해 주세요" 출력.

## 슬라이드 편집 모드 (이 포크 전용)

- 편집 서버(`edit-server.py`)로 연 슬라이드(`project.html?p=<폴더>`) 오른쪽 아래 [✎ 편집 모드] → 덱 위 글·그림을 누르면 아래에 입력창이 뜨고, 저장하면 새로고침해 같은 장면으로 돌아옵니다. 덱 데이터(`deck`)로 그린 덱에서만 켜지고, 예전 데이터 자동 구성 덱·GitHub Pages·`python -m http.server`에서는 버튼이 없습니다.
- 누른 것에 따라 저장 길이 둘입니다(`assets/deck-edit.js`가 나눔).
  1. 표·코드·다이어그램(프로젝트 문서 원문 블록, 그림(image) 자체는 편집에서 뺌 — 그림 설명 글만 아래 2번) → 그 블록의 md 원문 줄 → `POST /__edit/save`(상세 문서 편집과 같은 길, 프로젝트 문서만 고침). 덱이 블록을 찾는 지문(표 머리 칸·그림 경로·코드·다이어그램 첫 줄)을 바꾸면 `/portfolio-build`를 다시 할 때까지 그 장면에 띠가 뜹니다(입력창에 안내). data의 스크린샷 목록에서 온 그림(`ref.direct`)과 원문을 찾지 못한 그림(빨간 띠)은 누를 수 없습니다.
  2. 장면 제목·도입·요점 카드·그림 설명(캡션)·장 표지 키워드 → `POST /__edit/deck` → `portfolio-edits.py`의 `save_deck_text`. 고친 글은 늘 `src/<폴더>/deck-edits.json`의 `"deckText"` 목록(`{slide, field, n?, value, from}`)에 남고, 프로젝트 문서에 같은 글이 한 번만 그대로 있으면(제목 줄·코드 블록 제외) 그 줄도 고칩니다(입력창의 [슬라이드에만 저장]을 고르면 문서는 그대로). 키워드는 늘 덱에만. 그림 설명은 `{slide, field:"caption", n: visuals 번호(1부터)}`이고 값은 data `visuals[n-1].caption.text`(글자면 `.caption`)이며, 프로젝트 문서에서는 같은 글이 한 번뿐인 그림 줄(alt)을 고칩니다. 같은 장면에 같은 `src` 그림이 둘이거나 `ref.direct`면 누를 수 없고, 코드 캡션(렌더러가 만든 언어·줄 범위 라벨)은 대상이 아닙니다. data는 `deck.slides`와 `deck.base`(있으면) 둘 다 고치고 `deck.applied` 해시를 맞추며, 글 조각의 `verbatim`은 `false`로 바꿉니다. 빌드 글(`from`)과 같게 고치면 그 줄이 지워집니다(되돌리기).
- **`/portfolio-build`와 `/portfolio-edit` 뒤에는 반드시** `deck-edits.py apply --all` 다음에 `python .claude/skills/portfolio-build/portfolio-edits.py apply --all`을 실행합니다(`deckText`를 data에 다시 덮음, 멱등). 특히 `/portfolio-edit`로 고정(pin)한 장면은 고치기 파일의 글로 다시 만들어지므로 이 apply가 없으면 덱 편집 모드에서 고친 글이 빠집니다.
- 장면 배치(틀·나누기·합치기·숨기기·순서)와 그림 파일 바꾸기는 편집 모드에 없습니다. 배치는 `/portfolio-edit`, 새 그림은 `src/<폴더>/images/`에 넣은 뒤 그림 줄의 경로를 고칩니다.
- 템플릿 파일에 넣는 줄은 디스크에 없습니다. 편집 서버가 보낼 때만 메모리에서 넣으므로(`.claude/skills/edit-mode-install/edit-mode-patch.py`의 `GROUPS`) `/portfolio-update`가 덮어도 다시 넣을 것이 없습니다. 원본이 `</body>` 줄을 바꿔 서버 창에 `[멈춤]`이 뜨면 `GROUPS`의 기준 줄을 고칩니다.
  - `project.html`: `</body>` 바로 위 로더 한 줄(`assets/edit-loader.js`). 로더가 전역 `PFScene.info.page === 'project'`로 덱을 알아보고 CSS 3개(`doc-edit.css`→`portfolio-edit.css`→`deck-edit.css`)를 `<head>` 끝에, JS 2개(`doc-edit.js`→`deck-edit.js`)를 차례로 싣습니다. 원본이 `PFDoc`·`PFPage`·`PFScene.info.page`를 안 만들게 바뀌면 편집 버튼이 **조용히** 없습니다.
  - `assets/project-deck.js`는 가로채지 않습니다. 대신 `deck-edit.js`가 원본이 이미 다는 것을 읽습니다(원본이 바꾸면 이 둘이 **조용히** 안 됩니다): 장면 key는 장면 요소 `id`(`d-<key>`, 나뉜 장면은 `-2`·`-t1` 꼬리)에서(`slideForSceneId`), 표·코드·다이어그램은 `figure.vbox`의 `data-kind`와 내용(코드 글자 포함·표 머리 칸·다이어그램 순서)에서(`visualRefForFigure`) 찾습니다. 같은 종류가 한 장면에 여럿이라 못 가리면 누를 수 없습니다.
  - 그림 설명 칸은 `figure.vbox[data-kind="image"] > figcaption.cap`(원본 `project-deck.js`의 `vboxHtml`)과 그림 `<img src>`를 읽습니다. 원본이 클래스 `cap`이나 그 구조를 바꾸면 캡션만 **조용히** 안 눌립니다.
  - 남는 것(이 포크에만 있는 파일): `assets/edit-loader.js`·`assets/deck-edit.js`·`assets/deck-edit.css`·`assets/doc-edit.js`(덱이 `PFDocEdit.mapBlocksToLines`를 빌려 씀)·`assets/doc-edit.css`·`assets/portfolio-edit.css`·`edit-server.py`(`/__edit/deck`)·`portfolio-edits.py`(`save_deck_text`·`apply_deck_text`).

## 상세 문서 [± 바뀐 점] (이 포크 전용)

- 편집 서버로 연 상세 문서(`doc.html?p=<폴더>`) 오른쪽 아래 [± 바뀐 점] → 프로젝트 문서의 git 기준판(기본 `HEAD` = 커밋 안 한 변경)과 지금 글을 맨 위 블록 단위로 비교해 색칠합니다. 새 블록 초록, 지운 블록은 그 자리에 빨간 배경으로 끼움, 고친 문단·제목·목록은 낱말 단위 `<del>`/`<ins>`. 표·코드·다이어그램은 블록째 지움+추가. 위쪽 띠에 개수와 [다음 변경 ▼].
- 주소 옵션: `&diff-base=HEAD~1`(기준 바꾸기), `&pf-diff=1`(열자마자 켬). 켜면 편집 모드는 꺼지고, 끄면 새로고침 없이 원래 화면.
- 파일: `assets/doc-diff.js`·`assets/doc-diff.css`(로더 `edit-loader.js`의 doc 묶음), `edit-server.py`의 `GET /__edit/base?path=&base=`(`git show <base>:<경로>`, base는 영문·숫자·`~^._/-`만).

## 슬라이드 장면 수 (이 포크 전용)

- 슬라이드는 **30~40장 권장**이고, 필요하면 넘겨도 됩니다. `/portfolio-build`는 장면 수를 맞추려고 장면(두 번째 코드·보조 표·겹치는 화면)을 **빼지 않습니다.** 40장을 넘을 때만 리포트에 [참고]로 장면 수를 알리고, 줄일지는 사용자가 `/portfolio-edit`로 정합니다.
- `/portfolio-update`가 템플릿 원본으로 덮으면 다시 고칠 곳(원본은 15~30장·30장 초과 시 빼기): `.claude/skills/portfolio-build/SKILL.md` §6 분량·§7 `many-scenes` 행, `schema.md` 「분량」, `deck-edits.py`의 `n1 > 40` 안내, `.claude/skills/portfolio-edit/SKILL.md`의 "40장 넘으면 알림", `assets/project-deck.js`의 `all.length > 40` `many-scenes` 알림, `assets/deck-common.js` 주석, `format/01-portfolio.md`·`format/02-tech-document.md`·`README.md`의 숫자.
- 장 표지는 장면 목록이 두 단으로도 안 들어가면 **절 묶음 목록**으로 바꿉니다(2026-11-22, Hive Purge 3장 장면 57개). `sectionPath`의 깊은 칸부터 이웃 장면을 같은 절끼리 한 줄로 묶고(줄 = 절 제목 · 장면 수 · 그 절 첫 장면으로 이동), 들어가는 깊이를 씁니다. `/portfolio-update`가 덮으면 `assets/project-deck.js`의 `chapterScene`(rows 인자·두 번째 for)과 `sectionGroupRows`를 다시 넣습니다.
- 코드 한 장면 상한은 **24줄**입니다(원본 20줄, 2026-11-11 Hive Purge `MonsterAttack.Tick` 세 단계를 한 장에 띄우려고 바꿈). `/portfolio-update`가 덮으면 `assets/project-deck.js`의 `const MAXL = 24;`와 그 아래 `slideScenes` 위 주석을 다시 고칩니다. 스킬·`schema.md`·`.claude/rules/portfolio-writing.md` 규칙 3의 "20줄" 글은 아직 그대로입니다.

## 절 번호 참조 링크·숨김 (이 포크 전용)

- 프로젝트 문서 본문의 절 번호 참조(`(3.2)`·`(4.1~4.4)`·`(5.2-1)`·`3.7절`·`4.1에서`)와 파일:줄 참조(`` (`code/X.cs:44`) ``)는 md에 그대로 둡니다(2026-11-19 사용자 결정). 렌더러가 처리합니다.
  - 상세 문서(`doc.html`): 그 번호로 시작하는 제목의 slug 링크, `code/X.cs`는 `src/<폴더>/code/X.cs` 새 탭 링크. 제목에 없는 번호(버전 `17.3.0`·`0.25초`)와 코드 블록·제목 안은 그대로.
  - 슬라이드(`project.html`): 괄호째 참조를 숨김(장면 글·카드·캡션·표·코드 주석, 다이어그램 SVG 제외). 문장형("3.7에서 다룬다.")은 숨기지 않습니다.
- 코드 위치: `assets/md-blocks.js`의 `sectionNumbers`·`dropRefs`(공용), `assets/doc.js`의 `linkRefs`(renderDoc ③-2에서 부름), `assets/project-deck.js` 위쪽 `refNumbers`·`dropRefs`·`inline` 재정의와 `txt`·`codeHtml`·표 `outerHTML`의 `dropRefs`, `assets/deck-edit.js` 코드 그림 대조의 `dropRefs`.
- 원 레포를 머지할 때 위 네 파일의 이 부분이 충돌하거나 사라질 수 있습니다. 머지 뒤 `doc.html?p=<id>`에서 참조가 링크인지, `project.html?p=<id>&pf-audit=1` dump에 괄호 참조가 없는지 확인합니다.

## 명령 패널 (이 포크 전용)

- Claude 데스크톱 앱의 채팅 옆 패널 "포트폴리오 명령"(mod `portfolio-panel`). 이 레포를 연 세션에서 자동으로 뜨고, 닫았으면 `/portfolio-panel`로 다시 엽니다. README 「도구 한눈에」의 명령을 단계별 버튼으로 보여 주고, 맨 위에서 고른 프로젝트(`src/` 폴더, `_`로 시작하는 폴더 제외)를 명령 뒤에 붙입니다.
- 버튼 종류: **run**(`/portfolio-check`·`/portfolio-build` — 바로 실행) · **fill**(✋ 명령과 주소·스크린샷·요청 글이 필요한 명령 — 입력창에 채우기만, Enter는 사용자) · **say**(말 부탁 문장을 사용자 말로 보냄). ✋ 명령은 fill이므로 "사용자가 직접 입력했을 때만" 규칙이 그대로 지켜집니다.
- 파일: `tools/portfolio-panel/`(버튼 표는 `hooks/register.tsx` 맨 위 `BUTTONS` 배열만 고침) · `tools/.claude-plugin/marketplace.json`(로컬 마켓 `portfolio-tools`) · `.claude/settings.json`의 `enabledPlugins`. 설치된 플러그인은 레포 폴더를 그대로 읽으므로 고친 뒤 새 세션부터 반영됩니다.
- 새 PC에서 켜기: 앱에 딸린 `claude.exe`로 `claude plugin marketplace add ./tools` 한 번(사용자 설정에 마켓 등록). `enabledPlugins`는 레포에 있습니다.
- 확인: `claude plugin validate tools/portfolio-panel` · `claude plugin test tools/portfolio-panel`(버튼 run·fill·say가 각각 `command.run`·`prompt.fill`·`prompt.submit`으로 넘어가는지).
- `/portfolio-update`는 `tools/`·`.claude/settings.json`을 건드리지 않습니다.
- 「유지」 단계의 원 레포 받기 버튼은 `/portfolio-update`가 아니라 **git 머지**를 부탁하는 say 버튼입니다(2026-11-18). `/portfolio-update`는 루트 `CLAUDE.md`·`format/`·`portfolio-*` 스킬을 원본으로 덮어 keywords·장면 수 30~40·편집 모드 연결 같은 포크 수정을 지울 수 있어서입니다. 이 포크는 `git fetch upstream` 뒤 `git merge upstream/main`으로 받고, 머지 전에 바뀌는 점과 포크 규칙 충돌을 정리해 확인받습니다.

## 프로젝트별 슬라이드 설정

- WGO의 페이지 구성과 문구는 `src/4-wgo/deck-edits.json`에 둡니다. 다시 빌드한 뒤 `deck-edits.py apply`가 기존 기본 장면을 바탕으로 고정·숨김, 합친 장면의 옛 주소, 회고 표시를 `data/projects/4-wgo.json`에 반영합니다.
- 원문 참조와 지문을 먼저 확인하고, `presentation`·`visuals[].view`가 지정된 장면에 표시 변형을 적용합니다. `/portfolio-update`나 원본 머지로 덮일 수 있는 경로는 `assets/project-deck.js`의 배치·Mermaid 변형·회고·옛 주소 이동, `assets/deck-theme.css`의 대응 배치, `.claude/skills/portfolio-build/deck-edits.py`와 `schema.md`의 선택 필드 검증·보존입니다.
- `chapterOptions.<장 key>.hideSceneTabs`는 지정한 장 표지의 장면 제목 아래 탭 이름만 숨깁니다. 슬라이드 탭과 다른 장 표지의 표시는 별개입니다. `omitFromCells`는 지정 칸의 원문구를 화면에서 생략합니다. `columns` 배치는 모든 프로젝트에서 각 다이어그램을 같은 열의 제목과 설명 위에 놓습니다.
- 새 프로젝트는 같은 `deck-edits.json` 설정과 원문 참조를 사용합니다. 배치·노출·원문 범위에 대한 기대값은 그 프로젝트의 데이터에서 읽습니다. 기존 `node tools/check-wgo-deck.mjs` 명령도 공통 실행기를 사용합니다.

## 슬라이드 회귀 검사 (이 포크 전용)

다이어그램과 설명 카드를 좌우로 놓는 `visual-notes`·`side-notes` 배치는 카드 묶음의 세로 중앙을 다이어그램 중앙에 맞춥니다. 장면의 카드 내용이 달라져도 이 정렬을 유지하고, 공통 검사에서 실제 렌더링 위치를 확인합니다.
다이어그램·화면 여러 개가 각각 다른 제목·설명에 대응하는 `columns` 배치는 표처럼 동일한 열에 해당 세 항목을 두고, 모든 시각 자료·제목·설명 행의 시작 위치를 맞춥니다. 공통 검사는 원문 순서와 각 행의 렌더링 정렬을 확인합니다.
한 기능의 전후 이미지 두 장과 다른 기능의 이미지 한 장을 보여 주는 `feature-rows` 배치는 기능 설명을 왼쪽 열, 대응 이미지를 오른쪽 열에 두 행으로 맞춥니다. 위쪽 두 이미지의 표시 높이를 같게 하고, 원문 이미지 순서와 설명 대응을 공통 검사로 확인합니다.
시각 자료 두 개와 표 하나를 함께 보여 주는 `visual-pair-table` 배치는 시각 자료를 위쪽 좌우에 같은 높이로 놓고 표를 아래쪽 전체 폭의 중앙에 둡니다. 공통 검사는 두 자료의 행 정렬, 겹침, 표의 위치와 중앙 정렬을 확인합니다.

### 코드와 설명의 대응 검수

- 모든 프로젝트에서 코드 동작을 설명하는 슬라이드를 **완료 전에 반드시 검수**합니다. `node tools/check-deck.mjs <프로젝트 폴더> --output <경로>.json`의 `codeReview`에서 번호 강조가 없는 발췌와 이어지는 장면까지 확인합니다. 전체 장면의 제목과 설명도 훑어, 구현을 설명하지만 코드가 빠져 목록에 없을 수 있는 장면을 포함합니다.
- 제목·도입·카드가 주장하는 핵심 동작마다 화면에 보이는 실행 줄을 찾습니다. 조건 분기, 호출, 상태 변경, 예외 처리 등 설명의 결론을 뒷받침하는 줄을 확인합니다. 번호가 있으면 같은 번호가 붙은 범위에서 확인합니다. 앞뒤 장면에 나뉜 근거는 장면 번호도 함께 기록합니다.
- 코드가 없거나 범위가 잘렸거나 설명과 동작이 다르면 **필수 수정**으로 처리합니다. 원문에서 실제 구현을 찾아 발췌 범위를 고치거나 설명을 표시 코드에 맞춰 수정하고 다시 대조합니다. 예를 들어 설명에 “원격 플레이어 입력을 끄고 물리 바디를 Kinematic으로 변경”이라고 쓰면 두 설정을 모두 볼 수 있어야 합니다.
- 검수한 에이전트는 보고서에 장면 ID·설명 항목·근거 줄(표시 범위 또는 인용)·판정을 남깁니다. 의미 대응 검수가 빠졌거나 근거가 확인되지 않은 장면이 있으면 완료로 보고하지 않습니다. 공통 검사기는 화면의 설명·발췌·강조를 수집하고 코드 누락과 강조 구조를 검사합니다. 의미 대응 판정은 검수자가 내립니다.
- 여러 카드로 실행 코드를 설명하는 장면에는 `codeEvidence: "all-points"`를 지정하고 각 카드 번호를 실제 실행 줄의 `view.highlights` 번호와 연결합니다. 분할 코드 장면은 이어지는 장면까지 합쳐 모든 번호를 확인합니다. 공통 검사는 번호 누락·잘못된 범위·화면 표시를 실패로 처리하며, 설명의 뜻과 실행 동작은 사람이 별도 검수합니다. 코드 밖 그림만이 근거인 카드는 같은 번호의 코드 설명으로 바꾸거나 코드와 그림 설명을 별도 장면으로 나눕니다.

### 공통 실행과 배치 검사

- `/portfolio-build`·`/portfolio-edit`의 데이터 반영과 `PFScene.report()` 미리보기 단계에서, 로컬 미리보기 서버를 켜고 `node tools/check-deck.mjs <프로젝트 폴더>`를 실행합니다. 공용 슬라이드 렌더러·CSS·원문 해석기를 바꿨거나 원본 업데이트·머지를 했으면 `node tools/check-deck.mjs --all`로 현재 `data/projects/*.json`의 프로젝트를 검사합니다. 기본 주소는 `http://127.0.0.1:4600`이며 필요하면 `--url`로 로컬 포트를 지정합니다.
- 공통 검사는 렌더링 오류·잘림에 더해 장 목차의 항목·그룹 수, 배치·정렬, Mermaid, 표 칸 변경·줄바꿈, 코드 발췌·강조, 해결 배지, 병합 전 주소, 회고 표시를 확인합니다. 콘솔 오류 또는 필수 검사 실패는 종료 코드 1입니다.
- 내용이 적은 장면은 미리보기의 `low-fill`을 보고 원문에서 보강할 설명·도표·사례를 찾습니다. 보강 근거가 없으면 메시지가 이어지는 장면을 묶을 수 있는지 확인합니다. 묶을 때 문제 단위와 설명 순서, 기존 링크 목적지를 보존합니다. `tiny-diagram`·`long-code`도 가독성이나 설명 근거 누락이 있는지 판단해 기록합니다.
- 다이어그램과 설명은 각 그림의 대상·단계·데이터가 대응 카드와 같은지 원문과 비교합니다. 비교표·성과의 수치, 단위, 비교 조건, 측정 한계와 적용 여부도 확인합니다. 사용자 피드백에는 장면 ID, 재현 조건, 기대 동작, 판정 근거를 남깁니다. 페이지 번호는 최종 보고서에서 확인합니다.
- 실패 재현은 프로젝트마다 실제 존재하는 요소에 적용합니다. `--probe swapped-layout`은 그림·카드 순서를 바꾸고, `missing-highlight`는 코드 번호를 지우며, `missing-code`와 `wrong-code-text`는 발췌를 제거하거나 변조합니다. `wrong-retro-pair`는 회고의 개선 계획 셀을 변경합니다. 변경은 브라우저에서만 일어나고 `--output <경로>.json`에 실패 근거를 기록합니다.
- PDF 납품 시 `node tools/check-deck.mjs <프로젝트 폴더> --print --output <경로>.json`으로 인쇄 배치와 링크의 기대 페이지를 기록합니다. 같은 미리보기에서 PDF를 새로 저장할 때는 `--pdf <새 파일>.pdf`를 사용할 수 있습니다. PDF 파일을 정한 뒤 `python tools/check-deck-pdf.py <PDF 파일> <검사 보고서>.json <프로젝트 폴더>`로 실제 페이지 크기·수와 내부 링크 목적지를 검증하고 수정한 페이지를 화면으로 봅니다. PDF 검사에는 `pypdf`가 필요합니다. 인쇄 측정의 1px 이하 경계 초과는 보고서에 남겨 실제 PDF에서 확인합니다.

### 회고의 근거와 대응 검수

- 원문 회고에서 실제 관찰·측정·확인하지 못한 결과, 문제와 원인, 앞으로 할 일을 구분합니다. 수치와 예시는 출처가 있는 경우에만 쓰고, 이미 적용한 해결과 다음에 적용할 계획을 분명히 구별합니다.
- 각 아쉬운 점과 개선 계획은 같은 문제를 다뤄야 합니다. 계획이 동작이나 정책을 둘 이상 나누면 각각의 역할과 적용 사례를 원문에 맞게 보여 줍니다. `retroReview`의 줄과 원문 회고 절을 대조하고, 확인한 근거와 남은 불확실성을 보고서에 적습니다.
- 회고 화면은 `retro.hideAchieved`를 선택한 프로젝트에서만 성과 열을 숨깁니다. 공통 검사는 실제 표시 행과 순서, 개선 계획의 문구, 성과 표시 설정을 프로젝트 데이터와 대조합니다. 의미 대응과 근거 검수는 생성·편집 완료 전에 에이전트가 수행합니다.

## Engineering Editorial 배치 (이 포크 전용)

- 2026-11-22 디자인 핸드오프(흰 바탕 Engineering Editorial)를 적용했습니다. 색·모양·영문 글꼴(Sora)은 `theme/theme.css`에만 있어 업데이트·머지와 상관없습니다. 아래 렌더러 수정만 원본이 덮거나 머지 충돌이 날 수 있습니다(줄마다 `[포크]` 주석).
  - **2026-11-25 B안 카드형 배치로 교체**(사용자가 고른 시안 패키지 `B_layout_agent_package.zip`). 아래 예전 [포크] 블록(68:32 Hero·Engineering Editorial·Indexed·타임라인·Hive 3열)은 모두 지웠습니다.
    - `assets/portfolio.js` `render()`·`card()`·`detail()`·`linkRows()`·`cardFacts()`: nav → 머리(인사·자기소개 | 개발 분야 상자) → 카드 3열(`#projects`) → 상세 패널(`#p-<폴더>`, 기능마다 그림·설명·[슬라이드 열기]) → 기술 스택(`#stack`) → 연락처. 프로젝트는 `data/portfolio.json`의 `projects` 순서. 한눈에 보기·자기소개 절 없음(예전 주소 `#intro`·`#glance`·`#p-<폴더>-core`는 `legacyTarget`이 넘김).
    - `assets/portfolio.css`: 파일 전체가 B안 값(`--b-*`: #F8FAFC·#17628B·#D7E2E9, 1150px, 800px 경계). `theme/theme.css` 색은 이 페이지에 안 쓰고 슬라이드·상세 문서에만 들어갑니다.
    - `assets/strings.js`: `page.brand`·`en.hero`·`page.hello`·`page.hello-name`·`en.focus`·`page.focus-count`·`en.projects`·`page.games`·`en.details`·`page.details`·`en.capabilities`·`page.view`·`page.card-*` 키 추가.
    - `assets/portfolio-edit.js`: 글 칸 선택자를 새 DOM(`#cover h1 .nm`·`.card .title/.role/.intent`·`.detail h3/.d-role/.tagline/.meta/.tags`·`.features li h4/.what`)으로.
  - `assets/doc.css`: `--measure: 44rem`(원본 40rem), `--fig-max: 1120px`(원본 1200px).
  - `index.html`: JetBrains Mono `latin-400` 링크 한 줄. `doc.html`: JetBrains Mono·D2Coding 링크 두 줄(원본은 `--mono` 첫 순위가 JetBrains Mono인데 글꼴을 싣지 않았음).
- 슬라이드(`project.html`·`deck-theme.css`)는 고치지 않았습니다. 장면마다 글자 폭을 재서 배치하므로 코드 글꼴·크기를 바꾸면 배치 점검이 달라집니다.
- 되돌리기: 색은 `/portfolio-theme 되돌리기`, 배치는 위 `[포크]` 줄을 원본 값으로(또는 그 커밋 `git revert`).
- 원 레포 머지 뒤 확인: `index.html`이 B안 카드 3열·상세 패널 구조인지, `doc.html?p=<id>` 본문 폭이 약 704px인지.
