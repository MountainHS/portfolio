---
name: local-preview
description: 로컬 미리보기를 연다 — preview.ps1을 불러 편집 서버(edit-server.py, 127.0.0.1:4600)를 켜고 포트폴리오 문서·발표 덱·상세 문서를 브라우저로 연다. 끄기(stop)·서버만 켜기(server)·report 열기도 같은 명령. 사용자가 /local-preview를 입력하거나 "미리보기 열어 줘", "덱 띄워 줘", "편집 서버 꺼 줘"처럼 로컬 미리보기를 열고 닫아 달라고 할 때 사용.
argument-hint: "[portfolio|deck|doc|all|report|server|stop] [프로젝트 폴더]"
---

# /local-preview — 로컬 미리보기 열기

인자: `$ARGUMENTS` — `preview.ps1`과 같은 순서입니다(페이지 종류, 프로젝트 폴더). 페이지 종류가 비면 **`portfolio`(포트폴리오 문서만)**, 프로젝트 폴더가 비면 `src/`의 첫 폴더.
발표 덱·상세 문서는 사용자가 `deck`·`doc`·`all`을 적었을 때만 엽니다.

이 명령은 레포 루트의 **`preview.ps1`을 그대로 부릅니다.** 서버 켜기·포트 검사·끄기를 여기서 새로 짜지 않습니다.
`preview.ps1`이 바뀌면 이 명령도 그대로 따라갑니다.

> 이름이 `portfolio-`로 시작하지 않는 이유: `/portfolio-update`는 `.claude/skills/portfolio-*/` 폴더를 템플릿과 비교해 교체·삭제 후보로 올립니다. 이 명령은 이 포크 전용이라 그 목록 밖에 둡니다.

## 인자 읽기

| 사용자가 친 것 | 부를 명령 |
|---|---|
| (없음) | `preview.ps1 portfolio` (포트폴리오 문서만, 첫 프로젝트) |
| `5-hive-purge` (페이지 종류 없이 폴더만) | `preview.ps1 portfolio 5-hive-purge` (포트폴리오 문서만) |
| `deck 5-hive-purge` | `preview.ps1 deck 5-hive-purge` |
| `all 5-hive-purge` | `preview.ps1 all 5-hive-purge` (세 페이지) |
| `stop` | `preview.ps1 stop` |

- 페이지 종류: `portfolio`(기본) · `deck` · `doc` · `all`(세 페이지) · `report`(루트 `report.md` + `report/<폴더>.md`) · `server`(서버만) · `stop`(4600 포트 서버 끄기).
- 인자가 없으면 `preview.ps1`에 `portfolio`를 붙여 부릅니다. `preview.ps1`만 부르면 그 기본값 `all`(더블클릭 `preview.cmd`와 같음)로 세 페이지가 열리므로, 페이지 종류를 빼고 부르지 않습니다.
- 첫 인자가 위 종류가 아니고 `src/<그 이름>/` 폴더가 있으면 프로젝트 폴더로 보고 `portfolio`를 앞에 붙입니다. 명령 패널의 "미리보기 띄우기" 버튼(`/local-preview <고른 폴더>`)도 이 경우라 포트폴리오 문서만 엽니다.
- 둘 다 아니면 실행하지 않고, 쓸 수 있는 종류와 `src/`의 폴더 목록(`_`로 시작하는 폴더 제외)을 보여 줍니다.

## 실행

Bash 도구로 한 번 부릅니다. `R`은 이 파일(`.claude/skills/local-preview/SKILL.md`)이 든 레포 루트입니다.

```bash
R="<레포 절대 경로>"
powershell -NoProfile -ExecutionPolicy Bypass -File "$R/preview.ps1" <페이지 종류> <프로젝트 폴더>
```

- `preview.ps1`은 서버를 따로 띄운 프로세스(`Start-Process`, 작업 표시줄에 최소화된 창)로 켜고 바로 끝납니다. 그래서 대화를 막지 않습니다. `run_in_background`는 쓰지 않습니다.
- 서버가 이미 켜져 있으면 새로 켜지 않고 페이지만 엽니다.
- Windows 전용입니다(`preview.ps1`이 PowerShell 스크립트). PowerShell이 없는 환경이면 실행하지 않고 `python edit-server.py`를 직접 켜라고 안내합니다.

## 결과 알리기

출력의 줄을 그대로 근거로 짧게 알립니다.

| 출력 | 알릴 것 |
|---|---|
| `편집 서버 켜짐` 또는 `이미 켜져 있습니다` + `열기: <주소>` | 연 주소. 페이지 오른쪽 아래 [✎ 편집 모드]로 글을 고칠 수 있음 |
| `포트 4600 를 다른 서버(예: python -m http.server)가 쓰고 있어 …` 경고 | 페이지는 열렸지만 편집 모드가 꺼져 있음. **"그 서버를 끄고 편집 서버로 다시 켤까요?"라고 묻고**, 동의하면 `stop` → 같은 인자로 다시 실행 |
| `편집 서버가 6초 안에 켜지지 않았습니다` | 오류 줄 그대로. `python edit-server.py`를 직접 실행해 첫 오류 줄을 확인 |
| `python(또는 py)을 찾을 수 없습니다` | Python 3 설치(설치 화면의 'Add python.exe to PATH') |
| `포트 4600 서버(프로세스 N)를 껐습니다` / `켜진 서버가 없습니다` | 그대로 |

- 서버 창을 닫으면 서버도 꺼집니다. 끌 때는 `/local-preview stop`.
- 편집 서버를 이 포크의 새 기능 전에 켜 두었으면 저장 때 "편집 서버가 이 기능 전의 것입니다"가 뜹니다. `/local-preview stop` 뒤 다시 켭니다.

## 지키는 것

- `preview.ps1`·`edit-server.py`는 읽기만 합니다(이 명령은 고치지 않음).
- 4600 포트의 다른 서버는 사용자가 동의했을 때만 끕니다(`stop`을 직접 입력한 경우는 동의한 것).
