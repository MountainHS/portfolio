using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeGotOne
{
    public class WorldItemRegistry: SceneSingleton<WorldItemRegistry>
    {
        private const string PlankGroupName = "Plank Group";

        private readonly Dictionary<int, WorldItem> worldItems = new();
        private readonly HashSet<int> fixedWorldItemIds = new();
        private int nextDynamicWorldItemId;

        public IReadOnlyDictionary<int, WorldItem> WorldItems => worldItems;
        public IEnumerable<int> FixedWorldItemIds => fixedWorldItemIds;

        protected override void Awake()
        {
            base.Awake();
            RegisterFixedPlanks();
        }

        public bool TryGetWorldItem(int worldItemId, out WorldItem worldItem)
        {
            return worldItems.TryGetValue(worldItemId, out worldItem);
        }

        public bool IsFixedWorldItem(int worldItemId)
        {
            return fixedWorldItemIds.Contains(worldItemId);
        }

        public bool RegisterNewDynamic(WorldItem worldItem, out int worldItemId)
        {
            while (worldItems.ContainsKey(nextDynamicWorldItemId))
            {
                nextDynamicWorldItemId++;
            }

            worldItemId = nextDynamicWorldItemId;
            return RegisterDynamic(worldItem, worldItemId);
        }

        public bool RegisterDynamic(WorldItem worldItem, int worldItemId)
        {
            if (worldItems.TryGetValue(worldItemId, out WorldItem existing) && existing != worldItem)
            {
                Debug.LogError($"[WorldItemRegistry] WorldItemId가 이미 등록되어 있습니다: {worldItemId}");
                return false;
            }

            worldItems[worldItemId] = worldItem;
            worldItem.AssignWorldItemId(worldItemId);
            nextDynamicWorldItemId = Mathf.Max(nextDynamicWorldItemId, worldItemId + 1);
            return true;
        }

        public bool UnregisterDynamic(int worldItemId, out WorldItem worldItem)
        {
            worldItem = null;

            if (worldItems.TryGetValue(worldItemId, out worldItem) == false)
                return false;

            worldItems.Remove(worldItemId);
            worldItem.AssignWorldItemId(-1);
            return true;
        }

        private void RegisterFixedPlanks()
        {
            GameObject plankGroup = GameObject.Find(PlankGroupName);
            if (plankGroup == null)
            {
                Debug.LogError($"[WorldItemRegistry] '{PlankGroupName}'을 찾을 수 없습니다.");
                return;
            }

            int nextFixedWorldItemId = 0;
            RegisterFixedPlanksRecursively(plankGroup.transform, ref nextFixedWorldItemId);
            nextDynamicWorldItemId = nextFixedWorldItemId;
        }

        private void RegisterFixedPlanksRecursively(Transform parent, ref int nextFixedWorldItemId)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.TryGetComponent<Plank>(out _) && child.TryGetComponent(out WorldItem worldItem))
                {
                    int worldItemId = nextFixedWorldItemId++;
                    worldItems.Add(worldItemId, worldItem);
                    fixedWorldItemIds.Add(worldItemId);
                    worldItem.AssignWorldItemId(worldItemId);
                }
                else if (child.TryGetComponent<Plank>(out _))
                {
                    Debug.LogError($"[WorldItemRegistry] 고정 Plank에 WorldItem이 없습니다: {child.name}");
                    nextFixedWorldItemId++;
                }
                RegisterFixedPlanksRecursively(child, ref nextFixedWorldItemId);
            }
        }
    }
}
