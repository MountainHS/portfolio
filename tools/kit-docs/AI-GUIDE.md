# AI-GUIDE — 기술문서 고민 정리 키트 (AI용)

> 읽는 쪽: 이 키트를 받은 사용자의 AI 코딩 에이전트(Claude Code 기준).
> 사람용 소개는 [README.md](README.md)입니다. 사용자에게 설명할 때는 README의 말을 쓰고, 이 파일의 경로·인자·규칙 세부는 사용자에게 늘어놓지 않습니다.
> 이 키트는 한 사용자가 Unity 포트폴리오 템플릿 레포에서 2026-11-09~11-27에 실제로 쓴 절차와 도구를 묶은 것입니다. `examples/`는 그때의 실제 파일입니다(절대경로·이메일·팀원 이름을 가렸습니다).

## 1. 이 키트가 하는 일

포트폴리오 기술문서(이하 **프로젝트 문서**)를 쓰기 전에, 사용자의 원천 자료(회고·노트·일정 도구 내보내기·발표 PDF·일지)에서 **고민·판단·시행착오**를 찾아 분류하고, 사용자 답으로 구체화한 뒤 문서에 옮깁니다. 목표는 정보 손실을 막는 것입니다. 최종 구현보다 그 구현에 이른 사고 과정(처음 판단, 버린 대안, 실패, 판단 변화)을 원천에서 복원합니다.

중심은 스킬 `/tech-doc-issue-summary`입니다. 모드가 여섯 개입니다.

| 모드 | 언제 | 읽을 파일(스킬 폴더 기준) |
|---|---|---|
| 준비 | 문서 초안도 정해진 원천 폴더도 없음 | `references/before-classification.md` |
| 시작(1~4단계) | 분류 파일이 없음 | `SKILL.md` 1~4단계, `references/levels.md`, `references/file-template.md` |
| 답 반영(5단계) | 분류 파일에 탭 들여쓴 답 줄이 있음 | `references/questions-and-dialogue.md` §2 |
| 대화 세션(6단계) | 기억이 흐린 질문 하나를 코드와 함께 풂 | `references/questions-and-dialogue.md` §3 |
| 프로젝트 문서 반영(7단계) | 사용자가 "반영해 줘" | `references/to-tech-doc.md` |
| 퇴고·피드백 | 반영 뒤 새 초안 섞기·흐름 손보기·외부 피드백 | `references/feedback-and-revision.md` |

## 2. 키트 파일 지도

| 키트 안 경로 | 역할 | 이식 위치 |
|---|---|---|
| `README.md` | 사람용 소개 | 옮기지 않음(키트 폴더에 둠) |
| `AI-GUIDE.md` | 이 파일 | 옮기지 않음 |
| `고민-레벨과-구체화-예시.md` | 사람용 보충: L1·L2·L3 설명, 정리 파일 표시 읽는 법, WGO D-009가 원천 한 줄에서 기술문서 절이 되기까지의 실제 기록. 사용자가 레벨이나 진행 방식을 물으면 이 파일을 가리킴 | 옮기지 않음 |
| `.claude/skills/tech-doc-issue-summary/SKILL.md` | 스킬 본체(모드 판정·단계) | 대상 레포 `.claude/skills/tech-doc-issue-summary/` |
| `.claude/skills/tech-doc-issue-summary/references/*.md` | 단계별 상세(Level 정의, 파일 골격, 질문·답 반영·대화, 문서 반영, 준비, 퇴고·피드백, 최초 지시문 원문) | 같은 폴더 |
| `.claude/skills/tech-doc-issue-summary/templates/*` | 빈 견본: 분류 파일, `INDEX.md`, 별칭표, 대화 파일, 피드백 계획, 답변 초안(O/X), 노트 대조표, 원천 소스 프롬프트 | 같은 폴더 |
| `.claude/skills/tech-doc-issue-summary/scripts/check_quotes.py` | 대응표 발췌가 원천 줄(±2)에 있는지 검사 | 같은 폴더 |
| `.claude/skills/tech-doc-issue-summary/scripts/make_alias.py` | 원천 폴더 → 별칭표(`별칭<TAB>절대경로`) 초안, md5 중복 알림 | 같은 폴더 |
| `.claude/skills/tech-doc-issue-summary/scripts/gomin_status.py` | 분류 파일의 남은 질문·보류·답 줄·ID 중복과 §1↔본문 번호 불일치 | 같은 폴더 |
| `.claude/rules/portfolio-writing.md` | 문서 글쓰기 지침(독자·화자·합니다체·"A가 아니라 B" 금지·장 순서)과 시각화 규칙 0~7 | `.claude/rules/` |
| `.claude/rules/report-structure.md` | report 운영 규칙(메인 report·작업 파일·질문 한 곳·✍ 할 일) | `.claude/rules/` |
| `tools/voice-lint.py` | 프로젝트 문서의 보고투·한다체·제3자 시점·커밋 해시 후보 찾기, `--hook` 모드 | 대상 레포 `tools/` |
| `tools/report-toc.py` | 메인 report 목차·✍ 할 일 상자 자동 생성, `--split`·`--archive`·`--check` | 대상 레포 `tools/` |
| `settings-hooks.json` | voice-lint PostToolUse 훅 조각 | 대상 `.claude/settings.json`의 `hooks`에 합침 |
| `examples/고민-분류/` | 실제 분류 결과: `INDEX.md`, `WGO.md`(4차 반영본, 본보기), `5-hive-purge.md`, `WGO-Q22-대화.md`, `WGO-협업.md` | 옮기지 않음. 형식이 헷갈릴 때만 Grep으로 필요한 절만 읽음 |
| `examples/준비/` | 확인 필요 답변 초안(O/X), 노트↔문서 대조 결과 | 옮기지 않음 |
| `examples/퇴고-피드백/` | 외부 피드백 반영 계획(14차), 개편안 섞기 기록, 장 연결성 개선 계획 | 옮기지 않음 |

