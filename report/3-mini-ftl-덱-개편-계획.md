# Mini FTL 발표 덱 개편 계획 — 2026-11-25

> 적용 대기. 이 문서는 조사와 제안만 기록합니다. `진행해` 승인 뒤에도 아래 불일치를 먼저 해결하고 덱을 수정합니다.

## 1. 기준과 작업 전 상태

- 원본 저장소 `C:/Users/hsgood01/repos/RawBlog/portfolio-test`: `main` / `450b08027299de677122ceba50c27e1cebee3398` (`origin/main`과 같은 로컬 최신 커밋). 조사 시작 시 수정은 `src/5-hive-purge/deck-edits.json` 1건입니다. Mini FTL 이미지와 `src/3-mini-ftl/기술문서.md`, Hive Purge 기술문서는 Git 수정 표시가 없었습니다. 기존 WGO 워크트리도 별개입니다. 원본의 Hive Purge 변경을 읽기만 했고 보존합니다.
- 독립 작업 위치: `report/worktrees/mini-ftl-deck-plan` / `codex/mini-ftl-deck-plan` / 같은 HEAD. 현재 덱·프로젝트 파일은 아직 수정하지 않았습니다. 이 워크트리의 `report/`는 Git 제외 경로이며 계획과 검사 기록을 둡니다.
- 조사 중 원본 작업 트리에 Hive Purge 데이터·편집 설정, 공용 렌더러·스키마·검사기 등의 추가 수정이 나타났습니다. 이 작업이 만든 변경은 아니며 독립 워크트리로 가져오거나 덮지 않습니다. 적용 전 원본/독립 워크트리 상태를 다시 기록합니다.
- 근거: [기술문서](../src/3-mini-ftl/기술문서.md), `src/3-mini-ftl/code/*.cs` 보관본, `data/projects/3-mini-ftl.json`, `src/3-mini-ftl/deck-edits.json`, `.claude/rules/fork-features.md`, `/portfolio-build`, `/portfolio-edit`, WGO의 `deck-edits.json` 배치·링크 설정. Mini FTL `deck-edits.json`은 현재 `pin: []`, `hide: []`, `editModePortfolio` 1건입니다. 그 포트폴리오 문구 설정은 유지합니다.
- 실제 미리보기 `http://127.0.0.1:4601/project.html?p=3-mini-ftl`를 화면과 인쇄 미디어에서 렌더했습니다. [기준 검사 JSON](3-mini-ftl-baseline.json): 49장, 공통 검사 99건 통과, 콘솔 오류 0, `low-fill` 참고 25장, `codeReview` 수집 5장. 장면 37·20·22·27·10·47·48은 화면도 직접 열어 확인했습니다. 자동 통과는 코드 설명의 의미 일치나 이력의 사실 여부를 보증하지 않습니다.
- 49장 = 데이터 장면 41 + 렌더러 장면 8(표지·목차·장 표지 4·회고·맺음). 아래 번호는 현재 미리보기 기준입니다.

### 구현과 기술문서의 충돌 — 우선 처리

`기술문서.md` §3.1은 시설 피해 시 중복 `ReducePower()`를 제거해 수정했다고 기술합니다. 그러나 보관된 구현의 `FacilityController.cs:82-86`은 `ReducePower(overflowAmount)` 뒤 `ShipPowerController.DeallocatePower(...)`를 호출하고, 후자는 `ShipPowerController.cs:79-83`에서 다시 `facility.ReducePower(recoverAmount)`를 호출합니다. 현재 코드 보관본만으로는 **수정 완료를 확인할 수 없습니다**. 따라서 `d-power-cost`와 회고에는 이 해결을 달성 결과로 쓰지 않습니다. 적용 단계에서는 이 코드 보관본을 기준으로 기술문서의 해당 완료 주장과 덱 문구를 정정하고, 실제 게임 빌드나 다른 소스에서 수정됐다는 근거가 확인되면 그때만 완료로 되돌립니다. 런타임 C# 코드는 이번 덱 개편 범위에서 수정하지 않습니다.

## 2. 현재 발표 흐름과 장면별 점검

