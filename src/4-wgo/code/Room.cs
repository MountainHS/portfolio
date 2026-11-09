using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace HelloServer;

// 방 하나. 방에 있는 사람들을 들고 있다가
// 메세지를 전달해 준다.
// 방의 기능은 아래와 같습니다
// 1. 들어온다 : JoinAsync
// 2. 말한다   : ReceiveLoopAsync => 결국 요게 채팅임
// 3. 나간다   : LeaveAsync
// 4. 뿌린다   : BroadcastStateAsync

// Room은 메시지를 무리는 법만 알고
// 언제 뿌릴지는 결정하지 않습니다. 

public class Room
{
    private const string HandleLog = "[Handle]";
    private const string StateTickLog = "[StateTick]";
    private const string InputTickLog = "[InputTick]";
    private const int SendTimeoutMilliseconds = 9000;

    // 접속자 한 명.
    public class Member
    {
        public User User;
        public WebSocket Socket;
        
        // DateTime?
        // : 날짜랑 시간을 표현하고 조작할 때 사용하는 구조체 입니다.
        //  DateTime.Now : 현재 지역 시간을 나타낼 수 있ㅅ브니다
        //  DateTime.UtcNow : 협정 세계시(영국 본초 자오선(?))
        // 출력 서식을 따로 지정할 수 있습니다. 그거는 MS 홈페이지 가서 보세요
        // 로그 전용
        public DateTime LastLogAt;
        public DateTime LastInputLogAt;
        public DateTime LastRejectedSnapshotLogAt;
        public int RejectedSnapshotsSinceLog;
        public int InputsSinceLog;

        public GuestInputMessage LastInput;
        public int SendFailed;
        
        // 보낼때 여러메시지를 동시에 보내지 않기 위에
        // 사람(멤버)마다 Gate를 하나씩 두고 한번에 하나씩 보내기 위해
        // 사용하는 클래스. (비동기에서 lock처리가 안되서 사용)
        // 읽는것은 여러 쓰레드에서 읽을 수 있는데 사용(Write)는
        // 하나의 쓰레드에서만 온전히 돌아갈 수 있도록 하게 해주는 클래스
        public readonly SemaphoreSlim SendLock 
            = new SemaphoreSlim(1, 1);
        
        public readonly Channel<BroadcastWorkItem> LatestSnapshotChannel =
            Channel.CreateBounded<BroadcastWorkItem>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
    }

    public readonly record struct BroadcastWorkItem(object Message, string ExceptId);
    
    // race condition이 일어나도 여러 쓰레드에서 동시적으로
    // 참조 하여 읽을 수 있는 딕셔너리 입니다. 일반적인 Dictionary를 쓰면
    // race condition이 발생하면 깨질꺼에여.
    // 여러 쓰레드에서 동시에 사용하더라도 딕셔너리의 한 상태를 유지 시킬 수 있는
    // 안정성이 보장된 딕셔너리 입니다.
    private readonly ConcurrentDictionary<string, Member> members = new();

    // 들어오고 나가는 메시지 처리(일)을 한줄로 세우는 자물쇠 입니다.
    // lock블록이 await가 안먹어서 사용합니다.
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    // 호스트가 나가 방이 만료됐을 때, 대기 중인 모든 수신을 깨운다.
    private readonly CancellationTokenSource roomExpiredCancellation = new();
    private readonly string code; // 방번호
    private readonly int logMovesPerSecond; // 룸허브를 통해서 전달 받습니다. 

    private int inputGroupsSinceLog;
    private DateTime lastInputGroupLogAt;
    private int snapshotsSinceLog;
    private DateTime lastSnapshotLogAt;
    private int stateBroadcastsSinceLog;
    private DateTime lastStateBroadcastLogAt;
    private SnapshotMessage lastSnapshot;
    private volatile bool roomExpired;

    public bool IsEmpty => members.IsEmpty;
    
