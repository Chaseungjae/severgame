using MiniParty.Server.Games;

namespace MiniParty.Server;

/// <summary>
/// 1:1 경기 하나. 두 자리(P1 · P2)를 채우고 라운드를 돌린다.
/// 미니게임 선택 · 시드 · 입력 검증 · 승패 · 데미지를 전부 여기서 정한다 (방장 PC 가 아니라 서버가 판정).
/// </summary>
internal sealed class Room
{
    public const int MaxHp = 100;
    const int MinDamage = 25, MaxDamage = 35;

    readonly UdpGameServer server;
    readonly ClientPeer?[] slots = new ClientPeer?[2];
    readonly int[] hp = { MaxHp, MaxHp };
    readonly Dictionary<int, bool[]> barriers = new();
    readonly Random rng = new();
    readonly List<int> deck = new();

    int group;
    int round;
    int gameIndex = -1;
    int lastGameIndex = -1;
    IGameRules rules = new DefaultRules();
    bool playing;
    bool matchOver;

    public Room(UdpGameServer server) => this.server = server;

    public int PlayerCount => (slots[0] != null ? 1 : 0) + (slots[1] != null ? 1 : 0);

    bool InMatch => round > 0 && !matchOver;

    // ---------------------------------------------------------------- 입장 · 퇴장

    /// <summary>HELLO 를 보낸 새 주소에게 자리를 내줄 수 있는지 판단한다.</summary>
    public bool TryAdd(ClientPeer peer, NetMsg hello, out RejectReason reason)
    {
        if (hello.A != UdpProtocol.Version)
        {
            reason = RejectReason.BadVersion;
            return false;
        }
        if (PlayerCount >= 2)
        {
            reason = RejectReason.Full;
            return false;
        }
        if (InMatch)
        {
            reason = RejectReason.InGame; // 한 명이 끊긴 자리에 난입하는 것은 막는다
            return false;
        }
        if (PlayerCount == 1 && hello.B != group)
        {
            reason = RejectReason.GroupMismatch;
            return false;
        }

        int slot = slots[0] == null ? 0 : 1;
        slots[slot] = peer;
        peer.Slot = slot;
        peer.Group = hello.B;
        if (PlayerCount == 1) group = hello.B;

        reason = RejectReason.None;
        return true;
    }

    public void Remove(ClientPeer peer, float now)
    {
        if (peer.Slot < 0 || slots[peer.Slot] != peer) return;
        slots[peer.Slot] = null;

        var other = slots[1 - peer.Slot];
        if (other != null) other.Send(MsgType.PeerLeft, now);

        Log.Event($"퇴장: {peer.Name} — 남은 인원 {PlayerCount}명");
        ResetMatch();
    }

    void ResetMatch()
    {
        round = 0;
        gameIndex = -1;
        lastGameIndex = -1;
        playing = false;
        matchOver = false;
        hp[0] = hp[1] = MaxHp;
        deck.Clear();
        barriers.Clear();
        rules = new DefaultRules();
    }

    // ---------------------------------------------------------------- 메시지 처리

    public void Handle(ClientPeer peer, NetMsg msg, float now)
    {
        switch (msg.Type)
        {
            case MsgType.Hello:
                OnHello(peer, msg, now);
                break;

            case MsgType.Barrier:
                OnBarrier(peer, msg.A, now);
                break;

            case MsgType.Action:
                OnAction(peer, msg, now);
                break;

            case MsgType.Claim:
                OnClaim(peer, msg, now);
                break;

            case MsgType.Input:
                OnInput(peer, msg);
                break;

            case MsgType.Ping:
                peer.SendUnreliable(MsgType.Pong);
                break;

            default:
                Log.Warn($"{peer.Name}: 서버가 처리하지 않는 메시지 {msg.Type}");
                break;
        }
    }

    void OnHello(ClientPeer peer, NetMsg hello, float now)
    {
        peer.Character = hello.C;
        peer.Send(MsgType.Welcome, now, peer.Slot);

        if (peer.Greeted) return;
        peer.Greeted = true;
        Log.Event($"입장: {peer.Name} — 캐릭터 {peer.Character}, {MiniGameCatalog.GroupNames[Math.Clamp(peer.Group, 0, 4)]} / 현재 {PlayerCount}명");

        var p1 = slots[0];
        var p2 = slots[1];
        if (p1 == null || p2 == null || !p1.Greeted || !p2.Greeted) return;

        Log.Event("두 명 모두 접속 완료 — 경기를 시작합니다");
        Broadcast(MsgType.Match, now, p1.Character, p2.Character);
        StartRound(now);
    }

    void OnBarrier(ClientPeer peer, int tag, float now)
    {
        if (!barriers.TryGetValue(tag, out var arrived)) barriers[tag] = arrived = new bool[2];
        if (arrived[peer.Slot]) return;
        arrived[peer.Slot] = true;

        string step = Barriers.KindOf(tag) == Barriers.Ready ? "소개 화면 확인" : "결과 화면 확인";
        int count = (arrived[0] ? 1 : 0) + (arrived[1] ? 1 : 0);
        Log.Info($"{Prefix} P{peer.Slot + 1} 준비 완료 ({step}) {count}/2");
        if (count < 2) return;

        barriers.Remove(tag);
        Log.Info($"{Prefix} 두 명 모두 준비 → 진행 신호(Go) 전송");
        Broadcast(MsgType.Go, now, tag);
        if (Barriers.KindOf(tag) == Barriers.Next) StartRound(now);
    }

