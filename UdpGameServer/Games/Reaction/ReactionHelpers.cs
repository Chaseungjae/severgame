using System.Collections.Generic;

namespace MiniParty.Server.Games;

/// <summary>최근 1초 동안의 입력 수를 세어 사람이 낼 수 없는 연타(매크로·패킷 조작)를 막는다.</summary>
internal sealed class RateLimit
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
internal sealed class SurvivalJudge
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
