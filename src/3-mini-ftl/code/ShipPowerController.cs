using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace FTL
{
    public class ShipPowerController : MonoBehaviour
    {
        [FormerlySerializedAs("reactorSystem")] [SerializeField] protected ReactorController reactorController;
        [SerializeField] protected List<FacilityController> facilities = new();
        protected readonly Dictionary<RoomType, int> assignedPowers = new();
        public int GetReactorMaxPower => reactorController.MaxPower;
        public int GetPower => reactorController.Power;

        #region Unity Method

        protected virtual void Start()
        {
            facilities.Add(GetComponent<ShieldController>());
            facilities.Add(GetComponent<WeaponSystem>());
            facilities.Add(GetComponent<EngineController>());
        }

        #endregion

        #region Public Method

        public void AllocatePower(RoomType facilityType, int amount = 1)
        {
            if (amount <= 0 || reactorController == null)
            {
                return;
            }

            FacilityController facility = FindFacility(facilityType);
            if (facility == null)
            {
                return;
            }

            int currentPower = GetAssignedPower(facilityType);
            int nextPower = currentPower + amount;
            if (CanSpendReactorPower(amount) == false || facility.CanAddPower(amount) == false ||
                CanSetFacilityPower(facility, nextPower) == false)
            {
                return;
            }

            SetAssignedPower(facilityType, nextPower);
            facility.AddPower(amount);
            reactorController.ReducePower(amount);
        }

        public void DeallocatePower(RoomType facilityType, int amount = 1)
        {
            if (amount <= 0 || reactorController == null)
            {
                return;
            }

            FacilityController facility = FindFacility(facilityType);
            if (facility == null)
            {
                return;
            }

            int currentFacilityPower = GetAssignedPower(facilityType);
            if (currentFacilityPower <= 0)
            {
                return;
            }

            int recoverAmount = Mathf.Min(amount, currentFacilityPower);
            if (!CanRecoverReactorPower(recoverAmount))
            {
                return;
            }

            int nextPower = currentFacilityPower - recoverAmount;
            SetAssignedPower(facilityType, nextPower);

            reactorController.AddPower(recoverAmount);
            facility.ReducePower(recoverAmount);
        }

        public int GetAssignedPower(RoomType facilityType)
        {
            if (assignedPowers.TryGetValue(facilityType, out int power))
            {
                return power;
            }

            return 0;
        }

        public void AddReactorPower(int amount)
        {
            reactorController.AddMaxPower(amount);
        }

        public void SetReactorMaxPower(int amount)
        {
            reactorController.SetMaxPower(amount);
        }

        public void SetReactorPower(int amount)
        {
            reactorController.SetPower(amount);
        }

        protected FacilityController FindFacility(RoomType facilityType)
        {
            for (int i = 0; i < facilities.Count; i++)
            {
                if (facilities[i].Type == facilityType)
                {
                    return facilities[i];
                }
            }

            return null;
        }

        protected void SetAssignedPower(RoomType facilityType, int power)
        {
            assignedPowers[facilityType] = power;
        }

        protected bool CanSpendReactorPower(int amount)
        {
            return reactorController.Power >= amount;
        }

        protected bool CanRecoverReactorPower(int amount)
        {
            return reactorController.Power + amount <= reactorController.MaxPower;
        }

        protected bool CanSetFacilityPower(FacilityController facility, int power)
        {
            return power <= facility.MaxPower;
        }

        public void ApplyState(FacilityState facilityState)
        {
            RoomType type = facilityState.Type;
            int power = facilityState.Power;
            SetAssignedPower(type, power);
        }

        #endregion
    }
}
