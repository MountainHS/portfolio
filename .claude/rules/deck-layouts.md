---
paths:
  - "src/**/deck-edits.json"
  - "assets/project-deck.js"
  - "assets/deck-theme.css"
  - "assets/deck-common.js"
  - "tools/deck-checks.mjs"
  - ".claude/skills/portfolio-edit/**"
  - ".claude/skills/portfolio-build/**"
---

# 슬라이드 레이아웃 카탈로그 (에이전트용, 글만)

> 장면 배치(`layout`·`presentation`)를 고르거나 새로 만들 때 봅니다. 스크린샷이 있는 사람용 문서는 `docs/deck-layouts.md`입니다. 그 그림을 Read하지 않습니다. 판단은 아래 데이터 조건과 `node tools/check-deck.mjs <폴더>` 숫자로 합니다.
> 데이터 조건은 `.claude/skills/portfolio-build/deck-edits.py` 검증과 같습니다. 그 파일의 `LAYOUTS`·`PRESENTATIONS`가 바뀌면 이 표도 고칩니다. 기준: 2026-11-27.

## 먼저 볼 원칙: 칸마다 그림을 따로 넣는 배치는 틀을 맞춘다

- **재발한 형태**: 한 줄에 칸을 여러 개 두고, 칸마다 그림을 **따로** 넣는 배치(칸마다 `.vis` 하나, 또는 `.vis` 밖 그림).
- **원인**: `PF.fitVisuals`(`assets/deck-common.js`)는 `.vis`마다 따로 크기를 정합니다. 칸 하나에 그림 하나면 높이 = 칸 폭 ÷ 그 그림 비율이 됩니다. 그래서 비율이 다른 사진은 틀 높이가 달라지고, 그 아래 글 줄도 칸마다 어긋납니다.
- **맞추는 방법**:
  - `screens`: 렌더러 `alignScreenCells`가 그 줄에서 가장 큰 틀을 모든 칸에 씁니다. CSS subgrid로 FEATURE·제목·설명 줄을 맞춥니다.
  - `columns`·`feature-rows`·`visual-pair-table`: 그 배치 전용 고정 높이 CSS를 씁니다.
- 그림 여러 장을 `.vis` **하나**에 넣는 기본 배치는 해당하지 않습니다. `fitVisuals`의 가로 배치가 공통 높이를 써서 원래 높이가 맞습니다.
- **새 배치를 만들 때**: 칸마다 그림을 따로 넣으면 같은 틀 맞추기를 쓰고, `tools/deck-checks.mjs`에 그 배치의 틀·글 줄 검사를 넣습니다. 고정 px 하나로 끝내지 않습니다.

## 먼저 볼 원칙: 두 패널을 나란히 두는 배치는 행 틀을 함께 쓴다

- **재발한 형태**: `paired`처럼 패널 두 개를 좌우에 두고, 패널마다 제목·그림·카드·흐름 칸을 위에서 아래로 쌓는 배치(2026-11-27 WGO `spec-level`·`ts-input`·`ts-bridge-count`).
- **원인**: 패널마다 grid가 따로였습니다. 그래서 한쪽 도입 글이 한 줄 길거나 표가 한 행 많거나 흐름 칸 글이 한 줄 길면, 그 아래 카드·칸이 그 패널에서만 밀립니다. 두 패널 사이에 줄을 맞추는 장치도, 그 높이를 재는 검사도 없었습니다(예전 정렬 수정 `55f4042`·`f920f11`·`7ea8272`은 다른 배치만 고침).
- **맞추는 방법**: 바깥 `.dk-paired`가 행 틀을 정하고 두 패널이 `grid-template-rows: subgrid`로 같은 틀을 씁니다(`assets/deck-theme.css`, 행 수 `--body-rows`는 `assets/project-deck.js` paired 분기가 정함).
  - 표 있음(`has-vis`): 제목 · 표 · 카드 3행. 카드 묶음이 같은 높이에서 시작합니다.
  - 흐름 칸 수가 같음(`rows-shared`): 칸마다 한 행. 같은 번호 칸끼리 시작·높이가 같습니다.
  - 흐름 칸 수가 다름(`rows-centered`): 짧은 쪽 흐름을 긴 쪽 높이의 세로 가운데에 둡니다.
