using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FTL
{
    [DefaultExecutionOrder(1000)]
    public class ShipController : MonoBehaviour, IDamageable, IDeadable
    {
        private const float deadAnimationTime = 5f;

        [Header("Start Weapon Types")]
        [field: SerializeField]
        public List<WeaponType> WeaponTypes { get; private set; } = new();

        [SerializeField] protected GameObject weaponChargeStatusPrefab;
        [SerializeField] protected List<WeaponTransformData> weaponTransformList;

        [Header("Ship Data")]
        [field: SerializeField]
        public ShipData ShipData { get; private set; }

        [field: SerializeField] public int Hp { get; set; }

        [Header("Power System")] public ShipPowerController PowerSystem;

        [Header("Room")] [SerializeField] private GameObject roomPrefab;

        [SerializeField] private Transform roomGroupTransform;
        [field: SerializeField] public List<Room> Rooms { get; private set; } = new();

        [Header("Crew")] [SerializeField] private Transform crewGroupTransform;

        [SerializeField] private Transform crewSummaryGroupTransform;
        [SerializeField] protected Transform weaponGroupTransform;
        [SerializeField] protected Transform weaponChargeGroupTransform;
        [SerializeField] private GameObject crewPrefab;
        [SerializeField] private GameObject crewSummaryPrefab;
        [SerializeField] protected int crewCount = 3;
        [SerializeField] protected bool isPlayer;
        public List<FacilityController> FacilitySystems = new();
        protected CrewSystem crewSystem;
        protected bool isDeadCoroutineActivated;
        protected bool isShipInitialized;
        protected WeaponSystem WeaponController;

        public bool IsDead => Hp == 0;
        public bool IsCombatStopped { get; private set; }

        public ShipOwnership Ownership { get; private set; }

        public List<CrewController> Crews { get; set; } = new();

        #region Unity Method

        protected virtual void Awake()
        {
            InitShipReferences();
            TryInitializeShip(false);
        }

        protected virtual void Start()
        {
            crewSystem.Rooms = Rooms;
        }

        #endregion

        #region Public Method

        public void SetExternalUIGroups(Transform crewSummaryGroup, Transform weaponChargeGroup)
        {
            crewSummaryGroupTransform = crewSummaryGroup;
            weaponChargeGroupTransform = weaponChargeGroup;
        }

        public void InitializeShipIfReady()
        {
            TryInitializeShip(true);
        }

        #endregion

        #region Private Method

        private void InitShipReferences()
        {
            if (ShipData != null)
            {
                Hp = ShipData.MaxHp;
            }

            FacilitySystems.Clear();
            FacilitySystems.Add(GetComponent<WeaponSystem>());
            FacilitySystems.Add(GetComponent<EngineController>());
            FacilitySystems.Add(GetComponent<ShieldController>());
            crewSystem = GetComponent<CrewSystem>();

            Ownership = GetComponentInChildren<ShipOwnership>();

            WeaponController = (WeaponSystem)GetFacilitySystemByERoomType(RoomType.Weapon);
            PowerSystem = GetComponent<ShipPowerController>();
            roomGroupTransform = FindDirectChildWithTag(transform, "Room Group").transform;
            crewGroupTransform = FindDirectChildWithTag(transform, "Crew Group").transform;
            weaponGroupTransform = FindDirectChildWithTag(transform, "Weapon Group").transform;
            isDeadCoroutineActivated = false;
            isShipInitialized = false;
            IsCombatStopped = false;
        }

        private void TryInitializeShip(bool shouldLogError)
        {
            if (isShipInitialized)
            {
                return;
            }

            if (!CanInitializeShip(shouldLogError))
            {
                return;
            }

            InitRooms();
            InitCrews();
            InitWeapons();
            isShipInitialized = true;
        }

        private bool CanInitializeShip(bool shouldLogError)
        {
            bool canInitialize =
                ShipData != null &&
                roomPrefab != null &&
                roomGroupTransform != null &&
                crewGroupTransform != null &&
                crewSummaryGroupTransform != null &&
                weaponGroupTransform != null &&
                weaponChargeGroupTransform != null &&
                crewPrefab != null &&
                crewSummaryPrefab != null &&
                weaponChargeStatusPrefab != null &&
                crewSystem != null &&
                WeaponController != null &&
                PowerSystem != null;

            if (!canInitialize && shouldLogError)
            {
                Debug.LogError($"{name}: Ship initialization failed. Required prefab or group reference is missing.");
            }

            return canInitialize;
        }

        private void InitRooms()
        {
            for (int i = 0; i < ShipData.Rooms.Count; i++)
            {
                GameObject roomObject = Instantiate(roomPrefab, roomGroupTransform);
                RoomViewer roomViewer = roomObject.GetComponent<RoomViewer>();
                Room room = new(ShipData.Rooms[i]);
                // RoomData 초기화 함수 생성
                roomObject.transform.localPosition = room.LocalPosition;
                room.GlobalPosition = roomObject.transform.position;
                roomViewer.Room = room;
                roomViewer.SetRoomState();

                if (room.Type != RoomType.Empty)
                {
                    room.FacilitySystem = GetFacilitySystemByERoomType(room.Type);
                }

                Rooms.Add(room);
            }
        }

        private void InitCrews()
        {
            for (int i = 0; i < crewCount; i++)
            {
                GameObject crewObject = Instantiate(crewPrefab, crewGroupTransform);
                CrewController crew = crewObject.GetComponent<CrewController>();
                Room room = Rooms[i];
                GameObject crewSummaryObject = Instantiate(crewSummaryPrefab, crewSummaryGroupTransform);
                CrewViewer crewViewer = crewSummaryObject.GetComponent<CrewViewer>();
                SpriteRenderer crewSprite = crewObject.transform.Find("Crew Sprite").GetComponent<SpriteRenderer>();
                SpriteRenderer crewTargetSprite =
                    crewObject.transform.Find("Target Room").GetComponent<SpriteRenderer>();

                crewViewer.SetCrewSprite(crewSprite);
                crewViewer.SetCrewTargetSpite(crewTargetSprite);
                crewViewer.SetCrewController(crew);
                crew.InitStartRoom(room);
                Crews.Add(crew);
            }
        }

        private void InitWeapons()
        {
            for (int i = 0; i < WeaponTypes.Count; i++)
            {
                WeaponType weaponType = WeaponTypes[i];
                WeaponDatabase weaponDatabase = GameManager.Instance.WeaponDatabase;
                GameObject weaponPrefab = weaponDatabase.GetPrefab(weaponType);
                GameObject weaponObject = Instantiate(weaponPrefab, weaponGroupTransform);
                GameObject weaponChargeObject = Instantiate(weaponChargeStatusPrefab, weaponChargeGroupTransform);
                WeaponViewer weaponViewer = weaponObject.GetComponent<WeaponViewer>();
                WeaponController weapon = weaponObject.GetComponent<WeaponController>();
                Image chargeImage = weaponChargeObject.transform.GetChild(1)
                    .GetChild(1)
                    .GetComponent<Image>();
                Button weaponButton = weaponChargeObject.GetComponentInChildren<Button>();
                SpriteRenderer weaponSprite = weaponObject.GetComponentInChildren<SpriteRenderer>();
                Vector3 weaponOffset = weaponTransformList[i].WeaponOffset;
                Vector3 weaponRotation = weaponTransformList[i].WeaponRotation;
                bool isFlipX = weaponTransformList[i].isFlipX;
                bool isFlipY = weaponTransformList[i].isFlipY;
                int index = i;

                weaponObject.transform.localPosition += weaponOffset;
                weaponObject.transform.Rotate(weaponRotation);
                weaponSprite.flipX = isFlipX;
                weaponSprite.flipY = isFlipY;
                weaponViewer.SetChargeBar(chargeImage);
                WeaponController.AddWeapon(weapon);
                if (isPlayer)
                {
                    weaponButton.onClick.RemoveAllListeners();
                    weaponButton.onClick.AddListener(() => WeaponController.SetSelectedWeapon(index));
                }
            }

            WeaponController.SetWeaponSystemOfWeapons();
        }


        protected void HandlePowerInput(RoomType facilityType, bool isRecoverInput)
        {
            if (!CanControlShip())
            {
                return;
            }

            if (isRecoverInput)
            {
                RequestDeallocatePower(facilityType);
            }
            else
            {
                RequestAllocatePower(facilityType);
            }
        }

        // roomId를 받으면 Rooms에서 탐색해서 해당 room 리턴
        protected Room GetRoomByIdOrNull(int roomId)
        {
            for (int i = 0; i < Rooms.Count; i++)
            {
                bool isMatchingRoom = Rooms[i].Id == roomId;
                if (isMatchingRoom)
                {
                    return Rooms[i];
                }
            }

            return null;
        }

        protected Room GetRoomByPositionOrNull(Vector2 position)
        {
            for (int i = 0; i < Rooms.Count; i++)
            {
                bool isMatchingPosition = Rooms[i].GlobalPosition == position;
                if (isMatchingPosition)
                {
                    return Rooms[i];
                }
            }

            return null;
        }

        #endregion

        #region Public Method

        public FacilityController GetFacilitySystemByERoomType(RoomType roomType)
        {
            switch (roomType)
            {
                case RoomType.Weapon:
                    return FacilitySystems[(int)RoomType.Weapon];
                case RoomType.Engine:
                    return FacilitySystems[(int)RoomType.Engine];
                case RoomType.Shield:
                    return FacilitySystems[(int)RoomType.Shield];
                default:
                    throw new ArgumentException("Invalid room type");
            }
        }

        public void SetWeaponTypes(List<WeaponType> weaponTypes)
        {
            WeaponTypes = new List<WeaponType>();

            for (int i = 0; i < weaponTypes.Count; i++)
            {
                WeaponTypes.Add(weaponTypes[i]);
            }
        }

        public void TakeDamage(int damage)
        {
            Hp = Mathf.Max(0, Hp - damage);
        }

        public void TakeDamageToRoom(ProjectileController projectile, int roomId)
        {
            int damage = projectile.Damage;
            int crewDamage = projectile.CrewDamage;
            TakeDamageToRoom(damage, crewDamage, roomId);
        }

        public void TakeDamageToRoom(int damage, int crewDamage, int roomId)
        {
            // 피격당한 room 객체 가져오기
            Room room = GetRoomByIdOrNull(roomId);
            if (room == null)
            {
                Debug.LogError($"{name}: Cannot apply room damage. Room id {roomId} was not found.");
                return;
            }

            room.TakeDamageToCrews(crewDamage);
            StartCoroutine(SetRoomHitCoroutine(room));
            bool canDamageFacility = room != null && room.HasFacility;
            if (canDamageFacility)
            {
                room.TakeDamageToFacility(damage);
            }

            TakeDamage(damage);

            CheckGameOver();
        }

        public void StopCombatFunctions()
        {
            IsCombatStopped = true;
            if (WeaponController != null)
            {
                WeaponController.ResetSelectedWeapon();
            }
        }

        public void RestoreHp(int damage)
        {
            if (IsDead)
            {
                return;
            }

            int restoredHp = Hp + damage;
            Hp = restoredHp > ShipData.MaxHp ? ShipData.MaxHp : restoredHp;
        }

        public void RequestAllocatePower(RoomType facilityType)
        {
            if (!CanControlShip())
            {
                return;
            }

            PowerSystem.AllocatePower(facilityType);
        }

        public void RequestDeallocatePower(RoomType facilityType)
        {
            if (!CanControlShip())
            {
                return;
            }

            PowerSystem.DeallocatePower(facilityType);
        }

        public virtual IEnumerator DeadCoroutine(float seconds, BattleResult result)
        {
            isDeadCoroutineActivated = true;
            yield return new WaitForSeconds(seconds);
            GameManager.Instance.HandleBattleResult(result);
            gameObject.SetActive(false);
        }

        [ContextMenu("TakeDamage 1")]
        public void TestTakeDamage()
        {
            TakeDamage(1);
            CheckGameOver();
        }

        #endregion

        #region Private Method

        protected bool CanControlShip()
        {
            return !IsDead &&
                   !IsCombatStopped &&
                   GameManager.Instance.CanRunCombat;
        }

        protected GameObject FindDirectChildWithTag(Transform parent, string tag)
        {
            foreach (Transform child in parent)
            {
                if (child.CompareTag(tag))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        protected IEnumerator SetRoomHitCoroutine(Room room)
        {
            room.SetIsHit(true);
            yield return new WaitForSeconds(0.25f);
            room.SetIsHit(false);
        }

        protected void CheckGameOver()
        {
            BattleResult battleResult = BattleResult.None;
            bool isGameOver = IsDead || crewSystem.IsAllCrewDead;
            if (!isGameOver || isDeadCoroutineActivated)
            {
                return;
            }

            StopCombatFunctions();

            if (crewSystem.IsAllCrewDead)
            {
                battleResult = Ownership.ShipSide == ShipSide.Player
                    ? BattleResult.PlayerAllCrewDead
                    : BattleResult.EnemyAllCrewDead;
            }

            if (IsDead)
            {
                battleResult = Ownership.ShipSide == ShipSide.Player
                    ? BattleResult.PlayerShipDestroyed
                    : BattleResult.EnemyShipDestroyed;
            }

            GameManager.Instance.StopBattle();

            StartCoroutine(DeadCoroutine(deadAnimationTime, battleResult));
        }

        #endregion
    }
}
