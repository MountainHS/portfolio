# 템플릿 업데이트 받기

| | |
|---|---|
| **명령** | `/portfolio-update [되돌리기]` · ✋ **직접 입력할 때만** |
| **언제 쓰나** | 템플릿에 새 기능이 생겼을 때 |
| **바뀌는 것** | 렌더러 · 명령 · 판정 기준 · 문서, 그리고 업데이트 커밋 1개 (아래 표) |
| **되돌리기** | `/portfolio-update 되돌리기` |

템플릿에 새 기능이 생기면 `/portfolio-update`로 받습니다. Fork로 만든 레포든 파일을 복사해 만든 레포든 똑같이 동작합니다.

```text
/portfolio-update
```

| 바뀌는 것 | 그대로 두는 것 |
|---|---|
| 렌더러(`index.html`·`project.html`·`doc.html`·`assets/`), Claude 명령(`.claude/skills/portfolio-*`), 판정 기준(`format/`), 문서(`docs/`), `CLAUDE.md`, `.nojekyll`. `.gitignore`·`.claude/launch.json`은 새 줄만 더함 | `src/`, `profile.md`, `data/`, `custom/`, `report.md`, `theme/layout.json`, `theme/theme.css`·`theme/strings.tsv`(없을 때만 새로 만듦) |

- 템플릿(`https://github.com/krKooHoo/portfolio-template`)에서 받기 전, 파일을 바꾸기 전, 커밋하기 전에 **각각 동의를 받습니다**. 무엇이 바뀌는지 먼저 보여 줍니다.
- 업데이트는 커밋 1개로 남고 **push는 하지 않습니다**. **되돌리기**: `/portfolio-update 되돌리기`.
- 끝나면 `/portfolio-build`를 다시 실행하고, 미리보기로 확인한 뒤 push하세요. 다시 빌드하기 전에는 슬라이드가 노란 **예전 데이터로 자동 구성** 띠와 함께 보입니다.

## 더 알아 둘 것

- 커밋하지 않은 변경이 있으면 먼저 커밋하라고 하고 멈춥니다.
- 이 PC의 git 설정(`url.….insteadOf`)이 템플릿 주소를 다른 곳으로 바꾸고 있으면 받기 전에 멈춥니다(다른 곳의 코드가 렌더러로 들어오지 않게). 설정을 확인하거나 강사에게 문의하세요. GitHub 로그인 때문에 같은 저장소를 SSH·토큰 주소로 바꾸는 설정은 그대로 진행됩니다.
- README는 직접 고치지 않았으면 새 것으로 바꾸고, 고쳤으면 유지할지 묻습니다(기본은 유지). 렌더러나 명령 파일을 직접 고친 흔적이 있으면 파일마다 바꿀지 남길지 묻습니다.
  한 번 남기기로 한 파일은 템플릿에서 그 파일이 다시 바뀌기 전까지 다음 업데이트에서 다시 묻지 않습니다.
- 예전 템플릿에 있던 `GUIDE.md`(전체 흐름 가이드)는 `docs/2-workflow.md`로 옮겨졌으므로 지웁니다. 직접 고친 흔적이 있으면 지울지 묻습니다.
- 문구 표에 새 문구 자리가 생겼거나 기본 문구가 바뀌었거나 `profile.md` 안내가 바뀌었으면 알려만 줍니다(내 파일은 고치지 않음).
  기본 문구가 바뀐 줄은 다음 빌드 보고에 `default-edited`로 나오지만 그대로 둬도 됩니다.

## `docs/` 폴더가 없다면

예전 템플릿에 들어 있던 `/portfolio-update`는 `docs/`(이 안내 문서들)를 받지 않았습니다. 그 명령으로 한 번 받은 레포에는 새 `CLAUDE.md`·명령·렌더러는 들어오고 `docs/`만 없습니다.
README의 `docs/…` 링크가 열리지 않으면 `/portfolio-update`를 **한 번 더** 입력하세요. 두 번째에는 새 명령이 실행되어 `docs/`가 들어옵니다(내 문서·데이터는 그대로). Claude도 이 상태를 알아채면 같은 안내를 합니다.
처음부터 한 번에 받으려면 아래 방법으로 명령을 먼저 새로 받은 뒤 `/portfolio-update`를 입력합니다.

## 내 레포에 `/portfolio-update` 명령이 없다면

레포 폴더의 터미널에서 먼저 받을 곳을 확인합니다(접속하지 않고 주소만 보여 줌).

```bash
git ls-remote --get-url https://github.com/krKooHoo/portfolio-template
```

`https://github.com/krKooHoo/portfolio-template`(또는 `git@github.com:krKooHoo/portfolio-template`)이 나오면 아래 두 줄을 **한 번만** 실행해 명령부터 받습니다. 다른 주소가 나오면 이 PC의 git 설정이 주소를 바꾸고 있는 것이니 실행하지 말고 강사에게 문의하세요.

```bash
git fetch https://github.com/krKooHoo/portfolio-template main
```

```bash
git checkout FETCH_HEAD -- .claude/skills/portfolio-update
```

그다음 Claude Code에서 `/portfolio-update`를 입력합니다(명령 목록에 안 보이면 Claude Code를 새로 시작하세요).

Fork한 레포를 GitHub의 **Sync fork**로 받다가 `data/`에서 충돌이 나면 **내 것을 유지**하면 됩니다. 템플릿의 샘플 빌드 결과와 내 빌드 결과가 같은 파일이라서 생기는 충돌이고, `data/`는 `/portfolio-build`가 다시 만듭니다.
