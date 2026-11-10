using System;
using HybridShooter.Core.Contracts;
using UnityEngine;
using UnityEngine.Serialization;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터 1마리의 이동 진입점.
    /// 이동 상태를 가지고, 정책(IMovementPolicy)이 낸 의도를 실행기(IMoveExecutor)에 넘기며, 외부 정지 요청(SetSuspended)을 처리한다.
    /// 군중 모드는 CrowdController 가, 직접 추적 모드는 자신의 Update 가 Think·TickMovement 를 호출한다.
    /// </summary>
    /// <remarks>
    /// 같은 GameObject 에 IMoveExecutor 구현 컴포넌트가 있어야 한다.
    /// 런타임 생성 시: 비활성 GameObject 에 붙이고 Configure() → SetActive(true).
    /// </remarks>
    public sealed class MonsterMovement : MonoBehaviour
    {
        // ════════ 설정 (Inspector / Configure 로 주입) ════════
        [Header("설정")]
        [SerializeField] private MovementProfile profile;
        [Tooltip("이 몬스터를 통제할 군중 묶음. 비우면 직접 추적 모드 (Crowd 형식 스폰만 스포너가 채운다)")]
        [FormerlySerializedAs("coordinator")]
        [SerializeField] private CrowdController assignedController;
        [Tooltip("군중 관리자 없는 직접 추적 모드의 IPlayerTarget")]
        [SerializeField] private MonoBehaviour directTargetSource;

        [Header("시선")]
        [Tooltip("플레이어 방향으로 돌아보는 속도. 클수록 빠르게 따라붙는다 (지수 보간 계수, 1/s)")]
        [SerializeField, Min(0f)] private float turnSpeed = 8f;

        [Header("목적지 보간")]
        [Tooltip("실행기에 넘기는 추적 목적지가 새 목적지를 따라잡는 빠르기 (지수 보간 계수, 1/s). 클수록 빨리 따라붙는다. 0 = 보간 끔(새 목적지로 즉시 점프)")]
        [SerializeField, Min(0f)] private float destinationFollowRate = 5f;

        // ════════ 협력 객체 (Awake/OnEnable 에서 연결) ════════
        // 실행기: "어떻게 움직일지"를 NavMeshAgent 로 실제 수행 (정책이 낸 MoveIntent 를 받는다)
        // 나중에 NavMesh 만으로
        private IMoveExecutor executor;
        // 정책: "어디로 갈지"를 판단 (profile.PolicyKind 로 생성, 새 이동 방식은 여기만 추가)
        private IMovementPolicy policy;
        // profile 의 수치 복사본 (정책이 ScriptableObject 에 의존하지 않게)
        private MovementTuning tuning;
        // 슬롯·토큰을 나눠주는 군중 관리자 (직접 추적 모드면 null)
        private CrowdController crowdController;
        // 추적 대상. 군중 모드에서는 Think(target) 인자로 받고, 직접 모드에서는 이 값을 쓴다
        private IPlayerTarget targetInfo;
        private bool isDirectMode;   // true = 군중 관리자 없이 targetInfo 를 직접 추적 (targetInfo 가 null 이어도 직접 모드)

        // ════════ 현재 이동 상태 (외부 공개 프로퍼티의 원본) ════════
        private MovementState state = MovementState.Idle;
        private bool suspended;         // 공격·경직 등으로 이동 정지 중
        private bool isInAttackRange;   // 목표 유효 + 수평 거리 ≤ AttackRange
        private MoveIntent lastIntent;  // 마지막으로 확정한 의도. 목적지는 보간 전 원래 값 (FollowDestination 이 따라갈 목표)

        // ── 추적 목적지 (Think 사이 목적지 점프를 매 프레임 보간) ──
        private Vector3 followDestination;  // 실행기에 실제로 넘기는 목적지 (월드)
        private bool hasFollowDestination;  // false = 다음 의도의 목적지로 바로 놓는다 (첫 이동·정지 뒤)

        // ════════ 군중 배분 결과 (CrowdController 가 채워 넣고 정책이 읽는다) ════════
        private CrowdDirective directive;
        private bool hasToken;                // 근접 토큰 보유 여부 (토큰 모드)
        private float waitingSince = -1f;     // Wait 시작 시각 (음수 = 대기 아님)
        private int orbitSign;                // 대기 중 선회 방향 +1/-1
        private int slotIndex = -1;           // 포위 슬롯 번호 (토큰 모드, -1 = 없음)

        // ════════ 초기화 시점 ════════
        private bool started;                 // Start 를 거쳤는가 (첫 활성화는 Start, 이후 재활성화는 OnEnable 에서 초기화)
        private bool registered;              // CrowdController 에 등록돼 있는가
        private bool retainReservation;       // 정지 중에도 등록·슬롯·토큰을 유지하는가 (공격 중 정지만 true)
        private bool hasCombatTuning;         // SetCombatTuning 으로 개체별 속도·사거리를 받았는가
        private float combatSpeed;            // 개체별 이동 속도 (m/s)
        private float combatRange;            // 개체별 공격 사거리 (m)
        private float flankApproachSpeedMultiplier = 1f; // 원거리 시야 밖 접근 속도 배율 (SetFlankApproachSpeedMultiplier, 기본 1)

        // ════════ 판단 주기 ════════
        private bool hasThought;              // 한 번이라도 Think 했는가 (첫 dt 를 0 으로 만들기 위함)
        private float lastThinkTime;          // 마지막 Think 시각 (s)
        private float nextDirectThink;        // 직접 추적 모드의 다음 판단 시각 (s, Time.time 기준)

        // ════════ 반응 지연 (슬롯 목적지가 크게 바뀌면 잠깐 뜸 들인 뒤 반응) ════════
        private MoveIntent pendingIntent;     // 지연 중인 의도
        private bool hasPendingIntent;
        private float pendingApplyAt;         // pendingIntent 를 적용할 시각 (s, Time.time 기준)
        private Vector3 reactionReferenceDestination; // "목적지가 바뀌었나" 비교 기준점
        private bool hasReactionReference;
        private int reactionSlotId = -1;      // 기준점을 잡은 슬롯 (-1 = 없음)
        private IPlayerTarget reactionTargetInfo; // 기준점을 잡은 목표 (목표 교체 감지용)

        // ════════ 슬롯 실행 추적 ════════
        // 실행기가 지금 향하고 있는 슬롯 번호. 경로 없음(Unreachable)이면 그 슬롯을 거절하기 위해 기억한다 (-1 = 없음)
        private int executingSlotId = -1;

        // ════════ 시선 (Think 가 갱신, LateUpdate 가 회전에 사용) ════════
        private Vector3 lookTarget;           // 마지막 Think 에서 얻은 플레이어 위치
        private bool hasLookTarget;           // 목표가 유효한가
        private float viewHalfAngleDeg;

        // ════════ 외부 공개 (읽기 전용) ════════
        public MovementProfile Profile => profile;
        
        public IPlayerTarget Target => crowdController != null ? crowdController.Target : targetInfo;
        // 거리와 별개인 현재 군중 허가 (MonsterStateMachine.CanAttack 이 읽음). 정지 중에도 실제 예약/토큰을 확인한다.
        // 직접 추적 모드는 지시가 Unrestricted 면 true
        public bool CanAttack
        {
            get
            {
                if (isActiveAndEnabled == false) return false;
                if (Target == null) return false;
                if (crowdController == null) return directive.Kind == DirectiveKind.Unrestricted;
                if (registered == false) return false;
                if (crowdController.Settings.EnableCrowdControl == false) return true;
                if (profile.UsesEngageToken == false) return true;
                if (directive.Kind != DirectiveKind.Engage) return false;
                if (crowdController.Settings.UseSlots) return crowdController.IsSlotReservedBy(this, directive.SlotId);
                return hasToken;
            }
        }
        public MovementState State => state;
        // 목표 유효 + 수평 거리 ≤ AttackRange
        public bool IsInAttackRange => isInAttackRange;
        public bool IsSuspended => suspended;
        // 현재 속도 (m/s). 실행기가 없으면 0
        public Vector3 Velocity => executor != null ? executor.Velocity : Vector3.zero;
        // 마지막으로 받은 군중 지시 (디버그용)
        public CrowdDirective Directive => directive;
        public MoveIntent LastIntent => lastIntent;
        // 정책에 넘기는 수치 (profile + 개체별 속도·사거리·원거리 배율). 디버그 시각화용
        public MovementTuning Tuning => tuning;
        // 마지막 판단이 원거리 시야 밖 접근이었는가 (측면 침투형만 true 가능). MonsterController 가 이동 클립 배율을 고를 때 읽는다
        public bool IsApproachingOutOfView => policy is FlankPolicy flank && flank.IsApproachingOutOfView;
        public MoveExecutionState ExecutionState => executor != null ? executor.ExecutionState : MoveExecutionState.Idle;
        public float Radius => executor.Radius;

        // 이동 상태가 실제로 바뀔 때 발생 (sender, previous, current). SetState 가 발생시킨다
        public event Action<MonsterMovement, MovementState, MovementState> StateChanged;

        /// <summary>같은 GameObject 의 IMoveExecutor 를 찾는다. 없으면 에러 로그 후 컴포넌트 비활성화</summary>
        private void Awake()
        {
            executor = GetComponent<IMoveExecutor>();
            if (executor == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] IMoveExecutor 구현 컴포넌트 없음", this);
                enabled = false;
            }
        }

        /// <summary>재활성화 때만 여기서 바로 초기화(Activate)한다. 첫 활성화는 Start 로 미룬다.</summary>
        /// <remarks>
        /// 첫 활성화를 Start 로 미뤄, 같은 프레임에 다른 컴포넌트가 OnEnable·Awake 에서 하는 준비가 끝난 뒤 모드를 정한다
        /// (DefaultExecutionOrder 없이 순서 보장).
        /// </remarks>
        private void OnEnable()
        {
            if (started) Activate();
        }

        /// <summary>첫 활성화 초기화 (모든 OnEnable 이 끝난 뒤). 이후 재활성화는 OnEnable 이 Activate 를 호출한다</summary>
        private void Start()
        {
            started = true;
            if (enabled) Activate();
        }

        /// <summary>
        /// 활성화 초기화: 동작 모드를 결정하고 정책·수치를 준비한다. Start(첫 활성화)와 OnEnable(재활성화)이 호출한다.
        /// 배정된 CrowdController(assignedController)가 있으면 군중 모드로 소속·등록하고, 없으면 직접 추적 모드로 동작한다.
        /// profile 이 없으면 에러 로그 후 비활성화.
        /// </summary>
        /// <exception cref="InvalidOperationException">directTargetSource 가 IPlayerTarget 이 아닐 때</exception>
        private void Activate()
        {
            if (profile == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] MovementProfile 없음", this);
                enabled = false;
                return;
            }

            // 1) 동작 모드 결정: 배정된 컨트롤러가 있으면 군중 모드, 없으면 직접 추적 모드 (컨트롤러가 파괴됐어도 직접 추적)
            if (isDirectMode == false && directTargetSource != null)
            {
                var directTarget = directTargetSource as IPlayerTarget;
                if (directTarget == null)
                    throw new InvalidOperationException("directTargetSource 는 IPlayerTarget 이어야 한다");
                targetInfo = directTarget;
            }
            isDirectMode = assignedController == null;

            // 2) 정책·수치 준비 (정책은 개체 하나당 인스턴스 하나, 재활성화 시 재사용)
            ApplyCombatTuning();
            if (policy == null) policy = MovementPolicyFactory.Create(profile.PolicyKind, GetInstanceID());

            // 3) 이전 활성화 때 남은 상태 초기화
            hasThought = false;
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;

            // 4) 군중 모드면 묶음에 소속 + 등록, 직접 모드면 제한 없음(Unrestricted) 지시를 고정
            if (isDirectMode)
            {
                directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
                return;
            }
            JoinAssignedCrowd();
            RegisterAssignedCrowd();
        }

        /// <summary>
        /// 배정된 묶음(assignedController)에 소속한다 (이미 소속이면 무시). 소속은 묶음 수명 판단용이다.
        /// Activate(활성화 시 배정돼 있음)와 JoinCrowd(소환 직후 배정)가 부른다.
        /// </summary>
        private void JoinAssignedCrowd()
        {
            if (crowdController != null) return;
            crowdController = assignedController;
            crowdController.Join(this);
        }

        /// <summary>
        /// 소속된 묶음의 배분 대상으로 등록하고 군중 모드로 동작한다. Activate 와 JoinCrowd(이미 활성인 개체)가 소속 뒤에 부른다.
        /// </summary>
        /// <exception cref="InvalidOperationException">슬롯 모드에서 접근 슬롯 반경 + 여유가 공격 사거리 이상일 때 (ValidateCombatSlots)</exception>
        private void RegisterAssignedCrowd()
        {
            isDirectMode = false;
            ValidateCombatSlots();
            if (suspended == false || retainReservation) RegisterCrowd();
        }

        /// <summary>
        /// 이 몬스터를 군중 묶음에 배정하고 바로 소속시킨다. MonsterSpawner 가 Crowd 형식으로 소환한 직후 부른다.
        /// 바로 소속시켜야 같은 Tick 에 스포너가 끝나도(MarkSpawnFinished) 묶음이 0마리로 보고 파괴되지 않는다.
        /// 첫 활성화(Start) 전이면 배분 등록은 Activate 가 하고, 풀에서 꺼내 이미 직접 추적으로 활성화됐으면 지금 군중 모드로 바꾼다.
        /// </summary>
        /// <param name="controller">통제받을 묶음</param>
        /// <exception cref="ArgumentNullException">controller 가 null 일 때</exception>
        /// <exception cref="InvalidOperationException">이미 군중 묶음에 소속돼 있을 때</exception>
        /// <remarks>직접 추적으로 쥐고 있던 목표는 묶음에 목표가 없을 때만 넘긴다 (묶음 SetTarget 은 예약을 모두 비우므로).</remarks>
        public void JoinCrowd(CrowdController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (crowdController != null) throw new InvalidOperationException("이미 군중 묶음에 소속돼 있다");

            assignedController = controller;
            JoinAssignedCrowd();
            bool isActivated = started && isActiveAndEnabled;
            if (isActivated == false) return;

            IPlayerTarget directTarget = targetInfo;
            RegisterAssignedCrowd();
            bool shouldHandOverTarget = controller.Target == null && directTarget != null;
            if (shouldHandOverTarget) controller.SetTarget(directTarget);
        }

        /// <summary>
        /// 묶음 배정을 지운다. 다음 활성화는 직접 추적. MonsterPool 이 반납받은 몬스터를 끈 뒤 부른다 (재사용 때 옛 묶음에 다시 들어가지 않게).
        /// </summary>
        /// <exception cref="InvalidOperationException">활성 상태일 때 (소속 해제는 OnDisable 이 한다)</exception>
        public void ClearCrowdAssignment()
        {
            if (isActiveAndEnabled) throw new InvalidOperationException("ClearCrowdAssignment 는 비활성 상태에서만 호출할 수 있다");
            assignedController = null;
        }

        /// <summary>
        /// 직접 추적 모드만 스스로 갱신한다 (매 프레임 TickMovement, 0.1초마다 Think).
        /// 군중 모드는 CrowdController.Update 가 Think 와 TickMovement 를 순서대로 호출하므로 여기서는 아무것도 하지 않는다.
        /// </summary>
        private void Update()
        {
            if (crowdController != null || isDirectMode == false) return;
            TickMovement();
            if (Time.time < nextDirectThink) return;
            Think(targetInfo, Time.time);
            nextDirectThink = Time.time + 0.1f;
        }

        /// <summary>모든 Update(이동·NavMeshAgent 갱신) 이후 매 프레임 시선 회전을 적용한다</summary>
        private void LateUpdate() => ApplyLookRotation();

        /// <summary>
        /// 이동 방향이 아니라 플레이어(lookTarget) 쪽을 수평으로 바라보게 한다. LateUpdate 가 매 프레임 호출한다.
        /// 지수 보간(Slerp)이라 멀리 돌수록 빠르고 가까워질수록 부드럽게 멈춘다. 정지(suspended)·목표 무효 시에는 회전하지 않는다.
        /// </summary>
        /// <remarks>NavMeshAgent.updateRotation 이 꺼져 있어야 하므로 NavMeshMoveExecutor.Awake 에서 끈다.</remarks>
        private void ApplyLookRotation()
        {
            if (hasLookTarget == false) return;
            if (suspended) return;

            Vector3 dir = lookTarget - transform.position;
            dir.y = 0f;
            if (dir.magnitude < 0.01f) return;

            float t = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime); // 프레임레이트와 무관한 보간 비율
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), t);
        }

        /// <summary>
        /// 프레임 갱신: 슬롯 예약이 사라졌으면 정지하고, 반응 지연이 끝난 의도를 실행기에 적용한 뒤,
        /// 추적 목적지를 최신 목적지 쪽으로 당겨 실행기에 다시 넘긴다.
        /// 군중 모드는 CrowdController 가 Think 직후 호출, 직접 모드는 Update 가 호출.
        /// </summary>
        internal void TickMovement()
        {
            if (IsReactionReservationLost())
                StopForInvalidReservation();
            if (hasPendingIntent)
            {
                if (Time.time >= pendingApplyAt)
                {
                    hasPendingIntent = false;
                    reactionReferenceDestination = pendingIntent.Destination;
                    CommitIntent(in pendingIntent);
                }
            }
            FollowDestination();
        }

        /// <summary>
        /// 추적 목적지를 마지막 의도의 목적지 쪽으로 한 프레임만큼 당기고, 그 목적지로 실행기를 갱신한다. TickMovement 가 매 프레임 호출한다.
        /// Think(0.5초)마다 계단식으로 점프하는 목적지를 연속으로 움직이는 점으로 바꿔, 빠른 몬스터가 낡은 목적지에 먼저 닿아 멈칫하지 않게 한다.
        /// </summary>
        /// <remarks>상태·실행 슬롯은 CommitIntent 에서만 정하므로 Apply 결과는 쓰지 않는다. 슬롯 거절은 Think 가 executor.ExecutionState 로 판단한다.</remarks>
        private void FollowDestination()
        {
            if (suspended) return;
            if (lastIntent.HasDestination == false) return;
            if (hasFollowDestination == false) return;

            followDestination = StepTowardDestination(followDestination, lastIntent.Destination, destinationFollowRate, Time.deltaTime);
            executor.Apply(new MoveIntent(lastIntent.State, followDestination, lastIntent.Speed, lastIntent.StopDistance));
        }

        /// <summary>
        /// 지수 감쇠 보간 한 걸음. 남은 거리의 일정 비율만 따라가므로 도중에 목표가 바뀌어도 지금 위치에서 새 목표 쪽으로 이어서 움직인다.
        /// FollowDestination 이 매 프레임 호출한다. 비율은 1 − e^(−rate·Δt) 라 프레임레이트와 무관하고 target 을 넘지 않는다.
        /// </summary>
        /// <param name="current">지금 추적 목적지 (월드)</param>
        /// <param name="target">따라갈 목적지 (월드)</param>
        /// <param name="rate">따라잡는 빠르기 (1/s). 0 = 보간 끔</param>
        /// <param name="deltaTime">경과 시간 (s)</param>
        /// <returns>한 걸음 옮긴 추적 목적지 (월드). rate 가 0 이면 target 그대로</returns>
        private static Vector3 StepTowardDestination(Vector3 current, Vector3 target, float rate, float deltaTime)
        {
            if (rate == 0f) return target;

            float t = 1f - Mathf.Exp(-rate * deltaTime);
            return Vector3.Lerp(current, target, t);
        }

        /// <summary>
        /// 반응 지연 기준점을 잡은 슬롯 예약이 사라졌는가 (목표 소실 또는 슬롯 회수·무효화). TickMovement 가 정지 여부를 정할 때 호출한다.
        /// </summary>
        /// <returns>기준점이 있는데 목표가 없거나 그 슬롯이 더 이상 내 예약이 아니면 true. 기준점이 없으면 false</returns>
        private bool IsReactionReservationLost()
        {
            if (hasReactionReference == false) return false;
            if (Target == null) return true;
            return HasCurrentReservation(reactionSlotId) == false;
        }

        /// <summary>
        /// 비활성화/파괴(몬스터 사망 포함) 시 정리: 군중 관리자 등록 해제(점유 중인 슬롯·토큰 반환)·묶음 소속 해제 후 이동 정지, 상태를 Idle 로 되돌린다.
        /// 이 덕분에 몬스터가 중간에 제거돼도 슬롯이 다른 몬스터에게 돌아가고, 마지막 소속이 빠지면 묶음이 정리된다.
        /// </summary>
        private void OnDisable()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            hasLookTarget = false;
            if (crowdController != null)
            {
                UnregisterCrowd();
                crowdController.Leave(this);
                crowdController = null;
            }
            if (executor != null) executor.Stop();
            hasFollowDestination = false;
            suspended = false;
            retainReservation = false;
            SetState(MovementState.Idle);
            isInAttackRange = false;
            lastIntent = MoveIntent.Stop(MovementState.Idle);
        }

        /// <summary>
        /// 런타임 생성 몬스터의 profile 과 군중 묶음을 설정한다. 이전 직접 추적 설정과 정책을 비우고, 다음 활성화 때 새 profile 로 정책을 만든다.
        /// </summary>
        /// <param name="profile">이동 수치·정책 종류</param>
        /// <param name="controller">통제받을 군중 묶음. null 이면 활성화 시 직접 추적 모드</param>
        /// <exception cref="InvalidOperationException">이미 활성 상태일 때 (OnEnable 전에만 호출 가능)</exception>
        public void Configure(MovementProfile profile, CrowdController controller)
        {
            if (isActiveAndEnabled)
                throw new InvalidOperationException("Configure 는 비활성 상태에서만 호출할 수 있다");
            this.profile = profile;
            assignedController = controller;
            targetInfo = null;
            isDirectMode = false;
            directTargetSource = null;
            policy = null; // 활성화 시 새 profile 로 재생성
        }

        /// <summary>
        /// 군중 관리자 없이 목표를 직접 추적하는 모드로 설정한다.
        /// 군중 관리자 지정과 정책을 비우고, 다음 활성화 때 새 profile 로 정책을 만든다.
        /// </summary>
        /// <param name="profile">이동 수치·정책 종류</param>
        /// <param name="target">직접 추적할 목표</param>
        /// <exception cref="InvalidOperationException">이미 활성 상태일 때 (OnEnable 전에만 호출 가능)</exception>
        /// <exception cref="ArgumentNullException">target 이 null 일 때</exception>
        public void ConfigureDirect(MovementProfile profile, IPlayerTarget target)
        {
            if (isActiveAndEnabled) throw new InvalidOperationException("ConfigureDirect 는 비활성 상태에서만 호출할 수 있다");
            this.profile = profile;
            if (target == null) throw new ArgumentNullException(nameof(target));
            targetInfo = target;
            isDirectMode = true;
            directTargetSource = target as MonoBehaviour;
            assignedController = null;
            policy = null;
        }

        /// <summary>
        /// Def/Attack의 개체별 수치를 이동에 반영한다. MonsterController.Start 가 공격 조립 직후 호출한다.
        /// Profile 원본은 변경하지 않는다.
        /// </summary>
        /// <param name="moveSpeed">개체별 이동 속도 (m/s, 양수)</param>
        /// <param name="attackRange">현재 공격 사거리 (m, 양수)</param>
        /// <exception cref="ArgumentOutOfRangeException">moveSpeed 또는 attackRange 가 0 이하일 때</exception>
        /// <exception cref="InvalidOperationException">슬롯 모드에서 접근 슬롯 반경 + 여유가 사거리 이상일 때 (ValidateCombatSlots)</exception>
        public void SetCombatTuning(float moveSpeed, float attackRange)
        {
            if (moveSpeed <= 0f || attackRange <= 0f)
                throw new ArgumentOutOfRangeException(nameof(attackRange), "전투 이동 속도와 사거리는 양수여야 한다");
            combatSpeed = moveSpeed;
            combatRange = attackRange;
            hasCombatTuning = true;
            if (profile != null) ApplyCombatTuning();
            ValidateCombatSlots();
        }

        /// <summary>
        /// 원거리 시야 밖 접근 속도 배율을 개체별로 정한다. MonsterController 가 SetCombatTuning 직후 PounceSettings 값으로 호출한다.
        /// 부르지 않으면 1 (기본 속도).
        /// </summary>
        /// <param name="multiplier">테이블 이동 속도에 곱할 배율</param>
        public void SetFlankApproachSpeedMultiplier(float multiplier)
        {
            flankApproachSpeedMultiplier = multiplier;
            if (profile != null) ApplyCombatTuning();
        }

        /// <summary>
        /// profile 수치에 개체별 이동 속도·공격 사거리·원거리 접근 배율을 합쳐 tuning 에 넣는다.
        /// Activate, SetCombatTuning, SetFlankApproachSpeedMultiplier 가 호출한다.
        /// </summary>
        /// <remarks>SetCombatTuning 전에 Activate 되면 속도·사거리가 0 인 채로 들어간다. Think 가 SetCombatTuning 누락을 예외로 드러낸다.</remarks>
        private void ApplyCombatTuning()
        {
            tuning = profile.ToTuning(combatSpeed, combatRange, flankApproachSpeedMultiplier);
        }

        /// <summary>
        /// 슬롯 모드에서 접근 슬롯에 도착한 몬스터가 실제로 공격 사거리 안에 들어오는지 검사한다.
        /// Activate(군중 모드)와 SetCombatTuning 이 호출한다.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// 개체별 수치·군중 관리자·토큰 대상·군중 제어·슬롯 모드가 모두 켜져 있고,
        /// 접근 슬롯 반경 + NavMesh 투영 반경 + 정지 여유가 공격 사거리 이상일 때
        /// </exception>
        private void ValidateCombatSlots()
        {
            bool hasTuning = hasCombatTuning;
            bool hasController = crowdController != null;
            bool usesEngageToken = profile.UsesEngageToken;

            bool crowdControlEnabled = hasController && crowdController.Settings.EnableCrowdControl;
            bool slotsEnabled = hasController && crowdController.Settings.UseSlots;

            bool invalidApproachRadius = false;

            // 접근 슬롯 반경 + NavMesh 투영 반경 + 정지 여유가 사거리 안에 들어오는지 계산
            if (hasController)
            {
                var slots = crowdController.Settings.Slots;

                float approachRadius = slots.ApproachRadius;
                float sampleRadius = slots.SampleRadius;
                float arrivalMargin = Mathf.Min(0.2f, slots.ArrivalDistance * 0.5f);

                float requiredRange = approachRadius + sampleRadius + arrivalMargin;

                invalidApproachRadius = requiredRange >= combatRange;
            }

            if (hasTuning &&
                hasController &&
                usesEngageToken &&
                crowdControlEnabled &&
                slotsEnabled &&
                invalidApproachRadius)
            {
                throw new InvalidOperationException(
                    "접근 슬롯 반경 + NavMesh 투영/정지 여유는 공격 사거리보다 작아야 한다");
            }
        }

        /// <summary>
        /// 직접/군중 모드의 공유 목표를 연결한다. MonsterController 가 목표 확보·상실 시 호출한다.
        /// 군중 묶음(활성화 전이면 배정된 묶음)이 있으면 그쪽(CrowdController.SetTarget)에 넘기고, 없으면 직접 추적 목표로 쥔다.
        /// </summary>
        /// <param name="target">추적할 목표. null = 목표 상실(사망 등)이며 정지한다</param>
        public void SetTarget(IPlayerTarget target)
        {
            if (crowdController != null) crowdController.SetTarget(target);
            else if (assignedController != null) assignedController.SetTarget(target);
            else targetInfo = target;
        }

        /// <summary>군중 관리자에 한 번만 등록한다 (이미 등록됐거나 군중 관리자가 없으면 무시). Activate 와 정지 해제 시 호출</summary>
        private void RegisterCrowd()
        {
            if (crowdController == null) return;
            if (registered) return;
            crowdController.Register(this);
            registered = true;
        }

        /// <summary>군중 관리자 등록을 해제해 슬롯·토큰을 반환한다 (등록 안 됐으면 무시). OnDisable 과 예약 유지 없는 정지 시 호출</summary>
        private void UnregisterCrowd()
        {
            if (registered == false) return;
            crowdController.Unregister(this);
            registered = false;
        }

        /// <summary>
        /// 외부 정지 요청(공격·경직 등)을 받아 이동을 멈추거나 재개한다. MonsterStateMachine 이 호출한다.
        /// 공격 중에만 예약 유지. 그 외 정지는 등록·슬롯·토큰을 즉시 반환한다.
        /// </summary>
        /// <param name="suspended">true = 정지(상태 Suspended), false = 재개 (상태는 다음 Think 에서 재계산)</param>
        /// <param name="retainReservation">정지 중에도 등록·슬롯·토큰을 유지할지. suspended 가 false 면 무시</param>
        public void SetSuspended(bool suspended, bool retainReservation = false)
        {
            this.suspended = suspended;
            this.retainReservation = suspended && retainReservation;
            if (suspended == false)
            {
                RegisterCrowd();
                return; // 상태는 다음 Think 에서 재계산
            }
            isInAttackRange = false;
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            if (retainReservation == false) UnregisterCrowd();
            executor.Stop();
            hasFollowDestination = false;
            lastIntent = MoveIntent.Stop(MovementState.Suspended);
            SetState(MovementState.Suspended);
        }

        /// <summary>
        /// 공격·준비 동작 중 몸을 직접 옮긴다 (돌진 등). 실행기에 그대로 맡긴다.
        /// ChargeAttack 이 Attack(돌진)과 Idle(준비 동작) 상태에서 호출한다. 두 상태 모두 SetSuspended(true) 로 경로 이동이 멈춰 있다.
        /// </summary>
        /// <param name="displacement">이번 틱 이동량 (월드, m)</param>
        /// <returns>true = 벽에 막혔다</returns>
        /// <exception cref="InvalidOperationException">정지(suspended) 상태가 아닐 때. 경로 이동과 직접 이동이 겹치지 않게 한다</exception>
        public bool MoveDuringAttack(Vector3 displacement)
        {
            if (suspended == false) throw new InvalidOperationException("MoveDuringAttack 은 이동이 정지(suspended)된 상태에서만 쓴다");
            return executor.MoveBy(displacement);
        }

        // ── CrowdController 전용 (internal) ──

        /// <summary>토큰 배분 입력용 현재 상태를 만든다. CrowdController 가 배분 전에 호출한다</summary>
        /// <returns>위치·토큰·대기 시각·선회 방향·슬롯 번호를 담은 배분 입력 값</returns>
        internal AllocationAgent BuildAllocationAgent() => new AllocationAgent
        {
            Position = transform.position,
            UsesToken = profile.UsesEngageToken,
            HasToken = hasToken,
            WaitingSince = waitingSince,
            MinWaitSeconds = profile.MinWaitSeconds,
            OrbitSign = orbitSign,
            SlotIndex = slotIndex,
        };

        /// <summary>배분 결과 반영 (지시·토큰·대기 시각·선회 방향·슬롯). CrowdController 가 배분 후 호출한다</summary>
        /// <param name="result">이 개체에 대한 배분 결과</param>
        internal void ApplyAllocation(in AllocationResult result)
        {
            directive = result.Directive;
            hasToken = result.HasToken;
            waitingSince = result.WaitingSince;
            orbitSign = result.OrbitSign;
            slotIndex = result.SlotIndex;
        }

        /// <summary>
        /// 판단 1회: (슬롯 모드면 지시 갱신) → 정책 Evaluate → 실행기 Apply → 공격 사거리 갱신.
        /// 군중 모드는 CrowdController.Update 가 라운드 로빈으로, 직접 모드는 Update 가 0.1초마다 호출한다. 정지 중이면 아무것도 하지 않는다.
        /// </summary>
        /// <param name="target">추적 목표. null = 목표 무효 (정책이 Idle 정지를 낸다)</param>
        /// <param name="now">현재 시각 (s, Time.time 기준)</param>
        /// <exception cref="InvalidOperationException">SetCombatTuning 으로 이동 속도·사거리를 받기 전에 판단할 때</exception>
        internal void Think(IPlayerTarget target, float now)
        {
            if (suspended) return;
            if (hasCombatTuning == false)
                throw new InvalidOperationException("SetCombatTuning 으로 이동 속도·사거리를 먼저 넣어야 한다");

            // 1) 슬롯 모드: 도달 불가였던 슬롯은 거절하고, 군중 관리자에게 최신 지시(슬롯)를 받는다
            if (IsSlotMode())
            {
                if (executingSlotId >= 0 && executor.ExecutionState == MoveExecutionState.Unreachable)
                {
                    crowdController.RejectSlot(this, executingSlotId, now);
                    executingSlotId = -1;
                }
                directive = crowdController.RequestDirective(this, now);
            }

            // 2) 정책 입력(Context) 구성. 목표가 무효면 자기 위치를 목표로 둔다 (정책이 TargetValid 로 걸러냄)
            //    시야 값은 플레이어 카메라(ViewPoint)에서 꺼낸다. 침투형(FlankPolicy)의 "시야 밖" 판정용
            float dt = hasThought ? now - lastThinkTime : 0f;
            hasThought = true;
            lastThinkTime = now;

            Vector3 self = transform.position;
            bool targetValid = target != null;
            Vector3 targetPosition = targetValid ? target.PlayerTrackingInfo.Position : self;
            Vector3 viewForward = Vector3.forward;
            Vector3 viewPosition = self;
            // float viewHalfAngleDeg = 0f;
            if (targetValid)
            {
                Transform viewPoint = target.ViewPoint;
                viewForward = viewPoint.forward;
                viewPosition = viewPoint.position;
                viewHalfAngleDeg = HorizontalHalfFovDeg(viewPoint.GetComponent<Camera>());
            }
            lookTarget = targetPosition;   // 시선 갱신 (실제 회전은 LateUpdate 의 ApplyLookRotation)
            hasLookTarget = targetValid;
            var ctx = new MovementContext(self, targetPosition, viewForward, viewPosition, viewHalfAngleDeg, targetValid,
                                           directive, tuning, now, dt);
            // 3) 정책이 "어디로 갈지" 결정 → 반응 지연 규칙을 거쳐 실행기에 전달
            var intent = policy.Evaluate(in ctx);
            SubmitIntent(in intent, target, targetValid, now);

            // 4) 공격 사거리 안인지 갱신 (IsInAttackRange 로 공개)
            isInAttackRange = targetValid && HorizontalDistance(targetPosition, self) <= tuning.AttackRange;
        }

        /// <summary>
        /// 카메라 FOV 의 수평 반각을 구한다. Unity 의 fieldOfView 는 세로 FOV 라서 화면 비율로 가로로 바꾼다.
        /// Think 가 플레이어 카메라의 시야 반각을 정책 입력에 담을 때 호출한다.
        /// </summary>
        /// <param name="viewCamera">플레이어 ViewPoint 의 카메라</param>
        /// <returns>수평 반각 (도) = atan(tan(세로 FOV / 2) × aspect)</returns>
        private static float HorizontalHalfFovDeg(Camera viewCamera)
        {
            float verticalHalfRad = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float horizontalHalfRad = Mathf.Atan(Mathf.Tan(verticalHalfRad) * viewCamera.aspect);
            return horizontalHalfRad * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 슬롯 모드(군중 관리자 있음 + 슬롯 사용 + 군중 제어 켜짐)인가.
        /// Think 가 지시를 갱신할지, SubmitIntent 가 반응 지연을 적용할지 정할 때 호출한다.
        /// </summary>
        /// <returns>세 조건이 모두 참이면 true. 직접 추적 모드(군중 관리자 없음)면 false</returns>
        private bool IsSlotMode()
        {
            if (crowdController == null) return false;
            return crowdController.Settings.UseSlots && crowdController.Settings.EnableCrowdControl;
        }

        /// <summary>등록 해제 시 토큰·대기·슬롯·지시 상태 초기화 (선회 방향은 유지). CrowdController.Unregister 가 호출한다</summary>
        internal void ResetCrowdState()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            hasToken = false;
            waitingSince = -1f;
            slotIndex = -1;
            directive = CrowdDirective.None;
        }

        /// <summary>
        /// Think 가 정책의 의도를 실행기에 보내기 전 "반응 지연"을 적용한다.
        /// 슬롯 목적지가 기준점에서 ReactionChangeDistance 이상 바뀌면 랜덤 시간 뒤에 반응해 몬스터들이 동시에 꺾이는 부자연스러움을 줄인다.
        /// 슬롯 모드가 아니거나 슬롯이 없으면 지연 없이 즉시 실행.
        /// </summary>
        /// <param name="intent">정책이 이번 판단에서 낸 이동 의도</param>
        /// <param name="target">현재 목표. 기준점을 잡은 목표와 다르면 기준점을 새로 잡는다</param>
        /// <param name="targetValid">목표가 유효한가</param>
        /// <param name="now">현재 시각 (s). 지연 적용 시각 계산에 쓴다</param>
        /// <remarks>지연을 시작하면 CrowdController.PauseSlotProgress 로 "슬롯으로 진행이 없다" 판정 시계를 지연 시간만큼 늦춘다.</remarks>
        private void SubmitIntent(in MoveIntent intent, IPlayerTarget target, bool targetValid, float now)
        {
            // 1) 지연 대상이 아닌 경우: 슬롯 모드가 아니거나, 목표/목적지/슬롯이 없거나, 예약이 이미 무효 → 즉시 실행
            bool slotMode = IsSlotMode();
            if (CanDelayIntent(slotMode, in intent, targetValid) == false)
            {
                bool targetLost = targetValid == false;
                bool destinationMissing = intent.HasDestination == false;
                bool slotPositionMissing = slotMode && directive.HasSlotPosition == false;
                CancelPendingIntent();
                hasReactionReference = false;
                if (targetLost || destinationMissing || slotPositionMissing)
                    executingSlotId = -1;
                CommitIntent(in intent);
                return;
            }

            // 2) 기준점이 없거나 슬롯/목표가 바뀌었으면: 새 기준점을 잡고 즉시 실행 (슬롯이 바뀌면 이전 이동은 멈춤)
            int slotId = directive.SlotId;
            bool referenceMissing = hasReactionReference == false;
            bool targetChanged = ReferenceEquals(reactionTargetInfo, target) == false;
            bool slotChanged = reactionSlotId != slotId;
            if (referenceMissing || targetChanged || slotChanged)
            {
                CancelPendingIntent();
                if (hasReactionReference && slotChanged)
                {
                    executor.Stop();
                    hasFollowDestination = false;
                    executingSlotId = -1;
                }
                reactionTargetInfo = target;
                reactionSlotId = slotId;
                reactionReferenceDestination = intent.Destination;
                hasReactionReference = true;
                CommitIntent(in intent);
                return;
            }

            // 3) 같은 슬롯을 계속 추적 중: 목적지 변화량이 임계값 이상인지 판단.
            //    이미 지연 중이면 최신 의도로 갱신(변화 유지) 또는 취소(변화 사라짐), 아니면 지연을 새로 시작
            float changeDistance = HorizontalDistance(intent.Destination, reactionReferenceDestination);
            bool changed = changeDistance > 0f && changeDistance >= profile.ReactionChangeDistance;
            if (hasPendingIntent)
            {
                if (changed)
                {
                    pendingIntent = intent;
                    return;
                }
                CancelPendingIntent();
            }
            else if (changed && profile.ReactionDelayMax > 0f)
            {
                float delay = UnityEngine.Random.Range(profile.ReactionDelayMin, profile.ReactionDelayMax);
                if (delay > 0f)
                {
                    pendingIntent = intent;
                    hasPendingIntent = true;
                    pendingApplyAt = now + delay;
                    crowdController.PauseSlotProgress(this, delay);
                    return;
                }
            }

            // 4) 지연할 필요 없음: 기준점 갱신 후 즉시 실행
            if (changed) reactionReferenceDestination = intent.Destination;
            CommitIntent(in intent);
        }

        /// <summary>
        /// 이 의도에 반응 지연을 적용할 수 있는가. SubmitIntent 가 즉시 실행할지 정할 때 호출한다.
        /// 조건은 앞에서부터 확인하며, 슬롯 예약 조회는 앞 조건이 모두 참일 때만 한다.
        /// </summary>
        /// <param name="slotMode">슬롯 모드인가 (IsSlotMode 결과)</param>
        /// <param name="intent">정책이 이번 판단에서 낸 이동 의도</param>
        /// <param name="targetValid">목표가 유효한가</param>
        /// <returns>슬롯 모드 + 목표 유효 + 목적지 있음 + 슬롯 위치 있음 + 그 슬롯이 아직 내 예약이면 true</returns>
        private bool CanDelayIntent(bool slotMode, in MoveIntent intent, bool targetValid)
        {
            if (slotMode == false) return false;
            if (targetValid == false) return false;
            if (intent.HasDestination == false) return false;
            if (directive.HasSlotPosition == false) return false;
            return HasCurrentReservation(directive.SlotId);
        }

        /// <summary>
        /// 의도를 확정해 실행기에 적용하고, 결과에 따라 "실행 중인 슬롯"과 상태를 갱신한다. SubmitIntent 와 TickMovement(지연 의도)가 호출한다.
        /// 실행기에는 원래 목적지 대신 추적 목적지를 넘긴다. 추적 목적지가 없으면(첫 이동·정지 뒤) 이번 목적지로 바로 놓는다.
        /// </summary>
        /// <param name="intent">확정할 이동 의도 (목적지는 보간 전 원래 값)</param>
        private void CommitIntent(in MoveIntent intent)
        {
            lastIntent = intent;
            MoveIntent appliedIntent = ToFollowIntent(in intent);
            MoveApplyResult result = executor.Apply(in appliedIntent);
            if (result == MoveApplyResult.PathSet || result == MoveApplyResult.Unreachable)
                executingSlotId = directive.HasSlotPosition ? directive.SlotId : -1;
            else if (result == MoveApplyResult.Stopped)
                executingSlotId = -1;
            SetState(intent.State);
        }

        /// <summary>
        /// 확정한 의도를 실행기에 넘길 의도로 바꾼다. CommitIntent 가 호출한다.
        /// 목적지 없는 의도면 추적 목적지를 비우고 그대로 넘긴다. 추적 목적지가 없으면 이번 목적지로 바로 놓는다.
        /// </summary>
        /// <param name="intent">확정한 이동 의도</param>
        /// <returns>목적지만 추적 목적지로 바꾼 의도. 목적지 없는 의도면 intent 그대로</returns>
        private MoveIntent ToFollowIntent(in MoveIntent intent)
        {
            if (intent.HasDestination == false)
            {
                hasFollowDestination = false;
                return intent;
            }
            if (hasFollowDestination == false)
            {
                followDestination = intent.Destination;
                hasFollowDestination = true;
            }
            return new MoveIntent(intent.State, followDestination, intent.Speed, intent.StopDistance);
        }

        /// <summary>
        /// slotId 가 아직 내 예약이며 유효한 슬롯인가.
        /// 스냅샷(혼잡 판정 포함) 대신 코디네이터의 O(1) 조회를 쓴다.
        /// </summary>
        /// <param name="slotId">확인할 슬롯 번호</param>
        /// <returns>내 예약이면 true. 슬롯이 무효화/회수됐거나 군중 관리자가 없으면 false</returns>
        private bool HasCurrentReservation(int slotId) =>
            crowdController != null && crowdController.IsSlotReservedBy(this, slotId);

        /// <summary>예약 슬롯이 사라졌을 때(타겟 소실·NavMesh 변경 등) 이동을 멈추고 Idle 로 돌아간다. TickMovement 가 호출하며, 다음 Think 에서 새 슬롯을 받는다</summary>
        private void StopForInvalidReservation()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            executor.Stop();
            hasFollowDestination = false;
            lastIntent = MoveIntent.Stop(MovementState.Idle);
            SetState(MovementState.Idle);
        }

        /// <summary>반응 지연 중인 의도를 버린다</summary>
        private void CancelPendingIntent() => hasPendingIntent = false;

        /// <summary>높이를 무시한 수평(XZ) 거리. Think 의 공격 사거리 판정과 SubmitIntent 의 목적지 변화량 비교에 쓴다</summary>
        /// <param name="a">첫 위치 (월드)</param>
        /// <param name="b">둘째 위치 (월드)</param>
        /// <returns>수평(XZ) 거리 (m)</returns>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>상태 변경. 실제로 바뀐 경우에만 StateChanged 이벤트 발생</summary>
        /// <param name="next">바꿀 이동 상태</param>
        private void SetState(MovementState next)
        {
            if (state == next) return;
            var prev = state;
            state = next;
            StateChanged?.Invoke(this, prev, next);
        }
    }
}