`examples/`의 분류 파일은 1800줄 안팎입니다. 통째로 읽지 말고 `Grep "^## |^### "`로 절을 찾은 뒤 그 절만 읽습니다.

## 3. 이식 절차

### 3-1. 사용자에게 먼저 물을 것 (한 번에)

1. 프로젝트 문서 위치. 이 키트는 `src/<프로젝트 폴더>/<문서>.md` 구조를 가정합니다. 다르면 실제 경로를 받습니다.
2. 원천 자료가 있는 폴더(프로젝트마다). 읽어도 되는 범위와 안 되는 범위(코드 레포·Git 이력·블로그는 기본적으로 허락 뒤에만).
3. 프로젝트마다 쓸 ID 접두어(예: `WGO`, `HP`). 정하지 않으면 폴더 이름에서 제안합니다.
4. 문서 말투(합니다체 / 한다체). 키트 기본은 합니다체입니다.
5. 사용자를 부르는 말(키트 기본은 "사용자").
6. report를 공개 레포에 올려도 되는지. 기본은 `report/`를 `.gitignore`에 넣습니다(사용자 답 원문·날짜·출처가 남음).

### 3-2. 대상 레포 전제 확인

```bash
git rev-parse --show-toplevel; git status -s | head; ls src report .claude 2>/dev/null
```

- `src/<폴더>/`가 없으면 사용자에게 문서 위치를 확인하고, 스킬 `SKILL.md`의 `paths`와 `tools/voice-lint.py`의 `is_project_document()`를 그 구조에 맞게 고칩니다.
- `report/`가 없으면 만들고 `.gitignore`에 `report/`를 넣을지 3-1의 답대로 합니다.
- 대상 레포가 이 포트폴리오 템플릿(`format/`·`/portfolio-build`·`/portfolio-check`가 있음)이 아니면, 스킬에 나오는 빌드·슬라이드 단계(`/portfolio-build`, `deck-edits.py`, `portfolio-edits.py`, `/portfolio-edit`, `format/03-diagrams.md`)는 건너뛰고 문서 반영까지만 합니다.

### 3-3. 복사

키트 폴더를 `<키트>`라 할 때:

```bash
mkdir -p .claude/skills .claude/rules tools report/고민-분류
cp -r <키트>/.claude/skills/tech-doc-issue-summary .claude/skills/
cp <키트>/.claude/rules/portfolio-writing.md <키트>/.claude/rules/report-structure.md .claude/rules/
cp <키트>/tools/voice-lint.py <키트>/tools/report-toc.py tools/
```

