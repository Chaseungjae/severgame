using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// 블럭 쌓기 (StackGame). 각 PC 가 자기 블럭을 움직이고 떨어뜨린 결과(층, 왼쪽 위치, 폭)를 보낸다.
/// 서버는 탑 상태를 따라가며 "한 층씩만 오르는지, 블럭이 넓어지거나 아래 블럭 밖에 놓이지 않았는지" 검사하고,
/// SurvivalJudge 규칙으로 승자를 정한다 (먼저 떨어진 사람보다 높이 쌓는 순간 승리).
/// </summary>
internal sealed class StackRules : RoundRules
{
    const float BaseWidth = 300f;
    const int MaxLevel = 80;
    const float Tolerance = 1.01f; // 왼쪽 위치를 정수로 반올림해서 보내므로 1 까지 허용

    readonly float[] left = { -BaseWidth / 2, -BaseWidth / 2 };
    readonly float[] width = { BaseWidth, BaseWidth };
    readonly int[] level = new int[2];
    readonly bool[] done = new bool[2];
    readonly SurvivalJudge judge = new();

    public StackRules(IRoundHost host) : base(host) { }

    public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
    {
        if (done[p]) return Reject(out why, "이미 끝난 탑");
        switch (kind)
        {
            case Reaction1.StackPlace:
            {
                int lv = a / 100000;
                float nl = a % 100000 - 50000f;
                float w = f;
                if (lv != level[p] + 1) return Reject(out why, $"층 건너뜀 ({level[p]} → {lv})");
                if (!(w > 1f) || w > width[p] + 0.5f) return Reject(out why, $"블럭 폭이 이상함 ({width[p]:0.#} → {w:0.#})");
                if (nl < left[p] - Tolerance || nl + w > left[p] + width[p] + Tolerance)
                    return Reject(out why, "아래 블럭 밖에 놓임");

                level[p] = lv;
                left[p] = nl;
                width[p] = w;
                judge.Score[p] = lv;
                judge.TieBreak[p] = -w; // 동점이면 마지막 블럭이 넓은 쪽
                if (lv >= MaxLevel)
                {
                    done[p] = true;
                    judge.Out[p] = true;
                }
                break;
            }

            case Reaction1.StackFail:
                if (a != level[p]) return Reject(out why, $"층 불일치 (서버 {level[p]}, 클라 {a})");
                done[p] = true;
                judge.Out[p] = true;
                Host.Note($"P{p + 1} {level[p]}층에서 실패");
                break;

            default:
                return Reject(out why, $"알 수 없는 액션 {kind}");
        }

        int w2 = judge.Check();
        if (w2 >= 0) Win(w2, $"{level[0]}층 : {level[1]}층");
        return Relay(out why);
    }
}
