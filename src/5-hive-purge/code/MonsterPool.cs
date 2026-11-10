using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 라운드 군중전 몬스터의 오브젝트 풀 (씬에 1개). 켜져 있는 동안 IMonsterFactory 로 등록된다.
    /// 소환은 미리 만들어 둔 같은 종류 개체를 꺼내 쓰고, MonsterController.Despawn 이 부르는 반납은 개체를 끄고 종류 루트에 모아 둔다.
    /// </summary>
    /// <remarks>
    /// 쉬는 개체는 몬스터 종류(MonsterDef.Id)마다 따로 둔다. 한 종류의 개체를 다른 종류로 바꿔 쓰지 않는다.
    /// 쉬는 개체는 두 갈래다. Unspawned = 한 번도 안 켜진 개체 (초기화는 Unity Awake/Start 에 맡긴다),
    /// Returned = 반납된 개체 (꺼낼 때 Respawn 으로 되살린다).
    /// 보스는 다루지 않는다. 만든 개체는 지우지 않는다.
    /// </remarks>
    public sealed class MonsterPool : MonoBehaviour, IMonsterFactory
    {
        // ── Warmup 목표 ──
        private const int MinWarmupCount = 10;   // 종류마다 최소로 쉬게 둘 개체 수 (마리)
        private const float WarmupRatio = 0.5f;  // 스테이지 예상 스폰 수 중 미리 만들 비율 (0.5 = 50%)

        // ── 풀 ──
        // 몬스터 종류(MonsterDef.Id) → 그 종류의 쉬는 개체들
        private readonly Dictionary<int, IdleMonsters> idleMonstersById = new Dictionary<int, IdleMonsters>();

        /// <summary>
        /// 한 종류의 쉬는 개체 묶음. Root(꺼 둔 종류 루트) 아래에 모여 있다.
        /// </summary>
        private sealed class IdleMonsters
        {
            public Transform Root;  // 꺼 둔 종류 루트. 풀 오브젝트의 자식
            public readonly Stack<MonsterController> Unspawned = new Stack<MonsterController>();  // 한 번도 안 켜진 개체
            public readonly Stack<MonsterController> Returned = new Stack<MonsterController>();  // 반납된 개체
            public int Count => Unspawned.Count + Returned.Count;
        }

        /// <summary>몬스터 팩토리로 등록한다. 이후 MonsterController.Despawn 이 이 풀로 반납한다</summary>
        private void OnEnable() => ServiceLocator.Register<IMonsterFactory>(this);

        /// <summary>몬스터 팩토리 등록을 해제한다</summary>
        private void OnDisable() => ServiceLocator.Unregister<IMonsterFactory>(this);

        /// <summary>
        /// 씬 시작 준비: 씬에 꺼진 채 놓인 몬스터를 등록하고, 웨이브에 나오는 종류마다 쉬는 개체를 채운다.
        /// </summary>
        private void Start()
        {
            RegisterSceneMonsters();
            WarmupSpawnerMonsters();
        }

        /// <summary>
        /// 씬의 MonsterController 중 꺼진 개체를 MonsterId 종류의 첫 스폰 대상으로 등록한다.
        /// 켜진 개체는 등록하지 않는다. 그 개체는 나중에 반납될 때 이 풀에 들어온다.
        /// </summary>
        /// <remarks>
        /// 꺼진 개체는 Awake 가 안 돌아 Def 가 없으므로 직렬화된 MonsterId 로 종류를 정한다.
        /// 꺼진 종류 루트 아래로 옮긴 뒤 켜 두면, 첫 스폰 때 씬 루트로 옮기는 순간 처음 켜진다.
        /// </remarks>
        private void RegisterSceneMonsters()
        {
            MonsterController[] sceneMonsters = FindObjectsByType<MonsterController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonsterController monster in sceneMonsters)
            {
                if (monster.gameObject.activeInHierarchy) continue;

                IdleMonsters idleMonsters = GetIdleMonsters(monster.MonsterId);
                monster.transform.SetParent(idleMonsters.Root);
                monster.gameObject.SetActive(true);
                idleMonsters.Unspawned.Push(monster);
            }
        }

        /// <summary>
        /// WaveSpawnerTable 에 나오는 몬스터 종류마다 쉬는 개체가 MinWarmupCount 가 되도록 만든다. 라운드는 가리지 않는다.
        /// 스테이지별 50% 목표는 스테이지 시작 때 WarmupStage 가 채운다.
        /// </summary>
        private void WarmupSpawnerMonsters()
        {
            foreach (WaveSpawnerDef spawner in DataManager.WaveSpawners.All)
            {
                IdleMonsters idleMonsters = GetIdleMonsters(spawner.Monster.Id);
                while (idleMonsters.Count < MinWarmupCount) CreateUnspawned(spawner.Monster);
            }
        }

        /// <summary>
        /// 스테이지 시작 때 그 스테이지 몫을 미리 만든다. 쉬는 개체가 목표보다 적은 종류만 부족분만큼 만든다.
        /// 호출자: 웨이브 진행(B5). B2 에서는 MonsterPoolLab.
        /// </summary>
        /// <param name="stage">시작할 군중전 스테이지. Waves → Spawners 의 몬스터를 센다</param>
        public void WarmupStage(WaveStageDef stage)
        {
            Dictionary<int, int> spawnCountById = CountSpawnsByMonster(stage);
            foreach (KeyValuePair<int, int> pair in spawnCountById)
            {
                MonsterDef def = DataManager.Monsters.Get(pair.Key);
                int warmupTarget = GetWarmupTarget(pair.Value);
                IdleMonsters idleMonsters = GetIdleMonsters(def.Id);
                while (idleMonsters.Count < warmupTarget) CreateUnspawned(def);
            }
        }

        /// <summary>
        /// 스테이지의 예상 스폰 수를 몬스터 종류별로 센다.
        /// </summary>
        /// <param name="stage">셀 스테이지</param>
        /// <returns>MonsterDef.Id → 그 종류를 내는 WaveSpawnerDef.Count 합 (마리). 스테이지에 없는 종류는 키가 없다</returns>
        private static Dictionary<int, int> CountSpawnsByMonster(WaveStageDef stage)
        {
            var spawnCountById = new Dictionary<int, int>();
            foreach (WaveDef wave in stage.Waves)
            {
                foreach (WaveSpawnerDef spawner in wave.Spawners)
                {
                    spawnCountById.TryGetValue(spawner.MonsterId, out int countSoFar);
                    spawnCountById[spawner.MonsterId] = countSoFar + spawner.Count;
                }
            }
            return spawnCountById;
        }

        /// <summary>
        /// 한 종류의 Warmup 목표 수 = max(MinWarmupCount, 올림(예상 스폰 수 × WarmupRatio)).
        /// </summary>
        /// <param name="expectedSpawnCount">스테이지에서 그 종류의 예상 스폰 수 (마리)</param>
        /// <returns>쉬게 둘 개체 수 (마리)</returns>
        private static int GetWarmupTarget(int expectedSpawnCount) =>
            Mathf.Max(MinWarmupCount, Mathf.CeilToInt(expectedSpawnCount * WarmupRatio));

        /// <summary>
        /// 꺼 둔 종류 루트 아래에 개체 1개를 만들고 첫 스폰 스택에 넣는다.
        /// 부모가 꺼져 있어 Awake·OnEnable·Start 는 첫 스폰 때 처음 돈다.
        /// </summary>
        /// <param name="def">만들 몬스터 Def. Prefab 을 쓴다</param>
        private void CreateUnspawned(MonsterDef def)
        {
            IdleMonsters idleMonsters = GetIdleMonsters(def.Id);
            GameObject instance = Instantiate(def.Prefab, idleMonsters.Root);
            idleMonsters.Unspawned.Push(instance.GetComponent<MonsterController>());
        }

        /// <summary>
        /// 종류의 쉬는 개체 묶음을 돌려준다. 처음 쓰는 종류면 꺼 둔 종류 루트를 풀 아래에 만든다.
        /// </summary>
        /// <param name="monsterId">MonsterDef.Id</param>
        /// <returns>그 종류의 쉬는 개체 묶음</returns>
        private IdleMonsters GetIdleMonsters(int monsterId)
        {
            if (idleMonstersById.TryGetValue(monsterId, out IdleMonsters idleMonsters)) return idleMonsters;

            var root = new GameObject($"Idle {monsterId}");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            idleMonsters = new IdleMonsters { Root = root.transform };
            idleMonstersById.Add(monsterId, idleMonsters);
            return idleMonsters;
        }

        // ════════ IMonsterFactory ════════

        /// <summary>
        /// 같은 종류의 쉬는 개체를 꺼내 지정 위치에 소환하고 MonsterSpawnedEvent 를 발행한다. 쉬는 개체가 없으면 1개 만든다.
        /// 반납됐던 개체는 Respawn 으로 되살리고, 한 번도 안 켜진 개체는 켜기만 한다 (조립은 다음 프레임 Start).
        /// </summary>
        /// <param name="def">소환할 몬스터 Def</param>
        /// <param name="position">소환 월드 위치</param>
        /// <param name="rotation">소환 회전</param>
        /// <returns>소환된 몬스터 GameObject</returns>
        /// <remarks>
        /// 첫 스폰 개체는 소환한 프레임에 StateMachine 이 null 이고 게임 이벤트도 아직 구독하지 않았다.
        /// 호출자는 소환한 프레임에 StateMachine 을 만지지 않는다.
        /// </remarks>
        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation)
        {
            IdleMonsters idleMonsters = GetIdleMonsters(def.Id);
            if (idleMonsters.Count == 0) CreateUnspawned(def);

            MonsterController monster;
            if (idleMonsters.Returned.Count > 0)
            {
                // 꺼진 채 옮긴 뒤 켜야 NavMeshAgent 가 새 위치에서 시작한다
                monster = idleMonsters.Returned.Pop();
                monster.transform.SetPositionAndRotation(position, rotation);
                monster.transform.SetParent(null);
                monster.gameObject.SetActive(true);
                monster.Respawn();
            }
            else
            {
                // 위치를 먼저 바꾼다. 씬 루트로 옮기는 순간 처음 켜지며 Awake·OnEnable 이 돈다
                monster = idleMonsters.Unspawned.Pop();
                monster.transform.SetPositionAndRotation(position, rotation);
                monster.transform.SetParent(null);
            }

            EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(monster.Def, monster.gameObject));
            return monster.gameObject;
        }

        /// <summary>
        /// MonsterController.Despawn 이 호출한다. 개체를 끄고 군중 묶음 배정을 지운 뒤 Def.Id 종류 루트로 옮겨 반납 스택에 넣는다.
        /// 이 풀이 만들지 않은 몬스터도 같은 방법으로 받는다.
        /// </summary>
        /// <param name="monster">반납할 몬스터 GameObject (루트에 MonsterController)</param>
        public void Despawn(GameObject monster)
        {
            var controller = monster.GetComponent<MonsterController>();
            monster.SetActive(false);
            monster.GetComponent<MonsterMovement>().ClearCrowdAssignment(); // 재사용 때 옛 묶음에 다시 들어가지 않게
            IdleMonsters idleMonsters = GetIdleMonsters(controller.Def.Id);
            monster.transform.SetParent(idleMonsters.Root);
            idleMonsters.Returned.Push(controller);
        }

        /// <summary>
        /// 살아 있는 몬스터 전부에게 보상 없는 사망을 알린다 (MonsterWipeRequestedEvent).
        /// 각 몬스터는 사망 연출(despawnDelay) 뒤 Despawn 으로 이 풀에 반납된다.
        /// </summary>
        /// <remarks>소환한 그 프레임의 첫 스폰 개체는 아직 구독 전이라 빠진다.</remarks>
        public void WipeAll() =>
            EventBus<MonsterWipeRequestedEvent>.Publish(new MonsterWipeRequestedEvent(transform.position, false));
    }
}
