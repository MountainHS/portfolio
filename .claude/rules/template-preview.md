---
paths:
  - "assets/**"
  - "*.html"
  - "theme/**"
  - "data/**"
  - "edit-server.py"
  - "preview.ps1"
  - "tools/check-*"
---

> 루트 `CLAUDE.md` 「미리보기」 절을 그대로 옮긴 것입니다(시작 문맥에서 루트 파일을 뺐기 때문, 2026-11-26). 원본이 바뀌면 `.claude/CLAUDE.md` 「루트 CLAUDE.md 요약본」 규칙대로 맞춥니다.

## 미리보기

`fetch()`로 데이터를 읽으므로 파일 더블클릭(`file://`)으로는 열리지 않습니다. 로컬 서버로 엽니다.

- `python edit-server.py` (Windows에서 `python`이 없으면 `py edit-server.py`) → http://127.0.0.1:4600 — 로컬 미리보기는 늘 이 서버입니다(정적 파일 + 상세 문서 편집 모드). `python -m http.server`로 열면 편집 모드가 꺼집니다
- JetBrains IDE(Rider·WebStorm): `index.html` 우클릭 → **Open in → Browser** (내장 서버)
- Claude 데스크톱 앱: `.claude/launch.json`의 `portfolio` 구성으로 미리보기
- **상세 문서 편집 모드**: `python edit-server.py`(포트 4600, launch.json의 `portfolio`)를 띄우고 http://127.0.0.1:4600 으로 열면 상세 문서(`doc.html`) 오른쪽 아래에 [✎ 편집 모드]가 생깁니다.
  블록(문단·제목·목록·표·코드)을 누르면 그 줄의 md 원문 입력창이 열리고, [저장]하면 `edit-server.py`가 `src/<폴더>/` 프로젝트 문서의 그 줄만 바꿔 씁니다(줄바꿈 방식 유지).
  주소가 localhost가 아니거나 `/__edit/ping`이 없으면(GitHub Pages·`http.server`) 켜지지 않습니다. 슬라이드·포트폴리오 페이지 내용은 고친 뒤 `/portfolio-build`를 다시 실행해야 맞춰집니다.
- 슬라이드 아래 띠의 빨간 숫자(미리보기 배치 점검)를 물으면 데이터를 다시 만들지 않고, 그 슬라이드(`project.html?p=<id>`)에서
  `await PFScene.whenReady()` 뒤 `PFScene.report().audit`을 읽어 `.claude/skills/portfolio-build/SKILL.md` §7-1처럼 장면마다 1건으로
  묶어 알려 줍니다. 브라우저 도구가 없으면 headless Chrome으로
  `chrome --headless=new --virtual-time-budget=30000 --dump-dom "http://localhost:4600/project.html?p=<id>&pf-audit=1"`
  을 실행해 `#pf-audit`의 base64 JSON(= `report()`)을 읽고, `ready`가 `yes`인지 확인합니다.
  포트폴리오 페이지(`index.html`)는 장면이 없는 흐르는 페이지라 빨간 띠가 없고, `await PFPage.whenReady()` 뒤 `PFPage.report()`
  (깨진 이미지 `images.broken`·가로 넘침 `overflowX`·`errors`)를 봅니다. headless는 `index.html?pf-audit=1`로 같은 방법입니다.
  상세 문서(`doc.html?p=<id>`)는 `await PFDoc.whenReady()` 뒤 `PFDoc.report()`(`errors`·깨진 이미지 `images.broken`·그리지 못한 다이어그램 `diagrams.failed`)를 봅니다. headless는 `doc.html?p=<id>&pf-audit=1`로 같은 방법이고(그림까지 받은 뒤 씀), 상세 문서의 `ready`는 `true`입니다.
  슬라이드의 빨간 띠(원문 블록을 찾지 못함·여러 곳·바뀜)도 `audit`에 함께 나오고, `info.mode`가 `auto`면 예전 데이터로 자동 구성된 슬라이드이므로 `/portfolio-build`를 다시 실행하도록 안내합니다.
- 슬라이드의 **[PPTX로 저장]**(`assets/deck-pptx.js`)은 렌더러 기능이라 데이터를 다시 만들지 않습니다. 결과가 이상하다고 하면 그 슬라이드에서 `await PFScene.whenReady()` 뒤
  `(await PFPptx.build()).report`(내려받지 않음)의 `warnings`(넣지 못한 그림 등)·`slides`(슬라이드별 글상자·도형·그림·표·코드 개수)·`fonts`(들어간 글꼴 이름)를 봅니다.
  PowerPoint에서 글꼴·줄바꿈이 다르면 대개 그 PC에 Pretendard·D2Coding이 없는 것입니다(설치 또는 [홈 → 바꾸기 → 글꼴 바꾸기]). PPTX에서 고친 것은 소스로 돌아오지 않으므로 내용 문제는 프로젝트 문서로 안내합니다.
- 문구 표(`theme/strings.tsv`)가 제대로 읽혔는지는 세 페이지 report의 `strings`로 봅니다 — 포트폴리오 페이지 `PFPage.report().strings`, 슬라이드 `PFScene.report().info.strings`,
  상세 문서 `PFDoc.report().strings`. `issues`의 `type`: `spaces`(탭 대신 공백 — 그 줄은 기본 문구) · `unknown-key` · `duplicate` · `default-edited`(기본 문구 칸을 고침) ·
  `extra-cols` · `too-long`(권장 글자 수 초과) · `hangul-in-en`(영문 라벨 칸에 한글) · `missing-var`/`unknown-var`/`unknown-ref`/`ref-loop`(자리표시) · `encoding`(CP949·UTF-16) · `no-file`.
  `line`이 그 표의 줄 번호입니다. 줄마다 무엇을 고치면 되는지 `detail`대로 알려 줍니다.
