using System.Collections.Generic;
using UnityEngine;

namespace FTL
{
    public class CrewSystem : MonoBehaviour
    {
        [field: SerializeField] private ShipController ship;
        [SerializeField] private List<CrewController> crews = new();
        [SerializeField] private List<CrewController> selectedCrews = new();
        [SerializeField] private bool isPlayer = true;
        [SerializeField] private Room targetRoom;
        private readonly List<FacilityController> facilitySystems = new();

        public List<Room> Rooms { get; set; }
        public bool IsAllCrewDead => crews.Count == 0;

        #region Private Method

        //private void Update()
        //{
        //    // 조건에 맞으면 승무원 이동 로직 수행
        //    if (selectedCrews.Count > 0 &&
        //        targetRoom != null)
        //    {
        //    }
        //}

        private void SetTargetToCrew()
        {
            for (int i = 0; i < selectedCrews.Count; i++)
            {
                bool isTargetRoomFull = targetRoom.IsFull();
                if (isTargetRoomFull)
                {
                    Debug.LogWarning($"Room {targetRoom.Id} is full. Cannot move crew.");
                    break;
                }

                CrewController crew = selectedCrews[i];
                crew.SetTargetRoom(targetRoom);
            }

            //selectedCrews.Clear();
            targetRoom = null;
        }

        #endregion

        #region Unity Method

        private void Awake()
        {
            ship = GetComponent<ShipController>();
        }

        private void Start()
        {
            CrewController[] crews = GetComponentsInChildren<CrewController>();
            for (int i = 0; i < crews.Length; i++)
            {
                CrewController crew = crews[i];
                this.crews.Add(crew);
                crew.CrewSystem = this;
            }

            targetRoom = null;

            facilitySystems.Add(GetComponent<WeaponSystem>());
            facilitySystems.Add(GetComponent<EngineController>());
            facilitySystems.Add(GetComponent<ShieldController>());
        }

        #endregion

        #region Public Method

        public void AddSelectedCrew(CrewController crew)
        {
            if (!CanRunCrewLogic())
            {
                return;
            }

            crew.IsSelected = true;
            selectedCrews.Add(crew);
        }

        public void ResetSelectedCrews()
        {
            int length = selectedCrews.Count;
            for (int i = 0; i < length; i++)
            {
                CrewController crew = selectedCrews[i];
                crew.IsSelected = false;
            }

            selectedCrews.Clear();
        }

        public void SetTargetRoom(Room room)
        {
            if (!CanRunCrewLogic())
            {
                return;
            }

            bool hasSelectedCrew = selectedCrews.Count > 0;
            if (!hasSelectedCrew)
            {
                Debug.LogWarning("Must select crew before choosing target room");
                return;
            }

            targetRoom = room;
            SetTargetToCrew();
        }

        public void HandleCrewDeath(CrewController crew)
        {
            ship.Crews.Remove(crew);
            crews.Remove(crew);
            Room room = null;
            for (int i = 0; i < Rooms.Count; i++)
            {
                if (Rooms[i].ContainsCrew(crew))
                {
                    room = Rooms[i];
                }
            }

            room.ReduceCrew(crew);

            //if (IsAllCrewDead)
            //{
            //    EBattleResult battleResult = (ship.Ownership.ShipSide == EShipSide.Player) 
            //        ? EBattleResult.PlayerAllCrewDead 
            //        : EBattleResult.EnemyAllCrewDead;
            //    GameManager.Instance.HandleBattleResult(battleResult);
            //}
        }

        public bool CanRunCrewLogic()
        {
            return GameManager.Instance.CanRunCombat &&
                   !ship.IsCombatStopped;
        }

        #endregion
    }
}
