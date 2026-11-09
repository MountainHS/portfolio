using HelloServer;
using UnityEngine;
using WeGotOne.Fish_Interface;

namespace WeGotOne
{
    // 다리
    public class Bridge : MonoBehaviour
    {
        private void OnCollisionEnter2D(Collision2D other)
        {
            if (RoomClient.Instance.Me.IsHost == false) return;
            if (other.gameObject.tag is "Fish")
            {
                if (other.gameObject.TryGetComponent<FishStateManager>(out var fish) == false)
                {
                    Debug.LogError($"[{gameObject.name}] fish 태그를 가지고 있는데 FishStateManager가 없습니다! 에러 대상: {other.gameObject.name}");
                    return;
                }

                if (fish.IsGrabbed)
                {
                    BridgeManager.Instance.HandleItemPickup(other);
                }
                else
                {
                    Debug.Log($"물고기가 플레이어가 잡지 않았는데 다리에 들어가려고 시도함");
                }
            }

            if (other.gameObject.tag is "Item")
            {
                BridgeManager.Instance.HandleItemPickup(other);
            }
        }

        private void OnCollisionStay2D(Collision2D other)
        {
            if (RoomClient.Instance.Me.IsHost == false) return;
            if (other.gameObject.CompareTag("Player"))
            {
                PlayerController player = other.gameObject.GetComponentInParent<PlayerController>();
                if (player.IsBuild)
                {
                    BridgeManager.Instance.BridgeBuild();
                }
            }
        }

        // private void OnCollisionExit2D(Collision2D other)
        // {
        //     if (RoomClient.Instance.Me.IsHost == false) return;
        //     if (other.gameObject.CompareTag("Player"))
        //     {
        //         isCanBuild = false;
        //     }
        // }
    }
}
