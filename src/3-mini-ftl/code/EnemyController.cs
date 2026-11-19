namespace FTL
{
    public class EnemyController : ShipController
    {
        private ITargeting targetingSystem;

        #region Unity Method

        protected override void Start()
        {
            base.Start();
            targetingSystem = new RandomTargeter(CombatSystem.Instance.GetPlayerRooms());
            WeaponController.SetWeaponsTargetingSystem(targetingSystem);
            Ownership.ShipSide = ShipSide.Enemy0;
        }

        #endregion
    }
}
