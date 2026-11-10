using MiniParty.Udp;

namespace MiniParty.Server.Games
{
    /// <summary>
    /// Shared turn logic of 사목 / 오델로 (mirror of BoardGameBase): colors, whose turn it is, move checks and
    /// the turn clock. The client of the player to move auto-plays when its clock runs out; if that client is
    /// frozen the server plays a fallback move for it a little later, so the match never stalls.
    /// </summary>
    internal abstract class BoardRules : RoundRules
    {
        protected const int Empty = 0, Black = 1, White = 2;
        const double FallbackGrace = 3.0; // seconds past the client's own turn clock

        protected readonly int Size, Rows;
        protected readonly int[] Cells;
        protected readonly int BlackPlayer;
        protected int Turn = Black;
        protected int MoveCount;
        double turnClock;

        protected abstract double TurnTime { get; }
        protected abstract bool IsLegal(int move, int color);
        /// <summary>Applies a legal move, advances <see cref="Turn"/>. Returns the winner (player) or -1.</summary>
        protected abstract int ApplyMove(int move, int color, out string reason);
        /// <summary>Any legal move (server fallback when the player's client stopped answering).</summary>
        protected abstract int FallbackMove(int color);
        protected abstract string MoveName(int move);

        protected BoardRules(IRoundHost host, int size, int rows) : base(host)
        {
            Size = size;
            Rows = rows;
            Cells = new int[size * rows];
            BlackPlayer = host.Seed % 2; // same as BoardGameBase (seed is shared with the clients)
        }

        protected int PlayerOf(int color) => color == Black ? BlackPlayer : 1 - BlackPlayer;
        protected static int Other(int color) => color == Black ? White : Black;
        protected int Idx(int x, int y) => y * Size + x;
        protected bool In(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Rows;
        protected int TieWinner => Host.Seed % 3 == 0 ? PlayerOf(Black) : PlayerOf(White); // same coin as the clients

        public override void Begin()
        {
            turnClock = 0;
            Host.Note($"선공 P{PlayerOf(Black) + 1}");
        }

        public override Verdict OnPlayerAction(int p, int kind, int a, float f, out string why)
        {
            if (Decided) return Reject(out why, "이미 끝난 판");
            if (kind != Board3.BoardMove) return Reject(out why, $"보드게임에 없는 액션 {kind}");
            if (p != PlayerOf(Turn)) return Reject(out why, "자기 차례가 아님");
            if (a < 0 || a >= Cells.Length || !IsLegal(a, Turn)) return Reject(out why, $"둘 수 없는 자리 {MoveName(a)}");
            Play(p, a);
            return Relay(out why);
        }

        void Play(int p, int move)
        {
            int winner = ApplyMove(move, Turn, out string reason);
            MoveCount++;
            turnClock = 0;
            if (winner >= 0) Win(winner, reason);
        }

        protected override void OnTick(double dt)
        {
            turnClock += dt;
            if (turnClock < TurnTime + FallbackGrace) return;
            int p = PlayerOf(Turn), move = FallbackMove(Turn);
            Host.Note($"P{p + 1} 응답 없음 — 서버가 대신 둠 ({MoveName(move)})");
            Host.SendAs(p, Board3.BoardMove, move);
            Play(p, move);
        }
    }

    /// <summary>사목 (ConnectFourGame): 7×6, a move is a column, 4 in a row wins, full board = coin toss.</summary>
    internal sealed class ConnectFourRules : BoardRules
    {
        static readonly (int dx, int dy)[] Dirs = { (1, 0), (0, 1), (1, 1), (1, -1) };

        public ConnectFourRules(IRoundHost host) : base(host, 7, 6) { }

        protected override double TurnTime => Board3.ConnectFourTurnTime;
        protected override string MoveName(int col) => $"{col + 1}번 줄";

        int Landing(int col)
        {
            for (int y = Rows - 1; y >= 0; y--)
                if (Cells[Idx(col, y)] == Empty) return y;
            return -1;
        }

        protected override bool IsLegal(int col, int color) => col >= 0 && col < Size && Landing(col) >= 0;

