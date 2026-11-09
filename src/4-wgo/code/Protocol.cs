using System;
using System.Collections.Generic;

namespace HelloServer
{
    public class User
    {
        public string Id { get; set; }
        public string NickName { get; set; }
        public bool IsHost { get; set; }
        
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
    }

    // 받은 글자가 어떤 종류인지 나타내는 데이터 객체
    // 일반적으로 Header라고 부릅니다.
    
    // 전달받은 데이터의 종류만 먼저 읽고, 알맞은 처리를 합니다.
    public class TypeOnly
    {
        public string Type { get; set; }
    }

    // 플레이어 상태 정보
    public class PlayerState
    {
        public string Id { get; set; }

        public float CurrentHunger { get; set; }

        public PartState Body { get; set; }
        public PartState LeftArmTop { get; set; }
        public PartState RightArmTop { get; set; }
        public PartState LeftArmBottom { get; set; }
        public PartState RightArmBottom { get; set; }
        
        public bool IsLeftGrab { get; set; }
        public bool IsRightGrab { get; set; }
    }

    public class PartState
    {
        public float X { get; set; }
        public float Y { get; set; }
        
        public Rotation Rotation { get; set; }
    }

    public class Rotation
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }

    // 물고기 코드
    public class FishState
    {
        public int WorldItemId { get; set; }
        public int ItemId { get; set; }
        public float X { get; set; }          // 현재 위치 X
        public float Y { get; set; }          // 현재 위치 Y
        public float ScaleX { get; set; }     // 좌우 반전 스케일값 (ApplyingFacing 결과 동기화용)
        public float Angle { get; set; }      // 머리 회전 각도 (발버둥 칠 때 회전값 동기화용)
        public string CurrentState { get; set; }     // 현재 상태 이름 (Idle, Caught, Faint, Runaway 등)
    }
    
    public class PlankState
    {
        public int WorldItemId { get; set; }
        public int ItemId { get; set; }
        public float X { get; set; }          // 현재 위치 X
        public float Y { get; set; }          // 현재 위치 Y
        public float Angle { get; set; }      // 머리 회전 각도 (발버둥 칠 때 회전값 동기화용)
        public bool IsPickUp { get; set; }
    }

    public class InventoryEntryState
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }
    
    #region 클라이언트 -> 서버 (C2S)

    public class HelloMessage
    {
        public string Type { get; set; } = "hello";
        public string NickName { get; set; }
        
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
    }

    public class GuestInputMessage
    {
        public string Type { get; set; } = "input";
        public string Id { get; set; }

        public float X { get; set; }
        public float Y { get; set; }

        public bool IsLeftShiftHold { get; set; }
        public bool IsRightShiftHold { get; set; }
        public bool IsBuildKeyHold { get; set; } // 아직 건설 키 확정이 아니라 명시는 안 함
        public bool IsPressRespawn { get; set; }
    }

    public class GuestInputGroupMessage
    {
        public string Type { get; set; } = "inputGroup";

        public List<GuestInputMessage> Inputs { get; set; } = new();
    }

    // Host가 만든 Physics 상태와 인벤토리, 맵, 게임 상황까지를 Guest에게 전달할 때 사용하는 메시지.
    public class SnapshotMessage
    {
        public string Type { get; set; } = "snapshot";
        
        // 1. 플레이어 물고기 물리 및 위치 상태 
        public PlayerState[] Players { get; set; } // 플레이어 (위치, 무력화 상태)
        public FishState[] Fishes { get; set; }
        public PlankState[] Planks { get; set; }
        
        // 2. 공용 인벤토리 정보
        public InventoryEntryState[] InventoryEntries { get; set; }
        
        // 3. 맵 오브젝트 건설 정보
        public int CurrentBridgeCount { get; set; } // 현재 다리가 몇 개 건설 됐는지
        
        // 4. 게임 라이프 사이클 및 시간 상태
        public bool IsDay { get; set; }      // 흐른 시간 혹은 DayLoop 상태
        public int ActivePlayerCount { get; set; }
        public int ArrivedPlayerCount { get; set; }
        public int TutorialReadyCount { get; set; }
        public int TutorialTotalCount { get; set; }
        public bool IsStartButtonPressed { get; set; }
        public bool IsTutorialEnd { get; set; }
    }
    

    #endregion

    #region 서버 -> 클라이언트 (S2C)

    public class WelcomeMessage
    {
        public string Type { get; set; } = "welcome";

        public string RoomCode { get; set; }

        public User User { get; set; }
        public User[] Users { get; set; }
    }

    public class JoinMessage
    {
        public string Type { get; set; } = "join";
        public User User { get; set; }
    }

    public class LeaveMessage
    {
        public string Type { get; set; } = "leave";
        public string Id { get; set; }
    }

    public class ChatMessage
    {
        public string Type { get; set; } = "chat";
        public string Id { get; set; }
        public string NickName { get; set; }
        public string Text { get; set; }
    }

    #endregion
        
}
