using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 역할(Role)에 맞는 공격 전략을 만든다. MonsterController.Start 가 조립 때 호출한다.
    /// 새 공격 방식 = 전략 클래스 1개 + case 1줄.
    /// </summary>
    public static class MonsterAttackFactory
    {
        /// <summary>
        /// 역할 → 공격 전략. 만든 공격은 MonsterStateMachine 에 넘겨져 AttackState 가 진행한다.
        /// </summary>
        /// <param name="role">몬스터 역할. Melee, Infiltrator, ZonePressure, Rusher, Air, SelfDestruct 지원</param>
        /// <param name="spec">공격 1개의 값 묶음</param>
        /// <param name="body">공격자 몸체 (위치·방향)</param>
        /// <param name="findDamageable">공격 대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <param name="settings">몬스터에 붙은 공격 설정 컴포넌트 전부. 역할에 필요한 타입만 FindSettings 로 꺼낸다</param>
        /// <param name="movement">돌진·급강하가 몸을 직접 옮길 때 쓰는 이동 컴포넌트. Rusher, Air 가 쓴다</param>
        /// <param name="flyingExecutor">공중(Air) 프리팹의 비행 실행기. 다른 역할은 null</param>
        /// <returns>역할에 맞는 공격 전략</returns>
        /// <exception cref="NotSupportedException">아직 공격이 구현되지 않은 역할일 때</exception>
        public static MonsterAttack CreateAttack(MonsterRole role, in AttackSpec spec, MonsterBody body,
                                                  Func<IPlayerTarget, IDamageable> findDamageable,
                                                  IReadOnlyList<MonsterAttackSettingsComponent> settings,
                                                  MonsterMovement movement, FlyingMoveExecutor flyingExecutor)
        {
            switch (role)
            {
                case MonsterRole.Melee: return new StrikeAttack(spec, body, findDamageable);
                case MonsterRole.SelfDestruct: return new SelfDestructAttack(spec, body, findDamageable, FindSettings<SelfDestructSettings>(settings));
                case MonsterRole.Infiltrator: return new PounceAttack(spec, body, findDamageable, FindSettings<PounceSettings>(settings));
                case MonsterRole.ZonePressure: return new PoisonAreaAttack(spec, body, findDamageable, FindSettings<PoisonAreaSettings>(settings));
                case MonsterRole.Rusher: return new ChargeAttack(spec, body, findDamageable, movement, FindSettings<ChargeSettings>(settings));
                case MonsterRole.Air: return new DiveAttack(spec, body, findDamageable, movement, FindSettings<DiveSettings>(settings), flyingExecutor);
                default: throw new NotSupportedException($"{role} 역할의 공격은 아직 구현되지 않았다");
            }
        }

        /// <summary>
        /// 설정 목록에서 원하는 타입의 설정을 찾는다. 공격 하나가 여러 설정을 쓰면 타입별로 한 번씩 부른다.
        /// </summary>
        /// <typeparam name="T">찾을 설정 타입 (PounceSettings 등)</typeparam>
        /// <param name="settings">몬스터에 붙은 공격 설정 컴포넌트 전부</param>
        /// <returns>처음 찾은 T 타입 설정. 없으면 null</returns>
        private static T FindSettings<T>(IReadOnlyList<MonsterAttackSettingsComponent> settings)
            where T : MonsterAttackSettingsComponent
        {
            foreach (MonsterAttackSettingsComponent candidate in settings)
            {
                if (candidate is T found) return found;
            }
            return null;
        }
    }
}
