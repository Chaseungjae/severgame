using MiniParty.Group1Net;

namespace Group1Server.Games;

/// <summary>방(Room)이 서버 게임에게 제공하는 기능.</summary>
public interface IGameHost
{
    /// <summary>시스템 액션을 두 클라이언트 모두에게 (Player = -1).</summary>
    void Broadcast(int kind, int a, float f);
    /// <summary>이번 라운드 승자 확정. 한 번만 효과가 있다.</summary>
    void Declare(int winner, string reason);
    void Note(string text);
    Random Rng { get; }
    /// <summary>서버 시각(초). 게임 시작 전에도 흐른다.</summary>
    double Now { get; }
}

/// <summary>
/// 서버가 돌리는 1조 미니게임 규칙. Unity 쪽 게임 스크립트에서 "방장(IsAuthority)만 하던 일"을 옮겨 온 것.
///  - OnPlayerAction: 클라이언트 입력 검증 + 상태 반영. false 면 거절 (상대에게 중계하지 않음).
///                    a / f 를 바꾸면 바뀐 값이 상대에게 중계된다 (예: 서버가 센 점수로 덮어쓰기).
///  - Begin / Tick : 카운트다운이 끝난 뒤부터 서버 시계로 진행 (시간 제한, 히트 시작 등).
/// </summary>
public abstract class ServerGame
{
    protected readonly IGameHost Host;
    protected double Elapsed;
    protected bool Decided;

    protected ServerGame(IGameHost host) => Host = host;

    public static ServerGame Create(int id, IGameHost host) => id switch
    {
        G1Game.SpaceTug => new SpaceTugRules(host),
        G1Game.Cps => new CpsRules(host),
        G1Game.F1Reaction => new F1ReactionRules(host),
        G1Game.Aim => new AimRules(host),
        G1Game.Stack => new StackRules(host),
        G1Game.Timing => new TimingRules(host),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "1조 게임 번호가 아닙니다"),
    };

    public virtual void Begin() { }

    public void Tick(double dt)
    {
        Elapsed += dt;
        if (!Decided) OnTick(dt);
    }

    protected virtual void OnTick(double dt) { }

    public abstract bool OnPlayerAction(int p, int kind, ref int a, ref float f, out string reject);

    protected void Win(int p, string reason)
    {
        if (Decided) return;
        Decided = true;
        Host.Declare(p, reason);
    }

    protected static bool Fail(out string reject, string why)
    {
        reject = why;
        return false;
    }

    protected static bool Ok(out string reject)
    {
        reject = null;
        return true;
    }
}

/// <summary>최근 1초 동안의 입력 수를 세어 사람이 낼 수 없는 연타(매크로·패킷 조작)를 막는다.</summary>
public sealed class RateLimit
{
    readonly int perSecond;
    readonly Queue<double> times = new();

    public RateLimit(int perSecond) => this.perSecond = perSecond;

    public bool Allow(double now)
    {
        while (times.Count > 0 && now - times.Peek() > 1.0) times.Dequeue();
        if (times.Count >= perSecond) return false;
        times.Enqueue(now);
        return true;
    }
}

/// <summary>"누가 더 오래 버티나" 판정 (Unity 의 SurvivalJudge 와 같은 규칙).</summary>
public sealed class SurvivalJudge
{
    public readonly int[] Score = new int[2];
    public readonly bool[] Out = new bool[2];
    public readonly float[] TieBreak = new float[2];

    /// <summary>승자, 아직 모르면 -1.</summary>
    public int Check()
    {
        for (int p = 0; p < 2; p++)
            if (Out[1 - p] && Score[p] > Score[1 - p]) return p;

        if (Out[0] && Out[1])
        {
            if (Score[0] != Score[1]) return Score[0] > Score[1] ? 0 : 1;
            return TieBreak[0] <= TieBreak[1] ? 0 : 1;
        }
        return -1;
    }
}
