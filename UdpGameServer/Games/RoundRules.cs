using System;
using MiniParty.Udp;

namespace MiniParty.Server.Games
{
    /// <summary>What a room offers to the rules of the minigame being played.</summary>
    internal interface IRoundHost
    {
        /// <summary>System action to both players (Player = -1).</summary>
        void Broadcast(int kind, int a, float f = 0f);
        /// <summary>Action stamped as one player, to both (e.g. dice the server rolled for that player).</summary>
        void SendAs(int player, int kind, int a, float f = 0f);
        /// <summary>Round winner. Only the first call counts.</summary>
        void Declare(int winner, string reason);
        void Note(string text);
        Random Rng { get; }
        int Seed { get; }
    }

    internal enum Verdict
    {
        Relay,     // legal: forward to the opponent (the sender already applied it locally)
        Consumed,  // handled by the server only (e.g. a dice roll request)
        Reject,    // illegal / impossible: dropped and logged
    }

    /// <summary>
    /// Server side of one 3조 minigame: what the host's "IsAuthority" code did in the game script, moved to the server.
    ///   OnPlayerAction  validate a player's action and update the server's copy of the game state
    ///   Begin / Tick    start after the countdown, then time limits and fallbacks (a frozen client can't stall a match)
    /// </summary>
    internal abstract class RoundRules
    {
        protected readonly IRoundHost Host;
        protected double Elapsed;
        protected bool Decided;

        protected RoundRules(IRoundHost host) => Host = host;

        public static RoundRules Create(int game, IRoundHost host) => game switch
        {
            Board3.ConnectFour => new ConnectFourRules(host),
            Board3.Othello => new OthelloRules(host),
            Board3.Circle => new CircleRules(host),
            Board3.Typing => new TypingRules(host),
            Board3.Yacht => new YachtRules(host),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "3조 미니게임 번호가 아닙니다"),
        };

        public virtual void Begin() { }

        public void Tick(double dt)
        {
            Elapsed += dt;
            if (!Decided) OnTick(dt);
        }

        protected virtual void OnTick(double dt) { }

        public abstract Verdict OnPlayerAction(int p, int kind, int a, float f, out string why);

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
}