- **새 배치를 만들 때**: 칸·패널을 나란히 두고 그 안에 여러 줄을 쌓으면, 줄 높이를 칸끼리 공유하는 틀(subgrid 또는 공통 높이)을 쓰고 `tools/deck-checks.mjs`에 줄마다 시작·높이 검사를 넣습니다. 패널 위쪽 끝만 재는 검사로 끝내지 않습니다.

## 다이어그램 + 카드 장면: 그림과 카드가 같은 말을 두 번 하지 않는다

- **재발한 형태**(2026-11-27 WGO `bridge-build` 「소비 계획 생성과 일괄 소비」, `visual-notes` + 세로 Mermaid + 카드 3개): 그림은 4단계·카드는 3단계라 짝이 흐렸습니다. 카드가 노드 글(수치·재료)을 문장으로 다시 썼고, 세로 그림이 옆 카드에 밀려 본문 폭의 14%로 줄며 노드 글이 꺾였습니다. 정작 핵심 경계(소비 **계획**을 만드는 단계 vs 실제 자원을 **바꾸는** 단계)는 그림에도 카드에도 드러나지 않았습니다.
- **규칙**:
  1. **단계 수를 맞춘다.** 흐름도 노드 하나에 카드 하나. 묶어야 하면 그림에서도 같은 묶음(subgraph·같은 색)으로 보이게 하고 카드 이름표를 그 묶음 이름으로 씁니다.
  2. **그림이 말한 것은 카드에서 반복하지 않는다.** 노드에 적힌 수치·재료·순서("나무 1개 + 열대어 1마리 = 5점")를 카드 본문에 다시 쓰지 않습니다. "먼저 확보합니다", "한 마리만 더 고릅니다"처럼 그림의 화살표·숫자로 이미 보이는 동작 서술도 뺍니다. 카드는 그림에 없는 것(왜 그렇게 나눴는지, 무엇이 바뀌는지, 실패하면 어떻게 되는지)만 씁니다. 카드 이름표 = 노드 첫 줄은 괜찮습니다(짝을 보여 주는 용도).
  3. **단계형 흐름은 가로 배치를 먼저 쓴다.** 노드 3~6개의 일직선 흐름이면 `steps`(노드 아래 설명)·`flow-table`(노드 카드 가로)·`stack`(그림 위, 카드 아래)을 먼저 고릅니다. `visual-notes`·`side-notes`는 갈래·계층이 있어 세로가 자연스러운 그림일 때만 씁니다.
  4. **핵심 경계를 그림에서 드러낸다.** 장면의 요지가 경계(계획/적용, 검사/실행, 호스트/게스트)라면 그 경계를 그림의 묶음·구분선·색으로 보이게 하고, 카드는 경계의 이유에 무게를 둡니다. 세부(재료 고르는 순서 등)에 카드를 여러 장 쓰지 않습니다.
- **자동 검사**(`tools/deck-checks.mjs`, 경고 — 실패해도 종료 코드 0, `findings`·`warnings` 개수에 나옴). `flow-table`·`steps`·`cycle`·`columns`·`stage-table`·`comparison`은 노드와 칸 짝이 정해진 배치라 뺍니다.
  - `diagram steps match explanation cards`: 흐름도 노드 3~6개·간선 ≤ 노드 수(일직선에 가까움)·카드 2개 이상인데 노드 수 ≠ 카드 수.
  - `explanation cards do not repeat diagram node text`: 카드 본문(`p`)에 노드 글 한 줄(공백 뺀 6자 이상, 클래스 이름 하나는 제외)이 그대로 들어 있음.
  - `vertical diagram beside cards keeps node text on its written lines`: 세로 흐름도(viewBox 높이 > 폭)가 카드 옆에 있고, 노드 글이 원문 줄 수보다 더 꺾여 보임. 근거에 `widthShare`(그림 폭 ÷ 본문 폭).
  - probe `diagram-card-overlap`: 첫 `visual-notes`·`side-notes` 흐름도 장면의 노드 수를 카드 수 ± 1로 바꾸고(카드는 그대로), 가장 긴 노드 글 줄을 첫 카드에 붙이고, 노드 글을 강제로 꺾어 세 경고를 모두 띄웁니다.
