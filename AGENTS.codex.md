# Codex에서 Claude 스킬 사용

- 요청에 해당하는 스킬을 `.claude/skills/<이름>/SKILL.md`에서 먼저 찾고, 없으면 사용자 홈의 `.claude/skills/<이름>/SKILL.md`를 확인합니다. `/<이름> <인자>` 요청은 이름으로 파일을 찾고 뒤의 입력을 `$ARGUMENTS`에 대응시킵니다. 스킬의 상대 링크는 스킬 파일 기준으로 읽고, 명령의 작업 디렉터리는 스킬에 따릅니다. 직접 호출이나 변경 전 확인이 명시된 단계는 그 조건을 따릅니다.
- 작업에 관련된 루트 `CLAUDE.md`, `.claude/CLAUDE.md`, 그리고 `.claude/rules/*.md`의 경로별 규칙을 함께 읽습니다. 스킬과 스크립트는 기존 경로에서 사용합니다.

## 도구 대응

| Claude에서 쓰는 기능 | Codex에서의 실행 경로 |
| --- | --- |
| Skill, Read, Glob, Grep, Edit, Write, Bash | 스킬 파일 직접 읽기, 파일 검색과 편집 도구, PowerShell로 실행. 저장소 스크립트와 인자는 스킬의 지침을 따릅니다. |
| AskUserQuestion, Agent | Codex의 사용자 입력과 협업 도구를 현재 세션의 사용 조건에 맞춰 사용합니다. |
| Claude 브라우저, Claude in Chrome | Codex 브라우저 도구로 열고 검사합니다. `portfolio-layout`은 스킬 폴더의 `measure-layout.js`를 읽고 브라우저 도구가 지원하는 페이지 검사 범위에서 측정합니다. 측정이 불가능하면 스킬의 스크린샷 경로를 사용합니다. |
| portfolio-panel | 패널 소스는 `tools/portfolio-panel/hooks/register.tsx`에 있습니다. 패널 버튼에 연결된 스킬 이름과 프로젝트 인자를 가져와 실행합니다. 설치된 Claude 패널은 사용자 홈의 `.claude/plugins/cache/portfolio-tools/portfolio-panel/0.1.0`에서 확인했습니다. |
| cp-reactivememory-mcp-server | 필요한 작업에서 Codex에 노출된 ReactiveMemory 도구를 찾습니다. |

## 훅에 연결된 작업

`.claude/settings.json`의 훅이 담당하는 다음 단계를 Codex 작업 중에도 적용합니다.

- `src/<폴더>/*.md`를 수정하면 `tools/voice-lint.py <수정한 파일>`을 실행하고 변경 부분에 해당하는 후보를 확인합니다.
- `video/*-subtitles.tsv`를 수정하면 결과를 보고하기 전에 같은 턴에서 `video-subtitle-revise`를 적용합니다.
- 지원서 작성 요청은 `application-draft`와 `.claude/rules/application-writing.md`를 먼저 읽습니다. 훅의 감지 문구는 `.claude/hooks/application-draft-trigger.py`에 있습니다.

## Windows에서 Python 실행

`python`과 `py` 별칭을 실행할 수 없으면 사용자 홈 아래 Codex 런타임을 확인합니다. 이 환경에서는 다음 실행 파일을 검증했습니다.

```powershell
$portfolioPython = "$env:USERPROFILE/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe"
& $portfolioPython --version
& $portfolioPython tools/voice-lint.py <검사할 Markdown 경로>
```

`preview.ps1`처럼 자식 스크립트가 이름으로 Python을 찾으면 실행하는 PowerShell 프로세스의 PATH 앞에만 런타임 폴더를 추가합니다. 미리보기 명령과 인자는 `local-preview` 스킬을 따릅니다.
