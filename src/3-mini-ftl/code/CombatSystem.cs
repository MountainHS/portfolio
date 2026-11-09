using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Utilities;

namespace FTL
{
    public enum EDamageType
    {
        Shield, Room, Crew
    }

    public class CombatSystem : SingletonBase<CombatSystem>
    {
        [SerializeField] private GameObject floatingTextPrefab;
        [SerializeField] private GameObject explosionPrefab;
        private CameraShakeController cameraShakeController;

        private readonly Dictionary<Collider2D, ShipSide> colToNextShipAreaDict = new();
        private ObjectPool<GameObject> explosionPool;
        private Transform floatingTextGroupTransform;

        private ObjectPool<GameObject> floatingTextPool;
        private Collider2D[] shipAreas;

        private ShipController[] ships;
        private readonly Dictionary<ShipSide, Collider2D> shipSideToColDict = new();
        private readonly Dictionary<ShipSide, SpriteShakeController> shipSideToSpriteShake = new();
        private SpriteShakeController[] spriteShakeControllers;

        #region Unity Method

        protected override void OnInitialize()
        {
            base.OnInitialize();
            floatingTextPool = new ObjectPool<GameObject>(CreateBar, OnGetBar, OnReleaseBar, OnDestroyBar);
            explosionPool = new ObjectPool<GameObject>(CreateExplostion, OnGetExplosion, OnReleaseExplosion,
                OnDestroyExplosion);
            GameObject floatingTextGroup = new("Floating Text Group");
            floatingTextGroupTransform = floatingTextGroup.transform;
            floatingTextGroupTransform.SetParent(gameObject.transform);
        }

        #endregion

        #region Public Method

        public void InitCombatSystem(PlayerController player, EnemyController enemy)
        {
            if (!NeedSceneBinding())
            {
                return;
            }

            cameraShakeController = FindFirstObjectByType<CameraShakeController>();
            ShipOwnership playerOwnership = player.Ownership;
            ShipOwnership enemyOwnership = enemy.Ownership;
            Collider2D playerCollider = playerOwnership.GetComponent<Collider2D>();
            Collider2D enemyCollider = enemyOwnership.GetComponent<Collider2D>();
            SpriteShakeController playerShaker = player.GetComponent<SpriteShakeController>();
            SpriteShakeController enemyShaker = enemy.GetComponent<SpriteShakeController>();

            ships = new ShipController[] { player, enemy };
            shipAreas = new[] { playerCollider, enemyCollider };
            spriteShakeControllers = new[] { playerShaker, enemyShaker };
            shipSideToColDict.Clear();
            colToNextShipAreaDict.Clear();
            shipSideToSpriteShake.Clear();
            shipSideToColDict.Add(ShipSide.Player, shipAreas[0]);
            shipSideToColDict.Add(ShipSide.Enemy0, shipAreas[1]);
            colToNextShipAreaDict.Add(shipAreas[0], ShipSide.Enemy0);
            colToNextShipAreaDict.Add(shipAreas[1], ShipSide.Player);
            shipSideToSpriteShake.Add(ShipSide.Player, playerShaker);
            shipSideToSpriteShake.Add(ShipSide.Enemy0, enemyShaker);
        }

