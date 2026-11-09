using System;
using System.Collections;
using System.Collections.Generic;
using HelloServer;
using UnityEngine;

namespace WeGotOne
{
    public class InventoryItemDB : SceneSingleton<InventoryItemDB>
    {
        private readonly Dictionary<int, int> itemCountDic = new();

        public new bool IsInitialized { get; private set; }
        public event Action<int, int> ItemCountChanged;

        public IEnumerable<KeyValuePair<int, int>> EnumerateCounts()
        {
            return itemCountDic;
        }

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => ItemManager.Instance.IsInitialized);

            foreach (ItemRef item in ItemManager.Instance.Items)
            {
                itemCountDic.Add(item.ItemId, 0);
            }

            IsInitialized = true;
        }

        public ItemRef GetItem(int id)
        {
            return ItemManager.Instance.Get(id);
        }

        public int GetCount(int itemId)
        {
            return itemCountDic.TryGetValue(itemId, out int count) ? count : 0;
        }

        public bool Add(int itemId, int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[InventoryItemDB] 추가 수량은 양수여야 합니다. ItemId = {itemId}, Amount = {amount}");
                return false;
            }

            if (itemCountDic.TryGetValue(itemId, out int currentCount) == false)
            {
                Debug.LogWarning($"[InventoryItemDB] 없는 ItemId에 추가 시도 = {itemId}");
                return false;
            }

            SetCount(itemId, currentCount + amount);
            return true;
        }

        public bool TryConsume(int itemId, int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[InventoryItemDB] 소비 수량은 양수여야 합니다. ItemId = {itemId}, Amount = {amount}");
                return false;
            }

            if (itemCountDic.TryGetValue(itemId, out int currentCount) == false)
            {
                Debug.LogWarning($"[InventoryItemDB] 없는 ItemId에 소비 시도 = {itemId}");
                return false;
            }

            if (currentCount < amount)
            {
                Debug.LogWarning($"[InventoryItemDB] 소비 가능한 수량이 부족합니다. ItemId = {itemId}, Amount = {amount}");
                return false;
            }

            SetCount(itemId, currentCount - amount);
            return true;
        }

        public bool TryConsumeAll(IReadOnlyDictionary<int, int> consumePlan)
        {
            // foreach (KeyValuePair<int, int> consume in consumePlan)
            // {
            //     // consumePlan의 유효여부 검사
            //     if (consume.Value <= 0 ||
            //         itemCountDic.TryGetValue(consume.Key, out int currentCount) == false ||
            //         currentCount < consume.Value)
            //     {
            //         return false;
            //     }
            // }

            foreach (KeyValuePair<int, int> consume in consumePlan)
            {
                SetCount(consume.Key, itemCountDic[consume.Key] - consume.Value);
            }

            return true;
        }

        public void ApplySnapshot(InventoryEntryState[] entries)
        {
            Dictionary<int, int> snapshotCounts = new();

            for (int i = 0; i < entries.Length; ++i)
            {
                InventoryEntryState entry = entries[i];

                if (snapshotCounts.ContainsKey(entry.ItemId))
                {
                    Debug.LogWarning($"[InventoryItemDB] 중복 ItemId 스냅샷 = {entry.ItemId}");
                    continue;
                }

                snapshotCounts.Add(entry.ItemId, entry.Count);
            }

            List<int> itemIds = new(itemCountDic.Keys);
            for (int i = 0; i < itemIds.Count; ++i)
            {
                int itemId = itemIds[i];
                int count = snapshotCounts.TryGetValue(itemId, out int snapshotCount)
                    ? snapshotCount
                    : 0;

                SetCount(itemId, count);
            }
        }

        private void SetCount(int itemId, int newCount)
        {
            if (itemCountDic[itemId] == newCount)
            {
                return;
            }

            itemCountDic[itemId] = newCount;
            ItemCountChanged?.Invoke(itemId, newCount);
        }
    }
}

