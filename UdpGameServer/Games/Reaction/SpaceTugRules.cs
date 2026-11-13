using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// 스페이스바 줄다리기 (SpaceTugGame). 깃발 위치(offset)의 "진짜 값"은 서버만 계산한다.
/// 누를 때마다 7씩 내 쪽으로, ±300 을 넘거나 15초 뒤 더 가까운 쪽이 승리. 0.05초마다 위치를 전원에게 보낸다.
/// </summary>
internal sealed class SpaceTugRules : RoundRules
{
    const float Goal = 300f, Step = 7f, TimeLimit = 15f;
    const double SyncInterval = 0.05;
    const int MaxPressPerSecond = 20; // 사람 손으로는 넘기 힘든 속도

    float offset; // 음수 = P1(왼쪽) 쪽
    readonly int[] presses = new int[2];
    readonly RateLimit[] limits = { new(MaxPressPerSecond), new(MaxPressPerSecond) };
    double syncTimer;

    public SpaceTugRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (kind != Reaction1.TugPull) return Reject(out why, $"알 수 없는 액션 {kind}");
        if (!limits[p].Allow(Host.Now)) return Reject(out why, $"초당 {MaxPressPerSecond}회 초과 연타");

        presses[p]++;
        offset += p == 0 ? -Step : Step;
        a = 0;
        f = 0f;
        CheckGoal();
        return Relay(out why);
    }

    protected override void OnTick(double dt)
    {
        if ((syncTimer -= dt) <= 0)
        {
            syncTimer = SyncInterval;
            Host.Broadcast(Reaction1.TugOffset, 0, offset);
        }
        CheckGoal();
        if (Decided || Elapsed < TimeLimit) return;

        Host.Broadcast(Reaction1.TugOffset, 0, offset);
        if (Math.Abs(offset) > 0.01f) Win(offset < 0 ? 0 : 1, $"시간 종료, 깃발 위치 {offset:0}");
        else if (presses[0] != presses[1]) Win(presses[0] > presses[1] ? 0 : 1, $"시간 종료·동점, 연타 {presses[0]} : {presses[1]}");
        else Win(Host.Rng.Next(2), "완전 동점 → 무작위");
    }

    void CheckGoal()
    {
        if (Decided) return;
        if (offset <= -Goal || offset >= Goal)
        {
            Host.Broadcast(Reaction1.TugOffset, 0, offset);
            Win(offset < 0 ? 0 : 1, $"깃발이 결승선 통과 (연타 {presses[0]} : {presses[1]})");
        }
    }
}
