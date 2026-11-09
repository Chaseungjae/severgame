using MiniParty.Group1Net;

namespace Group1Server.Games;

/// <summary>
/// 에임 대결 (AimGame). 같은 시드로 두 PC 에 같은 표적 24개가 같은 순서로 나온다.
/// 명중(Hit)마다 서버가 표적 번호·반응 시간을 검증해서 직접 집계하고, Final 은 서버 집계값으로 바꿔 중계한다.
/// 많이 맞힌 쪽 승, 같으면 평균 반응 시간이 빠른 쪽.
/// </summary>
public sealed class AimRules : ServerGame
{
    const int TargetCount = 24;
    const float MaxLife = 1.15f;          // 표적이 떠 있는 최대 시간 (클라이언트 BuildSchedule 과 같음)
    const double FinalTimeout = 40.0;     // 표적은 최대 약 25초. 그 뒤로도 Final 이 없으면 서버 집계로 판정

    readonly bool[,] hit = new bool[2, TargetCount];
    readonly int[] hits = new int[2];
    readonly float[] reactSum = new float[2];
    readonly bool[] hasFinal = new bool[2];

    public AimRules(IGameHost host) : base(host) { }

    public override bool OnPlayerAction(int p, int kind, ref int a, ref float f, out string reject)
    {
        switch (kind)
        {
            case G1Game.AimHit:
                if (hasFinal[p]) return Fail(out reject, "Final 이후 명중");
                if (a < 0 || a >= TargetCount) return Fail(out reject, $"없는 표적 {a}");
                if (hit[p, a]) return Fail(out reject, $"이미 맞힌 표적 {a}");
                if (f < 0f || f > MaxLife + 0.05f) return Fail(out reject, $"표적이 없을 때의 명중 ({f:0.000}s)");
                hit[p, a] = true;
                hits[p]++;
                reactSum[p] += f;
                return Ok(out reject);

            case G1Game.AimFinal:
                if (hasFinal[p]) return Fail(out reject, "Final 중복");
                hasFinal[p] = true;
                if (a != hits[p]) Host.Note($"P{p + 1} 클라이언트 명중 {a} ≠ 서버 집계 {hits[p]} → 서버 값 사용");
                a = hits[p];
                f = reactSum[p];
                if (hasFinal[0] && hasFinal[1]) Decide("두 명 다 Final");
                return Ok(out reject);

            default:
                return Fail(out reject, $"알 수 없는 액션 {kind}");
        }
    }

    protected override void OnTick(double dt)
    {
        if (Elapsed >= FinalTimeout) Decide("Final 시간 초과 → 서버 집계로 판정");
    }

    void Decide(string why)
    {
        float avg0 = hits[0] > 0 ? reactSum[0] / hits[0] : float.MaxValue;
        float avg1 = hits[1] > 0 ? reactSum[1] / hits[1] : float.MaxValue;
        string score = $"명중 {hits[0]} : {hits[1]}";
        if (hits[0] != hits[1]) Win(hits[0] > hits[1] ? 0 : 1, $"{why}, {score}");
        else Win(avg0 <= avg1 ? 0 : 1, $"{why}, {score} 동점 → 평균 {Ms(avg0)} : {Ms(avg1)}");
    }

    static string Ms(float seconds) => seconds == float.MaxValue ? "---" : $"{seconds * 1000:0}ms";
}
