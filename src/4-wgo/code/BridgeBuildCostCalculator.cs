using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WeGotOne
{
    public static class BridgeBuildCostCalculator
    {
        private sealed class CandidateItem
        {
            public int ItemId;
            public int BuildValue;
            public int MaxCount;
        }

        public static bool TryCreateConsumePlan(
            BridgeRef bridgeRef,
            IEnumerable<KeyValuePair<int, int>> inventoryCounts,
            IEnumerable<ItemRef> items,
            out IReadOnlyDictionary<int, int> consumePlan)
        {
            consumePlan = null;

            if (bridgeRef == null ||
                bridgeRef.RequiredValue <= 0 ||
                inventoryCounts == null ||
                items == null)
            {
                return false;
            }

            var itemById = new Dictionary<int, ItemRef>();
            foreach (ItemRef item in items)
            {
                if (item == null || itemById.ContainsKey(item.ItemId))
                {
                    return false;
                }

                itemById.Add(item.ItemId, item);
            }

            var countById = new Dictionary<int, int>();
            foreach (KeyValuePair<int, int> inventoryCount in inventoryCounts)
            {
                if (inventoryCount.Value < 0 || countById.ContainsKey(inventoryCount.Key))
                {
                    return false;
                }

                countById.Add(inventoryCount.Key, inventoryCount.Value);
            }

            var plannedCounts = new Dictionary<int, int>();
            long minimumValue = 0;
            foreach (KeyValuePair<int, int> minimumRequirement in bridgeRef.MinimumRequirements)
            {
                if (minimumRequirement.Value <= 0 ||
                    !countById.TryGetValue(minimumRequirement.Key, out int inventoryCount) ||
                    inventoryCount < minimumRequirement.Value ||
                    !itemById.TryGetValue(minimumRequirement.Key, out ItemRef item) ||
                    item.BuildValue <= 0)
                {
                    return false;
                }

                plannedCounts.Add(minimumRequirement.Key, minimumRequirement.Value);
                minimumValue += (long)item.BuildValue * minimumRequirement.Value;
            }

            long remainingValue = bridgeRef.RequiredValue - minimumValue;
            if (remainingValue <= 0)
            {
                consumePlan = new ReadOnlyDictionary<int, int>(plannedCounts);
                return true;
            }

            var candidates = new List<CandidateItem>();
            int maxBuildValue = 0;
            foreach (KeyValuePair<int, ItemRef> itemPair in itemById)
            {
                ItemRef item = itemPair.Value;
                int plannedCount = plannedCounts.TryGetValue(item.ItemId, out int minimumCount)
                    ? minimumCount
                    : 0;
                int inventoryCount = countById.TryGetValue(item.ItemId, out int count) ? count : 0;
                int availableCount = inventoryCount - plannedCount;

                if (item.BuildValue <= 0 || availableCount <= 0)
                {
                    continue;
                }

                int maxCount = (int)System.Math.Min(
                    availableCount,
                    (remainingValue + item.BuildValue - 1) / item.BuildValue);
                candidates.Add(new CandidateItem
                {
                    ItemId = item.ItemId,
                    BuildValue = item.BuildValue,
                    MaxCount = maxCount,
                });
                maxBuildValue = System.Math.Max(maxBuildValue, item.BuildValue);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort((left, right) =>
            {
                int valueComparison = left.BuildValue.CompareTo(right.BuildValue);
                return valueComparison != 0
                    ? valueComparison
                    : left.ItemId.CompareTo(right.ItemId);
            });

            long maximumSearchValue = remainingValue + maxBuildValue - 1;
            var states = new Dictionary<long, int[]>
            {
                { 0, new int[candidates.Count] },
            };

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                CandidateItem candidate = candidates[candidateIndex];
                var nextStates = new Dictionary<long, int[]>(states);

                foreach (KeyValuePair<long, int[]> state in states)
                {
                    int countLimit = (int)System.Math.Min(
                        candidate.MaxCount,
                        (maximumSearchValue - state.Key) / candidate.BuildValue);

                    for (int count = 1; count <= countLimit; count++)
                    {
                        long nextValue = state.Key + (long)candidate.BuildValue * count;
                        int[] nextCounts = (int[])state.Value.Clone();
                        nextCounts[candidateIndex] = count;

                        if (!nextStates.TryGetValue(nextValue, out int[] existingCounts) ||
                            IsLowerValuePreferred(nextCounts, existingCounts, candidates))
                        {
                            nextStates[nextValue] = nextCounts;
                        }
                    }
                }

                states = nextStates;
            }

            long bestValue = long.MaxValue;
            int[] bestCounts = null;
            foreach (KeyValuePair<long, int[]> state in states)
            {
                if (state.Key >= remainingValue && state.Key < bestValue)
                {
                    bestValue = state.Key;
                    bestCounts = state.Value;
                }
            }

            if (bestCounts == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (bestCounts[i] == 0)
                {
                    continue;
                }

                CandidateItem candidate = candidates[i];
                plannedCounts[candidate.ItemId] = plannedCounts.TryGetValue(candidate.ItemId, out int count)
                    ? count + bestCounts[i]
                    : bestCounts[i];
            }

            consumePlan = new ReadOnlyDictionary<int, int>(plannedCounts);
            return true;
        }

        private static bool IsLowerValuePreferred(
            int[] candidateCounts,
            int[] existingCounts,
            List<CandidateItem> candidates)
        {
            int index = 0;
            while (index < candidates.Count)
            {
                int buildValue = candidates[index].BuildValue;
                long candidateCount = 0;
                long existingCount = 0;

                do
                {
                    candidateCount += candidateCounts[index];
                    existingCount += existingCounts[index];
                    index++;
                }
                while (index < candidates.Count && candidates[index].BuildValue == buildValue);

                if (candidateCount != existingCount)
                {
                    return candidateCount > existingCount;
                }
            }

            return false;
        }
    }
}