- 자동 검사가 못 잡는 것(4번 핵심 경계, "먼저 확보합니다" 같은 뜻 반복)은 장면을 만들거나 고칠 때 사람이 봅니다.

## 읽는 법

- 칸 그림: `[그림]` 이미지·Mermaid · `[표]` · `[코드]` · `(1)` 번호 카드 · `(·)` 번호 없는 카드 · `라벨`은 요점 이름표.
- 조건: `V` = visuals 종류·개수, `P` = points 개수. `presentation`을 쓰면 `layout`은 그대로 두고 배치만 바뀝니다.

## 1. 사진 여러 장 + 기능 설명

### `screens` — 사진 위, 글 아래 (칸 2~3개)
```
[그림 A] [그림 B] [그림 C]   ← 같은 틀(가장 큰 틀), 사진은 틀 안에 비율 유지
 캡션     캡션     캡션
 FEATURE 01 · 02 · 03
 제목     제목     제목
 설명     설명     설명      ← 줄마다 같은 높이에서 시작
```
- 언제: 화면 2~3장이 서로 다른 기능이나 요소를 하나씩 보여 줄 때.
- 조건: `layout: "screens"`, V = 이미지 2~3개, P = 0~V개(`points[i]`가 `i`번 칸 글).
- 검사: `screen cells share one frame and text rows`(필수) · probe `uneven-screen-cell`.
- 예: `5-hive-purge/animator-override`(글 있음), `3-mini-ftl/three-decisions`(글 없음).

### `feature-rows` — 글 왼쪽, 사진 오른쪽 (2행)
```
| FEATURE 01 라벨·설명 | [전 그림] [후 그림] |   ← 두 그림 같은 높이
| FEATURE 02 라벨·설명 |    [그림 하나]      |
```
- 언제: 한 기능의 전후 사진 2장과, 다른 기능 사진 1장.
- 조건: `presentation: "feature-rows"`, V = 이미지 3개(위 2·아래 1 순서), P = 2.
- 검사: `two feature rows pair descriptions with ordered images`.
- 예: `5-hive-purge/shader`.

### `columns` — 그림 3열 + 제목 + 설명
```
[그림] [그림] [그림]   ← 행 높이 고정(210px), 열 가운데
 라벨   라벨   라벨
 설명   설명   설명
```
- 언제: 상태·단계 세 개를 나란히 비교하고, 각각 작은 그림이 있을 때.
- 조건: `presentation: "columns"`, V = Mermaid·이미지 3개, P = 3.
- 검사: `each column shows its own diagram above its explanation` · `matched diagram, title and explanation table rows align`.
- 예: `5-hive-purge/state-idle`, `4-wgo/bridge-data`.

## 2. 그림(다이어그램·화면) 1장 + 설명

### `visual` 기본 — 그림 + 번호 카드 (옆 또는 위아래 자동)
```
[그림        ] (1)          또는   [그림 ─────────]
[            ] (2)                 (1) (2) (3)
```
- 언제: 그림 하나를 요점 몇 개로 풀 때. 가로로 긴 그림은 렌더러가 위아래 배치를 고릅니다.
- 조건: `layout: "visual"`, V ≥ 1, P = 0~4. 그림 여러 장은 한 `.vis`에 들어가 공통 높이로 맞춰집니다.
- 예: `3-mini-ftl/ftl-intro`(화면), `3-mini-ftl/system-map`(다이어그램).

### `visual-notes` — 다이어그램 왼쪽, 카드 오른쪽
```
[다이어그램] (1)
[          ] (2)   ← 카드 묶음 세로 중앙 = 그림 중앙
```
- 조건: `presentation: "visual-notes"`, V = Mermaid 1개, P = 1~4.
- 검사: `diagram and stacked cards placement` 외 세로 중앙 검사.
- 예: `4-wgo/schedule`, `5-hive-purge/arch`.

