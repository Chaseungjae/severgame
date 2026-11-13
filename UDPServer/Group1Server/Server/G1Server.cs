using System.Diagnostics;
using System.Net;
using Group1Server.Net;
using MiniParty.Group1Net;

namespace Group1Server.Server;

public sealed class ServerOptions
{
    public int Port = G1Protocol.DefaultPort;
    public int MaxClients = 100;
    /// <summary>null = 라운드마다 25~35 랜덤 (GameManager 기본값과 같음).</summary>
    public int? Damage;
    public int MaxHp = 100;
    public int? Seed; // 서버 난수 고정 (재현 테스트용)
    /// <summary>테스트용: 이 1조 게임(G1Game 번호)만 계속 나온다. null = 6개 무작위.</summary>
    public int? OnlyGame;
}

/// <summary>서버에 접속한 클라이언트 한 명.</summary>
public sealed class ClientConn
{
    public IPEndPoint EndPoint;
    public int Nonce;
    public ReliableChannel Channel;
    public Room Room;
    public int Index; // 0 = P1, 1 = P2

    public string Tag => Room != null ? $"[방{Room.Id} P{Index + 1}]" : $"[{EndPoint}]";
}

/// <summary>
/// 1조 미니게임 전용 서버.
///  - 접속(Join)한 순서대로 두 명씩 방에 넣는다 (방 여러 개 동시 진행 가능).
///  - 방 안에서는 서버가 "방장" 역할: 게임 추첨, 라운드 동기화, 입력 검증·중계, 승패·데미지 판정.
///  - 소켓 수신만 별도 스레드, 나머지는 전부 이 루프 한 스레드에서 처리한다.
/// </summary>
public sealed class G1Server : IDisposable
{
    readonly ServerOptions options;
    readonly UdpSocket socket;
    readonly Dictionary<IPEndPoint, ClientConn> clients = new();
    readonly List<Room> rooms = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng;
    int nextRoomId = 1;
    double lastTick;

    public double Now => clock.Elapsed.TotalSeconds;
    public int Port => socket.LocalEndPoint.Port;
    public ServerOptions Options => options;
    public Random Rng => rng;
    public int FinishedMatches { get; private set; }
    public int AbortedMatches { get; private set; }

    public G1Server(ServerOptions options)
    {
        this.options = options;
        rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        socket = new UdpSocket(options.Port);
    }

    public void Run(CancellationToken ct)
    {
        Log.Good($"1조 전용 UDP 서버 시작: 0.0.0.0:{Port}  (프로토콜 v{G1Protocol.Version})");
        Log.Info("접속 주소: " + string.Join(", ", LocalAddresses().Select(a => $"{a}:{Port}")));
        Log.Info($"데미지: {(options.Damage.HasValue ? options.Damage.Value.ToString() : "25~35 랜덤")} / 최대 HP {options.MaxHp}. 종료: Ctrl+C");
        if (options.OnlyGame.HasValue) Log.Warn($"테스트 모드: '{G1Game.Name(options.OnlyGame.Value)}' 만 나옵니다");

        while (!ct.IsCancellationRequested)
        {
            Step();
            Thread.Sleep(1);
        }
        Shutdown();
    }

    /// <summary>받은 패킷 처리 → 방 시간 진행 → 재전송·시간 초과 검사. (자체 테스트에서도 직접 부른다)</summary>
    public void Step()
    {
        double now = Now;
        while (socket.TryReceive(out var from, out var data)) HandleDatagram(from, data, now);

        double dt = Math.Min(0.1, now - lastTick);
        lastTick = now;
        foreach (var room in rooms.ToArray()) room.Tick(now, dt);

        foreach (var c in clients.Values.ToArray())
        {
            if (now - c.Channel.LastHeard > G1Protocol.Timeout)
            {
                Log.Warn($"{c.Tag} {G1Protocol.Timeout:0}초 동안 응답 없음 → 연결 끊김 처리");
                RemoveClient(c, "시간 초과");
                continue;
            }
            c.Channel.Update(now);
        }
    }

    void HandleDatagram(IPEndPoint from, byte[] data, double now)
    {
        if (!Packet.TryParse(data, data.Length, out var packet, out bool badVersion))
        {
            if (badVersion)
            {
                Log.Warn($"{from} 프로토콜 버전이 다름 → 거절");
                SendRawUnreliable(from, Payloads.Reject(RejectReason.BadVersion));
            }
            else Log.Debug($"{from} 형식이 틀린 패킷 {data.Length}바이트 무시");
            return;
        }

        if (!clients.TryGetValue(from, out var conn))
        {
            // 모르는 주소는 Join 만 받는다
            if (packet.Channel == Channel.Unreliable && packet.Payload[0] == (byte)Op.Join)
                HandleJoin(from, packet.Payload, now);
            else
                Log.Debug($"{from} 접속(Join) 전 패킷 무시: {Payloads.Describe(packet.Payload)}");
            return;
        }

        conn.Channel.Receive(packet, now, payload => HandlePayload(conn, payload, now));
    }

