using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 돌진 (Rusher 역할). 이동 자체가 공격이다.
    /// 예고 중에는 공통 회전으로 플레이어를 서서히 겨누고, Active 시작에 방향을 다시 잡은 뒤 플레이어를 지나칠 때까지 달린다.
    /// 부딪히면 1회 피해, 부딪히거나 지나치면 같은 감속률로 멈춘다. 벽에 막히면 기절(WallHit)한다.
    /// 끝나면 Idle 에서 쉬면서 천천히 걸으며 정면을 맞추고(준비 동작), 맞춰지면 다시 공격할 수 있다.
    /// </summary>
    public sealed class ChargeAttack : MonsterAttack
    {
        // 돌진 방향과 (몬스터 → 플레이어) 방향의 각이 이 이상이면 플레이어를 지나친 것이다 (도)
        private const float PassAngleDeg = 155f;
        // 정면과 (몬스터 → 플레이어) 방향의 각이 이 이하면 준비 완료 (도)
        private const float PreparedAngleDeg = 30f;
        // 이보다 가까우면 방향 판정을 하지 않는다 (m)
        private const float MinJudgeDistance = 0.01f;
        // 한 번에 다 도는 회전량 (도). Active 시작의 방향 재설정에 쓴다
        private const float FullTurnDegrees = 360f;

        /// <summary>돌진 진행 단계. Aiming → Charging → Braking → Stopped</summary>
        private enum ChargeStage { Aiming, Charging, Braking, Stopped }

        private readonly MonsterMovement movement;
        private readonly ChargeSettings settings;

        // ── 이번 주기 (OnWindup 에서 초기화) ──
        private ChargeStage stage;
        private float currentSpeed;        // m/s. 준비 동작 중에는 걷는 속도
        private float travelledDistance;   // m, 이번 돌진에서 달린 수평 거리
        private bool hasHit;               // 이번 주기에 피해를 줬나 (주기당 1회)

        // ── Idle 준비 동작 ──
        private bool isFacingTarget;       // 마지막 TickPreparation 에서 정면이 플레이어 쪽(30° 이내)이었나

        public override AfterAttack After => AfterAttack.RestInIdle;
        public override bool IsPrepared => isFacingTarget;
        public override bool UsesMoveAnimation => true;
        public override float BodyMoveSpeed => currentSpeed;

        /// <summary>MonsterAttackFactory 가 Rusher 역할에 만든다</summary>
        /// <param name="spec">공격 값 묶음. Range = 공격 진입 거리, Width / 2 = 접촉 반경, WallHitStunTime = 벽 기절 시간</param>
        /// <param name="body">공격자 몸체. 돌진 방향 = 몸 정면</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <param name="movement">몸을 직접 옮기는 이동 컴포넌트 (MoveDuringAttack)</param>
        /// <param name="settings">돌진 프리팹의 튜닝 값</param>
        /// <exception cref="ArgumentNullException">body, movement, settings 중 하나가 null 일 때 (프리팹에 ChargeSettings 가 없을 때 등). body 는 기반 생성자가 던진다</exception>
        public ChargeAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable,
                            MonsterMovement movement, ChargeSettings settings)
            : base(spec, body, findDamageable)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            if (settings == null) throw new ArgumentNullException(nameof(settings), "돌진 몬스터 프리팹에 ChargeSettings 가 없다");

            this.movement = movement;
            this.settings = settings;
        }

        /// <summary>
        /// 달리는(Charging) 동안만 돌진 전용 클립 기준 속도를 쓴다. 감속·준비 동작은 몬스터 공통 값을 쓴다.
        /// MonsterController.UpdateMoveAnimation 이 매 프레임 묻는다.
        /// </summary>
        /// <param name="defaultClipSpeed">몬스터 공통 이동 클립 기준 속도 (m/s)</param>
        /// <returns>Charging 이면 ChargeSettings.ChargeClipSpeed, 아니면 defaultClipSpeed (m/s)</returns>
        public override float MoveClipSpeedFor(float defaultClipSpeed)
        {
            if (stage == ChargeStage.Charging) return settings.ChargeClipSpeed;
            return defaultClipSpeed;
        }

        /// <summary>
        /// Idle 휴식 중 준비 동작 1틱. 정면이 플레이어 쪽 30° 이내면 멈추고 준비 완료, 아니면 천천히 돌면서 정면으로 걷는다.
        /// IdleState 가 IsPrepared 를 읽기 전에 매 틱 부른다.
        /// </summary>
        /// <param name="target">공격 대상</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        public override void TickPreparation(IPlayerTarget target, float deltaTime)
        {
            Vector3 targetPosition = target.PlayerTrackingInfo.Position;
            Vector3 toTarget = HitAreas.Flat(targetPosition - Body.Position);
            isFacingTarget = Vector3.Angle(HitAreas.Flat(Body.Forward), toTarget) <= PreparedAngleDeg;
            if (isFacingTarget)
            {
                currentSpeed = 0f;
                return;
            }

            Body.TurnToward(targetPosition, settings.PrepTurnSpeed, deltaTime);
            currentSpeed = settings.PrepMoveSpeed;
            movement.MoveDuringAttack(ForwardStep(deltaTime)); // 벽에 막히면 그 틱은 막힌 곳까지만 간다
        }

        /// <summary>예고 시작: 이번 돌진 상태를 비운다. 플레이어 쪽 회전은 기반 Tick 이 예고 내내 한다</summary>
        protected override void OnWindup()
        {
            stage = ChargeStage.Aiming;
            currentSpeed = 0f;
            travelledDistance = 0f;
            hasHit = false;
        }

        /// <summary>
        /// 돌진 1틱. 첫 틱에 방향을 다시 잡고 달리기 시작 → 달리기 → 감속 순서로 진행한다.
        /// 기반 Tick 이 Active 동안 true 를 받을 때까지 매 틱 부른다.
        /// </summary>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>true = 멈춤. 남은 Active 시간은 서서 기다린다</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            if (stage == ChargeStage.Aiming) StartCharging();
            if (stage == ChargeStage.Charging) TickCharging(deltaTime);
            if (stage == ChargeStage.Braking) TickBraking(deltaTime);
            return stage == ChargeStage.Stopped;
        }

        /// <summary>기절·사망·대상 상실로 끊길 때 멈춘다. 이동은 상태(Attack/Idle)의 정지가 이미 막고 있다</summary>
        protected override void OnCancel()
        {
            stage = ChargeStage.Stopped;
            currentSpeed = 0f;
        }

        /// <summary>Active 시작: 몸을 조준점(AimPoint) 쪽으로 한 번에 돌리고 돌진 속도로 달리기 시작한다. OnActive 첫 틱이 부른다</summary>
        private void StartCharging()
        {
            Body.TurnToward(AimPoint, FullTurnDegrees, 1f);
            currentSpeed = settings.ChargeSpeed;
            stage = ChargeStage.Charging;
        }

        /// <summary>
        /// 달리기 1틱: 조준점(AimPoint) 쪽으로 천천히 방향을 보정하고 정면으로 이동한다.
        /// 벽에 막히면 기절로 끝내고, 이동 구간에서 실제 플레이어와 부딪히면 피해 후 감속, 조준점을 지나쳤거나 최대 거리를 넘으면 감속으로 넘어간다.
        /// </summary>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        private void TickCharging(float deltaTime)
        {
            Vector3 aimPoint = AimPoint;
            Body.TurnToward(aimPoint, settings.ChargeTurnSpeed, deltaTime);
            Vector3 from = Body.Position;
            bool hitWall = movement.MoveDuringAttack(ForwardStep(deltaTime));
            Vector3 to = Body.Position;
            travelledDistance += HitAreas.Flat(to - from).magnitude;

            if (hitWall)
            {
                HitWall();
                return;
            }
            HitIfTouched(from, to, Target.PlayerTrackingInfo.Position);

            bool passedTarget = IsPastTarget(Body.Forward, to, aimPoint);
            bool reachedMaxDistance = travelledDistance >= settings.MaxChargeDistance;
            if (hasHit || passedTarget || reachedMaxDistance) stage = ChargeStage.Braking;
        }

        /// <summary>
        /// 감속 1틱: 감속률만큼 속도를 줄이며 정면으로 이동한다. 아직 피해를 안 줬으면 접촉 시 피해를 준다.
        /// 벽에 막히거나 속도가 0 이 되면 멈춘다 (감속 중 벽은 기절시키지 않는다).
        /// </summary>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        private void TickBraking(float deltaTime)
        {
            currentSpeed = Mathf.Max(0f, currentSpeed - settings.BrakeDeceleration * deltaTime);
            Vector3 from = Body.Position;
            bool hitWall = movement.MoveDuringAttack(ForwardStep(deltaTime));
            Vector3 to = Body.Position;
            HitIfTouched(from, to, Target.PlayerTrackingInfo.Position);

            bool isStopped = currentSpeed <= 0f;
            if (hitWall || isStopped)
            {
                currentSpeed = 0f;
                stage = ChargeStage.Stopped;
            }
        }

        /// <summary>달리다 벽에 막혔다: 멈추고 주기를 WallHit 으로 끝낸다 (AttackState 가 기절시킨다). TickCharging 이 부른다</summary>
        private void HitWall()
        {
            currentSpeed = 0f;
            stage = ChargeStage.Stopped;
            EndEarly(AttackResult.WallHit);
        }

        /// <summary>이번 틱 이동 구간이 플레이어에 닿았으면 피해를 주고 충돌을 알린다. 주기당 1회. TickCharging·TickBraking 이 부른다</summary>
        /// <param name="from">이동 전 위치 (월드)</param>
        /// <param name="to">이동 후 위치 (월드)</param>
        /// <param name="targetPosition">플레이어 위치 (월드)</param>
        private void HitIfTouched(Vector3 from, Vector3 to, Vector3 targetPosition)
        {
            if (hasHit) return;
            if (TouchesDuringMove(from, to, targetPosition, Spec.Width * 0.5f) == false) return;

            Hit(Target, from);
            hasHit = true;
            NotifyBodyContacted(targetPosition);
        }

        /// <summary>이번 틱 정면 이동량 (수평). 속도 = currentSpeed</summary>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>이동량 (월드, m)</returns>
        private Vector3 ForwardStep(float deltaTime) => HitAreas.Flat(Body.Forward).normalized * currentSpeed * deltaTime;

        /// <summary>
        /// 돌진이 플레이어를 지나쳤는지 판정한다. 돌진 방향과 (몬스터 → 플레이어) 수평 방향의 각이 155° 이상이면 지나친 것이다.
        /// TickCharging 이 매 틱 부른다.
        /// </summary>
        /// <param name="chargeDirection">돌진 방향 (몸 정면). 높이는 무시한다</param>
        /// <param name="monsterPosition">몬스터 위치 (월드)</param>
        /// <param name="targetPosition">플레이어 위치 (월드)</param>
        /// <returns>지나쳤으면 true. 수평 거리가 0.01m 미만이면 판정하지 않고 false</returns>
        public static bool IsPastTarget(Vector3 chargeDirection, Vector3 monsterPosition, Vector3 targetPosition)
        {
            Vector3 toTarget = HitAreas.Flat(targetPosition - monsterPosition);
            if (toTarget.magnitude < MinJudgeDistance) return false;
            return Vector3.Angle(HitAreas.Flat(chargeDirection), toTarget) >= PassAngleDeg;
        }

        /// <summary>
        /// 이번 틱 이동 구간(선분)이 플레이어에 닿았는지 수평으로 판정한다. 빠른 돌진이 한 틱에 플레이어를 건너뛰어도 놓치지 않게 한다.
        /// </summary>
        /// <param name="from">이동 전 위치 (월드)</param>
        /// <param name="to">이동 후 위치 (월드)</param>
        /// <param name="targetPosition">플레이어 위치 (월드)</param>
        /// <param name="contactRadius">접촉 반경 (m)</param>
        /// <returns>선분과 플레이어 사이의 수평 최단 거리가 접촉 반경 이하면 true</returns>
        public static bool TouchesDuringMove(Vector3 from, Vector3 to, Vector3 targetPosition, float contactRadius)
        {
            Vector3 segment = HitAreas.Flat(to - from);
            Vector3 fromToTarget = HitAreas.Flat(targetPosition - from);
            float segmentLength = segment.magnitude;
            if (segmentLength < MinJudgeDistance) return fromToTarget.magnitude <= contactRadius;

            Vector3 segmentDirection = segment / segmentLength;
            float distanceAlongSegment = Mathf.Clamp(Vector3.Dot(fromToTarget, segmentDirection), 0f, segmentLength);
            Vector3 closestPoint = segmentDirection * distanceAlongSegment;
            return Vector3.Distance(fromToTarget, closestPoint) <= contactRadius;
        }
    }
}
