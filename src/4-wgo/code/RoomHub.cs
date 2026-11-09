using System.Diagnostics;
using System.Net.WebSockets;

namespace HelloServer;

// 여러개의 방들을 관리하는 클래스.
// 코드로 방을 찾아주고, 빈 방을 치운다, 

public class RoomHub
{
    private const string HandleLog = "[Handle]";
    private const string StateTickLog = "[StateTick]";
    private const string InputTickLog = "[InputTick]";
    private const int MaxRoomMember = 4;

    // 방 하나와 그 방에 들어가겠다고 한 사람의 수
    // 방을 만들기 전에(방을 찾은 뒤) 실제로 방이 생성 될때까지는
    // 시간이 조금 걸립니다. 그 사이에 어떤 유저가 나가게되면
    // 바로 비어버리게 되니까 별도의 클래스를 만들어서 룸자체를 관리해줍니다. 
    // (룸을 한번 감싸는 거임)
    private class Entry
    {
        public Room Room;
        public int Users;
    }

    private readonly Dictionary<string, Entry> entries = new();

    private readonly int broadcastPerSecond;
    private readonly int logMovesPerSecond;

    private readonly object gate = new object();

    private int lastId;

    public RoomHub(int broadcastPerSecond, int logMovesPerSecond)
    {
        this.broadcastPerSecond = broadcastPerSecond;
        this.logMovesPerSecond = logMovesPerSecond;
    }

    #region 방 관리 함수들(찾기, 지우기)

    // 방을 먼저 찾아보고, 없다면 만든다.
    private Room Enter(string code)
    {
        // lock(매개변수)?
        // : 해당 영역에 들어갈 수 있는 녀석들은. gate를 참조하고있는
        //  녀석들만 들어올 수 있습니다. 다른 녀석을 못들어옴.
        //  lock이라는게 걸리면 gate가 키의 역할을 한다고 생각하시면 됩니다.
        //  (gate를 알고있는 갈래의 객체들만 들어옴)
        //  안전한 처리를 위해 사용을 합니다.
        
        //  실제 컴퓨터에서 처리를 할때 해당 영역을 처음부터 끝까지
        //  처리함을 보장하는 장치 입니다. (이렇게 알고있으면 됩니다)
        //  매개변수에 객체는 private readonly로 하게되면
        //  나만 알고있는 키를 사용해서 더 안전하게 사용 할 수있습니다.
        //  (this로 걸게되면 이 클래스가 public이니까 외부에서도
        //  잠글 수 있음)
        lock (gate)
        {
            bool hasCode = entries.TryGetValue(code, out Entry entry);
            if (hasCode == false || entry.Users < MaxRoomMember)
            {
                if (hasCode == false)
                {
                    entry = new Entry()
                        {Room = new Room(code, logMovesPerSecond), Users = 0};
                    entries.Add(code, entry);
                    Console.WriteLine($"{HandleLog}[{code}] 방을 열었다. 총 방의 개수 : {entries.Count}");
                }
                entry.Users++;
                return entry.Room;
            }
        }
        Console.WriteLine($"[Rejected][{code}] 방이 꽉차서 연결 거부");
        return null;
    }

    // 방을 떠나고, 아무도 없으면 방을 지운다.
    private void Leave(string code)
    {
        Room roomToStop = null;

        lock (gate)
        {
            // 예외 처리 한번 해준다
            if (entries.TryGetValue(code, out Entry entry) == false) return;
            entry.Users--;
            
            // 호스트 나가면 방 폭파여서 일단 없어도 될거 같은데 남기기
            if (entry.Users > 0) return;
            
            roomToStop = entry.Room;
            roomToStop.StopWriteToAllChannel();
            entries.Remove(code);
            Console.WriteLine($"{HandleLog}[{code}] 아무도 없어서 방을 지움. 총 방의 개수 {entries.Count}");
        }

        // room에 있는 모든 channel의 쓰기를 멈추게 해야
    }

    #endregion

    // 아래의 비동기 함수는 한 사람의 접속부터 끊김까지
    // 방을 찾아 넘기고, 끝나면 뒷정리하는 함수 입니다.
    // (RoomHub -> Room의 함수를 호출)
    public async Task HandleAsync(string code, 
        WebSocket socket, CancellationToken token)
    {
        Room room = Enter(code);
        
        // null을 리턴했으면 방코드가 유효하지 않거나 인원 제한이 터진것
        if (room == null)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Room Member Limit", CancellationToken.None);
            socket.Dispose();
            return;
        }
        // lastId를 여러 접속자가 동시에 수정 할수 있으므로
        // Interlocked를 이용해서 락을 걸어줍니다.
        // 이것도 외워버리십쇼.
        string id = $"u{Interlocked.Increment(ref lastId)}";

        try
        {
            // 방에 들어왔으니, 해당 유저의 소켓을 넘겨주고
            // join, receiveLoop, leave 처리를 room에게 위임합니다
            await room.HandleAsync(socket, id, token);
        }
        finally
        {
            // room.HandleAsync는 유저가 퇴장할때 끝납니다.
            Leave(code);
        }
    }

    public async Task SendGuestInputsToHostLoopAsync(CancellationToken token)
    {
        PeriodicTimer timer = new PeriodicTimer(
            TimeSpan.FromSeconds(1.0 / broadcastPerSecond));
        
        try
        {
            // 타이머가 종료되면 자동으로 false를 반환합니다.
            while (await timer.WaitForNextTickAsync(token))
            {
                // 락을걸기전에 snapshot(복사본)이 들어갈
                // list를 생성해놓는다
                List<Room> rooms = new List<Room>();

                // 위에 설정된 object 객체인 gate를 이용하여 lock을 걸어놓는다.
                lock (gate)
                {
                    if(entries.Count == 0) continue;

                    foreach (Entry entry in entries.Values)
                    {
                        rooms.Add(entry.Room);
                    }
                }

                List<Task> sending = new List<Task>();
                foreach (Room room in rooms)
                {
                    // 상태정보 보내는 Task를 가져와서 sending에 추가해준다.
                    sending.Add(room.SendGuestInputsToHostAsync());
                }
                
                await Task.WhenAll(sending);
            }

        }
        catch (OperationCanceledException)
        {
            // 서버가 꺼지는 중.
        }
        catch (Exception e)
        {
            Console.WriteLine($"{InputTickLog}[RoomHub] Exception: {e.Message}");
        }
    }

    // 방 코드를 정규화 하는 유틸 함수입니다.
    // 빈 문자처리, 특수문자 처리등을 합니다.
    public static string Normalize(string raw)
    {
        // 공백있니?
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // string을 컨버팅할때 문화권에 따라서 변경되는게 달라요
        // 우리는 소숫점을 3.141592 이렇게 쓰는데
        // 프랑스는 3,141592 이렇게 씀. 그래서 문화권에 따라서
        // string처리를 유연하게 해줘야함.
        //string code = raw.Trim().ToUpperInvariant();
        
        // raw의 앞뒤 공백을 제거하고, 대문자 처리 해준다.
        string code = raw.Trim().ToUpper();

        // string의 문자를 하나씩 검사해서
        // 특수문자가 있는지 확인해 준다
        foreach (char c in code)
        {
            // c가 문자 또는 숫자가 아니라면 return null 한다.
            if (char.IsLetterOrDigit(c) == false) return null;
        }

        return code;
    }

}
