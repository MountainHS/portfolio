using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// AttackState 가 쓰는 공격 전략의 기반 (Template Method). 상태 머신은 이 타입으로 공격을 다룬다.
    /// 진행 순서 "예고(TelegraphTime) → 공격(Active) → 회복(RecoveryTime)" 은 이 클래스가 고정하고,
    /// 하위 클래스는 각 단계의 내용(OnWindup / OnActive / OnRecover / OnCancel)만 정한다.
    /// 단계는 Tick 1번에 최대 1개씩 넘어간다.
    /// 예고 중에는 모든 공격이 WindupLookPoint 쪽으로 WindupTurnSpeed 만큼씩 돈다 (회전 코드는 이 클래스 한 곳).
    /// 공격 한 사이클 = 예고 → Active(ActiveSeconds) → 회복 → 쿨다운(Cooldown). 판정(OnActive)은 Active 의 HitTimeRatio 지점에서 시작한다.
    /// 쿨다운은 회복까지 정상 완료한 시점부터 센다. 취소·조기 종료는 Begin 시점부터 센다.
    /// 조준은 AimPoint(플레이어 위치 + Begin 때 뽑은 랜덤 offset)로 하고, 피해 판정은 하위 클래스가 실제 플레이어 위치로 한다.
    /// </summary>
    public abstract class MonsterAttack
    {
        private readonly Func<IPlayerTarget, IDamageable> findDamageable;
        private float phaseEndTime;
        private float nextReadyTime;

        // ── 공격 주기 (Begin 때 고정, 진행 중 값 변경은 다음 Begin 부터) ──
        private float cycleCooldown;      // s
        private float cycleActiveSeconds; // s
        private float cycleHitTimeRatio;  // 0~1
        private float cycleHitEndRatio;   // 0~1
        private Vector3 aimOffset;        // m. 이번 주기 조준점이 플레이어에서 벗어난 양 (수평, 길이 = AimOffsetRadius)
        private float activeStartTime;    // s
        private bool isActionDone;        // OnActive 가 true 를 돌려줬으면 같은 Active 에서 다시 부르지 않는다

        public AttackSpec Spec { get; }

        // 회복이 끝난 뒤 다음 공격 시작까지 기다리는 시간 (s). 기본값 = Spec.Cooldown. MonsterController 가 인스펙터 값을 넣는다
        public float Cooldown { get; set; }

        // Active 최소 유지 시간 (s). 0 = 동작이 끝나면 바로 회복. 도약은 이 시간 동안 난다. MonsterController 가 인스펙터 값을 넣는다
        public float ActiveSeconds { get; set; }

        // Active 시작부터 판정까지의 비율 (0~1). 0 = Active 첫 틱에 판정
        public float HitTimeRatio { get; set; } = 0.5f;

        // 판정 구간 끝 비율 (0~1). 판정 구간 = Active 의 [HitTimeRatio, HitEndRatio]. HitTimeRatio 이하 = 판정 시작 1틱만. StrikeAttack 만 쓴다. MonsterController 가 인스펙터 값을 넣는다
        public float HitEndRatio { get; set; } = 0.5f;

        // 판정 구간 동안 줄 수 있는 최대 명중 횟수. 1 = 1번만. 명중 1번 = Damage × Spec.HitCount. StrikeAttack 만 쓴다. MonsterController 가 인스펙터 값을 넣는다
        public int MaxHitsPerAttack { get; set; } = 1;

        // 판정 구간 안 반복 명중 사이 최소 간격 (s). StrikeAttack 만 쓴다. MonsterController 가 인스펙터 값을 넣는다
        public float HitIntervalSeconds { get; set; }

        // 조준점 랜덤 반경 (m). 0 = 정확히 플레이어 위치를 노린다. 다음 Begin 부터 반영. 생성 때 Spec.AimOffsetRadius(테이블 값)로 시작한다
        public float AimOffsetRadius { get; set; }

        // 근접 판정 거리 ÷ 진입 거리(Spec.Range). 1 = 같음. StrikeAttack 의 Melee 판정만 쓴다. MonsterController 가 인스펙터 값을 넣는다
        public float HitRangeRatio { get; set; } = 1f;

        // 예고 중 WindupLookPoint 쪽으로 도는 속도 (도/초). 0 = 돌지 않는다. MonsterController 가 인스펙터 값을 넣는다
        public float WindupTurnSpeed { get; set; }

        // 진행 중(또는 마지막) 주기의 Active 길이 (s). MonsterController 가 Attack 클립 재생 배율 계산에 쓴다
        public float ActiveDuration => cycleActiveSeconds;
        public SkillPhase Phase { get; private set; } = SkillPhase.Idle;

        // 예고·공격·회복 중 하나에 있으면 true
        public bool IsAttacking => Phase != SkillPhase.Idle;

        public AttackResult LastResult { get; private set; }

        // 공격 상태로 들어가는 수평 거리 (m)
        public virtual float Range => Spec.Range;

        // LastResult 가 WallHit 일 때 기절 시간 (s)
        public float WallHitStunTime => Spec.WallHitStunTime;

        // ── 공격 뒤 행동 (공통 상태가 읽는 값. 하위 클래스가 바꾼다) ──
        // 정상 완료 뒤 갈 곳. 기본 = 쿨다운 동안 Chase 로 슬롯까지 이동
        public virtual AfterAttack After => AfterAttack.ChaseUntilReady;

        // Idle 휴식 중 준비 동작이 끝났으면 true. 준비 동작이 없는 공격은 항상 true
        public virtual bool IsPrepared => true;

        // ── 애니메이션 (MonsterController 가 읽는 값. 하위 클래스가 바꾼다) ──
        // true = 이동 자체가 공격(돌진 등). Attack 클립 대신 이동 클립을 BodyMoveSpeed 에 맞춰 재생한다
        public virtual bool UsesMoveAnimation => false;

        // 공격·준비 동작이 몸을 직접 옮기는 현재 속도 (m/s). 0 = 서 있음
        public virtual float BodyMoveSpeed => 0f;

        // 단계 변경 알림 (애니메이션·예고 표시·보스 이벤트). Idle = 종료
        public event Action<MonsterAttack, SkillPhase> PhaseChanged;

        // 공격 몸 이동이 땅에 닿음 (도약 착지). 인자 = 착지 지점. 소리·연출용
        public event Action<MonsterAttack, Vector3> BodyLanded;

        // 공격 몸이 대상에 부딪힘 (돌진 명중). 인자 = 부딪힌 지점. 소리·연출용
        public event Action<MonsterAttack, Vector3> BodyContacted;

        // 판정 1프레임 알림 (IHitArea 를 쓰는 공격). 인자 = 범위, 기준 위치, 기준 정면, 대상이 범위 안이었는지. 범위 시각화용
        public event Action<MonsterAttack, IHitArea, Vector3, Vector3, bool> MonsterHitEvent;

        // 진행 중인 공격의 대상. 진행 중이 아니면 null
        protected IPlayerTarget Target { get; private set; }

        // 공격자 몸체. 예고 중 회전과 하위 클래스의 발사 위치·판정 방향에 쓴다
        protected MonsterBody Body { get; }

        // 이번 주기에 노리는 월드 좌표 = 매 Tick 움직이는 플레이어 위치 + Begin 때 뽑은 랜덤 offset. 조준(방향·착지점·착탄점)에만 쓰고 피해 판정은 실제 플레이어 위치로 한다
        protected Vector3 AimPoint => Target.PlayerTrackingInfo.Position + aimOffset;

        // 판정 구간이 끝나는 시각 (s) = Active 시작 + ActiveDuration × HitEndRatio(Begin 때 고정). Active 에 들어간 뒤에만 의미 있다
        protected float HitEndTime => activeStartTime + ActiveDuration * cycleHitEndRatio;

        // 예고 중 바라볼 월드 좌표. 기본 = 조준점(AimPoint). 하위 클래스가 바꾼다 (독장판 = 고정 착탄점)
        protected virtual Vector3 WindupLookPoint => AimPoint;

        /// <summary>
        /// 공격 값, 몸체, 피해 대상 탐색 방법을 받는다. 하위 전략의 생성자가 호출한다. Cooldown 은 Spec.Cooldown, AimOffsetRadius 는 Spec.AimOffsetRadius 로 시작한다.
        /// </summary>
        /// <param name="spec">공격 1개의 값 묶음</param>
        /// <param name="body">공격자 몸체. 예고 중 회전에 쓴다</param>
        /// <param name="findDamageable">대상에게 피해를 줄 IDamageable 을 찾는 방법 (런타임에서 주입)</param>
        /// <exception cref="ArgumentNullException">body 또는 findDamageable 이 null 일 때</exception>
        protected MonsterAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (findDamageable == null) throw new ArgumentNullException(nameof(findDamageable));

            Spec = spec;
            Cooldown = spec.Cooldown;
            AimOffsetRadius = spec.AimOffsetRadius;
            Body = body;
            this.findDamageable = findDamageable;
        }

        /// <summary>
        /// 몸 이동 중 이동 클립 재생 배율의 기준 속도를 알려준다. MonsterController.UpdateMoveAnimation 이 UsesMoveAnimation 인 공격에 묻는다.
        /// 기본은 받은 값(몬스터 공통 이동 클립 기준 속도)을 그대로 돌려준다. 하위 클래스가 단계별로 바꾼다 (돌진).
        /// </summary>
        /// <param name="defaultClipSpeed">몬스터 공통 이동 클립 기준 속도 (m/s). MonsterController.moveClipSpeed</param>
        /// <returns>이동 클립을 1배속으로 재생할 때의 이동 속도 (m/s)</returns>
        public virtual float MoveClipSpeedFor(float defaultClipSpeed) => defaultClipSpeed;

        /// <summary>쿨다운 확인. AttackState 가 Begin 전에 부른다. 하위 클래스가 발동 조건을 더할 수 있다</summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <returns>진행 중이 아니고 쿨다운이 지났으면 true (정상 완료 = 회복 끝 기준, 취소·조기 종료 = Begin 기준)</returns>
        public virtual bool CanStart(float now) => IsAttacking == false && now >= nextReadyTime;

        /// <summary>
        /// 공격 한 주기를 시작한다 (예고 진입). AttackState 가 사거리 안·공격 허가·CanStart 일 때 부른다.
        /// 이 주기의 쿨다운·Active 길이·판정 비율·판정 끝 비율·조준 offset 을 여기서 고정하므로
        /// 진행 중에 Cooldown / ActiveSeconds / HitTimeRatio / HitEndRatio / AimOffsetRadius 가 바뀌어도 다음 Begin 부터 반영된다.
        /// </summary>
        /// <remarks>여기서 정하는 다음 시작 시각(시작 + 쿨다운)은 취소·조기 종료용이다. 정상 완료하면 Tick 이 회복 끝 기준으로 다시 정한다.</remarks>
        /// <param name="target">이번 주기의 공격 대상</param>
        /// <param name="now">현재 시각 (s)</param>
        /// <exception cref="InvalidOperationException">이미 진행 중인 공격이 있을 때</exception>
        /// <exception cref="ArgumentNullException">target 이 null 일 때</exception>
        public void Begin(IPlayerTarget target, float now)
        {
            if (IsAttacking) throw new InvalidOperationException("이미 진행 중인 공격이다");
            if (target == null) throw new ArgumentNullException(nameof(target));
            Target = target;
            LastResult = AttackResult.None;
            cycleCooldown = Cooldown;
            cycleActiveSeconds = ActiveSeconds;
            cycleHitTimeRatio = HitTimeRatio;
            cycleHitEndRatio = HitEndRatio;
            nextReadyTime = now + cycleCooldown;
            PickAimOffset();
            SetPhase(SkillPhase.Windup, now + Spec.TelegraphTime);
            OnWindup();
        }

        /// <summary>
        /// 이번 주기 조준 offset 을 반경 AimOffsetRadius 구 안에서 1번 뽑는다. Begin 이 OnWindup 전에 부른다.
        /// 주기 동안 고정해 두어야 조준이 매 틱 떨리지 않는다. 반경 0 이면 offset 0 = 정확히 플레이어를 노린다.
        /// </summary>
        private void PickAimOffset()
        {
            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere;
            randomOffset.y = 0;
            randomOffset.Normalize();
            aimOffset = randomOffset * AimOffsetRadius;
        }

        /// <summary>
        /// 단계를 진행한다. 예고 중에는 매 Tick WindupLookPoint 쪽으로 WindupTurnSpeed 만큼 돈다. Active 는 판정 시각(시작 + ActiveDuration × 판정 비율)까지 기다렸다가 OnActive 를 부르고,
        /// OnActive 가 끝났다고 알린 뒤 Active 시간이 다 지나야 회복으로 넘어간다.
        /// 회복이 끝나면 그 시각부터 쿨다운을 센다.
        /// AttackState 가 공격 진행 중 매 판단마다 부른다.
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">지난 판단부터 경과 시간 (s). 예고 중 회전량 계산과 OnActive 에 쓴다</param>
        /// <exception cref="InvalidOperationException">진행 중이 아닌(Idle) 공격을 Tick 할 때</exception>
        public void Tick(float now, float deltaTime)
        {
            switch (Phase)
            {
                case SkillPhase.Windup:
                    Body.TurnToward(WindupLookPoint, WindupTurnSpeed, deltaTime);
                    if (now < phaseEndTime) return;
                    activeStartTime = now;
                    SetPhase(SkillPhase.Active, now);
                    return;
                case SkillPhase.Active:
                    bool isBeforeHitTime = now < activeStartTime + ActiveDuration * cycleHitTimeRatio;
                    if (isBeforeHitTime) return;

                    if (isActionDone == false) isActionDone = OnActive(now, deltaTime);
                    if (Phase != SkillPhase.Active) return; // OnActive 안에서 EndEarly 로 끝난 경우 (벽 충돌 등)

                    bool isActiveTimeOver = now >= activeStartTime + ActiveDuration;
                    if (isActionDone == false) return;
                    if (isActiveTimeOver == false) return;
                    SetPhase(SkillPhase.Recover, now + Spec.RecoveryTime);
                    OnRecover();
                    return;
                case SkillPhase.Recover:
                    if (now < phaseEndTime) return;
                    nextReadyTime = now + cycleCooldown;
                    Finish(AttackResult.Completed);
                    return;
                default:
                    throw new InvalidOperationException("진행 중이 아닌 공격은 Tick 할 수 없다");
            }
        }

        /// <summary>
        /// 진행 중 공격을 중간에 끊는다 (OnCancel → 결과 Cancelled). 진행 중이 아니면 무시한다.
        /// 상태 머신의 대상 변경·정리와 AttackState.Exit 가 부른다.
        /// </summary>
        public void Cancel()
        {
            if (IsAttacking == false) return;
            OnCancel();
            Finish(AttackResult.Cancelled);
        }

        /// <summary>
        /// Idle 휴식 중 준비 동작 1틱 (돌진의 방향 정렬 등). IdleState 가 IsPrepared 가 될 때까지 매 틱 부른다.
        /// 기본은 아무것도 하지 않는다.
        /// </summary>
        /// <param name="target">공격 대상</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        public virtual void TickPreparation(IPlayerTarget target, float deltaTime) { }

        /// <summary>
        /// Chase 재정비가 끝났는지 판단한다 (공중의 선회 시간·고도·거리 등). ChaseState 가 재정비 중 매 틱 부른다.
        /// </summary>
        /// <param name="target">공격 대상</param>
        /// <param name="now">현재 시각 (s)</param>
        /// <returns>true = 재정비 끝, 다시 공격할 수 있다. 기본은 항상 true</returns>
        public virtual bool IsRepositioned(IPlayerTarget target, float now) => true;

        // ── 하위 클래스 훅 ──

        /// <summary>예고 시작 (목표 확정, 예고 범위 표시). 회전은 Tick 이 WindupLookPoint 로 한다</summary>
        protected virtual void OnWindup() { }

        /// <summary>실제 공격 (판정 시각부터 매 틱 호출)</summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>true = 공격 동작 완료, 이후 다시 부르지 않고 Active 시간이 끝나면 회복으로 넘어간다. false = 다음 틱에 다시 부른다</returns>
        protected abstract bool OnActive(float now, float deltaTime);

        /// <summary>회복(경직) 시작</summary>
        protected virtual void OnRecover() { }

        /// <summary>처치 등으로 중간에 끊길 때</summary>
        protected virtual void OnCancel() { }

        /// <summary>회복을 건너뛰고 즉시 끝낸다 (돌진 중 벽 충돌 등). 하위 클래스의 OnActive 안에서 부른다</summary>
        /// <param name="result">끝난 이유. LastResult 에 남는다 (WallHit 이면 AttackState 가 기절시킨다)</param>
        protected void EndEarly(AttackResult result) => Finish(result);

        /// <summary>몸 이동이 땅에 닿았음을 알린다 (도약 착지). 하위 클래스가 실제로 내려앉은 순간에 부른다</summary>
        /// <param name="ground">착지 지점</param>
        protected void NotifyBodyLanded(Vector3 ground) => BodyLanded?.Invoke(this, ground);

        /// <summary>몸이 대상에 부딪혔음을 알린다 (돌진 명중). 하위 클래스가 실제로 닿은 순간에 부른다</summary>
        /// <param name="point">부딪힌 지점</param>
        protected void NotifyBodyContacted(Vector3 point) => BodyContacted?.Invoke(this, point);

        /// <summary>범위 판정을 한 순간을 알린다. IHitArea 로 판정하는 하위 클래스가 Contains 직후, 피해를 주기 전에 부른다</summary>
        /// <param name="area">판정에 쓴 범위</param>
        /// <param name="origin">판정 기준 위치 (월드, m)</param>
        /// <param name="forward">판정 기준 정면</param>
        /// <param name="didHit">대상이 범위 안이었으면 true</param>
        protected void NotifyMonsterHit(IHitArea area, Vector3 origin, Vector3 forward, bool didHit)
            => MonsterHitEvent?.Invoke(this, area, origin, forward, didHit);

        /// <summary>대상에게 Damage × HitCount 피해. 대상이 이미 죽었으면 주지 않는다</summary>
        /// <param name="target">피해를 받을 대상</param>
        /// <param name="from">공격 위치. 피해 방향(수평) 계산에 쓴다</param>
        /// <exception cref="InvalidOperationException">대상에서 IDamageable 을 찾지 못했을 때</exception>
        protected void Hit(IPlayerTarget target, Vector3 from)
        {
            var damageable = findDamageable(target);
            if (damageable == null) throw new InvalidOperationException("공격 대상에서 IDamageable 을 찾지 못했다");
            if (damageable.CanBeHitBy(Faction.Monster) == false) return;

            Vector3 direction = HitAreas.Flat(target.PlayerTrackingInfo.Position - from);
            direction = direction.magnitude > 1e-4f ? direction.normalized : Vector3.zero;
            var info = new DamageInfo(Spec.Damage, DamageKind.MonsterAttack, Faction.Monster, null,
                                      target.PlayerTrackingInfo.Position, direction, Spec.SourceDefId);
            
            // 전투시스템 없어서 직접 피해주기
            for (int i = 0; i < Spec.HitCount; i++)
                damageable.TakeDamage(in info);
        }

        /// <summary>주기를 끝낸다 (완료·취소·조기 종료 공통). 다음 주기를 위해 판정 완료 표시를 되돌린다</summary>
        /// <param name="result">끝난 이유. LastResult 에 남긴다</param>
        private void Finish(AttackResult result)
        {
            LastResult = result;
            Target = null;
            isActionDone = false;
            SetPhase(SkillPhase.Idle, 0f);
        }

        /// <summary>단계를 바꾸고 PhaseChanged 를 발행한다. 모든 단계 변경은 여기를 지난다</summary>
        /// <param name="phase">새 단계</param>
        /// <param name="endTime">이 단계가 끝나는 시각 (s). Windup·Recover 만 쓰고 Active·Idle 은 의미 없음</param>
        private void SetPhase(SkillPhase phase, float endTime)
        {
            Phase = phase;
            phaseEndTime = endTime;
            PhaseChanged?.Invoke(this, phase);
        }
    }
}