흐름은 **개요(3–10) → 시스템 구조(11–17) → 전력·초기화·승무원·경험 사례(18–40) → 결과·짧은 문제 해결·회고(41–49)**입니다. 개요에서 원작과 세 조작, 구조에서 다섯 하위 시스템, 사례에서 설계의 비용과 검증의 기준을 설명하는 큰 순서는 유지합니다. 표의 ‘유지’는 근거·배치상 큰 문제를 발견하지 못했다는 뜻이며, 적용 후에도 인쇄 화면을 점검합니다.

| 현재 쪽 · ID | 주요 문제와 최소 개편 방향 |
|---|---|
| 1 `cover` | 유지. 표지 이미지·프로젝트명 확인. |
| 2 `toc` | 유지. 병합 후 항목·쪽 번호 자동 갱신 확인. |
| 3 `ch-overview` | 유지. 장면 키워드 목록이 병합 후에도 순서대로 보이는지 확인. |
| 4 `d-intro` | 유지. 본문은 1인 제작·전투 범위, 대표 화면은 문서 머리 그림을 참조. |
| 5 `d-ftl-intro` | 원작 화면 하나에 설명 4개. 그림을 왼쪽, 원작 화면의 내 함선·적 함선·전력 바와 Mini FTL의 전투 범위 카드를 오른쪽 세로로 대응. |
| 6 `d-why` | 글 채움 39%. 재현 가능성 검증과 객체 설계 학습이라는 두 목적은 구별해 균형 있게 중앙에 배치. 기술문서 §1.2의 두 목표만 사용. |
| 7 `d-experience` | 글 채움 42%. §1.3의 피해 최소화·약점 공략·전력 배분을 같은 크기의 세 카드로 중앙 배치. 미촬영 플레이 화면을 임의로 추가하지 않음. |
| 8 `d-loop` | 다이어그램 채움 51%. 아래의 승패 조건을 같은 메시지로 묶고 전투↔상점 흐름 아래에 승리·패배 조건을 배치. |
| 9 `d-win-lose` | 표 단독 채움 27%. 8쪽에 통합하고 옛 주소를 `d-loop`로 연결. ‘전투 4번’은 문서 §1.4와 `GameManager.cs:455-478` 범위 재확인. |
| 10 `d-three-decisions` | 그림 3개 크기·높이가 달라 설명 관계가 약함. 전력·무기·승무원 그림을 각각 같은 열 위, §1.5 제목과 행동 설명을 그 그림 바로 아래에 배치. |
| 11 `ch-structure` | 유지. |
| 12 `d-system-map` | 다섯 시스템 화살표의 독해가 어렵다. 전투 진입/결과는 GameManager–Ship, 전력은 Ship–Power, 조준은 Ship–Combat, 이동은 Ship–Crew로 카드와 같은 위치에 맞춤. 실제 호출 근거를 대표 줄로 확인. |
| 13 `d-game-flow` | `GameContext` 상태 보관과 `SceneBinder` 복원을 말하지만 다이어그램만 보임. 좌측 진행 도식, 우측 `GameManager.OnSceneLoaded`와 `SceneBinder.BindSceneObjectsWithEnvironment`의 핵심 실행 줄. |
| 14 `d-ship` | 상속·방 피해 공유를 말하는 실행 줄이 없음. 그림의 Player/Enemy 아래 해당 상속 선언, 공통 Ship 아래 `TakeDamageToRoom` 호출 근거를 짧게 붙임. |
| 15 `d-crew-system` | 다이어그램 채움 54%, 소유·명령 관계가 멀다. CrewSystem→CrewController 이동, CrewController↔Room 소속/시설을 그림 가까운 카드와 실제 줄로 짝지음. |
| 16 `d-power-system` | 소유 객체와 배분 담당을 그림만 설명. Reactor/Facility의 보유값과 ShipPowerController의 배분 호출을 같은 위치에 맞추고 각 코드 근거를 확인. |
| 17 `d-combat-system` | 다이어그램 채움 56%, 충돌/도착이 한 화살표처럼 읽힘. 실드 충돌과 방 도착을 별도 단계로 보이고 `ProjectileController`의 두 호출 및 `CombatSystem` 판정 줄을 대응. |
| 18 `ch-cases` | 유지. 병합 후 22개였던 사례 탭과 목차가 줄어드는지 확인. |
| 19 `d-power-choice` | 비교표 단독 채움 36%. §3.1의 불변 조건(반응로 가용량·시설 할당량·피해 한계)과 ‘미래 이온 피해 고려’라는 선택 근거를 표 위·아래 짧게 배치. 표의 대안/장점/부담은 행마다 같은 위치 유지. |
| 20 `d-power-code` | 화면 코드가 할당 검사와 세 값 변경만 보여 주는데 카드 1·2는 Reactor/Facility의 모든 상태 관리까지 주장. 카드를 표시된 검사→할당값 갱신→시설·반응로 갱신으로 좁히고 1·2·3 번호를 해당 실행 줄에 연결. |
| 21 `d-power-cost` | 채움 48%, 중복 감소를 ‘수정했다’는 문서·코드 충돌. 피해 경로 코드를 보이고 **보관 코드에서 중복 경로 확인**으로 판정. 23쪽의 교훈을 이 장면 하단으로 옮겨 현재 확인/향후 기준을 구분. |
| 22 `d-power-ui` | 그림·코드 위에 문제/원인/해결/교훈 4카드가 가로로 놓여 관련 줄과 멂. UI 그림 왼쪽, 오른쪽에 상태 조건 코드와 ‘파괴 우선→피해→전력 0→정상’ 설명을 위아래로 쌓음. 코드의 상태 우선순위는 구현과 일치. 2~3시간은 문서의 회고 수치일 뿐 추가 측정치로 쓰지 않음. |
| 23 `d-power-lessons` | 글 채움 42%, 21·22쪽과 반복. `d-power-cost`로 통합하고 옛 주소 연결. |
| 24 `d-init-design` | 씬 유지와 씬 참조가 한 화면에서 혼합. 왼쪽 전투→상점 그림, 오른쪽에 GameContext 지속 상태와 CombatSystem 씬 참조를 분리해 표기. `DontDestroyOnLoad` 구현 줄 확인. |
| 25 `d-init-order` | 시퀀스만으로 `ships` 준비 조건이 추상적. 26쪽 코드를 통합해 `SceneBinder`의 초기화 호출→`CombatSystem.ships` 대입→`EnemyController.Start` 조회 순서를 대응. |
| 26 `d-init-code` | 코드 채움 54%, 현재 발췌에는 핵심 `ships` 대입 줄이 빠짐. 25쪽으로 통합하고 옛 주소 연결. |
| 27 `d-init-fix` | 두 코드 블록은 해결 1·2를 뒷받침하지만 ‘Awake에서 이동’이라는 과거 변경 자체는 최종 코드만으로 입증하지 못함. 1번은 실행 순서 선언, 2번은 현재 `Start`의 대입 줄, 3번은 문서의 재진입 확인 결과로 구분. |
| 28 `d-init-limit` | 글 채움 40%. 30쪽 교훈을 이 화면 아래로 옮겨 ‘당시 해결’과 ‘앞으로 명시적 초기화’의 시간대를 분리. |
| 29 `d-combat-lifetime` | 글 채움 37%, 참조 재연결 코드를 설명하지만 없음. 씬 로드→SceneBinder→`InitCombatSystem` 실행 줄을 왼쪽, 지속 객체와 새 씬 참조의 역할 카드를 오른쪽에 쌓음. |
| 30 `d-init-lessons` | 글 채움 36%, 28쪽과 반복. `d-init-limit`로 통합하고 옛 주소 연결. |
| 31 `d-crew-room` | 작은 원본 그림(342×116)과 카드 3개, 채움 50%. 그림을 왼쪽 원본 비율로, `CurrentRoom`·`Room.Crews` 및 상호 작용 카드와 보유 필드를 오른쪽 세로로 대응. 무리한 확대는 하지 않음. |
| 32 `d-crew-paths` | 이동/사망 두 발췌에 번호 강조 없음. 이동의 `ReduceCrew`→빈 방 정리, 사망의 `ReduceCrew`만 표시를 1·2 카드에 연결. |
| 33 `d-crew-bugs` | 글 채움 44%. 34쪽 상태 그림과 묶어 첫째 ‘후처리 누락 가능성’과 둘째 ‘이동 중 소속 선반영’을 서로 다른 행에 배치. `SetTargetRoom`·`MoveToTargetRoom` 근거를 표시. |
| 34 `d-crew-state` | 상태 그림의 전이와 두 불일치가 섞여 있음. 33쪽으로 통합하고 옛 주소 연결. 그림의 Idle/Repair/Assisting은 `UpdateCrewStatus`, Moving은 `SetTargetRoom`·`MoveToTargetRoom`에 대응. |
| 35 `d-crew-redo` | 글 채움 40%. 현재 적용한 해결처럼 보이지 않게 ‘다시 구현한다면’으로 유지하고 36쪽 교훈과 통합. |
| 36 `d-crew-lessons` | 글 채움 26%, 35쪽과 같은 문제. `d-crew-redo`로 통합하고 옛 주소 연결. |
| 37 `d-loop-goal` | 화면에서 본문 한 줄만 중앙에 떨어져 있음(채움 12%). 38쪽의 실제 범위·부족한 선택지와 묶어 목표/확인한 결과 두 열로 구성. 원작 화재·산소·강습은 **구현한 기능이 아니라 원작과의 차이**로 표시. |
| 38 `d-loop-result` | 글 채움 26%, 37쪽과 한 논지. `d-loop-goal`로 통합하고 옛 주소 연결. 플레이 경험의 유사성은 검증되지 않았다고 명시. |
| 39 `d-scope-misjudge` | 글 채움 37%. 40쪽의 이후 기준과 좌우 패널로 묶음. 7단계·약 3주·4일·6일은 기술문서 §3.4의 회고값이며 작업 로그 재측정 수치로 표현하지 않음. |
| 40 `d-loop-next` | 글 채움 37%, 37–39쪽 결과와 겹침. `d-scope-misjudge`의 오른쪽 ‘다음 기준’ 패널로 통합하고 옛 주소 연결. |
| 41 `ch-results` | 유지. |
| 42 `d-targeting` | 글 채움 42%, `ITargeting` 주입을 말하지만 코드 없음. 왼쪽 `EnemyController.Start`→`WeaponSystem.SetWeaponsTargetingSystem`, 오른쪽 실제 규칙 하나/추적 비용/다음 기준. |
| 43 `d-damage-timing` | 그림은 조준 화면인데 본문은 도착 시점 판정. 실드 충돌과 목표 도착에서 호출하는 줄을 넣고, 조준 그림은 왼쪽 보조 자료, 오른쪽에 판정·테스트 카드를 세로로. 통합 테스트는 **필요한 검증**이며 수행 결과로 쓰지 않음. |
| 44 `d-ts-dead-coroutine` | 글 채움 44%, 해결 플래그 코드 없음. `ShipController.CheckGameOver` 가드와 `DeadCoroutine` 플래그 대입을 보이고 문제/해결/문서상 확인 결과를 중앙 균형 배치. |
| 45 `d-ts-projectile` | 글 채움 44%, 회전 수정 코드 없음. 46쪽 조준 UI 해제 사례와 좌우 2패널 통합. 왼쪽은 `CombatSystem.MoveToEnemyArea`의 `targetPos - spawnPos` 회전 줄. |
| 46 `d-ts-cursor` | 글 채움 44%, UI 정리와 Esc 순서 코드 없음. 45쪽 오른쪽 패널에 `WeaponSystem.ResetSelectedWeapon`의 커서·목표 초기화와 `PlayerController.Update`의 Esc 선처리 표시. 옛 주소는 `d-ts-projectile`로 연결. |
| 47 `d-assumptions` | 4행 표는 전력·초기화·승무원·핵심 경험 순서로 읽힘. 각 행의 ‘초기 가정 → 실제 확인 → 이후 기준’을 같은 열/위치에 고정하고 긴 셀은 문장 경계로 줄바꿈. 특히 중복 전력 해결 여부는 표의 ‘추적·검증 비용’까지만 주장. |
| 48 `retro` | ‘달성’ 열에 **검증 못 한 결과**와 작업 습관이 섞임. 확인 결과는 37·39쪽으로, 이미 적용한 해결은 각 사례로, 회고에는 ‘아쉬운 점 ↔ 같은 문제의 이후 계획’ 4쌍을 표시. 4번째 전력 UI 경계값 쌍은 §4.1, 앞 3쌍은 §4.4와 대조. |
| 49 `end` | 유지. 최종 페이지 수 갱신 확인. |

