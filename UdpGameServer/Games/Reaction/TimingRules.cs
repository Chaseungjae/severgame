using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// 타이밍 서바이벌 (TimingGame). 움직이는 마커를 구역 안에서 멈추기. 목숨 3개, 다 잃으면 탈락.
/// 서버는 레벨이 1씩만 오르는지, 목숨이 1씩만 줄어드는지, 구역에서 벗어난 거리가 말이 되는지 검사하고
/// SurvivalJudge 로 승자를 정한다 (동점이면 구역 중심에서 벗어난 거리 합이 작은 쪽).
/// </summary>
internal sealed class TimingRules : RoundRules
{
    const int Lives = 3;
    const int MaxLevel = 200;
    const float MaxHitOffset = 150f / 2f + 5f + 0.5f; // 가장 넓은 구역 절반 + 마커 절반

    readonly int[] level = new int[2];
    readonly int[] lives = { Lives, Lives };
    readonly bool[] isOut = new bool[2];
    readonly SurvivalJudge judge = new();

    public TimingRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (isOut[p]) return Reject(out why, "이미 탈락");
        switch (kind)
        {
            case Reaction1.TimingHit:
                if (a != level[p] + 1) return Reject(out why, $"레벨 건너뜀 ({level[p]} → {a})");
                if (!(f >= 0f) || f > MaxHitOffset) return Reject(out why, $"구역 밖인데 성공 ({f:0.#})");
                level[p] = a;
                judge.Score[p] = a;
                judge.TieBreak[p] += f;
                if (a >= MaxLevel)
                {
                    isOut[p] = true;
                    judge.Out[p] = true;
                }
                break;

            case Reaction1.TimingMiss:
                if (a != lives[p] - 1 || a < 1) return Reject(out why, $"목숨 불일치 (서버 {lives[p]}, 클라 {a})");
                lives[p] = a;
                break;

            case Reaction1.TimingOut:
                if (a != level[p]) return Reject(out why, $"레벨 불일치 (서버 {level[p]}, 클라 {a})");
                lives[p] = 0;
                isOut[p] = true;
                judge.Out[p] = true;
                Host.Note($"P{p + 1} 레벨 {level[p]}에서 탈락");
                break;

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }

        int w = judge.Check();
        if (w >= 0) Win(w, $"레벨 {level[0]} : {level[1]}");
        return Relay(out why);
    }
}
