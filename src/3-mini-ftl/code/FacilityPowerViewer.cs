using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FTL
{
    public class FacilityPowerViewer : PowerViewer, IPointerClickHandler
    {
        [SerializeField] private Image facilityIcon;
        [SerializeField] private Image assistIcon;
        [SerializeField] private TMP_Text iconText;
        [SerializeField] private RoomType facilityType = RoomType.Empty;
        [SerializeField] private bool isEnemy;
        private Image repairBar;

        #region Unity Method

        protected override void Awake()
        {
            base.Awake();
            barGroup = transform.Find("Power Bar Group").gameObject;
            facilityIcon = transform.Find("Facility Icon").GetComponent<Image>();
            Transform assistIconTransform = transform.Find("Assist Icon");
            Transform repairBarTransform = transform.Find("Repair Bar");
            if (assistIconTransform != null)
            {
                assistIcon = assistIconTransform.GetComponent<Image>();
            }

            if (repairBarTransform != null)
            {
                repairBar = repairBarTransform.GetComponent<Image>();
            }

            CacheBarSpritesFromChildren();
        }

        #endregion

        #region Unity Method

        private void Start()
        {
            if (repairBar != null)
            {
                repairBar.enabled = false;
            }

            // facilityType에 맞는 type을 가져오도록
            if (isEnemy)
            {
                // EnemyShield
                GameObject[] enemies = GameObject.FindGameObjectsWithTag("EnemyShip");
                switch (facilityType)
                {
                    case RoomType.Weapon:
                        WeaponSystem weaponController = null;
                        foreach (GameObject enemy in enemies)
                        {
                            if (enemy.TryGetComponent(out weaponController))
                            {
                                Debug.Log("ReactorViewer: Enemy ReactorSystem Found!");
                                break;
                            }
                        }

                        facilitySystem = weaponController;
                        break;
                    case RoomType.Engine:
                        EngineController engineController = null;
                        foreach (GameObject enemy in enemies)
                        {
                            if (enemy.TryGetComponent(out engineController))
                            {
                                Debug.Log("ReactorViewer: Enemy ReactorSystem Found!");
                                break;
                            }
                        }

                        facilitySystem = engineController;
                        break;
                    case RoomType.Shield:
                        ShieldController shieldController = null;
                        foreach (GameObject enemy in enemies)
                        {
                            if (enemy.TryGetComponent(out shieldController))
                            {
                                Debug.Log("ReactorViewer: Enemy ReactorSystem Found!");
                                break;
                            }
                        }

                        facilitySystem = shieldController;
                        break;
                    case RoomType.Empty:
                    default:
                        Debug.LogError("ReactorViewer: Invalid Facility Type!");
                        break;
                }

                if (facilitySystem == null)
                {
                    Debug.LogError("ReactorViewer: There is no ReactorSystem on Enemy!");
                }
            }

            if (facilityType == RoomType.Empty)
            {
                facilityType = facilitySystem.Type;
            }

            iconText.SetText(facilityType.ToRoomTypeName());
        }

        #endregion

        #region Public Method

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (!CanHandlePlayerPowerClick(eventData))
            {
                return;
            }

            ShipPowerController powerSystem = GameManager.Instance.Player.PowerSystem;
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                powerSystem.AllocatePower(facilityType);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                powerSystem.DeallocatePower(facilityType);
            }
        }

        #endregion

        #region Private Method

        private bool CanHandlePlayerPowerClick(PointerEventData eventData)
        {
            if (isEnemy || facilitySystem == null || facilityType == RoomType.Empty)
            {
                return false;
            }

            if (!IsFacilityIconClicked(eventData))
            {
                return false;
            }

            if (!GameManager.IsInitialized || GameManager.Instance.Player == null)
            {
                Debug.LogWarning($"{gameObject.name}: Player is not ready to handle facility power click.");
                return false;
            }

            return GameManager.Instance.Player.PowerSystem != null;
        }

        private bool IsFacilityIconClicked(PointerEventData eventData)
        {
            if (facilityIcon == null)
            {
                return false;
            }

            GameObject clickedObject = eventData.pointerPressRaycast.gameObject;
            if (clickedObject == null)
            {
                return false;
            }

            return clickedObject == facilityIcon.gameObject ||
                   clickedObject.transform.IsChildOf(facilityIcon.transform);
        }

        #endregion

        #region Private Method

        protected override void SetBarState(int barIndex, PowerBarStatus status)
        {
            base.SetBarState(barIndex, status);
        }

        protected override void UpdateViewer()
        {
            if (facilitySystem == null)
            {
                UpdateGameContextViewer();
                return;
            }

            int maxPower = facilitySystem.MaxPower;
            int power = facilitySystem.Power;
            int maxPowerLimit = facilitySystem.MaxPowerLimit;
            int totalDamage = facilitySystem.TotalDamage;

            UpdateAssistImage();
            UpdatePowerBars(power, maxPower, maxPowerLimit, totalDamage);
            UpdateRepairBar();
            UpdateFacilityIcon();
        }

        private void UpdateRepairBar()
        {
            // 수리바는 상점 씬에서는 필요가 없음
            if (repairBar == null)
            {
                return;
            }

            if (!repairBar.enabled)
            {
                return;
            }

            float repairDuration = facilitySystem.RepairDuration;
            float repairProgress = facilitySystem.RepairProgress;
            float newFillAmount = Mathf.InverseLerp(0f, repairDuration, repairProgress);
            repairBar.fillAmount = newFillAmount;
            if (newFillAmount == 1f)
            {
                repairBar.enabled = false;
            }
        }

        private void UpdateFacilityIcon()
        {
            if (facilityIcon == null)
            {
                return;
            }

            // 만약 할당된 전력이 0이라면 시설 아이콘 색깔 비활성화
            switch (facilitySystem.FacilityStatus)
            {
                case FacilityStatus.Normal:
                    facilityIcon.color = Color.green;
                    break;
                case FacilityStatus.Damaged:
                    facilityIcon.color = Color.orange;
                    break;
                case FacilityStatus.Broked:
                    facilityIcon.color = Color.red;
                    break;
                case FacilityStatus.Disabled:
                    facilityIcon.color = Color.grey;
                    break;
                default:
                    Debug.LogWarning("Unknown Facility Status");
                    break;
            }
        }

        private void UpdatePowerBars(int power, int maxPower, int maxPowerLimit, int totalDamage)
        {
            // 만약 현재 powerBarSprites 수가 실제 totalPower 와 일치하지 않는다면 생성
            if (barSprites.Count < maxPowerLimit)
            {
                int n = maxPowerLimit - barSprites.Count;
                for (int i = 0; i < n; i++)
                {
                    AddBar();
                }
            }

            for (int i = 0; i < barSprites.Count; i++)
            {
                if (i < power)
                {
                    SetBarState(i, PowerBarStatus.Available);
                }
                else if (totalDamage > 0 && i >= maxPowerLimit - totalDamage)
                {
                    SetBarState(i, PowerBarStatus.Broken);
                    if (facilitySystem.RepairProgress > 0f && i == maxPowerLimit - totalDamage)
                    {
                        // 해당 PowerBar 위치로 repairBar 이동시키고 SetActive
                        repairBar.enabled = true;
                        RectTransform barTransform = barSprites[i].rectTransform;
                        repairBar.rectTransform.position = barTransform.position;
                    }
                }
                else
                {
                    SetBarState(i, PowerBarStatus.UnAvailable);
                }
            }
        }

        private void UpdateAssistImage()
        {
            if (assistIcon == null)
            {
                return;
            }

            assistIcon.enabled = facilitySystem.IsAssisted;
        }

        private void UpdateGameContextViewer()
        {
            int maxPowerLimit = 0;
            if (!TryGetGameContextFacilityMaxPowerLimit(out maxPowerLimit))
            {
                Debug.LogError($"{gameObject.name}: Failed to get facility max power limit!");
                return;
            }

            //assistIcon.enabled = false;
            //repairBar.enabled = false;
            facilityIcon.color = Color.green;
            UpdateGameContextPowerBars(maxPowerLimit);
        }

        private void UpdateGameContextPowerBars(int maxPowerLimit)
        {
            if (barSprites.Count < maxPowerLimit && barPrefab != null && barGroup != null)
            {
                int n = maxPowerLimit - barSprites.Count;
                for (int i = 0; i < n; i++)
                {
                    AddBar();
                }
            }

            for (int i = 0; i < barSprites.Count; i++)
            {
                if (i < maxPowerLimit)
                {
                    SetBarState(i, PowerBarStatus.Available);
                }
                else
                {
                    SetBarState(i, PowerBarStatus.UnAvailable);
                }
            }
        }

        private bool TryGetGameContextFacilityMaxPowerLimit(out int maxPowerLimit)
        {
            maxPowerLimit = 0;

            if (!GameManager.IsInitialized)
            {
                Debug.LogError($"{gameObject.name}: GameManager is not Initialized to use!");
                return false;
            }

            GameManager gameManager = GameManager.Instance;
            if (gameManager.IsGameContextEmpty)
            {
                Debug.LogError($"{gameObject.name}: GameContext is Empty to use!");
                return false;
            }

            // 문자열 기반 탐색은 절대 안됨 (무조건 고칠것)
            maxPowerLimit = gameManager.GetFacilityMaxPowerLimit(facilityType);
            return true;
        }

        private void CacheBarSpritesFromChildren()
        {
            Image[] images = barGroup.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (!barSprites.Contains(images[i]))
                {
                    barSprites.Add(images[i]);
                    images[i].gameObject.SetActive(false);
                }
            }
        }

        #endregion
    }
}
