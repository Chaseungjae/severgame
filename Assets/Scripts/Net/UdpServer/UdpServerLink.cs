using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MiniParty.Udp
{
    /// <summary>
    /// Client side of the dedicated UDP game server (UdpGameServer/). Owns the socket, a background receive
    /// thread and a <see cref="ReliableChannel"/> to the server. Everything the game sees happens on the main
    /// thread: <see cref="UdpPump"/> calls <see cref="Pump"/> every frame, which hands bodies to <see cref="OnBody"/>.
    /// </summary>
    public sealed class UdpServerLink
    {
        public enum Phase { Connecting, Waiting, Matched, Rejected, Lost }

        public Phase State { get; private set; } = Phase.Connecting;
        public int Index { get; private set; } = -1;
        public string Room { get; private set; } = "";
        public string RejectReason { get; private set; } = "";
        public readonly IPEndPoint Server;

        /// <summary>Game message bodies ("MSG|...") and PEER_LEFT, delivered on the main thread in order.</summary>
        public event Action<string> OnBody;

        readonly Socket socket;
        readonly ReliableChannel channel;
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly List<string> ready = new List<string>();
        readonly Thread receiver;
        volatile bool running = true;
        double lastPing;
        UdpPump pump;

        static double Now => Time.realtimeSinceStartupAsDouble;

        public UdpServerLink(string host, int port, string room)
        {
            Server = new IPEndPoint(Resolve(host), port);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { socket.IOControl(UdpWire.SIO_UDP_CONNRESET, new byte[4], null); } catch { /* not Windows */ }
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            channel = new ReliableChannel(data => SafeSend(data), Now);

            receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "UdpServerLink" };
            receiver.Start();

            var go = new GameObject("UdpServerLink");
            UnityEngine.Object.DontDestroyOnLoad(go);
            pump = go.AddComponent<UdpPump>();
            pump.Link = this;

            if (!CheckRegistry(out string drift))
            {
                State = Phase.Rejected;
                RejectReason = "REGISTRY";
                Debug.LogError($"[UDP] 미니게임 목록이 서버 규격(BoardProtocol.cs)과 달라요: {drift}");
                return;
            }
            channel.SendReliable($"HELLO|{UdpWire.Version}|{room}", Now);
            Debug.Log($"[UDP] 서버 {Server} 에 접속 요청 (방: {(room.Length > 0 ? room : "빠른 매칭")})");
        }

        /// <summary>The server refers to 3조 games by registry index: make sure they still point at the same games.</summary>
        static bool CheckRegistry(out string drift)
        {
            var expected = new (int Index, Type Type)[]
            {
                (Board3.ConnectFour, typeof(ConnectFourGame)), (Board3.Othello, typeof(OthelloGame)),
                (Board3.Circle, typeof(CircleGame)), (Board3.Typing, typeof(TypingGame)), (Board3.Yacht, typeof(YachtGame)),
            };
            foreach (var (index, type) in expected)
            {
                var games = MiniGameRegistry.Games;
                if (index >= games.Length || games[index].Type != type)
                {
                    drift = $"#{index} 은(는) {type.Name} 이어야 해요";
                    return false;
                }
            }
            drift = null;
            return true;
        }

        static IPAddress Resolve(string host)
        {
            if (IPAddress.TryParse(host, out var ip)) return ip;
            foreach (var a in Dns.GetHostAddresses(host))
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            throw new SocketException((int)SocketError.HostNotFound);
        }

        public void Send(string body)
        {
            if (State == Phase.Lost || State == Phase.Rejected) return;
            channel.SendReliable(body, Now);
        }

        void SafeSend(byte[] data)
        {
            try { socket.SendTo(data, Server); }
            catch (Exception e) { Debug.LogWarning($"[UDP] send failed: {e.Message}"); }
        }

        void ReceiveLoop()
        {
            var buffer = new byte[4096];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    if (!socket.Poll(200_000, SelectMode.SelectRead)) continue;
                    int n = socket.ReceiveFrom(buffer, ref from);
                    if (!from.Equals(Server)) continue; // only the server talks to us
                    inbox.Enqueue(UdpWire.Utf8.GetString(buffer, 0, n));
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { break; }
            }
        }

        /// <summary>Main thread, every frame.</summary>
        public void Pump()
        {
            double now = Now;
            while (inbox.TryDequeue(out var packet)) channel.Receive(packet, now, ready);
            foreach (var body in ready) Handle(body);
            ready.Clear();

            channel.Tick(now);
            if (State == Phase.Lost || State == Phase.Rejected) return;
            if (now - lastPing > 1.0)
            {
                lastPing = now;
                channel.SendUnreliable("PING");
            }
            bool silent = State != Phase.Connecting && now - channel.LastHeard > UdpWire.Timeout;
            if (channel.Failed || silent)
            {
                State = Phase.Lost;
                Debug.LogWarning("[UDP] 서버와 연결이 끊겼습니다");
                OnBody?.Invoke("PEER_LEFT");
            }
        }

        void Handle(string body)
        {
            var f = body.Split('|');
            switch (f[0])
            {
                case "WELCOME":
                    if (f.Length >= 3 && int.TryParse(f[1], out int index))
                    {
                        Index = index;
                        Room = f[2];
                        if (State == Phase.Connecting) State = Phase.Waiting;
                        Debug.Log($"[UDP] 입장: 방 {Room}, P{Index + 1}");
                    }
                    break;
                case "MATCH":
                    State = Phase.Matched;
                    Debug.Log($"[UDP] 방 {Room} 매칭 완료");
                    break;
                case "REJECT":
                    State = Phase.Rejected;
                    RejectReason = f.Length > 1 ? f[1] : "";
                    break;
                case "PONG":
                    break;
                default: // MSG|..., PEER_LEFT
                    OnBody?.Invoke(body);
                    break;
            }
        }

        public void Close()
        {
            if (!running) return;
            for (int i = 0; i < 3; i++) channel.SendUnreliable("BYE"); // best effort; the server also times us out
            running = false;
            try { socket.Close(); } catch { }
            if (pump != null) UnityEngine.Object.Destroy(pump.gameObject);
            pump = null;
        }
    }

    /// <summary>Drives a <see cref="UdpServerLink"/> from Unity's main loop (keeps running while unfocused).</summary>
    public sealed class UdpPump : MonoBehaviour
    {
        public UdpServerLink Link;

        void Update() => Link?.Pump();
    }
}