    public Room(string code, int logMovesPerSecond)
    {
        this.code = code;
        this.logMovesPerSecond = logMovesPerSecond;
        // room 단위 broadcastLoop를 member 단위로 쪼개는 거임
        // broadcastLoop = BroadcastQueueLoopAsync();
    }

    public void StopWriteToAllChannel()
    {
        foreach (Member member in members.Values)
        {
            member.LatestSnapshotChannel.Writer.TryComplete();
        }
    }

    // 현재 방의 Host를 찾는다.
    private Member GetHost()
    {
        foreach (Member member in members.Values)
        {
            if (member.User.IsHost) return member;
        }

        return null;
    }

    #region 듣기

    // 글자를 받는다. 상대가 연결을 닫았으면 nulll을 돌려준다
    // PS
    // : 긴글자일 경우 가끔 조각으로 나뉘어서 오는 경우가 있다.
    //  우리가 StreamReader를 다뤘을때 처럼 메시지의 끝 EndOf~~
    //  을 정확하게 파악하여 메시시 한 단위를 만들어 준다.
    private static async Task<string> ReceiveTextAsync
        (WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        StringBuilder builder = new StringBuilder();
        // StringBuilder?
        // : 여러 문자들을 이어붙힐때 사용하는 객체. string은 각각 개별로
        // 생성되는 별도의 객체임("a" + "b" + "c" = "abc" 이런식이면 총 string 4개가 생성됨)
        // StringBuilder를 사용하게 되면 하나의 스트링 객체를 이어 붙힐 수 있게 됨.
        // 예전에 유니티에서도 많이 썼었음.
        // TMP_Text text 객체에게 text.SetText("abc"); 하면 내부에서 StringBuilder를
        // 이용해서 문자열을 취합해 줍니다. 최적화된 "문자열 계산기"라고 생각하면 됩니다

        while (true)
        {
            // 웹소켓 수신 결과를 저장할 수있는 객체를 선언해주고,
            // await 키워드를 이용하여 해당 소켓(유저와 연결된..)에
            // 메시지가 들어올때까지 기다려 줍니다.
            WebSocketReceiveResult result = 
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

            // 예외 처리부터 해줍니다. 소켓이 닫혔을 경우.
            if (result.MessageType == WebSocketMessageType.Close)
            {
                socket.Dispose();
                return null;
            }
            // 일단 메세지가 도착을 했으면 StringBuilder에 이어 붙혀 줍니다. 
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            // 메세지가 끝났니?. 끝났다면
            // StringBuilder.ToString을 호출해서 String을 생성하여 반환합니다. 
            if (result.EndOfMessage) return builder.ToString();
            
            // 아니라면 다시 루프를 반복합니다
        }
    }
    
    // 멤버와 연결이 끊길때까지 멤버가 보낸 메시지를 계속 듣는다. 
    private async Task ReceiveLoopAsync(Member member, CancellationToken token)
    {
        // 토큰에 취소 요청이 없으면 계속 돈다
        while (token.IsCancellationRequested == false)
        {
            // 호스트가 방 폭파시켰으면
            if (roomExpired)
            {
                Console.WriteLine($"[RoomExpired] [{code}] {member.User.NickName}({member.User.Id})({(member.User.IsHost ? "Host" : "Guest")}) 방 만료. 접속 해제.");
                await member.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Room Expired", CancellationToken.None);
                member.Socket.Dispose();
                return;
            }
            
            string text = await ReceiveTextAsync(member.Socket, token);
            // text가 비어있으면 닫았다는 뜻
            
            if (string.IsNullOrEmpty(text)) return;

            // 아래부터는 Json 텍스트 처리가 된다.
            // 분기에 따라 알맞은 처리 함수를 선택하여 실행해 준다.
            // JsonSerializer?
            // C#에서 Json의 직렬화, 역직렬화를 담당하는 클래스 입니다.
            // Unity와 C#에서 사용하는 직렬화 클래스가 다른것에 유의하세여
            // (타입이랑 매개변수로 텍스트만 넘기면 알아서 잘 처리해줍니다)
            TypeOnly kind = JsonSerializer.Deserialize<TypeOnly>(text);
            
            // if(kind?.Type == "move") HandleMove(member, text);
            // if(kind?.Type == "chat") await HandleChatAsync(member, text);
            if(kind?.Type == "input") HandleInputAsync(member, text);
            else if(kind?.Type == "snapshot") await HandleSnapshotAsync(member, text);
            
            // 모르는 정보는 그냥 흘려버립니다.
            // Tip
            // : 여기 부분에 여러분이 넣고싶은 커스텀한 함수를 처리하는
            // 구간을 만들면 되겠죠?   
        }
    }

