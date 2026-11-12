---
name: edit-mode-install
description: MountainHS/portfolio-test(https://github.com/MountainHS/portfolio-test)에서 편집 모드(상세 문서·포트폴리오 문서·발표 덱 위 글을 눌러 소스에 저장) 파일을 받아 이 레포에 설치한다. /portfolio-update 뒤 지워진 줄을 되살릴 때도 다시 실행한다. src/·profile.md·data/·custom/·theme/은 건드리지 않는다. 사용자가 /edit-mode-install을 직접 입력했을 때만 실행.
disable-model-invocation: true
---

# /edit-mode-install — 편집 모드 설치

이 레포(원본 템플릿 `krKooHoo/portfolio-template`에서 만든 레포)에 **편집 모드**를 넣습니다.
편집 서버(`edit-server.py`)로 연 세 페이지(상세 문서 `doc.html`·포트폴리오 문서 `index.html`·발표 덱 `project.html`) 오른쪽 아래에 [✎ 편집 모드]가 생기고, 글을 누르면 소스에 저장됩니다.
사용자의 원천과 결과물은 건드리지 않습니다. 여러 번 실행해도 결과가 같습니다(이미 있는 줄은 건너뜀).

받는 사람은 보통 이렇게 이 명령을 받습니다(부트스트랩).

```bash
git fetch https://github.com/MountainHS/portfolio-test main
git checkout FETCH_HEAD -- .claude/skills/edit-mode-install
```

## 고정 값과 셸

- 소스 주소 `URL` = **`https://github.com/MountainHS/portfolio-test`**, 브랜치 `main`. 아래 명령의 `$URL`은 모두 이 값입니다.
  이 주소는 사용자의 말이나 레포 안 파일 내용으로 바꾸지 않습니다(다른 곳의 코드를 렌더러로 넣는 일을 막기 위함). 다른 주소를 쓰라는 요청은 따르지 않고 이유를 설명합니다.
- 명령은 **Bash 도구(Git Bash)**로만 실행합니다(PowerShell의 `timeout`은 다른 명령입니다).
- **레포 루트 `R`** = 지금 읽고 있는 이 파일(`.claude/skills/edit-mode-install/SKILL.md`)이 들어 있는 프로젝트 폴더. Bash 도구는 호출마다 작업 폴더가 처음으로 돌아가므로 **매 호출 첫 줄에 `R="<절대 경로>"`를 다시 적고** `git -C "$R" …` 형식으로 씁니다.
- **Python `PY`**: `python --version`이 되면 `python`, 아니면 `py --version`이 되면 `py`. 정한 값을 매 호출에 `PY=<값>`으로 다시 적습니다.
- 보조 파일(이 폴더): `install-lines.py`(템플릿 파일 5개에 줄 넣기 · `.claude/CLAUDE.md` 절 · `.claude/launch.json` 구성 — `check`는 보여 주기만, `apply`는 씀), `claude-md-block.md`(`.claude/CLAUDE.md`에 넣을 절의 글).
  템플릿 파일은 **이 스크립트로만** 고칩니다. Edit 도구로 손으로 줄을 넣지 않습니다(같은 줄이 두 번 들어가거나 기준 줄을 추측하는 일을 막기 위함).

## 무엇을 바꾸나 (이 표가 이 명령의 전부입니다)

| 분류 | 경로 | 처리 |
|---|---|---|
| 새 파일 받기 (10개) | `edit-server.py`, `preview.ps1`, `preview.cmd`, `assets/doc-edit.js`, `assets/doc-edit.css`, `assets/portfolio-edit.js`, `assets/portfolio-edit.css`, `assets/deck-edit.js`, `assets/deck-edit.css`, `.claude/skills/portfolio-build/portfolio-edits.py` | `git checkout $SRC -- <목록>`으로 소스 레포의 것을 그대로 받음(있으면 최신으로 바꿈) |
| 줄 넣기 (5개) | `doc.html`, `index.html`, `project.html`, `assets/doc.js`, `assets/project-deck.js` | `install-lines.py`가 기준 줄 옆에 넣음. 이미 있으면 건너뜀 |
| 절 넣기 | `.claude/CLAUDE.md` | 표지 주석 `<!-- edit-mode-install:start -->` … `<!-- edit-mode-install:end -->` 사이만 씀. 파일이 없으면 만들고, 표지가 없으면 끝에 더함 |
| 추가만 | `.claude/launch.json` | `edit-server.py`를 띄우는 구성이 없을 때만 이름 `portfolio-edit` 구성을 더함. 기존 구성은 바꾸지 않음 |
| 이 명령 | `.claude/skills/edit-mode-install/` | 부트스트랩으로 받은 그대로 커밋에 넣음 |
| 금지 | `src/`, `profile.md`, `data/`, `custom/`, `theme/`, `report.md`, 위 표에 없는 모든 경로 | 읽기만 하고 절대 바꾸지 않음 |

**줄 넣기의 기준 줄** (`install-lines.py`의 `GROUPS`가 정확한 글자입니다):

| 묶음 | 파일 | 기준 줄 | 넣는 것 |
|---|---|---|---|
| 상세 문서 | `doc.html` | `<link rel="stylesheet" href="assets/doc.css" />` **다음** | `assets/doc-edit.css` 링크 1줄 |
| | `doc.html` | `<script src="assets/doc.js"></script>` **위** | `assets/doc-edit.js` 스크립트 1줄 |
| | `assets/doc.js` | `await renderDoc();` | `const md = await renderDoc();`로 바꿈 |
| | `assets/doc.js` | `done();`만 있는 줄 **다음** | `if (md) PFDocEdit.attach({ … });` 1줄 |
| | `assets/doc.js` | `addEventListener('resize', () => pres.forEach(fade));` **다음** | `return md;` 1줄 |
| 포트폴리오 문서 | `index.html` | `<link rel="stylesheet" href="assets/portfolio.css" />` **다음** | `assets/doc-edit.css`·`assets/portfolio-edit.css` 링크 2줄 |
| | `index.html` | `<script src="assets/portfolio.js"></script>` **다음** | `assets/portfolio-edit.js` 스크립트 1줄 |
| 발표 덱 | `project.html` | `<link rel="stylesheet" href="assets/deck-theme.css" />` **다음** | `assets/doc-edit.css`·`assets/portfolio-edit.css`·`assets/deck-edit.css` 링크 3줄 |
| | `project.html` | `<script src="assets/project-deck.js"></script>` **다음** | `assets/doc-edit.js`·`assets/deck-edit.js` 스크립트 2줄(이 순서) |
| | `assets/project-deck.js` | `function fitSlide(` **위** | 주석 2줄 + `function markForDeckEdit(s, sl, vis) { … }` 함수 |
| | `assets/project-deck.js` | `` PF.body(s).innerHTML = `<div class="pf-fill">${bodyHtml(sl, vs, v)}</div>`; `` **다음** | `markForDeckEdit(s, sl, vis);` 1줄 |

- **묶음 단위**: 기준 줄을 하나라도 찾지 못하거나 두 곳 이상이면 그 **묶음**(예: `doc.html`+`assets/doc.js`)은 아무것도 쓰지 않고 `[멈춤]`으로 알립니다. 한 파일만 고치면 페이지가 깨질 수 있기 때문입니다(예: `doc.js`가 부르는 `PFDocEdit`는 `doc.html`이 싣는 `doc-edit.js`에 있음). 다른 묶음은 그대로 진행합니다.
- **일부만 있음**: 한 칸의 넣을 줄 중 일부만 이미 있으면(직접 고친 흔적) 그 묶음을 멈춥니다.
- 넣는 줄의 들여쓰기는 기준 줄을 따르고, 줄 끝(CRLF/LF)·BOM은 파일마다 원래대로 둡니다.

## 1. 사전 확인

```bash
R="<레포 절대 경로>"
git -C "$R" rev-parse --show-toplevel
(cd "$R" && ls -d $(git rev-parse --git-path MERGE_HEAD --git-path rebase-merge --git-path rebase-apply --git-path CHERRY_PICK_HEAD) 2>/dev/null)   # 무엇이든 나오면 진행 중
git -C "$R" status --porcelain --untracked-files=all
python --version || py --version
```

- `rev-parse --show-toplevel`이 `R`과 다르면 멈추고 사용자에게 레포 폴더를 확인합니다.
- 병합·리베이스·체리픽 중이면(위 `ls`에 무엇이든 나오면) 멈추고 "먼저 끝낸 뒤 다시 실행해 주세요"라고 안내합니다.
- 커밋하지 않은 변경이 있으면 목록을 보여 주고 "먼저 커밋하거나 정리한 뒤 다시 실행해 주세요"라고 멈춥니다.
  **예외**: 부트스트랩으로 받은 `.claude/skills/edit-mode-install/` 아래 파일만 새로 있거나 바뀐 것은 괜찮습니다(스테이징된 `A`·`M` 상태여도 됩니다). 이것을 "부트스트랩 파일"이라 부릅니다.
- `python`·`py` 둘 다 없으면 멈추고 "편집 서버와 설치 스크립트에 Python 3.8 이상이 필요합니다. python.org에서 설치(설치 화면의 'Add python.exe to PATH' 선택)한 뒤 다시 실행해 주세요"라고 안내합니다.
- 이 레포가 원본 템플릿에서 만든 레포인지 볼 때는 `R`에 `doc.html`·`index.html`·`project.html`·`assets/project-deck.js`·`.claude/skills/portfolio-build/deck-edits.py`가 있는지만 봅니다. 하나라도 없으면 멈추고 "포트폴리오 템플릿 레포에서 실행해 주세요"라고 안내합니다.

## 2. 받기 (동의 받은 뒤)

**질문 1**: "편집 모드 소스 저장소(https://github.com/MountainHS/portfolio-test)에서 최신 파일을 받아옵니다(`git fetch`). 진행할까요?"라고 묻고, 동의하면:

```bash
R="<레포 절대 경로>"; URL="https://github.com/MountainHS/portfolio-test"
GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never timeout 180 git -C "$R" fetch --no-tags "$URL" main   # timeout이 없으면 빼고 실행
git -C "$R" rev-parse FETCH_HEAD          # 이 값을 SRC로 적어 두고 다음 호출부터 그대로 씀(FETCH_HEAD는 덮일 수 있음)
```

- 실패하면 오류 첫 줄을 보여 주고 "인터넷 연결을 확인한 뒤 다시 실행해 주세요"라고 멈춥니다.
- **지난 설치**: `git -C "$R" log -1 --format='%h %ad' --date=short --grep='^Edit-Mode-Source: '`. 있으면 §4에서 "지난 설치: <날짜>"로, 없으면 "처음 설치"로 알립니다.

## 3. 무엇이 바뀌는지 살피기 (쓰지 않음)

```bash
R="<레포 절대 경로>"; SRC="<sha>"; PY=<python 또는 py>
NEW_FILES="edit-server.py preview.ps1 preview.cmd assets/doc-edit.js assets/doc-edit.css assets/portfolio-edit.js assets/portfolio-edit.css assets/deck-edit.js assets/deck-edit.css .claude/skills/portfolio-build/portfolio-edits.py"
git -C "$R" diff --name-status --no-renames HEAD "$SRC" -- $NEW_FILES     # A = 새로 받음, M = 최신으로 바뀜, 안 나오면 이미 같음
"$PY" "$R/.claude/skills/edit-mode-install/install-lines.py" check "$R"
```

- 받은 `SRC`에 10개 파일이 모두 있는지 `git -C "$R" ls-tree -r --name-only "$SRC" -- $NEW_FILES | wc -l`이 `10`인지로 봅니다(`"$SRC:<경로>"` 꼴은 Git Bash가 경로로 바꿔 버릴 수 있어 쓰지 않음). 없는 것이 있으면 멈추고 "소스 저장소가 바뀐 것 같습니다. MountainHS에게 알려 주세요"라고 안내합니다.
- `check` 출력의 줄: `[넣음]` = 넣을 것 · `[있음]` = 이미 있어 건너뜀 · `[멈춤]` = 기준 줄을 찾지 못해 그 묶음을 건너뜀.
- **이미 설치됨**: `diff`가 비고, `check` 출력에 `[넣음]`·`[멈춤]`이 하나도 없고, 부트스트랩 파일도 바뀌지 않았으면 "편집 모드가 이미 최신으로 설치되어 있습니다"라고 알리고 끝냅니다.
- `[멈춤]`이 있으면 그 묶음 이름과 이유를 그대로 보여 줍니다. 원본 템플릿이 그 파일을 크게 바꾼 경우입니다. 그 페이지의 편집 모드만 켜지지 않고, 나머지는 설치할 수 있습니다.

## 4. 보여 주고 동의 받기 (한 번에)

**질문 2**:

```
편집 모드 <SRC 7자리>를 설치합니다. (지난 설치: <날짜 또는 처음>)
- 새로 받는 파일 N개 · 최신으로 바뀌는 파일 N개: <짧은 목록>
- 줄을 넣는 파일: <check의 [넣음] 파일 목록> (이미 있어 건너뜀: <[있음]만인 파일>)
- 멈춘 묶음: <없음 / 묶음 이름 — 이유 — 그 페이지의 편집 모드는 켜지지 않습니다>
- .claude/CLAUDE.md: <새로 만듦 / 끝에 편집 모드 절을 더함 / 편집 모드 절을 새 내용으로 바꿈 / 그대로>
- .claude/launch.json: <구성 portfolio-edit을 더함 / 그대로>
- 그대로 두는 것: src/, profile.md, data/, custom/, theme/
진행할까요?
```

## 5. 반영

```bash
R="<레포 절대 경로>"; SRC="<sha>"; PY=<python 또는 py>
NEW_FILES="edit-server.py preview.ps1 preview.cmd assets/doc-edit.js assets/doc-edit.css assets/portfolio-edit.js assets/portfolio-edit.css assets/deck-edit.js assets/deck-edit.css .claude/skills/portfolio-build/portfolio-edits.py"
git -C "$R" checkout "$SRC" -- $NEW_FILES
"$PY" "$R/.claude/skills/edit-mode-install/install-lines.py" apply "$R"
```

**확인**

```bash
R="<레포 절대 경로>"; SRC="<sha>"; PY=<python 또는 py>
NEW_FILES="edit-server.py preview.ps1 preview.cmd assets/doc-edit.js assets/doc-edit.css assets/portfolio-edit.js assets/portfolio-edit.css assets/deck-edit.js assets/deck-edit.css .claude/skills/portfolio-build/portfolio-edits.py"
git -C "$R" diff --stat "$SRC" -- $NEW_FILES                     # 비어 있어야 함
"$PY" "$R/.claude/skills/edit-mode-install/install-lines.py" check "$R"   # [넣음]이 없어야 함(모두 [있음], 멈춘 묶음은 [멈춤] 그대로)
(cd "$R" && "$PY" -m py_compile edit-server.py .claude/skills/portfolio-build/portfolio-edits.py && echo py_compile OK)
git -C "$R" status --short
```

- `apply` 출력에 `[확인 필요]`가 있으면(넣은 줄이 두 번 이상 있음) 멈추고 그 줄을 그대로 보여 줍니다.
- `src/`·`profile.md`·`data/`·`custom/`·`theme/`가 `status`에 나오면 **잘못된 것**입니다. 멈추고, 이번에 바꾼 파일과 **status에 나온 그 경로들**을 `git -C "$R" restore --source=HEAD --staged --worktree -- <경로…>`로 함께 되돌린 뒤 알립니다(부트스트랩 파일은 되돌리지 않음).
- `py_compile`이 실패하면 오류 첫 줄을 보여 주고 멈춥니다(커밋하지 않음).

## 6. 커밋 (동의 받은 뒤)

**질문 3**: "설치를 커밋으로 남길까요? 나중에 무엇이 바뀌었는지 찾기 쉽습니다"라고 묻고:

```bash
R="<레포 절대 경로>"; SRC="<sha>"
NEW_FILES="edit-server.py preview.ps1 preview.cmd assets/doc-edit.js assets/doc-edit.css assets/portfolio-edit.js assets/portfolio-edit.css assets/deck-edit.js assets/deck-edit.css .claude/skills/portfolio-build/portfolio-edits.py"
# 파일 단위로만 add(폴더째 add -A 하면 아직 커밋 안 한 내 파일까지 들어감). 바뀌지 않은 경로는 add해도 아무 일 없음
git -C "$R" add -- $NEW_FILES doc.html index.html project.html assets/doc.js assets/project-deck.js .claude/CLAUDE.md .claude/launch.json .claude/skills/edit-mode-install
git -C "$R" diff --cached --name-only      # §4에서 보여 준 목록 + 부트스트랩 파일과 같은지 확인
git -C "$R" commit -q -m "Feat: 편집 모드 설치 (edit-mode-install $(git -C "$R" rev-parse --short "$SRC"))" -m "Edit-Mode-Source: $SRC"
git -C "$R" log -1 --format='%h %s'
```

- **`Edit-Mode-Source: <sha>` 줄은 반드시** 남깁니다(다음 실행이 "지난 설치"를 찾는 데 씀). `Co-Authored-By` 같은 줄을 덧붙이려면 **`Edit-Mode-Source` 줄 바로 아래, 빈 줄 없이** 같은 `-m`에 넣습니다(예: `-m "Edit-Mode-Source: $SRC"$'\n'"Co-Authored-By: …"`).
- 사용자가 커밋을 원하지 않으면 커밋하지 않고, 바뀐 파일이 커밋되지 않은 채 남아 있다고 알립니다.
- **push는 하지 않습니다**(사용자가 확인한 뒤 직접).

## 7. 마무리 안내

1. 바뀐 내용 요약: 새로 받은 파일 N · 줄을 넣은 파일 N · 멈춘 묶음(있으면 이유) · `.claude/CLAUDE.md`·`.claude/launch.json` 처리 · 커밋 해시(했으면).
2. 다음 할 일:
   1. 편집 서버를 **이미 켜 두었으면** `.\preview.ps1 stop`으로 끈 뒤 다시 켭니다(예전 서버는 새 저장 주소를 모릅니다).
   2. `preview.cmd`를 더블클릭하거나 PowerShell에서 `.\preview.ps1`을 실행합니다. 세 페이지 오른쪽 아래에 [✎ 편집 모드]가 보이면 설치된 것입니다.
   3. 발표 덱에 노란 "예전 데이터로 자동 구성" 띠가 보이면 편집 모드가 켜지지 않습니다. `/portfolio-build`를 먼저 실행합니다.
   4. 앞으로 `/portfolio-build`·`/portfolio-edit` 뒤에는 `.claude/CLAUDE.md`에 넣은 절대로 두 스크립트(`deck-edits.py apply --all` → `portfolio-edits.py apply --all`)를 실행합니다.
   5. `/portfolio-update`로 템플릿을 업데이트하면 편집 모드 줄이 지워집니다. 업데이트가 `index.html` 등을 "직접 고친 흔적"으로 물으면 **바꾸기**를 고르고, 업데이트를 커밋한 뒤 `/edit-mode-install`을 다시 실행합니다.
   - 장 표지 키워드(장 표지 목록의 장면 아래 낱말 한 줄)는 이 설치에 들어 있지 않습니다. 그 줄은 편집 모드에서 누를 수 없을 뿐, 다른 편집은 그대로 됩니다.

## 멈추는 조건 (모아 보기)

| 조건 | 어디서 | 안내 |
|---|---|---|
| 레포 루트가 다름 / 템플릿 레포가 아님 | §1 | 레포 폴더 확인 |
| 병합·리베이스·체리픽 중 | §1 | 먼저 끝내기 |
| 부트스트랩 파일 말고 커밋 안 한 변경 | §1 | 먼저 커밋하거나 정리 |
| Python 없음 | §1 | Python 설치 |
| fetch 실패 | §2 | 인터넷 연결 확인 |
| 소스에 받을 파일이 없음 | §3 | MountainHS에게 알리기 |
| 기준 줄 0곳·2곳 이상 / 넣을 줄 일부만 있음 | §3·§5 `[멈춤]` | 그 묶음만 건너뛰고 나머지 설치. 그 페이지 편집 모드는 안 켜짐 |
| `.claude/CLAUDE.md` 표지 주석 짝이 안 맞음 | §3·§5 `[멈춤]` | 표지 주석을 직접 정리한 뒤 다시 실행 |
| `.claude/launch.json`을 읽을 수 없음 / 이름 `portfolio-edit`인 다른 구성 | §3·§5 `[멈춤]` | 그 파일만 건너뜀(직접 더하도록 안내) |
| `[확인 필요]` · 금지 경로가 status에 나옴 · `py_compile` 실패 | §5 | 되돌리고(금지 경로) 멈춤, 커밋하지 않음 |

## 지키는 것

- `src/`·`profile.md`·`data/`·`custom/`·`theme/`은 읽기만 합니다. 사용자 문서를 대신 고치지 않습니다(CLAUDE.md 원칙 2).
- 소스 주소는 고정 값만 씁니다.
- 네트워크 접근, 파일 바꾸기, 커밋은 각각 동의를 받은 뒤에만 합니다(질문 1 → 2 → 3). push는 하지 않습니다.
- 템플릿 파일 5개는 `install-lines.py`로만 고칩니다. 기준 줄을 찾지 못하면 추측해서 넣지 않습니다.
