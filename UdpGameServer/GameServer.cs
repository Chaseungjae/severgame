using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MiniParty.Server.Games;
using MiniParty.Udp;

namespace MiniParty.Server
{
    internal sealed class Client
    {
        public IPEndPoint EndPoint;
        public ReliableChannel Channel;
        public Room Room;
        public int Index = -1;
        public string Tag => Room == null ? EndPoint.ToString() : $"{Room.Id}/P{Index + 1}";
    }

    /// <summary>
    /// Transport side: one UDP socket, a reliable channel per client, room assignment and timeouts.
    /// Game messages ("MSG|...") are handed to the client's <see cref="Room"/>, which runs the match.
    /// Single-threaded: everything happens in <see cref="Run"/>'s loop, so rooms need no locking.
    /// </summary>
    internal sealed class GameServer
    {
        private readonly ServerOptions options;
        private readonly Dictionary<IPEndPoint, Client> clients = new();
        private readonly Dictionary<string, Room> rooms = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<string> bodies = new();
        private readonly Random lossRandom = new();
        private Socket socket;
        private volatile bool running = true;
        private int quickRooms;
        private long dropped;

        public double Now => clock.Elapsed.TotalSeconds;
        public ServerOptions Options => options;

        public GameServer(ServerOptions options) => this.options = options;

        public void Stop() => running = false;

        public int Run()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { socket.IOControl(UdpWire.SIO_UDP_CONNRESET, new byte[4], null); } catch { /* not Windows */ }
            try
            {
                socket.Bind(new IPEndPoint(options.Bind, options.Port));
            }
            catch (SocketException e)
            {
                string why = e.SocketErrorCode == SocketError.AddressAlreadyInUse ? "이미 이 포트를 쓰는 프로그램(서버)이 있어요"
                    : e.SocketErrorCode == SocketError.AddressNotAvailable ? "이 PC의 IP 주소가 아니에요"
                    : e.SocketErrorCode.ToString();
                Log.Line($"{options.Bind}:{options.Port} 에서 서버를 열 수 없어요: {why}");
                return 1;
            }

            bool any = options.Bind.Equals(IPAddress.Any);
            Log.Line($"MiniParty UDP 게임 서버 시작 — {(any ? "모든 주소" : options.Bind.ToString())} : 포트 {options.Port} (프로토콜 {UdpWire.Version})");
            Log.Line($"진행하는 미니게임: {string.Join(" + ", Array.ConvertAll(options.Groups, Catalog.GroupName))} (방마다 첫 접속자의 씬 기준)");
            Log.Line("서버가 방장: 미니게임 추첨 · 입력 검증 · 승패 · 데미지를 서버가 정합니다");
            string address = any ? string.Join(", ", LocalAddresses()) : options.Bind.ToString();
            Log.Line($"게임에서 [온라인 1:1] → [IP / 서버 연결] → [UDP 서버 접속] 에 입력할 주소: {address} (포트 {options.Port})");
            if (options.FixedDamage > 0) Log.Line($"[테스트] 라운드 데미지 {options.FixedDamage} 고정");
            if (options.ForcedGame >= 0) Log.Line($"[테스트] 미니게임 {Catalog.Name(options.ForcedGame)} (#{options.ForcedGame}) 만 진행");
            if (options.LossPercent > 0) Log.Line($"[테스트] 받은 패킷의 {options.LossPercent}% 를 일부러 버립니다 (재전송 확인용)");
            Log.Line("종료: Ctrl+C");

            var buffer = new byte[4096];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            double lastTick = Now, lastStatus = 0;
            while (running)
            {
                try
                {
                    if (socket.Poll(10_000, SelectMode.SelectRead))
                    {
                        do
                        {
                            int n = socket.ReceiveFrom(buffer, ref from);
                            HandlePacket((IPEndPoint)from, UdpWire.Utf8.GetString(buffer, 0, n));
                        } while (socket.Available > 0);
                    }
                }
                catch (SocketException e)
                {
                    if (!options.Quiet) Log.Line($"수신 오류 무시: {e.SocketErrorCode}");
                }

                double now = Now, dt = now - lastTick;
                lastTick = now;
                foreach (var c in clients.Values.ToList())
                {
                    c.Channel.Tick(now);
                    if (c.Channel.Failed || now - c.Channel.LastHeard > UdpWire.Timeout)
                        Drop(c, c.Channel.Failed ? "응답 없음(재전송 실패)" : "시간 초과");
                }
                foreach (var room in rooms.Values.ToList()) room.Tick(dt);

                if (now - lastStatus > 30)
                {
                    lastStatus = now;
                    if (clients.Count > 0)
                        Log.Line($"[상태] 접속 {clients.Count}명, 방 {rooms.Count}개{(options.LossPercent > 0 ? $", 일부러 버린 패킷 {dropped}개" : "")}");
                }
            }

