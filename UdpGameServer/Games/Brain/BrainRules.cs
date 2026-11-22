using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

// 2조 (두뇌·관찰) — 게임 스크립트의 방장(IsAuthority) 부분을 서버로 옮긴 것.
// 액션 번호·시간 값은 Assets/Scripts/Net/UdpServer/BrainProtocol.cs (Unity 와 같은 파일).

/// <summary>
/// 1 to 50 (OneToFiftyGame). 각자 1부터 50까지 순서대로 누른다. 서버가 플레이어마다 다음 숫자를 세고,
/// 순서가 틀린 Press 는 거절한다. 50 을 먼저 누른 사람이 승리.
/// </summary>
internal sealed class OneToFiftyRules : RoundRules
{
    const double TimeLimit = 120.0; // 둘 다 멈춰 버린 경우 대비
    readonly int[] next = { 1, 1 };

    public OneToFiftyRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        switch (kind)
        {
            case BrainProtocol.OneToFifty.Press:
                if (a < 0 || a >= 25) return Reject(out why, $"없는 칸 {a}");
                int v = (int)Math.Round(f);
                if (v != next[p]) return Reject(out why, $"순서가 아님 (누른 수 {v}, 다음 차례 {next[p]})");
                next[p]++;
                if (next[p] > BrainProtocol.OneToFifty.Goal) Win(p, $"50까지 먼저 완료 (상대는 {next[1 - p] - 1}까지)");
                return Relay(out why);

            case BrainProtocol.OneToFifty.Wrong:
                if (a < 0 || a >= 25) return Reject(out why, $"없는 칸 {a}");
                return Relay(out why);

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }
    }

    protected override void OnTick(double dt)
    {
        if (Elapsed < TimeLimit) return;
        if (next[0] != next[1]) Win(next[0] > next[1] ? 0 : 1, $"시간 초과, 진행 {next[0] - 1} : {next[1] - 1}");
        else Win(Host.Seed % 2, "시간 초과, 동점 → 추첨");
    }
}

/// <summary>
/// 색깔 찾기 (ColorGame). 30초 동안 다른 색 칸을 찾아 단계를 올린다. 단계·실수는 서버가 직접 세고,
/// 각 PC 의 Final 이 오면(또는 시간이 지나면) 단계 → 실수 → 추첨 순으로 판정.
/// </summary>
internal sealed class ColorRules : RoundRules
{
    const double LateGrace = 2.0, FinalTimeout = 8.0;
    readonly int[] level = new int[2], mistakes = new int[2];
    readonly bool[] hasFinal = new bool[2];
    readonly RateLimit[] limits = { new(15), new(15) };

    public ColorRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (hasFinal[p]) return Reject(out why, "Final 이후 입력");
        switch (kind)
        {
            case BrainProtocol.Colors.Correct:
                if (Elapsed > BrainProtocol.Colors.Duration + LateGrace) return Reject(out why, "시간이 끝난 뒤 입력");
                if (!limits[p].Allow(Host.Now)) return Reject(out why, "너무 빠른 연타");
                if (a != level[p] + 1) return Reject(out why, $"단계가 맞지 않음 ({a}, 서버 {level[p]})");
                level[p] = a;
                return Relay(out why);

            case BrainProtocol.Colors.Wrong:
                if (!limits[p].Allow(Host.Now)) return Reject(out why, "너무 빠른 연타");
                mistakes[p]++;
                a = level[p];
                return Relay(out why);

            case BrainProtocol.Colors.Final:
                hasFinal[p] = true;
                if (a != level[p] || (int)Math.Round(f) != mistakes[p])
                    Host.Note($"P{p + 1} 클라이언트 값 (단계 {a}, 실수 {f:0}) ≠ 서버 집계 (단계 {level[p]}, 실수 {mistakes[p]}) → 서버 값 사용");
                a = level[p];
                f = mistakes[p];
                if (hasFinal[0] && hasFinal[1]) Decide("두 명 다 종료");
                return Relay(out why);

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }
    }

    protected override void OnTick(double dt)
    {
        if (Elapsed < BrainProtocol.Colors.Duration + FinalTimeout) return;
        Decide("Final 시간 초과 → 서버 집계로 판정");
    }

    void Decide(string why)
    {
        string score = $"단계 {level[0]} : {level[1]}, 실수 {mistakes[0]} : {mistakes[1]}";
        if (level[0] != level[1]) Win(level[0] > level[1] ? 0 : 1, $"{why}, {score}");
        else if (mistakes[0] != mistakes[1]) Win(mistakes[0] < mistakes[1] ? 0 : 1, $"{why}, {score} → 실수 적은 쪽");
        else Win(Host.Seed % 2, $"{why}, {score} → 완전 동점, 추첨");
    }
}

