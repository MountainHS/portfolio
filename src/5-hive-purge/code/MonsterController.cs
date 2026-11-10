using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 총에 맞았을 때 몬스터의 추가 반응. MonsterController 인스펙터에서 몬스터마다 고른다.
    /// None = 반응 없음 (엘리트 등), Stun = 잠깐 경직, Slow = 잠깐 이동 감속.
    /// </summary>
    public enum HitReaction
    {
        None,
        Stun,
        Slow
    }

    /// <summary>
    /// 몬스터 1마리의 Unity 진입점 (Facade).
    /// 체력(단일 체력. 약점은 배율만 곱해 같은 체력을 깎는다)을 직접 가지고(IDamageable), 매 프레임 상태 머신을 돌리고,
    /// 게임 이벤트(보스 정리·라운드 정리·플레이어 사망)를 상태 머신에 전달한다.
    /// </summary>
    /// <remarks>
    /// 조립 순서: Awake = Def·체력 준비 → Start = 공격·상태 머신 조립.
    /// 기본 IMonsterFactory 는 프리팹을 Instantiate 만 하므로 초기화는 이 클래스가 스스로 한다.
    /// 라이플·수류탄은 IDamageable 로 이 클래스를 찾아 피해를 준다.
    /// </remarks>
    [RequireComponent(typeof(MonsterMovement))]
    public sealed class MonsterController : MonoBehaviour, IDamageable, IMonsterSoundSource
    {
        [Tooltip("MonsterTable Id")]
        [SerializeField] private int monsterId;

        [Header("애니메이션")]
        [Tooltip("Move 클립을 1배속으로 재생할 때의 이동 속도 (m/s)")]
        [SerializeField, Min(0.01f)] private float moveClipSpeed = 3f;
        [Tooltip("Chase 중 이 속도 미만이면 Idle 애니를 재생 (m/s)")]
        [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.1f;

        [Header("공격")]
        [Tooltip("켜면 데이터 테이블 Cooldown 대신 아래 값을 쓴다. 다음 공격 시작부터 반영")]
        [SerializeField] private bool useCooldownOverride;
        [Tooltip("회복이 끝난 뒤 다음 공격 시작까지 기다리는 시간 (s)")]
        [SerializeField, Min(0f)] private float cooldownOverrideSeconds = 1f;
        [Tooltip("Active 구간 최소 유지 시간 (s). 도약은 이 시간 동안 날고, Attack 클립은 이 시간에 맞춰 1번 재생된다. 다음 공격 시작부터 반영")]
        [SerializeField, Min(0.01f)] private float activeSeconds = 0.5f;
        [Tooltip("Active 구간 중 판정 시점 비율. 0 = Active 시작, 1 = Active 끝. 다음 공격 시작부터 반영")]
        [SerializeField, Range(0f, 1f)] private float hitTimeRatio = 0.5f;
        [Tooltip("근접 판정 구간 끝 비율. 판정 구간 = Active 의 [판정 시점 비율, 이 값]. 판정 시점 비율 이하 = 판정 시점 1틱만. 근접(Strike)만 쓴다. 다음 공격 시작부터 반영")]
        [SerializeField, Range(0f, 1f)] private float hitEndRatio = 0.5f;
        [Tooltip("근접 판정 구간 동안 최대 명중 횟수. 1 = 1번만. 명중 1번 = 테이블 Damage × HitCount. 다음 판정부터 반영")]
        [SerializeField, Min(1)] private int maxHitsPerAttack = 1;
        [Tooltip("근접 판정 구간 안 반복 명중 사이 최소 간격 (s). 다음 판정부터 반영")]
        [SerializeField, Min(0f)] private float hitIntervalSeconds = 0f;
        [Tooltip("근접 판정 거리 = 테이블 Range × 이 비율. 1 = 진입 거리와 같음. 예고 동안 같은 방향으로 도망가는 플레이어를 맞히려면 (Range + 플레이어 속도 × 판정 시각 + 0.2) ÷ Range. 다음 판정부터 반영")]
        [SerializeField, Min(1f)] private float hitRangeRatio = 1f;
        [Tooltip("예고 중 바라볼 지점으로 도는 속도 (도/초). 0 = 돌지 않는다. 다음 프레임부터 반영")]
        [SerializeField, Min(0f)] private float windupTurnSpeed = 360f;

        [Header("저체력")]
        [Tooltip("현재 체력이 최대 체력의 이 비율 이하면 저체력. 느려지고 붉어진다 (0~1)")]
        [SerializeField, Range(0f, 1f)] private float lowHealthRatio = 0.3f;
        [Tooltip("저체력일 때 이동 속도 배율. 테이블 MoveSpeed × 이 값. 돌진·급강하 같은 공격 이동은 그대로")]
        [SerializeField, Range(0.05f, 1f)] private float lowHealthSpeedRatio = 0.6f;

        [Header("피격 반응")]
        [Tooltip("총에 맞았을 때 반응. None = 없음 (엘리트), Stun = 일정 시간 안에 쌓인 피해가 임계값 이상이면 잠깐 경직, Slow = 잠깐 감속")]
        [SerializeField] private HitReaction hitReaction = HitReaction.None;
        [Tooltip("Stun: 누적 시간 안에 쌓인 피해가 이 값 이상이면 경직한다 (HP). 약점 배율을 곱한 피해로 센다. 0 = 모든 피격에 경직")]
        [SerializeField, Min(0)] private int hitStunDamageThreshold = 0;
        [Tooltip("Stun: 피해를 쌓는 시간 (s). 첫 피격부터 센다. 지나면 다음 피격부터 다시 쌓는다")]
        [SerializeField, Min(0.01f)] private float hitStunDamageWindowSeconds = 1f;
        [Tooltip("Stun: 경직 시간 (s). 공격 중이면 공격을 끊는다")]
        [SerializeField, Min(0f)] private float hitStunSeconds = 0.3f;
        [Tooltip("Stun: 경직이 끝난 뒤 다시 경직되지 않는 시간 (s)")]
        [SerializeField, Min(0f)] private float hitStunCooldownSeconds = 1f;
        [Tooltip("Slow: 슬로우 중 이동 속도 배율. 저체력 배율과 곱한다. 돌진·급강하 같은 공격 이동은 그대로")]
        [SerializeField, Range(0.05f, 1f)] private float hitSlowRatio = 0.5f;
        [Tooltip("Slow: 마지막 피격부터 슬로우 유지 시간 (s). 맞을 때마다 다시 센다")]
        [SerializeField, Min(0f)] private float hitSlowSeconds = 1f;

        // Base Controller 의 Attack 상태 원본 클립 이름. Override 인덱서는 원본 클립 이름으로 슬롯을 찾는다
        private const string BaseAttackClipName = "Placeholder_Attack";

        private MonsterMovement movement;
        private PounceSettings pounceSettings; // 측면 침투형(도약)만 있음. null = 원거리 접근 배율 없음
        private bool playerDead;
        private bool dormant;              // 휴면: 대상을 잡지 않는다 (SetDormant). 재사용(Respawn) 때 꺼진다
        private bool despawned;

        // ── 피격·사망 표현 ──
        private MonsterDamageFeedback damageFeedback;
        private MonsterCorpse corpse;
        private Vector3 lastHitDirection;  // 마지막 피격 방향 (공격자 → 몬스터, 월드). 0 = 아직 안 맞음. 시체를 미는 방향

        // ── 피격 반응 ──
        private float nextHitStunTime;     // 이 시각 전에는 경직하지 않는다 (s, Time.time 기준)
        private float hitSlowEndTime;      // 이 시각까지 슬로우 (s, Time.time 기준)
        private bool isHitSlowed;          // 슬로우 적용 중. 끝날 때 속도를 1번만 되돌리려고 둔다
        private int stunDamageTotal;           // 이번 누적 시간 동안 쌓인 피해 (HP)
        private float stunDamageWindowEndTime; // 이 시각까지 피해를 쌓는다 (s, Time.time 기준)

        // ── 애니메이션 ──
        private Animator animator;

        public int MonsterId => monsterId;
        public MonsterDef Def { get; private set; }
        public MonsterStateMachine StateMachine { get; private set; }
        // 공격 동작(예고·공격·회복)이 진행 중인가. MonsterWeakPoint 가 WhileAttacking 약점 노출을 판단할 때 쓴다
        public bool IsAttacking => StateMachine.CurrentAttack.IsAttacking;

        // ── 소리 알림 (IMonsterSoundSource. MonsterSoundEmitter 가 구독) ──
        public event Action<MonsterAttackDef, SkillPhase> AttackPhaseChanged;
        public event Action<float> Stunned;
        public event Action<MonsterAttackDef, Vector3> AttackLanded;
        public event Action<MonsterAttackDef, Vector3> AttackContacted;
        public event Action Died;

        // ── 체력 ──
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public Faction Faction => Faction.Monster;
        public bool IsAlive => CurrentHp > 0;
        public bool IsLowHealth => CurrentHp <= MaxHp * lowHealthRatio;
        /// <summary>휴면 중 (SetDormant)</summary>
        public bool IsDormant => dormant;

        /// <summary>
        /// 휴면 상태를 켜고 끈다 (튜토리얼 표적 등). 켜져 있으면 대상을 잡지 않아 Idle 로 제자리에 서 있고 공격하지 않는다.
        /// 피격·사망·처치 보상·울음(IdleSfx)은 그대로다. 소환 직후(Start 전)에 불러도 된다. 풀 재사용(Respawn) 때 꺼진다.
        /// </summary>
        /// <param name="value">true = 휴면, false = 다음 Update 부터 다시 대상을 잡는다</param>
        public void SetDormant(bool value)
        {
            dormant = value;
            if (value == false || StateMachine == null) return;
            StateMachine.SetTarget(null);
            SetMovementTarget(null);
        }

        /// <summary>
        /// 컴포넌트 참조를 잡고, monsterId 로 테이블 Def 를 읽어 체력을 채운다.
        /// </summary>
        /// <remarks>다른 컴포넌트의 초기화 결과는 아직 쓰지 않는다. 조립은 Start 가 맡는다.</remarks>
        private void Awake()
        {
            movement = GetComponent<MonsterMovement>();
            animator = GetComponent<Animator>();
            damageFeedback = GetComponent<MonsterDamageFeedback>();
            corpse = GetComponent<MonsterCorpse>();

            Def = DataManager.Monsters.Get(monsterId);
            InitializeHealth(Def.MaxHp);
        }

        /// <summary>
        /// Def 로 공격과 상태 머신을 만들고, 상태 머신·공격·게임 이벤트를 구독한다.
        /// </summary>
        /// <remarks>모든 컴포넌트 Awake 이후에 실행되어야 한다. 상태 머신 생성 직후 Idle.Stop 이 이동 실행기를 쓴다.</remarks>
        private void Start()
        {
            AssembleStateMachine();
            Subscribe();
        }

        /// <summary>
        /// 풀에서 다시 꺼낸 몬스터를 처음 소환된 상태로 되살린다.
        /// 풀(팩토리)이 재사용 개체를 켠 직후 1번 부른다. 처음 만든 개체는 Start 가 조립하므로 부르지 않는다.
        /// </summary>
        /// <remarks>
        /// Dead 는 상태 머신의 종료 상태라 상태 머신을 새로 만든다. 시체 물리·붉은 기도 되돌린다.
        /// 이동 속도는 AssembleStateMachine 의 SetCombatTuning 이 테이블 값으로 되돌린다 (저체력·피격 슬로우 감속 해제).
        /// 피격 반응의 경직 쿨다운·슬로우 시각도 처음 상태로 되돌리고, 경직 판정용 누적 피해도 비운다.
        /// 마지막에 enabled = true 로 OnEnable 을 태워 이동을 켜고 게임 이벤트를 다시 구독한다.
        /// Animator 는 GameObject 를 끄고 켤 때 초기화된다 (Keep Animator State On Disable 꺼짐 전제).
        /// </remarks>
        public void Respawn()
        {
            despawned = false;
            playerDead = false;
            dormant = false;
            InitializeHealth(Def.MaxHp);
            lastHitDirection = Vector3.zero;
            nextHitStunTime = 0f;
            ResetStunDamage();
            hitSlowEndTime = 0f;
            isHitSlowed = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = true; // MonsterCorpse 가 끈 피격 판정 복구
            corpse.Restore(); // 이동 재개(AssembleStateMachine) 전에 NavMeshAgent 를 켠다
            damageFeedback.ResetForRespawn();
            AssembleStateMachine();
            enabled = true;
        }

        /// <summary>
        /// Def 로 공격 목록과 상태 머신을 만들고 상태 머신·공격 이벤트를 구독한다.
        /// Start(최초 조립)와 Respawn(재사용 시 재조립)이 호출한다.
        /// </summary>
        private void AssembleStateMachine()
        {
            // 모든 컴포넌트 Awake 이후 조립한다. Brain 생성 직후 Idle.Stop도 실행기를 안전하게 사용한다.
            var body = new MonsterBody(transform, GetComponent<NavMeshMoveExecutor>());
            var attackSettings = GetComponents<MonsterAttackSettingsComponent>(); // 붙은 공격 설정 전부. 공격마다 필요한 타입만 꺼내 쓴다
            
            // 돌진형 개체 navmeshagent 우선순위 조정
            var navMeshExecutor = GetComponent<NavMeshMoveExecutor>();
            var chargeSettings = GetComponent<ChargeSettings>();
            if (chargeSettings != null) navMeshExecutor.FixAvoidancePriority(chargeSettings.AvoidancePriority); // 돌진형은 슬롯 규칙 대신 고정 우선순위
            
            var flyingExecutor = GetComponent<FlyingMoveExecutor>(); // 공중 프리팹에만 있다. 없으면 null
            var attacks = new List<MonsterAttack>();
            foreach (MonsterAttackDef attackDef in BasicAttackDefs(Def))
            {
                var attackSpec = AttackSpec.From(attackDef);
                attacks.Add(MonsterAttackFactory.CreateAttack(Def.Role, attackSpec, body, FindPlayerDamageable,
                                                              attackSettings, movement, flyingExecutor));
            }
            movement.SetCombatTuning(Def.MoveSpeed, attacks[0].Range); // 이동의 전투 사거리는 첫 공격 기준
            pounceSettings = GetComponent<PounceSettings>();
            if (pounceSettings != null) movement.SetFlankApproachSpeedMultiplier(pounceSettings.FarApproachSpeedMultiplier);
            StateMachine = new MonsterStateMachine(movement, body, attacks);

            StateMachine.Died += OnStateMachineDied;
            StateMachine.StateChanged += OnStateChanged;
            foreach (MonsterAttack attack in StateMachine.Attacks)
            {
                attack.PhaseChanged += OnAttackPhaseChanged;
                attack.BodyLanded += OnAttackBodyLanded;
                attack.BodyContacted += OnAttackBodyContacted;
            }
        }

        /// <summary>
        /// 비활성 후 다시 켜질 때 이동과 게임 이벤트 구독을 되살린다.
        /// </summary>
        /// <remarks>최초 활성화(StateMachine == null)는 건너뛴다. 최초 구독은 Start 가 조립 후 한다.</remarks>
        private void OnEnable()
        {
            if (StateMachine == null) return; // 최초 구독은 Start의 조립이 끝난 뒤 수행한다.
            movement.enabled = true;
            Subscribe();
        }

        /// <summary>
        /// 몬스터가 반응하는 게임 이벤트 4개(보스 정리, 라운드 정리, 플레이어 사망, 플레이어 부활)를 구독한다.
        /// 해제는 OnDisable 이 한다.
        /// </summary>
        private void Subscribe()
        {
            EventBus<MonsterWipeRequestedEvent>.Subscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Subscribe(OnPlayerRevived);
        }

        /// <summary>
        /// 꺼질 때 상태 머신을 정리하고, 이동을 끄고, 게임 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            if (StateMachine != null) StateMachine.Cleanup();
            if (movement != null) movement.enabled = false;
            EventBus<MonsterWipeRequestedEvent>.Unsubscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Unsubscribe(OnPlayerRevived);
        }

        /// <summary>
        /// 매 프레임 진입점.
        /// 죽었으면 시체 단계(누움 → 침강)를 넘기고, 침강이 끝나면 회수한다.
        /// 살아 있으면 대상 확보 → 공격 튜닝 → 피격 슬로우 종료 → 상태 갱신 → 이동 애니 순서로 돈다.
        /// </summary>
        private void Update()
        {
            if (StateMachine.IsDead)
            {
                corpse.Tick(Time.time);
                if (corpse.IsGone) Despawn();
                return;
            }

            AcquireTarget();
            ApplyAttackTuning();
            EndHitSlowIfExpired();
            StateMachine.Update(Time.time, Time.deltaTime);
            UpdateMoveAnimation();
        }

        /// <summary>
        /// 인스펙터의 쿨다운·Active 길이·판정 비율·판정 끝 비율·최대 명중 횟수·명중 간격·판정 거리 비율·예고 회전 속도를 이 몬스터의 모든 공격에 넣는다.
        /// 덮어쓰기가 꺼져 있으면 쿨다운은 각 공격의 데이터 테이블 값을 쓴다. 조준 반경은 테이블 AimOffsetRadius 값이라 여기서 넣지 않는다.
        /// </summary>
        /// <remarks>쿨다운·Active 길이·판정 비율·판정 끝 비율은 Begin 때 고정하므로 실제 반영은 다음 공격부터다. 최대 명중 횟수·명중 간격·판정 거리 비율은 다음 판정부터, 예고 회전 속도는 다음 프레임부터 반영된다.</remarks>
        private void ApplyAttackTuning()
        {
            foreach (MonsterAttack attack in StateMachine.Attacks)
            {
                float cooldown = attack.Spec.Cooldown;
                if (useCooldownOverride) cooldown = cooldownOverrideSeconds;

                attack.Cooldown = cooldown;
                attack.ActiveSeconds = activeSeconds;
                attack.HitTimeRatio = hitTimeRatio;
                attack.HitEndRatio = hitEndRatio;
                attack.MaxHitsPerAttack = maxHitsPerAttack;
                attack.HitIntervalSeconds = hitIntervalSeconds;
                attack.HitRangeRatio = hitRangeRatio;
                attack.WindupTurnSpeed = windupTurnSpeed;
            }
        }

        /// <summary>
        /// 상태 머신의 상태가 바뀔 때 Animator 의 Bool/Trigger 를 새 상태에 맞춘다.
        /// Stunned → IsStunned, Dead → IsDead + Death Trigger, Idle/Chase → IsStunned 해제.
        /// </summary>
        /// <remarks>
        /// Attack 신호는 보내지 않는다 (OnAttackPhaseChanged 담당).
        /// Idle/Chase 안의 정지·이동은 UpdateMoveAnimation 이 IsMoving 으로 정한다.
        /// IsDead 가 Any State 전이를 막아 Death 에서 나가지 않는다.
        /// </remarks>
        /// <param name="current">새로 들어간 상태</param>
        private void OnStateChanged(MonsterState previous, MonsterState current)
        {
            animator.speed = 1f;
            animator.SetBool("IsMoving", false);

            if (current == StateMachine.Stunned)
            {
                animator.SetBool("IsStunned", true);
                Stunned?.Invoke(StateMachine.StunDuration);
                return;
            }
            if (current == StateMachine.Dead)
            {
                animator.SetBool("IsStunned", false);
                animator.SetBool("IsDead", true);
                animator.SetTrigger("Death");
                return;
            }
            if (current == StateMachine.Attack) return;

            // 여기부터 Idle / Chase 진입: 기절에서 나왔으면 Stunned → Idle 전이가 일어난다
            animator.SetBool("IsStunned", false);
        }

        /// <summary>
        /// 소리 쪽에 단계 변경을 알리고, 공격 Active 시작에 Attack 클립을 재생한다.
        /// 재생 배율 = 클립 길이 ÷ Active 길이 → 클립이 Active 동안 정확히 1번 재생된다.
        /// </summary>
        /// <remarks>
        /// 예고·회복·종료 단계는 무시한다. Animator 는 Exit Time 으로 이미 Idle 에 돌아가 있다.
        /// 이동 자체가 공격(UsesMoveAnimation, 돌진)이면 Attack 클립을 쓰지 않는다. 이동 클립은 UpdateMoveAnimation 이 맡는다.
        /// </remarks>
        /// <param name="attack">단계가 바뀐 공격. ActiveDuration 을 읽는다</param>
        /// <param name="phase">새 단계. Active 일 때만 반응한다</param>
        private void OnAttackPhaseChanged(MonsterAttack attack, SkillPhase phase)
        {
            AttackPhaseChanged?.Invoke(FindAttackDef(attack), phase); // 소리: 모든 단계를 알린다 (예고음 = Windup)
            if (phase != SkillPhase.Active) return;
            if (attack.UsesMoveAnimation) return;

            animator.SetFloat("AttackSpeed", ReadAttackClipSeconds() / attack.ActiveDuration);
            animator.SetTrigger("Attack");
        }

        /// <summary>도약 착지를 소리 쪽에 알린다</summary>
        private void OnAttackBodyLanded(MonsterAttack attack, Vector3 ground) => AttackLanded?.Invoke(FindAttackDef(attack), ground);

        /// <summary>돌진 명중(몸 충돌)을 소리 쪽에 알린다</summary>
        private void OnAttackBodyContacted(MonsterAttack attack, Vector3 point) => AttackContacted?.Invoke(FindAttackDef(attack), point);

        /// <summary>공격의 테이블 Def (Spec.SourceDefId). 테이블에 없으면 null</summary>
        private static MonsterAttackDef FindAttackDef(MonsterAttack attack) =>
            DataManager.MonsterAttacks.TryGet(attack.Spec.SourceDefId, out var def) ? def : null;

        /// <summary>
        /// Chase 중 실제 이동 속도로 IsMoving 을 매 프레임 넣어 Idle ↔ Move 전이를 일으킨다.
        /// 이동 자체가 공격인 몬스터(돌진)는 Attack 중과 Idle 준비 동작 중에도 공격이 알려준 몸 이동 속도로 같은 일을 한다.
        /// Move 면 재생 배율을 (속도 ÷ 클립 기준 속도) 로 맞춰 발 미끄러짐을 줄인다.
        /// 클립 기준 속도는 Chase 면 moveClipSpeed, 공격 이동 중이면 공격의 MoveClipSpeedFor (돌진 = ChargeSettings.ChargeClipSpeed).
        /// 측면 침투형이 Chase 중 원거리 시야 밖 접근이면 재생 배율에 PounceSettings.FarApproachClipSpeedMultiplier 를 더 곱한다.
        /// </summary>
        private void UpdateMoveAnimation()
        {
            MonsterAttack currentAttack = StateMachine.CurrentAttack;
            bool isChasing = StateMachine.Current == StateMachine.Chase;
            bool isAttacking = StateMachine.Current == StateMachine.Attack;
            bool isPreparingInIdle = StateMachine.Current == StateMachine.Idle && StateMachine.IsResting;
            bool isAttackMovingBody = currentAttack.UsesMoveAnimation && (isAttacking || isPreparingInIdle);
            if (isChasing == false && isAttackMovingBody == false) return;

            float speed = movement.Velocity.magnitude;
            float clipSpeed = moveClipSpeed;
            if (isAttackMovingBody)
            {
                speed = currentAttack.BodyMoveSpeed;
                clipSpeed = currentAttack.MoveClipSpeedFor(moveClipSpeed);
            }
            bool shouldMove = speed >= idleSpeedThreshold;
            bool isApproachingOutOfView = isChasing && pounceSettings != null && movement.IsApproachingOutOfView;
            animator.SetBool("IsMoving", shouldMove);

            float playbackRate = speed / clipSpeed;
            if (isApproachingOutOfView) playbackRate *= pounceSettings.FarApproachClipSpeedMultiplier;

            if (shouldMove) animator.speed = playbackRate;
            else animator.speed = 1f;
        }

        /// <summary>
        /// 몬스터별 Override Controller 에서 Attack 슬롯 클립 길이를 읽는다.
        /// </summary>
        /// <returns>Attack 슬롯 클립 길이 (s)</returns>
        /// <exception cref="InvalidOperationException">Override Controller 에 Attack 슬롯 클립이 없을 때</exception>
        private float ReadAttackClipSeconds()
        {
            var overrideController = (AnimatorOverrideController)animator.runtimeAnimatorController;
            AnimationClip attackClip = overrideController[BaseAttackClipName];
            if (attackClip == null)
                throw new InvalidOperationException($"{name}: Override Controller 에 {BaseAttackClipName} 슬롯 클립이 없다");
            return attackClip.length;
        }

        /// <summary>
        /// 대상이 없으면 ServiceLocator 의 플레이어를 상태 머신과 이동에 같은 값으로 넣는다.
        /// 플레이어가 죽었거나 아직 등록 전이면 아무것도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// 사망 이벤트 뒤에 생성·재사용된 몬스터는 playerDead 가 false 라서, 플레이어의 IsAlive 를 직접 읽어 시체 재추적을 막는다.
        /// </remarks>
        private void AcquireTarget()
        {
            if (dormant || playerDead) return;
            if (StateMachine.HasTarget) return;
            if (ServiceLocator.TryGet(out IPlayerTarget player) == false) return; // 플레이어 등록 전
            var playerDamageable = player as IDamageable;
            bool isPlayerDamageable = playerDamageable != null;
            bool isPlayerDead = isPlayerDamageable && playerDamageable.IsAlive == false;
            if (isPlayerDead) return;
            StateMachine.SetTarget(player);
            SetMovementTarget(player);
        }

        /// <summary>
        /// 이동(군중)에 목표를 넣는다. 지금 목표와 같으면 다시 넣지 않는다.
        /// </summary>
        /// <remarks>CrowdController.SetTarget 은 호출마다 군중 전체의 슬롯 예약을 비우므로 중복 호출을 막는다.</remarks>
        /// <param name="target">새 목표. null = 대상 상실 (플레이어 사망 등)</param>
        private void SetMovementTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(movement.Target, target)) return;
            movement.SetTarget(target);
        }

        /// <summary>
        /// 최대 체력과 현재 체력을 같은 값으로 채운다. Awake 에서 1번 호출한다.
        /// </summary>
        /// <param name="maxHp">테이블의 MaxHp. 1 이상</param>
        /// <exception cref="ArgumentOutOfRangeException">maxHp 가 0 이하일 때</exception>
        private void InitializeHealth(int maxHp)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "최대 체력은 1 이상");
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        /// <summary>무기가 IDamageable 로 호출한다 (몸통 명중). ApplyDamage 로 넘긴다</summary>
        /// <param name="info">피해 정보. Amount 만큼 체력을 깎는다</param>
        public void TakeDamage(in DamageInfo info) => ApplyDamage(info, false);

        /// <summary>MonsterWeakPoint 가 부위 배율을 곱한 피해로 호출한다. 약점용 피를 튀긴다</summary>
        /// <param name="info">배율을 곱한 피해 정보</param>
        public void TakeWeakPointDamage(in DamageInfo info) => ApplyDamage(info, true);

        /// <summary>
        /// 체력을 깎고 맞을 때마다 DamageDealtEvent 를 발행하고, 맞은 곳에 피를 튀긴다.
        /// 체력이 0 이 된 순간 1번 상태 머신을 Dead 로 보낸다 (보상 지급, 자폭형은 그 전에 폭발). 아니면 피격 모션을 재생하고,
        /// 피해량(약점 배율 포함)을 넘겨 인스펙터의 피격 반응(경직·슬로우)을 건 뒤, 저체력 기준을 처음 넘은 순간 1번 느려지고 붉어진다. 이미 죽었으면 무시한다.
        /// </summary>
        /// <param name="info">피해 정보. Amount 만큼 체력을 깎는다</param>
        /// <param name="isWeakPoint">true = 약점 명중</param>
        /// <exception cref="InvalidOperationException">InitializeHealth 전에 호출됐을 때</exception>
        private void ApplyDamage(in DamageInfo info, bool isWeakPoint)
        {
            if (MaxHp == 0) throw new InvalidOperationException($"{nameof(InitializeHealth)} 전에 피해를 받았다");
            if (IsAlive == false) return;

            bool wasLowHealth = IsLowHealth;
            CurrentHp = Mathf.Max(0, CurrentHp - info.Amount);
            lastHitDirection = info.Direction;
            bool isKill = IsAlive == false;
            bool becameLowHealth = wasLowHealth == false && IsLowHealth;
            Debug.Log($"{gameObject.name}: {info.Amount} 체력을 깎는다. 현재 체력: {CurrentHp}");
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, isKill));
            damageFeedback.PlayHitBlood(info.HitPoint, info.Direction, isWeakPoint);

            if (isKill)
            {
                ExplodeIfSelfDestruct();
                StateMachine.Die(true);
                return;
            }
            damageFeedback.PlayHitAnimation(info.Direction);
            ApplyHitReaction(info.Amount);
            if (becameLowHealth) EnterLowHealth();
        }

        /// <summary>
        /// 인스펙터에서 고른 피격 반응을 건다. ApplyDamage 가 살아남은 피격마다 부른다.
        /// Stun = 피해를 누적해 임계값 이상이면 경직, Slow = 슬로우 시작·갱신, None = 아무것도 하지 않는다.
        /// </summary>
        /// <param name="damage">이번 피격 피해 (HP). 약점 배율이 곱해진 값. Stun 누적에만 쓴다</param>
        private void ApplyHitReaction(int damage)
        {
            switch (hitReaction)
            {
                case HitReaction.Stun:
                    TryHitStun(damage);
                    break;
                case HitReaction.Slow:
                    ApplyHitSlow();
                    break;
                case HitReaction.None:
                    break;
            }
        }

        /// <summary>
        /// 경직 쿨다운이 지났으면 피해를 누적하고, 누적 시간 안의 합계가 hitStunDamageThreshold 이상이면
        /// 상태 머신을 hitStunSeconds 동안 경직시킨다 (공격 중이면 공격이 끊긴다). ApplyHitReaction 이 Stun 일 때 부른다.
        /// 경직 시간 + 쿨다운이 지나기 전의 재피격은 무시하므로, 경직 중 재피격도 시간을 늘리지 않고 누적하지도 않는다.
        /// </summary>
        /// <param name="damage">이번 피격 피해 (HP). 약점 배율이 곱해진 값</param>
        private void TryHitStun(int damage)
        {
            if (Time.time < nextHitStunTime) return;

            AddStunDamage(damage);
            bool hasReachedStunDamage = stunDamageTotal >= hitStunDamageThreshold;
            if (hasReachedStunDamage == false) return;

            StateMachine.Stun(hitStunSeconds);
            nextHitStunTime = Time.time + hitStunSeconds + hitStunCooldownSeconds;
            ResetStunDamage();
        }

        /// <summary>
        /// 경직 판정용 피해를 누적 합계에 더한다. TryHitStun 이 쿨다운 밖의 피격마다 부른다.
        /// 누적 시간이 지났으면 합계를 비우고 지금부터 hitStunDamageWindowSeconds 동안 새로 쌓는다 (첫 피격 기준 고정 구간).
        /// </summary>
        /// <param name="damage">더할 피해 (HP). 약점 배율이 곱해진 값</param>
        private void AddStunDamage(int damage)
        {
            bool hasWindowEnded = Time.time >= stunDamageWindowEndTime;
            if (hasWindowEnded)
            {
                stunDamageTotal = 0;
                stunDamageWindowEndTime = Time.time + hitStunDamageWindowSeconds;
            }

            stunDamageTotal += damage;
        }

        /// <summary>
        /// 경직 판정용 누적 피해와 누적 종료 시각을 비운다. 다음 피격이 새 누적을 시작한다.
        /// TryHitStun 이 경직시킨 직후와 Respawn 이 피격 반응을 초기화할 때 부른다.
        /// </summary>
        private void ResetStunDamage()
        {
            stunDamageTotal = 0;
            stunDamageWindowEndTime = 0f;
        }

        /// <summary>
        /// 슬로우를 시작하거나 종료 시각을 지금부터 hitSlowSeconds 뒤로 갱신하고 이동 속도를 다시 계산한다.
        /// 종료는 Update 의 EndHitSlowIfExpired 가 한다.
        /// </summary>
        private void ApplyHitSlow()
        {
            hitSlowEndTime = Time.time + hitSlowSeconds;
            isHitSlowed = true;
            ApplyMoveSpeed();
        }

        /// <summary>
        /// 슬로우 종료 시각이 지났으면 슬로우를 끄고 이동 속도를 되돌린다 (저체력이면 저체력 속도로).
        /// Update 가 상태 갱신 전에 매 프레임 부른다.
        /// </summary>
        private void EndHitSlowIfExpired()
        {
            if (isHitSlowed == false) return;
            if (Time.time < hitSlowEndTime) return;

            isHitSlowed = false;
            ApplyMoveSpeed();
        }

        /// <summary>
        /// 이동 속도 = 테이블 MoveSpeed × 저체력 배율 × 피격 슬로우 배율을 계산해 이동에 넣는다. 배율 곱 규칙은 여기 한 곳에 둔다.
        /// 저체력 진입(EnterLowHealth)·슬로우 시작(ApplyHitSlow)·슬로우 종료(EndHitSlowIfExpired)가 부른다.
        /// </summary>
        private void ApplyMoveSpeed()
        {
            float lowHealthMultiplier = 1f;
            if (IsLowHealth) lowHealthMultiplier = lowHealthSpeedRatio;

            float hitSlowMultiplier = 1f;
            if (isHitSlowed) hitSlowMultiplier = hitSlowRatio;

            float moveSpeed = Def.MoveSpeed * lowHealthMultiplier * hitSlowMultiplier;
            float attackRange = StateMachine.Attacks[0].Range; // AssembleStateMachine 과 같은 첫 공격 기준
            movement.SetCombatTuning(moveSpeed, attackRange);
        }

        /// <summary>
        /// 자폭형이면 처치된 그 자리에서 폭발시킨다 (SelfDestructSettings 의 처치 시 폭발이 켜져 있을 때만).
        /// ApplyDamage 가 체력이 0 이 된 순간, Dead 로 보내기 전에 부른다. 자폭형이 아니면 할 일이 없다.
        /// </summary>
        private void ExplodeIfSelfDestruct()
        {
            foreach (MonsterAttack attack in StateMachine.Attacks)
            {
                if (attack is SelfDestructAttack selfDestruct) selfDestruct.ExplodeOnKilled(StateMachine.Target);
            }
        }

        /// <summary>
        /// 저체력에 들어간다: 이동 속도에 lowHealthSpeedRatio 를 곱하고 (피격 슬로우 중이면 그 배율도 곱한다) 몸을 붉게 한다.
        /// ApplyDamage 가 체력을 깎은 뒤 저체력 기준을 처음 넘은 순간 1번 부른다 (IsLowHealth 가 이미 true). Respawn 의 재조립이 속도를 되돌린다.
        /// </summary>
        private void EnterLowHealth()
        {
            ApplyMoveSpeed();
            damageFeedback.ShowLowHealth();
        }

        /// <summary>
        /// 상태 머신이 Dead 에 들어갈 때 호출된다.
        /// 대상을 놓고, 피를 터뜨리고, 몸을 물리 시체로 바꾸고, MonsterDiedEvent 를 발행한다.
        /// 회수는 시체가 가라앉은 뒤 Update 가 한다.
        /// </summary>
        /// <param name="grantReward">true = 처치 보상 지급. MonsterDiedEvent 로 그대로 전달한다</param>
        private void OnStateMachineDied(bool grantReward)
        {
            CurrentHp = 0; // 자폭처럼 체력 피해 없이 Dead 로 들어오는 경우에도 IsAlive 를 일치시킨다.
            StateMachine.SetTarget(null);
            damageFeedback.PlayDeathBlood(); // 콜라이더가 바뀌기 전 몸통 중심에서
            BecomeCorpse();
            Died?.Invoke();
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(Def, transform.position, gameObject, grantReward));
        }

        /// <summary>
        /// 이동을 끄고 몸을 물리 시체로 넘긴다. 공중 몬스터는 죽는 순간의 비행 속도를 이어받아 떨어지고 구른다.
        /// OnStateMachineDied 가 부른다. 이동은 Respawn 의 enabled = true(OnEnable)가 다시 켠다.
        /// </summary>
        private void BecomeCorpse()
        {
            Vector3 deathVelocity = Vector3.zero;
            var flyingExecutor = GetComponent<FlyingMoveExecutor>(); // 공중 프리팹에만 있다. 없으면 null
            if (flyingExecutor != null) deathVelocity = flyingExecutor.Velocity;

            movement.enabled = false; // NavMeshAgent·비행 이동이 시체를 다시 옮기지 않게
            corpse.BecomeCorpse(deathVelocity, lastHitDirection);
        }

        /// <summary>보스 등장 공격이 잡몹을 정리할 때 이 몬스터를 죽인다.</summary>
        /// <param name="e">GrantReward 로 보상 지급 여부를 정한다</param>
        private void OnWipeRequested(MonsterWipeRequestedEvent e) => StateMachine.Die(e.GrantReward);

        /// <summary>라운드 정리: 처치 이벤트 없이 바로 회수한다.</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => Despawn();

        /// <summary>
        /// 플레이어가 죽으면 상태 머신과 이동의 대상을 비우고, 이후 대상 확보를 멈춘다.
        /// 최종 사망이 아니다: 부활하면 OnPlayerRevived 가 대상 확보를 다시 연다.
        /// </summary>
        private void OnPlayerDied(PlayerDiedEvent e)
        {
            playerDead = true;
            StateMachine.SetTarget(null);
            SetMovementTarget(null);
        }

        /// <summary>
        /// 플레이어가 부활하면 대상 확보를 다시 연다. 실제 대상 지정은 다음 Update 의 AcquireTarget 이 한다.
        /// </summary>
        private void OnPlayerRevived(PlayerRevivedEvent e) => playerDead = false;

        /// <summary>
        /// 상태 머신·이동·이 컴포넌트를 끄고 팩토리에 회수를 맡긴다. 여러 번 불려도 1번만 실행한다.
        /// </summary>
        private void Despawn()
        {
            if (despawned) return;
            despawned = true;
            StateMachine.Cleanup();
            movement.enabled = false;
            enabled = false;
            ServiceLocator.Get<IMonsterFactory>().Despawn(gameObject);
        }

        /// <summary>
        /// 일반 몬스터 Def 에서 기본 공격 정의 전부를 꺼낸다. Start 가 공격 목록을 만들 때 쓴다.
        /// </summary>
        /// <param name="def">일반 몬스터 Def. BasicAttacks 가 1개 이상이어야 한다</param>
        /// <returns>기본 공격 정의 목록 (테이블 순서 그대로)</returns>
        /// <exception cref="InvalidOperationException">BasicAttacks 가 비어 있을 때</exception>
        private static IReadOnlyList<MonsterAttackDef> BasicAttackDefs(MonsterDef def)
        {
            if (def.BasicAttacks.Count == 0)
                throw new InvalidOperationException($"MonsterTable {def.Id}: 일반 몬스터의 기본 공격은 1개 이상이어야 한다");
            return def.BasicAttacks;
        }

        /// <summary>
        /// 공격 판정이 피해를 줄 플레이어 IDamageable 을 찾는다.
        /// Core 의 TryGetDamageable 과 같은 부모 방향 탐색이다.
        /// </summary>
        /// <param name="target">현재 공격 대상</param>
        /// <returns>target.Root 또는 그 부모의 IDamageable. 없으면 null</returns>
        private static IDamageable FindPlayerDamageable(IPlayerTarget target) =>
            target.Root.GetComponentInParent<IDamageable>();
    }
}