### `side-notes` — 카드 왼쪽, 다이어그램 오른쪽
```
(1) [다이어그램]
(2) [          ]
(3)
```
- 조건: `presentation: "side-notes"`, V = Mermaid 0~1개, P = 3. 시각 자료 없이도 `visual`·`code`·`full` 틀에 쓸 수 있습니다.
- 예: `4-wgo/no-prediction`.

### `stack` — 그림 위, 카드 한 줄 아래
```
[그림 ───────────────]
(·) (·) (·)
```
- 조건: `presentation: "stack"`, V ≥ 1, P = 0~4(번호 없는 카드).
- 예: `4-wgo/scope-gap`.

### `steps` — 흐름도 + 노드 아래 설명
```
[A] → [B] → [C] → [D]
설명  설명  설명  설명   ← 노드 가운데 정렬
```
- 조건: `presentation: "steps"`, V = Mermaid 1개(노드 4개), P = 4.
- 검사: `steps are centered in the body` · `step explanations align with their diagram nodes`.
- 예: `4-wgo/core-loop`.

### `cycle` — 순환 흐름
```
[A] → [B] → [C]
 ↑___________|
```
- 조건: `presentation: "cycle"`, V = Mermaid 1개.
- 검사: `cycle nodes align and the last arrow returns to the start`.
- 예: 지금 보이는 장면 없음.

### `flow-table` — 노드 카드 가로 + 아래 표
```
| 패널 제목(그림 caption)                  |
| [노드1 카드] → [노드2 카드] → [노드3]    |  ← 카드 설명 = 요점(노드 순서)
| 패널 제목(표 caption)                    |
| [표 ───────────────────────────────]     |
```
- 언제: 계층·흐름 Mermaid(노드 2~5개)와 표 하나, 단계마다 한 줄 설명.
- 조건: `presentation: "flow-table"`, V = Mermaid 1개 + 표 1개, P = 2~5(노드 수와 같게, 이름표 = 노드 첫 줄).
- 검사: `points rendered` · `flow-table bracket note starts its own line`.
- 예: `5-hive-purge/wave-structure`, `3-mini-ftl/loop`.

## 3. 그림 + 표

### `pair` — 그림 하나, 표 하나 (요점 없음)
```
[그림] [표]
```
- 조건: `presentation: "pair"`, V = Mermaid·이미지 1개 + 표 1개, P = 0.
- 예: `4-wgo/host-guest-cases`.

### `visual-pair-table` — 그림 둘 위, 표 아래 (요점 없음)
```
[그림 A] [그림 B]   ← 같은 높이(205px), 가운데 맞춤
[     표     ]      ← 전체 폭 가운데
```
- 조건: `presentation: "visual-pair-table"`, V = Mermaid·이미지 2개 + 표 1개, P = 0.
- 검사: `two visuals share a row above centered table`.
- 예: `5-hive-purge/wave-data`.

### `stage-table` — 단계 표 안에 작은 다이어그램 3개
```
| 단계1 | 단계2 | 단계3 |
| [그림]| [그림]| [그림]|
| 설명  | 설명  | 설명  |
```
- 조건: `presentation: "stage-table"`, V = Mermaid 3개, P = 3.
- 검사: `stage table columns and explicit line breaks`.
- 예: `4-wgo/snapshot-stage2`.

### `comparison` — 원문 표에 다이어그램 칸 끼우기 (요점 없음)
```
| 항목 | 데이터 흐름 | … |
| 행1  | [그림]      | … |
```
- 조건: `presentation: "comparison"`, V = 표 1개 + Mermaid 3개(표 행 수 = 다이어그램 수), P = 0.
- 예: `4-wgo/snapshot-compare`.

## 4. 코드

