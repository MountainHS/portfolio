using UnityEngine;

namespace FTL
{
    public class FacilityController : MonoBehaviour
    {
        [field: SerializeField] public bool IsAssisted { get; protected set; }
        [field: SerializeField] public float RepairDuration { get; set; } = 7.5f;
        [field: SerializeField] public float RepairProgress { get; set; }
        [field: SerializeField] public int MaxPowerLimit { get; set; } = 3;
        [SerializeField] protected RoomType type;
        [SerializeField] protected int maxPower;
        public int TotalDamage;
        public int Power;
        protected FacilityStatus facilityStatus;
        protected ShipController ship;
        protected ShipPowerController shipPowerController;
        public RoomType Type => type;
        public FacilityStatus FacilityStatus => facilityStatus;
        public int MaxPower => maxPower;
        public bool IsPowered => Power > 0;

        #region Unity Method

        protected virtual void Awake()
        {
            ship = GetComponent<ShipController>();
            IsAssisted = false;
            Power = 0;
            maxPower = MaxPowerLimit;
            facilityStatus = FacilityStatus.Normal;
            TotalDamage = 0;
            UpdateFacilityStatus();
        }

        protected virtual void Start()
        {
            shipPowerController = ship.GetComponent<ShipPowerController>();
        }

        #endregion

        #region Public Method

        public bool CanAddPower(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            return Power + amount <= maxPower;
        }

        public void AddPower(int amount)
        {
            if (CanAddPower(amount))
            {
                Power += amount;
            }

            UpdateFacilityStatus();
        }

        public void ReducePower(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Power = Mathf.Max(Power - amount, 0);
            UpdateFacilityStatus();
        }

        public virtual void TakeDamage(int amount)
        {
            int newDamage = TotalDamage + amount;
            newDamage = Mathf.Min(newDamage, MaxPowerLimit);
            TotalDamage = newDamage;
            maxPower = MaxPowerLimit - newDamage;
            if (maxPower < Power)
            {
                int overflowAmount = Power - maxPower;
                ReducePower(overflowAmount);
                shipPowerController.DeallocatePower(type,  overflowAmount);
            }

            UpdateFacilityStatus();
        }

        public void ApplyState(FacilityState state)
        {
            if (state == null)
            {
                return;
            }

            MaxPowerLimit = Mathf.Max(0, state.MaxPowerLimit);
            TotalDamage = Mathf.Clamp(state.TotalDamage, 0, MaxPowerLimit);
            maxPower = MaxPowerLimit - TotalDamage;
            Power = Mathf.Clamp(state.Power, 0, maxPower);

            UpdateFacilityStatus();
        }

        public void Repair(int amount)
        {
            TotalDamage = Mathf.Max(TotalDamage - amount, 0);

            maxPower = MaxPowerLimit - TotalDamage;
            UpdateFacilityStatus();
        }

        public void UpdateFacilityStatus()
        {
            if (TotalDamage == MaxPowerLimit)
            {
                facilityStatus = FacilityStatus.Broked;
            }
            else if (TotalDamage > 0 && TotalDamage < MaxPowerLimit)
            {
                facilityStatus = FacilityStatus.Damaged;
            }
            else if (Power == 0)
            {
                facilityStatus = FacilityStatus.Disabled;
            }
            else if (TotalDamage == 0)
            {
                facilityStatus = FacilityStatus.Normal;
            }
        }

        public bool CanRunFacilityLogic()
        {
            return GameManager.Instance.CanRunCombat &&
                   !ship.IsCombatStopped;
        }

        public virtual void ApplyAssistEffectOnFacility(bool isApplied)
        {
            if (isApplied != IsAssisted)
            {
                Debug.Log($"Apply crew assist! in {this}! before: {IsAssisted} after: {isApplied}");
                IsAssisted = isApplied;
            }
        }

        [ContextMenu("Add Power")]
        public void TestAddPower()
        {
            // TODO: 전력 변경 검증 및 변경 후 시설별 효과 갱신 호출을 구체화한다.
            AddPower(1);
        }

        [ContextMenu("Reduce Power")]
        public void TestReducePower()
        {
            ReducePower(1);
        }

        [ContextMenu("Take Damage")]
        public void TestTakeDamage()
        {
            TakeDamage(1);
        }

        [ContextMenu("Repair")]
        public void TestRepair()
        {
            Repair(1);
        }

        #endregion
    }
}
