using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeGotOne;

namespace HelloServer
{
    // RoomClient가 서버와의 네트워크 작업을 담당한다.
    // 송신, 수신 등을 모두 처리하는 객체
    // 씬에 종속적이지 않음
    public class RoomClient : SingletonBase<RoomClient>
    {
        [Header("서버")]
        // 웹소켓 주소. http 가 아니라 ws로 시작.
        [SerializeField]
        private string url = "ws://localhost:5000/room";

        // 서버에 알릴 내 이름.
        [SerializeField] private string nickName = "이름없음";

        // 들어갈 방.
        [SerializeField] private string roomCode = "ABCD";
        
        [SerializeField] private Color playerColor = Color.white;

        [Header("장면")]
        // 내가 조종하는 캐릭터. 씬에 미리 놓아 둔다.
        // 8/28 수정 : 예시 플레이어인 LocalPlayer를 제거하고 프로젝트에서 사용하는 플레이어 컨트롤러로 변경
        // 남이 들어올 때마다 이걸로 하나씩 만든다.
        [SerializeField] private PlayerController remotePrefab;
        private PlayerController localPlayer;
        private Image staminaGauge;
        private GameObject up;
        private GameObject down;

        [Header("보내기")]
        // 초당 몇 번 내 위치를 보낼지.
        // 서버가 뿌리는 횟수와는 별개. (그쪽은 appsettings.json)
        // 보내는 쪽이 더 자주 보내도 되지만, 받는 쪽이 그만큼 자주 보게 되지는 않는다.
        [SerializeField]
        private float sendPerSecond = 20f;

        [SerializeField] private Transform spawnPoint;

        private bool isRespawn;
        // 서버가 정해 준 내 정보. welcome 을 받기 전에는 비어 있다.
        public User Me { get; private set; }

        // 화면에 그리는 일은 RoomLobby 가 한다. 여기는 선을 타는 일만 한다.
        public int RemoteCount => remotes.Count;
        public PlayerController Local => localPlayer;

        // 이름표에 표시하는 번호와 같은 입장 순서로 조회합니다 (1부터 시작).
        public PlayerController GetPlayerByNumber(int playerNumber)
        {
            return remotes[$"u{cameraIdList[playerNumber - 1]}"];
        }

        public GuestInputGroupMessage GuestInputGroupMessage { get; private set; }
        public GuestInputMessage GuestInputMessage { get; private set; }
        public SnapshotMessage SnapshotMessage { get; private set; }

        private ClientWebSocket socket;

        // 남들. 번호로 찾는다.
        // private readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();

        // SSG 커스텀 : 플레이어 맞춰서 편집
        private readonly Dictionary<string, PlayerController> remotes = new Dictionary<string, PlayerController>();
        private readonly HashSet<string> arrivedPlayerIds = new HashSet<string>();
        private readonly HashSet<string> respawnRequestingPlayerIds = new HashSet<string>();
        private readonly List<string> cameraIdList = new List<string>();

        // 오간 말. 너무 쌓이지 않게 앞에서부터 버린다.
        private readonly List<string> chatLog = new List<string>();

        // 채팅 정보 필드 폐기
        // private const int ChatKeep = 12;

        // 한 소켓에 동시에 두 번 보내면 안 된다.
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private WorldItemSnapshotSynchronizer worldItemSynchronizer;

        #region unity method

        protected override void OnInitialize()
        {
            base.OnInitialize();
            // true로 해둬야 백그라운드에서도 데이터 수신 가능
            Application.runInBackground = true;
        }

        protected override void OnDestroy()
        {
            socket?.Dispose();
            base.OnDestroy();
        }

        #endregion

        public void Join(string code, string who, Color color)
        {
            if (IsBusy) return;
            
            roomCode = code;
            nickName = who;
            playerColor = color;

            RunAsync().Forget();
        }
        
        public bool IsBusy { get; private set; }

