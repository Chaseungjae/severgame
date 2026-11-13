using System;
using System.Collections.Generic;
using MiniParty.Udp;

namespace MiniParty.Server.Games
{
    /// <summary>What a room offers to the rules of the minigame being played.</summary>
    internal interface IRoundHost
    {
        /// <summary>System action to both players (Player = -1). Sent after the action being handled is relayed.</summary>
        void Broadcast(int kind, int a, float f = 0f);
        /// <summary>Action stamped as one player, to both (e.g. dice the server rolled for that player).</summary>
        void SendAs(int player, int kind, int a, float f = 0f);
        /// <summary>Round winner. Only the first call counts.</summary>
        void Declare(int winner, string reason);
        void Note(string text);
        Random Rng { get; }
        int Seed { get; }
        /// <summary>Server clock (seconds), also running before the round begins.</summary>
        double Now { get; }
    }

    internal enum Verdict
    {
        Relay,     // legal: forward to the opponent (the sender already applied it locally)
        Consumed,  // handled by the server only (e.g. a dice roll request)
        Reject,    // illegal / impossible: dropped and logged
    }

    /// <summary>
    /// Server side of one minigame: what the host's "IsAuthority" code did in the game script, moved to the server.
    ///   OnPlayerAction  validate a player's action and update the server's copy of the game state.
    ///                   Changing a / f changes what the opponent receives (e.g. the server's own click count).
    ///   Begin / Tick    start after the countdown, then time limits and fallbacks (a frozen client can't stall a match)
    /// 1조 (반응·연타): Games/Reaction/*.cs · 3조 (보드·턴제): BoardRules / CircleTypingRules / YachtRules.
    /// </summary>
    internal abstract class RoundRules
    {
        protected readonly IRoundHost Host;
        protected double Elapsed;
        protected bool Decided;

        protected RoundRules(IRoundHost host) => Host = host;

        public static RoundRules Create(int game, IRoundHost host) => game switch
        {
            Reaction1.SpaceTug => new SpaceTugRules(host),
            Reaction1.Cps => new CpsRules(host),
            Reaction1.F1Reaction => new F1ReactionRules(host),
            Reaction1.Aim => new AimRules(host),
            Reaction1.Stack => new StackRules(host),
            Reaction1.Timing => new TimingRules(host),
            Board3.ConnectFour => new ConnectFourRules(host),
            Board3.Othello => new OthelloRules(host),
            Board3.Circle => new CircleRules(host),
            Board3.Typing => new TypingRules(host),
            Board3.Yacht => new YachtRules(host),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "서버가 진행할 수 없는 미니게임 번호입니다"),
        };

        public virtual void Begin() { }

        public void Tick(double dt)
        {
            Elapsed += dt;
            if (!Decided) OnTick(dt);
        }

        protected virtual void OnTick(double dt) { }

        public abstract Verdict OnPlayerAction(int p, int kind, ref int a, ref float f, out string why);

        protected void Win(int p, string reason)
        {
            if (Decided) return;
            Decided = true;
            Host.Declare(p, reason);
        }

        protected static Verdict Reject(out string why, string reason)
        {
            why = reason;
            return Verdict.Reject;
        }

        protected static Verdict Relay(out string why)
        {
            why = null;
            return Verdict.Relay;
        }

        protected static Verdict Consumed(out string why)
        {
            why = null;
            return Verdict.Consumed;
        }
    }

    /// <summary>Games the server can run: names, groups and the client countdown before each one starts.</summary>
    internal static class Catalog
    {
        public static string Name(int index) => index switch
        {
            Reaction1.SpaceTug => "스페이스바 줄다리기",
            Reaction1.Cps => "CPS 클릭 대결",
            Reaction1.F1Reaction => "F1 반응 속도",
            Reaction1.Aim => "에임 대결",
            Reaction1.Stack => "블럭 쌓기",
            Reaction1.Timing => "타이밍 서바이벌",
            Board3.ConnectFour => "사목",
            Board3.Othello => "오델로",
            Board3.Circle => "원을 그려봐요",
            Board3.Typing => "타자 대결",
            Board3.Yacht => "야추 다이스",
            _ => "#" + index,
        };

        public static string GroupName(int group) => group == 1 ? "1조 반응·연타" : group == 3 ? "3조 보드·턴제" : group + "조";

        public static bool IsServerGame(int index)
        {
            foreach (int g in ServerGames.Groups)
                if (Array.IndexOf(ServerGames.Of(g), index) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Seconds between Go and the server starting the rules: the client's 3-2-1-START (RoundIntro.Countdown).
        /// 1조 games are timed on both sides (heats, 5 s click windows), so they use the measured 2.6 s from the old 1조 server.
        /// </summary>
        public static double Countdown(int index) => Array.IndexOf(Reaction1.Games, index) >= 0 ? 2.6 : 3.0;

        /// <summary>"1,3" / "1 3" / "13" → {1, 3}, only groups the server runs. Empty or unknown → every server group.</summary>
        public static int[] ParseGroups(string text)
        {
            var list = new List<int>();
            foreach (char c in text ?? "")
            {
                int g = c - '0';
                if (char.IsDigit(c) && Array.IndexOf(ServerGames.Groups, g) >= 0 && !list.Contains(g)) list.Add(g);
            }
            if (list.Count == 0) list.AddRange(ServerGames.Groups);
            list.Sort();
            return list.ToArray();
        }

        public static List<int> GamesOf(int[] groups)
        {
            var games = new List<int>();
            foreach (int g in groups) games.AddRange(ServerGames.Of(g));
            return games;
        }
    }
}
