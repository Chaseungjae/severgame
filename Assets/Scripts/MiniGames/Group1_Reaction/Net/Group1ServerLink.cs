using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MiniParty.Group1Net;
using UnityEngine;

namespace MiniParty
{
    /// <summary>
    /// 1조 전용 서버(UDPServer/Group1Server)에 붙는 클라이언트 연결. NetSession 의 DedicatedServer 모드가 쓴다.
    ///
    ///  - System.Net.Sockets.Socket 을 직접 사용 (외부 라이브러리 없음). 수신은 별도 스레드 → 큐,
    ///    해석·게임 반영은 유니티 메인 스레드 Update 에서 (유니티 API 는 메인 스레드에서만 쓸 수 있으므로).
    ///  - 신뢰성(재전송·순서·중복 제거)은 서버와 같은 코드(Net/Shared/G1Reliable.cs)를 쓴다.
    ///  - 서버는 두 클라이언트 모두에게 "방장" 역할을 하므로 이쪽은 항상 게스트(IsHost = false)로 동작하고,
    ///    GameManager / 미니게임 코드는 기존 온라인 대전과 똑같이 돈다.
    /// </summary>
    public class Group1ServerLink : MonoBehaviour
    {
        public enum LinkState { Connecting, Waiting, Matched, Failed, Closed }

        // Windows 전용: 서버가 꺼진 뒤 ICMP 응답 때문에 ReceiveFrom 이 10054 로 깨지는 것을 막는다.
        const int SIO_UDP_CONNRESET = -1744830452;

        public LinkState State { get; private set; } = LinkState.Connecting;
        public string Status { get; private set; } = "서버에 접속 중...";
        /// <summary>서버가 정해 준 내 번호 (0 = P1, 1 = P2). 접속 전 -1.</summary>
        public int LocalIndex { get; private set; } = -1;
        public int RoomId { get; private set; }

        /// <summary>서버에서 온 게임 흐름 메시지 (메인 스레드에서 호출).</summary>
        public event Action<NetMsg> NetMessage;
        /// <summary>경기 중 연결이 끊김 (상대 나감 / 서버 응답 없음).</summary>
        public event Action Lost;

        readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        Socket socket;
        Thread receiveThread;
        volatile bool running;
        IPEndPoint server;
        ReliableChannel channel;
        int nonce, pingId;
        double joinStarted, nextJoin, nextPing;

        static double Now => Time.realtimeSinceStartupAsDouble;

        // ------------------------------------------------------------------------------------------ 생성 / 종료

        public static Group1ServerLink Create(IPEndPoint server)
        {
            CheckProtocolNumbers();
            var go = new GameObject("Group1ServerLink");
            DontDestroyOnLoad(go);
            var link = go.AddComponent<Group1ServerLink>();
            link.Open(server);
            return link;
        }

