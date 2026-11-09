using System.Net;
using Group1Server.Net;
using MiniParty.Group1Net;

namespace Group1Server.Bot;

/// <summary>
/// 테스트용 가짜 클라이언트. Unity 클라이언트와 똑같은 패킷을 보내며 GameManager 흐름
/// (Hello → StartRound → Barrier → 플레이 → RoundResult → Barrier ...)을 따라가고,
/// 게임마다 사람처럼 보이는 입력을 만든다. Unity 없이 서버를 시험하거나, Unity 1대 + 봇으로 대전할 때 쓴다.
/// </summary>
public sealed class BotClient : IDisposable
{
    readonly string name;
    readonly UdpSocket socket;
    readonly IPEndPoint server;
    readonly Random rng;
    readonly int nonce;
    readonly int character;
    readonly List<(double At, Action Run)> timers = new();
    ReliableChannel channel;
    double now, lastStep = -1, joinStarted = -1, nextJoin, nextPing;
    int pingId;

    public int Index { get; private set; } = -1;
    public int RoomId { get; private set; }
    public bool Matched { get; private set; }
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    public string FailReason { get; private set; }
    public int Rounds { get; private set; }
    public int MatchWinner { get; private set; } = -1;
    public readonly HashSet<int> GamesSeen = new();
    public readonly int[] Hp = { 100, 100 };

    int round, gameId;
    BotGame game;
    bool playing; // 카운트다운이 끝나 입력을 만들기 시작했는지

    public bool Done => Finished || Failed;
    string Tag => Index >= 0 ? $"[{name} 방{RoomId} P{Index + 1}]" : $"[{name}]";

    public BotClient(string name, IPEndPoint server, int seed)
    {
        this.name = name;
        this.server = server;
        rng = new Random(seed);
        nonce = rng.Next(1, int.MaxValue);
        character = rng.Next(0, 4);
        socket = new UdpSocket(0);
    }

    // ------------------------------------------------------------------------------------------ 루프

    public void Step(double now)
    {
        double dt = lastStep < 0 ? 0 : now - lastStep;
        lastStep = now;
        this.now = now;
        if (joinStarted < 0) joinStarted = now;
        while (socket.TryReceive(out var from, out var data))
        {
            if (!from.Equals(server)) continue; // 서버가 아닌 곳에서 온 패킷은 무시
            if (!Packet.TryParse(data, data.Length, out var packet, out bool badVersion))
            {
                if (badVersion) Fail("서버와 프로토콜 버전이 다름");
                continue;
            }
            if (channel == null)
            {
                // 아직 JoinOk 전: 채널 없이 직접 해석
                if (packet.Channel == Channel.Unreliable) OnPayload(packet.Payload);
                continue;
            }
            channel.Receive(packet, now, OnPayload);
        }
        if (Done) return;

        if (channel == null)
        {
            if (now - joinStarted > G1Protocol.JoinTimeout) Fail("서버 응답 없음 (주소/포트/방화벽 확인)");
            else if (now >= nextJoin)
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
        if (now - channel.LastHeard > G1Protocol.Timeout) Fail("서버와 연결 끊김 (시간 초과)");
        channel.Update(now);

        for (int i = 0; i < timers.Count; i++)
        {
            if (timers[i].At > now) continue;
            var run = timers[i].Run;
            timers.RemoveAt(i--);
            run();
        }
        if (playing) game?.Tick(dt);
    }

    void After(double seconds, Action run) => timers.Add((now + seconds, run));

    void SendRaw(byte[] data) => socket.Send(server, data);

    void Send(WireMsg msg) => channel?.SendReliable(Payloads.Net(msg), now);

    // ------------------------------------------------------------------------------------------ 수신

    void OnPayload(byte[] payload)
    {
        var r = new PacketReader(payload, 0, payload.Length);
        var op = (Op)r.Byte();
        switch (op)
        {
            case Op.JoinOk:
                if (channel != null) return;
                Index = r.Byte();
                RoomId = r.Int();
                if (r.Int() != nonce) return;
                channel = new ReliableChannel(SendRaw, now);
                Log.Info($"{Tag} 접속 성공 → 상대 기다리는 중");
                break;

            case Op.Reject:
                Fail("서버가 거절: " + (RejectReason)r.Byte());
                break;

            case Op.Matched:
                Matched = true;
                Log.Info($"{Tag} 매칭 완료 → Hello(캐릭터 {character})");
                Send(new WireMsg { Type = NetType.Hello, A = character });
                break;

            case Op.OpponentLeft:
                Fail("상대가 나감");
                break;

            case Op.Net:
                OnNet(Payloads.ReadNet(r));
                break;
        }
    }

    void OnNet(WireMsg m)
    {
        switch (m.Type)
        {
            case NetType.Welcome:
                Log.Debug($"{Tag} Welcome: 캐릭터 {m.A} vs {m.B}");
                break;

            case NetType.StartRound:
                round = m.A;
                gameId = m.B;
                GamesSeen.Add(gameId);
                // Unity 처럼 라운드 시작 때 게임을 만든다 (카운트다운 중에 온 서버 액션도 받을 수 있게)
                game = BotGame.Create(gameId, this, rng);
                playing = false;
                // 게임 소개 카드를 읽는 척 잠깐 기다렸다가 준비
                After(0.5 + rng.NextDouble() * 0.8, () => Send(new WireMsg { Type = NetType.Barrier, A = round * 10 + 1 }));
                break;

            case NetType.Go:
                if (m.A == round * 10 + 1)
                    After(G1Protocol.CountdownSeconds, () => playing = true);
                break;

            case NetType.Action:
                if (m.D != round) break;
                if (m.A == G1Game.KindWinner)
                {
                    game = null;
                    playing = false;
                    break;
                }
                if (m.B < 0) game?.OnSystem(m.A, m.C, m.F); // 서버가 보낸 시스템 액션 (상대 입력은 무시)
                break;

            case NetType.RoundResult:
            {
                Rounds++;
                int loser = 1 - m.A;
                Hp[loser] = Math.Max(0, Hp[loser] - m.B);
                string me = m.A == Index ? "승리" : "패배";
                Log.Debug($"{Tag} {round}라운드 {me} (HP {Hp[0]} : {Hp[1]})");
                if (Hp[loser] <= 0)
                {
                    MatchWinner = m.A;
                    After(0.5, () =>
                    {
                        channel.SendUnreliable(Payloads.Simple(Op.Leave));
                        Finished = true;
                        Log.Good($"{Tag} 경기 끝 ({Rounds}라운드, {(MatchWinner == Index ? "최종 승리" : "최종 패배")}) → 나가기");
                    });
                }
                else
                {
                    // 결과·벌칙 연출을 보는 척 잠깐 기다렸다가 다음 라운드 준비
                    After(0.6 + rng.NextDouble() * 0.8, () => Send(new WireMsg { Type = NetType.Barrier, A = round * 10 + 2 }));
                }
                break;
            }
        }
    }

    /// <summary>봇 게임이 자기 입력을 보낼 때.</summary>
    public void Emit(int kind, int a = 0, float f = 0f) =>
        Send(new WireMsg { Type = NetType.Action, A = kind, B = Index, C = a, D = round, F = f });

    void Fail(string reason)
    {
        if (Done) return;
        Failed = true;
        FailReason = reason;
        Log.Error($"{Tag} {reason}");
        channel?.SendUnreliable(Payloads.Simple(Op.Leave));
    }

    public void Dispose()
    {
        if (!Done) channel?.SendUnreliable(Payloads.Simple(Op.Leave));
        socket.Dispose();
    }
}