    // Guest가 보낸 입력을 Room 자체적으로 저장한다
    private void HandleInputAsync(Member member, string text)
    {
        // Host의 입력은 자기 자신에게 다시 보내지 않는다.
        if (member.User.IsHost) return;

        Member host = GetHost();
        // Host가 없으면 이번 입력은 무시한다.
        if (host == null) return;

        GuestInputMessage input = JsonSerializer.Deserialize<GuestInputMessage>(text);
        // 클라이언트가 보낸 ID는 믿지 않고 현재 Member의 ID를 사용한다.
        input.Id = member.User.Id;
        
        // 게스트 입력값 저장
        member.LastInput = input;
        LogGuestInput(member, input);
    }

    // Host가 보낸 Snapshot을 같은 방의 Guest들에게 바로 전달한다.
    private Task HandleSnapshotAsync(Member member, string text)
    {
        // Guest는 authoritative Snapshot을 전달할 수 없다.
        if (member.User.IsHost == false)
        {
            LogRejectedSnapshot(member);
            return Task.CompletedTask;
        }

        SnapshotMessage snapshot = JsonSerializer.Deserialize<SnapshotMessage>(text);
        lastSnapshot = snapshot;
        // 송신 Host를 제외한 나머지 Member에게만 전달한다.
        LogSnapshot(member, snapshot);
        EnqueueSnapshot(snapshot, member.User.Id);
        return Task.CompletedTask;
    }

    #endregion

    #region 뿌리기

    // 메시지를 방송 큐에 넣는다. 호출자는 네트워크 전송을 기다리지 않는다.
    private void EnqueueSnapshot(object message, string exceptId = null)
    {
        foreach (Member member in members.Values)
        {
            if (member.User.Id == exceptId)
                continue;
            
            if (member.LatestSnapshotChannel.Writer.TryWrite(new BroadcastWorkItem(message, exceptId)) == false)
                Console.Error.WriteLine($"[{code}] 종료된 방송 큐에 메시지를 넣으려 했습니다.");
        }
    }

    // 방송 큐를 하나씩 읽어 실제 네트워크 전송을 전담한다.
    private async Task SendMemberChannelLoopAsync(Member member)
    {
        // 여기도 channel 하나 짜리라서 수정해야될듯
        // 큐전체가 하나라서 foreach가 필요가 없음
        await foreach (BroadcastWorkItem work in member.LatestSnapshotChannel.Reader.ReadAllAsync())
        {
            try
            {
                await SendAsync(member, work.Message);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[{code}] 방송 처리 예외: {e}");
            }
        }
    }

    // 메시지를 여러명에게 실제로 뿌리는 함수. 방송 워커만 호출한다.
    // private async Task BroadcastNowAsync(object message, string exceptId = null)
    // {
    //     string json = JsonSerializer.Serialize(message, message.GetType());
    //     
    //     // 보낼 json객체를 미리 생성하고,
    //     // 유저수에 맞게 보내는 작업을 처리한다.
    //     List<Task> sending = new List<Task>();
    //
    //     // 딕셔너리에 있는 모든 멤버를 순회한다
    //     foreach (Member member in members.Values)
    //     {
    //         // 제외 대상이라면 건너 뛴다
    //         if(member.User.Id == exceptId) continue;
    //         // 한명단위 메시지 Task를 만들어서 List에 넣어준다
    //         sending.Add(SendRawAsync(member, json));
    //     }
    //     
    //     await Task.WhenAll(sending);
    // }

