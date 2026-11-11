using System;
using System.Collections;
using System.Collections.Generic;
using HelloServer;
using UnityEngine;
using WeGotOne.Fish_Interface;

namespace WeGotOne
{
    public sealed class WorldItemSnapshotSynchronizer : SceneSingleton<WorldItemSnapshotSynchronizer>
    {
        private WorldItemRegistry worldItemRegistry;

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => RoomClient.Instance != null 
                                             && RoomClient.Instance.Me != null 
                                             && WorldItemRegistry.IsInitialized);

            worldItemRegistry = WorldItemRegistry.Instance;
            if (RoomClient.Instance.Me.IsHost == false)
            {
                LockFixedPlanksForGuest();
            }
        }

        public void PackActiveWorldItems(SnapshotMessage snapshot)
        {
            List<FishState> fishes = new();
            List<PlankState> planks = new();

            // 예외처리 좀 그만해
            // EnsureReferences();
            if (worldItemRegistry != null)
            {
                foreach (WorldItem worldItem in worldItemRegistry.WorldItems.Values)
                {
                    if (worldItem == null || worldItem.gameObject.activeInHierarchy == false)
                        continue;

                    if (worldItem.TryGetComponent<Fish>(out _))
                    {
                        FishStateManager stateManager = worldItem.GetComponent<FishStateManager>();
                        fishes.Add(new FishState
                        {
                            WorldItemId = worldItem.WorldItemId,
                            ItemId = worldItem.ItemId,
                            X = worldItem.transform.position.x,
                            Y = worldItem.transform.position.y,
                            ScaleX = worldItem.transform.localScale.x,
                            Angle = worldItem.transform.eulerAngles.z,
                            CurrentState = stateManager != null ? stateManager.CurrentStateName : "Unknown",
                        });
                    }
                    else if (worldItem.TryGetComponent<Plank>(out _))
                    {
                        planks.Add(new PlankState
                        {
                            WorldItemId = worldItem.WorldItemId,
                            ItemId = worldItem.ItemId,
                            X = worldItem.transform.position.x,
                            Y = worldItem.transform.position.y,
                            Angle = worldItem.transform.eulerAngles.z,
                            IsPickUp = worldItem.IsPickUp,
                        });
                    }
                }
            }

            snapshot.Fishes = fishes.ToArray();
            snapshot.Planks = planks.ToArray();
        }

        public void ApplySnapshot(SnapshotMessage snapshot)
        {
            // EnsureReferences();
            if (FishObjectPool.Instance == null || worldItemRegistry == null)
            {
                Debug.LogError("[WorldItemSnapshotSynchronizer] 동기화에 필요한 참조가 없습니다.");
                return;
            }

            ApplyFishes(snapshot.Fishes ?? Array.Empty<FishState>());
            ApplyPlanks(snapshot.Planks ?? Array.Empty<PlankState>());
        }

        private void LockFixedPlanksForGuest()
        {
            foreach (int worldItemId in worldItemRegistry.FixedWorldItemIds)
            {
                if (worldItemRegistry.TryGetWorldItem(worldItemId, out WorldItem worldItem) &&
                    worldItem.TryGetComponent(out Rigidbody2D rigidbody))
                {
                    rigidbody.bodyType = RigidbodyType2D.Kinematic;
                }
            }
        }

        private void ApplyFishes(FishState[] fishStates)
        {
            HashSet<int> receivedWorldItemIds = new();
            HashSet<int> missingFishWorldItemIds = new HashSet<int>(worldItemRegistry.WorldItems.Keys);
            missingFishWorldItemIds.ExceptWith(worldItemRegistry.FixedWorldItemIds);

            foreach (FishState state in fishStates)
            {
                if (worldItemRegistry.TryGetWorldItem(state.WorldItemId, out WorldItem worldItem) == false)
                {
                    worldItem = FishObjectPool.Instance.Get(state.ItemId);
                    if (worldItem == null)
                        continue;

                    if (worldItemRegistry.RegisterDynamic(worldItem, state.WorldItemId) == false)
                    {
                        FishObjectPool.Instance.Release(worldItem);
                        continue;
                    }
                }

                if (worldItem.TryGetComponent(out Fish fish) == false)
                {
                    Debug.LogError($"[WorldItemSnapshotSynchronizer] Fish WorldItemId가 Fish가 아닙니다: {state.WorldItemId}");
                    continue;
                }

                if (worldItem.ItemId != state.ItemId)
                {
                    Debug.LogError($"[WorldItemSnapshotSynchronizer] Fish ItemId가 일치하지 않습니다. WorldItemId={state.WorldItemId}, Local={worldItem.ItemId}, Remote={state.ItemId}");
                    continue;
                }

                bool wasActive = worldItem.gameObject.activeSelf;
                worldItem.SetIsPickUp(false);
                worldItem.gameObject.SetActive(true);
                LockFishForGuest(fish);
                fish.ApplySnapshot(state, wasActive == false);
                receivedWorldItemIds.Add(state.WorldItemId);
                missingFishWorldItemIds.Remove(state.WorldItemId);
            }

            foreach (int worldItemId in missingFishWorldItemIds)
            {
                if (worldItemRegistry.UnregisterDynamic(worldItemId, out WorldItem worldItem))
                {
                    FishObjectPool.Instance.Release(worldItem);
                }
            }
        }

        private void ApplyPlanks(PlankState[] plankStates)
        {
            HashSet<int> receivedWorldItemIds = new();

            foreach (PlankState state in plankStates)
            {
                if (state == null)
                    continue;

                if (worldItemRegistry.TryGetWorldItem(state.WorldItemId, out WorldItem worldItem) == false ||
                    worldItemRegistry.IsFixedWorldItem(state.WorldItemId) == false)
                {
                    Debug.LogWarning($"[WorldItemSnapshotSynchronizer] 고정 Plank WorldItemId를 찾을 수 없습니다: {state.WorldItemId}");
                    continue;
                }

                if (worldItem.TryGetComponent<Plank>(out Plank plank) == false)
                {
                    Debug.LogWarning($"[WorldItemSnapshotSynchronizer] WorldItemId가 Plank가 아닙니다: {state.WorldItemId}");
                    continue;
                }

                if (worldItem.ItemId != state.ItemId)
                {
                    Debug.LogWarning($"[WorldItemSnapshotSynchronizer] Plank ItemId가 일치하지 않습니다. WorldItemId={state.WorldItemId}, Local={worldItem.ItemId}, Remote={state.ItemId}");
                    continue;
                }

                bool wasActive = worldItem.gameObject.activeSelf;
                worldItem.gameObject.SetActive(true);
                plank.ApplySnapshot(state, wasActive == false);
                receivedWorldItemIds.Add(state.WorldItemId);
            }

            foreach (int worldItemId in worldItemRegistry.FixedWorldItemIds)
            {
                if (receivedWorldItemIds.Contains(worldItemId) == false &&
                    worldItemRegistry.TryGetWorldItem(worldItemId, out WorldItem worldItem))
                {
                    worldItem.DeactivateFromSnapshot();
                }
            }
        }

        private void LockFishForGuest(Fish fish)
        {
            FishMovement movement = fish.GetComponent<FishMovement>();
            if (movement != null)
            {
                movement.LockForGuest();
            }
        }
    }
}
