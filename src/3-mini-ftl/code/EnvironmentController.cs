using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FTL
{
    public class EnvironmentController : MonoBehaviour
    {
        [Header("Timing")] [SerializeField] private float effectInterval = 10f;

        [Header("Room Damage")] [SerializeField]
        private int minRoomHitCount = 1;

        [SerializeField] private int maxRoomHitCount = 2;
        [SerializeField] private int roomDamage = 1;
        [SerializeField] private int crewDamage = 10;
        [SerializeField] private bool applyToPlayer = true;
        [SerializeField] private bool applyToEnemy = true;

        [Header("Asteroid Object")] [SerializeField]
        private GameObject asteroidAttackPrefab;

        [SerializeField] private SolarFlareController solarFlareController;
        [SerializeField] private Transform asteroidGroupTransform;
        [SerializeField] private float asteroidLifetime = 1.5f;
        [SerializeField] private bool isAsteroidStage;
        [SerializeField] private bool isSunStage;

        private Coroutine environmentCoroutine;
        private bool hasLoggedMissingAsteroidPrefab;

        #region Unity Method

        private void OnDisable()
        {
            StopEnvironmentEffects();
        }

        #endregion

        #region Public Method

        internal void SetStageEnvironment(bool isAsteroid, bool isSun)
        {
            isAsteroidStage = isAsteroid;
            isSunStage = isSun;
        }

        public void InitStageEnvironment()
        {
            hasLoggedMissingAsteroidPrefab = false;
            StartEnvironmentEffects();
        }

        #endregion

        #region Private Method

        private void StartEnvironmentEffects()
        {
            StopEnvironmentEffects();

            if (effectInterval <= 0f)
            {
                Debug.LogError("StageEnvironmentController: effectInterval must be greater than 0.");
                return;
            }

            environmentCoroutine = StartCoroutine(EnvironmentCoroutine());
        }

        private void StopEnvironmentEffects()
        {
            if (environmentCoroutine == null)
            {
                return;
            }

            StopCoroutine(environmentCoroutine);
            environmentCoroutine = null;
        }

        private IEnumerator EnvironmentCoroutine()
        {
            while (true)
            {
                if (!GameManager.Instance.CanRunCombat)
                {
                    StopEnvironmentEffects();
                    break;
                }

                if (isSunStage)
                {
                    float solarFlareDuration = solarFlareController.FlareDuration;
                    yield return new WaitForSeconds(effectInterval - solarFlareDuration);
                    solarFlareController.ApplyFlareEffect();
                    yield return new WaitForSeconds(solarFlareDuration);
                    ApplyEnvironmentEffect();
                }
                else
                {
                    yield return new WaitForSeconds(effectInterval);
                    ApplyEnvironmentEffect();
                }
            }
        }

        private void ApplyEnvironmentEffect()
        {
            CombatSystem combatSystem = CombatSystem.Instance;
            if (applyToPlayer)
            {
                ApplyEnvironmentEffectToShip(combatSystem.GetShip(ShipSide.Player));
            }

            if (applyToEnemy)
            {
                ApplyEnvironmentEffectToShip(combatSystem.GetShip(ShipSide.Enemy0));
            }
        }

        private void ApplyEnvironmentEffectToShip(ShipController ship)
        {
            if (ship == null || ship.IsDead)
            {
                return;
            }

            List<Room> rooms = ship.Rooms;
            if (rooms == null || rooms.Count == 0)
            {
                Debug.LogError($"{ship.name}: Cannot apply environment damage. Rooms are empty.");
                return;
            }

            ShipSide startShipSide = ship.Ownership.ShipSide;
            ShipSide endShipSide = CombatSystem.Instance.GetOpponentShipSide(startShipSide);
            Collider2D startCol = CombatSystem.Instance.GetColliderByShipSide(endShipSide);
            Collider2D endCol = CombatSystem.Instance.GetColliderByShipSide(startShipSide);
            int minHitCount = Mathf.Max(1, minRoomHitCount);
            int maxHitCount = Mathf.Max(minHitCount, maxRoomHitCount);
            int roomHitCount = Random.Range(minHitCount, maxHitCount + 1);
            roomHitCount = Mathf.Clamp(roomHitCount, 1, rooms.Count);

            for (int i = 0; i < roomHitCount; i++)
            {
                int roomIndex = Random.Range(0, rooms.Count);

                Room room = rooms[roomIndex];
                if (isAsteroidStage)
                {
                    SpawnAsteroidAttack(room, startCol, endCol);
                }
                else if (isSunStage)
                {
                    ship.TakeDamageToRoom(roomDamage, crewDamage, room.Id);
                    CombatSystem.Instance.SetFloatingText(room, EDamageType.Room, roomDamage);
                }
            }
        }

        private int GetRandomRoomIndex(int roomCount, List<int> selectedIndexes)
        {
            for (int i = 0; i < 20; i++)
            {
                int randomIndex = Random.Range(0, roomCount);
                if (!selectedIndexes.Contains(randomIndex))
                {
                    return randomIndex;
                }
            }

            for (int i = 0; i < roomCount; i++)
            {
                if (!selectedIndexes.Contains(i))
                {
                    return i;
                }
            }

            return 0;
        }

        private void SpawnAsteroidAttack(Room room, Collider2D startCol, Collider2D endCol)
        {
            if (asteroidAttackPrefab == null)
            {
                if (!hasLoggedMissingAsteroidPrefab)
                {
                    Debug.LogError("StageEnvironmentController: asteroidAttackPrefab is not assigned.");
                    hasLoggedMissingAsteroidPrefab = true;
                }

                return;
            }

            GameObject asteroidObject = Instantiate(asteroidAttackPrefab, room.GlobalPosition, Quaternion.identity,
                asteroidGroupTransform);
            ProjectileController projectile = asteroidObject.GetComponent<ProjectileController>();
            ProjectileTargetData targetData;
            float seed = Random.Range(0f, 4f);

            targetData.TargetPosition = room.GlobalPosition;
            targetData.RoomId = room.Id;
            projectile.SetIsAfterStartCol(true);
            projectile.SetTarget(targetData);
            projectile.SetStartCol(startCol);
            projectile.SetEndCol(endCol);
            projectile.SetIsMoving(true);

            seed = seed == 4f ? 0f : seed;
            projectile.SetRandomSeed(seed);
            CombatSystem.Instance.MoveToEnemyArea(startCol, projectile);
            //if (asteroidLifetime > 0f)
            //{
            //    Destroy(asteroidObject, asteroidLifetime);
            //}
        }

        #endregion
    }
}
