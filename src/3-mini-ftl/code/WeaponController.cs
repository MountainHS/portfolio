using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FTL
{
    public class WeaponController : MonoBehaviour
    {
        private const float chargeSpeedAssistMultiplier = 1.2f;

        [field: SerializeField] public bool IsAssisted { get; set; }
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float chargeProgress;

        [SerializeField] private float chargeDuration = 7f;
        [SerializeField] private float fireDelay;
        [SerializeField] private bool isReady;
        [SerializeField] private bool prevIsPowered;
        [SerializeField] private int fireCount;
        public ProjectileTargetData TargetData;
        private float chargeSpeedMultiplier = 1f;
        private readonly float reduceSpeed = 4f;
        private ITargeting targetingSystem;
        private WeaponSystem weaponController;

        public bool IsReady => isReady;
        public bool IsPowered { get; set; }
        public float ChargeProgress => chargeProgress;
        public float ChargeDuration => chargeDuration;

        #region Unity Method

        private void Awake()
        {
            isReady = false;
            IsAssisted = false;
            IsPowered = false;
            TargetData.TargetPosition = Vector3.zero;
            TargetData.RoomId = -1;
        }

        private void Update()
        {
            if (!CanRunWeaponLogic())
            {
                return;
            }

            if (IsPowered)
            {
                //if (prevIsPowered == false)
                //{
                //    chargeProgress = 0f;
                //}

                chargeProgress = Mathf.Min(chargeProgress + (Time.deltaTime * GetChargeSpeed()), chargeDuration);
                // chargeSprites에 있는 스프라이트 수 - 1만큼 간격 검증
            }
            else
            {
                //isReady = false;
                chargeProgress = Mathf.Max(chargeProgress - (Time.deltaTime * reduceSpeed), 0);
            }

            UpdateIsReady();

            // roomTarget이 유효하면 발사
            if (IsReadyToFire())
            {
                // 플레이어 발사 로직
                float seed = Random.Range(0f, 4f);
                seed = seed == 4f ? 0f : seed;
                if (targetingSystem == null)
                {
                    StartCoroutine(FireCoroutine(TargetData, seed));
                }

                TargetData.Reset();
                chargeProgress = 0f;
                weaponController.ResetWeaponTargetSprite(this);
            }
            else if (IsReady && targetingSystem != null)
            {
                // 자체 조준 로직을 가지고 있는 적 발사 로직
                float seed = Random.Range(0f, 4f);
                seed = seed == 4f ? 0f : seed;
                StartCoroutine(FireCoroutine(seed));
                chargeProgress = 0f;
            }

            //prevIsPowered = IsPowered;
        }

        #endregion

        #region Public Method

        public void SetRoomTarget(ProjectileTargetData targetData)
        {
            TargetData.TargetPosition = targetData.TargetPosition;
            TargetData.RoomId = targetData.RoomId;
        }

        public void SetTargetingSystem(ITargeting targetingSystem)
        {
            this.targetingSystem = targetingSystem;
        }

        public void SetWeaponSystem(WeaponSystem weaponController)
        {
            this.weaponController = weaponController;
        }

        public void ApplyAssistEffectOnWeapon(bool isApplied)
        {
            if (IsAssisted != isApplied)
            {
                IsAssisted = isApplied;
                chargeProgress = Mathf.Min(chargeProgress, chargeDuration);
            }
        }

        internal void SetChargeSpeedMultiplier(float multiplier)
        {
            chargeSpeedMultiplier = multiplier;
        }

        #endregion

        #region Private Method

        private IEnumerator FireCoroutine(ProjectileTargetData targetData, float seed)
        {
            for (int i = 0; i < fireCount; i++)
            {
                if (!CanRunWeaponLogic())
                {
                    yield break;
                }

                // 투사체에 타깃 정보 전달
                GameObject projectile = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
                ProjectileController projectileController = projectile.GetComponent<ProjectileController>();
                projectileController.SetTarget(targetData);
                projectileController.SetRandomSeed(seed);
                projectile.transform.rotation = transform.rotation;
                yield return new WaitForSeconds(fireDelay);
            }
        }

        private IEnumerator FireCoroutine(float seed)
        {
            for (int i = 0; i < fireCount; i++)
            {
                if (!CanRunWeaponLogic())
                {
                    yield break;
                }

                // 투사체에 타깃 정보 전달
                GameObject projectile = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
                ProjectileController projectileController = projectile.GetComponent<ProjectileController>();
                ProjectileTargetData targetData = new();
                Room targetRoom = targetingSystem.GetTarget();
                targetData.RoomId = targetRoom.Id;
                targetData.TargetPosition = targetRoom.GlobalPosition;
                projectileController.SetTarget(targetData);
                projectileController.SetRandomSeed(seed);
                projectile.transform.rotation = transform.rotation;
                yield return new WaitForSeconds(fireDelay);
            }
        }

        private float GetChargeSpeed()
        {
            return chargeSpeedMultiplier * (IsAssisted ? chargeSpeedAssistMultiplier : 1f);
        }

        private bool IsReadyToFire()
        {
            return TargetData.IsValid() && isReady;
        }

        private bool CanRunWeaponLogic()
        {
            return weaponController != null && weaponController.CanRunFacilityLogic();
        }

        private void UpdateIsReady()
        {
            if (chargeProgress == chargeDuration)
            {
                isReady = true;
            }
            else
            {
                isReady = false;
            }
        }

        #endregion
    }
}