            foreach (var c in clients.Values) c.Channel.SendUnreliable("PEER_LEFT");
            socket.Close();
            Log.Line("서버를 종료합니다.");
            return 0;
        }

        private void HandlePacket(IPEndPoint from, string packet)
        {
            if (options.LossPercent > 0 && lossRandom.Next(100) < options.LossPercent)
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
                HandleBody(client, body);
            }
        }

        private void HandleBody(Client c, string body)
        {
            if (body.StartsWith("MSG|", StringComparison.Ordinal))
            {
                if (c.Room == null) return;
                if (Msg.TryParse(body, out var msg)) c.Room.OnMessage(c.Index, msg);
                else Log.Line($"{c.Tag}: 형식이 잘못된 메시지 무시 ({body})");
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
                    // HELLO|version|room|groups — groups = the client's scene ("1", "3", "1,3"; empty = every server group)
                    Join(c, f[2].Trim().ToUpperInvariant(), RoomGroups(f.Length > 3 ? f[3] : ""));
                    break;
                case "PING":
                    c.Channel.SendUnreliable("PONG");
                    break;
                case "BYE":
                    Drop(c, "나감");
                    break;
                default:
                    if (!options.Quiet) Log.Line($"{c.Tag}: 알 수 없는 메시지 무시 ({body})");
                    break;
            }
        }

        /// <summary>The client's groups limited to what this server was started with (--groups).</summary>
        private int[] RoomGroups(string requested)
        {
            var asked = Catalog.ParseGroups(requested);
            var groups = Array.FindAll(asked, g => Array.IndexOf(options.Groups, g) >= 0);
            return groups.Length > 0 ? groups : options.Groups;
        }

        private void Join(Client c, string roomName, int[] groups)
        {
            Room room;
            string key = string.Join(",", groups);
            if (roomName.Length > 0)
            {
                if (!rooms.TryGetValue(roomName, out room))
                    rooms[roomName] = room = new Room(this, roomName, named: true, groups);
                else if (room.GroupKey != key)
                    Log.Line($"방 {roomName}: 늦게 온 {c.EndPoint} 의 씬({key})은 무시하고 방 설정({room.GroupKey})으로 진행");
                if (!room.Open)
                {
                    Reject(c, "FULL", $"방 {roomName} 이(가) 꽉 찼거나 경기 중");
                    return;
                }
            }
            else
            {
                // quick match: pair players whose scenes play the same groups
                room = rooms.Values.FirstOrDefault(r => !r.Named && r.Open && r.Count == 1 && r.GroupKey == key);
                if (room == null)
                {
                    string id = "Q" + (++quickRooms);
                    rooms[id] = room = new Room(this, id, named: false, groups);
                }
            }

            int slot = room.Add(c);
            c.Room = room;
            c.Index = slot;
            c.Channel.SendReliable($"WELCOME|{slot}|{room.Id}", Now);
            Log.Line($"입장 {c.EndPoint} → 방 {room.Id} P{slot + 1} ({room.Count}/2, 미니게임 {string.Join("+", Array.ConvertAll(room.Groups, g => g + "조"))})");

            if (room.Count == 2)
            {
                room.ForEach(s => s.Channel.SendReliable("MATCH", Now));
                Log.Line($"방 {room.Id} 매칭 완료 — 캐릭터 정보를 기다리는 중");
            }
        }

        private void Reject(Client c, string reason, string log)
        {
            c.Channel.SendUnreliable("REJECT|" + reason);
            clients.Remove(c.EndPoint);
            Log.Line($"거절 {c.EndPoint}: {log}");
        }

        private void Drop(Client c, string why)
        {
            if (!clients.Remove(c.EndPoint)) return;
            Log.Line($"퇴장 {c.Tag} ({why})");
            var room = c.Room;
            if (room == null) return;
            room.Remove(c);
            if (room.Count == 0)
            {
                rooms.Remove(room.Id);
                Log.Line($"방 {room.Id} 삭제");
            }
        }

        /// <summary>Sends a game message to one player of a room (reliable, ordered).</summary>
        public void Send(Client to, Msg msg) => to?.Channel.SendReliable(msg.Format(), Now);

        public void SendRaw(byte[] data, IPEndPoint to)
        {
            try { socket.SendTo(data, to); }
            catch (SocketException e) { if (!options.Quiet) Log.Line($"송신 실패 {to}: {e.SocketErrorCode}"); }
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
    }
}
