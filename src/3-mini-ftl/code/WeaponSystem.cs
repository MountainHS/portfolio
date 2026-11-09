using System.Collections.Generic;
using UnityEngine;

namespace FTL
{
    public class WeaponSystem : FacilityController
    {
        [SerializeField] private List<WeaponController> weapons;
        [SerializeField] private WeaponController selectedWeapon;
        [SerializeField] private List<GameObject> targetSpriteObjects;
        [SerializeField] private ProjectileTargetData targetData;
        [SerializeField] private float chargeSpeedMultiplier;
        public ITargeting TargetingSystem { get; set; }

        #region Unity Method

        protected override void Awake()
        {
            base.Awake();
            type = RoomType.Weapon;
            selectedWeapon = null;
            targetData = new ProjectileTargetData(Vector3.zero, -1);
        }

        private void Update()
        {
            if (!CanRunFacilityLogic())
            {
                return;
            }

            UpdateWeaponActivation();

            UpdateWeaponTargeting();
        }

        #endregion

        #region Public Method

        public void SetWeaponsTargetingSystem(ITargeting targetingSystem)
        {
            TargetingSystem = targetingSystem;
            for (int i = 0; i < weapons.Count; i++)
            {
                weapons[i].SetTargetingSystem(TargetingSystem);
            }
        }

        public void SetSelectedWeapon(int index)
        {
            if (!CanRunFacilityLogic())
            {
                return;
            }

            // 선택한 무기가 없으면 (무기 리스트의 인덱스 범위 초과) 예외처리
            if (index > weapons.Count)
            {
                Debug.LogWarning("WeaponSystem: SetSelectWeapon index out of range");
                return;
            }

            // 선택한 무기 갱신
            selectedWeapon = weapons[index];
            CursorController.Instance.SetCursorAimSprite(index);
        }

        public void SetSelectedRoomTarget(ProjectileTargetData targetData)
        {
            if (!CanRunFacilityLogic())
            {
                return;
            }

            this.targetData = targetData;
            CursorController.Instance.ResetMouseSprite();
        }

        // 선택한 무기 리셋
        public void ResetSelectedWeapon()
        {
            ProjectileTargetData emptyData = new(Vector2.zero, -1);
            ResetWeaponTargetSprite(selectedWeapon);
            CursorController.Instance.ResetMouseSprite();
            if (selectedWeapon == null)
            {
                Debug.LogWarning("Selected Weapon is null. Reset failed.");
                return;
            }

            selectedWeapon.SetRoomTarget(emptyData);
            selectedWeapon = null;
        }

        public override void ApplyAssistEffectOnFacility(bool isApplied)
        {
            base.ApplyAssistEffectOnFacility(isApplied);
            for (int i = 0; i < weapons.Count; i++)
            {
                weapons[i].ApplyAssistEffectOnWeapon(isApplied);
            }
        }

        public void AddWeapon(WeaponController weapon)
        {
            //weapons 초기화 해야함
            weapons.Add(weapon);
        }

        public void SetWeaponSystemOfWeapons()
        {
            for (int i = 0; i < weapons.Count; i++)
            {
                weapons[i].SetWeaponSystem(this);
            }
        }

        public void ResetWeaponTargetSprite(WeaponController weapon)
        {
            int weaponIndex = FindWeaponIndex(weapon);
            if (weaponIndex == -1)
            {
                Debug.LogWarning("WeaponSystem: ResetWeaponTargetSprite weapon not found");
                return;
            }

            targetSpriteObjects[weaponIndex].SetActive(false);
        }

        #endregion

        #region Private Method

        private void UpdateWeaponTargeting()
        {
            if (TargetingSystem == null)
            {
                if (CanUpdateWeapon())
                {
                    int weaponIndex = FindWeaponIndex(selectedWeapon);

                    if (selectedWeapon.IsPowered)
                    {
                        SetWeaponTargetSprite(weaponIndex, targetData);
                        selectedWeapon.SetRoomTarget(targetData);
                        selectedWeapon = null;
                        targetData = new ProjectileTargetData(Vector3.zero, -1);
                    }
                }
            }
            // 적 무기 시스템은 플레이어 입력을 받지 않고
            // 랜덤하게 공격을 해야함
            // for 문으로 무기중 isPowered이면서 목표 방 정보가 없는 무기는
            // 조준 클래스의 방 정보 제공
            //for (int i = 0; i < weapons.Count; i++)
            //{
            //    WeaponController weapon = weapons[i];
            //    if (weapon.IsPowered &&
            //        weapon.TargetData.IsValid() == false)
            //    {
            //        // 조준 클래스가 조준 정보 리턴
            //        Room targetRoom = TargetingSystem.GetTarget();
            //        targetData.RoomId = targetRoom.Id;
            //        targetData.TargetPosition = targetRoom.GlobalPosition;
            //        weapon.SetRoomTarget(targetData);
            //    }
            //}
        }

        private void UpdateWeaponActivation()
        {
            // 무기 시스템 전력 상태에 따라 무기 활성화 상태 변화
            if (weapons.Count > 0)
            {
                float chargeSpeedMultiplier = GetChargeSpeedMultiplierByPower();
                this.chargeSpeedMultiplier = chargeSpeedMultiplier;
                if (IsPowered)
                {
                    for (int i = 0; i < weapons.Count; i++)
                    {
                        weapons[i].IsPowered = true;
                        weapons[i].SetChargeSpeedMultiplier(chargeSpeedMultiplier);
                    }
                }
                else
                {
                    for (int i = 0; i < weapons.Count; i++)
                    {
                        weapons[i].IsPowered = false;
                        weapons[i].SetChargeSpeedMultiplier(1f);
                    }
                }
            }
        }

        private float GetChargeSpeedMultiplierByPower()
        {
            if (Power <= 0)
            {
                return 0f;
            }

            if (Power == 1)
            {
                return 0.75f;
            }

            if (Power == 2)
            {
                return 0.9f;
            }

            if (Power == 3)
            {
                return 1f;
            }

            if (Power == 4)
            {
                return 1.25f;
            }

            if (Power == 5)
            {
                return 1.4f;
            }

            if (Power == 6)
            {
                return 1.6f;
            }

            return 1.75f;
        }

        private bool CanUpdateWeapon()
        {
            return targetData.IsValid() &&
                   selectedWeapon != null;
        }

        private void SetWeaponTargetSprite(int index, ProjectileTargetData targetData)
        {
            if (index < 0 || index >= targetSpriteObjects.Count)
            {
                Debug.LogWarning("WeaponSystem: SetWeaponTargetSprite index out of range");
                return;
            }

            targetSpriteObjects[index].SetActive(true);
            targetSpriteObjects[index].transform.position = targetData.TargetPosition;
        }

        private int FindWeaponIndex(WeaponController weapon)
        {
            for (int i = 0; i < weapons.Count; i++)
            {
                if (weapons[i] == weapon)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion
    }
}
