using Group1Server.Games;
using MiniParty.Group1Net;

namespace Group1Server.Server;

/// <summary>
/// 1:1 방 하나. Unity 의 GameManager 가 "방장"에게 기대하는 흐름을 서버가 그대로 해 준다.
///
///   Join ×2 → Matched → (두 명 다) Hello(캐릭터) → Welcome(두 캐릭터)
///   → 라운드 반복:
///       StartRound(라운드, 게임, 시드)
///       Barrier(라운드*10+1) 두 명 다 → Go → (카운트다운 뒤) 서버 게임 시작
///       Action 들: 검증 → 상대에게 중계 / 서버 게임 규칙에 반영 → 서버가 승자 결정
///       Action(승자) + RoundResult(승자, 데미지)
///       HP 0 이면 경기 끝. 아니면 Barrier(라운드*10+2) 두 명 다 → Go → 다음 라운드
/// </summary>
public sealed class Room : IGameHost
{
    enum State { Waiting, Handshake, Playing, Over, Closed }

    const int BarrierReady = 1, BarrierNext = 2; // GameManager 와 같은 값

    public readonly int Id;
    readonly G1Server server;
    readonly ClientConn[] players = new ClientConn[2];
    readonly int[] characters = { -1, -1 };
    readonly int[] hp = new int[2];
    readonly Dictionary<int, bool[]> barriers = new();
    readonly List<int> deck = new();
    readonly List<WireMsg> deferred = new();

    State state = State.Waiting;
    int round;
    int lastGame = -1;
    ServerGame game;
    int gameId;
    double gameStartAt = -1;
    bool gameBegun, roundDecided, inPlayerAction;
    double now;

    public Room(int id, G1Server server)
    {
        Id = id;
        this.server = server;
        hp[0] = hp[1] = server.Options.MaxHp;
    }

    public bool IsWaiting => state == State.Waiting && players[0] != null && players[1] == null;
    public bool IsEmpty => players[0] == null && players[1] == null;

    // ------------------------------------------------------------------------------------------ 입장 / 퇴장

    public void Add(ClientConn conn)
    {
        int index = players[0] == null ? 0 : 1;
        players[index] = conn;
        conn.Room = this;
        conn.Index = index;
    }

    public void TryBeginMatch(double now)
    {
        if (state != State.Waiting || players[0] == null || players[1] == null) return;
        state = State.Handshake;
        server.SendOp(players[0], Op.Matched, now);
        server.SendOp(players[1], Op.Matched, now);
        Log.Good($"[방{Id}] 매칭 완료: P1 {players[0].EndPoint} vs P2 {players[1].EndPoint} → 캐릭터(Hello) 기다리는 중");
    }

    public void OnLeave(ClientConn conn, string reason, double now)
    {
        players[conn.Index] = null;
        var other = players[1 - conn.Index];

        switch (state)
        {
            case State.Waiting:
                return; // 혼자 기다리던 방: 그냥 사라짐
            case State.Over:
            case State.Closed:
                break;   // 이미 끝난 경기: 남은 사람도 곧 나간다
            default:
                Log.Warn($"[방{Id}] P{conn.Index + 1} 이(가) 경기 중 나감 ({reason}) → 방 닫음");
                server.SendOp(other, Op.OpponentLeft, now);
                server.OnMatchAborted(this);
                break;
        }
        state = State.Closed;
        game = null;
    }

    // ------------------------------------------------------------------------------------------ 메시지

    public void OnNet(ClientConn from, WireMsg msg, double now)
    {
        this.now = now;
        int p = from.Index;
        switch (msg.Type)
        {
            case NetType.Hello:
                OnHello(p, msg.A);
                break;
            case NetType.Barrier:
                OnBarrier(p, msg.A);
                break;
            case NetType.Action:
                OnPlayerAction(p, msg);
                break;
            default:
                Log.Warn($"[방{Id}] P{p + 1} 이(가) 보낼 수 없는 메시지 무시: {msg}");
                break;
        }
    }

    void OnHello(int p, int character)
    {
        if (state != State.Handshake) return; // 로비가 1초마다 다시 보내는 중복 Hello
        characters[p] = Math.Clamp(character, 0, 99);
        Log.Info($"[방{Id}] P{p + 1} 캐릭터 {characters[p]}");
        if (characters[0] < 0 || characters[1] < 0) return;

        var welcome = new WireMsg { Type = NetType.Welcome, A = characters[0], B = characters[1] };
        SendBoth(welcome);
        state = State.Playing;
        Log.Good($"[방{Id}] 경기 시작! (HP {hp[0]} : {hp[1]})");
        StartNextRound();
    }

    void OnBarrier(int p, int tag)
    {
        if (state != State.Playing) return;
        if (tag / 10 != round)
        {
            Log.Warn($"[방{Id}] P{p + 1} 지난 라운드의 Barrier {tag} 무시 (현재 {round}라운드)");
            return;
        }
        if (!barriers.TryGetValue(tag, out var arrived)) barriers[tag] = arrived = new bool[2];
        if (arrived[p]) return;
        arrived[p] = true;
        Log.Debug($"[방{Id}] P{p + 1} Barrier {tag} 도착");
        if (!arrived[1 - p]) return;

        barriers.Remove(tag);
        SendBoth(new WireMsg { Type = NetType.Go, A = tag });

        if (tag % 10 == BarrierReady)
        {
            gameStartAt = now + G1Protocol.CountdownSeconds;
            Log.Info($"[방{Id}] 두 명 다 준비 → Go. {G1Protocol.CountdownSeconds}초 카운트다운 뒤 '{G1Game.Name(gameId)}' 시작");
        }
        else if (tag % 10 == BarrierNext)
        {
            StartNextRound();
        }
    }