## 3. 코드·그림·표의 의미 대응 검수 계획

적용 전에 기술문서의 코드 블록과 보관 코드가 같은지 대조합니다. 코드가 없는 구현 설명 장면은 원문에 짧은 **실제 실행 줄**을 추가한 뒤 그 블록을 참조하거나, 주장을 표시 가능한 범위로 좁힙니다. 한 블록은 가급적 20줄 이내로 발췌해 자동 분할을 피합니다. 아래는 필수 대조의 핵심이며, 최종 보고서에는 **모든 해당 장면의 ID·주장·화면 근거 줄·판정**을 남깁니다.

| 장면 ID(현재 쪽) | 주장 → 필요한 실행 근거 | 현재 판정/처리 |
|---|---|---|
| `d-game-flow`(13), `d-init-design`(24), `d-combat-lifetime`(29) | 씬 로드·상태 복원·재연결 → `GameManager.cs:406-443`, `SceneBinder.cs:24-63`, `CombatSystem.cs:48-65`, `SingletonBase.cs`의 유지 처리 | 코드 없는 설명. 해당 호출만 발췌. |
| `d-ship`(14), `d-crew-system`(15), `d-power-system`(16), `d-combat-system`(17), `d-crew-room`(31) | 다이어그램의 상속·상태 소유·전달·판정 → 해당 `ShipController`, `CrewSystem`, `CrewController`, `Room`, `ShipPowerController`, `ProjectileController`, `CombatSystem` 선언/호출 | 구조 그림만으로 실행 동작까지 단정하지 않음. 도식 각 요소 옆에 같은 이름의 코드 줄 대응. |
| `d-power-code`(20) | 배분 검사·할당·시설/반응로 갱신 → `ShipPowerController.cs:41-51` | 현재 1·2번 카드의 전 범위 상태 소유 주장은 발췌 밖. 카드 수정 및 1–3 번호 강조. |
| `d-power-cost`(21) | 피해 시 중복 감소 → `FacilityController.cs:82-86` + `ShipPowerController.cs:79-83` | ‘수정 완료’는 현재 코드와 충돌. 원문/덱 정정 전까지 미확인. |
| `d-power-ui`(22) | 파괴/피해/비활성/정상 우선 → `FacilityController.cs:115-132` | 설명과 최종 코드 부합. 조건 분기와 해결 카드 대조. |
| `d-init-order`(25), `d-init-code`(26) | 적의 조회 전에 `ships`가 채워짐 → `SceneBinder.cs:61-63`, `CombatSystem.cs:48-65,181-185`, `EnemyController.cs:9-13` | 현재 발췌의 `ships` 대입 누락. 통합 화면에서 표시. |
| `d-init-fix`(27) | 실행 순서 지정, 현재 Start 대입 → `SingletonBase.cs:5`, `ShipController.cs`의 `Start` | 과거 Awake→Start 이동 사실은 최종 스냅샷만으로 검증 불가. 문서 이력 주장으로 분리. |
| `d-crew-paths`(32), `d-crew-bugs`(33), `d-crew-state`(34) | 이동의 퇴장 정리와 사망 경로 차이, 이동 전 소속 갱신 → `CrewController.cs:45-58,98-132,154-170`, `CrewSystem.cs:119-132` | 발췌 2개에 각 카드 번호. 후처리 누락은 소스 검토상 가능성, 위치/소속 어긋남은 문서상 겪은 버그로 구별. |
| `d-targeting`(42) | 랜덤 Targeter 생성·주입 → `EnemyController.cs:9-13`, `WeaponSystem.cs:41-45`, `RandomTargetingController.cs` | 현재 코드 없음. 실제 규칙이 한 개인지 파일/호출 확인. |
| `d-damage-timing`(43) | 실드 충돌/방 도착 시 현재 판정 → `ProjectileController.cs:76-85,121-132`, `CombatSystem.cs:137-173` | 현재 코드 없음. 두 사건의 줄과 설명을 각각 짝지음. |
| `d-ts-dead-coroutine`(44) | 중복 실행 방지 → `ShipController.cs:388-393,434-461` | 현재 코드 없음. 가드·플래그 설정을 함께 표시. |
| `d-ts-projectile`(45), `d-ts-cursor`(46) | 목표 방향 회전, 선택 해제 시 UI/목표 복구, Esc 선처리 → `CombatSystem.cs:120-135`, `WeaponSystem.cs:81-94`, `PlayerController.cs:41-51` | 현재 코드 없음. 병합한 두 패널에서 각 문제/코드가 같은 열에 오게 함. |