    void OnAction(ClientPeer peer, NetMsg msg, float now)
    {
        if (!playing || msg.D != round) return; // 지난 라운드의 늦은 패킷

        var action = msg.ToAction();
        // player = -1 은 문제 뽑기 · 라운드 진행 같은 시스템 이벤트로, 그 일을 맡은 P1 만 보낼 수 있다
        bool systemEvent = action.Player == -1 && peer.Slot == 0;
        string what = ActionLog.Describe(gameIndex, msg);
        if (action.Player != peer.Slot && !systemEvent)
        {
            Log.Warn($"{Prefix} P{peer.Slot + 1} 거부 | 남의 플레이어({ActionLog.Who(action.Player)}) 이름으로 보낸 입력: {what}");
            return;
        }
        if (!rules.Apply(action))
        {
            Log.Warn($"{Prefix} {ActionLog.Who(action.Player)} 거부 | 규칙 위반이라 중계하지 않음: {what}");
            return;
        }

        Log.Input($"{Prefix} {ActionLog.Who(action.Player)} 중계 | {what}");
        var other = slots[1 - peer.Slot];
        if (other != null) other.Send(msg, now);

        if (rules.Winner >= 0) EndRound(rules.Winner, now, "서버 판정");
    }

    void OnClaim(ClientPeer peer, NetMsg msg, float now)
    {
        if (!playing || msg.D != round) return;
        if (!rules.TrustsClaims)
        {
            Log.Warn($"{peer.Name}: 서버가 판정하는 게임인데 승리를 주장함 — 무시");
            return;
        }
        if (msg.A is < 0 or > 1) return;
        Log.Info($"{Prefix} P{peer.Slot + 1} 승자 보고 → P{msg.A + 1}");
        EndRound(msg.A, now, "클라이언트 보고");
    }

    /// <summary>답을 확정하기 전의 키 입력처럼 상대에게 보내면 안 되는 입력. 터미널에만 남긴다.</summary>
    void OnInput(ClientPeer peer, NetMsg msg)
    {
        if (!playing || msg.D != round || msg.B != peer.Slot) return;
        Log.Input($"{Prefix} P{peer.Slot + 1} 입력 | {ActionLog.Describe(gameIndex, msg)}");
    }

    string Prefix => $"  [R{round} {MiniGameCatalog.TitleOf(gameIndex)}]";

    // ---------------------------------------------------------------- 라운드 진행

    void StartRound(float now)
    {
        if (matchOver || PlayerCount < 2) return;

        round++;
        if (deck.Count == 0) RefillDeck();
        gameIndex = deck[0];
        deck.RemoveAt(0);
        lastGameIndex = gameIndex;

        int seed = rng.Next(1, int.MaxValue);
        rules = RulesCatalog.For(gameIndex);
        rules.Begin(seed);
        playing = true;

        Log.Event($"ROUND {round} — {MiniGameCatalog.TitleOf(gameIndex)} (seed {seed}, 판정 {(rules.TrustsClaims ? "클라이언트" : "서버")}) | HP {hp[0]} : {hp[1]}");
        // D = 1 이면 승패를 서버가 정한다는 뜻 — 클라이언트는 승리 보고를 보내지 않는다
        Broadcast(MsgType.StartRound, now, round, gameIndex, seed, rules.TrustsClaims ? 0 : 1);
    }

    void EndRound(int winner, float now, string decidedBy)
    {
        if (!playing) return;
        playing = false;

        int loser = 1 - winner;
        int damage = rng.Next(MinDamage, MaxDamage + 1);
        hp[loser] = Math.Max(0, hp[loser] - damage);

        Log.Event($"ROUND {round} 결과 — 승자 P{winner + 1} ({decidedBy}), 데미지 {damage} | HP {hp[0]} : {hp[1]}");
        Broadcast(MsgType.RoundResult, now, winner, damage);

        if (hp[loser] > 0) return;
        matchOver = true;
        Log.Event($"경기 종료 — P{winner + 1} 승리 (P{loser + 1} KO)");
    }

    void RefillDeck()
    {
        var pool = MiniGameCatalog.PoolFor(group);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        // 덱을 새로 깔 때 직전 라운드와 같은 게임이 연달아 나오지 않게 한다
        if (pool.Count > 1 && pool[0] == lastGameIndex) (pool[0], pool[^1]) = (pool[^1], pool[0]);
        deck.AddRange(pool);
    }

    void Broadcast(MsgType type, float now, int a = 0, int b = 0, int c = 0, int d = 0)
    {
        foreach (var peer in slots)
            peer?.Send(type, now, a, b, c, d);
    }
}
