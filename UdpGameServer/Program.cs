using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MiniParty.Udp;

namespace MiniParty.Server
{
    /// <summary>
    /// MiniParty dedicated UDP game server.
    ///   UdpGameServer.exe [port] [--quiet] [--loss 20]
    /// --loss N drops N% of incoming packets on purpose, to test that the reliable channel copes with UDP loss.
    /// Clients join rooms of two (quick match pairs whoever is waiting, or a named room "address#ROOM").
    /// The first player in a room is P1 = the game's authority (round picks, results); the server relays every
    /// game message to the other player over a reliable, ordered UDP channel and watches heartbeats.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int port = UdpWire.DefaultPort;
            bool quiet = false;
            int loss = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--quiet") quiet = true;
                else if (args[i] == "--loss" && i + 1 < args.Length) int.TryParse(args[++i], out loss);
                else if (int.TryParse(args[i], out int p)) port = p;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var server = new GameServer(port, quiet, Math.Clamp(loss, 0, 90));
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                server.Stop();
            };
            return server.Run();
        }
    }

    internal sealed class Client
    {
        public IPEndPoint EndPoint;
        public ReliableChannel Channel;
        public Room Room;
        public int Index = -1;
        public string Tag => Room == null ? EndPoint.ToString() : $"{Room.Id}/P{Index + 1}";
    }

    internal sealed class Room
    {
        public string Id;
        public bool Named;
        public bool Closed;          // a player left mid-game: nobody new joins this room
        public readonly Client[] Slots = new Client[2];
        public int Count => Slots.Count(s => s != null);
        public Client Other(Client c) => Slots[0] == c ? Slots[1] : Slots[0];
    }

    internal sealed class GameServer
    {
        private readonly int port;
        private readonly bool quiet;
        private readonly int lossPercent;
        private readonly Random lossRandom = new();
        private long dropped;
        private readonly Dictionary<IPEndPoint, Client> clients = new();
        private readonly Dictionary<string, Room> rooms = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<string> bodies = new();
        private Socket socket;
        private volatile bool running = true;
        private int quickRooms;
        private long relayed;

        private double Now => clock.Elapsed.TotalSeconds;

        public GameServer(int port, bool quiet, int lossPercent)
        {
            this.port = port;
            this.quiet = quiet;
            this.lossPercent = lossPercent;
        }

        public void Stop() => running = false;

        public int Run()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { socket.IOControl(UdpWire.SIO_UDP_CONNRESET, new byte[4], null); } catch { /* not Windows */ }
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException e)
            {
                Log($"포트 {port} 를 열 수 없어요: {e.SocketErrorCode} (이미 실행 중인 서버가 있나요?)");
                return 1;
            }

            Log($"MiniParty UDP 게임 서버 시작 — 포트 {port} (프로토콜 {UdpWire.Version})");
            Log($"친구들은 게임에서 [온라인 1:1] → [IP / 서버 연결] → [UDP 서버 접속] 에 이 주소를 입력: {string.Join(", ", LocalAddresses())}");
            if (lossPercent > 0) Log($"[테스트] 받은 패킷의 {lossPercent}% 를 일부러 버립니다 (재전송 확인용)");
            Log("종료: Ctrl+C");

            var buffer = new byte[4096];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            double lastStatus = 0;
            while (running)
            {
                try
                {
                    // drain everything that arrived, then do timers
                    while (socket.Poll(20_000, SelectMode.SelectRead))
                    {
                        int n = socket.ReceiveFrom(buffer, ref from);
                        HandlePacket((IPEndPoint)from, UdpWire.Utf8.GetString(buffer, 0, n));
                        if (!socket.Poll(0, SelectMode.SelectRead)) break;
                    }
                }
                catch (SocketException e)
                {
                    if (!quiet) Log($"수신 오류 무시: {e.SocketErrorCode}");
                }

                double now = Now;
                foreach (var c in clients.Values.ToList())
                {
                    c.Channel.Tick(now);
                    if (c.Channel.Failed || now - c.Channel.LastHeard > UdpWire.Timeout)
                        Drop(c, c.Channel.Failed ? "응답 없음(재전송 실패)" : "시간 초과");
                }
                if (now - lastStatus > 30)
                {
                    lastStatus = now;
                    if (clients.Count > 0) Log($"[상태] 접속 {clients.Count}명, 방 {rooms.Count}개, 중계한 메시지 {relayed}개{(lossPercent > 0 ? $", 일부러 버린 패킷 {dropped}개" : "")}");
                }
            }

            foreach (var c in clients.Values) c.Channel.SendUnreliable("PEER_LEFT");
            socket.Close();
            Log("서버를 종료합니다.");
            return 0;
        }

        private void HandlePacket(IPEndPoint from, string packet)
        {
            if (lossPercent > 0 && lossRandom.Next(100) < lossPercent)
            {
                dropped++;
                return;
            }
            double now = Now;
            if (!clients.TryGetValue(from, out var client))
            {
                // a new endpoint must start with the reliable HELLO (R|1|HELLO|...)
                if (!packet.StartsWith("R|1|HELLO|", StringComparison.Ordinal)) return;
                var ep = new IPEndPoint(from.Address, from.Port);
                client = new Client { EndPoint = ep };
                client.Channel = new ReliableChannel(data => SendRaw(data, ep), now);
                clients[ep] = client;
            }

            bodies.Clear();
            client.Channel.Receive(packet, now, bodies);
            foreach (var body in bodies.ToArray())
            {
                if (!clients.ContainsKey(client.EndPoint)) break; // dropped while handling
                HandleBody(client, body, now);
            }
        }

        private void HandleBody(Client c, string body, double now)
        {
            if (body.StartsWith("MSG|", StringComparison.Ordinal))
            {
                var other = c.Room?.Other(c);
                if (other == null) return; // not matched yet: nothing to relay to
                other.Channel.SendReliable(body, now);
                relayed++;
                if (!quiet) Log($"{c.Tag} → P{other.Index + 1}  {Describe(body)}");
                return;
            }

            var f = body.Split('|');
            switch (f[0])
            {
                case "HELLO":
                    if (c.Room != null) return; // duplicate
                    if (f.Length < 3 || f[1] != UdpWire.Version)
                    {
                        Reject(c, "VERSION", $"버전 불일치 ({(f.Length > 1 ? f[1] : "?")} ≠ {UdpWire.Version})");
                        return;
                    }
                    Join(c, f[2].Trim().ToUpperInvariant(), now);
                    break;
                case "PING":
                    c.Channel.SendUnreliable("PONG");
                    break;
                case "BYE":
                    Drop(c, "나감");
                    break;
                default:
                    if (!quiet) Log($"{c.Tag}: 알 수 없는 메시지 무시 ({body})");
                    break;
            }
        }

        private void Join(Client c, string roomName, double now)
        {
            Room room;
            if (roomName.Length > 0)
            {
                if (!rooms.TryGetValue(roomName, out room))
                    rooms[roomName] = room = new Room { Id = roomName, Named = true };
                if (room.Closed || room.Count >= 2)
                {
                    Reject(c, "FULL", $"방 {roomName} 이(가) 꽉 찼거나 게임이 끝나는 중");
                    return;
                }
            }
            else
            {
                room = rooms.Values.FirstOrDefault(r => !r.Named && !r.Closed && r.Count == 1);
                if (room == null)
                {
                    string id = "Q" + (++quickRooms);
                    rooms[id] = room = new Room { Id = id };
                }
            }

            int slot = room.Slots[0] == null ? 0 : 1;
            room.Slots[slot] = c;
            c.Room = room;
            c.Index = slot;
            c.Channel.SendReliable($"WELCOME|{slot}|{room.Id}", now);
            Log($"입장 {c.EndPoint} → 방 {room.Id} P{slot + 1} ({room.Count}/2){(slot == 0 ? " · 방장(판정 담당)" : "")}");

            if (room.Count == 2)
            {
                foreach (var s in room.Slots) s.Channel.SendReliable("MATCH", now);
                Log($"방 {room.Id} 매칭 완료: P1 {room.Slots[0].EndPoint} vs P2 {room.Slots[1].EndPoint}");
            }
        }

        private void Reject(Client c, string reason, string log)
        {
            c.Channel.SendUnreliable("REJECT|" + reason);
            clients.Remove(c.EndPoint);
            Log($"거절 {c.EndPoint}: {log}");
        }

        private void Drop(Client c, string why)
        {
            if (!clients.Remove(c.EndPoint)) return;
            Log($"퇴장 {c.Tag} ({why})");
            var room = c.Room;
            if (room == null) return;
            room.Slots[c.Index] = null;
            var other = room.Other(c);
            if (other != null)
            {
                room.Closed = true;
                other.Channel.SendReliable("PEER_LEFT", Now);
                Log($"방 {room.Id}: 상대 P{other.Index + 1} 에게 PEER_LEFT 전송");
            }
            if (room.Count == 0)
            {
                rooms.Remove(room.Id);
                Log($"방 {room.Id} 삭제");
            }
        }

        private void SendRaw(byte[] data, IPEndPoint to)
        {
            try { socket.SendTo(data, to); }
            catch (SocketException e) { if (!quiet) Log($"송신 실패 {to}: {e.SocketErrorCode}"); }
        }

        // NetMsg types (Assets/Scripts/Net/NetSession.cs MsgType) for readable logs
        private static readonly string[] MsgNames = { "?", "Hello", "Welcome", "StartRound", "Barrier", "Go", "Action", "RoundResult" };

        private static string Describe(string body)
        {
            var f = body.Split('|');
            if (f.Length < 7 || !int.TryParse(f[1], out int t)) return body;
            string name = t >= 0 && t < MsgNames.Length ? MsgNames[t] : "type" + t;
            return t switch
            {
                3 => $"{name} 라운드 {f[2]}, 미니게임 #{f[3]}",
                6 => $"{name} 종류 {f[2]}, 플레이어 P{(int.TryParse(f[3], out int p) && p >= 0 ? (p + 1).ToString() : "-")}, 값 {f[4]}, 라운드 {f[5]}",
                7 => $"{name} 승자 P{(int.TryParse(f[2], out int w) ? w + 1 : 0)}, 데미지 {f[3]}",
                _ => $"{name} {f[2]} {f[3]}",
            };
        }

        private static IEnumerable<string> LocalAddresses()
        {
            var list = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
            }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        private static void Log(string text) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
    }
}