### `code` — 코드 + 번호 카드
```
[코드 A ─ ①② 강조]  (1)
 캡션 A              (2)
[코드 B ─ ③ 강조 ]  (3)
 캡션 B
```
- 조건: `layout: "code"`, V에 코드 1개 이상, 24줄 넘으면 이어지는 장면으로 나뉨. 카드마다 실행 줄을 대면 `codeEvidence: "all-points"`와 `view.highlights` 번호.
- 배치(요점이 있을 때 고정, 2026-11-27 WGO `timing`): 코드 열은 왼쪽, 코드가 둘 이상이면 위·아래로 쌓음. 번호 카드 열은 오른쪽에 위·아래. 렌더러 `fitSlide`의 `codeSide`가 stack 변형을 빼고, `bodyHtml`이 코드 칸에 `data-arr="col"`을 달아 `PF.fitVisuals`가 코드를 가로로 놓지 못하게 합니다.
  - 예외: 코드 줄이 넓어 왼쪽 칸에 하한 글자(12px)로도 안 들어가면 그때만 코드 위·카드 아래로 물러섭니다(장면에 `data-pf-code-stack="wide"`, 알림 `code-stack`). 지금 Hive `attack-hooks`·`factory`·`wave-start`.
  - 요점이 없는 나뉜 뒤쪽 장면(`-2`)은 코드만 가로 전체입니다.
- 검사: `every code explanation card has a matching execution-line highlight` · `code caption shows source location` · `code scene keeps code column left and cards column right`(넓은 코드 예외는 참고) · probe `code-stack`.
- 예: `4-wgo/timing`(코드 둘), `5-hive-purge/attack-state`(코드 하나).

## 5. 전후·선택 비교

### `paired` — 두 패널 나란히
```
| 패널1 제목·도입 | 패널2 제목·도입 |
| [표] 또는 흐름   | [표] 또는 흐름   |
```
- 언제: 선택지 둘, 전후 둘, 문제·해결 둘.
- 조건: `presentation: "paired"`, `panels` 2개, V = 표 2개 또는 없음, 상위 P = 0.
- 행 맞춤: 표 있음 = 카드 묶음 시작 높이 같음 · 흐름 칸 수 같음 = 칸마다 시작·높이 같음 · 칸 수 다름 = 짧은 쪽 세로 가운데(위 원칙).
- 검사: `paired panel bodies align row by row`(필수) · probe `uneven-paired-row`.
- 예: `3-mini-ftl/power-cost`, `4-wgo/spec-level`.

### 선택 비교 장면 규칙 — 대안 A/B와 고른 결과 (2026-11-27, WGO `host-guest` 피드백)
- **재발한 형태**: `대안|장점|단점` 표 하나에 대안 설명 구체성이 서로 다르고(A는 수치까지, B는 한 줄), 실제 구현에서 확인한 수치(예: "14개·4곳")가 긴 단점 문장에 묻히고, 사후 판단 카드가 비교한 범위 밖의 대안(예: 클래스 구조 비교에 서버 권한형 아키텍처)까지 끌어옴.
- **규칙**
  - 대안마다 같은 칸 순서·같은 구체성: 제목 → 구조(그림) → 장점 → 단점. 한쪽만 구조도가 있거나 한쪽만 수치가 있으면 맞춥니다.
  - 실제 구현 / 검토한 대안 / 사후 개선 방향을 표시로 나눕니다(`실제 구현`·`검토한 대안` 꼬리표). 구현하지 않은 대안을 검증한 것처럼 쓰지 않습니다.
  - 실제 구현에서 확인한 수치는 문장에서 떼어 지표 칸(`dg-stats`)으로 둡니다. 수치 뜻은 원문 표현 그대로(예: "IsHost를 검사하는 스크립트 14개").
  - 사후 판단은 비교한 설계 범위 안의 내용만 씁니다. 범위가 다른 대안(아키텍처·동기화 모델)은 그 주제 장면에 둡니다. 소제목은 "사후 판단" 대신 개선 방향 자체(예: "역할과 데이터 명세의 선행 정의").
  - 그림이 말한 내용을 카드에서 다시 쓰지 않습니다. 비교·결과·사후 판단을 한 장면에 모을 때는 deck-only HTML 그림(아래 「HTML 그림」) 하나로 그리고 `points`는 비웁니다.