    // 한명의 User에게 메시지를 보내는 함수
    private async Task SendRawAsync(Member member, string json)
    {
        // 소켓이 끊겨있는지 확인을 해준다. 보내기전에 마지막 체크
        if (member.Socket.State != WebSocketState.Open ||
            Volatile.Read(ref member.SendFailed) != 0) return;
        
        using CancellationTokenSource sendTimeout =
            new CancellationTokenSource(SendTimeoutMilliseconds);
        bool lockTaken = false;

        try
        {
            // 보내는 중인 메시지가 있다면 lock이 풀릴때까지 잠깐 기다린다.
            // 그리고 내가 보낼 턴이면 잠궈버린다. 두가지를 동시에 수행합니다.
            await member.SendLock.WaitAsync(sendTimeout.Token);
            lockTaken = true;

            if (member.Socket.State != WebSocketState.Open ||
                Volatile.Read(ref member.SendFailed) != 0) return;

            // 보낼때는 string이 아니라 byte배열로 바꿔준다
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            
            await member.Socket.SendAsync(
                bytes, WebSocketMessageType.Text, true, sendTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            MarkSendFailed(member, "송신 제한 시간 초과");
        }
        catch (WebSocketException)
        {
            MarkSendFailed(member, "소켓 송신 실패");
        }
        finally // 예외가 발생하든 안하든 꼭 처리되는 finally 구문(찾아 보십쇼) 
        {
            if (lockTaken) member.SendLock.Release();
        }
    }

    private void MarkSendFailed(Member member, string reason)
    {
        if (Interlocked.Exchange(ref member.SendFailed, 1) != 0) return;

        Console.Error.WriteLine($"[{code}] {member.User.NickName}({member.User.Id}) {reason}");
        member.Socket.Abort();
    }

    // 단순 호출용 유틸 함수
    private Task SendAsync(Member member, object message)
    {
        return SendRawAsync(member, JsonSerializer.Serialize(message, message.GetType()));
    }

    public async Task SendGuestInputsToHostAsync()
    {
        Member host = GetHost();
        // 아직 hello 처리가 끝나지 않은 Room은 Host가 없을 수 있다.
        // 이거 처리 안해줘서 처음에 가끔 의문사 당했구나...
        // 흐름 2개가 동시에 다르게 돌아가면 이렇게 억까 당할 수 있다...
        // hello 쪽 핸들이 끝나고 얘가 실행되어야하는데 이전에 바로 실행되버리는 서순 문제
        if (host == null) return;

        GuestInputGroupMessage inputGroup = new GuestInputGroupMessage();
        foreach (Member member in members.Values)
        {
            if (member.User.IsHost) continue;

            GuestInputMessage input =
                Interlocked.Exchange(ref member.LastInput, null);

            if (input != null)
                inputGroup.Inputs.Add(input);
        }

        if (inputGroup.Inputs.Count == 0) return;

        LogGuestInputGroup(host, inputGroup);
        await SendAsync(host, inputGroup); 
    }
    
    #endregion

    #region 들어오기, 나가기

    private async Task<Member> JoinAsync(WebSocket socket, 
        string id, CancellationToken token)
    {
        // 첫 메시지를 들어 봅니다. 지금 서버코드 규약에 따르면
        // hello 여야 합니다
        string first = await ReceiveTextAsync(socket, token);
        // 만약에 메시지가 이상하다면 종료
        if (string.IsNullOrEmpty(first)) return null;
        
        // 타입을 꺼내준다
        TypeOnly kind = JsonSerializer.Deserialize<TypeOnly>(first);
        // hello인지 확인해준다
        if (kind?.Type != "hello") 
        {
            Console.WriteLine($"{HandleLog}[{code}] 첫 메시지가 hello가 아님 : {first}");
            return null;
        }
        
        // 아래서 부터는 정상처리
        HelloMessage hello = JsonSerializer.Deserialize<HelloMessage>(first);
        if (hello == null)
        {
            Console.WriteLine($"{HandleLog}[{code}] hello가 null : {first}");
            return null;
        }
        
        // 메시지와 매개변수를 조합해서 Member객체를 생성한다.
        Member member = new Member();
        member.Socket = socket;
        member.LastLogAt = DateTime.Now; // 들어온 시각으로 맞춰 둔다.
        member.User = new User();
        member.User.Id = id;
        
        // broadcast 될때 방 전체 생애주기네 이거...
        // 방에 members 변수 이용하면 될듯
        if (hello.NickName == null)
        {
            member.User.NickName = "꼬마돌";
        }
        else
        {
            member.User.NickName = hello.NickName.Trim();
        }
        
        member.User.R = hello.R;
        member.User.G = hello.G;
        member.User.B = hello.B;

        // BroadcastMemberQueueLoopAsync가 메인 Handle과 독립적으로 실행되게 유도
        _ = SendMemberChannelLoopAsync(member);
        
        // 들어오고 나가는 일은 한사람에 한명씩 해야합니다.
        // 사람이 들어오면 현재 방에 있는 멤버들에게도 메시지를 보내줘야겠죠?
        // 그래서 여기서도 lock을 걸어줘야 하는데 await이기 때문에
        // lock을 못걸어서 gate를 이용해서 대기하여 처리합니다.
        await gate.WaitAsync(token);

        try
        {
            // 방에 처음 들어오는 Member가 Host가 된다.
            member.User.IsHost = members.IsEmpty;
            // 누군가가 hello 메시지를 보냈으면
            // welcome 메시지를 이용해서
            // 현재 방 사람들을 접속한 유저에게 전송하고,
            // join 메시지를 다른 사람들에게 보내준다
            List<User> already = new List<User>();

            foreach (Member other in members.Values)
                already.Add(other.User);

            // welcome 메시지를 전송
            WelcomeMessage welcome = new WelcomeMessage();
            welcome.RoomCode = code; // 서버 방정보를 보낸다
            welcome.User = member.User; // 서버에서 생성한 유저 정보를 접속자에게 보낸다
            welcome.Users = already.ToArray(); // 현재 방에 있는 유저들 정보를 보낸다
            await SendAsync(member, welcome);

            members[member.User.Id] = member;
            // join 메시지를 뿌린다. 접속자인 member 에게는 보내지 않는다
            // 
            await BroadcastNowAsync(welcome, member.User.Id);
            // EnqueueSnapshot(new JoinMessage { User = member.User }, member.User.Id);
        }
        finally
        {
            gate.Release();
        }
        
        Console.WriteLine($"{HandleLog}[{code}] {member.User.NickName}({member.User.Id})({(member.User.IsHost ? "Host" : "Guest")}) 들어옴");
        return member;
    }

    private async Task BroadcastNowAsync(object message, string exceptId)
    {
        List<Task> tasks = new List<Task>();

        foreach (Member member in members.Values)
        {
            if (member.User.Id == exceptId)
                continue;
            
            tasks.Add(SendAsync(member, message));
        }

        await Task.WhenAll(tasks);
    }

    // private void EnqueueDataToChannel(Member member, object message)
    // {
    //     if (member.SendChannel.Writer.TryWrite(new BroadcastWorkItem(message, null)) == false)
    //     {
    //         Console.Error.WriteLine($"[{code}] 종료된 방송 채널에 메시지를 넣으려 했습니다.");
    //     }
    // }

    private async Task LeaveAsync(Member member)
    {
        // 들어오기과 같은 자물쇠를 사용합니다.
        // 들어오기 나가기는 방의 멤버를 수정하고 메시지를 처리하기 때문에
        // 같은 자물쇠를 사용해줘야 합니다.
        // (안하면 유령객체 생길수도?)
        await gate.WaitAsync();

        try
        {
            members.TryRemove(member.User.Id, out _);

            // Host가 나갔고 방에 사람이 남아 있으면 한 명을 다음 Host로 지정한다.
            // 일단 잠시 비활성화... 바로 호스트 나가면 방 터트리기
            // if (wasHost)
            // {
            //     foreach (Member other in members.Values)
            //     {
            //         other.User.IsHost = true;
            //         break;
            //     }
            // }
            // 나간 사람이 호스트이면
            if (member.User.IsHost)
            {
                Console.WriteLine($"[RoomExpired] [{code}] {member.User.NickName}({member.User.Id})({(member.User.IsHost ? "Host" : "Guest")}) 호스트가 나가서 방 폭파");
                roomExpired = true;
                roomExpiredCancellation.Cancel();
            }

            // 퇴장한것을 알려줍니다.
            Console.WriteLine($"[Leave] [{code}] {member.User.NickName}({member.User.Id})({(member.User.IsHost ? "Host" : "Guest")}) 나감");
            await BroadcastNowAsync(new LeaveMessage { Id = member.User.Id }, member.User.Id);
            // EnqueueSnapshot(new LeaveMessage { Id = member.User.Id }, member.User.Id);
        }
        finally
        {
            gate.Release();
        }
        
        Console.WriteLine($"{HandleLog}[{code}] {member.User.NickName}({member.User.Id}) 나감");
    }

    // 한 사람의 접속부터 끊김까지 통째로 관리하느 ㄴ함수
    // id를 외부에서 전달받는 이유
    // : userID는 겹치면 안되기에 모든 유저를 관리하느 RoomHub에서 전달해 준다.
    public async Task HandleAsync(WebSocket socket,
        string id, CancellationToken token)
    {
        Member member = null;
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                token, roomExpiredCancellation.Token);

        try
        {
            // Join처리를 실행하고 끝난뒤 멤버 객체를 저장해준다.
            member = await JoinAsync(socket, id, linkedCancellation.Token);
            // hello 안보내고 딴소리 했다. 방에 못 들인다
            if (member == null) return;

            // 접속완료 했으면 메시지를 계속 들을 수 있게 루프를 호출해준다.
            await ReceiveLoopAsync(member, linkedCancellation.Token);
        }
        catch (OperationCanceledException e)
        {
            Console.Error.WriteLine($"[{code}] OperationCanceled 처리 예외 ({id}): {e}");
        } // 서버 종료. 정상
        catch (WebSocketException e)
        {
            Console.Error.WriteLine($"[{code}] WebSocket 처리 예외 ({id}): {e}");
        }                  // 창 그냥 껐다. 흔한 일
        catch (Exception e)
        {
            Console.Error.WriteLine($"[{code}] 처리 예외 ({id}): {e}");
        }
        finally
        {
            // 입장이 완료된 연결만 퇴장 처리한다.
            if (member != null) await LeaveAsync(member);
        }
    }
    