- 다이어그램 대조 예: 25쪽 시퀀스의 `SceneBinder → CombatSystem.InitCombatSystem`은 `SceneBinder.cs:61-63`, `ships 설정`은 `CombatSystem.cs:63-65`, `EnemyController → GetPlayerRooms`는 `EnemyController.cs:12` 및 `CombatSystem.cs:181-185`입니다. 화살표의 시점은 코드와 Unity 이벤트 순서를 함께 확인하고, 코드만으로 증명되지 않는 역사적 실행 순서는 확정 문장으로 만들지 않습니다.
- 표 대조 예: 47쪽 1행은 전력 분리의 예상 이점·관찰한 추적 비용·앞으로의 분리 기준, 2행은 초기화 순서·씬 참조 수명·명시적 조립 기준입니다. 열마다 같은 종류의 정보가 오도록 유지하고 원문 셀에 `<br>` 같은 문장 경계만 추가할지 실제 렌더를 보고 결정합니다. 48쪽 회고의 각 아쉬운 점과 계획도 같은 문제끼리 짝지어 원문 §4.1·4.4와 대조합니다.
- 수치: ‘전투 4번’은 코드 경로까지 확인합니다. ‘약 3주/4일/6일’은 기술문서의 개인 회고이며 비교 기준이나 시간 기록을 새로 만든 수치가 아닙니다. ‘2~3시간’도 같은 한계를 명시합니다. 플레이 경험의 유사성은 측정·검증 결과가 없으므로 성과 수치나 성공 판정으로 쓰지 않습니다.

