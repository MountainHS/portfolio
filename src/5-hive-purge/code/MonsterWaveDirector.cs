using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Movement;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 라운드 군중전의 스테이지 진행 (씬에 1개). 라운드 경과 시간을 재고, 그 라운드 스테이지를 각 StartTime 에 시작한다.
    /// RoundSequencer가 StartRound/StopRound를 직접 호출하고, 실행 중 스테이지를 매 프레임 Tick한다.
    /// </summary>
    /// <remarks>
    /// 다음 스테이지가 시작돼도 이전 스테이지를 멈추지 않는다. 이전 스테이지는 스스로 Finished 를 낼 때까지 남은 소환을 이어 간다.
    /// 그래서 Normal 스테이지의 EndTime 은 쓰지 않는다. 보스는 다루지 않는다 (보스전 전이 때 군중 소환을 멈추기만 한다).
    /// ClearBonus 스테이지는 StartTime 이 아니라 "EndTime 전에 본 웨이브 전멸" 로 1번 시작한다 (StartClearBonusIfEarned).
    /// 라운드에 ClearBonus 가 여러 개면 (예: Normal 스테이지마다 하나) 각자 자기 EndTime(마감) 기준으로 따로 판정한다.
    /// 라운드 진행은 Update 끝에서 "스테이지를 다 시작했고, 실행 중 스테이지가 없고, 특수 웨이브를 기다리는 중이 아님" 일 때 끝난다 (EndHordePhaseIfAllDone).
    /// 그래서 마지막 소환이 끝난 뒤에도 마감 전까지는 전멸 판정이 계속 돈다.
    /// Time.deltaTime 으로 시간을 재므로 강화 화면(timeScale 0)에서 라운드 시간과 소환이 함께 멈춘다.
    /// </remarks>
    public sealed class MonsterWaveDirector : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("스테이지 시작 직전 WarmupStage 와 소환 팩토리 둘 다 이 풀을 쓴다")]
        [SerializeField] private MonsterPool monsterPool;
        [Tooltip("Crowd 형식 묶음마다 복제할 CrowdController 프리팹 (Crowd Controller.prefab). Crowd 줄이 없으면 비워도 된다")]
        [SerializeField] private CrowdController crowdControllerTemplate;

        // ── 라운드 진행 ──
        private bool isRoundRunning;
        private int roundNo;              // 로그용
        private float roundElapsedTime;   // 초, 라운드 시작 기준 (WaveStageDef.StartTime 과 같은 기준)
        private List<WaveStageDef> roundStages = new List<WaveStageDef>();  // 이번 라운드 스테이지, StartTime 순
        private int nextStageIndex;       // 다음에 시작할 roundStages 인덱스. Count = 다 시작함
        private readonly List<MonsterWaveStage> runningStages = new List<MonsterWaveStage>();

        // ── 조기 전멸 특수 웨이브 ──
        private List<WaveStageDef> pendingClearBonusStages = new List<WaveStageDef>();  // 이번 라운드에 아직 시작 안 한 ClearBonus, EndTime 순. 시작하면 뺀다
        private readonly HashSet<GameObject> aliveMonsters = new HashSet<GameObject>();  // 이번 라운드에 나와 아직 안 죽은 몬스터

        /// <summary>전체 라운드 정리를 중단 안전망으로 구독한다. 시작·보스 전이는 시퀀서가 직접 호출한다. 소환·사망은 전멸 판정용으로 센다.</summary>
        private void OnEnable()
        {
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
        }

        /// <summary>구독을 해제하고 남은 소환을 취소한다. 다시 켜도 자동으로 이어 가지 않는다</summary>
        private void OnDisable()
        {
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            StopRound();
        }

        private void Awake()
        {
            if (monsterPool == null)
                monsterPool = FindAnyObjectByType<MonsterPool>();
        }

        /// <summary>라운드 정리(다음 라운드·재시작) 때 남은 소환을 모두 취소한다. FullReset 은 구분하지 않는다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => StopRound();

        /// <summary>풀이 몬스터를 꺼낼 때마다 살아 있는 목록에 넣는다 (MonsterPool.Spawn 이 발행)</summary>
        private void OnMonsterSpawned(MonsterSpawnedEvent e) => aliveMonsters.Add(e.Instance);

        /// <summary>몬스터 사망 확정 때 살아 있는 목록에서 뺀다 (MonsterController 가 발행, 보스 등장 정리 포함)</summary>
        private void OnMonsterDied(MonsterDiedEvent e) => aliveMonsters.Remove(e.Instance);

        /// <summary>
        /// 라운드의 군중전 스테이지 진행을 0초부터 새로 시작한다. 진행 중이던 라운드는 버린다.
        /// StartTime 0인 스테이지는 이 호출 안에서 바로 시작된다. RoundSequencer 또는 Lab이 직접 부른다.
        /// </summary>
        /// <param name="roundNumber">라운드 번호. 이 RoundNo 의 스테이지가 없으면 경고만 남기고 시작하지 않는다</param>
        public void StartRound(int roundNumber)
        {
            StopRound();

            List<WaveStageDef> stagesOfRound = FindStagesOfRound(roundNumber);
            if (stagesOfRound.Count == 0)
            {
                // 끝 신호를 줄 스테이지가 없어 진행 중으로 두면 영원히 끝나지 않는다
                Debug.LogWarning($"[MonsterWaveDirector] 라운드 {roundNumber} 스테이지 데이터 없음. 군중 소환 안 함", this);
                return;
            }

            roundNo = roundNumber;
            roundStages = stagesOfRound;
            roundElapsedTime = 0f;
            nextStageIndex = 0;
            isRoundRunning = true;
            pendingClearBonusStages = FindClearBonusStagesOfRound(roundNumber);
            aliveMonsters.Clear();

            StartDueStages();
        }

        /// <summary>
        /// 실행 중 스테이지의 남은 소환과 아직 시작 안 한 스테이지를 모두 취소한다. 이미 나온 몬스터는 그대로 둔다.
        /// 라운드 정리·보스전 전이·비활성화 때 불린다. 스테이지 Finished 는 오지 않는다.
        /// </summary>
        public void StopRound()
        {
            isRoundRunning = false;
            foreach (MonsterWaveStage stage in runningStages) stage.Stop();
            runningStages.Clear();
        }

        /// <summary>
        /// 라운드 시간을 진행하고, 실행 중 스테이지를 Tick 한 뒤 시작 시각이 된 스테이지를 시작한다.
        /// 마감 전 전멸이면 특수 웨이브를 시작하고, 마지막으로 라운드 군중전이 끝났는지 본다.
        /// </summary>
        /// <remarks>
        /// Tick → 시작 순서라서 새 스테이지의 첫 Tick 은 다음 프레임이다.
        /// 종료 판정을 맨 끝에 둬서, 같은 프레임에 시작한 특수 웨이브가 있으면 라운드를 끝내지 않는다.
        /// </remarks>
        private void Update()
        {
            if (isRoundRunning == false) return;

            roundElapsedTime += Time.deltaTime;
            TickRunningStages(Time.deltaTime);
            StartDueStages();
            StartClearBonusIfEarned();
            EndHordePhaseIfAllDone();
        }

        /// <summary>
        /// 실행 중 스테이지에 시간을 넘긴다. 끝난 스테이지는 Finished 신호로 OnStageFinished 가 뺀다.
        /// 뒤에서부터 돌아서, 도중에 목록에서 빠져도 아직 안 돈 앞쪽 인덱스는 그대로다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초)</param>
        private void TickRunningStages(float deltaTime)
        {
            for (int i = runningStages.Count - 1; i >= 0; i--) runningStages[i].Tick(deltaTime);
        }

        /// <summary>
        /// StartTime 이 지난 스테이지를 순서대로 시작한다. 한 프레임에 여러 개가 지났으면 몰아서 시작한다.
        /// </summary>
        private void StartDueStages()
        {
            while (nextStageIndex < roundStages.Count && roundStages[nextStageIndex].StartTime <= roundElapsedTime)
            {
                StartStage(roundStages[nextStageIndex]);
                nextStageIndex++;
            }
        }

        /// <summary>
        /// 마감(ClearBonus.EndTime) 전에 본 웨이브를 다 잡았으면 그 마감의 특수 웨이브를 1번 시작한다.
        /// 전멸 = 소환 중인 스테이지가 없고, 살아 있는 몬스터가 0. 어느 특수 웨이브인지는 FindEarnedClearBonusStage 가 고른다.
        /// 마감을 넘긴 특수 웨이브는 시작하지 않는다 (다음 Normal 스테이지는 원래 StartTime 에 그대로 시작).
        /// </summary>
        private void StartClearBonusIfEarned()
        {
            bool hasNoRunningStage = runningStages.Count == 0;
            bool hasNoAliveMonster = aliveMonsters.Count == 0;
            if (hasNoRunningStage == false) return;
            if (hasNoAliveMonster == false) return;

            WaveStageDef earnedStage = FindEarnedClearBonusStage();
            if (earnedStage == null) return;

            pendingClearBonusStages.Remove(earnedStage);
            Debug.Log($"[MonsterWaveDirector] 라운드 {roundNo} 마감 {earnedStage.EndTime:0}초 전 전멸 → 특수 웨이브", this);
            StartStage(earnedStage);
        }

        /// <summary>
        /// 아직 시작 안 한 ClearBonus 중, 마감 전이고 그 마감 전에 시작할 Normal 스테이지를 다 시작한 것을 찾는다.
        /// 예: 마감 50초 ClearBonus 는 60초 스테이지가 아직이어도 되지만, 0초 스테이지는 시작했어야 한다.
        /// </summary>
        /// <returns>지금 시작할 수 있는 ClearBonus (마감이 이른 것 우선). 없으면 null</returns>
        private WaveStageDef FindEarnedClearBonusStage()
        {
            foreach (WaveStageDef bonusStage in pendingClearBonusStages)
            {
                bool isBeforeDeadline = roundElapsedTime < bonusStage.EndTime;
                bool hasStartedStagesBeforeDeadline = nextStageIndex >= roundStages.Count
                    || roundStages[nextStageIndex].StartTime >= bonusStage.EndTime;
                if (isBeforeDeadline && hasStartedStagesBeforeDeadline) return bonusStage;
            }
            return null;
        }

        /// <summary>
        /// 스테이지 몫의 몬스터를 미리 만든 뒤(같은 프레임) 스테이지 실행을 만들고 Finished 를 구독한다.
        /// </summary>
        /// <param name="stage">시작할 스테이지 행</param>
        private void StartStage(WaveStageDef stage)
        {
            monsterPool.WarmupStage(stage);

            var runningStage = new MonsterWaveStage(stage, monsterPool, ServiceLocator.Get<ISpawnZoneProvider>(), crowdControllerTemplate);
            runningStage.Finished += OnStageFinished;
            runningStages.Add(runningStage);
            Debug.Log($"[MonsterWaveDirector] 라운드 {roundNo} 스테이지 {stage.Id} 시작 (라운드 {roundElapsedTime:0.0}초)", this);
        }

        /// <summary>
        /// 스테이지가 스스로 끝나면 목록에서 뺀다. 스테이지 Tick 안에서 불린다.
        /// 라운드 종료 판정은 여기서 하지 않고 Update 끝(EndHordePhaseIfAllDone)에서 한다.
        /// </summary>
        /// <param name="stage">방금 끝난 스테이지</param>
        private void OnStageFinished(MonsterWaveStage stage)
        {
            runningStages.Remove(stage);
        }

        /// <summary>
        /// 스테이지를 다 시작했고, 실행 중 스테이지가 0개이고, 특수 웨이브를 기다리는 중이 아니면 라운드 진행을 끝낸다.
        /// Update 끝에서 매 프레임 불린다. 끝난 뒤 Update 는 아무것도 하지 않는다.
        /// </summary>
        private void EndHordePhaseIfAllDone()
        {
            bool hasStartedAllStages = nextStageIndex >= roundStages.Count;
            bool hasNoRunningStage = runningStages.Count == 0;
            bool isWaitingClearBonus = IsWaitingClearBonus();
            if (hasStartedAllStages == false) return;
            if (hasNoRunningStage == false) return;
            if (isWaitingClearBonus) return;

            isRoundRunning = false;
            Debug.Log($"[MonsterWaveDirector] 라운드 {roundNo} 군중전 소환 끝", this);
        }

        /// <summary>
        /// 특수 웨이브가 아직 시작될 수 있는지 본다. EndHordePhaseIfAllDone 이 라운드를 끝내도 되는지 판단할 때 쓴다.
        /// </summary>
        /// <returns>아직 시작 안 한 ClearBonus 중 마감 전인 것이 하나라도 있으면 true</returns>
        private bool IsWaitingClearBonus()
        {
            foreach (WaveStageDef bonusStage in pendingClearBonusStages)
            {
                bool isBeforeDeadline = roundElapsedTime < bonusStage.EndTime;
                if (isBeforeDeadline) return true;
            }
            return false;
        }

        /// <summary>
        /// 라운드 번호의 Normal 스테이지를 StartTime 순으로 모은다. ClearBonus 는 FindClearBonusStagesOfRound 가 따로 찾는다.
        /// </summary>
        /// <param name="roundNumber">WaveStageDef.RoundNo 와 비교할 라운드 번호</param>
        /// <returns>그 라운드 Normal 스테이지의 새 목록 (StartTime 순). 없으면 빈 목록</returns>
        private static List<WaveStageDef> FindStagesOfRound(int roundNumber)
        {
            var stagesOfRound = new List<WaveStageDef>();
            foreach (WaveStageDef stage in DataManager.WaveStages.All)
            {
                bool isThisRound = stage.RoundNo == roundNumber;
                bool isNormal = stage.Kind == WaveStageKind.Normal;
                if (isThisRound && isNormal) stagesOfRound.Add(stage);
            }
            stagesOfRound.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            return stagesOfRound;
        }

        /// <summary>
        /// 라운드 번호의 ClearBonus 스테이지를 마감(EndTime) 순으로 모은다. 각각 자기 마감 전 전멸 시 1번씩 시작한다.
        /// </summary>
        /// <param name="roundNumber">WaveStageDef.RoundNo 와 비교할 라운드 번호</param>
        /// <returns>그 라운드 ClearBonus 스테이지의 새 목록 (EndTime 순). 없으면 빈 목록</returns>
        private static List<WaveStageDef> FindClearBonusStagesOfRound(int roundNumber)
        {
            var clearBonusStages = new List<WaveStageDef>();
            foreach (WaveStageDef stage in DataManager.WaveStages.All)
            {
                bool isThisRound = stage.RoundNo == roundNumber;
                bool isClearBonus = stage.Kind == WaveStageKind.ClearBonus;
                if (isThisRound && isClearBonus) clearBonusStages.Add(stage);
            }
            clearBonusStages.Sort((a, b) => a.EndTime.CompareTo(b.EndTime));
            return clearBonusStages;
        }
    }
}
