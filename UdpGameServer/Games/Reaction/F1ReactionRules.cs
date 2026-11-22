using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// F1 반응 속도 (F1ReactionGame). 서버가 히트(Heat)를 시작하고(불 꺼지는 대기 시간도 서버가 정함),
/// 각 PC 는 자기 플레이어의 반응을 재서 Report 로 보낸다. 서버가 히트 승자를 정하고 2승 먼저면 승리.
///   부정출발(Jump) = 즉시 상대 승 / 둘 다 반응 없음 = 다시 / 아니면 더 빠른 쪽.
/// </summary>
internal sealed class F1ReactionRules : RoundRules
{
    const int WinsNeeded = 2;
    const float ResponseWindow = 1.2f;
    const double LightsTime = 5.3;      // 불 5개가 켜지는 시간 (클라이언트 LocalHeat 와 같음)
    const double NextHeatDelay = 1.8;
    const double ReportTimeout = 4.0;   // 반응 창이 끝나고도 Report 가 안 오면 "반응 없음" 처리

    int heat;
    bool resolved;
    readonly bool[] reported = new bool[2];
    readonly int[] code = new int[2];
    readonly float[] time = new float[2];
    readonly int[] wins = new int[2];
    double heatDeadline, nextHeatAt = -1;

    public F1ReactionRules(IRoundHost host) : base(host) { }

    public override void Begin() => StartHeat();

    void StartHeat()
    {
        heat++;
        resolved = false;
        reported[0] = reported[1] = false;
        float hold = 0.4f + (float)Host.Rng.NextDouble() * 2.2f; // 0.4 ~ 2.6초
        heatDeadline = Elapsed + LightsTime + hold + ResponseWindow + ReportTimeout;
        Host.Broadcast(Reaction1.F1HeatStart, heat, hold);
        Host.Note($"HEAT {heat} 시작 (대기 {hold:0.00}s, 전적 {wins[0]} : {wins[1]})");
    }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (kind != Reaction1.F1Report) return Reject(out why, $"플레이어가 보낼 수 없는 액션 {kind}");
        if (a / 10 != heat) return Reject(out why, $"다른 히트의 기록 (현재 {heat})");
        if (resolved || reported[p]) return Reject(out why, "이미 끝난 히트 / 중복 기록");
        int c = a % 10;
        if (c < Reaction1.F1CodeReact || c > Reaction1.F1CodeNone) return Reject(out why, $"알 수 없는 코드 {c}");
        if (c == Reaction1.F1CodeReact && (f < 0.05f || f > ResponseWindow + 0.05f))
            return Reject(out why, $"불가능한 반응 시간 {f:0.000}s");

        reported[p] = true;
        code[p] = c;
        time[p] = c == Reaction1.F1CodeReact ? f : 0f;
        TryResolve();
        return Relay(out why);
    }

    protected override void OnTick(double dt)
    {
        if (!resolved && Elapsed > heatDeadline)
        {
            for (int p = 0; p < 2; p++)
                if (!reported[p])
                {
                    reported[p] = true;
                    code[p] = Reaction1.F1CodeNone;
                    Host.Note($"P{p + 1} 기록이 오지 않음 → 반응 없음 처리");
                }
            TryResolve();
        }

        if (resolved && nextHeatAt >= 0 && Elapsed >= nextHeatAt)
        {
            nextHeatAt = -1;
            if (wins[0] >= WinsNeeded || wins[1] >= WinsNeeded)
                Win(wins[0] >= WinsNeeded ? 0 : 1, $"{heat}히트 만에 {wins[0]} : {wins[1]}");
            else StartHeat();
        }
    }

    void TryResolve()
    {
        if (resolved) return;
        for (int p = 0; p < 2; p++)
            if (reported[p] && code[p] == Reaction1.F1CodeJump)
            {
                Finish(1 - p, true, $"P{p + 1} 부정출발");
                return;
            }
        if (!reported[0] || !reported[1]) return;

        bool r0 = code[0] == Reaction1.F1CodeReact, r1 = code[1] == Reaction1.F1CodeReact;
        if (!r0 && !r1) Finish(-1, false, "둘 다 반응 없음 → 다시");
        else if (!r0) Finish(1, false, "P1 반응 없음");
        else if (!r1) Finish(0, false, "P2 반응 없음");
        else Finish(time[0] <= time[1] ? 0 : 1, false, $"{time[0] * 1000:0}ms : {time[1] * 1000:0}ms");
    }

    void Finish(int winner, bool byJump, string why)
    {
        resolved = true;
        Host.Broadcast(Reaction1.F1HeatResult, winner, byJump ? 1f : 0f);
        if (winner >= 0) wins[winner] = Math.Min(WinsNeeded, wins[winner] + 1);
        Host.Note($"HEAT {heat} 결과: {(winner < 0 ? "무효" : $"P{winner + 1} 승")} ({why}) → {wins[0]} : {wins[1]}");
        nextHeatAt = Elapsed + NextHeatDelay;
    }
}
