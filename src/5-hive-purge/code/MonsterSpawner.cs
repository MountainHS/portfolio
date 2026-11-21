using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Movement;
using UnityEngine;
using Random = UnityEngine.Random;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 스포너 한 줄(WaveSpawnerDef)의 실행 1회. "StartDelay 초 뒤부터 SpawnInterval 간격으로 Monster 를 Count 마리, SpawnPattern 위치에" 소환한다.
    /// 웨이브(B4)가 만들어 매 프레임 Tick 으로 시간을 넘기고, Finished 를 받으면 목록에서 뺀다.
    /// </summary>
    /// <remarks>
    /// 스스로 시간을 재지 않는다. Tick 에 Time.deltaTime 을 넘기면 강화 화면(timeScale 0)에서 저절로 멈춘다.
    /// 소환한 몬스터는 기억하지 않는다. 제거는 MonsterPool.WipeAll·RoundCleanupEvent 몫이다.
    /// 첫 스폰 개체는 소환한 프레임에 StateMachine 이 null 이다 (MonsterPool.Spawn 참고). 그래서 반환값은 Crowd 묶음 배정(MonsterMovement.JoinCrowd)에만 쓴다.
    /// </remarks>
    public sealed class MonsterSpawner
    {
        private const int MaxPoseAttempts = 5;  // 1마리 소환에 위치를 다시 뽑는 최대 횟수

        private readonly WaveSpawnerDef spawner;
        private readonly IMonsterFactory monsterFactory;
        private readonly ISpawnZoneProvider spawnZones;
        private readonly int groupSeed;  // Herd 방향. 실행마다 무작위라 라운드마다 방향이 바뀐다
        private readonly SpawnNest nest;  // Nest·Crowd 패턴일 때만. 같은 웨이브·같은 StartDelay·같은 패턴의 스포너와 공유한다
        private readonly ISpawnGroup spawnGroup;  // Crowd 패턴일 때만. 소환한 몬스터를 이 묶음에 배정한다 (null = 직접 추적)

        // ── 진행 상태 ──
        private float elapsedTime;  // 초, 이 실행이 시작된 뒤 지난 시간
        private int spawnedCount;   // 소환 차례를 처리한 수 (위치를 못 구해 건너뛴 것 포함)
        private bool isStopped;

        // Count 마리를 다 처리했거나 Stop 됐으면 true. 상태 조회와 자기 Tick 가드용 (부모는 Finished 로 안다)
        public bool IsFinished => isStopped || spawnedCount >= spawner.Count;

        // 소환한 몬스터를 배정하는 군중 묶음. null = Crowd 어댑터가 아님. 기존 호출부 호환용
        public CrowdController Crowd => (spawnGroup as CrowdSpawnGroup)?.Crowd;

        public ISpawnGroup SpawnGroup => spawnGroup;

        // 마지막 소환 차례를 처리한 Tick 에서 정확히 한 번 발행. Stop 때는 발행하지 않는다 (멈춘 쪽이 이미 안다)
        public event Action<MonsterSpawner> Finished;

        /// <summary>
        /// 스포너 한 줄의 실행을 준비한다. 첫 소환은 다음 Tick 부터다.
        /// </summary>
        /// <param name="spawner">실행할 스포너 행 (Monster 가 Link 된 것)</param>
        /// <param name="monsterFactory">소환에 쓸 팩토리 (보통 ServiceLocator 의 MonsterPool)</param>
        /// <param name="spawnZones">소환 위치 계산 (보통 ServiceLocator 의 RingSpawnZoneProvider)</param>
        /// <param name="sharedNest">웨이브가 나눠 준 둥지 (Nest·Crowd 패턴만). null 이고 Nest·Crowd 패턴이면 이 스포너 혼자 쓰는 둥지를 만든다 (실험 씬처럼 스포너만 따로 돌릴 때)</param>
        /// <param name="crowd">웨이브가 나눠 준 군중 묶음 (Crowd 패턴만, 다른 패턴은 null)</param>
        /// <exception cref="ArgumentNullException">Crowd 패턴인데 crowd 가 null 일 때</exception>
        public MonsterSpawner(WaveSpawnerDef spawner, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones, SpawnNest sharedNest = null, CrowdController crowd = null)
            : this(CreateCrowdGroup(spawner, crowd), spawner, monsterFactory, spawnZones, sharedNest)
        {
        }

        private static ISpawnGroup CreateCrowdGroup(WaveSpawnerDef spawner, CrowdController crowd)
        {
            if (spawner.SpawnPattern == SpawnPattern.Crowd && crowd == null)
                throw new ArgumentNullException(nameof(crowd), $"스포너 {spawner.Id}: Crowd 패턴은 군중 묶음(CrowdController)이 필요하다");
            return crowd == null ? null : new CrowdSpawnGroup(crowd);
        }

        /// <summary>소환 직후 소비자를 연결한다. 묶음 완료 통지는 소유 Wave가 담당한다.</summary>
        public MonsterSpawner(ISpawnGroup spawnGroup, WaveSpawnerDef spawner, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones, SpawnNest sharedNest = null)
        {
            bool isCrowdPattern = spawner.SpawnPattern == SpawnPattern.Crowd;
            if (isCrowdPattern && spawnGroup == null)
                throw new ArgumentNullException(nameof(spawnGroup));

            this.spawner = spawner;
            this.monsterFactory = monsterFactory;
            this.spawnZones = spawnZones;
            this.spawnGroup = spawnGroup;
            groupSeed = Random.Range(int.MinValue, int.MaxValue);

            bool usesNest = spawner.SpawnPattern == SpawnPattern.Nest || isCrowdPattern;
            bool needsOwnNest = sharedNest == null && usesNest;
            if (needsOwnNest) nest = new SpawnNest(spawnZones);
            else nest = sharedNest;
        }

        /// <summary>
        /// 시간을 진행하고, 소환 시각이 지난 몬스터를 소환한다. 웨이브가 매 프레임 부른다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초). 보통 Time.deltaTime</param>
        public void Tick(float deltaTime)
        {
            if (IsFinished) return;

            elapsedTime += deltaTime;
            SpawnDueMonsters();
            NotifySpawnFinished();
        }

        /// <summary>
        /// 남은 소환을 취소한다. 이미 소환된 몬스터는 그대로 둔다. 스테이지 중단·라운드 종료 때 소유자가 부른다.
        /// </summary>
        public void Stop() => isStopped = true;

        /// <summary>
        /// Count 마리를 다 처리했으면 Finished 를 발행한다. 다음 Tick 부터는 IsFinished 가드로 여기까지 오지 않아 한 번만 발행된다.
        /// </summary>
        private void NotifySpawnFinished()
        {
            if (spawnedCount < spawner.Count) return;

            Finished?.Invoke(this);
        }

        /// <summary>
        /// 소환 시각이 지난 차례를 모두 처리한다. 한 프레임에 여러 차례가 지났으면 몰아서 소환한다 (SpawnInterval 0 = 전부 한 번에).
        /// </summary>
        private void SpawnDueMonsters()
        {
            while (spawnedCount < spawner.Count && elapsedTime >= GetSpawnTime(spawnedCount))
            {
                SpawnOne();
                spawnedCount++;
            }
        }

        /// <summary>
        /// index 번째 몬스터의 소환 시각.
        /// </summary>
        /// <param name="index">소환 차례 (0부터)</param>
        /// <returns>실행 시작 기준 초</returns>
        private float GetSpawnTime(int index) => spawner.StartDelay + index * spawner.SpawnInterval;

        /// <summary>
        /// 1마리 소환. 위치를 MaxPoseAttempts 번까지 다시 뽑고, 다 실패하면 경고를 남기고 이 차례를 건너뛴다.
        /// Crowd 패턴이면 소환한 몬스터를 군중 묶음에 배정한다.
        /// </summary>
        private void SpawnOne()
        {
            for (int attempt = 0; attempt < MaxPoseAttempts; attempt++)
            {
                bool hasPose = TryGetSpawnPose(out Vector3 position, out Quaternion rotation);
                if (hasPose == false) continue;

                GameObject monster = monsterFactory.Spawn(spawner.Monster, position, rotation);
                spawnGroup?.Assign(monster);
                return;
            }

            Debug.LogWarning($"[MonsterSpawner] 스포너 {spawner.Id}: 위치를 {MaxPoseAttempts}번 못 구해 {spawnedCount + 1}번째 몬스터를 건너뜀");
        }

        /// <summary>
        /// 소환 위치를 한 번 뽑는다. Nest·Crowd 면 둥지 주위, 아니면 패턴별 링 포인트.
        /// </summary>
        /// <param name="position">소환 위치</param>
        /// <param name="rotation">소환 회전</param>
        /// <returns>false = 이번 점은 못 씀 (SpawnOne 이 다시 뽑는다)</returns>
        private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (nest != null) return nest.TryGetSpawnPose(out position, out rotation);
            return spawnZones.TryGetSpawnPose(spawner.SpawnPattern, groupSeed, out position, out rotation);
        }
    }
}
