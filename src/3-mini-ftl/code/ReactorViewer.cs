using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace FTL
{
    public class ReactorViewer : MonoBehaviour
    {
        [SerializeField] protected GameObject barPrefab;
        [SerializeField] protected Transform barGroupTransform;
        [SerializeField] protected List<Image> barSprites;
        [FormerlySerializedAs("reactorSystem")] [SerializeField] protected ReactorController reactorController;
        [SerializeField] private bool isEnemy;
        protected ObjectPool<GameObject> barPool;

        #region Public Method

        public void AddBar()
        {
            GameObject powerBar = barPool.Get();
            barSprites.Add(powerBar.GetComponent<Image>());
            SetBarState(barSprites.Count - 1, ReactorBarStatus.Available);
        }

        #endregion

        #region Unity Method

        protected virtual void Awake()
        {
            barPool = new ObjectPool<GameObject>(CreateBar, OnGetBar, OnReleaseBar, OnDestroyBar);
        }

        private void Start()
        {
            if (isEnemy)
            {
                // EnemyShield
                GameObject[] enemies = GameObject.FindGameObjectsWithTag("EnemyShip");

                foreach (GameObject enemy in enemies)
                {
                    if (enemy.TryGetComponent(out reactorController))
                    {
                        Debug.Log("ReactorViewer: Enemy ReactorSystem Found!");
                        break;
                    }
                }

                if (reactorController == null)
                {
                    Debug.LogError("ReactorViewer: There is no ReactorSystem on Enemy!");
                }
            }
        }

        protected void LateUpdate()
        {
            UpdateViewer();
        }

        // 전력바 상태 관리: 가용, 불가능
        // 전력바 상태에 따라 스프라이트 상태 변경

        #endregion

        #region Private Method

        private void SetBarState(int barIndex, ReactorBarStatus status)
        {
            if (barIndex < 0 || barIndex >= barSprites.Count)
            {
                Debug.LogWarning($"Invalid bar index: {barIndex}");
                return;
            }

            Image image = barSprites[barIndex];
            // 스프라이트 상태 변경
            switch (status)
            {
                case ReactorBarStatus.Available:
                    image.color = Color.green; // 가용 상태 색상
                    break;
                case ReactorBarStatus.UnAvailable:
                    image.color = Color.grey; // 불가능 상태 색상
                    break;
                default:
                    Debug.LogError($"Unknown power bar status: {status}");
                    break;
            }
        }

        // 현재 전력 상태 (총 용량, 가용 가능한 양 상태에 따라 power bar 업데이트)
        private void UpdateViewer()
        {
            int totalPower = 0;
            int availablePower = 0;
            if (reactorController != null)
            {
                totalPower = reactorController.MaxPower;
                availablePower = reactorController.Power;
            }
            else if (!TryGetGameContextReactorCapacity(out totalPower))
            {
                return;
            }
            else
            {
                availablePower = totalPower;
            }

            // 만약 현재 powerBarSprites 수가 실제 totalPower 와 일치하지 않는 다면 생성
            if (barSprites.Count < totalPower)
            {
                int n = totalPower - barSprites.Count;
                for (int i = 0; i < n; i++)
                {
                    AddBar();
                }
            }

            for (int i = 0; i < barSprites.Count; i++)
            {
                if (i < availablePower)
                {
                    SetBarState(i, ReactorBarStatus.Available);
                }
                else
                {
                    SetBarState(i, ReactorBarStatus.UnAvailable);
                }
            }
        }

        private bool TryGetGameContextReactorCapacity(out int reactorCapacity)
        {
            reactorCapacity = 0;

            if (!GameManager.IsInitialized)
            {
                Debug.LogError($"{gameObject.name}: GameManager is not Initialized to use!");
                return false;
            }

            GameManager gameManager = GameManager.Instance;
            if (gameManager.IsGameContextEmpty)
            {
                Debug.LogError($"{gameObject.name}: GameManager's GameContext is empty to use!");
                return false;
            }

            reactorCapacity = gameManager.GetShipReactorCapacity();
            return true;
        }

        private GameObject CreateBar()
        {
            return Instantiate(barPrefab, barGroupTransform);
        }

        private void OnGetBar(GameObject powerBar)
        {
            powerBar.transform.SetParent(barGroupTransform);
            powerBar.SetActive(true);
        }

        private void OnReleaseBar(GameObject powerBar)
        {
            powerBar.SetActive(false);
        }

        private void OnDestroyBar(GameObject powerBar)
        {
            Destroy(powerBar);
        }

        #endregion
    }
}
