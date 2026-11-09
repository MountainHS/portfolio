using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Utilities;

namespace FTL
{
    /// <summary>
    ///     GameManager는 전투 종료, 게임 종료 같은 씬간 정보를 관리
    /// </summary>
    public class GameManager : SingletonBase<GameManager>
    {
        private const string battleScene = "BattleScene";
        private const string battleScene1 = "BattleScene1";
        private const string battleScene2 = "BattleScene2";
        private const string battleScene3 = "BattleScene3";
        private const string battlePrepare = "BattlePrepare";
        private const string victoryScene = "Victory";
        [field: SerializeField] public BattleResult BattleResult { get; set; }

        [field: SerializeField] public EnemyDatabase EnemyDatabase { get; private set; }
        [field: SerializeField] public WeaponDatabase WeaponDatabase { get; private set; }
        [SerializeField] private GameObject toggleMenuObject;
        [SerializeField] private GameContext gameContext;
        private MainUIController mainUIController;

        public bool IsGameContextEmpty => gameContext == null || gameContext.IsEmpty;
        public bool IsCombatPaused { get; private set; }

        public bool IsBattleStopped { get; private set; }

        public bool CanRunCombat => !IsCombatPaused && !IsBattleStopped;
        public PlayerController Player { get; set; }

        #region Unity Method

        private void Start()
        {
            InitUIObject();
        }

