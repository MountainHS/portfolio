# 템플릿 핵심 규칙 (루트 CLAUDE.md 요약본)

> 루트 `CLAUDE.md`(템플릿 원본, `/portfolio-update`가 덮어씀)는 `.claude/settings.json`의 `claudeMdExcludes`로 시작 문맥에서 뺐습니다(2026-11-26, 약 9k → 3k 토큰). 이 파일이 그 요약입니다. 기준 원본: 커밋 `ace61c8`(2026-11-18)의 루트 `CLAUDE.md`.
> 세부(폴더 규약 전문·데이터 흐름 그림·명령 상세·`format/` 표)가 필요하면 루트 `CLAUDE.md`를 직접 Read합니다. 「미리보기」 절은 `.claude/rules/template-preview.md`(렌더러·데이터·테마 파일을 다룰 때 자동 로드)에 그대로 있습니다.

## 이 레포

- 사용자의 원천 소스(프로젝트 문서·스크린샷·코드)로 포트폴리오 웹페이지를 만드는 템플릿입니다. 기준은 「유니티 개발자 취업·기술 문서 작성 가이드 v2」, 판정 기준 압축본은 `format/`(읽기 전용).
- 이름: **프로젝트 문서**(예전 '기술 문서') · **포트폴리오 페이지**(예전 '포트폴리오 문서') · **슬라이드**(예전 '발표 덱'). 예전 이름도 알아듣고 답은 지금 이름으로. 파일·코드 이름(`deck`, `deck-edits.json`, `project.html`)은 그대로.

## 폴더 (핵심)

- `profile.md` ✍ 이름·연락처·자기소개·페이지 설정 · `src/<NN-프로젝트>/` ✍ 프로젝트 문서 md 하나 + `images/` + (선택) `code/` + `deck-edits.json`(/portfolio-edit, 소스) + `_intake/`(재료, git 제외, build가 안 읽음). `_`로 시작하는 폴더는 무시.
- `data/` 🤖 build 생성 · `index.html`(포트폴리오 페이지, A4 인쇄 = 제출 PDF) · `project.html?p=<폴더>`(슬라이드) · `doc.html?p=<폴더>`(상세 문서, md 직접 읽음) · `assets/` 렌더러.
- `theme/theme.css`(/portfolio-theme) · `theme/strings.tsv`(문구 표, 사용자가 맨 끝 "내 문구" 칸을 고침) · `theme/layout.json`(/portfolio-layout, /portfolio-custom만 읽음) · `custom/` · `report.md`(git 제외).
- `docs/` 사람이 읽는 문서. 전체 흐름 질문은 `docs/2-workflow.md`, 명령 사용법은 `docs/tools/`. `docs/`가 없으면 "`/portfolio-update`를 한 번 더 입력하면 들어옵니다"라고 한 번 알림.

## 명령

| 명령 | 하는 일 |
|---|---|
| `/portfolio-build [폴더]` | 소스 → `data/` + 점검 리포트 + 미리보기 |
| `/portfolio-check [폴더]` | 프로젝트 문서 점검만(재료 점검표 포함), data 안 건드림 |
| `/portfolio-edit [폴더] [장면] [요청]` | 슬라이드 장면 배치를 `deck-edits.json`에. **장면 카드로 확인받은 뒤에만** 씀. 내용 문제는 프로젝트 문서로 |
| `/portfolio-theme` · `/portfolio-layout` | 스크린샷 → `theme/theme.css` · 레퍼런스 배치 → `theme/layout.json`(확인받고) |
| `/portfolio-custom` · `/portfolio-update` · `/portfolio-intake` | **사용자가 직접 입력했을 때만** 실행 |

## 반드시 지킬 원칙

1. **지어내지 않는다.** `data/`의 모든 값은 `profile.md`·프로젝트 문서·코드에서 찾을 수 있는 사실. 없으면 비우고 리포트에 적음.
2. **사용자 문서를 대신 고쳐 쓰지 않는다.** `src/`·`profile.md`는 사용자 것. 리포트로 문제와 방향만, "고쳐 줘"라고 한 부분만 수정.
3. **Claude가 요약한 문장은 표시한다.** 압축한 문장은 리포트 "확인해 주세요"에.
4. **고칠 곳은 소스다.** 페이지 내용이 틀리면 `data/` 대신 `src/`·`profile.md`를 고치고 다시 빌드. 장면 배치만 `/portfolio-edit` → `deck-edits.json`.
5. **렌더러(`index.html`·`project.html`·`doc.html`·`assets/`)는 요청할 때만 수정.** 디자인 요청은 ① `profile.md` 강조색 ② `theme/strings.tsv` ③ `/portfolio-theme` ④ `/portfolio-custom`(필요하면 먼저 `/portfolio-layout`) 순. 문구 표를 "고쳐 줘"하면 그 줄 맨 끝 "내 문구" 칸만(`{name.deck}`·`{n}` 자리표시 유지).
6. **경로는 상대경로, 대소문자 정확히**(GitHub Pages는 대소문자 구분).

## 그 밖에

- 샘플 프로젝트(`00-example-crafting` 등)는 동의받은 선배 작품. 내용을 사용자 문서에 옮기지 않음.
- 다이어그램을 직접 요청받으면 `format/03-diagrams.md` 표로 종류를 고르고 실제 코드 이름으로 그림. 문서에 넣는 것은 "넣어 줘"할 때만.
- 판단 기준: `format/01-portfolio.md`(페이지 체크리스트) · `02-tech-document.md`(5단 구조) · `03-diagrams.md` · `04`·`05`(커스텀) · `06-deploy.md` · `07-materials.md`(재료 점검표).
- 미리보기는 `file://` 불가, `python edit-server.py`(포트 4600). 점검 절차 상세는 `template-preview.md`.
