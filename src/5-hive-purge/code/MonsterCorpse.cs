using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 죽은 몬스터 몸을 물리 시체로 바꿔 중력·충돌로 굴러가게 하고, 잠시 누워 있다가 땅속으로 가라앉힌 뒤 회수를 알린다.
    /// 풀 재사용 때는 살아있는 몸으로 되돌린다.
    /// MonsterController 가 사망 순간 BecomeCorpse, 죽어 있는 동안 매 프레임 Tick, Respawn 때 Restore 를 부른다.
    /// </summary>
    /// <remarks>
    /// Rigidbody 는 죽는 순간 붙이고 Restore 가 뗀다. 살아 있는 동안에는 Rigidbody 가 없어서
    /// NavMeshAgent·루트 모션·MonsterBody 회전으로 움직이는 기존 이동이 그대로다.
    /// 시체 단계: Alive → Lying(물리로 구르며 사망 애니) → Sinking(물리 정지, 아래로 이동) → Gone(회수 대기).
    /// </remarks>
    public sealed class MonsterCorpse : MonoBehaviour
    {
        /// <summary>시체 단계. 한 번에 하나만</summary>
        private enum CorpseStage { Alive, Lying, Sinking, Gone }

        [Header("시체 물리")]
        [Tooltip("시체가 쓰는 레이어. 충돌 매트릭스에서 살아있는 몬스터 레이어와 부딪히지 않게 해 두었다")]
        [SerializeField] private string corpseLayerName = "MonsterCorpse";
        [Tooltip("시체가 바닥에 닿을 몸통 콜라이더. 비우면 루트의 Collider. 몸통이 자식에 있는 프리팹(GiantSlug 의 Hit Collider)은 직접 연결")]
        [SerializeField] private Collider bodyCollider;
        [Tooltip("시체 질량 (kg). 수류탄 폭발에 밀리는 정도")]
        [SerializeField, Min(0.01f)] private float mass = 60f;
        [Tooltip("굴러가다 멈추는 회전 감쇠. 클수록 빨리 멈춘다")]
        [SerializeField, Min(0f)] private float angularDamping = 0.5f;

        [Header("죽는 순간 튕김")]
        [Tooltip("마지막 피격 방향(공격자 → 몬스터)으로 밀어내는 속도 (m/s)")]
        [SerializeField, Min(0f)] private float hitPushSpeed = 3f;
        [Tooltip("위로 띄우는 속도 (m/s)")]
        [SerializeField, Min(0f)] private float upwardKickSpeed = 1.5f;
        [Tooltip("무작위 회전 속도 최대값 (rad/s). 시체가 한쪽으로 넘어지며 구르게 한다")]
        [SerializeField, Min(0f)] private float randomSpinSpeed = 2f;

        [Header("시체 수명")]
        [Tooltip("죽은 뒤 가라앉기 시작할 때까지 누워 있는 시간 (s). 가장 긴 사망 애니(약 2.2초)보다 길게")]
        [SerializeField, Min(0f)] private float lyingSeconds = 4f;
        [Tooltip("땅속으로 가라앉는 데 걸리는 시간 (s). 끝나면 풀로 회수된다")]
        [SerializeField, Min(0.01f)] private float sinkSeconds = 1.5f;
        [Tooltip("몸통 높이에 더해 더 내려가는 깊이 (m). 몸 전체가 바닥 아래로 사라지게 하는 여유")]
        [SerializeField, Min(0f)] private float extraSinkDepth = 0.5f;

        private Rigidbody body;              // 죽어 있는 동안만 있다. null = 살아 있음
        private NavMeshAgent agent;          // 지상 몬스터만. 공중은 null
        private Animator animator;
        private int aliveLayer;
        private int aliveBodyColliderLayer;
        private bool aliveAppliesRootMotion;

        // ── 시체 단계 ──
        private CorpseStage stage = CorpseStage.Alive;
        private float stageStartTime;        // 지금 단계에 들어간 시각 (s, Time.time 기준)
        private float sinkStartY;            // 가라앉기 시작한 월드 y (m)
        private float sinkEndY;              // 가라앉기를 끝낼 월드 y (m)

        // 침강이 끝나 풀로 회수해도 되는가. MonsterController 가 보고 Despawn 한다
        public bool IsGone => stage == CorpseStage.Gone;

        /// <summary>살아있는 몸의 레이어·루트 모션 설정을 기억한다 (Restore 가 되돌릴 값)</summary>
        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider>();
            aliveLayer = gameObject.layer;
            aliveBodyColliderLayer = bodyCollider.gameObject.layer;
            aliveAppliesRootMotion = animator.applyRootMotion;
        }

        /// <summary>
        /// 몸을 물리 시체로 바꾼다. 이동 장치를 끄고, 몸통 콜라이더만 남기고, Rigidbody 를 붙여 죽는 순간 속도로 날려 보낸다.
        /// MonsterController 가 사망 순간 이동 컴포넌트를 끈 직후 부른다. 여기서부터 Lying 단계다.
        /// </summary>
        /// <param name="deathVelocity">죽는 순간 몸의 속도 (m/s, 월드). 공중 몬스터는 비행 속도, 지상은 0</param>
        /// <param name="hitDirection">마지막 피격 방향 (공격자 → 몬스터, 월드). 0 = 피격 없이 죽음 (밀지 않는다)</param>
        public void BecomeCorpse(Vector3 deathVelocity, Vector3 hitDirection)
        {
            StopAliveMovers();
            KeepOnlyBodyCollider();
            int corpseLayer = LayerMask.NameToLayer(corpseLayerName);
            gameObject.layer = corpseLayer;
            bodyCollider.gameObject.layer = corpseLayer;
            AddCorpseBody();

            Vector3 hitPush = hitDirection.normalized * hitPushSpeed;
            body.linearVelocity = deathVelocity + hitPush + Vector3.up * upwardKickSpeed;
            body.angularVelocity = Random.insideUnitSphere * randomSpinSpeed;
            EnterStage(CorpseStage.Lying);
        }

        /// <summary>
        /// 시체 단계를 시간에 따라 넘긴다: 누워 있기가 끝나면 가라앉기 시작, 가라앉기가 끝나면 Gone.
        /// MonsterController.Update 가 죽어 있는 동안 매 프레임 부른다. Gone 이 되면 호출자가 회수한다.
        /// </summary>
        /// <param name="now">현재 시각 (s, Time.time 기준)</param>
        public void Tick(float now)
        {
            float elapsed = now - stageStartTime;
            switch (stage)
            {
                case CorpseStage.Lying:
                    // 남은 시간에 맞춰 현재 회전 속도를 줄이고, 침강 시작 시 0으로 만든다.
                    float remaining = lyingSeconds - elapsed;
                    float spinFade = remaining > 0f ? Time.deltaTime / (remaining + Time.deltaTime) : 1f;
                    body.angularVelocity = Vector3.Lerp(body.angularVelocity, Vector3.zero, spinFade);
                    if (elapsed >= lyingSeconds) StartSinking();
                    return;
                case CorpseStage.Sinking:
                    MoveDownWhileSinking(elapsed);
                    if (elapsed >= sinkSeconds) EnterStage(CorpseStage.Gone);
                    return;
            }
        }

        /// <summary>
        /// 시체를 살아있는 몸으로 되돌린다: Rigidbody 를 떼고 레이어·루트 모션·NavMeshAgent 를 원래대로, 단계는 Alive 로.
        /// 콜라이더는 MonsterController.Respawn 이 모두 다시 켠다. 위치는 풀이 소환 위치로 옮긴다.
        /// 죽지 않은 채 재사용되면(라운드 정리) 할 일이 없다.
        /// </summary>
        /// <remarks>Respawn 이 상태 머신 조립(이동 재개)보다 먼저 불러야 한다. 이동이 꺼진 NavMeshAgent 를 쓰지 않게 한다.</remarks>
        public void Restore()
        {
            if (stage == CorpseStage.Alive) return;

            stage = CorpseStage.Alive;
            body.isKinematic = true;
            Destroy(body);
            body = null;
            gameObject.layer = aliveLayer;
            bodyCollider.gameObject.layer = aliveBodyColliderLayer;
            animator.applyRootMotion = aliveAppliesRootMotion;
            if (agent != null) agent.enabled = true;
        }

        /// <summary>NavMeshAgent·루트 모션이 물리 대신 몸을 옮기지 않게 끈다</summary>
        private void StopAliveMovers()
        {
            if (agent != null) agent.enabled = false;
            animator.applyRootMotion = false;
        }

        /// <summary>약점 등 다른 콜라이더는 끄고 몸통 콜라이더만 켜 둔다 (시체가 바닥에 닿을 몸)</summary>
        private void KeepOnlyBodyCollider()
        {
            foreach (Collider part in GetComponentsInChildren<Collider>()) part.enabled = false;
            bodyCollider.enabled = true;
        }

        /// <summary>중력을 받는 Rigidbody 를 붙인다. 질량·감쇠는 Inspector 값</summary>
        private void AddCorpseBody()
        {
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.angularDamping = angularDamping;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// 물리를 멈추고 가라앉기를 시작한다. 지금 회전은 그대로 두고, 몸통 높이 + 여유만큼 아래를 목표 y 로 잡는다.
        /// Tick 이 누워 있기가 끝난 순간 1번 부른다.
        /// </summary>
        private void StartSinking()
        {
            float bodyHeight = bodyCollider.bounds.size.y; // 콜라이더를 끄기 전에 잰다 (꺼진 콜라이더의 bounds 는 0)
            body.isKinematic = true;
            bodyCollider.enabled = false;
            sinkStartY = transform.position.y;
            sinkEndY = sinkStartY - bodyHeight - extraSinkDepth;
            EnterStage(CorpseStage.Sinking);
        }

        /// <summary>가라앉기 경과 비율만큼 월드 y 를 시작 → 목표로 보간한다. x·z·회전은 그대로</summary>
        /// <param name="elapsed">가라앉기 시작 후 지난 시간 (s)</param>
        private void MoveDownWhileSinking(float elapsed)
        {
            float progress = Mathf.Clamp01(elapsed / sinkSeconds);
            Vector3 position = transform.position;
            position.y = Mathf.Lerp(sinkStartY, sinkEndY, progress);
            transform.position = position;
        }

        /// <summary>단계를 바꾸고 그 단계의 시작 시각을 기록한다</summary>
        /// <param name="next">들어갈 단계</param>
        private void EnterStage(CorpseStage next)
        {
            stage = next;
            stageStartTime = Time.time;
        }
    }
}