        protected override int ApplyMove(int col, int color, out string reason)
        {
            int y = Landing(col);
            Cells[Idx(col, y)] = color;
            foreach (var (dx, dy) in Dirs)
            {
                int n = 1;
                for (int k = 1; In(col + dx * k, y + dy * k) && Cells[Idx(col + dx * k, y + dy * k)] == color; k++) n++;
                for (int k = 1; In(col - dx * k, y - dy * k) && Cells[Idx(col - dx * k, y - dy * k)] == color; k++) n++;
                if (n >= 4)
                {
                    reason = "사목 완성";
                    return PlayerOf(color);
                }
            }
            if (MoveCount + 1 >= Cells.Length)
            {
                reason = "판이 가득 참 (무승부 → 추첨)";
                return TieWinner;
            }
            Turn = Other(color);
            reason = null;
            return -1;
        }

        protected override int FallbackMove(int color)
        {
            foreach (int col in new[] { 3, 2, 4, 1, 5, 0, 6 })
                if (IsLegal(col, color)) return col;
            return 0;
        }
    }

    /// <summary>오델로 (OthelloGame): 8×8, black first, a move must flip; no move = pass; nobody can move = count.</summary>
    internal sealed class OthelloRules : BoardRules
    {
        static readonly (int dx, int dy)[] Dirs8 = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

        public OthelloRules(IRoundHost host) : base(host, 8, 8)
        {
            Cells[Idx(3, 3)] = White;
            Cells[Idx(4, 4)] = White;
            Cells[Idx(3, 4)] = Black;
            Cells[Idx(4, 3)] = Black;
        }

        protected override double TurnTime => Board3.OthelloTurnTime;
        protected override string MoveName(int idx) => $"({idx % Size + 1}, {idx / Size + 1})";

        int FlipCount(int idx, int color, bool apply)
        {
            if (Cells[idx] != Empty) return 0;
            int x = idx % Size, y = idx / Size, opp = Other(color), total = 0;
            foreach (var (dx, dy) in Dirs8)
            {
                int n = 0, cx = x + dx, cy = y + dy;
                while (In(cx, cy) && Cells[Idx(cx, cy)] == opp)
                {
                    n++;
                    cx += dx;
                    cy += dy;
                }
                if (n == 0 || !In(cx, cy) || Cells[Idx(cx, cy)] != color) continue;
                total += n;
                if (apply)
                    for (int k = 1; k <= n; k++) Cells[Idx(x + dx * k, y + dy * k)] = color;
            }
            return total;
        }

        protected override bool IsLegal(int idx, int color) => idx >= 0 && idx < Cells.Length && FlipCount(idx, color, false) > 0;

        bool HasMoves(int color)
        {
            for (int i = 0; i < Cells.Length; i++)
                if (IsLegal(i, color)) return true;
            return false;
        }

        int Count(int color)
        {
            int n = 0;
            foreach (int c in Cells) if (c == color) n++;
            return n;
        }

        protected override int ApplyMove(int idx, int color, out string reason)
        {
            FlipCount(idx, color, true);
            Cells[idx] = color;
            reason = null;

            int opp = Other(color);
            if (HasMoves(opp))
            {
                Turn = opp;
                return -1;
            }
            if (HasMoves(color))
            {
                Host.Note($"{(opp == Black ? "흑" : "백")} 둘 곳 없음 → 패스");
                Turn = color;
                return -1;
            }
            int b = Count(Black), w = Count(White);
            reason = $"둘 곳 없음, 흑 {b} : {w} 백";
            if (b != w) return PlayerOf(b > w ? Black : White);
            reason += " (무승부 → 추첨)";
            return TieWinner;
        }

        protected override int FallbackMove(int color)
        {
            int best = -1, bestFlips = -1;
            for (int i = 0; i < Cells.Length; i++)
            {
                int n = FlipCount(i, color, false);
                if (n > bestFlips)
                {
                    best = i;
                    bestFlips = n;
                }
            }
            return best;
        }
    }
}
