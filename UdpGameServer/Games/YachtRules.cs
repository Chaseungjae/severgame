using MiniParty.Udp;

namespace MiniParty.Server.Games
{
    /// <summary>
    /// 야추 다이스 (YachtGame). The server rolls every die: a client only asks (RollRequest + which dice it holds)
    /// and the server answers with the result for that player (Roll) to both PCs, so dice can't be faked.
    /// A "Done" must report exactly the dice the server rolled. Bigger hand wins; an identical hand re-rolls.
    /// </summary>
    internal sealed class YachtRules : RoundRules
    {
        const int Dice = 5, MaxRolls = 3;
        const double TieDelay = 2.2, WinDelay = 2.6; // the clients show the reveal before the next step
        const double Slack = 8.0;                    // past the clients' own 30 s timer

        int round;
        readonly int[][] values = { new int[Dice], new int[Dice] };
        readonly int[] rolls = new int[2];
        readonly bool[] done = new bool[2];
        double roundStarted, pendingAt = -1;
        int pendingWinner;

        public YachtRules(IRoundHost host) : base(host) { }

        public override void Begin() => StartRound(1);

        void StartRound(int r)
        {
            round = r;
            for (int p = 0; p < 2; p++)
            {
                rolls[p] = 0;
                done[p] = false;
                System.Array.Clear(values[p]);
            }
            roundStarted = Elapsed;
            Host.Broadcast(Board3.YachtRoundStart, round);
            if (r > 1) Host.Note($"완전히 같은 패 — 다시 굴림 ({r}번째)");
        }

        public override Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why)
        {
            if (Decided || pendingAt >= 0) return Reject(out why, "결과 발표 중");
            if (a >> 20 != round) return Reject(out why, $"다른 판의 입력 ({a >> 20} ≠ {round})");
            if (done[p]) return Reject(out why, "이미 확정함");
            int payload = a & 0xFFFFF;

            switch (kind)
            {
                case Board3.YachtRollRequest:
                    if (rolls[p] >= MaxRolls) return Reject(out why, "굴릴 기회 없음");
                    Roll(p, payload & 31);
                    return Consumed(out why);

                case Board3.YachtHold:
                    if (rolls[p] == 0 || rolls[p] >= MaxRolls) return Reject(out why, "지금은 고정할 수 없음");
                    return Relay(out why); // display only: the held mask that counts comes with the next request

                case Board3.YachtDone:
                    if (rolls[p] == 0) return Reject(out why, "한 번도 안 굴림");
                    if (payload != Pack(values[p])) return Reject(out why, "서버가 굴린 주사위와 다른 결과");
                    Finish(p);
                    return Relay(out why);

                case Board3.YachtRoll:
                    return Reject(out why, "주사위는 서버만 굴릴 수 있음");
            }
            return Reject(out why, $"야추에 없는 액션 {kind}");
        }

        void Roll(int p, int held)
        {
            if (rolls[p] == 0) held = 0;
            for (int i = 0; i < Dice; i++)
                if ((held >> i & 1) == 0) values[p][i] = Host.Rng.Next(1, 7);
            rolls[p]++;
            Host.SendAs(p, Board3.YachtRoll, round << 20 | held << 15 | Pack(values[p]));
            YachtHands.Score(values[p], out string name);
            Host.Note($"P{p + 1} {rolls[p]}번째 굴림 [{string.Join(" ", values[p])}] {name}");
        }

        void Finish(int p)
        {
            done[p] = true;
            if (!done[0] || !done[1]) return;
            int s0 = YachtHands.Score(values[0], out string n0), s1 = YachtHands.Score(values[1], out string n1);
            int winner = s0 == s1 ? -1 : s0 > s1 ? 0 : 1;
            Host.Broadcast(Board3.YachtResult, winner);
            Host.Note($"결과: P1 {n0} [{string.Join(" ", values[0])}] vs P2 {n1} [{string.Join(" ", values[1])}]");
            pendingWinner = winner;
            pendingAt = Elapsed + (winner < 0 ? TieDelay : WinDelay);
        }

        protected override void OnTick(double dt)
        {
            if (pendingAt >= 0)
            {
                if (Elapsed < pendingAt) return;
                pendingAt = -1;
                if (pendingWinner < 0) StartRound(round + 1);
                else Win(pendingWinner, "더 센 패");
                return;
            }

            if (Elapsed - roundStarted < Board3.YachtTimeLimit + Slack) return;
            for (int p = 0; p < 2 && pendingAt < 0; p++)
            {
                if (done[p]) continue;
                Host.Note($"P{p + 1} 응답 없음 — 서버가 대신 확정");
                if (rolls[p] == 0) Roll(p, 0);
                Host.SendAs(p, Board3.YachtDone, round << 20 | Pack(values[p]));
                Finish(p);
            }
        }

        static int Pack(int[] v)
        {
            int a = 0;
            for (int i = 0; i < Dice; i++) a |= (v[i] & 7) << (i * 3);
            return a;
        }
    }
}