    void HandleJoin(IPEndPoint from, byte[] payload, double now)
    {
        int nonce;
        try { nonce = new PacketReader(payload, 1, payload.Length - 1).Int(); }
        catch (FormatException) { return; }

        if (clients.Count >= options.MaxClients)
        {
            Log.Warn($"{from} 접속 거절: 서버 정원({options.MaxClients}) 초과");
            SendRawUnreliable(from, Payloads.Reject(RejectReason.ServerFull));
            return;
        }

        var conn = new ClientConn { EndPoint = from, Nonce = nonce };
        conn.Channel = new ReliableChannel(bytes => socket.Send(from, bytes), now);
        clients[from] = conn;

        // 상대를 기다리는 방이 있으면 거기로, 없으면 새 방
        var room = rooms.FirstOrDefault(r => r.IsWaiting);
        if (room == null)
        {
            room = new Room(nextRoomId++, this);
            rooms.Add(room);
        }
        room.Add(conn);
        conn.Channel.SendUnreliable(Payloads.JoinOk(conn.Index, room.Id, nonce));
        Log.Good($"{conn.Tag} 접속: {from} (접속자 {clients.Count}명, 방 {rooms.Count}개)");
        room.TryBeginMatch(now);
    }

    void HandlePayload(ClientConn conn, byte[] payload, double now)
    {
        try
        {
            var r = new PacketReader(payload, 0, payload.Length);
            var op = (Op)r.Byte();
            switch (op)
            {
                case Op.Join:
                    // JoinOk 가 유실돼서 다시 보낸 것: 같은 대답을 한 번 더
                    int nonce = r.Int();
                    if (nonce == conn.Nonce) conn.Channel.SendUnreliable(Payloads.JoinOk(conn.Index, conn.Room.Id, nonce));
                    else
                    {
                        // 같은 주소·포트에서 새로 실행된 클라이언트: 이전 접속을 정리하고 새로 받는다
                        Log.Warn($"{conn.Tag} 같은 주소에서 새 접속 → 이전 접속 정리");
                        RemoveClient(conn, "재접속");
                        HandleJoin(conn.EndPoint, payload, now);
                    }
                    break;

                case Op.Ping:
                    conn.Channel.SendUnreliable(Payloads.OneInt(Op.Pong, r.Int()));
                    break;

                case Op.Leave:
                    Log.Info($"{conn.Tag} 나가기 요청");
                    RemoveClient(conn, "나감");
                    break;

                case Op.Net:
                    var msg = Payloads.ReadNet(r);
                    Log.Debug($"{conn.Tag} → {msg}");
                    conn.Room.OnNet(conn, msg, now);
                    break;

                default:
                    Log.Warn($"{conn.Tag} 클라이언트가 보낼 수 없는 메시지 무시: {op}");
                    break;
            }
        }
        catch (FormatException e)
        {
            Log.Warn($"{conn.Tag} 잘못된 패킷 무시: {e.Message}");
        }
    }

    public void SendNet(ClientConn to, WireMsg msg, double now)
    {
        if (to == null || !clients.ContainsKey(to.EndPoint)) return;
        Log.Debug($"{to.Tag} ← {msg}");
        to.Channel.SendReliable(Payloads.Net(msg), now);
    }

    public void SendOp(ClientConn to, Op op, double now)
    {
        if (to == null || !clients.ContainsKey(to.EndPoint)) return;
        to.Channel.SendReliable(Payloads.Simple(op), now);
    }

    void SendRawUnreliable(IPEndPoint to, byte[] payload) =>
        socket.Send(to, new PacketWriter().Header(Channel.Unreliable, 0).Bytes(payload).ToArray());

    void RemoveClient(ClientConn conn, string reason)
    {
        if (!clients.Remove(conn.EndPoint)) return;
        var room = conn.Room;
        room?.OnLeave(conn, reason, Now);
        if (room != null && room.IsEmpty) rooms.Remove(room);
        Log.Info($"{conn.Tag} 연결 해제 ({reason}). 접속자 {clients.Count}명, 방 {rooms.Count}개");
    }

    public void OnMatchFinished(Room room) => FinishedMatches++;
    public void OnMatchAborted(Room room) => AbortedMatches++;

    void Shutdown()
    {
        foreach (var c in clients.Values.ToArray())
        {
            // 서버 종료를 알려 클라이언트가 곧바로 로비로 돌아가게 한다 (몇 번 보내서 유실 대비)
            for (int i = 0; i < 3; i++) c.Channel.SendUnreliable(Payloads.Simple(Op.OpponentLeft));
        }
        Log.Info($"서버 종료. 끝난 경기 {FinishedMatches}, 중단된 경기 {AbortedMatches}, 송신 {socket.PacketsSent}패킷 / 수신 {socket.PacketsReceived}패킷");
    }

    public void Dispose() => socket.Dispose();

    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        list.Add(ua.Address.ToString());
            }
        }
        catch (Exception e)
        {
            Log.Warn("IP 목록을 가져오지 못함: " + e.Message);
        }
        list.Add("127.0.0.1");
        return list;
    }
}
