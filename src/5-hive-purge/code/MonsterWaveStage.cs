using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 군중전 스테이지 한 줄(WaveStageDef)의 실행 1회. 스테이지 시작 뒤 경과 시간을 재고, 각 웨이브를 자기 StartTime 에 시작한다.
    /// 소유자(B5)가 스테이지 시작 때 만들어 매 프레임 Tick 으로 시간을 넘기고, Finished 를 받으면 목록에서 뺀다.
    /// </summary>
    /// <remarks>
    /// 스테이지 끝 시각(EndTime)은 모른다. 끝 판단은 소유자 몫이다. Finished 는 "더 소환할 게 없다"는 뜻일 뿐 스테이지 시간이 끝났다는 뜻이 아니다.
    /// 앞 웨이브가 일찍 끝나도 다음 웨이브를 앞당기지 않는다. 웨이브 시작은 StartTime 으로만 정한다.
    /// 스스로 시간을 재지 않는다. Tick 에 Time.deltaTime 을 넘기면 강화 화면(timeScale 0)에서 저절로 멈춘다.
    /// </remarks>
    public sealed class MonsterWaveStage
    {
        private readonly WaveStageDef stage;
        private readonly IMonsterFactory monsterFactory;
        private readonly ISpawnZoneProvider spawnZones;
        private readonly ISpawnGroupFactory spawnGroups;  // Wave에 전달할 묶음 소비자 팩토리

        // ── 진행 상태 ──
        private float elapsedTime;  // 초, 스테이지 시작 기준 (WaveDef.StartTime 과 같은 기준)
        private int nextWaveIndex;  // 다음에 시작할 stage.Waves 인덱스. Waves.Count = 다 시작함
        private readonly List<MonsterWave> runningWaves = new List<MonsterWave>();
        private bool isStopped;

        // Stop 됐거나, 웨이브를 다 시작했고 실행 중 웨이브가 0개면 true. 상태 조회와 자기 Tick 가드용 (부모는 Finished 로 안다)
        public bool IsFinished
        {
            get
            {
                if (isStopped) return true;

                bool hasStartedAllWaves = nextWaveIndex >= stage.Waves.Count;
                bool hasNoRunningWave = runningWaves.Count == 0;
                return hasStartedAllWaves && hasNoRunningWave;
            }
        }

        // 웨이브를 다 시작했고 마지막 웨이브가 끝난 Tick 에서 정확히 한 번 발행. Stop 때는 발행하지 않는다
        public event Action<MonsterWaveStage> Finished;

        /// <summary>
        /// 스테이지 실행을 준비한다. 경과 0 에서 시작하고, StartTime 0 인 웨이브도 첫 Tick 에서 시작한다.
        /// </summary>
        /// <param name="stage">실행할 스테이지 행 (Waves 가 Link 된 것, StartTime 순)</param>
        /// <param name="monsterFactory">웨이브·스포너에 그대로 넘길 팩토리 (보통 ServiceLocator 의 MonsterPool)</param>
        /// <param name="spawnZones">웨이브·스포너에 그대로 넘길 소환 위치 계산 (보통 ServiceLocator 의 RingSpawnZoneProvider)</param>
        /// <param name="crowdControllerTemplate">웨이브에 그대로 넘길 군중 묶음 템플릿. Crowd 줄이 없으면 null 이어도 된다</param>
        public MonsterWaveStage(WaveStageDef stage, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones, CrowdController crowdControllerTemplate = null)
            : this(new CrowdSpawnGroupFactory(crowdControllerTemplate), stage, monsterFactory, spawnZones)
        {
        }

        /// <summary>시간표를 변경하지 않고 Wave의 소환 묶음 소비자를 교체한다.</summary>
        public MonsterWaveStage(ISpawnGroupFactory spawnGroups, WaveStageDef stage, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones)
        {
            this.stage = stage;
            this.monsterFactory = monsterFactory;
            this.spawnZones = spawnZones;
            this.spawnGroups = spawnGroups;
        }

        /// <summary>
        /// 시간을 진행하고, 실행 중 웨이브를 Tick 한 뒤 시작 시각이 된 웨이브를 시작한다. 소유자가 매 프레임 부른다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초). 보통 Time.deltaTime</param>
        /// <remarks>
        /// Tick → 시작 순서라서 새 웨이브의 첫 Tick 은 다음 프레임이다. 목록을 도는 중에 웨이브를 추가하지 않는다.
        /// </remarks>
        public void Tick(float deltaTime)
        {
            if (IsFinished) return;

            elapsedTime += deltaTime;
            TickRunningWaves(deltaTime);
            StartDueWaves();
        }

        /// <summary>
        /// 실행 중 웨이브의 남은 소환과 아직 시작 안 한 웨이브를 모두 취소한다. 이미 나온 몬스터는 그대로 둔다.
        /// 라운드 중단(정리·재시작) 때 소유자가 부른다. 스테이지 EndTime 에는 부르지 않는다 (남은 소환은 끝까지).
        /// </summary>
        public void Stop()
        {
            isStopped = true;
            foreach (MonsterWave wave in runningWaves) wave.Stop();
            runningWaves.Clear();
        }

        /// <summary>
        /// 실행 중 웨이브에 시간을 넘긴다. 끝난 웨이브는 Finished 신호로 OnWaveFinished 가 뺀다.
        /// 뒤에서부터 돌아서, 도중에 목록에서 빠져도 아직 안 돈 앞쪽 인덱스는 그대로다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초)</param>
        private void TickRunningWaves(float deltaTime)
        {
            for (int i = runningWaves.Count - 1; i >= 0; i--) runningWaves[i].Tick(deltaTime);
        }

        /// <summary>
        /// StartTime 이 지난 웨이브를 순서대로 시작한다. 한 프레임에 여러 개가 지났으면 몰아서 시작한다.
        /// </summary>
        private void StartDueWaves()
        {
            while (nextWaveIndex < stage.Waves.Count && stage.Waves[nextWaveIndex].StartTime <= elapsedTime)
            {
                var wave = new MonsterWave(spawnGroups, stage.Waves[nextWaveIndex], monsterFactory, spawnZones);
                wave.Finished += OnWaveFinished;
                runningWaves.Add(wave);
                nextWaveIndex++;
            }
        }

        /// <summary>
        /// 웨이브가 끝나면 목록에서 빼고, 스테이지가 끝났는지 본다. 웨이브 Tick 안에서 불린다.
        /// </summary>
        /// <param name="wave">방금 끝난 웨이브</param>
        private void OnWaveFinished(MonsterWave wave)
        {
            runningWaves.Remove(wave);
            NotifyWaveFinished();
        }

        /// <summary>
        /// 웨이브를 다 시작했고 실행 중 웨이브가 0개면 Finished 를 발행한다.
        /// 마지막 웨이브는 반드시 시작된 뒤에 끝나므로, 마지막으로 끝나는 웨이브의 신호에서 한 번만 발행된다.
        /// </summary>
        private void NotifyWaveFinished()
        {
            bool hasStartedAllWaves = nextWaveIndex >= stage.Waves.Count;
            bool hasNoRunningWave = runningWaves.Count == 0;
            if (hasStartedAllWaves == false) return;
            if (hasNoRunningWave == false) return;

            Finished?.Invoke(this);
        }
    }
}