## 4. 적용 순서·예상 규모·파일

1. §3.1 중복 감소 완료 주장과 회고의 사실 관계부터 보관 코드에 맞춥니다. 코드 블록이 빠진 기술 설명에는 실제 소스의 짧은 발췌를 원문에 넣고, 기존 Mini FTL 이미지 내용과 경로는 보존합니다. 기술문서를 바꾸면 `tools/voice-lint.py`로 변경 문장을 검사합니다.
2. `/portfolio-build 3-mini-ftl` 방식으로 대상 데이터만 원문과 맞춘 뒤, `/portfolio-edit`의 `pin`·`hide --into` 절차로 기존 key·원문 참조를 유지하며 배치·발췌·병합을 고정합니다. `deck-edits.json`의 `editModePortfolio` 기존 항목은 그대로 보존합니다. 프로젝트 범위의 빌드 뒤 필수 `deck-edits.py apply --all` → `portfolio-edits.py apply --all`은 독립 워크트리에서 실행하고 다른 프로젝트의 생성물 변경이 생기면 원인과 diff를 확인해 대상 범위만 남깁니다. 원본 작업 트리의 Hive Purge 변경에는 접근하지 않습니다.
3. 병합 9건: `d-win-lose → d-loop`, `d-power-lessons → d-power-cost`, `d-init-code → d-init-order`, `d-init-lessons → d-init-limit`, `d-crew-state → d-crew-bugs`, `d-crew-lessons → d-crew-redo`, `d-loop-result → d-loop-goal`, `d-loop-next → d-scope-misjudge`, `d-ts-cursor → d-ts-projectile`. `hide.into` 별칭으로 옛 `#s=d-…` 링크가 남긴 장면으로 이동하게 합니다. 다른 기존 key는 유지합니다.
4. **예상 40장(49 − 9)**. 코드가 20줄 초과로 자동 분할되거나 표가 인쇄에서 나뉘면 실제 수가 늘 수 있습니다. 발췌를 줄여 먼저 40장을 목표로 하되, 가독성 때문에 필요한 장면을 억지로 삭제하지 않고 실측 수를 보고합니다.
5. 예상 수정 파일: `src/3-mini-ftl/기술문서.md`(사실 충돌 및 필요한 코드 원문), `src/3-mini-ftl/deck-edits.json`(기존 설정에 pin/hide 추가), `data/projects/3-mini-ftl.json`(반영된 덱/회고). 기록은 이 워크트리 `report/3-mini-ftl-덱-개편-계획.md`, 최종 의미 대응 보고서·JSON·PDF는 `report/export/`에 둡니다. `assets/project-deck.js`, `assets/deck-theme.css`, `tools/check-deck.mjs`는 현재 배치·검사 기능으로 충분해 보이며 수정 예정이 없습니다. 실제 렌더가 이 판단을 뒤집을 때만 변경하고 `--all` 회귀를 추가합니다.

