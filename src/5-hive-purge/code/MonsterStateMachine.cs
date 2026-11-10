using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 1마리의 상태 머신 본체 (상태 패턴의 Owner). Unity 컴포넌트가 아니라 순수 C# 이다.
    /// MonoBehaviour 껍데기(MonsterController)가 Update(now, dt) 를 매 프레임 호출하고, 대상·처치 같은 외부 사건을 전달한다.
    /// 이동은 MonsterMovement 의 정지/재개(SetSuspended)와 공격 허가(CanAttack)로만 제어한다.
    /// </summary>
    public sealed class MonsterStateMachine
    {
        private readonly MonsterMovement movement;
        private readonly MonsterBody body;

        public MonsterState Idle { get; }
        public MonsterState Chase { get; }
        public MonsterState Attack { get; }
        public MonsterState Stunned { get; }
        public MonsterState Dead { get; }

        public MonsterState Current { get; private set; }
        // 이 몬스터가 가진 공격 전부. 생성 후 불변, 1개 이상
        public IReadOnlyList<MonsterAttack> Attacks { get; }
        // 지금 고른 공격. 생성 시 Attacks[0], 이후 ChooseNextAttack 이 바꾼다
        public MonsterAttack CurrentAttack { get; private set; }
        public IPlayerTarget Target { get; private set; }

        // 마지막 Update 시각 (s)
        public float Now { get; private set; }
        // 마지막 Update 의 경과 시간 (s)
        public float DeltaTime { get; private set; }
        // 마지막 Stun 요청 시간 (s)
        public float StunDuration { get; private set; }
        // Die 때 받은 보상 지급 여부
        public bool GrantReward { get; private set; }
        public bool IsDead => Current == Dead;

        // ── 공격 뒤 행동 (ClearAfterAttackFlags 가 함께 해제) ──
        // RestInIdle 공격이 끝나 Idle 에서 준비 동작 중이면 true
        public bool IsResting { get; private set; }
        // RepositionInChase 공격이 끝나 Chase 에서 재정비 중이면 true. 이 동안 Attack 으로 가지 않는다
        public bool IsRepositioning { get; private set; }

        // 인자 = (previous, current)
        public event Action<MonsterState, MonsterState> StateChanged;
        // DeadState 진입 시 1번. 인자 = 보상 지급 여부
        public event Action<bool> Died;

        /// <summary>
        /// 공격 1개짜리 몬스터용. 목록 생성자에 공격 1개짜리 목록으로 넘긴다.
        /// 기존 테스트가 이 형태로 만든다.
        /// </summary>
        /// <param name="movement">이 몬스터의 이동 컴포넌트</param>
        /// <param name="body">이 몬스터의 몸체 (위치·방향)</param>
        /// <param name="attack">이 몬스터가 쓸 공격 전략</param>
        /// <exception cref="ArgumentNullException">movement, body, attack 중 하나가 null 일 때</exception>
        public MonsterStateMachine(MonsterMovement movement, MonsterBody body, MonsterAttack attack)
            : this(movement, body, new[] { attack })
        {
        }

        /// <summary>
        /// 공격 목록을 받고 상태 5개를 만들어 Idle 로 시작한다. MonsterController.Start 가 조립 때 1번 호출한다.
        /// 처음 고른 공격은 attacks[0] 이다.
        /// </summary>
        /// <remarks>생성 중 Idle.Enter 가 이동을 멈추므로 movement 는 Awake 가 끝난 상태여야 한다.</remarks>
        /// <param name="movement">이 몬스터의 이동 컴포넌트</param>
        /// <param name="body">이 몬스터의 몸체 (위치·방향)</param>
        /// <param name="attacks">이 몬스터가 쓸 공격 전략 목록. 1개 이상</param>
        /// <exception cref="ArgumentNullException">movement, body, attacks 또는 그 원소 중 하나가 null 일 때</exception>
        /// <exception cref="ArgumentException">attacks 가 비어 있을 때</exception>
        public MonsterStateMachine(MonsterMovement movement, MonsterBody body, IReadOnlyList<MonsterAttack> attacks)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (attacks == null) throw new ArgumentNullException(nameof(attacks));
            if (attacks.Count == 0) throw new ArgumentException("공격은 1개 이상이어야 한다", nameof(attacks));
            foreach (MonsterAttack attack in attacks)
            {
                if (attack == null) throw new ArgumentNullException(nameof(attacks), "공격 목록에 null 이 있다");
            }

            this.movement = movement;
            this.body = body;
            Attacks = attacks;
            CurrentAttack = attacks[0];

            Idle = new IdleState(this);
            Chase = new ChaseState(this);
            Attack = new AttackState(this);
            Stunned = new StunnedState(this);
            Dead = new DeadState(this);

            Current = Idle;
            Current.Enter();
        }

        // 대상이 있으면 true. 대상 상실(사망 등)은 소유자(MonsterController)가 SetTarget(null)로 알린다
        public bool HasTarget => Target != null;
        // 군중 이동의 공격 허가 (슬롯 예약·토큰). 직접 추적 모드는 대상이 있으면 true
        public bool CanAttack => movement.CanAttack;

        // 대상까지 수평 거리 ≤ 현재 공격 사거리. 대상이 없으면 false
        public bool IsTargetInAttackRange
        {
            get
            {
                if (HasTarget == false) return false;
                Vector3 toTarget = Target.PlayerTrackingInfo.Position - body.Position;
                toTarget.y = 0f;
                float range = CurrentAttack.Range;
                return toTarget.magnitude <= range;
            }
        }

        // Attack 유지 판정용 (수평 거리 ≤ 사거리 × HitRangeRatio, 대상 없으면 false). 진입은 IsTargetInAttackRange
        public bool IsTargetInAttackHoldRange
        {
            get
            {
                if (HasTarget == false) return false;
                Vector3 toTarget = Target.PlayerTrackingInfo.Position - body.Position;
                toTarget.y = 0f;
                float holdRange = CurrentAttack.Range * CurrentAttack.HitRangeRatio;
                return toTarget.magnitude <= holdRange;
            }
        }

        /// <summary>
        /// 공격 대상을 바꾼다. MonsterController 가 대상 확보·상실 때 호출한다.
        /// 대상이 바뀌면 진행 중 공격을 취소하고, 대상이 없어지면 휴식·재정비를 해제하고 Attack/Chase 에서 Idle 로 돌아간다.
        /// </summary>
        /// <param name="target">새 대상. null = 대상 상실 (플레이어 사망 등)</param>
        public void SetTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(Target, target) == false && CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            Target = target;
            if (HasTarget == false)
            {
                ClearAfterAttackFlags();
                if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
                if (Current == Attack || Current == Chase) ChangeState(Idle);
            }
        }

        /// <summary>
        /// 판단 1회. MonsterController.Update 가 매 프레임 호출한다.
        /// 시각을 기록하고 현재 상태의 Tick 을 부른다. 상태들은 기록된 Now / DeltaTime 을 읽는다.
        /// </summary>
        /// <param name="now">현재 시각 (s, Time.time)</param>
        /// <param name="deltaTime">지난 프레임부터 경과 시간 (s)</param>
        public void Update(float now, float deltaTime)
        {
            Now = now;
            DeltaTime = deltaTime;
            bool hasNoTarget = HasTarget == false;
            bool isInAttackState = Current == Attack;
            bool isInChaseState = Current == Chase;
            if (hasNoTarget && (isInAttackState || isInChaseState)) SetTarget(null);
            Current.Tick();
        }

        /// <summary>
        /// 상태를 전이한다: 이전 상태 Exit → 새 상태 Enter → StateChanged 발행.
        /// 각 상태의 Tick 과 이 클래스의 SetTarget / Die / Stun / Cleanup 이 호출한다.
        /// 같은 상태로의 변경과 Dead 이후의 변경은 무시한다.
        /// </summary>
        /// <param name="next">들어갈 상태 (이 머신의 Idle / Chase / Attack / Stunned / Dead 중 하나)</param>
        /// <exception cref="ArgumentNullException">next 가 null 일 때</exception>
        public void ChangeState(MonsterState next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (next == Current || Current == Dead) return;

            var previous = Current;
            previous.Exit();
            Current = next;
            next.Enter();
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>
        /// 처치·정리. 어느 상태에서든 Dead 로 간다 (진행 중 공격은 AttackState.Exit 가 취소).
        /// MonsterController 가 체력 0 이나 보스 정리 이벤트 때 호출한다. 이미 죽었으면 무시한다.
        /// </summary>
        /// <param name="grantReward">true = 처치 보상 지급. Died 이벤트로 그대로 전달된다</param>
        public void Die(bool grantReward)
        {
            if (IsDead) return;
            GrantReward = grantReward;
            ChangeState(Dead);
        }

        /// <summary>
        /// 기절. 어느 상태에서든 Stunned 로 간다. AttackState 가 벽 충돌(WallHit) 결과를 받았을 때 호출한다.
        /// 휴식·재정비는 해제한다 (기절이 끝나면 평소처럼 Chase). 죽었거나 이미 기절 중이면 무시한다.
        /// </summary>
        /// <param name="seconds">기절 시간 (s). 0 이상</param>
        /// <exception cref="ArgumentOutOfRangeException">seconds 가 0 미만일 때</exception>
        public void Stun(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "기절 시간은 0 이상");
            if (IsDead || Current == Stunned) return; // 반복 기절은 최초 종료시각을 유지한다.
            ClearAfterAttackFlags();
            StunDuration = seconds;
            ChangeState(Stunned);
        }

        /// <summary>
        /// 회수/비활성화: 보상·사망 알림 없이 공격·휴식·재정비와 이동을 정리한다.
        /// MonsterController 의 OnDisable 과 Despawn 이 호출한다.
        /// </summary>
        public void Cleanup()
        {
            SetTarget(null);
            ClearAfterAttackFlags();
            if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            if (IsDead == false) ChangeState(Idle);
            movement.SetSuspended(true);
        }

        // ── 상태 전용 ──
        /// <summary>
        /// 다음에 쓸 공격을 골라 CurrentAttack 에 넣는다. AttackState 가 공격 시작 직전에 호출한다.
        /// 지금은 항상 Attacks[0] 을 고른다.
        /// </summary>
        /// <returns>고른 공격 (CurrentAttack 과 같음)</returns>
        internal MonsterAttack ChooseNextAttack()
        {
            CurrentAttack = Attacks[0];
            return CurrentAttack;
        }

        /// <summary>
        /// 휴식하러 Idle 로 간다. AttackState.FinishAttackCycle 이 After = RestInIdle 공격의 정상 완료 때 부른다.
        /// IdleState 가 준비 동작을 돌리고, 준비가 끝나면 ClearAfterAttackFlags 로 해제한다.
        /// </summary>
        internal void RestInIdle()
        {
            IsResting = true;
            ChangeState(Idle);
        }

        /// <summary>
        /// 재정비하러 Chase 로 간다. AttackState.FinishAttackCycle 이 After = RepositionInChase 공격의 정상 완료 때 부른다.
        /// ChaseState 가 공격의 IsRepositioned 가 true 가 될 때까지 Attack 으로 가지 않는다.
        /// </summary>
        internal void RepositionInChase()
        {
            IsRepositioning = true;
            ChaseAgain();
        }

        /// <summary>
        /// 쿨다운 동안 Chase 로 가서 슬롯까지 이동한다. AttackState.FinishAttackCycle 이 After = ChaseUntilReady 공격의 정상 완료 때 부른다.
        /// 공격 슬롯 예약은 반환하지 않는다 (Attack 진입 때 StopForAttack 으로 유지한 예약을 Chase.Enter 의 ResumeMove 가 그대로 이어 쓴다).
        /// ChaseState 가 쿨다운이 끝날 때까지 Attack 으로 가지 않는다.
        /// </summary>
        internal void ChaseUntilReady() => ChangeState(Chase);

        /// <summary>
        /// 군중 이동의 예약을 반환하고 Chase 로 간다 (Chase.Enter 가 다시 등록해 슬롯을 새로 경쟁한다).
        /// AttackState 가 사거리 밖·공격 불허일 때와 RepositionInChase 가 부른다.
        /// </summary>
        internal void ChaseAgain()
        {
            StopMove();
            ChangeState(Chase);
        }

        /// <summary>
        /// 휴식·재정비를 함께 해제한다. Idle 준비 완료, Chase 재정비 완료, Stun, 대상 상실, Cleanup 이 부른다.
        /// </summary>
        internal void ClearAfterAttackFlags()
        {
            IsResting = false;
            IsRepositioning = false;
        }

        /// <summary>멈춘다. 군중 이동의 등록·슬롯·토큰 예약은 즉시 반환한다</summary>
        internal void StopMove() => movement.SetSuspended(true);
        /// <summary>공격하려고 멈춘다. 군중 이동의 공격 슬롯 예약은 유지한다</summary>
        internal void StopForAttack() => movement.SetSuspended(true, retainReservation: true);
        /// <summary>Chase 진입 때 이동을 재개한다</summary>
        internal void ResumeMove() => movement.SetSuspended(false);
        /// <summary>DeadState 진입 때 Died 이벤트를 보상 지급 여부와 함께 발행한다</summary>
        internal void NotifyDied() => Died?.Invoke(GrantReward);
    }
}
