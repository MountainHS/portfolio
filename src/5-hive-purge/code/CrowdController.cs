using System;
using System.Collections.Generic;
using System.Diagnostics;
using HybridShooter.Core.Contracts;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 씬 단위 군중 제어 파라미터. CrowdController 의 Inspector 에서 조정하고 배분·판단 주기마다 읽힌다.
    /// 초기값은 팀 ConfigTable 제안값 6m / 8마리 참고.
    /// </summary>
    [Serializable]
    public sealed class CrowdSettings
    {
        [Min(0)] public int EngageCapacity = 8;       // 근접 동시 허용 수 (마리)
        [Min(0f)] public float NearRadius = 6f;       // 과밀 판정 반경 (m)
        [Min(0f)] public float ReleaseRadius = 10.5f; // 토큰 모드: 토큰 보유자가 이 반경 밖으로 나가면 회수 (m)
        [Tooltip("토큰 배분 주기 (s)")]
        [Min(0.02f)] public float AllocationInterval = 0.2f;
        [Tooltip("개체 판단을 이 시간 동안 프레임에 나눠 한 바퀴 돈다 = 개체당 Think 간격 (s)")]
        [Min(0.02f)] public float ThinkCycle = 0.5f;
        public bool EnableCrowdControl = true;        // false = 전원 제한 없이 이동 (Unrestricted)
        public bool UseSlots = true;                  // true = 슬롯 모드, false = 토큰 모드
        public SlotSettings Slots = new SlotSettings();
    }

    /// <summary>디버그·HUD 용 집계 (읽기 전용). CrowdController.GetSnapshot 이 만들어 MovementLab HUD 가 읽는다.</summary>
    public readonly struct CrowdSnapshot
    {
        public readonly int Registered;           // 등록된 몬스터 수
        public readonly AllocationSummary Summary;
        // 토큰 보유 + NearRadius 안 개체 수 (불변식: ≤ Capacity)
        public readonly int EngagedInsideNear;
        public readonly int Capacity;
        // 마지막 배분 + 판단에 걸린 시간 (ms)
        public readonly float LastTickMs;

        /// <summary>CrowdController.GetSnapshot 이 현재 집계 값을 묶을 때 쓴다.</summary>
        /// <param name="registered">등록된 몬스터 수</param>
        /// <param name="summary">마지막 배분 집계</param>
        /// <param name="engagedInsideNear">토큰 보유 + NearRadius 안 개체 수</param>
        /// <param name="capacity">근접 동시 허용 수 (EngageCapacity)</param>
        /// <param name="lastTickMs">마지막 배분 + 판단에 걸린 시간 (ms)</param>
        public CrowdSnapshot(int registered, AllocationSummary summary, int engagedInsideNear, int capacity, float lastTickMs)
        {
            Registered = registered;
            Summary = summary;
            EngagedInsideNear = engagedInsideNear;
            Capacity = capacity;
            LastTickMs = lastTickMs;
        }
    }

    /// <summary>
    /// 군중 묶음 1개의 통제자. Crowd 형식으로 스폰된 묶음마다 1개씩 만들어진다 (랩 씬은 직접 배치).
    /// 등록 관리 + 주기적 토큰 배분 + 개체 판단을 프레임에 분산 호출.
    /// 이동 "명령"은 내리지 않는다. CrowdDirective(허가 + 정보)만 전달한다.
    /// </summary>
    /// <remarks>
    /// 군중 모드 MonsterMovement 의 Think·TickMovement 는 이 클래스의 Update 가 호출 순서대로 부른다.
    /// 모드: UseSlots = true 면 SlotRegistry 로 슬롯 예약, false 면 EngageAllocator 로 토큰 배분.
    /// 수명: 스포너가 MarkSpawnFinished 를 부른 뒤 소속 개체(members)가 0 이 되면 스스로 파괴된다.
    /// </remarks>
    public sealed class CrowdController : MonoBehaviour
    {
        // ════════ 설정 (Inspector) ════════
        [Header("설정")]
        [Tooltip("IPlayerTarget 을 구현한 컴포넌트")]
        [SerializeField] private MonoBehaviour targetSource;
        [SerializeField] private CrowdSettings settings = new CrowdSettings();

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("CrowdController.Tick");

        // ════════ 소속 개체·수명 ════════
        // 이 묶음에 속한 몬스터 (Join/Leave 로 관리). 정지 중이라 등록 해제된 개체도 포함한다
        private readonly List<MonsterMovement> members = new List<MonsterMovement>();
        private bool spawnFinished;   // 스포너가 이 묶음의 소환을 모두 마쳤나 (MarkSpawnFinished)

        // ════════ 등록된 몬스터·추적 대상 ════════
        private readonly List<MonsterMovement> agents = new List<MonsterMovement>(); // Register/Unregister 로 관리
        private IReadOnlyList<MonsterMovement> agentsView;                            // agents 의 읽기 전용 뷰 (캐시)
        private IPlayerTarget target;                                               // 추적 대상 (플레이어 등). null = 목표 없음(사망 등) → 전원 Idle

        // ════════ 슬롯 모드 (UseSlots = true) ════════
        private SlotRegistry slots;                                                   // 목표 둘레 슬롯 배정 관리
        private NavMeshSlotNavigation slotNavigation;                                 // slots 와 함께 만든 투영·경로 판단 (추적 지점 투영에도 재사용)
        private bool slotsRefreshed;                                                  // 현재 목표 기준으로 슬롯을 갱신했나
        private readonly List<SlotObstacle> slotObstacles = new List<SlotObstacle>(); // 혼잡 판정용 몬스터 위치 목록 (재사용)

        // ════════ 추적 지점 (슬롯 링 중심) ════════
        private Vector3 chasePosition;              // 슬롯 링 중심. 플레이어를 NavMesh 에 투영한 지점, 투영 실패 시 마지막 성공 지점
        private bool hasProjectedChasePosition;     // 지금 목표로 투영에 한 번이라도 성공했나 (false = 이력 없음, 실제 위치 사용)

        // ════════ 토큰 모드 (UseSlots = false) 배분 버퍼 ════════
        private AllocationAgent[] agentBuffer = new AllocationAgent[16];              // EngageAllocator 입력 (매번 할당하지 않도록 재사용)
        private AllocationResult[] resultBuffer = new AllocationResult[16];           // EngageAllocator 출력

        // ════════ 주기·판단 분산 ════════
        private float nextAllocationTime;   // 다음 배분(슬롯 갱신) 시각
        private float thinkBudget;          // 이번 프레임에 판단시킬 수 (소수 누적)
        private int thinkCursor;            // 다음에 판단할 개체 순번 (라운드 로빈)

        // ════════ 디버그·HUD 집계 ════════
        private AllocationSummary lastSummary;
        private int lastEngagedInsideNear;
        private float lastTickMs;

        // ════════ 외부 공개 ════════
        public IPlayerTarget Target => target ?? targetSource as IPlayerTarget;   // null = 목표 없음
        public CrowdSettings Settings => settings;
        public int MemberCount => members.Count;
        public int EngageCapacity => settings.EngageCapacity;
        public IReadOnlyList<MonsterMovement> Agents
        {
            get
            {
                if (agentsView == null) agentsView = agents.AsReadOnly();
                return agentsView;
            }
        }
        public int SlotCount => slotsRefreshed ? slots.Count : 0;                 // 슬롯 갱신 전이면 0

        /// <summary>디버그 표시(MovementLab 기즈모 등)용으로 슬롯 id 의 현재 상태 복사본을 만든다.</summary>
        /// <param name="id">슬롯 id (0 ~ SlotCount-1)</param>
        /// <returns>슬롯 위치·유효성·소유자·혼잡 여부 복사본</returns>
        /// <exception cref="InvalidOperationException">슬롯이 아직 갱신 전(SlotCount == 0)일 때</exception>
        public SlotSnapshot GetSlotSnapshot(int id)
        {
            if (SlotCount == 0)
                throw new InvalidOperationException("슬롯 후보가 아직 갱신되지 않았다. RefreshSlots를 먼저 호출해야 한다");
            return slots.GetSnapshot(id);
        }

        /// <summary>
        /// agent 가 slotId 를 아직 유효하게 예약 중인가 (MonsterMovement.HasCurrentReservation·CanAttack 용 O(1) 조회).
        /// 스냅샷을 만들지 않아 혼잡 판정 비용이 없다.
        /// </summary>
        /// <param name="agent">예약을 확인할 몬스터</param>
        /// <param name="slotId">확인할 슬롯 id</param>
        /// <returns>agent 의 유효(Valid) 예약이면 true. 슬롯이 아직 갱신 전이면 false</returns>
        public bool IsSlotReservedBy(MonsterMovement agent, int slotId) =>
            slotsRefreshed && slots.IsValidlyReservedBy(slotId, agent.GetInstanceID());

        /// <summary>Inspector 의 targetSource 를 IPlayerTarget 으로 변환해 저장하고 설정값을 검증한다.</summary>
        /// <remarks>targetSource 가 IPlayerTarget 이 아니면 에러 로그만 남기고 target 은 null 로 둔다.</remarks>
        private void Awake()
        {
            if (targetSource != null) target = targetSource as IPlayerTarget;
            bool isTargetSourceWrongType = targetSource != null && target == null;
            if (isTargetSourceWrongType)
                Debug.LogError($"targetSource({targetSource.GetType().Name}) 가 IPlayerTarget 을 구현하지 않음", this);
            ValidateSettings();
        }

        /// <summary>Inspector 에서 값을 바꿀 때마다 설정을 검증한다.</summary>
        private void OnValidate() => ValidateSettings();

        /// <summary>반경·슬롯 설정이 잘못되면 에러 로그를 남긴다. Awake 와 OnValidate(Inspector 수정 시)가 부른다.</summary>
        /// <remarks>
        /// 반경 순서(Near &lt; Waiting &lt;= Release)는 토큰 모드(UseSlots = false)일 때만 검사한다.
        /// Slots 가 null 이면 반경 순서는 검사하지 않고 링 설정 에러만 남긴다.
        /// </remarks>
        private void ValidateSettings()
        {
            if (settings.Slots == null)
            {
                Debug.LogError("SlotSettings 첫 링 후보 수·반경·간격은 양수이고 최대 링 수는 2 이상이어야 함", this);
                return;
            }

            bool isTokenMode = settings.UseSlots == false;
            bool isNearRadiusNonNegative = settings.NearRadius >= 0f;
            bool isNearInsideWaiting = settings.NearRadius < settings.Slots.WaitingRadius;
            bool isWaitingInsideRelease = settings.Slots.WaitingRadius <= settings.ReleaseRadius;
            bool isRadiusOrderValid = isNearRadiusNonNegative && isNearInsideWaiting && isWaitingInsideRelease;
            if (isTokenMode && isRadiusOrderValid == false)
                Debug.LogError($"CrowdSettings 반경 오류: 0 <= Near({settings.NearRadius}) < Waiting({settings.Slots.WaitingRadius}) <= Release({settings.ReleaseRadius}) 이어야 함", this);

            bool isApproachCountInvalid = settings.Slots.ApproachCount < 1;
            bool isApproachRadiusInvalid = settings.Slots.ApproachRadius <= 0f;
            bool isRingGapInvalid = settings.Slots.MinimumRingGap <= 0f;
            bool isMaxRingCountInvalid = settings.Slots.MaxRingCount < 2;
            if (isApproachCountInvalid || isApproachRadiusInvalid || isRingGapInvalid || isMaxRingCountInvalid)
                Debug.LogError("SlotSettings 첫 링 후보 수·반경·간격은 양수이고 최대 링 수는 2 이상이어야 함", this);
        }

        /// <summary>꺼질 때 모든 슬롯 예약을 비운다.</summary>
        private void OnDisable()
        {
            ClearSlotReservations();
        }

        /// <summary>
        /// 몬스터를 이 묶음의 소속으로 넣는다. MonsterMovement.Activate 가 이 컨트롤러를 받을 때 부른다.
        /// 소속은 수명 판단용이고, 실제 배분 대상 등록은 Register 가 따로 한다 (정지 중이면 소속만 되고 등록은 안 될 수 있다).
        /// </summary>
        /// <param name="member">소속시킬 몬스터</param>
        /// <exception cref="ArgumentNullException">member 가 null 일 때</exception>
        /// <exception cref="InvalidOperationException">이미 소속된 몬스터일 때</exception>
        public void Join(MonsterMovement member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (members.Contains(member)) throw new InvalidOperationException($"이미 소속된 MonsterMovement: {member.name}");
            members.Add(member);
        }

        /// <summary>
        /// 몬스터를 소속에서 뺀다. MonsterMovement.OnDisable(사망·풀 반환)이 등록 해제 뒤에 부른다.
        /// 소환이 끝났고 소속이 0 이 되면 이 묶음을 파괴한다.
        /// </summary>
        /// <param name="member">뺄 몬스터</param>
        /// <exception cref="ArgumentNullException">member 가 null 일 때</exception>
        /// <exception cref="InvalidOperationException">소속되지 않은 몬스터일 때</exception>
        public void Leave(MonsterMovement member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (members.Remove(member) == false) throw new InvalidOperationException($"소속되지 않은 MonsterMovement: {member.name}");
            DestroyIfFinished();
        }

        /// <summary>
        /// 이 묶음의 소환이 모두 끝났음을 알린다. MonsterSpawner 가 묶음의 마지막 개체를 소환한 뒤 부른다.
        /// 이미 전멸해 있으면 바로 파괴한다.
        /// </summary>
        public void MarkSpawnFinished()
        {
            spawnFinished = true;
            DestroyIfFinished();
        }

        /// <summary>소환이 끝났고 소속 개체가 없으면 이 GameObject 를 파괴한다. Leave·MarkSpawnFinished 가 부른다.</summary>
        private void DestroyIfFinished()
        {
            if (spawnFinished == false) return;
            if (members.Count > 0) return;
            Destroy(gameObject);
        }

        /// <summary>
        /// 군중 전체의 목표를 교체한다 (대체 객체 ↔ 실제 플레이어). MonsterMovement.SetTarget 이 부른다.
        /// 호출마다 슬롯 예약을 비우고 다음 프레임에 즉시 재배분한다.
        /// </summary>
        /// <param name="newTarget">새 목표. null = 목표 상실: 예약을 비우고 전원 Idle 로 정지한다</param>
        public void SetTarget(IPlayerTarget newTarget)
        {
            target = newTarget;
            targetSource = newTarget as MonoBehaviour;
            hasProjectedChasePosition = false;
            ClearSlotReservations();
            slotsRefreshed = false;
            nextAllocationTime = 0f;
        }

        /// <summary>근접 동시 허용 수를 바꾸고 다음 프레임에 즉시 재배분한다. MovementLab 슬라이더가 부른다.</summary>
        /// <param name="capacity">새 근접 동시 허용 수 (마리, 0 이상)</param>
        /// <exception cref="ArgumentOutOfRangeException">capacity 가 음수일 때</exception>
        public void SetEngageCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "0 이상이어야 함");
            settings.EngageCapacity = capacity;
            nextAllocationTime = 0f;
        }

        /// <summary>디버그·HUD 용 현재 집계를 만든다. MovementLab HUD 가 매 프레임 읽는다.</summary>
        /// <returns>등록 수, 마지막 배분 집계, 근접 반경 안 보유자 수, 정원, 마지막 틱 비용</returns>
        public CrowdSnapshot GetSnapshot() =>
            new CrowdSnapshot(agents.Count, lastSummary, lastEngagedInsideNear, settings.EngageCapacity, lastTickMs);

        /// <summary>
        /// 몬스터를 군중 목록에 넣고 다음 프레임에 즉시 재배분한다.
        /// MonsterMovement 가 활성화될 때(OnEnable) 또는 정지(Suspended)가 풀릴 때 호출한다.
        /// </summary>
        /// <remarks>중복 등록은 에러 로그만 남기고 무시한다. 슬롯 모드에서 토큰을 쓰는 개체의 NavMeshAgent 타입이 SlotSettings.AgentTypeId 와 다르면 에러 로그 (비행체는 NavMeshAgent 가 없어 검사하지 않는다).</remarks>
        /// <param name="agent">등록할 몬스터</param>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public void Register(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (agents.Contains(agent))
            {
                Debug.LogError($"이미 등록된 MonsterMovement: {agent.name}", agent);
                return;
            }
            agents.Add(agent);
            bool isSlotMode = settings.EnableCrowdControl && settings.UseSlots;
            bool usesEngageToken = agent.Profile.UsesEngageToken;
            bool isAgentTypeDifferent = isSlotMode && usesEngageToken &&
                agent.GetComponent<UnityEngine.AI.NavMeshAgent>().agentTypeID != settings.Slots.AgentTypeId;
            if (isAgentTypeDifferent)
                Debug.LogError($"{agent.name}: NavMeshAgent 타입과 SlotSettings.AgentTypeId가 다름", agent);
            nextAllocationTime = 0f;
        }

        /// <summary>
        /// 슬롯 모드 배분: 등록 개체와 현재 타겟 기준으로 슬롯 후보와 실제 점유를 갱신한다.
        /// Update 가 배분 주기마다, RequestDirective 가 갱신 전일 때 부른다.
        /// 목표가 무효면 모든 예약을 비운다. 접근 링 예약이 허용 인원을 넘으면 먼 번호부터 회수하고, 집계(lastSummary)를 새로 계산한다.
        /// </summary>
        public void RefreshSlots()
        {
            EnsureSlots();
            IPlayerTarget currentTarget = Target;

            // 1) 목표가 없으면 예약 전부 해제하고 종료
            if (currentTarget == null)
            {
                slots.ClearReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                return;
            }
            // 2) 추적 지점을 정하고, 몬스터 위치를 장애물로 넘겨 그 둘레의 슬롯 후보·점유 상태 갱신
            UpdateChasePosition(currentTarget.PlayerTrackingInfo.Position);
            slotObstacles.Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                slotObstacles.Add(new SlotObstacle(agent.GetInstanceID(), agent.transform.position, agent.Radius));
            }
            slots.Refresh(chasePosition, Time.time, slotObstacles);
            slotsRefreshed = true;
            // 3) 동시 접근 허용 수(EngageCapacity)를 넘은 접근 링 예약은 번호가 큰 쪽부터 회수
            int maxApproach = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            for (int id = slots.ApproachCount - 1; slots.ApproachReservations > maxApproach && id >= 0; id--)
            {
                SlotSnapshot slot = slots.GetSnapshot(id);
                if (slot.Use != SlotUse.Free) slots.Release(slot.OwnerId);
            }
            // 4) 디버그·HUD 집계: 토큰 대상/비대상 수, 근접 반경 안 인원 등
            int tokenAgents = 0, unrestricted = 0, uncontrolledInside = 0, engagedNear = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                Vector3 delta = agent.transform.position - chasePosition;
                float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
                bool inside = horizontalDistance <= settings.NearRadius;
                bool usesToken = agent.Profile.UsesEngageToken;
                bool isUncontrolledInside = usesToken == false && inside;
                bool isTokenAgentInside = usesToken && inside;
                bool isEngagedInside = isTokenAgentInside && slots.HasApproachReservation(agent.GetInstanceID());

                if (usesToken) tokenAgents++;
                else unrestricted++;
                if (isUncontrolledInside) uncontrolledInside++;
                if (isEngagedInside) engagedNear++;
            }
            lastSummary = new AllocationSummary
            {
                Engaged = slots.ApproachReservations,
                Waiting = tokenAgents - slots.ApproachReservations,
                Unrestricted = unrestricted,
                UncontrolledInside = uncontrolledInside,
                FreeCapacity = Mathf.Max(0, maxApproach - slots.ApproachReservations)
            };
            lastEngagedInsideNear = engagedNear;
        }

        /// <summary>
        /// 슬롯 링 중심이 될 추적 지점(chasePosition)을 갱신한다. RefreshSlots 가 슬롯을 다시 계산하기 전에 부른다.
        /// 플레이어가 지붕 등 NavMesh 밖에 있으면 마지막으로 투영에 성공한 지점을 유지해, 몬스터가 그곳까지 와서 대기·선회하게 한다.
        /// </summary>
        /// <param name="playerPosition">플레이어 실제 위치 (월드)</param>
        /// <remarks>투영 이력이 없으면 실제 위치를 쓴다 (이전 동작). SetTarget 이 이력을 비운다.</remarks>
        private void UpdateChasePosition(Vector3 playerPosition)
        {
            if (slotNavigation.TryProject(playerPosition, out Vector3 projectedPosition))
            {
                chasePosition = projectedPosition;
                hasProjectedChasePosition = true;
                return;
            }
            if (hasProjectedChasePosition) return;
            chasePosition = playerPosition;
        }

        /// <summary>
        /// 개체 1마리의 지시를 만든다 (MonsterMovement.Think 에서 호출).
        /// 군중 제어 비대상이면 Unrestricted, 슬롯 모드면 슬롯을 즉시 예약해 Engage(0번 링)/Wait(바깥 링) 지시를 돌려준다.
        /// 슬롯을 못 얻으면 제자리 대기 지시.
        /// </summary>
        /// <param name="agent">지시를 받을 몬스터</param>
        /// <param name="now">현재 시각 (s). 슬롯 진행·거절 만료 판정에 쓴다</param>
        /// <returns>
        /// 이번 판단의 지시. 토큰 모드(UseSlots = false)면 마지막 배분 결과(agent.Directive)를 그대로 돌려준다.
        /// 목표가 없거나 슬롯을 못 얻으면 CrowdDirective.WaitingForSlot(제자리 대기)
        /// </returns>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public CrowdDirective RequestDirective(MonsterMovement agent, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));

            // 군중 제어 대상이 아니면 슬롯 없이 자유 이동
            bool isCrowdControlled = settings.EnableCrowdControl && agent.Profile.UsesEngageToken;
            if (isCrowdControlled == false)
            {
                ReleaseSlot(agent);
                return new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
            }
            if (settings.UseSlots == false) return agent.Directive;

            // 슬롯 준비가 끝나야 목표 유무를 판단할 수 있다 (갱신 전이면 여기서 RefreshSlots)
            EnsureSlotsRefreshed();
            IPlayerTarget currentTarget = Target;
            if (currentTarget == null)
            {
                slots.ClearReservations();
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            }
            // 접근 링(0번) 진입 허용 여부: 정원 안이거나 이미 접근 중이면 허용. 아니면 바깥 링에서만 슬롯을 찾는다
            int capacity = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            bool alreadyApproaching = slots.HasApproachReservation(agent.GetInstanceID());
            bool hasCapacity = capacity > 0;
            bool isUnderCapacity = slots.ApproachReservations < capacity;
            bool allowApproach = hasCapacity && (alreadyApproaching || isUnderCapacity);
            if (slots.TryRequest(agent.GetInstanceID(), agent.transform.position, now, allowApproach, out SlotSnapshot slot) == false)
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            DirectiveKind kind = slot.RingIndex == 0 ? DirectiveKind.Engage : DirectiveKind.Wait;
            return new CrowdDirective(kind, in slot, settings.Slots.ArrivalDistance);
        }

        /// <summary>agent 가 쥔 슬롯을 반납한다 (없으면 무시). Unregister 와 군중 제어 비대상 판단 때 부른다.</summary>
        /// <param name="agent">슬롯을 반납할 몬스터</param>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public void ReleaseSlot(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (slots != null) slots.Release(agent.GetInstanceID());
        }

        /// <summary>
        /// 도달 불가 등으로 slotId 를 거절한다. 일정 시간(RejectionSeconds) 동안 이 개체에게 같은 슬롯을 다시 주지 않는다.
        /// MonsterMovement.Think 가 실행기 상태가 Unreachable 일 때 부른다.
        /// </summary>
        /// <param name="agent">거절하는 몬스터</param>
        /// <param name="slotId">거절할 슬롯 id. agent 가 지금 쥔 슬롯이 아니면 무시된다</param>
        /// <param name="now">현재 시각 (s). 거절 만료 시각 계산 기준</param>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public void RejectSlot(MonsterMovement agent, int slotId, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (slots != null) slots.Reject(agent.GetInstanceID(), slotId, now);
        }

        /// <summary>반응 지연으로 일부러 멈춘 시간만큼 "슬롯으로 진행이 없다" 판정 시계를 늦춘다. MonsterMovement 의 반응 지연 처리가 부른다.</summary>
        /// <param name="agent">멈춘 몬스터</param>
        /// <param name="duration">일부러 멈춘 시간 (s, 0 이상)</param>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public void PauseSlotProgress(MonsterMovement agent, float duration)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (slots != null) slots.PauseProgress(agent.GetInstanceID(), duration);
        }

        /// <summary>SlotRegistry 가 없거나 링 설정이 바뀌었으면 새로 만든다 (이 경우 다음 갱신 때 후보를 다시 계산)</summary>
        /// <remarks>새로 만들면 기존 예약·거절 기록이 모두 사라진다.</remarks>
        private void EnsureSlots()
        {
            if (slots != null && HasSameRingGeometry()) return;
            slotNavigation = new NavMeshSlotNavigation(settings.Slots);
            slots = new SlotRegistry(settings.Slots, slotNavigation);
            slotsRefreshed = false;
        }

        /// <summary>기존 SlotRegistry 의 링 기하 설정이 현재 SlotSettings 와 같은가. EnsureSlots 가 다시 만들지 결정할 때 쓴다.</summary>
        /// <remarks>slots 가 null 이 아닐 때만 부른다.</remarks>
        /// <returns>첫 링 후보 수·반경·링 간격·최대 링 수가 모두 같으면 true</returns>
        private bool HasSameRingGeometry()
        {
            bool isSameApproachCount = slots.ApproachCount == settings.Slots.ApproachCount;
            bool isSameApproachRadius = slots.ApproachRadius == settings.Slots.ApproachRadius;
            bool isSameRingGap = slots.MinimumRingGap == settings.Slots.MinimumRingGap;
            bool isSameMaxRingCount = slots.MaxRingCount == settings.Slots.MaxRingCount;
            return isSameApproachCount && isSameApproachRadius && isSameRingGap && isSameMaxRingCount;
        }

        /// <summary>슬롯 관리자를 준비하고, 현재 목표 기준으로 아직 갱신하지 않았으면 바로 갱신한다. RequestDirective 가 슬롯을 요청하기 전에 부른다.</summary>
        private void EnsureSlotsRefreshed()
        {
            EnsureSlots();
            if (slotsRefreshed == false) RefreshSlots();
        }

        /// <summary>슬롯 관리자가 이미 만들어졌으면 모든 예약을 비운다 (아직 없으면 비울 예약도 없다). 목표 교체·상실, 비활성화, 슬롯 모드가 아닐 때 부른다.</summary>
        private void ClearSlotReservations()
        {
            if (slots != null) slots.ClearReservations();
        }

        /// <summary>
        /// MonsterMovement.OnDisable(몬스터 사망·제거 시)과 예약을 유지하지 않는 정지(SetSuspended)에서 호출. 목록에서 빼고 슬롯·토큰을 회수한다.
        /// 판단 순번(thinkCursor)을 보정하고 즉시 재배분을 예약해 빈 자리를 다른 몬스터가 채우게 한다
        /// </summary>
        /// <remarks>등록되지 않은 몬스터면 에러 로그만 남기고 무시한다. agent.ResetCrowdState 로 개체의 토큰·대기 상태도 초기화한다.</remarks>
        /// <param name="agent">해제할 몬스터</param>
        /// <exception cref="ArgumentNullException">agent 가 null 일 때</exception>
        public void Unregister(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            int index = agents.IndexOf(agent);
            if (index < 0)
            {
                Debug.LogError($"등록되지 않은 MonsterMovement 해제: {agent.name}", agent);
                return;
            }
            agents.RemoveAt(index);
            ReleaseSlot(agent);
            if (thinkCursor > index) thinkCursor--;
            if (thinkCursor >= agents.Count) thinkCursor = 0;
            agent.ResetCrowdState();
            nextAllocationTime = 0f; // 빈 자리를 다음 프레임에 재배분
        }

        /// <summary>
        /// 매 프레임 군중 제어 루프. 일정 주기마다 배분(슬롯 갱신/토큰 배분)을 하고,
        /// 몬스터 판단(Think)은 ThinkCycle 동안 프레임에 나눠 라운드 로빈으로 호출한다.
        /// 목표가 무효면 예약을 비우고 전원에게 Think 를 호출해 Idle 로 만든다.
        /// 마지막에 등록 개체의 TickMovement 를 호출한다: 군중 모드 몬스터의 Update 는 비어 있으므로,
        /// "CrowdController(배분·Think) → 몬스터(예약 검증·지연 의도 적용)" 순서가 실행 순서 설정 없이 호출 순서로 고정된다.
        /// </summary>
        private void Update()
        {
            // A) 목표 무효: 예약 해제 + 전원 Think(→ Idle 정지)
            if (target == null)
            {
                ClearSlotReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                for (int i = 0; i < agents.Count; i++)
                    agents[i].Think(target, Time.time);
                TickAgents();
                return;
            }

            long startTs = Stopwatch.GetTimestamp();
            using (TickMarker.Auto())
            {
                // B) 배분 주기가 되면 모드에 따라 배분: 슬롯 모드 / 토큰 모드 / 군중 제어 꺼짐(전원 자유 이동)
                float now = Time.time;
                if (now >= nextAllocationTime) AllocateByMode(now);

                // C) 개체 판단 분산: 이번 프레임에 판단할 수(thinkBudget)만큼 순번대로 Think 호출
                ThinkInTurn(now);
            }
            lastTickMs = (Stopwatch.GetTimestamp() - startTs) * 1000f / Stopwatch.Frequency;
            TickAgents(); // lastTickMs 는 배분 + 판단 비용만 잰다 (기존과 동일)
        }

        /// <summary>
        /// 모드에 따라 배분한다: 슬롯 모드(RefreshSlots) / 토큰 모드(Allocate) / 군중 제어 꺼짐(전원 자유 이동).
        /// Update 가 배분 주기(nextAllocationTime)가 됐을 때 부르고, 다음 배분 시각도 여기서(또는 Allocate 에서) 정한다.
        /// </summary>
        /// <param name="now">현재 시각 (s). 다음 배분 시각 계산 기준</param>
        private void AllocateByMode(float now)
        {
            if (settings.EnableCrowdControl && settings.UseSlots)
            {
                RefreshSlots();
                nextAllocationTime = now + settings.AllocationInterval;
            }
            else if (settings.EnableCrowdControl)
            {
                ClearSlotReservations();
                Allocate(now);
            }
            else
            {
                ClearSlotReservations();
                for (int i = 0; i < agents.Count; i++)
                    agents[i].ApplyAllocation(new AllocationResult
                    {
                        Directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f),
                        WaitingSince = -1f,
                        SlotIndex = -1
                    });
                lastSummary = new AllocationSummary
                {
                    Unrestricted = agents.Count,
                    FreeCapacity = settings.EngageCapacity
                };
                lastEngagedInsideNear = 0;
                nextAllocationTime = now + settings.AllocationInterval;
            }
        }

        /// <summary>
        /// 개체 판단 분산: 이번 프레임에 판단할 수(thinkBudget)만큼 순번(thinkCursor)대로 Think 를 호출한다. Update 가 매 프레임 부른다.
        /// </summary>
        /// <remarks>
        /// 배분 프레임에도 전원 Think 를 몰아주지 않고 예산만 누적한다: 개체당 정확히 ThinkCycle 마다 1회, 프레임에 고르게 분산
        /// (대신 지시 변화·신규 슬롯 반영이 최대 ThinkCycle 만큼 늦어진다).
        /// 판단 중 등록/해제가 일어나면 이번 프레임 판단을 멈춘다.
        /// </remarks>
        /// <param name="now">현재 시각 (s). Think 에 그대로 넘긴다</param>
        private void ThinkInTurn(float now)
        {
            int n = agents.Count;
            if (n <= 0) return;

            thinkBudget = Mathf.Min(thinkBudget + n * Time.deltaTime / settings.ThinkCycle, n);

            int k = Mathf.Min(n, (int)thinkBudget);
            thinkBudget -= k;
            for (int i = 0; i < k; i++)
            {
                if (agents.Count != n) break; // 판단 중 등록/해제 발생
                agents[thinkCursor % n].Think(target, now);
                thinkCursor = (thinkCursor + 1) % n;
            }
        }

        /// <summary>등록된 모든 몬스터의 프레임 갱신(TickMovement)을 호출한다. Update 에서 Think 뒤에 불려 순서가 보장된다</summary>
        private void TickAgents()
        {
            for (int i = 0; i < agents.Count; i++)
                agents[i].TickMovement();
        }

        /// <summary>토큰 모드 배분: 개체 상태를 모아 EngageAllocator 에 넘기고, 결과를 각 개체에 반영한다. Update 가 배분 주기마다 부른다.</summary>
        /// <param name="now">현재 시각 (s). 대기 시간 판정과 다음 배분 시각 계산에 쓴다</param>
        private void Allocate(float now)
        {
            // 1) 버퍼 용량 확보 (부족하면 2배씩 확장)
            int n = agents.Count;
            if (agentBuffer.Length < n)
            {
                int size = agentBuffer.Length;
                while (size < n) size *= 2;
                agentBuffer = new AllocationAgent[size];
                resultBuffer = new AllocationResult[size];
            }

            // 2) 순수 계산기(EngageAllocator)로 배분
            for (int i = 0; i < n; i++)
                agentBuffer[i] = agents[i].BuildAllocationAgent();

            Vector3 targetPos = target.PlayerTrackingInfo.Position;
            var p = new AllocationParams
            {
                TargetPosition = targetPos,
                Capacity = settings.EngageCapacity,
                NearRadius = settings.NearRadius,
                Slots = settings.Slots,
                ReleaseRadius = settings.ReleaseRadius,
                Now = now,
                UseSlots = settings.UseSlots,
            };
            lastSummary = EngageAllocator.Allocate(p, agentBuffer, n, resultBuffer);

            // 3) 결과를 개체에 반영하고, 근접 반경 안 토큰 보유자 수 집계
            int engagedNear = 0;
            for (int i = 0; i < n; i++)
            {
                agents[i].ApplyAllocation(resultBuffer[i]);
                float dx = agentBuffer[i].Position.x - targetPos.x;
                float dz = agentBuffer[i].Position.z - targetPos.z;
                float horizontalDistance = new Vector2(dx, dz).magnitude;
                bool isEngagedInside = resultBuffer[i].HasToken && horizontalDistance <= settings.NearRadius;
                if (isEngagedInside) engagedNear++;
            }
            lastEngagedInsideNear = engagedNear;
            nextAllocationTime = now + settings.AllocationInterval;
        }
    }
}
