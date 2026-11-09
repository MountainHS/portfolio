using System.Collections;
using UnityEngine;
using Utilities;

namespace FTL
{
    public class ProjectileController : MonoBehaviour
    {
        [field: SerializeField] public int Damage { get; private set; }
        [field: SerializeField] public int ShieldDamage { get; private set; }
        [field: SerializeField] public int CrewDamage { get; private set; }
        [SerializeField] private float speed;
        [field: SerializeField] public Collider2D StartCol { get; private set; }
        [field: SerializeField] public Vector3 TargetPos { get; private set; } = Vector3.zero;
        [field: SerializeField] public float RandomSeed { get; private set; }
        [field: SerializeField] public bool IsAfterStartCol { get; private set; }

        [SerializeField] private Collider2D endCol;
        [SerializeField] private LayerMask shipLayer;
        [SerializeField] private LayerMask shieldLayer;
        [SerializeField] private int targetRoomId;
        [SerializeField] private bool isMoving = true;

        private SpriteRenderer spriteRenderer;
        public bool IsArrived => transform.position == TargetPos;
        public int TargetRoomId => targetRoomId;

        #region Private Method

        private IEnumerator SetProjectileDelay()
        {
            const float MAX_SPEED = 11f;
            const float MIN_SPEED = 3f;
            const float MIN_TIME = 0.5f;
            const float MAX_TIME = 2f;
            // 진행도

            // 속도 11 ~ 3
            float lerpRate = Mathf.InverseLerp(MAX_SPEED, MIN_SPEED, speed);
            // 목표 시간: 0.5 ~ 2
            float delayTime = Mathf.Lerp(MIN_TIME, MAX_TIME, lerpRate);
            spriteRenderer.enabled = false;

            yield return new WaitForSeconds(delayTime);
            spriteRenderer.enabled = true;
            isMoving = true;
        }

        #endregion

        #region Unity Method

        private void Awake()
        {
            StartCol = null;
            endCol = null;
            targetRoomId = -1;
            IsAfterStartCol = false;
            if (!TryGetComponent(out spriteRenderer))
            {
                if (!transform.GetChild(0).TryGetComponent(out spriteRenderer))
                {
                    Debug.LogError(
                        "ProjectileController: SpriteRenderer component is missing. must be in Prefab root or child.");
                }
            }
        }

        private void Update()
        {
            if (!GameManager.Instance.CanRunCombat)
            {
                return;
            }

            // 목적지에 도착했으면 삭제
            if (IsArrived)
            {
                // 전투 시스템에 판정 요청
                // 회피이면 무시
                // 명중이면 삭제
                CombatSystem.Instance.HandleRoomCollision(this);
                GameObject explosionObject = CombatSystem.Instance.GetExplosionObject();
                explosionObject.transform.position = transform.position;
                Destroy(gameObject);
            }

            if (isMoving)
            {
                if (!IsAfterStartCol)
                {
                    Vector3 moveAmount = Vector3.right * speed * Time.deltaTime;
                    transform.Translate(moveAmount);
                }
                else
                {
                    float moveDistance = speed * Time.deltaTime;
                    transform.position = Vector3.MoveTowards(transform.position, TargetPos, moveDistance);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D collision)
        {
            if (!GameManager.Instance.CanRunCombat)
            {
                return;
            }

            // 충돌한 콜라이더가 함선 레이어 콜라이더이면
            bool hasNoStartCol = StartCol == null;
            if (hasNoStartCol)
            {
                bool isShipLayer = shipLayer.Contains(collision.gameObject.layer);
                if (isShipLayer)
                {
                    StartCol = collision;
                }
            }

            // 충돌한 콜라이더가 실드 레이어 && 태그가 enemyTag라면
            bool isShieldLayer = shieldLayer.Contains(collision.gameObject.layer);
            bool canHitShield = IsAfterStartCol && isShieldLayer;
            if (canHitShield)
            {
                // 전투 시스템 호출
                bool isShielded = CombatSystem.Instance.HandleShieldCollision(this);
                if (isShielded)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void OnTriggerExit2D(Collider2D collision)
        {
            if (!GameManager.Instance.CanRunCombat)
            {
                return;
            }

            // 특정 콜라이더이면 그에 대응되는 EShipArea가 뽑히도록 하면 좋을 거 같은데
            bool isLeavingStartCol = StartCol == collision;
            if (isLeavingStartCol)
            {
                // 전투 시스템 호출
                CombatSystem.Instance.MoveToEnemyArea(StartCol, this);
                IsAfterStartCol = true;
                isMoving = false;
                StartCoroutine(SetProjectileDelay());
            }

            // 종료 콜라이더 벗어나면 삭제
            bool isLeavingEndCol = endCol == collision;
            if (isLeavingEndCol)
            {
                // 전투 시스템 호출
                Destroy(gameObject);
            }
        }

        #endregion

        #region Public Method

        // SetTarget

        public void SetTarget(ProjectileTargetData targetData)
        {
            TargetPos = targetData.TargetPosition;
            targetRoomId = targetData.RoomId;
        }

        public void SetStartCol(Collider2D startCol)
        {
            StartCol = startCol;
        }

        public void SetEndCol(Collider2D endCol)
        {
            this.endCol = endCol;
        }

        public void SetIsAfterStartCol(bool isAfterStartCol)
        {
            IsAfterStartCol = isAfterStartCol;
        }

        public void SetIsMoving(bool isMoving)
        {
            this.isMoving = isMoving;
        }

        public void SetRandomSeed(float seed)
        {
            RandomSeed = seed;
        }

        #endregion
    }
}