    void OnPlayerAction(int p, WireMsg msg)
    {
        if (state != State.Playing || game == null) return;
        if (msg.D != round)
        {
            Log.Debug($"[방{Id}] P{p + 1} 지난 라운드 액션 무시: {msg}");
            return;
        }
        if (roundDecided) return; // 승부가 난 뒤 늦게 온 입력
        if (msg.B != p || msg.A == G1Game.KindWinner)
        {
            Log.Warn($"[방{Id}] P{p + 1} 거절: 자기 플레이어가 아닌 액션 / 시스템 액션 위조 {msg}");
            return;
        }

        int a = msg.C;
        float f = msg.F;
        inPlayerAction = true;
        try
        {
            if (!game.OnPlayerAction(p, msg.A, ref a, ref f, out string reject))
            {
                deferred.Clear();
                Log.Warn($"[방{Id}] P{p + 1} '{G1Game.Name(gameId)}' 입력 거절: {reject} {msg}");
                return;
            }
        }
        finally
        {
            inPlayerAction = false;
        }

        // 검증된 입력만 상대에게 중계 (보낸 사람은 이미 자기 화면에 적용했으므로 다시 보내지 않는다)
        server.SendNet(players[1 - p], new WireMsg { Type = NetType.Action, A = msg.A, B = p, C = a, D = round, F = f }, now);

        // 이 입력 때문에 서버 게임이 낸 결과(히트 결과, 승자 등)는 중계 "뒤에" 보낸다
        foreach (var m in deferred) SendBoth(m);
        deferred.Clear();
    }

    // ------------------------------------------------------------------------------------------ 라운드

    void StartNextRound()
    {
        round++;
        if (deck.Count == 0)
        {
            for (int i = 0; i < G1Game.Count; i++) deck.Add(i);
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = server.Rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            if (deck.Count > 1 && deck[0] == lastGame) (deck[0], deck[^1]) = (deck[^1], deck[0]);
        }
        gameId = deck[0];
        deck.RemoveAt(0);
        if (server.Options.OnlyGame is int only) gameId = only;
        lastGame = gameId;

        int seed = server.Rng.Next(1, int.MaxValue);
        game = ServerGame.Create(gameId, this);
        gameBegun = roundDecided = false;
        gameStartAt = -1;
        barriers.Clear();

        SendBoth(new WireMsg { Type = NetType.StartRound, A = round, B = gameId, C = seed });
        Log.Game($"[방{Id}] ===== {round}라운드: {G1Game.Name(gameId)} (시드 {seed}) =====");
    }

    public void Tick(double now, double dt)
    {
        this.now = now;
        if (state != State.Playing || game == null || roundDecided) return;
        if (!gameBegun)
        {
            if (gameStartAt < 0 || now < gameStartAt) return;
            gameBegun = true;
            game.Begin();
            return;
        }
        game.Tick(dt);
    }

    // ------------------------------------------------------------------------------------------ IGameHost (서버 게임 → 방)

    public Random Rng => server.Rng;
    public double Now => now;

    /// <summary>서버(시스템) 액션을 두 명 모두에게. Player = -1.</summary>
    public void Broadcast(int kind, int a, float f)
    {
        var msg = new WireMsg { Type = NetType.Action, A = kind, B = -1, C = a, D = round, F = f };
        if (inPlayerAction) deferred.Add(msg);
        else SendBoth(msg);
    }

    public void Declare(int winner, string reason)
    {
        if (roundDecided || state != State.Playing) return;
        roundDecided = true;
        int loser = 1 - winner;
        int damage = server.Options.Damage ?? server.Rng.Next(25, 36);
        hp[loser] = Math.Max(0, hp[loser] - damage);

        Broadcast(G1Game.KindWinner, winner, 0f);
        var result = new WireMsg { Type = NetType.RoundResult, A = winner, B = damage };
        if (inPlayerAction) deferred.Add(result);
        else SendBoth(result);

        Log.Game($"[방{Id}] {round}라운드 '{G1Game.Name(gameId)}' 승자: P{winner + 1} ({reason}) → P{loser + 1} -{damage} HP (HP {hp[0]} : {hp[1]})");
        if (hp[loser] <= 0)
        {
            state = State.Over;
            Log.Good($"[방{Id}] 경기 종료! 최종 승자 P{winner + 1} ({round}라운드)");
            server.OnMatchFinished(this);
        }
    }

    public void Note(string text) => Log.Info($"[방{Id}] {G1Game.Name(gameId)}: {text}");

    void SendBoth(WireMsg msg)
    {
        server.SendNet(players[0], msg, now);
        server.SendNet(players[1], msg, now);
    }
}
