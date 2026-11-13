using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MiniParty
{
    /// <summary>
    /// 전용 UDP 게임 서버(UDPServer 콘솔 앱)에 붙는 클라이언트.
    /// 소켓을 직접 열고, 받은 패킷은 수신 스레드 → 큐 → 메인 스레드(Pump)에서 처리한다.
    /// 신뢰성(Seq·Ack·재전송)은 서버와 같이 컴파일하는 ReliableChannel 이 맡는다.
    ///
    /// 라운드 선택 · 입력 검증 · 승패 · 데미지는 전부 서버가 정한다.
    /// 여기서 IsHost 는 "판정자"가 아니라 두 PC 가 같은 장면을 그릴 때 한쪽만 해야 하는 일
    /// (CPU 굴리기, 문제 뽑기 등)을 맡는 쪽 = P1 이라는 뜻이다.
    /// </summary>
    public class UdpNetSession : INetLink
    {
        const float ConnectTimeout = 10f;

        class Driver : MonoBehaviour
        {
            public UdpNetSession Session;
            void Update() => Session?.Pump();
        }

        public bool IsHost => LocalIndex == 0;
        public int LocalIndex { get; private set; } = -1;
        public bool Connected { get; private set; }
        public bool Disconnected { get; private set; }
        public string Status { get; private set; } = "";

        /// <summary>이번 라운드를 서버가 판정하는가 (서버가 StartRound 로 알려준다).</summary>
        public bool ServerJudges { get; set; }

        /// <summary>늦게 도착한 지난 라운드의 입력을 버리기 위한 현재 라운드 번호.</summary>
        public int CurrentRound;

        public MiniGameBase ActiveGame;

        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly List<NetMsg> messages = new List<NetMsg>();

        Socket socket;
        Thread receiveThread;
        volatile bool running;
        IPEndPoint server;
        ReliableChannel channel;
        Driver driver;
        float lastPing;

        static float Now => Time.realtimeSinceStartup;

        // ---------------------------------------------------------------- 접속

        public IEnumerator Connect(string address, int port, int character, int group)
        {
            VerifyCatalog();
            Connected = false;
            Disconnected = false;
            LocalIndex = -1;

            if (!TryResolve(address, out var ip))
            {
                Status = "주소를 찾을 수 없어요: " + address;
                yield break;
            }
            if (!Open(ip, port)) yield break;

            Status = $"{ip}:{port} 에 접속 중...";
            Send(new NetMsg { Type = MsgType.Hello, A = UdpProtocol.Version, B = group, C = character });

            float started = Now;
            while (LocalIndex < 0)
            {
                if (socket == null)
                {
                    if (string.IsNullOrEmpty(Status)) Status = "취소했어요";
                    yield break;
                }
                if (Disconnected)
                {
                    Leave();
                    yield break;
                }
                if (Now - started > ConnectTimeout)
                {
                    Status = "서버가 응답하지 않아요\n(주소 · 포트 · 방화벽을 확인해 주세요)";
                    Leave();
                    yield break;
                }
                yield return null;
            }

            Connected = true;
            Status = LocalIndex == 0 ? "접속 완료 — 내가 P1" : "접속 완료 — 내가 P2";
        }

        bool Open(IPAddress ip, int port)
        {
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                // Windows: 서버가 꺼진 뒤 보낸 패킷의 ICMP 응답으로 수신이 깨지는 것을 막는다
                try { s.IOControl(-1744830452, new byte[] { 0 }, null); }
                catch (SocketException) { }
                s.Bind(new IPEndPoint(IPAddress.Any, 0));

                socket = s;
                server = new IPEndPoint(ip, port);
                channel = new ReliableChannel(SendRaw, Now);
                lastPing = Now;
                running = true;

                receiveThread = new Thread(() => ReceiveLoop(s)) { IsBackground = true, Name = "UdpNetSession Receive" };
                receiveThread.Start();

                var go = new GameObject("UdpNetSession") { hideFlags = HideFlags.HideAndDontSave };
                driver = go.AddComponent<Driver>();
                driver.Session = this;
                return true;
            }
            catch (SocketException e)
            {
                Status = "소켓을 열 수 없어요: " + e.SocketErrorCode;
                Debug.LogError("[UdpNetSession] " + Status);
                return false;
            }
        }

        public void Leave()
        {
            running = false;
            Disconnected = true; // 메시지를 기다리고 있던 코루틴들이 빠져나오게 한다
            if (socket != null)
            {
                channel.SendUnreliable(GamePacket.Encode(MsgType.Leave));
                try { socket.Close(); }
                catch (SocketException) { }
            }
            socket = null;
            receiveThread?.Join(300);
            receiveThread = null;
            while (inbox.TryDequeue(out _)) { }
            messages.Clear();
            ActiveGame = null;
            if (driver != null) UnityEngine.Object.Destroy(driver.gameObject);
            driver = null;
        }

        // ---------------------------------------------------------------- 송수신

        void ReceiveLoop(Socket s)
        {
            var buffer = new byte[UdpProtocol.MaxPacketSize];
            while (running)
            {
                try
                {
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int length = s.ReceiveFrom(buffer, ref from);
                    if (!from.Equals(server)) continue; // 서버가 아닌 곳에서 온 패킷은 무시
                    inbox.Enqueue(UdpProtocol.FromBytes(buffer, length));
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset ||
                                                e.SocketErrorCode == SocketError.MessageSize)
                {
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        void SendRaw(string text)
        {
            var s = socket;
            if (s == null) return;
            try { s.SendTo(UdpProtocol.ToBytes(text), server); }
            catch (SocketException e) { Debug.LogWarning($"[UdpNetSession] 송신 실패: {e.SocketErrorCode}"); }
            catch (ObjectDisposedException) { }
        }

        public void Send(NetMsg msg)
        {
            if (socket == null || Disconnected) return;
            channel.SendReliable(GamePacket.Encode(msg), Now);
        }

        public void SendAction(GameAction act) => Send(NetMsg.FromAction(act, CurrentRound));

        public void ReportInput(GameAction input) => Send(NetMsg.FromAction(input, CurrentRound, MsgType.Input));

        /// <summary>서버 판정이 없는 미니게임에서만 승자를 보고한다 (서버가 받아줄지는 서버가 정한다).</summary>
        public void ClaimWinner(int player)
        {
            if (ServerJudges) return;
            Send(new NetMsg { Type = MsgType.Claim, A = player, D = CurrentRound });
        }

        /// <summary>메인 스레드에서 매 프레임 호출 (Driver).</summary>
        void Pump()
        {
            if (socket == null) return;
            float now = Now;

            while (inbox.TryDequeue(out string text))
            {
                if (!channel.Receive(text, now, out string body)) continue;
                if (GamePacket.TryDecode(body, out var msg)) Dispatch(msg);
                else Debug.LogWarning("[UdpNetSession] 알 수 없는 본문: " + body);
            }

            if (Disconnected) return;
            channel.Update(now);

            // 접속 전에는 Connect 쪽 제한 시간이 더 친절한 메시지를 준다
            if (LocalIndex >= 0 && channel.TimedOut(now))
            {
                Drop("서버와 연결이 끊겼어요");
                return;
            }
            if (now - lastPing < UdpProtocol.HeartbeatInterval) return;
            lastPing = now;
            channel.SendUnreliable(GamePacket.Encode(MsgType.Ping));
        }

        void Dispatch(NetMsg msg)
        {
            switch (msg.Type)
            {
                case MsgType.Welcome:
                    LocalIndex = Mathf.Clamp(msg.A, 0, 1);
                    return;

                case MsgType.Action:
                    // 지난 라운드의 늦은 입력은 버린다
                    if (msg.D == CurrentRound && ActiveGame != null) ActiveGame.ReceiveAction(msg.ToAction());
                    return;

                case MsgType.RoundResult:
                    // 서버가 확정한 승자 — 미니게임을 여기서 끝낸다
                    if (ActiveGame != null) ActiveGame.ForceWinner(msg.A);
                    break;

                case MsgType.PeerLeft:
                    Drop("상대가 나갔어요");
                    return;

                case MsgType.Reject:
                    Drop(DescribeReject((RejectReason)msg.A));
                    return;

                case MsgType.Pong:
                    return;
            }
            messages.Add(msg);
        }

        void Drop(string reason)
        {
            Disconnected = true;
            Status = reason;
            Debug.Log("[UdpNetSession] " + reason);
        }

        static string DescribeReject(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.Full: return "서버에 이미 두 명이 들어가 있어요";
                case RejectReason.BadVersion: return "서버와 버전이 달라요 (서버를 다시 빌드해 주세요)";
                case RejectReason.InGame: return "이미 경기가 진행 중이에요";
                case RejectReason.GroupMismatch: return "상대와 다른 조(씬)로 접속했어요";
                default: return "서버가 접속을 거절했어요";
            }
        }

        // ---------------------------------------------------------------- 메시지 기다리기

        public bool TryTake(MsgType type, out NetMsg msg, int? a = null)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Type != type || (a.HasValue && messages[i].A != a.Value)) continue;
                msg = messages[i];
                messages.RemoveAt(i);
                return true;
            }
            msg = default;
            return false;
        }

        /// <summary>그 메시지가 올 때까지 기다린다 (연결이 끊기면 먼저 빠져나온다).</summary>
        public IEnumerator WaitFor(MsgType type, Action<NetMsg> received, int? a = null)
        {
            NetMsg msg;
            while (!TryTake(type, out msg, a))
            {
                if (Disconnected) yield break;
                yield return null;
            }
            received(msg);
        }

        /// <summary>두 클라이언트가 모두 도착할 때까지 기다린다 (서버가 Go 로 풀어준다).</summary>
        public IEnumerator Barrier(int tag)
        {
            Send(new NetMsg { Type = MsgType.Barrier, A = tag });
            yield return WaitFor(MsgType.Go, _ => { }, tag);
        }

        // ---------------------------------------------------------------- 보조

        static bool TryResolve(string host, out IPAddress address)
        {
            host = (host ?? "").Trim();
            if (IPAddress.TryParse(host, out address)) return true;
            try
            {
                foreach (var a in Dns.GetHostAddresses(host))
                    if (a.AddressFamily == AddressFamily.InterNetwork)
                    {
                        address = a;
                        return true;
                    }
            }
            catch (SocketException) { }
            catch (ArgumentException) { }
            address = null;
            return false;
        }

        /// <summary>서버가 쓰는 번호표와 Unity 쪽 목록이 어긋나면 라운드마다 다른 게임이 뜬다.</summary>
        static void VerifyCatalog()
        {
            var names = new string[MiniGameRegistry.Games.Length];
            for (int i = 0; i < names.Length; i++) names[i] = MiniGameRegistry.Games[i].Type.Name;
            if (!MiniGameCatalog.Matches(names, out string problem))
                Debug.LogError($"[UdpNetSession] 미니게임 번호표가 서버와 다릅니다 — {problem}\nAssets/Scripts/Net/Shared/MiniGameCatalog.cs 를 고쳐 주세요.");
        }
    }
}