        public bool NeedSceneBinding()
        {
            if (ships == null)
            {
                return true;
            }

            if (shipAreas == null)
            {
                return true;
            }

            for (int i = 0; i < ships.Length; i++)
            {
                if (ships[i] == null)
                {
                    return true;
                }
            }

            for (int i = 0; i < shipAreas.Length; i++)
            {
                if (shipAreas[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     - 투사체 피격 판정
        ///     - 멤버
        ///     - 피격 판정
        ///     - 도달한 Room 정보
        ///     - 피격된 Room에 피격 시행 (TakeDamage)
        ///     - Room의 TakeDamage 호출
        ///     - Room의 시설
        ///     - Room의 함선에 체력 피해 전달
        /// </summary>
        /// <param name="col"></param>
        /// <param name="projectile"></param>
        public void MoveToEnemyArea(Collider2D col, ProjectileController projectile)
        {
            ShipSide nextShipArea = colToNextShipAreaDict[col];
            Collider2D nextShipCol = shipSideToColDict[nextShipArea];
            float seed = projectile.RandomSeed;

            Vector3 spawnPos = GetRandomPositionOnColliderEdge(nextShipCol, projectile.transform.position.z, seed);
            Vector3 targetPos = projectile.TargetPos;

            // 스폰장소 - 목표 콜라이더 중심 바라보게 회전시키기 right 이용
            projectile.SetEndCol(nextShipCol);
            projectile.transform.position = spawnPos;
            Vector3 direction = targetPos - spawnPos;
            direction.z = projectile.transform.position.z;
            projectile.transform.right = direction;
        }

        public bool HandleShieldCollision(ProjectileController projectile)
        {
            // projectile 주인 확인
            bool isPlayerProjectile = projectile.StartCol.CompareTag(Tags.PlayerShip);
            int opponentShipIndex = isPlayerProjectile ? 1 : 0;
            ShieldController oppenentShieldController =
                (ShieldController)ships[opponentShipIndex].GetFacilitySystemByERoomType(RoomType.Shield);
            // 실드 시스템의 현재 실드가 1 이상이면
            // 실드 시스템 TakeDamage 호출
            // return true;
            // 현재 실드가 0이라면 
            // return false
            if (oppenentShieldController.ShieldCount > 0)
            {
                oppenentShieldController.TakeDamageToShield(projectile.ShieldDamage);
                SetFloatingText(projectile, EDamageType.Shield);
                return true;
            }

            return false;
        }

        public bool HandleRoomCollision(ProjectileController projectile)
        {
            bool isPlayerProjectile = projectile.StartCol.CompareTag(Tags.PlayerShip);
            int opponentShipIndex = isPlayerProjectile ? 1 : 0;
            ShipController oppenentShipController = ships[opponentShipIndex];
            SpriteShakeController oppenentSpriteShaker = spriteShakeControllers[opponentShipIndex];
            ProjectileTargetData targetData = new(projectile.TargetPos, projectile.TargetRoomId);

            // 명중 판정 로직 추가 (추후 구현)
            oppenentShipController.TakeDamageToRoom(projectile, targetData.RoomId);
            oppenentSpriteShaker.ShakeSprite();
            SetFloatingText(projectile, EDamageType.Room);
            //if (isPlayerProjectile == false)
            //    cameraShakeController.ShakeCamera();
            return true;
        }

        public void SetShakeController(CameraShakeController shakeController)
        {
            cameraShakeController = shakeController;
        }

        public List<Room> GetPlayerRooms()
        {
            // Implementation for getting player rooms
            return ships[0].Rooms;
        }

        public ShipController GetShip(ShipSide shipSide)
        {
            if (ships == null)
            {
                Debug.LogError("CombatSystem: ships are not initialized.");
                return null;
            }

            int shipIndex = (int)shipSide;
            if (shipIndex < 0 || shipIndex >= ships.Length)
            {
                Debug.LogError($"CombatSystem: Invalid ship side {shipSide}.");
                return null;
            }

            return ships[shipIndex];
        }

        public Room GetRoomOrNull(ShipSide shipSide, Vector2 position)
        {
            List<Room> rooms = ships[(int)shipSide].Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                bool isMatchingPosition = rooms[i].GlobalPosition == position;
                if (isMatchingPosition)
                {
                    return rooms[i];
                }
            }

            return null;
        }

        public GameObject GetExplosionObject()
        {
            GameObject explosionObject = explosionPool.Get();
            return explosionObject;
        }

        public ShipSide GetOpponentShipSide(ShipSide startSide)
        {
            if (startSide == ShipSide.Player)
            {
                return ShipSide.Enemy0;
            }

            return ShipSide.Player;
        }

        public Collider2D GetColliderByShipSide(ShipSide shipSide)
        {
            if (shipSideToColDict.TryGetValue(shipSide, out Collider2D col))
            {
                return col;
            }

            Debug.LogError($"CombatSystem: No collider found for ship side {shipSide}.");
            return null;
        }

        public void SetFloatingText(ProjectileController projectile, EDamageType type)
        {
            GameObject floatingTextObject = floatingTextPool.Get();
            FloatingTextController floatingText = floatingTextObject.GetComponent<FloatingTextController>();
            Color color = type.GetColorFromDamageType();

            floatingText.transform.SetParent(floatingTextGroupTransform);
            floatingTextObject.transform.position = projectile.transform.position;
            floatingText.SetText($"{type.GetDamageFromDamageType(projectile)}");
            floatingText.SetTextColor(color);
        }

        public void SetFloatingText(Room room, EDamageType type, int damage)
        {
            GameObject floatingTextObject = floatingTextPool.Get();
            FloatingTextController floatingText = floatingTextObject.GetComponent<FloatingTextController>();
            Color color = type.GetColorFromDamageType();

            floatingText.transform.SetParent(floatingTextGroupTransform);
            floatingTextObject.transform.position = room.GlobalPosition;
            floatingText.SetText($"{damage}");
            floatingText.SetTextColor(color);
        }

        #endregion

        #region Private Method

        private GameObject CreateBar()
        {
            return Instantiate(floatingTextPrefab, floatingTextGroupTransform.transform);
        }

        private void OnGetBar(GameObject text)
        {
            text.transform.SetParent(floatingTextGroupTransform);
            text.SetActive(true);
        }

        private void OnReleaseBar(GameObject text)
        {
            text.SetActive(false);
        }

        private void OnDestroyBar(GameObject text)
        {
            Destroy(text);
        }

        private GameObject CreateExplostion()
        {
            //Debug.Assert(explosionPrefabs != null, "Explosion prefabs are not assigned.");

            return Instantiate(explosionPrefab, transform);
        }

        private void OnGetExplosion(GameObject explosion)
        {
            explosion.transform.SetParent(transform);
            explosion.SetActive(true);
        }

        private void OnReleaseExplosion(GameObject explosion)
        {
            explosion.SetActive(false);
        }

        private void OnDestroyExplosion(GameObject explosion)
        {
            Destroy(explosion);
        }

        private Vector3 GetRandomPositionOnColliderEdge(Collider2D col, float z, float seed)
        {
            Bounds bounds = col.bounds;
            Vector3[] colFaceStarts = new Vector3[4];
            Vector3[] colFaceEnds = new Vector3[4];
            colFaceStarts[0] = new Vector3(bounds.min.x, bounds.min.y); // bottom-left
            colFaceStarts[1] = new Vector3(bounds.max.x, bounds.min.y); // bottom-right
            colFaceStarts[2] = new Vector3(bounds.max.x, bounds.max.y); // top-right
            colFaceStarts[3] = new Vector3(bounds.min.x, bounds.max.y); // top-left
            colFaceEnds[0] = new Vector3(bounds.max.x, bounds.min.y); // bottom-right
            colFaceEnds[1] = new Vector3(bounds.max.x, bounds.max.y); // top-right
            colFaceEnds[2] = new Vector3(bounds.min.x, bounds.max.y); // top-left
            colFaceEnds[3] = new Vector3(bounds.min.x, bounds.min.y); // bottom-left

            // 선택한 면에서 선택할 위치 (보간, 비율) 구하기
            // 실수의 정수 부분, 소수점 값을 어떻게 구분하지? (Mathf 클래스에 api 있을 거 같은데)
            int randIndex = Mathf.FloorToInt(seed);
            float lerpRate = seed - randIndex;
            //int randIndex = Random.Range(0, 4);
            //float lerpRate = Random.Range(0.0f, 1.0f);
            Vector3 spawnPos = Vector3.Lerp(colFaceStarts[randIndex], colFaceEnds[randIndex], lerpRate);
            spawnPos.z = z;

            return spawnPos;
        }

        #endregion
    }
}
