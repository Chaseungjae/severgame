using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// CPS 클릭 대결 (CpsGame). 5초 동안 클릭 수. 각 PC 가 자기 5초를 재고(지연 때문에 클릭을 잃지 않게)
/// 끝나면 Final 을 보낸다. 점수는 서버가 직접 센 클릭 수로 판정하고, 상대에게도 그 값을 중계한다.
/// 동점이면 마지막 클릭이 더 빨랐던 사람이 승리.
/// </summary>
internal sealed class CpsRules : RoundRules
{
    const float Duration = 5f;
    const double LateGrace = 2.0;   // 서버 시계와 클라 시계 차이 허용
    const double FinalTimeout = 8.0; // Final 이 끝내 안 오면 서버가 센 값으로 판정
    const int MaxClicksPerSecond = 25;

    readonly int[] clicks = new int[2];
    readonly bool[] hasFinal = new bool[2];
    readonly float[] lastClick = new float[2];
    readonly RateLimit[] limits = { new(MaxClicksPerSecond), new(MaxClicksPerSecond) };

    public CpsRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        switch (kind)
        {
            case Reaction1.CpsClick:
                if (hasFinal[p]) return Reject(out why, "Final 이후 클릭");
                if (Elapsed > Duration + LateGrace) return Reject(out why, "시간이 끝난 뒤 클릭");
                if (!limits[p].Allow(Host.Now)) return Reject(out why, $"초당 {MaxClicksPerSecond}회 초과");
                clicks[p]++;
                a = 0;
                f = 0f;
                return Relay(out why);

            case Reaction1.CpsFinal:
                if (hasFinal[p]) return Reject(out why, "Final 중복");
                hasFinal[p] = true;
                if (a != clicks[p]) Host.Note($"P{p + 1} 클라이언트 클릭 수 {a} ≠ 서버 집계 {clicks[p]} → 서버 값 사용");
                a = clicks[p];
                lastClick[p] = Math.Clamp(f, 0f, Duration);
                f = lastClick[p];
                if (hasFinal[0] && hasFinal[1]) Decide("두 명 다 Final");
                return Relay(out why);

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }
    }

    protected override void OnTick(double dt)
    {
        if (Elapsed < Duration + FinalTimeout) return;
        for (int p = 0; p < 2; p++)
            if (!hasFinal[p])
            {
                hasFinal[p] = true;
                lastClick[p] = Duration;
            }
        Decide("Final 시간 초과 → 서버 집계로 판정");
    }

    void Decide(string why)
    {
        if (clicks[0] != clicks[1]) Win(clicks[0] > clicks[1] ? 0 : 1, $"{why}, 클릭 {clicks[0]} : {clicks[1]}");
        else Win(lastClick[0] <= lastClick[1] ? 0 : 1, $"{why}, 동점 {clicks[0]} → 마지막 클릭 {lastClick[0]:0.00}s : {lastClick[1]:0.00}s");
    }
}
