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
[코드 ─ ①②③ 강조]  (1)
[               ]  (2)
캡션: 클래스.메서드() · 파일:줄
```
- 조건: `layout: "code"`, V에 코드 1개 이상, 24줄 넘으면 이어지는 장면으로 나뉨. 카드마다 실행 줄을 대면 `codeEvidence: "all-points"`와 `view.highlights` 번호.
- 검사: `every code explanation card has a matching execution-line highlight` · `code caption shows source location`.
- 예: `5-hive-purge/attack-state`.

## 5. 전후·선택 비교

### `paired` — 두 패널 나란히
```
| 패널1 제목·도입 | 패널2 제목·도입 |
| [표] 또는 흐름   | [표] 또는 흐름   |
```
- 언제: 선택지 둘, 전후 둘, 문제·해결 둘.
- 조건: `presentation: "paired"`, `panels` 2개, V = 표 2개 또는 없음, 상위 P = 0.
- 예: `3-mini-ftl/power-cost`, `4-wgo/spec-level`.

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
