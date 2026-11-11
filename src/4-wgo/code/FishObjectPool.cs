using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeGotOne
{
    public sealed class FishObjectPool : SceneSingleton<FishObjectPool>
    {
        [SerializeField] private WorldItem[] fishPrefabs;

        private readonly Dictionary<int, Queue<WorldItem>> inactiveFishByItemId = new();
        private readonly HashSet<WorldItem> activeFish = new();
        private WorldItemRegistry worldItemRegistry;

        protected override void OnInitialize()
        {
            base.OnInitialize();
        }

        private void Start()
        {
            worldItemRegistry = WorldItemRegistry.Instance;
        }

        public WorldItem Get(int itemId)
        {
            if (inactiveFishByItemId.TryGetValue(itemId, out Queue<WorldItem> inactiveFish))
            {
                while (inactiveFish.Count > 0)
                {
                    WorldItem pooledFish = inactiveFish.Dequeue();
                    if (pooledFish == null)
                        continue;

                    InitializeFish(pooledFish);
                    activeFish.Add(pooledFish);
                    return pooledFish;
                }
            }

            WorldItem prefab = FindFishPrefab(itemId);
            if (prefab == null)
            {
                Debug.LogError($"[ItemObjectPool] ItemId {itemId}에 해당하는 Fish 프리팹을 찾을 수 없습니다.");
                return null;
            }

            WorldItem fish = Instantiate(prefab);
            InitializeFish(fish);
            activeFish.Add(fish);
            return fish;
        }

        public void Release(WorldItem fish)
        {
            if (fish == null)
                return;

            if (fish.TryGetComponent<Fish>(out _) == false)
            {
                Debug.LogWarning($"[ItemObjectPool] Fish가 아닌 WorldItem은 반환할 수 없습니다: {fish.name}");
                return;
            }

            fish.SetIsPickUp(false);
            fish.DeactivateFromSnapshot();

            if (fish.WorldItemId >= 0)
            {
                worldItemRegistry.UnregisterDynamic(fish.WorldItemId, out _);
            }

            activeFish.Remove(fish);

            if (inactiveFishByItemId.TryGetValue(fish.ItemId, out Queue<WorldItem> inactiveFish) == false)
            {
                inactiveFish = new Queue<WorldItem>();
                inactiveFishByItemId.Add(fish.ItemId, inactiveFish);
            }

            inactiveFish.Enqueue(fish);
        }

        public void ReleaseAllActiveFish()
        {
            var fishes = new List<WorldItem>(activeFish);
            foreach (WorldItem fish in fishes)
            {
                Release(fish);
            }
        }

        private WorldItem FindFishPrefab(int itemId)
        {
            foreach (WorldItem prefab in fishPrefabs)
            {
                if (IsFishPrefab(prefab) && prefab.ItemId == itemId)
                    return prefab;
            }
        
            return null;
        }
        
        private bool IsFishPrefab(WorldItem prefab)
        {
            return prefab != null && prefab.TryGetComponent<Fish>(out _);
        }
        
        private void InitializeFish(WorldItem fish)
        {
            fish.ResetTransformSnapshot();
            fish.SetIsPickUp(false);
            fish.gameObject.SetActive(true);
        }
    }
}
