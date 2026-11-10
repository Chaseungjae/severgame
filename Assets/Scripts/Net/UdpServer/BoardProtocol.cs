// Pure C# (no UnityEngine): compiled by both Unity and the UDP game server (UdpGameServer/).
namespace MiniParty.Udp
{
    /// <summary>Mirror of <c>MiniParty.MsgType</c> (NetSession.cs): the NetMsg types inside "MSG|..." bodies.</summary>
    public enum WireMsg : byte
    {
        Hello = 1,      // client → host : A = my character
        Welcome,        // host → client : A = P1 character, B = P2 character
        StartRound,     // host → client : A = round, B = minigame index, C = seed
        Barrier,        // client → host : A = barrier tag
        Go,             // host → client : A = barrier tag
        Action,         // both ways     : A = kind, B = player, C = value, D = round, F = float value
        RoundResult,    // host → client : A = winner, B = damage
    }

    /// <summary>
    /// 3조 (보드·턴제) minigames as the UDP game server sees them. The server is the authority for these games:
    /// it picks them, checks every move and decides the winner. The values below MUST match the game scripts
    /// (Assets/Scripts/MiniGames/Group3_Board) and the order of MiniGameRegistry.Games — the Unity client checks
    /// the indices when it connects (UdpServerLink.CheckRegistry) and refuses to play if they drifted.
    /// </summary>
    public static class Board3
    {
        // MiniGameRegistry.Games indices (1조 6 + 2조 5 games come first)
        public const int ConnectFour = 11, Othello = 12, Circle = 13, Typing = 14, Yacht = 15;
        public static readonly int[] Games = { ConnectFour, Othello, Circle, Typing, Yacht };

        /// <summary>MiniGameBase: system action that ends the minigame (A = winner).</summary>
        public const int KindWinner = -100;

        // BoardGameBase (사목·오델로)
        public const int BoardMove = 1;                  // player: A = column (사목) / cell (오델로)
        public const float ConnectFourTurnTime = 8f, OthelloTurnTime = 12f;

        // CircleGame (원을 그려봐요)
        public const int CircleTry = 1;                  // player: A = try index, F = score 0..100
        public const int CircleTries = 2;
        public const float CircleTryTime = 7f;

        // TypingGame (타자 대결)
        public const int TypingProgress = 1;             // player: A = words done
        public const int TypingFinal = 2;                // player: A = words done, F = seconds
        public const int TypingWords = 10;
        public const float TypingTimeLimit = 60f;

        // YachtGame (야추 다이스)
        public const int YachtRoundStart = 1;            // system: A = round
        public const int YachtRoll = 2;                  // player-stamped, from the authority: A = round << 20 | held << 15 | dice
        public const int YachtHold = 3;                  // player: A = round << 20 | held (display only)
        public const int YachtDone = 4;                  // player: A = round << 20 | dice
        public const int YachtResult = 5;                // system: A = winner, -1 = tie (re-roll)
        public const int YachtRollRequest = 6;           // player → authority: A = round << 20 | held
        public const float YachtTimeLimit = 30f;
    }

    /// <summary>야추 hands, used by YachtGame (client) and the server's YachtRules so both rank dice the same way.</summary>
    public static class YachtHands
    {
        /// <summary>Comparable hand strength: rank, then the dice that make the hand, then the total.</summary>
        public static int Score(int[] v, out string name)
        {
            var count = new int[7];
            int sum = 0;
            foreach (int x in v) { count[x]++; sum += x; }

            int four = 0, three = 0, highPair = 0, lowPair = 0, five = 0;
            for (int f = 6; f >= 1; f--)
            {
                if (count[f] == 5) five = f;
                else if (count[f] == 4) four = f;
                else if (count[f] == 3) three = f;
                else if (count[f] == 2) { if (highPair == 0) highPair = f; else lowPair = f; }
            }
            int run = 0, bestRun = 0, runHigh = 0;
            for (int f = 1; f <= 6; f++)
            {
                run = count[f] > 0 ? run + 1 : 0;
                if (run >= bestRun) { bestRun = run; runHigh = f; }
            }

            int rank, key;
            if (five > 0) { rank = 8; key = five; name = "야추!"; }
            else if (four > 0) { rank = 7; key = four; name = "포카드"; }
            else if (bestRun >= 5) { rank = 6; key = runHigh; name = "라지 스트레이트"; }
            else if (three > 0 && highPair > 0) { rank = 5; key = three * 10 + highPair; name = "풀하우스"; }
            else if (bestRun >= 4) { rank = 4; key = runHigh; name = "스몰 스트레이트"; }
            else if (three > 0) { rank = 3; key = three; name = "트리플"; }
            else if (lowPair > 0) { rank = 2; key = highPair * 10 + lowPair; name = "투 페어"; }
            else if (highPair > 0) { rank = 1; key = highPair; name = "원 페어"; }
            else { rank = 0; key = 0; name = "노 페어"; }
            return rank * 100000 + key * 100 + sum;
        }

        public static int Rank(int score) => score / 100000;
    }
}
