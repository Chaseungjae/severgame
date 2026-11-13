using MiniParty.Group1Net;

namespace Group1Server.Games;

/// <summary>
/// 스페이스바 줄다리기 (SpaceTugGame). 깃발 위치(offset)의 "진짜 값"은 서버만 계산한다.
/// 누를 때마다 7씩 내 쪽으로, ±300 을 넘거나 15초 뒤 더 가까운 쪽이 승리. 0.05초마다 위치를 전원에게 보낸다.
/// </summary>
public sealed class SpaceTugRules : ServerGame
{
    const float Goal = 300f, Step = 7f, TimeLimit = 15f;
    const double SyncInterval = 0.05;
    const int MaxPressPerSecond = 20; // 사람 손으로는 넘기 힘든 속도

    float offset; // 음수 = P1(왼쪽) 쪽
    readonly int[] presses = new int[2];
    readonly RateLimit[] limits = { new(MaxPressPerSecond), new(MaxPressPerSecond) };
    double syncTimer;

    public SpaceTugRules(IGameHost host) : base(host) { }

    public override bool OnPlayerAction(int p, int kind, ref int a, ref float f, out string reject)
    {
        if (kind != G1Game.TugPull) return Fail(out reject, $"알 수 없는 액션 {kind}");
        if (!limits[p].Allow(Host.Now)) return Fail(out reject, $"초당 {MaxPressPerSecond}회 초과 연타");

        presses[p]++;
        offset += p == 0 ? -Step : Step;
        a = 0;
        f = 0f;
        CheckGoal();
        return Ok(out reject);
    }

    protected override void OnTick(double dt)
    {
        if ((syncTimer -= dt) <= 0)
        {
            syncTimer = SyncInterval;
            Host.Broadcast(G1Game.TugOffset, 0, offset);
        }
        CheckGoal();
        if (Decided || Elapsed < TimeLimit) return;

        Host.Broadcast(G1Game.TugOffset, 0, offset);
        if (Math.Abs(offset) > 0.01f) Win(offset < 0 ? 0 : 1, $"시간 종료, 깃발 위치 {offset:0}");
        else if (presses[0] != presses[1]) Win(presses[0] > presses[1] ? 0 : 1, $"시간 종료·동점, 연타 {presses[0]} : {presses[1]}");
        else Win(Host.Rng.Next(2), "완전 동점 → 무작위");
    }

    void CheckGoal()
    {
        if (Decided) return;
        if (offset <= -Goal || offset >= Goal)
        {
            Host.Broadcast(G1Game.TugOffset, 0, offset);
            Win(offset < 0 ? 0 : 1, $"깃발이 결승선 통과 (연타 {presses[0]} : {presses[1]})");
        }
    }
}