        void Open(IPEndPoint endpoint)
        {
            Application.runInBackground = true; // 창이 비활성이어도 Ping 이 끊기지 않게
            server = endpoint;
            nonce = new System.Random().Next(1, int.MaxValue);
            joinStarted = Now;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try { socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null); }
                catch (Exception) { /* Windows 가 아니면 지원 안 함 */ }
                socket.Bind(new IPEndPoint(IPAddress.Any, 0)); // 내 포트는 OS 가 골라 준다
                running = true;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Group1ServerLink Receive" };
                receiveThread.Start();
                Debug.Log($"[1조 서버] {server} 에 접속 요청 (내 포트 {((IPEndPoint)socket.LocalEndPoint).Port})");
            }
            catch (SocketException e)
            {
                Fail("소켓을 열 수 없어요: " + e.SocketErrorCode);
            }
        }

        /// <summary>서버에 나간다고 알리고 정리한다.</summary>
        public void Close()
        {
            if (State == LinkState.Closed) return;
            if (channel != null)
                for (int i = 0; i < 2; i++) channel.SendUnreliable(Payloads.Simple(Op.Leave)); // 유실 대비 두 번
            State = LinkState.Closed;
            CloseSocket();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // 게임 종료 / 플레이 모드 정지: 서버가 6초 시간 초과를 기다리지 않게 바로 알린다
            if (State != LinkState.Closed && channel != null)
                for (int i = 0; i < 2; i++) channel.SendUnreliable(Payloads.Simple(Op.Leave));
            State = LinkState.Closed;
            CloseSocket();
        }

        void CloseSocket()
        {
            running = false;
            socket?.Close(); // 블로킹 중인 ReceiveFrom 이 예외를 던지며 빠져나온다
            socket = null;
            receiveThread?.Join(300);
            receiveThread = null;
        }

        // ------------------------------------------------------------------------------------------ 수신 스레드

        void ReceiveLoop()
        {
            var buffer = new byte[G1Protocol.MaxPacketSize * 2];
            var s = socket;
            while (running)
            {
                try
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int length = s.ReceiveFrom(buffer, ref remote);
                    if (!remote.Equals(server)) continue; // 서버가 아닌 곳에서 온 패킷은 버린다
                    var data = new byte[length];
                    Buffer.BlockCopy(buffer, 0, data, 0, length);
                    inbox.Enqueue(data);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset ||
                                                e.SocketErrorCode == SocketError.MessageSize)
                {
                }
                catch (SocketException)
                {
                    if (!running) break;
                    Thread.Sleep(5);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ 메인 스레드

        void Update()
        {
            if (State == LinkState.Failed || State == LinkState.Closed) return;
            double now = Now;

            while (inbox.TryDequeue(out var data))
            {
                if (!Packet.TryParse(data, data.Length, out var packet, out bool badVersion))
                {
                    if (badVersion) Fail("서버 버전이 달라요 (서버와 게임을 같은 버전으로 맞춰 주세요)");
                    continue;
                }
                if (channel == null)
                {
                    if (packet.Channel == Channel.Unreliable) HandlePayload(packet.Payload); // JoinOk / Reject
                    continue;
                }
                channel.Receive(packet, now, HandlePayload);
                if (State == LinkState.Failed || State == LinkState.Closed) return;
            }

            if (channel == null)
            {
                // 아직 JoinOk 를 못 받음: 0.5초마다 Join 재전송
                if (now - joinStarted > G1Protocol.JoinTimeout)
                {
                    Fail($"서버({server})가 응답하지 않아요\n주소·포트, 서버 실행 여부, 방화벽을 확인해 주세요");
                    return;
                }
                if (now >= nextJoin)
                {
                    nextJoin = now + G1Protocol.JoinRetryInterval;
                    SendRaw(new PacketWriter().Header(Channel.Unreliable, 0).Byte((byte)Op.Join).Int(nonce).ToArray());
                }
                return;
            }

            if (now >= nextPing)
            {
                nextPing = now + G1Protocol.PingInterval;
                channel.SendUnreliable(Payloads.OneInt(Op.Ping, ++pingId));
            }
            if (now - channel.LastHeard > G1Protocol.Timeout)
            {
                LoseConnection("서버와 연결이 끊겼어요 (응답 없음)");
                return;
            }
            channel.Update(now);
        }

        void HandlePayload(byte[] payload)
        {
            try
            {
                var r = new PacketReader(payload, 0, payload.Length);
                var op = (Op)r.Byte();
                switch (op)
                {
                    case Op.JoinOk:
                    {
                        if (channel != null) return;
                        int index = r.Byte();
                        int room = r.Int();
                        if (r.Int() != nonce) return;
                        LocalIndex = index;
                        RoomId = room;
                        channel = new ReliableChannel(SendRaw, Now);
                        State = LinkState.Waiting;
                        Status = $"서버 접속 완료 (방 {room}, 내가 P{index + 1})\n상대를 기다리는 중...";
                        Debug.Log($"[1조 서버] 접속 성공: 방 {room}, P{index + 1}");
                        break;
                    }

                    case Op.Reject:
                        var reason = (RejectReason)r.Byte();
                        Fail(reason == RejectReason.ServerFull ? "서버가 꽉 찼어요" : "서버 버전이 달라요");
                        break;

                    case Op.Matched:
                        State = LinkState.Matched;
                        Status = "상대 발견!";
                        Debug.Log("[1조 서버] 매칭 완료");
                        break;

                    case Op.OpponentLeft:
                        LoseConnection("상대가 나갔어요");
                        break;

                    case Op.Pong:
                        break;

                    case Op.Net:
                        var w = Payloads.ReadNet(r);
                        var msg = new NetMsg { Type = (MsgType)w.Type, A = w.A, B = w.B, C = w.C, D = w.D, F = w.F };
                        if (msg.Type == MsgType.StartRound)
                        {
                            // 서버는 1조 게임 번호(0~5)를 보낸다 → 이 빌드의 전체 미니게임 목록 번호로 바꾼다
                            int index = ToRegistryIndex(msg.B);
                            if (index < 0)
                            {
                                LoseConnection($"서버가 고른 게임({G1Game.Name(msg.B)})이 이 빌드에 없어요");
                                return;
                            }
                            msg.B = index;
                        }
                        NetMessage?.Invoke(msg);
                        break;
                }
            }
            catch (FormatException e)
            {
                Debug.LogWarning("[1조 서버] 잘못된 패킷 무시: " + e.Message);
            }
        }

        // ------------------------------------------------------------------------------------------ 송신

        public void Send(NetMsg msg)
        {
            if (channel == null || State == LinkState.Failed || State == LinkState.Closed) return;
            var w = new WireMsg { Type = (byte)msg.Type, A = msg.A, B = msg.B, C = msg.C, D = msg.D, F = msg.F };
            channel.SendReliable(Payloads.Net(w), Now);
        }

        void SendRaw(byte[] data)
        {
            if (socket == null) return;
            try { socket.SendTo(data, server); }
            catch (SocketException e) { Debug.LogWarning($"[1조 서버] 송신 실패: {e.SocketErrorCode}"); }
            catch (ObjectDisposedException) { }
        }

        void Fail(string status)
        {
            State = LinkState.Failed;
            Status = status;
            Debug.LogWarning("[1조 서버] " + status);
        }

        void LoseConnection(string status)
        {
            bool wasMatched = State == LinkState.Matched;
            Fail(status);
            if (wasMatched) Lost?.Invoke();
        }

        // ------------------------------------------------------------------------------------------ 도우미

        /// <summary>"192.168.0.5" 또는 "192.168.0.5:33333" (포트 생략 시 33333). 도메인 이름도 된다.</summary>
        public static bool TryParseAddress(string text, out IPEndPoint endpoint, out string error)
        {
            endpoint = null;
            error = null;
            string host = (text ?? "").Trim();
            int port = G1Protocol.DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(host.Substring(colon + 1), out port) || port <= 0 || port > 65535)
                {
                    error = "포트 번호가 이상해요";
                    return false;
                }
                host = host.Substring(0, colon);
            }
            if (host.Length == 0)
            {
                error = "서버 주소를 입력해 주세요";
                return false;
            }

            if (!IPAddress.TryParse(host, out var address))
            {
                try
                {
                    foreach (var a in Dns.GetHostAddresses(host))
                        if (a.AddressFamily == AddressFamily.InterNetwork) { address = a; break; }
                }
                catch (SocketException) { }
                if (address == null)
                {
                    error = $"주소를 찾을 수 없어요: {host}";
                    return false;
                }
            }
            endpoint = new IPEndPoint(address, port);
            return true;
        }

        /// <summary>서버 게임 번호(G1Game) 순서의 실제 게임 스크립트.</summary>
        static readonly Type[] GameTypes =
        {
            typeof(SpaceTugGame), typeof(CpsGame), typeof(F1ReactionGame), typeof(AimGame), typeof(StackGame), typeof(TimingGame),
        };

        /// <summary>게임 스크립트 → 서버 게임 번호(G1Game). 1조 게임이 아니면 -1.</summary>
        public static int ServerGameIdOf(Type gameType) => Array.IndexOf(GameTypes, gameType);

        static int ToRegistryIndex(int g1Game)
        {
            if (g1Game < 0 || g1Game >= GameTypes.Length) return -1;
            var games = MiniGameRegistry.Games;
            for (int i = 0; i < games.Length; i++)
                if (games[i].Type == GameTypes[g1Game]) return i;
            return -1;
        }

        static bool checkedNumbers;

        /// <summary>서버와 공유하는 메시지 번호가 NetSession.MsgType 과 같은지 (누가 enum 을 바꾸면 바로 알 수 있게).</summary>
        static void CheckProtocolNumbers()
        {
            if (checkedNumbers) return;
            checkedNumbers = true;
            bool ok = (byte)MsgType.Hello == NetType.Hello && (byte)MsgType.Welcome == NetType.Welcome &&
                      (byte)MsgType.StartRound == NetType.StartRound && (byte)MsgType.Barrier == NetType.Barrier &&
                      (byte)MsgType.Go == NetType.Go && (byte)MsgType.Action == NetType.Action &&
                      (byte)MsgType.RoundResult == NetType.RoundResult;
            if (!ok) Debug.LogError("[1조 서버] NetSession.MsgType 번호가 G1Protocol.NetType 과 다릅니다. 서버와 통신할 수 없어요.");
        }
    }
}