        // 서버가 다듬어 알려 준 방 코드. abcd 로 쳐도 ABCD 가 옴.
        public string RoomCode { get; private set; }

        // 방에서 오간 말. 로비가 그대로 그린다.
        public IReadOnlyList<string> ChatLog => chatLog;
        public PlayerController LocalPlayer => localPlayer;

        public void RequestRespawn()
        {
            isRespawn = true;
        }

        public bool ConsumeLocalRespawnRequest()
        {
            if (isRespawn == false)
            {
                return false;
            }

            isRespawn = false;
            return true;
        }

        // 접속하고, 이름을 알리고, 끊길 때까지 듣는다.
        private async UniTaskVoid RunAsync()
        {
            IsBusy = true;

            socket = new ClientWebSocket();

            // 방 코드는 주소에 실어 보낸다.  ws://localhost:5000/room?code=ABCD
            string full = $"{url}?code={UnityWebRequest.EscapeURL(roomCode)}";
            
            try
            {
                // socket을 통해 해당 주소로 접속한다. UnitTask를 사용한다
                await socket.ConnectAsync(new Uri(full), destroyCancellationToken).AsUniTask();
            }
            catch (Exception e)
            {
                // 서버가 안 켜져 있는 것은 오류가 아니라 흔한 일이다. 수업 중에 반드시 일어난다.
                Debug.LogWarning($"[방] 접속하지 못했다. 서버가 켜져 있는지, 방 코드가 맞는지 확인 : {e.Message}");

                IsBusy = false;
                return;
            }

            Debug.Log($"[방] {full} 에 접속.  : {nickName}");
            await SceneManager.LoadSceneAsync(1).ToUniTask();
            // 첫 마디는 반드시 hello 다. 서버가 이걸 받아야 번호를 줌.
            await SendAsync(new HelloMessage { NickName = nickName , R = playerColor.r, G = playerColor.g, B = playerColor.b});
            // 보내기는 따로 돈다. 듣는 동안에도 계속 보내야함.
            SendLoop().Forget();
            await ReceiveLoopAsync();

            // 여기까지 오면 끊긴 것.
            IsBusy = false;
            Me = null;
        }
        
        #region 듣기

        // 끊길 때까지 계속 듣는다.
        private async UniTask ReceiveLoopAsync()
        {
            byte[] buffer = new byte[4096];
            StringBuilder builder = new StringBuilder();

            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;

                try
                {
                    // .AsUniTask() 를 붙이는 이유
                    // : 이 await 뒤에서 유니티 오브젝트를 만진다.
                    //  붙여 두면 본 스레드로 돌아와서 이어진다. 안 붙이면 다른 스레드일 수 있고,
                    //  거기서 transform 을 만지는 순간 유니티가 예외를 던진다.
                    result = await socket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), destroyCancellationToken)
                        .AsUniTask();
                    // 호스트가 게스트 입력 받아오거나
                    // 게스트가 호스트 스냅샷 받아오거나
                    // 하는 시점을 Update 이전으로 고정
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
                catch (OperationCanceledException)
                {
                    // 이 오브젝트가 사라졌다
                    return;
                }
                catch (WebSocketException)
                {
                    Debug.LogWarning("[방] 연결이 끊겼다");
                    // 나가기 메시지를 받지 못했는데 먼저 예외처리되면 OnLeave 실행해주기
                    // OnLeave(new LeaveMessage { Id = Me.Id });
                    SceneManager.LoadScene(0);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    socket.Dispose();
                    return;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                // 긴 글자는 여러 조각으로 나뉘어 온다. 끝 조각이 올 때까지 이어 붙인다.
                if (result.EndOfMessage == false) continue;

                string json = builder.ToString();
                builder.Clear();

                Handle(json);
            }
        }

        public async UniTask QuitGame()
        {
            if (socket == null)
            {
                return;
            }
            
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Leave Room", CancellationToken.None);
                socket.Dispose();
            }