        private void Update()
        {
            if (SimpleInput.GetKeyDown(Key.Escape))
            {
                string sceneName = SceneManager.GetActiveScene().name;
                if (sceneName == battlePrepare || IsBattleScene(sceneName))
                {
                    ToggleMenu(sceneName);
                }
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        #endregion

        #region Public Method

        public void HandleBattleResult(BattleResult battleResult)
        {
            StopBattle();
            // 함선이 파괴되면
            switch (battleResult)
            {
                case BattleResult.PlayerShipDestroyed:
                    Debug.Log("Player ship destroyed");
                    mainUIController.SetBattleResult(battleResult, 0);
                    break;
                case BattleResult.PlayerAllCrewDead:
                    // 게임오버 화면으로
                    // 어떤 Uicontroller인지 몰라도 얘한테 시켜서 게임오버화면으로 이동
                    mainUIController.SetBattleResult(battleResult, 0);
                    Debug.Log("Player all crew dead.");
                    break;
                case BattleResult.EnemyShipDestroyed:
                    SetGameContext(Player);
                    gameContext.AddScrap(100);
                    mainUIController.SetBattleResult(battleResult, 100);
                    Debug.Log("Enemy ship destroyed.");
                    break;
                case BattleResult.EnemyAllCrewDead:
                    SetGameContext(Player);
                    gameContext.AddScrap(200);
                    mainUIController.SetBattleResult(battleResult, 200);
                    Debug.Log("Enemy all crew dead.");
                    break;
                case BattleResult.PlayerRun:
                    // 전투준비 화면으로 이동
                    // 어떤 Uicontroller인지 몰라도 얘한테 시켜서 전투준비화면으로 이동
                    SetGameContext(Player);
                    mainUIController.SetBattleResult(battleResult, 0);
                    Debug.Log("Player run.");
                    break;
                case BattleResult.None:
                default:
                    Debug.LogError("Unknown EBattleResult");
                    break;
            }
        }

        public void MoveSceneByBattleResult(BattleResult battleResult)
        {
            switch (battleResult)
            {
                case BattleResult.PlayerShipDestroyed:
                    mainUIController.MoveScene(SceneIndex.GameOver);
                    break;
                case BattleResult.PlayerAllCrewDead:
                    mainUIController.MoveScene(SceneIndex.GameOver);
                    Debug.Log("Player all crew dead.");
                    break;
                case BattleResult.EnemyShipDestroyed:
                    mainUIController.MoveScene(SceneIndex.BattlePrepare);
                    Debug.Log("Enemy ship destroyed.");
                    break;
                case BattleResult.EnemyAllCrewDead:
                    mainUIController.MoveScene(SceneIndex.BattlePrepare);
                    Debug.Log("Enemy all crew dead.");
                    break;
                case BattleResult.PlayerRun:
                    mainUIController.MoveScene(SceneIndex.BattlePrepare);
                    Debug.Log("Player run.");
                    break;
                case BattleResult.None:
                default:
                    Debug.LogError("Unknown EBattleResult");
                    break;
            }
        }

        public void PlayerRun()
        {
            HandleBattleResult(BattleResult.PlayerRun);
        }

        public void SetCombatPaused(bool isPaused)
        {
            if (IsBattleStopped && isPaused)
            {
                return;
            }

            IsCombatPaused = isPaused;
            Time.timeScale = isPaused ? 0f : 1f;
        }

        public void StopBattle()
        {
            IsBattleStopped = true;
            if (toggleMenuObject != null)
            {
                toggleMenuObject.SetActive(false);
            }

            SetCombatPaused(false);
        }

        //public void SetGameContext(List<Room> rooms, List<FacilitySystem> facilities, List<CrewController> crews, int shipHp, int shipMaxHp, int scrapAmount, int shipReactorCapacity, int shipPower)
        //{
        //    if (gameContext == null)
        //    {
        //        gameContext = new GameContext(rooms, facilities, crews, scrapAmount, shipHp, shipMaxHp, shipReactorCapacity, shipPower);
        //    }
        //    else
        //    {
        //        gameContext.SetGameContext(rooms, facilities, crews, scrapAmount, shipHp, shipMaxHp, shipReactorCapacity, shipPower);
        //    }
        //}

        public void SetGameContext(PlayerController player)
        {
            List<Room> rooms = player.Rooms;
            List<FacilityController> facilities = player.FacilitySystems;
            List<CrewController> crews = player.Crews;

            List<FacilityState> facilityStates = new();
            for (int i = 0; i < facilities.Count; i++)
            {
                facilityStates.Add(new FacilityState(facilities[i]));
            }

            List<CrewState> crewStates = new();
            for (int i = 0; i < crews.Count; i++)
            {
                crewStates.Add(new CrewState(crews[i]));
            }

            gameContext = new GameContext(rooms, facilityStates, crewStates, player.ScrapAmount, player.Hp,
                player.ShipData.MaxHp, player.PowerSystem.GetReactorMaxPower, player.PowerSystem.GetPower);
            gameContext.SetWeaponTypes(player.WeaponTypes);
        }

        public void EnsureBattlePrepareTestGameContext()
        {
            if (!IsGameContextEmpty)
            {
                return;
            }

            List<Room> rooms = new();

            List<FacilityState> facilityStates = new()
            {
                new FacilityState(0, 3, 1, RoomType.Weapon),
                new FacilityState(0, 3, 1, RoomType.Engine),
                new FacilityState(0, 3, 2, RoomType.Shield)
            };

            List<CrewState> crewStates = new()
            {
                new CrewState(100, 100, "Hans", 1),
                new CrewState(100, 100, "Tom", 2),
                new CrewState(100, 100, "Jane", 3)
            };

            gameContext = new GameContext(rooms, facilityStates, crewStates, 100, 30, 30, 6, 2);
        }

        //public void AddScrap(int amount = 1)
        //{
        //    if (gameContext == null)
        //    {
        //        Debug.LogError("GameContext is null. Cannot add scrap.");
        //        return;
        //    }
        //    gameContext.ScrapAmount += amount;
        //}

        public int GetScrap()
        {
            if (gameContext == null)
            {
                Debug.LogError("GameContext is null. Cannot get scrap.");
                return 0;
            }

            return gameContext.ScrapAmount;
        }

        public void IncreaseMaxPowerLimitOfFacility(RoomType roomType, int amount = 1)
        {
            gameContext.IncreaseMaxPowerLimitOfFacility(roomType, amount);
        }

        public void ResetGameContext()
        {
            gameContext = null;
            ShopManager.VisitCount = 0;
        }

        public void ExitGame()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void IncreaseReactorMaxPower(int amount = 1)
        {
            gameContext.IncreaseReactorMaxPower(amount);
        }

        public int GetCrewHp(int crewIndex)
        {
            return gameContext.GetCrewHp(crewIndex);
        }

        public int GetCrewMaxHp(int crewIndex)
        {
            return gameContext.GetCrewMaxHp(crewIndex);
        }

        public int GetCrewCount()
        {
            return gameContext.GetCrewCount();
        }

        public string GetCrewName(int crewIndex)
        {
            return gameContext.GetCrewName(crewIndex);
        }

        public void AddCrewHp(int crewIndex, int realIncrease)
        {
            gameContext.AddCrewHp(crewIndex, realIncrease);
        }

        public int GetShipHp()
        {
            return gameContext.ShipHp;
        }

        public int GetShipMaxHp()
        {
            return gameContext.ShipMaxHp;
        }

        public int GetShipReactorCapacity()
        {
            return gameContext.ShipReactorCapacity;
        }

        public int GetFacilityMaxPowerLimit(RoomType roomType)
        {
            return gameContext.GetFacilityMaxPowerLimit(roomType);
        }

        public void AddShipHp(int amount)
        {
            gameContext.AddShipHp(amount);
        }

        public void TestHandleBattleResult()
        {
            if (SimpleInput.GetKeyDown(Key.Digit1))
            {
                BattleResult = BattleResult.PlayerShipDestroyed;
            }

            if (SimpleInput.GetKeyDown(Key.Digit2))
            {
                BattleResult = BattleResult.PlayerAllCrewDead;
            }

            if (SimpleInput.GetKeyDown(Key.Digit3))
            {
                BattleResult = BattleResult.PlayerRun;
            }

            if (SimpleInput.GetKeyDown(Key.Digit4))
            {
                BattleResult = BattleResult.EnemyAllCrewDead;
            }

            if (SimpleInput.GetKeyDown(Key.Digit5))
            {
                BattleResult = BattleResult.EnemyShipDestroyed;
            }

            if (SimpleInput.AnyKeyDown())
            {
                HandleBattleResult(BattleResult);
            }
        }

        public void TurnOffToggleMenu()
        {
            if (toggleMenuObject != null)
            {
                toggleMenuObject.SetActive(false);
            }

            SetCombatPaused(false);
        }

        public void ReduceScrap(int v)
        {
            gameContext.ReduceScrap(v);
        }

        [ContextMenu("AddScrap100")]
        public void AddScrap100()
        {
            gameContext.AddScrap(100);
        }

        [ContextMenu("Change Background")]
        public void ChangeBackground()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == battleScene)
            {
                BackgroundController backgroundController = FindFirstObjectByType<BackgroundController>();
                backgroundController.SetRandomBackground();
            }
        }

