using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MiniParty.Server;

/// <summary>
/// UDP 소켓 하나로 두 클라이언트를 받는 전용 게임 서버.
/// 수신은 블로킹 스레드에서 받아 큐에 넣고, 본 루프(60Hz)에서 재전송 · 타임아웃 · 경기 진행을 처리한다.
/// </summary>
internal sealed class UdpGameServer
{
    // Windows: 꺼진 상대에게 보낸 패킷의 ICMP 응답 때문에 ReceiveFrom 이 10054 로 깨지는 것을 막는다
    const int SioUdpConnReset = -1744830452;
    const int TickMs = 16;

    readonly int port;
    readonly Room room;
    readonly Dictionary<IPEndPoint, ClientPeer> peers = new();
    readonly ConcurrentQueue<(IPEndPoint From, string Text)> inbox = new();
    readonly Stopwatch clock = Stopwatch.StartNew();

    Socket? socket;

    public UdpGameServer(int port)
    {
        this.port = port;
        room = new Room(this);
    }

    float Now => (float)clock.Elapsed.TotalSeconds;

    public void Run(CancellationToken token)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try { listener.IOControl(SioUdpConnReset, new byte[] { 0 }, null); }
        catch (SocketException) { /* Windows 가 아니면 지원하지 않는다 */ }

        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        socket = listener;

        var receiver = new Thread(() => ReceiveLoop(listener)) { IsBackground = true, Name = "UDP Receive" };
        receiver.Start();
        PrintBanner();

        while (!token.IsCancellationRequested)
        {
            float now = Now;
            Pump(now);
            foreach (var peer in peers.Values) peer.Channel.Update(now);
            DropTimedOut(now);
            Thread.Sleep(TickMs);
        }

        socket = null;
        Log.Info("서버를 종료합니다.");
    }

    void PrintBanner()
    {
        Log.Info($"UDP 게임 서버 시작 — 0.0.0.0:{port} (프로토콜 v{UdpProtocol.Version})");
        Log.Info($"같은 공유기 안에서 접속할 주소: {string.Join(", ", LocalAddresses())}");
        Log.Info("클라이언트 2개가 HELLO 를 보내면 경기가 시작됩니다. 종료하려면 Ctrl+C.");
    }

    // ---------------------------------------------------------------- 소켓

    void ReceiveLoop(Socket listener)
    {
        var buffer = new byte[UdpProtocol.MaxPacketSize];
        while (true)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int length = listener.ReceiveFrom(buffer, ref remote);
                var from = (IPEndPoint)remote;
                inbox.Enqueue((new IPEndPoint(from.Address, from.Port), UdpProtocol.FromBytes(buffer, length)));
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // 그 패킷만 버리고 계속 받는다
            }
            catch (ObjectDisposedException)
            {
                return; // 서버 종료
            }
            catch (SocketException)
            {
                return;
            }
        }
    }

    void SendRaw(IPEndPoint to, string text)
    {
        var s = socket;
        if (s == null) return;
        try { s.SendTo(UdpProtocol.ToBytes(text), to); }
        catch (SocketException e) { Log.Warn($"송신 실패 {to}: {e.SocketErrorCode}"); }
        catch (ObjectDisposedException) { }
    }

    // ---------------------------------------------------------------- 수신 처리 (본 스레드)

    void Pump(float now)
    {
        while (inbox.TryDequeue(out var packet))
        {
            var peer = Resolve(packet.From, packet.Text, now);
            if (peer == null) continue;
            if (!peer.Channel.Receive(packet.Text, now, out string body)) continue;

            if (!GamePacket.TryDecode(body, out var msg))
            {
                Log.Warn($"알 수 없는 본문 {peer.Name}: {body}");
                continue;
            }
            if (msg.Type == MsgType.Leave) Disconnect(peer, now, "나감");
            else room.Handle(peer, msg, now);
        }
    }

    void Disconnect(ClientPeer peer, float now, string reason)
    {
        if (!peers.Remove(peer.EndPoint)) return;
        Log.Warn($"{peer.Name} 연결 종료 ({reason})");
        room.Remove(peer, now);
    }

    /// <summary>처음 보는 주소는 HELLO 일 때만 받아준다.</summary>
    ClientPeer? Resolve(IPEndPoint from, string text, float now)
    {
        if (peers.TryGetValue(from, out var known)) return known;

        if (!UdpProtocol.TryParse(text, out var kind, out int seq, out string body) || kind != PacketKind.Reliable) return null;
        if (!GamePacket.TryDecode(body, out var hello) || hello.Type != MsgType.Hello) return null;

        var peer = new ClientPeer(from, new ReliableChannel(packet => SendRaw(from, packet), now));
        if (!room.TryAdd(peer, hello, out var reason))
        {
            // 받긴 받았다고 알려줘야 (ACK) 거절당한 클라이언트가 HELLO 를 계속 재전송하지 않는다
            SendRaw(from, UdpProtocol.Ack(seq));
            SendRaw(from, UdpProtocol.Unreliable(GamePacket.Encode(MsgType.Reject, (int)reason)));
            Log.Warn($"접속 거절 {from}: {reason}");
            return null;
        }

        peers[from] = peer;
        return peer;
    }

    void DropTimedOut(float now)
    {
        List<ClientPeer>? dead = null;
        foreach (var peer in peers.Values)
            if (peer.Channel.TimedOut(now)) (dead ??= new List<ClientPeer>()).Add(peer);
        if (dead == null) return;

        foreach (var peer in dead) Disconnect(peer, now, $"{UdpProtocol.Timeout:0}초 동안 응답 없음");
    }

    static List<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
            }
        }
        catch (NetworkInformationException e)
        {
            Log.Warn("IP 목록을 가져오지 못했습니다: " + e.Message);
        }
        if (list.Count == 0) list.Add("127.0.0.1");
        return list;
    }
}