    #endregion
    
    // Guest Input은 자주 들어오므로 사람마다 일정 간격으로 요약해서 출력한다.
    private void LogGuestInput(Member member, GuestInputMessage input)
    {
        if (logMovesPerSecond <= 0) return;

        member.InputsSinceLog++;
        DateTime now = DateTime.Now;
        TimeSpan gap = now - member.LastInputLogAt;
        double seconds = member.LastInputLogAt == default ? 0 : gap.TotalSeconds;
        if (member.LastInputLogAt != default &&
            gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        Console.WriteLine(
            $"{HandleLog}[{code}] Guest {member.User.NickName}({member.User.Id}) Input " +
            $"({input.X,7:F2}, {input.Y,7:F2}) " +
            $"LShift:{input.IsLeftShiftHold} RShift:{input.IsRightShiftHold}  " +
            $"최근 {seconds:F1}초에 {member.InputsSinceLog}번 수신");

        member.InputsSinceLog = 0;
        member.LastInputLogAt = now;
    }

    // Host에게 실제로 보낸 Input Group만 Room 단위로 요약해서 출력한다.
    private void LogGuestInputGroup(Member host, GuestInputGroupMessage inputGroup)
    {
        if (logMovesPerSecond <= 0) return;

        inputGroupsSinceLog++;
        DateTime now = DateTime.Now;
        TimeSpan gap = now - lastInputGroupLogAt;
        double seconds = lastInputGroupLogAt == default ? 0 : gap.TotalSeconds;
        if (lastInputGroupLogAt != default &&
            gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        Console.WriteLine(
            $"{InputTickLog}[{code}] Host {host.User.NickName}({host.User.Id})에게 " +
            $"Guest Input Group 전달: Guest {inputGroup.Inputs.Count}명, " +
            $"최근 {seconds:F1}초에 {inputGroupsSinceLog}회 전송");

        inputGroupsSinceLog = 0;
        lastInputGroupLogAt = now;
    }

    // Host Snapshot도 Physics 데이터 전체 대신 전달 결과만 요약해서 출력한다.
    private void LogSnapshot(Member host, SnapshotMessage snapshot)
    {
        if (logMovesPerSecond <= 0) return;

        snapshotsSinceLog++;
        DateTime now = DateTime.Now;
        TimeSpan gap = now - lastSnapshotLogAt;
        double seconds = lastSnapshotLogAt == default ? 0 : gap.TotalSeconds;
        if (lastSnapshotLogAt != default &&
            gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        int guestCount = Math.Max(0, members.Count - 1);
        Console.WriteLine(
            $"{HandleLog}[{code}] Host {host.User.NickName}({host.User.Id}) Snapshot Broadcast: " +
            $"Guest {guestCount}명, " +
            $"최근 {seconds:F1}초에 {snapshotsSinceLog}회 전달");

        snapshotsSinceLog = 0;
        lastSnapshotLogAt = now;
    }

    // Guest의 Snapshot 시도는 Broadcast하지 않고 일정 간격으로만 기록한다.
    private void LogRejectedSnapshot(Member member)
    {
        if (logMovesPerSecond <= 0) return;

        member.RejectedSnapshotsSinceLog++;
        DateTime now = DateTime.Now;
        TimeSpan gap = now - member.LastRejectedSnapshotLogAt;
        double seconds = member.LastRejectedSnapshotLogAt == default ? 0 : gap.TotalSeconds;
        if (member.LastRejectedSnapshotLogAt != default &&
            gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        Console.WriteLine(
            $"{HandleLog}[{code}] Guest {member.User.NickName}({member.User.Id})의 Snapshot " +
            $"최근 {seconds:F1}초에 {member.RejectedSnapshotsSinceLog}회 차단");

        member.RejectedSnapshotsSinceLog = 0;
        member.LastRejectedSnapshotLogAt = now;
    }

    // BroadcastLoopAsync에서 보내는 State는 Room 단위로 요약해서 출력한다.
    private void LogStateBroadcast(int memberCount)
    {
        if (logMovesPerSecond <= 0) return;

        stateBroadcastsSinceLog++;
        DateTime now = DateTime.Now;
        TimeSpan gap = now - lastStateBroadcastLogAt;
        double seconds = lastStateBroadcastLogAt == default ? 0 : gap.TotalSeconds;
        if (lastStateBroadcastLogAt != default &&
            gap.TotalSeconds < 1.0 / logMovesPerSecond) return;

        Console.WriteLine(
            $"{StateTickLog}[{code}] State Broadcast: Member {memberCount}명, " +
            $"최근 {seconds:F1}초에 {stateBroadcastsSinceLog}회 전송");

        stateBroadcastsSinceLog = 0;
        lastStateBroadcastLogAt = now;
    }

}