        #endregion

        #region Private Method

        private void InitUIObject()
        {
            mainUIController = FindFirstObjectByType<MainUIController>();
            if (mainUIController == null)
            {
                return;
            }

            toggleMenuObject = mainUIController.transform.GetChild(1).gameObject;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode sceneMode)
        {
            IsBattleStopped = false;
            SetCombatPaused(false);
            InitUIObject();
            CursorController.Instance.ResetMouseSprite();
            if (scene.name == battleScene)
            {
                BindLinearBattleScene();
            }
            else if (scene.name == battleScene1)
            {
                EnemyType enemyType = EnemyType.Enemy1;
                SceneBinder sceneBinder = FindFirstObjectByType<SceneBinder>();
                BackgroundController backgroundController = FindFirstObjectByType<BackgroundController>();

                gameContext.SetEnemyType(enemyType);
                sceneBinder.BindSceneObjects(gameContext);
                backgroundController.SetRandomBackground();
            }
            else if (scene.name == battleScene2)
            {
                EnemyType enemyType = EnemyType.Enemy2;
                SceneBinder sceneBinder = FindFirstObjectByType<SceneBinder>();
                BackgroundController backgroundController = FindFirstObjectByType<BackgroundController>();

                gameContext.SetEnemyType(enemyType);
                sceneBinder.BindSceneObjects(gameContext);
                backgroundController.SetRandomBackground();
            }
            else if (scene.name == battleScene3)
            {
                EnemyType enemyType = EnemyType.Enemy3;
                SceneBinder sceneBinder = FindFirstObjectByType<SceneBinder>();
                BackgroundController backgroundController = FindFirstObjectByType<BackgroundController>();

                gameContext.SetEnemyType(enemyType);
                sceneBinder.BindSceneObjects(gameContext);
                backgroundController.SetRandomBackground();
            }
            else if (scene.name == battlePrepare)
            {
                ShopManager shopManager = FindFirstObjectByType<ShopManager>();
                shopManager.ApplyGameContext(gameContext);
            }
        }

        private void BindLinearBattleScene()
        {
            int battlePrepareVisitCount = ShopManager.VisitCount;
            EnemyType enemyType = EnemyType.Enemy1;
            bool isAsteroidStage = false;
            bool isSunStage = false;

            if (battlePrepareVisitCount == 1)
            {
                enemyType = EnemyType.Enemy2;
                isAsteroidStage = true;
            }
            else if (battlePrepareVisitCount == 2)
            {
                enemyType = EnemyType.Enemy2;
                isSunStage = true;
            }
            else if (battlePrepareVisitCount == 3)
            {
                enemyType = EnemyType.Enemy3;
                isAsteroidStage = true;
                isSunStage = true;
            }
            else if (battlePrepareVisitCount >= 4)
            {
                SceneManager.LoadScene(victoryScene);
                return;
            }

            SceneBinder sceneBinder = FindFirstObjectByType<SceneBinder>();
            BackgroundController backgroundController = FindFirstObjectByType<BackgroundController>();

            gameContext.SetEnemyType(enemyType);
            sceneBinder.BindSceneObjectsWithEnvironment(gameContext, isAsteroidStage, isSunStage);
            backgroundController.SetRandomBackground();
        }

        private void ToggleMenu(string sceneName)
        {
            if (IsBattleStopped)
            {
                return;
            }

            if (toggleMenuObject == null)
            {
                Debug.LogError("GameManager: toggleMenuObject is not assigned.");
                return;
            }

            bool isToggleMenuActive = toggleMenuObject.activeSelf;
            bool shouldOpenMenu = !isToggleMenuActive;
            toggleMenuObject.SetActive(shouldOpenMenu);

            if (IsBattleScene(sceneName))
            {
                SetCombatPaused(shouldOpenMenu);
            }
        }

        private bool IsBattleScene(string sceneName)
        {
            return sceneName == battleScene ||
                   sceneName == battleScene1 ||
                   sceneName == battleScene2 ||
                   sceneName == battleScene3;
        }

        #endregion
    }
}