            ClearInfo();
            IsBusy = false;
        }

        // 받은 글자 하나를 종류에 따라 나눠 준다.
        private void Handle(string json)
        {
            // JsonUtility => Newtonsfot.Json의 Json Utility로 변경됨.

            string type = JsonConvert.DeserializeObject<TypeOnly>(json).Type;

            switch (type)
            {
                case "welcome": OnWelcome(JsonConvert.DeserializeObject<WelcomeMessage>(json)); break;
                case "join": OnJoin(JsonConvert.DeserializeObject<JoinMessage>(json)); break;
                case "leave": OnLeave(JsonConvert.DeserializeObject<LeaveMessage>(json)); break;
                case "inputGroup":
                    OnGuestInputGroup(JsonConvert.DeserializeObject<GuestInputGroupMessage>(json)); break;
                case "snapshot": OnSnapshot(JsonConvert.DeserializeObject<SnapshotMessage>(json)); break;
                default: Debug.Log($"[방] 등록되지 않은 종류(type) : {type}"); break;
            }
        }

        private void OnSnapshot(SnapshotMessage message)
        {
            if (Me.IsHost)
            {
                SnapshotMessage = message;
                return;
            }

            foreach (PlayerState state in message.Players)
            {
                if (state.Id == Me.Id && state.IsRespawnPressed)
                {
                    isRespawn = false;
                }

                if (remotes.TryGetValue(state.Id, out PlayerController remote))
                {
                    remote.ApplySnapshot(state, state.IsLeftGrab, state.IsRightGrab);
                }
            }
            
            // 스냅샷의 다리 개수만큼 게스트의 다리 오브젝트 활성화
            // BridgeManager.Instance.BuildBridgeWithoutCost(message.CurrentBridgeCount);
            BridgeManager.Instance.ApplySnapshot(message.CurrentBridgeCount);
            if (worldItemSynchronizer == null)
            {
                worldItemSynchronizer = WorldItemSnapshotSynchronizer.Instance;
            }
            worldItemSynchronizer.ApplySnapshot(message);
            
            GameManager.Instance.ApplySnapshot(message.ActivePlayerCount, message.ArrivedPlayerCount);
            InventoryItemDB.Instance.ApplySnapshot(message.InventoryEntries);
            if (DayLoop.Instance != null)
            {
                // 앞서 작성한 DayApplyFromNetwork 메서드를 호출해 상태가 다를 때만 연출을 깨웁니다.
                DayLoop.Instance.DayApplyFromNetwork(message.IsDay);
            }
            
            if (TutorialManager.Instance != null)
                TutorialManager.Instance.UpdateReadyState(message.TutorialReadyCount, message.TutorialTotalCount, message.IsStartButtonPressed, message.IsTutorialEnd);
            SnapshotMessage = message;
        }

        private void OnGuestInputGroup(GuestInputGroupMessage message)
        {
            if (Me.IsHost == false)
            {
                Debug.LogWarning("[방] 게스트가 게스트 입력 모음을 받았다. 서버 코드 문제");
                return;
            }

            List<GuestInputMessage> inputs = message.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                GuestInputMessage input = inputs[i];
                if (input == null)
                {
                    continue;
                }
                
                if (input.IsPressRespawn == false)
                {
                    respawnRequestingPlayerIds.Remove(input.Id);
                }

                if (remotes.TryGetValue(input.Id, out PlayerController remote) == false) continue;
                bool isNewRespawnRequest = input.IsPressRespawn && respawnRequestingPlayerIds.Add(input.Id);
                remote.ApplyInput(new Vector2(input.X, input.Y), input.IsLeftShiftHold, input.IsRightShiftHold, input.IsBuildKeyHold, isNewRespawnRequest);
            }

            GuestInputGroupMessage = message;
        }
        
        private void ClearInfo()
        {
            for (int i = 0; i < cameraIdList.Count; i++)
            {
                string id = $"u{cameraIdList[i]}";

                if (remotes.TryGetValue(id, out PlayerController player))
                {
                    if (player != null)
                    {
                        Destroy(player.gameObject);
                    }
                }
            }

            remotes.Clear();
            cameraIdList.Clear();
            arrivedPlayerIds.Clear();
            respawnRequestingPlayerIds.Clear();

            localPlayer = null;
        }

        private void OnWelcome(WelcomeMessage message)
        {
            ClearInfo();
            
            Me = message.User;
            RoomCode = message.RoomCode;
            
            GameManager.Instance.ResetNetworkPlayerCounts();
            arrivedPlayerIds.Clear();

            // 자기 자신 포함 모두 스폰
            // 자기 자신은 입력값을 전달해줘야함
            // 입력값 전달은 호스트 여부에 따라 달라짐
            // 호스트이면 즉시 반영
            // 게스트이면 호스트 스냅샷만 반영 (입력값 비활성화)
            if (Spawn(message.User)) GameManager.Instance.AddPlayer();
            UpdateUserText(true, message.User.Id);
            foreach (User user in message.Users)
            {
                if (Spawn(user)) GameManager.Instance.AddPlayer();
                UpdateUserText(true, user.Id);
            }

            remotes.TryGetValue(Me.Id, out PlayerController localPlayer);

            if (staminaGauge == null)
            {
                staminaGauge = GameObject.Find("Stamina Fill").GetComponent<Image>();
            }
            if (up == null)
            {
                up = GameObject.Find("Stamina Up");
            }
            if (down == null)
            {
                down = GameObject.Find("Stamina Down");
            }
            localPlayer.Body.StaminaGauge = staminaGauge;
            localPlayer.Body.up = up;
            localPlayer.Body.down = down;
            this.localPlayer = localPlayer;

            localPlayer.Setup(Me);
            TutorialManager.Instance.StartTutorial();
        }

        private void OnJoin(JoinMessage message)
        {
            if (Spawn(message.User)) GameManager.Instance.AddPlayer();
            UpdateUserText(true, message.User.Id);
            Debug.Log($"[방] {message.User.NickName}({message.User.Id}) 이(가) 들어왔다");
        }

        private void OnLeave(LeaveMessage message)
        {
            if (remotes.TryGetValue(message.Id, out PlayerController remote) == false) return;

            if (Me.IsHost)
            {
                bool wasArrived = arrivedPlayerIds.Remove(message.Id);
                GameManager.Instance?.RemovePlayer(message.Id, remote.Body.IsStarved, wasArrived);
            }
            else
            {
                GameManager.Instance?.RemovePlayer();
            }

            remotes.Remove(message.Id);
            Destroy(remote.gameObject);
            UpdateUserText(false, message.Id);

            Debug.Log($"[방] {message.Id} 이(가) 나갔다");
        }

        // Spawn, Leave시 플레이어 텍스트 업데이트
        // 상당히 난잡함 (추후 리팩토링 필요)
        private void UpdateUserText(bool isAdd, string userId)
        {
            string id = userId.Substring(1);
            if (isAdd)
            {
                // Spawn될때
                // OnWelcome이나 OnJoin시 스폰되면 Id에 추가
                cameraIdList.Add(id);
            }
            else
            {
                // OnLeave시 플레이어가 나가면 Id에 없애기
                cameraIdList.Remove(id);
            }
            cameraIdList.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b)));
            for (int i = 0; i < cameraIdList.Count; i++)
            {
                string currentUserId = $"u{cameraIdList[i]}";
                PlayerController player = remotes[currentUserId];
                TMP_Text playerText = player.GetComponentInChildren<TMP_Text>();
                playerText.SetText($"{i+1}. {player.User.NickName}");
            }
        }

        public void SetPlayerArrived(string playerId, bool isArrived)
        {
            if (Me == null || Me.IsHost == false) return;

            if (isArrived)
            {
                arrivedPlayerIds.Add(playerId);
            }
            else
            {
                arrivedPlayerIds.Remove(playerId);
            }
        }
        
        public void AllRelease()
        {
            localPlayer.LeftBottomArm.Release();
            localPlayer.RightBottomArm.Release();

            foreach (var player in remotes)
            {
                player.Value.LeftBottomArm.Release();
                player.Value.RightBottomArm.Release();
            }
        }

        private bool Spawn(User user)
        {
            // 이미 있으면 만들지 않는다. welcome 과 join 이 겹칠 수 있다.
            // 호스트 O / Me O
            // InputBranch O
            // 호스트 O / Me X
            // InputBranch X
            // 호스트 X / Me O
            // InputBranch X
            // 호스트 X / Me X
            // InputBranch X
            // if (spawnPoint == null)
            // {
            //     spawnPoint = GameObject.FindGameObjectWithTag("Player Spawn Point").transform;
            // }
            Debug.Log($"[방] {user.NickName}({user.Id}) 이(가) 스폰한다");
            if (remotes.ContainsKey(user.Id)) return false;

            PlayerController remote = Instantiate(remotePrefab, spawnPoint.position, Quaternion.identity);
            if (Me.IsHost == false || (user.IsHost == false && Me.IsHost))
            {
                remote.GetComponentInChildren<PlayerInputBranch>().enabled = false;
            }
            //---몸체랑 리지드바디랑 따로라서 그거 다 긁어오는 코드임
            Rigidbody2D[] rigid = remote.GetComponentsInChildren<Rigidbody2D>();
            for (int i = 0; i < rigid.Length; i++)
            {
                if (Me.IsHost == false)
                {
                    rigid[i].bodyType = RigidbodyType2D.Kinematic;
                }
            }
            //---

            remote.Setup(user);

            remotes[user.Id] = remote;
            return true;
        }

        #endregion

        #region 보내기

        private async UniTaskVoid SendLoop()
        {
            int delay = (int)(1000f / sendPerSecond);
            var token = destroyCancellationToken;

            while (token.IsCancellationRequested == false)
            {
                // Update, FixedUpdate 이후 (게임 로직 처리 이후) 
                // 호스트는 스냅샷
                // 게스트는 게스트 인풋 전송
                // WhenAll로하면 Delay가 나중에 완료되어서 PostLateUpdate 시점에 실행될거라 보장할 수 없음
                await UniTask.Delay(delay, cancellationToken: token);
                await UniTask.Yield(PlayerLoopTiming.LastUpdate);

                // 아직 번호를 못 받았으면 보낼 것이 없다.
                if (Me == null) continue;

                // 호스트일때는 호스트 스냅샷
                await SendSnapshotOrInputMessage();
            }
        }

        private async UniTask SendSnapshotOrInputMessage()
        {
            if (Me.IsHost)
            {
                // 1. 스냅샷 객체 생성
                var snapshot = new SnapshotMessage();

                // 2. 월드 아이템은 Registry 기준으로 활성 객체만 패킹한다.
                if (worldItemSynchronizer == null)
                {
                    worldItemSynchronizer = WorldItemSnapshotSynchronizer.Instance;
                }
                worldItemSynchronizer.PackActiveWorldItems(snapshot);

                // 3. [플레이어 상태 연동]: 나와 다른 게스트 플레이어들의 상태 채우기
                var playerStates = new List<PlayerState>();
        
                // remotes에 자기 자신도 있어서 제거
                // AddPlayerState(playerStates, Me.Id);

                // 접속한 다른 원격 유저(게스트)들의 현재 씬 위치 추가
                foreach (var pair in remotes)
                {
                    if (pair.Value != null)
                    {
                        AddPlayerState(playerStates, pair.Key);
                    }
                }
                
                snapshot.Players = playerStates.ToArray();

                // bool hostHolding = localPlayer.IsBuild;
                // int guestHoldingCount = 0;
                // if (GuestInputGroupMessage != null)
                // {
                //     foreach (var input in GuestInputGroupMessage.Inputs)
                //     {
                //         if (input != null && input.IsBuildKeyHold) guestHoldingCount++;
                //     }
                // }
                // snapshot.TutorialReadyCount = (hostHolding ? 1 : 0) + guestHoldingCount;
                snapshot.TutorialReadyCount = TutorialManager.Instance.ReadyCount;
                snapshot.TutorialTotalCount = RemoteCount;
                snapshot.IsStartButtonPressed = TutorialManager.Instance.IsStartButtonPressed;
                snapshot.IsTutorialEnd = TutorialManager.Instance.IsTutorialEnd;

                snapshot.Players = playerStates.ToArray();
                if (DayLoop.Instance != null)
                {
                    snapshot.IsDay = (DayLoop.Instance.CurrentDayType == DayLoop.DayType.Day);
                }

                List<InventoryEntryState> inventoryEntries = new();
                foreach (KeyValuePair<int, int> pair in InventoryItemDB.Instance.EnumerateCounts())
                {
                    inventoryEntries.Add(new InventoryEntryState
                    {
                        ItemId = pair.Key,
                        Count = pair.Value,
                    });
                }

                snapshot.InventoryEntries = inventoryEntries.ToArray();

                snapshot.CurrentBridgeCount = BridgeManager.Instance.CurrentBridgeCount;
                snapshot.ActivePlayerCount = GameManager.Instance.ActivePlayerCount;
                snapshot.ArrivedPlayerCount = GameManager.Instance.ArrivedPlayerCount;

                // 5. 서버로 패킷 전송
                SnapshotMessage = snapshot;
                await SendAsync(snapshot);
            }
            else
            {
                // 게스트는 자신의 키보드 입력을 전송
                Vector2 input = SimpleInput.GetMoveAxisRaw();
                Keyboard keyboard = Keyboard.current;
                Mouse mouse = Mouse.current;
                bool isPressRespawn = isRespawn;
                await SendInputMessages(input, mouse.leftButton.isPressed, mouse.rightButton.isPressed,
                    keyboard.fKey.isPressed, isPressRespawn);

                if (isPressRespawn)
                {
                    isRespawn = false;
                }
            }
        }


        private async UniTask SendInputMessages(Vector2 input, bool leftGrab, bool rightGrab, bool buildKey, bool  pressRespawn)
        {
            // 플레이어 스스로의 입력 넣기
            GuestInputMessage = new GuestInputMessage
            {
                Id = Me.Id,
                X = input.x,
                Y = input.y,
                IsLeftShiftHold = leftGrab,
                IsRightShiftHold = rightGrab,
                IsBuildKeyHold = buildKey,
                IsPressRespawn = pressRespawn,
            };
            await SendAsync(GuestInputMessage);
        }

        public void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            SendAsync(new ChatMessage { Text = text.Trim() }).Forget();
        }

        // 한 마디를 보낸다. 보내는 곳이 둘(SendLoop, Say)이라 lock을 걸어서 한 번에 하나씩 내보낸다.
        private async UniTask SendAsync(object message)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;

            // JsonUtility => Newtonsfot.Json의 Json Utility로 변경됨.
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            // 아래 라인은 바꾸기 전 코드
            //byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));

            await sendLock.WaitAsync();

            try
            {
                await socket
                    .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, destroyCancellationToken)
                    .AsUniTask();
            }
            catch (OperationCanceledException)
            {
                // 이 오브젝트가 사라졌다.
            }
            catch (WebSocketException)
            {
                // 보내는 순간 끊겼다. 듣는 쪽 루프가 알아서 빠져나온다.
            }
            finally
            {
                sendLock.Release();
            }
        }

        #endregion

        private static Color ColorOf(string id)
        {
            int hash = 0;

            foreach (char c in id) hash = hash * 31 + c;

            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.65f, 0.95f);
        }

        private void AddPlayerState(List<PlayerState> playerStates, string playerId)
        {
            // 내(호스트) 위치 정보 추가
            playerStates.Add(new PlayerState
            {
                Id = playerId,
                CurrentHunger = remotes[playerId].Body.CurrentStamina,
                IsRespawnPressed = remotes[playerId].IsRespawning,
                Body = new PartState
                {
                    X = remotes[playerId].Body.transform.position.x,
                    Y = remotes[playerId].Body.transform.position.y,
                    Rotation = new Rotation()
                    {
                        X = remotes[playerId].Body.transform.rotation.x,
                        Y = remotes[playerId].Body.transform.rotation.y,
                        Z = remotes[playerId].Body.transform.rotation.z,
                        W = remotes[playerId].Body.transform.rotation.w,
                    }
                },
                LeftArmTop = new PartState
                {
                    X = remotes[playerId].LeftUpperArm.transform.position.x,
                    Y = remotes[playerId].LeftUpperArm.transform.position.y,
                    Rotation = new Rotation()
                    {
                        X = remotes[playerId].LeftUpperArm.transform.rotation.x,
                        Y = remotes[playerId].LeftUpperArm.transform.rotation.y,
                        Z = remotes[playerId].LeftUpperArm.transform.rotation.z,
                        W = remotes[playerId].LeftUpperArm.transform.rotation.w,
                    }
                },
                RightArmTop = new PartState
                {
                    X = remotes[playerId].RightUpperArm.transform.position.x,
                    Y = remotes[playerId].RightUpperArm.transform.position.y,
                    Rotation = new Rotation()
                    {
                        X = remotes[playerId].RightUpperArm.transform.rotation.x,
                        Y = remotes[playerId].RightUpperArm.transform.rotation.y,
                        Z = remotes[playerId].RightUpperArm.transform.rotation.z,
                        W = remotes[playerId].RightUpperArm.transform.rotation.w,
                    }
                },
                LeftArmBottom = new PartState
                {
                    X = remotes[playerId].LeftBottomArm.transform.position.x,
                    Y = remotes[playerId].LeftBottomArm.transform.position.y,
                    Rotation = new Rotation()
                    {
                        X = remotes[playerId].LeftBottomArm.transform.rotation.x,
                        Y = remotes[playerId].LeftBottomArm.transform.rotation.y,
                        Z = remotes[playerId].LeftBottomArm.transform.rotation.z,
                        W = remotes[playerId].LeftBottomArm.transform.rotation.w,
                    }
                },
                RightArmBottom = new PartState
                {
                    X = remotes[playerId].RightBottomArm.transform.position.x,
                    Y = remotes[playerId].RightBottomArm.transform.position.y,
                    Rotation = new Rotation()
                    {
                        X = remotes[playerId].RightBottomArm.transform.rotation.x,
                        Y = remotes[playerId].RightBottomArm.transform.rotation.y,
                        Z = remotes[playerId].RightBottomArm.transform.rotation.z,
                        W = remotes[playerId].RightBottomArm.transform.rotation.w,
                    }
                },
                IsLeftGrab = remotes[playerId].IsLeftGrab,
                IsRightGrab = remotes[playerId].IsRightGrab,
            });
        }

        private string GetJson(object message)
        {
            return JsonConvert.SerializeObject(message, Formatting.Indented);       
        }
        
        [ContextMenu("Debug Snapshot")]
        private void DebugSnapshot()
        {
            Debug.Log(
                JsonConvert.SerializeObject(
                    SnapshotMessage,
                    Formatting.Indented
                )
            );
        }

        [ContextMenu("Debug InputGroup")]
        private void DebugInputGroup()
        {
            Debug.Log(
                JsonConvert.SerializeObject(
                    GuestInputGroupMessage,
                    Formatting.Indented
            ));
        }

        [ContextMenu("Debug GuestInput")]
        private void DebugGuestInput()
        {
            Debug.Log(
                JsonConvert.SerializeObject(
                    GuestInputMessage,
                    Formatting.Indented
                )
            );       
        }
    }
}