같은 이름의 파일이 이미 있으면 덮지 말고 사용자에게 diff를 보여 주고 묻습니다.

### 3-4. 바꿀 곳

| 파일 | 바꿀 것 |
|---|---|
| `SKILL.md`·`references/*.md` | 본보기 이름(`WGO.md`·`HP-D-…`·"WGO에서 정한 것")은 **그대로 둡니다**. 규칙이 생긴 실제 사례라서 지우면 근거가 사라집니다. 대신 `SKILL.md` 맨 위 "본보기 결과물" 줄 뒤에 "본보기 파일은 이 레포에 없고 `<키트>/examples/고민-분류/`에 있다"를 한 줄 넣습니다. |
| `SKILL.md` `paths` | 문서 위치가 `src/**`가 아니면 실제 경로로 |
| `references/to-tech-doc.md` | 문체 줄("WGO는 ~다 평서체")을 3-1의 답으로. 빌드 단계는 3-2 판단대로 |
| `.claude/rules/portfolio-writing.md` | 말투(합니다체) 줄을 3-1의 답으로. 이 파일 안 실제 사례(Hive Purge 표 등)는 그대로 둠 |
| `.claude/rules/report-structure.md` | 사용자 호칭, `report/<폴더>` 예시 이름 |
| `tools/voice-lint.py` | `is_project_document()`의 문서 위치, 말투 규칙(한다체 검사는 합니다체일 때만 의미 있음) |
| 루트 지시 파일(`CLAUDE.md` 등) | 아래 「반드시 지킬 원칙」 6줄이 없으면 추가 |

### 3-5. 훅 등록

`<키트>/settings-hooks.json`의 `PostToolUse` 항목을 대상 `.claude/settings.json`의 `hooks`에 합칩니다(기존 훅을 지우지 않음). 훅은 프로젝트 문서를 Edit·Write할 때 새로 넣은 글만 검사하고, 걸린 것이 없으면 아무것도 출력하지 않습니다. 사용자 승인 뒤에 등록합니다.

### 3-6. 이식 확인

```bash
python .claude/skills/tech-doc-issue-summary/scripts/gomin_status.py <키트>/examples/고민-분류/WGO.md
#   → 고민 29개, 남은 질문 0개 · 보류 4개, 종료 코드 0
python .claude/skills/tech-doc-issue-summary/scripts/make_alias.py <원천 폴더 하나> --td <문서 하나>
#   → 별칭<TAB>경로 줄이 나옴
python tools/voice-lint.py <프로젝트 문서 하나>
python tools/report-toc.py --check report/*.md   # report가 아직 없으면 건너뜀
```

`check_quotes.py`는 원천 파일이 있어야 하므로 첫 분류 파일을 만든 뒤 돌립니다. 결과를 사용자에게 README 말투로 한두 줄 알리고, 첫 명령으로 `/tech-doc-issue-summary <프로젝트 폴더>`(문서가 없으면 `준비`)를 권합니다.

## 4. 반드시 지킬 원칙

원래 템플릿 레포의 원칙입니다. 대상 레포 지시 파일에 없으면 넣습니다.

1. **지어내지 않는다.** 문서와 정리 파일의 모든 값은 원천·사용자 답·코드에서 찾을 수 있는 사실. 없으면 비우고 질문으로 남긴다.
2. **사용자 문서를 대신 고쳐 쓰지 않는다.** 프로젝트 문서는 사용자 것. "고쳐 줘·반영해 줘"라고 한 범위만 고친다. 고치기 전에 `git diff`로 사용자가 직접 고친 줄을 찾아 그대로 둔다.
3. **Claude가 요약·작성한 문장은 표시한다.** report 「확인해 주세요」에 한 줄씩.
4. **근거는 report에, 문서엔 내용만.** 정확한 일자·커밋 해시·원천 출처 표기(`R2:L92`, `Q-49`)는 프로젝트 문서 본문에 쓰지 않는다. 문서 안 절 번호 각주와 `code/` 파일:줄은 써도 된다.
5. **커밋하지 않는다.** 사용자가 요청할 때만.
6. **고르지 않은 자료는 보지 않는다.** 원천 범위는 사용자가 고르고, 코드·Git·블로그는 사용자가 가리킨 범위만.