/// <summary>
/// 순간 기억력 (MemoryGame). 자릿수·목숨을 서버가 관리한다. Clear 는 지금 자릿수와 같아야 하고, Fail 마다 목숨 -1.
/// 한 명이 탈락한 뒤 상대가 그 기록을 넘으면, 또는 둘 다 탈락하면 판정 (SurvivalJudge).
/// </summary>
internal sealed class MemoryRules : RoundRules
{
    readonly int[] length = { BrainProtocol.Memory.StartLength, BrainProtocol.Memory.StartLength };
    readonly int[] lives = { BrainProtocol.Memory.Lives, BrainProtocol.Memory.Lives };
    readonly SurvivalJudge judge = new();

    public MemoryRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (judge.Out[p]) return Reject(out why, "이미 탈락");
        switch (kind)
        {
            case BrainProtocol.Memory.Clear:
                if (a != length[p]) return Reject(out why, $"자릿수가 맞지 않음 ({a}, 서버 {length[p]})");
                judge.Score[p] = a - BrainProtocol.Memory.StartLength + 1;
                length[p] = a + 1;
                break;

            case BrainProtocol.Memory.Fail:
                lives[p] = Math.Max(0, lives[p] - 1);
                a = lives[p];
                if (lives[p] == 0) judge.Out[p] = true;
                break;

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }
        int w = judge.Check();
        if (w >= 0) Win(w, $"맞힌 개수 {judge.Score[0]} : {judge.Score[1]}");
        return Relay(out why);
    }
}

/// <summary>
/// 공 개수 세기 (BallCountGame). 서버가 판을 진행한다: RoundStart → 두 답 → 정답(같은 시드로 서버가 계산)에
/// 더 가까운 쪽, 같으면 먼저 확정한 쪽이 판 승리 → 3판 2선승.
/// </summary>
internal sealed class BallCountRules : RoundRules
{
    const double AnswerGrace = 3.0;
    readonly int[] wins = new int[2], answer = new int[2];
    readonly float[] lockTime = new float[2];
    readonly bool[] answered = new bool[2];
    int round, correct;
    double roundStart, pauseUntil = -1;
    bool resolved;

    public BallCountRules(IRoundHost host) : base(host) { }

    public override void Begin() => StartRound();

    void StartRound()
    {
        round++;
        resolved = false;
        answered[0] = answered[1] = false;
        BrainProtocol.BallCount.Setup(Host.Seed, round, out bool askAll, out int color, out int total, out correct);
        roundStart = Elapsed;
        Host.Broadcast(BrainProtocol.BallCount.RoundStart, round);
        Host.Note($"공 개수 세기 {round}판 시작 — {(askAll ? "전체" : $"{ColorName(color)} 공")} 정답 {correct}개 (전체 {total}개)");
    }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (kind != BrainProtocol.BallCount.Answer) return Reject(out why, $"알 수 없는 액션 {kind}");
        if (resolved || a / 1000 != round) return Reject(out why, $"지난 판의 답 ({a / 1000}판)");
        if (answered[p]) return Reject(out why, "답을 두 번 보냄");
        int value = a % 1000;
        if (value < 0 || value > 99) return Reject(out why, $"말이 안 되는 답 {value}");
        answered[p] = true;
        answer[p] = value;
        lockTime[p] = Math.Clamp(f, 0f, BrainProtocol.BallCount.AnswerTime);
        f = lockTime[p];
        TryResolve();
        return Relay(out why);
    }

    protected override void OnTick(double dt)
    {
        if (pauseUntil >= 0)
        {
            if (Elapsed < pauseUntil) return;
            pauseUntil = -1;
            if (wins[0] >= BrainProtocol.BallCount.WinsNeeded || wins[1] >= BrainProtocol.BallCount.WinsNeeded)
                Win(wins[0] > wins[1] ? 0 : 1, $"판 승리 {wins[0]} : {wins[1]}");
            else StartRound();
            return;
        }
        if (resolved) return;
        if (Elapsed - roundStart < BrainProtocol.BallCount.WatchTime + BrainProtocol.BallCount.AnswerTime + AnswerGrace) return;
        for (int p = 0; p < 2; p++)
            if (!answered[p])
            {
                answered[p] = true;
                answer[p] = 0;
                lockTime[p] = BrainProtocol.BallCount.AnswerTime;
                Host.Note($"P{p + 1} 답이 오지 않음 → 0개로 처리");
            }
        TryResolve();
    }

    void TryResolve()
    {
        if (resolved || !answered[0] || !answered[1]) return;
        resolved = true;
        int d0 = Math.Abs(answer[0] - correct), d1 = Math.Abs(answer[1] - correct);
        int w = d0 != d1 ? (d0 < d1 ? 0 : 1) : (lockTime[0] <= lockTime[1] ? 0 : 1);
        wins[w]++;
        Host.Broadcast(BrainProtocol.BallCount.RoundResult, w);
        Host.Note($"공 개수 세기 {round}판: 정답 {correct}, 답 {answer[0]} : {answer[1]} ({lockTime[0]:0.00}s : {lockTime[1]:0.00}s) → P{w + 1} 승 (판 {wins[0]} : {wins[1]})");
        pauseUntil = Elapsed + BrainProtocol.BallCount.ResultPause;
    }

    internal static string ColorName(int c) => c switch { 0 => "빨간", 1 => "파란", 2 => "초록", 3 => "노란", _ => "?" };
}

