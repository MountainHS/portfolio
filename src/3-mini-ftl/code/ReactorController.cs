using UnityEngine;

namespace FTL
{
    public class ReactorController : MonoBehaviour
    {
        [SerializeField] private int maxPower = 3;
        [SerializeField] private int power;
        public int MaxPower => maxPower;
        public int Power => power;
        public int TotalDamage => 0;

        #region Unity Method

        private void Awake()
        {
            power = maxPower;
        }

        #endregion

        #region Public Method

        public void AddPower(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            power = Mathf.Min(power + amount, maxPower);
        }

        public void AddMaxPower(int amount)
        {
            maxPower += amount;
            power += amount;
        }

        public void ReducePower(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            power = Mathf.Max(power - amount, 0);
        }

        public void SetMaxPower(int amount)
        {
            maxPower = amount;
        }

        public void SetPower(int amount)
        {
            power = amount;
        }

        #endregion
    }
}