## 5. 실행할 때 지킬 것 (요약)

- **세션을 나눕니다.** 차수 하나 = 세션 하나. 대화 세션은 질문 하나마다 따로 열고, 연 뒤 원래 세션과 연결을 끊습니다(새 세션 결과가 원래 세션에 섞이지 않게). 브리프는 "파일 위치·몇 차까지·읽을 references"면 충분합니다.
- **먼저 채우고 묻습니다.** 참고 범위 문서에 형식 칸에 맞는 내용이 있으면 출처와 `(추론)`을 달아 채운 뒤, 남은 것만 묻습니다. 사용자가 0에서 쓰게 하지 않습니다.
- **질문은 구체적으로.** 볼 원천 위치, 무엇이 부족한지, (가)/(나) 예시 선택지. 이미 답한 것은 다시 묻지 않습니다. Claude가 범위 안에서 찾을 수 있는 것은 먼저 찾습니다. 사실 확인은 주장 하나에 질문 하나.
- **질문은 파일 맨 앞(§1 고민 상자) 한 곳에만.** 사용자는 질문 바로 아래에 탭으로 들여써 답합니다. 사용자는 Obsidian으로 읽습니다(콜아웃 상자·`[[#제목]]` 링크).
- **사용자 답은 원문 그대로 남깁니다**(`💬 사용자 답 (날짜 N차): “…”`). 답이 원천·문서와 어긋나면 양쪽을 남기고 "충돌".
- **숫자는 스크립트로.** 차수마다 `gomin_status.py` 어긋남 0, `check_quotes.py` 불일치 0(문서를 고친 뒤 `TD` 행 줄 밀림은 정상).
- **"다 확인했다"고 쓰지 않습니다.** 못 본 자료는 못 봤다고 씁니다.

## 6. 실패했던 것 (같은 실수 피하기)

| 일어난 일 | 사용자 반응 | 지금 규칙 |
|---|---|---|
| 지시문 출력 순서대로 질문을 파일 뒤쪽 절에 따로 모음 | "5, 6같이 중요한걸 오히려 먼저 … (굳이 나누지 말고. 나눈 이유가 뭐야?)" | 질문·맥락을 §1 고민 상자 안에, 중요한 것 먼저 |
| 원천 줄 근거만 늘어놓고 칸을 비운 채 물음 | "자꾸 원천에 너무 매몰되는거 같은데" | 범위 안 내용으로 먼저 채우고 질문 |
| 넓은 표로 고민 목록 | "옵시디언에서 보면 가시성이 아주 구리거든?" | 고민 목록은 콜아웃 상자 |
| PDF 숫자·라벨 짝을 사용자에게 물음 | "이게 무슨 말이야???" | Claude가 PDF를 다시 뽑아 확인 |
| 개편 초안으로 문서를 통째로 교체 | "권고안대로 다 바꾸는 게 아니라 어떻게 혼합할지 계획" | 장마다 유지/개편/합침을 먼저 정하고 섞음, 지운 원문은 백업 |
| 피드백을 넓게 해석해 장 구조를 바꾸자고 제안 | "전혀 이런 말이 아니고 내용 위치랑 글 내용만 조금만 다듬어서" | 가장 작은 변경안을 첫 안으로 |
| 문서 본문에 원천 출처·절 위치를 붙임 | "이게 무슨 논문 출처 같은 것도 아니고" | 근거는 report에만 |
| 문서에 "제작자의 회고", "이 표에서는 …로 묶었습니다" 같은 정리자 말투 | "발표·설명하는 입장" | 1인칭 발표체, `voice-lint.py`로 검사 |

## 7. 원본과 다른 점

- 원래 레포에서는 `report/고민-분류/` 아래에 실제 분류 파일이 있었습니다. 키트에서는 `examples/고민-분류/`로 옮겼습니다.
- 별칭표(`*-alias.tsv`)는 개인 절대경로라 빼고 `templates/alias.tsv` 견본만 넣었습니다. `examples/`의 대응표 위치(`R2:L86`)는 원본 파일이 없으므로 검사할 수 없습니다.
- `examples/`와 references 안의 절대경로는 `<레포>`·`<노트 루트>`·`<홈>`으로, 팀원 이름은 `<팀원1>` 꼴로 바꿨습니다.
