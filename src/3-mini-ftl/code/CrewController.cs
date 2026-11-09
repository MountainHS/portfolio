using UnityEngine;

namespace FTL
{
    public class CrewController : MonoBehaviour
    {
        [field: SerializeField] public bool IsSelected { get; set; }
        [field: SerializeField] public Room CurrentRoom { get; set; }
        [field: SerializeField] public Room TargetRoom { get; private set; }
        [field: SerializeField] public int MaxHp { get; set; } = 100;
        [field: SerializeField] public int Hp { get; set; }
        [field: SerializeField] public string Name { get; set; } = "Hans";
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private CrewStatus crewStatus = CrewStatus.Idle;
        private Transform crewSpriteTransform;
        public bool IsDead => Hp == 0;
        public CrewSystem CrewSystem { get; set; }
        public static Vector3 Offset { get; } = new(0f, 0.5f, 0f);

        #region Unity Method

        private void Awake()
        {
            Hp = MaxHp;
            crewSpriteTransform = transform.GetChild(0).transform;
            CurrentRoom = null;
            TargetRoom = null;
        }

        private void Update()
        {
            if (CrewSystem == null || !CrewSystem.CanRunCrewLogic())
            {
                return;
            }

            UpdateCrewStatus();
            HandleStatus();
        }

        #endregion

        #region Public Method

        public void SetTargetRoom(Room room)
        {
            TargetRoom = room;
            CurrentRoom.ReduceCrew(this);
            // 이동하기전 보조 정지하고 가야함
            if (CurrentRoom.IsEmptyCrew())
            {
                AssistFacility(false);
                ResetRepairProgressOnEmptyRoom(CurrentRoom);
            }

            TargetRoom.AddCrew(this);
            CurrentRoom = TargetRoom;
            crewStatus = CrewStatus.Moving;
        }

        public void InitStartRoom(Room room)
        {
            CurrentRoom = room;
            room.AddCrew(this);
            crewSpriteTransform.position = room.GlobalPosition;
        }

        public void ApplyCrewState(CrewState crewState, Room room)
        {
            CurrentRoom.ReduceCrew(this);
            InitStartRoom(room);
            Hp = crewState.Hp;
            Name = crewState.Name;
        }

        public void HealHp(int amount)
        {
            Hp = Mathf.Min(MaxHp, Hp + amount);
        }

        public void TakeDamage(int amount)
        {
            Hp = Mathf.Max(0, Hp - amount);
            if (IsDead)
            {
                //CurrentRoom.ReduceCrew(this);
                //if (CurrentRoom != TargetRoom)
                //    TargetRoom.ReduceCrew(this);
                CrewSystem.HandleCrewDeath(this);
                gameObject.SetActive(false);
            }
        }

        #endregion

        #region Private Method

        private void UpdateCrewStatus()
        {
            if (crewStatus == CrewStatus.Moving)
            {
                return;
            }

            if (CanMoveToLowPosition())
            {
                crewStatus = CrewStatus.Moving;
                TargetRoom = CurrentRoom;
                return;
            }

            if (CurrentRoom.HasFacility)
            {
                switch (CurrentRoom.GetFacilityStatus())
                {
                    case FacilityStatus.Damaged:
                    case FacilityStatus.Broked:
                        crewStatus = CrewStatus.Repair;
                        break;
                    case FacilityStatus.Normal:
                        crewStatus = CrewStatus.Assisting;
                        break;
                    case FacilityStatus.Disabled:
                        crewStatus = CrewStatus.Idle;
                        break;
                    default:
                        Debug.LogWarning(
                            $"CrewController: UpdateStatus: Unexpected facility status {CurrentRoom.GetFacilityStatus()} in room {CurrentRoom.Id}");
                        break;
                }
            }
        }

        private bool CanMoveToLowPosition()
        {
            Vector3 roomAvailablePosition = CurrentRoom.GetCrewPosition(this, Offset);
            bool canMoveToLowPosition = roomAvailablePosition != crewSpriteTransform.position;
            return canMoveToLowPosition;
        }

        private void RepairFacilityInRoom()
        {
            CurrentRoom.RepairFacility();
        }

        private void AssistFacility(bool isAssist)
        {
            if (CurrentRoom.HasFacility)
            {
                CurrentRoom.ApplyAssistEffectOnFacility(isAssist);
            }
        }

        private void MoveToTargetRoom()
        {
            bool hasValidTargetRoom = TargetRoom?.IsUnintialized() == false;
            if (hasValidTargetRoom)
            {
                Vector3 targetPosition = TargetRoom.GetCrewPosition(this, Offset);
                bool hasReachedTarget = crewSpriteTransform.position == targetPosition;
                if (hasReachedTarget)
                {
                    crewStatus = CrewStatus.Idle;
                    TargetRoom = null;
                }
                else
                {
                    float moveDistance = moveSpeed * Time.deltaTime;
                    crewSpriteTransform.position =
                        Vector3.MoveTowards(crewSpriteTransform.position, targetPosition, moveDistance);
                }
            }
        }

        private void HandleStatus()
        {
            switch (crewStatus)
            {
                case CrewStatus.Moving:
                    MoveToTargetRoom();
                    break;
                case CrewStatus.Idle:
                    AssistFacility(false);
                    ResetRepairProgressOnEmptyRoom(CurrentRoom);
                    break;
                case CrewStatus.Repair:
                    AssistFacility(false);
                    RepairFacilityInRoom();
                    break;
                case CrewStatus.Assisting:
                    AssistFacility(true);
                    ResetRepairProgressOnEmptyRoom(CurrentRoom);
                    break;
            }
        }

        private static void ResetRepairProgressOnEmptyRoom(Room room)
        {
            if (room.IsEmptyCrew() && room.HasFacility)
            {
                room.ResetRepairProgress();
            }
        }

        #endregion
    }
}