/// <summary>
/// 10초 맞추기 (TenSecondsGame). 서버가 판을 진행한다: RoundStart → 두 Stop → 10.00초에 더 가까운 쪽이 판 승리
/// (완전 동점은 추첨) → 3판 2선승. 끝내 Stop 이 안 오면 포기 시간(15초)으로 처리.
/// </summary>
internal sealed class TenSecondsRules : RoundRules
{
    const double StopGrace = 3.0;
    readonly int[] wins = new int[2];
    readonly float[] time = new float[2];
    readonly bool[] stopped = new bool[2];
    int round;
    double roundStart, pauseUntil = -1;
    bool resolved;

    public TenSecondsRules(IRoundHost host) : base(host) { }

    public override void Begin() => StartRound();

    void StartRound()
    {
        round++;
        resolved = false;
        stopped[0] = stopped[1] = false;
        roundStart = Elapsed;
        Host.Broadcast(BrainProtocol.TenSeconds.RoundStart, round);
    }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (kind != BrainProtocol.TenSeconds.Stop) return Reject(out why, $"알 수 없는 액션 {kind}");
        if (resolved || a != round) return Reject(out why, $"지난 판의 기록 ({a}판)");
        if (stopped[p]) return Reject(out why, "두 번 멈춤");
        stopped[p] = true;
        time[p] = Math.Clamp(f, 0f, BrainProtocol.TenSeconds.GiveUp);
        f = time[p];
        TryResolve();
        return Relay(out why);
    }

    protected override void OnTick(double dt)
    {
        if (pauseUntil >= 0)
        {
            if (Elapsed < pauseUntil) return;
            pauseUntil = -1;
            if (wins[0] >= BrainProtocol.TenSeconds.WinsNeeded || wins[1] >= BrainProtocol.TenSeconds.WinsNeeded)
                Win(wins[0] > wins[1] ? 0 : 1, $"판 승리 {wins[0]} : {wins[1]}");
            else StartRound();
            return;
        }
        if (resolved || Elapsed - roundStart < BrainProtocol.TenSeconds.GiveUp + StopGrace) return;
        for (int p = 0; p < 2; p++)
            if (!stopped[p])
            {
                stopped[p] = true;
                time[p] = BrainProtocol.TenSeconds.GiveUp;
                Host.Note($"P{p + 1} 기록이 오지 않음 → {BrainProtocol.TenSeconds.GiveUp:0}초로 처리");
            }
        TryResolve();
    }

    void TryResolve()
    {
        if (resolved || !stopped[0] || !stopped[1]) return;
        resolved = true;
        float d0 = Math.Abs(time[0] - BrainProtocol.TenSeconds.Target), d1 = Math.Abs(time[1] - BrainProtocol.TenSeconds.Target);
        int w = Math.Abs(d0 - d1) < 0.0005f ? Host.Seed % 2 : d0 < d1 ? 0 : 1;
        wins[w]++;
        Host.Broadcast(BrainProtocol.TenSeconds.RoundResult, w);
        Host.Note($"10초 맞추기 {round}판: {time[0]:0.00}s : {time[1]:0.00}s → P{w + 1} 승 (판 {wins[0]} : {wins[1]})");
        pauseUntil = Elapsed + BrainProtocol.TenSeconds.ResultPause;
    }
}
