using System;
using System.Collections.Generic;
using UnityEngine;

namespace FTL
{
    [Serializable]
    public class GameContext
    {
        [field: SerializeField] public List<int> FacilityUpgradeCount { get; private set; } = new();
        [field: SerializeField] public int ScrapAmount { get; private set; }
        [field: SerializeField] public int ShipMaxHp { get; private set; }
        [field: SerializeField] public int ShipHp { get; private set; }
        [field: SerializeField] public int ShipReactorCapacity { get; private set; }
        [field: SerializeField] public int ShipPower { get; private set; }
        [field: SerializeField] public int ReactorUpgradeCount { get; private set; }
        [field: SerializeField] public List<WeaponType> WeaponTypes { get; private set; } = new();
        [field: SerializeField] public EnemyType EnemyType { get; private set; } = EnemyType.None;
        public bool IsEmpty => Rooms == null || FacilityStates == null || CrewStates == null || WeaponTypes == null;

        public List<Room> Rooms { get; private set; }
        public List<FacilityState> FacilityStates { get; private set; }
        public List<CrewState> CrewStates { get; private set; }

        #region Private Method

        private void InitFacilityUpgradeCount()
        {
            FacilityUpgradeCount.Clear();
            for (int i = 0; i < 3; i++)
            {
                FacilityUpgradeCount.Add(0);
            }
        }

        #endregion

        #region Public Method

        public GameContext(List<Room> rooms, List<FacilityState> facilities, List<CrewState> crews, int scrapAmount,
            int shipHp, int shipMaxHp, int shipReactorCapacity, int shipPower)
        {
            Rooms = new List<Room>();
            FacilityStates = new List<FacilityState>();
            CrewStates = new List<CrewState>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Rooms.Add(new Room(rooms[i]));
            }

            for (int i = 0; i < facilities.Count; i++)
            {
                FacilityStates.Add(new FacilityState(facilities[i]));
            }

            for (int i = 0; i < crews.Count; i++)
            {
                CrewStates.Add(new CrewState(crews[i]));
            }

            ScrapAmount = scrapAmount;
            ShipHp = shipHp;
            ShipMaxHp = shipMaxHp;
            ShipReactorCapacity = shipReactorCapacity;
            ShipPower = shipPower;
            InitFacilityUpgradeCount();
        }

        public void SetWeaponTypes(List<WeaponType> weaponTypes)
        {
            WeaponTypes = new List<WeaponType>();
            if (weaponTypes == null)
            {
                return;
            }

            for (int i = 0; i < weaponTypes.Count; i++)
            {
                WeaponTypes.Add(weaponTypes[i]);
            }
        }

        public void SetGameContext(List<int> facilityUpgradeCount, int reactorUpgradeCount)
        {
            for (int i = 0; i < FacilityUpgradeCount.Count; i++)
            {
                FacilityUpgradeCount[i] = facilityUpgradeCount[i];
            }

            ReactorUpgradeCount = reactorUpgradeCount;
        }

        public void IncreaseMaxPowerLimitOfFacility(RoomType roomType, int amount = 1)
        {
            int index = (int)roomType;
            FacilityStates[index].MaxPowerLimit += amount;
        }

        public void IncreaseReactorMaxPower(int amount)
        {
            ShipReactorCapacity += amount;
            ShipPower += amount;
        }

        public int GetCrewHp(int crewIndex)
        {
            return CrewStates[crewIndex].Hp;
        }

        public int GetCrewMaxHp(int crewIndex)
        {
            return CrewStates[crewIndex].MaxHp;
        }

        public int GetCrewCount()
        {
            return CrewStates.Count;
        }

        public string GetCrewName(int crewIndex)
        {
            return CrewStates[crewIndex].Name;
        }

        public int GetFacilityMaxPowerLimit(RoomType roomType)
        {
            int index = (int)roomType;

            return FacilityStates[index].MaxPowerLimit;
        }

        public void AddCrewHp(int crewIndex, int realIncrease)
        {
            CrewStates[crewIndex].Hp += realIncrease;
        }

        public void AddShipHp(int amount)
        {
            ShipHp = Math.Min(ShipMaxHp, ShipHp + amount);
        }

        public void ReduceScrap(int v)
        {
            if (ScrapAmount - v < 0)
            {
                Debug.LogError($"Cannot Reduce scrap. current scrap: {ScrapAmount}, reduce amount: {v}");
                return;
            }

            ScrapAmount -= v;
        }

        public void AddScrap(int v)
        {
            ScrapAmount += v;
        }

        public void SetEnemyType(EnemyType enemyType)
        {
            EnemyType = enemyType;
        }

        #endregion
    }
}
