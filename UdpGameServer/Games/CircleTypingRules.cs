using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games
{
    /// <summary>
    /// 원을 그려봐요 (CircleGame): each PC scores its own drawings (the strokes never leave the PC), so the server
    /// checks the order and the range of the reported scores and decides: best try wins, then the sum.
    /// </summary>
    internal sealed class CircleRules : RoundRules
    {
        const int Tries = Board3.CircleTries;
        // both tries + the "wait" between them, then a margin: a player who never reports scores 0
        static readonly double Deadline = Tries * (Board3.CircleTryTime + 3.0) + 10.0;

        readonly float[,] scores = new float[2, Tries];
        readonly int[] done = new int[2];

        public CircleRules(IRoundHost host) : base(host) { }

        public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
        {
            if (Decided) return Reject(out why, "이미 끝난 판");
            if (kind != Board3.CircleTry) return Reject(out why, $"원 그리기에 없는 액션 {kind}");
            if (a != done[p]) return Reject(out why, $"{done[p] + 1}번째 시도 차례인데 {a + 1}번째가 옴");
            if (f < 0f || f > 100f) return Reject(out why, $"불가능한 점수 {f:0.0}%");

            scores[p, a] = f;
            done[p]++;
            Host.Note($"P{p + 1} {a + 1}번째 원 {f:0.0}%");
            if (done[0] >= Tries && done[1] >= Tries) Decide("두 사람 모두 그림");
            return Relay(out why);
        }

        protected override void OnTick(double dt)
        {
            if (Elapsed < Deadline) return;
            Host.Note($"시간 초과 — 못 그린 시도는 0% 처리 (P1 {done[0]}회, P2 {done[1]}회)");
            Decide("시간 초과");
        }

        float Best(int p) => Math.Max(scores[p, 0], scores[p, 1]);

        void Decide(string why)
        {
            float b0 = Best(0), b1 = Best(1);
            if (Math.Abs(b0 - b1) > 0.001f) Win(b0 > b1 ? 0 : 1, $"{why}, 최고 {b0:0.0}% : {b1:0.0}%");
            else Win(scores[0, 0] + scores[0, 1] >= scores[1, 0] + scores[1, 1] ? 0 : 1, $"{why}, 최고점 같음 → 합계");
        }
    }

    /// <summary>
    /// 타자 대결 (TypingGame): progress must go up one word at a time, and a finish time has to fit the server's own
    /// clock (can't claim to be faster than the server saw, or faster than a human can type). More words wins,
    /// then the faster time; once someone finishes all words the other gets a short grace period.
    /// </summary>
    internal sealed class TypingRules : RoundRules
    {
        const int Words = Board3.TypingWords;
        const double Grace = 0.6;
        const float MinSecondsPerWord = 0.25f;
        const double ClockSlack = 3.0; // the PCs start their timers a little before / after the server

        readonly int[] progress = new int[2];
        readonly bool[] hasFinal = new bool[2];
        readonly int[] finalDone = new int[2];
        readonly float[] finalTime = new float[2];
        double graceUntil = -1;

        public TypingRules(IRoundHost host) : base(host) { }

        public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
        {
            if (Decided) return Reject(out why, "이미 끝난 판");
            if (hasFinal[p]) return Reject(out why, "이미 끝낸 사람");

            if (kind == Board3.TypingProgress)
            {
                if (a != progress[p] + 1 || a > Words) return Reject(out why, $"단어 수가 {progress[p]} → {a} 로 건너뜀");
                if (Elapsed + ClockSlack < a * MinSecondsPerWord) return Reject(out why, $"{a}단어를 {Elapsed:0.0}초에? 너무 빠름");
                progress[p] = a;
                return Relay(out why);
            }

            if (kind == Board3.TypingFinal)
            {
                if (a != progress[p]) return Reject(out why, $"완료 단어 {a} ≠ 진행 {progress[p]}");
                if (f < a * MinSecondsPerWord || f > Board3.TypingTimeLimit + 0.5f) return Reject(out why, $"불가능한 기록 {f:0.00}초");
                if (f < Elapsed - ClockSlack || f > Elapsed + ClockSlack) return Reject(out why, $"기록 {f:0.00}초가 서버 시계 {Elapsed:0.00}초와 안 맞음");

                hasFinal[p] = true;
                finalDone[p] = a;
                finalTime[p] = f;
                Host.Note($"P{p + 1} 끝: {a}단어, {f:0.00}초");
                if (hasFinal[0] && hasFinal[1]) Decide();
                else if (a >= Words && graceUntil < 0) graceUntil = Elapsed + Grace;
                return Relay(out why);
            }
            return Reject(out why, $"타자 대결에 없는 액션 {kind}");
        }

        protected override void OnTick(double dt)
        {
            if (graceUntil > 0 && Elapsed >= graceUntil)
            {
                int p = hasFinal[0] ? 0 : 1;
                Win(p, $"먼저 완주 ({finalTime[p]:0.00}초)");
                return;
            }
            if (Elapsed > Board3.TypingTimeLimit + 6.0)
            {
                for (int p = 0; p < 2; p++)
                {
                    if (hasFinal[p]) continue;
                    Host.Note($"P{p + 1} 기록이 안 옴 — {progress[p]}단어, {Board3.TypingTimeLimit}초로 처리");
                    hasFinal[p] = true;
                    finalDone[p] = progress[p];
                    finalTime[p] = Board3.TypingTimeLimit;
                }
                Decide();
            }
        }

        void Decide()
        {
            graceUntil = -1;
            if (finalDone[0] != finalDone[1]) Win(finalDone[0] > finalDone[1] ? 0 : 1, $"단어 {finalDone[0]} : {finalDone[1]}");
            else Win(finalTime[0] <= finalTime[1] ? 0 : 1, $"같은 단어 수, {finalTime[0]:0.00}초 : {finalTime[1]:0.00}초");
        }
    }
}
