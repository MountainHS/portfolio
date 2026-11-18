# 도구 한눈에

도구는 세 종류입니다. **명령**은 레포 폴더에서 Claude Code를 열고 입력하고(목록에 안 보이면 Claude Code를 새로 시작), **버튼**은 페이지에 있으며, **파일**은 직접 고칩니다.
✋ 표시가 있는 명령은 **직접 입력했을 때만** 실행되고, 나머지는 "포트폴리오 만들어 줘"처럼 말로 부탁해도 됩니다. 도구 이름을 누르면 설명으로 갑니다.

| 단계 | 하고 싶은 일 | 도구 | 바뀌는 것 |
|---|---|---|---|
| 재료 | GitHub 레포에서 프로젝트 문서 재료 모으기 | ✋ [`/portfolio-intake <레포 주소>`](intake.md#github-레포에서-가져오기) 명령 | `src/<폴더>/_intake/`·`code/` |
| | PowerPoint 발표 자료(PPTX) 가져오기 | ✋ [`/portfolio-intake <발표.pptx>`](intake.md#발표-자료pptx에서-가져오기) 명령 | `src/<폴더>/_intake/발표자료.md`·`pptx/` |
| 쓰기 | 쓰는 중에 가이드 기준으로 점검받기 | [`/portfolio-check`](check.md#쓰는-중에-점검받기) 명령 | `report.md` |
| 만들기 | 세 페이지의 데이터 만들기·다시 만들기 | [`/portfolio-build`](build.md#포트폴리오-데이터-만들기) 명령 | `data/`, `report.md` |
| | 만든 페이지 열어 보기 | ["미리보기 띄워줘"](build.md#미리보기) 또는 로컬 서버 | — |
| 다듬기 | 슬라이드 장면 하나만 바꾸기 | [`/portfolio-edit`](edit.md#슬라이드-장면-고치기) 명령 | `src/<폴더>/deck-edits.json`, `data/projects/<폴더>.json` |
| | 강조색 바꾸기 | [`profile.md`의 강조색](build.md#profilemd-채우기) 파일 → 다시 만들기 | `profile.md` |
| | 절 이름·버튼 문구 바꾸기 | [`theme/strings.tsv`](design.md#문구-바꾸기) 파일 | 그 파일 (다시 만들 필요 없음) |
| | 다른 사이트 느낌으로(색·모양·영문 글꼴) | [`/portfolio-theme`](design.md#다른-사이트-느낌으로-바꾸기) 명령 | `theme/theme.css` |
| | 다른 사이트의 배치(단 나눔·카드 격자) 따라 하기 | [`/portfolio-layout`](design.md#다른-사이트-배치-따라-하기) 명령 → ✋ `/portfolio-custom` | `theme/layout.json` → `custom/index.html` |
| | 배치까지 새 디자인 | ✋ [`/portfolio-custom`](design.md#나만의-디자인-새로-만들기) 명령 | `custom/index.html` |
| 내보내기 | 인터넷에 공개 | [commit · push + GitHub Pages](export.md#배포하기) | — |
| | 제출용 PDF(A4) | 포트폴리오 페이지의 [\[PDF로 저장\]](export.md#pdf로-제출하기) 버튼 | PDF 파일 |
| | 발표 자료 PDF · PowerPoint | 슬라이드의 [\[PDF로 저장\]](export.md#pdf로-제출하기) · [\[PPTX로 저장\]](export.md#슬라이드를-pptx로-저장하기) 버튼 | PDF · `.pptx` 파일 |
| 유지 | 템플릿의 새 기능 받기 | ✋ [`/portfolio-update`](update.md#템플릿-업데이트-받기) 명령 | 렌더러·명령·기준 + 업데이트 커밋 1개 |

말로 부탁하면 되는 것도 있습니다.

- "미리보기 띄워줘"
- "미리보기 배치 점검 결과 알려 줘" — 슬라이드 아래 띠의 빨간 숫자가 무엇인지 장면마다 알려 줍니다
- "문구 표 점검해 줘" — `theme/strings.tsv`에서 칸이 어긋났거나 너무 긴 줄을 줄 번호와 함께 알려 줍니다
- "충돌 해결 도와줘" — 템플릿을 받다가 git 충돌이 났을 때

도구 문서는 모두 같은 표로 시작합니다 — **명령(또는 방법) · 언제 쓰나 · 바뀌는 것 · 되돌리기**.

명령마다의 자세한 설명: [재료 모으기](intake.md) · [점검받기](check.md) · [페이지 만들기](build.md) · [장면 고치기](edit.md) · [디자인 바꾸기](design.md) · [내보내기](export.md) · [템플릿 업데이트](update.md)