## 5. 적용 후 검증과 완료 기준

- 작업 전 원본/독립 워크트리의 HEAD·상태를 다시 확인하고, 기존 수정과 충돌하지 않게 대상 파일 diff만 점검합니다.
- 로컬 미리보기에서 `node tools/check-deck.mjs 3-mini-ftl --print --output report/export/3-mini-ftl-덱-검사.json` 실행. `PFScene.report()`의 40장 실측 ID, 잘림·그림 참조·작은 그림·`low-fill`·인쇄 경계와 링크 기대 페이지를 확인합니다. WGO에서 쓰던 구성/원문 참조 기반 공통 규칙을 재사용합니다. 새 검사가 필요하면 프로젝트명·쪽 번호·특정 코드 문자열 상수 없이 데이터의 장면 설정과 원문 참조로 기대값을 계산합니다.
- 바꾼 모든 장면을 화면과 인쇄 배치로 직접 확인합니다. 병합 링크 9개, 목차/장 표지, 세 그림의 열 대응, 왼쪽 그림·오른쪽 세로 카드, 코드 번호·강조 범위, 표 문장 줄바꿈, 회고 표시를 점검합니다. 관련 기존 프로브(`swapped-layout`, `missing-highlight`, `missing-code`, `wrong-code-text`, `wrong-retro-pair`)도 적용 가능한 장면에서 실행해 실패 탐지 근거를 기록합니다.
- 자동 결과와 별개로 **코드·설명·회고 의미 대응 표**를 남깁니다: 장면 ID, 설명/표 항목, 원문·소스 실행 줄, 화면에 보이는 발췌 줄, 판정, 남은 한계. 확인 안 된 항목은 통과로 표시하지 않습니다.
- 같은 로컬 미리보기에서 최종 `report/export/3-mini-ftl-개편.pdf`를 새로 생성하고, `tools/check-deck-pdf.py <PDF> <검사 JSON> 3-mini-ftl`로 페이지 크기·수와 내부 링크 목적지를 검사합니다. 화면/인쇄 수정 장면도 PDF에서 직접 봅니다. 공용 렌더러/검사기를 수정한 경우에만 `node tools/check-deck.mjs --all`을 추가합니다.
- 완료 보고에는 전후 장면 수, 수정 파일, 병합 옛 링크 9건의 목적지, 자동 검사 결과, 코드/회고 의미 검수 결과, PDF/검사 경로, 실행하지 못한 검증(예: Unity 실게임 재현)을 구분해 적습니다.
