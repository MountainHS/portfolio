using System;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>피격 모션을 고르는 맞은 쪽. Animator 의 Hit 레이어 상태 이름이 "Hit" + 이 이름이다</summary>
    public enum HitSide { Front, Back, Left, Right }

    /// <summary>
    /// 몬스터 피격·저체력·사망의 보이는 표현 (BL-017): 맞은 곳의 피, 피격 모션, 저체력 붉은 기, 사망 피 터짐.
    /// MonsterController 가 피해·저체력 진입·사망·재사용 순간에 부른다. 체력·이동 속도 같은 게임플레이 값은 바꾸지 않는다.
    /// </summary>
    /// <remarks>
    /// 피격 모션은 Animator 의 Additive "Hit" 레이어에 재생한다. 아래 레이어(이동·공격·기절)는 끊기지 않고 몸만 흔들린다.
    /// 피 이펙트는 이펙트 종류마다 EffectPool 1개로 재사용한다. 풀은 Awake 에서 만든다.
    /// </remarks>
    public sealed class MonsterDamageFeedback : MonoBehaviour
    {
        [Header("피 이펙트")]
        [Tooltip("총알이 맞은 지점에서 맞은 쪽으로 튀는 피")]
        [SerializeField] private PooledEffect hitBloodPrefab;
        [Tooltip("약점에 맞았을 때 튀는 피. 일반 피보다 크고 밝다")]
        [SerializeField] private PooledEffect weakPointBloodPrefab;
        [Tooltip("죽는 순간 몸통 중심에서 터지는 피")]
        [SerializeField] private PooledEffect deathBloodPrefab;

        [Header("피격 모션")]
        [Tooltip("피격 모션을 다시 재생하기까지 최소 간격 (s). 연사에 매 발 재생하면 경련처럼 보인다. 피는 매 발 튄다")]
        [SerializeField, Min(0f)] private float hitAnimationCooldown = 0.3f;
        [Tooltip("피격 모션으로 넘어가는 블렌드 시간 (s)")]
        [SerializeField, Min(0f)] private float hitAnimationBlend = 0.05f;

        [Header("저체력 붉은 기")]
        [Tooltip("저체력일 때 머티리얼 기본색에 곱할 색. 흰색 = 변화 없음")]
        [SerializeField] private Color lowHealthTint = new Color(1f, 0.55f, 0.55f);

        // Animator 의 피격 모션 레이어 이름 (Monster Base Controller)
        private const string HitLayerName = "Hit";
        // URP Lit 머티리얼의 기본색 속성
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Animator animator;
        [SerializeField] private Collider bodyCollider;        // 사망 피를 터뜨릴 몸통 중심
        private Renderer[] bodyRenderers;     // 붉은 기를 입힐 몸 렌더러 전부
        private int hitLayerIndex;
        private float nextHitAnimationTime;   // 다음 피격 모션을 재생해도 되는 시각 (s, Time.time 기준)

        private EffectPool hitBloodPool;
        private EffectPool weakPointBloodPool;
        private EffectPool deathBloodPool;

        /// <summary>참조를 잡고 피 이펙트 풀 3개를 만든다</summary>
        /// <exception cref="InvalidOperationException">Animator Controller 에 "Hit" 레이어가 없을 때</exception>
        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider>();
            bodyRenderers = GetComponentsInChildren<Renderer>();
            hitLayerIndex = animator.GetLayerIndex(HitLayerName);
            if (hitLayerIndex < 0)
                throw new InvalidOperationException($"{name}: Animator Controller 에 {HitLayerName} 레이어가 없다 (Tools/HybridShooter/Setup Monster Hit Feedback)");

            hitBloodPool = new EffectPool(hitBloodPrefab);
            weakPointBloodPool = new EffectPool(weakPointBloodPrefab);
            deathBloodPool = new EffectPool(deathBloodPrefab);
        }

        /// <summary>
        /// 맞은 지점에 피를 튀긴다. 피는 총알이 들어온 쪽(공격자 쪽)으로 튄다. MonsterController 가 피해를 받을 때마다 부른다.
        /// </summary>
        /// <param name="hitPoint">명중 위치 (월드)</param>
        /// <param name="incomingDirection">피해가 들어온 방향 (공격자 → 몬스터, 월드). 0 = 방향 없음 → 위로 튄다</param>
        /// <param name="isWeakPoint">true = 약점 명중. 약점용 피를 쓴다</param>
        public void PlayHitBlood(Vector3 hitPoint, Vector3 incomingDirection, bool isWeakPoint)
        {
            Vector3 sprayDirection = Vector3.up;
            if (incomingDirection != Vector3.zero) sprayDirection = -incomingDirection;
            Quaternion rotation = Quaternion.LookRotation(sprayDirection);

            if (isWeakPoint) weakPointBloodPool.Play(hitPoint, rotation);
            else hitBloodPool.Play(hitPoint, rotation);
        }

        /// <summary>
        /// 맞은 쪽(앞·뒤·왼쪽·오른쪽)에 맞는 피격 모션을 Hit 레이어에 재생한다. 쿨다운 중이면 건너뛴다.
        /// MonsterController 가 죽지 않은 피격마다 부른다. 상태(이동·공격·기절)와 관계없이 재생한다.
        /// </summary>
        /// <param name="incomingDirection">피해가 들어온 방향 (공격자 → 몬스터, 월드)</param>
        public void PlayHitAnimation(Vector3 incomingDirection)
        {
            if (Time.time < nextHitAnimationTime) return;

            nextHitAnimationTime = Time.time + hitAnimationCooldown;
            Vector3 localDirection = transform.InverseTransformDirection(incomingDirection);
            HitSide side = ToHitSide(localDirection);
            animator.CrossFadeInFixedTime("Hit" + side, hitAnimationBlend, hitLayerIndex, 0f);
        }

        /// <summary>
        /// 들어온 방향으로 맞은 쪽을 고른다. 앞뒤·좌우 중 더 큰 성분 쪽이다.
        /// 예: 총알이 몬스터의 뒤쪽(-Z)으로 날아가면 앞에서 맞은 것 → Front.
        /// </summary>
        /// <param name="localIncomingDirection">피해가 들어온 방향 (공격자 → 몬스터, 몬스터 로컬). 0 = Front</param>
        /// <returns>맞은 쪽</returns>
        public static HitSide ToHitSide(Vector3 localIncomingDirection)
        {
            bool isFrontOrBack = Mathf.Abs(localIncomingDirection.z) >= Mathf.Abs(localIncomingDirection.x);
            if (isFrontOrBack)
            {
                if (localIncomingDirection.z <= 0f) return HitSide.Front;
                return HitSide.Back;
            }
            if (localIncomingDirection.x < 0f) return HitSide.Right;
            return HitSide.Left;
        }

        /// <summary>
        /// 몸 머티리얼 기본색에 붉은 기를 곱한다. 머티리얼 에셋은 바꾸지 않고 렌더러별 MaterialPropertyBlock 으로 덮는다.
        /// MonsterController 가 저체력에 처음 들어갈 때 1번 부른다.
        /// </summary>
        public void ShowLowHealth()
        {
            var tintBlock = new MaterialPropertyBlock();
            foreach (Renderer bodyRenderer in bodyRenderers)
            {
                Color baseColor = bodyRenderer.sharedMaterial.GetColor(BaseColorId);
                tintBlock.SetColor(BaseColorId, baseColor * lowHealthTint);
                bodyRenderer.SetPropertyBlock(tintBlock);
            }
        }

        /// <summary>몸통 중심에서 피를 사방으로 터뜨린다. MonsterController 가 사망 순간 1번 부른다 (처치·자폭·정리 모두)</summary>
        public void PlayDeathBlood()
        {
            deathBloodPool.Play(bodyCollider.bounds.center, Quaternion.identity);
        }

        /// <summary>
        /// 풀 재사용 전 표현 상태를 처음으로 되돌린다: 붉은 기를 지우고 피격 모션 쿨다운을 비운다.
        /// MonsterController.Respawn 이 부른다. Animator 상태는 GameObject 를 끄고 켤 때 초기화된다.
        /// </summary>
        public void ResetForRespawn()
        {
            nextHitAnimationTime = 0f;
            foreach (Renderer bodyRenderer in bodyRenderers) bodyRenderer.SetPropertyBlock(null);
        }
    }
}