- 점검: 장면을 고르거나 고친 뒤 대안 칸마다 "구조·장점·단점"이 다 있는지, 수치가 지표 칸으로 나왔는지, 사후 판단이 같은 범위인지 사람이 봅니다(자동 검사 없음).

### `phase-timeline` — 재사용 불가
- 전후 막대 글이 렌더러(`project-deck.js`)에 Hive 내용으로 박혀 있습니다. 다른 프로젝트에 쓰지 않습니다. 비슷한 장면은 `paired`나 렌더러 일반화를 먼저 사용자에게 묻습니다.
- 예: `5-hive-purge/phase-time`.

## 6. 글 위주

### `overview` — 카드만
```
(1) (2)
(3) (4)   ← 요점 3개 이상이면 두 열
```
- 조건: `layout: "overview"`, V = 0(그림이 있으면 `visual`처럼 그림 + 카드).
- 예: `3-mini-ftl/why`.

### `full` — 표·그림 전체 폭
```
[표 ─────────────────]
(·) (·)                 ← 요점이 있으면 아래 한 줄
```
- 조건: `layout: "full"`, V ≥ 1.
- 예: `5-hive-purge/threats`.

### `problem` — 문제 → 원인 → 해결 흐름
```
문제 → 원인 → 해결 (세로 흐름) | [그림]   ← 그림이 있으면 오른쪽
```
- 조건: `layout: "problem"`, V = 0~(그림·코드), P = 흐름 단계 수. 그림이 없으면 흐름만 둡니다.
- 예: `4-wgo/room-lifecycle`(글만), `3-mini-ftl/power-ui`(그림 + 흐름).

### `result-rows` — 결과 두 줄
```
| 라벨 | 결과 설명 |
| 라벨 | 결과 설명 |
```
- 조건: `presentation: "result-rows"`, V = 0, P = 2.
- 예: `5-hive-purge/result`.

## 7. HTML 그림 — deck-only (이 포크, 2026-11-27)

```
[HTML 그림 ─────────── 전체 폭]   ← 표처럼 칸 폭을 채우고, 높이가 넘치면 0.8배까지 줄임
```
- 언제: Mermaid로는 배치를 못 그리는 장면(가로 단계 카드 + 그룹 머리, 호스트·게스트 비교 아이콘, A/B 구조도 + 지표). 예: `4-wgo/world-item-sync`·`bridge-build`·`host-guest`.
- 원문: 프로젝트 문서 그 절에 ` ```html ` 블록을 두고 첫 줄에 `<!-- deck-only -->`. 상세 문서(`assets/doc.js`)는 이 블록을 빼고, 슬라이드는 `visuals: [{"kind": "html", "n": 1, "fp": "<블록의 주석 다음 첫 줄>"}]`로 그립니다(`md-blocks.js` `isDeckHtml`, `deck-edits.py` `KINDS`).
- 조건: `layout: "full"`, V = html 1개, P = 0(그림이 다 말함). 그림 안 글도 원문 사실만 씁니다(원칙 1).
- 꾸밈: `assets/deck-theme.css`의 `dg-*` 클래스만 씁니다(패널 `dg-panel`, 호스트·게스트 `dg-pair`/`dg-side`/`dg-tile`, 조건 갈래 `dg-branch`, 단계 카드 `dg-steps`/`dg-group`/`dg-step`/`dg-big`, 구조도 `dg-tree`/`dg-node`/`dg-fork`, 장단점 `dg-pros`, 지표 `dg-stats`, 사후 판단 `dg-after`). 색은 보라 = 계획·주 대상, 청록(`is-teal`) = 실제 변경. 인라인 색·글꼴을 쓰지 않습니다.
- 크기: `PF.fitVisuals`가 표처럼 다룹니다(`natOf` html, 높이 여유 6px). 넘치면 글을 줄이거나 칸을 줄입니다.

### 장면 각주 `footnote`
- 본문 흐름과 결이 다른 보충 한 줄(예: 「AI 활용」)은 요점 카드 대신 `footnote` 글 조각으로 둡니다. 본문 아래 `※` 작은 글(`.dk-footnote`). 예: `4-wgo/bridge-classes`.
