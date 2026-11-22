using System;
using System.Collections.Generic;
using MiniParty.Server.Games;
using MiniParty.Udp;

namespace MiniParty.Server
{
    /// <summary>
    /// One 1:1 match of 1조 (반응·연타) and/or 3조 (보드·턴제) minigames, depending on <see cref="Groups"/>. The server plays the part the host PC used to play (GameManager / LobbyScreen authority):
    ///   Hello ×2 → Welcome (characters)                         lobby handshake
    ///   StartRound → Barrier(ready) ×2 → Go → countdown → rules  each round
    ///   rules decide → Action(winner) → RoundResult(damage)      result
    ///   Barrier(next) ×2 → Go → next round, until someone's HP is 0
    /// During a round every player action goes through the game's rules (<see cref="RoundRules"/>):
    /// legal ones are relayed to the opponent, illegal ones are dropped and logged.
    /// </summary>
    internal sealed class Room : IRoundHost
    {
        enum Phase { Lobby, Ready, Countdown, Playing, Result, Over }

        const int BarrierReady = 1, BarrierNext = 2;   // same tags as GameManager
        const int MaxHp = 100;

        public readonly string Id;
        public readonly bool Named;
        /// <summary>Minigame groups this room draws from (the first player's scene: 1조 / 3조 / 통합).</summary>
        public readonly int[] Groups;
        readonly GameServer server;
        readonly Client[] slots = new Client[2];
        readonly int[] character = { -1, -1 };
        readonly int[] hp = { MaxHp, MaxHp };
        readonly HashSet<int>[] barriers = { new HashSet<int>(), new HashSet<int>() };
        readonly List<int> deck = new();
        readonly List<Msg> deferred = new();
        Phase phase = Phase.Lobby;
        bool closed, inPlayerAction;
        int round, gameIndex, seed;
        double countdown;
        RoundRules rules;

        public Random Rng { get; } = new();
        public int Seed => seed;
        public int Round => round;

        public Room(GameServer server, string id, bool named, int[] groups)
        {
            this.server = server;
            Id = id;
            Named = named;
            Groups = groups;
        }

        public string GroupKey => string.Join(",", Groups);

        public int Count => (slots[0] != null ? 1 : 0) + (slots[1] != null ? 1 : 0);
        public bool Open => !closed && Count < 2;

        public int Add(Client c)
        {
            int slot = slots[0] == null ? 0 : 1;
            slots[slot] = c;
            return slot;
        }

        public void ForEach(Action<Client> action)
        {
            foreach (var s in slots) if (s != null) action(s);
        }

        public void Remove(Client c)
        {
            slots[c.Index] = null;
            closed = true; // a match can't continue with one player
            var other = slots[1 - c.Index];
            if (other != null && phase != Phase.Over)
            {
                other.Channel.SendReliable("PEER_LEFT", server.Now);
                Log.Line($"방 {Id}: 상대 P{other.Index + 1} 에게 PEER_LEFT 전송 (경기 중단)");
            }
            phase = Phase.Over;
        }

        // ---------- messages from the players ----------

        public void OnMessage(int p, Msg msg)
        {
            switch (msg.Type)
            {
                case WireMsg.Hello:
                    OnHello(p, msg.A);
                    break;
                case WireMsg.Barrier:
                    OnBarrier(p, msg.A);
                    break;
                case WireMsg.Action:
                    OnAction(p, msg);
                    break;
                default:
                    Note($"P{p + 1} 이(가) 보낼 수 없는 메시지 무시: {msg}");
                    break;
            }
        }

        void OnHello(int p, int ch)
        {
            if (ch < 0 || ch > 31) ch = p; // character index (Roster) — just keep it sane
            bool first = character[p] < 0;
            character[p] = ch;
            if (character[0] < 0 || character[1] < 0) return;

            // both known: tell both (the client keeps resending Hello until it gets a Welcome)
            var welcome = new Msg { Type = WireMsg.Welcome, A = character[0], B = character[1] };
            if (phase == Phase.Lobby)
            {
                ForEach(s => server.Send(s, welcome));
                Log.Line($"방 {Id}: 캐릭터 P1 #{character[0]}, P2 #{character[1]} — 경기 시작");
                StartRound(1);
            }
            else if (!first) server.Send(slots[p], welcome); // a late duplicate Hello
        }

        void OnBarrier(int p, int tag)
        {
            barriers[p].Add(tag);
            if (!barriers[0].Contains(tag) || !barriers[1].Contains(tag)) return;
            barriers[0].Remove(tag);
            barriers[1].Remove(tag);
            ForEach(s => server.Send(s, new Msg { Type = WireMsg.Go, A = tag }));

            if (tag == round * 10 + BarrierReady && phase == Phase.Ready)
            {
                phase = Phase.Countdown;
                countdown = Catalog.Countdown(gameIndex);
                rules = RoundRules.Create(gameIndex, this);
            }
            else if (tag == round * 10 + BarrierNext && phase == Phase.Result)
            {
                StartRound(round + 1);
            }
        }

        void OnAction(int p, Msg msg)
        {
            if (msg.D != round || rules == null) return; // late packet from an earlier minigame
            if (msg.B != p || msg.A == Board3.KindWinner)
            {
                Note(msg.B != p ? $"P{p + 1} 이(가) 남의 이름(P{msg.B + 1})으로 보낸 입력 거절 ({msg})"
                                : $"P{p + 1} 이(가) 승자 결정 액션을 보냄 → 거절 (승패는 서버가 정함)");
                return;
            }
            if (phase != Phase.Playing && phase != Phase.Countdown) return;

            int a = msg.C;
            float f = msg.F;
            Verdict verdict;
            string why;
            inPlayerAction = true;
            try { verdict = rules.OnPlayerAction(p, msg.A, ref a, ref f, out why); }
            finally { inPlayerAction = false; }

            switch (verdict)
            {
                case Verdict.Relay:
                    // the rules may have replaced the value (e.g. the server's own click count)
                    var relayed = Msg.Action(msg.A, p, a, round, f);
                    server.Send(slots[1 - p], relayed);
                    if (!server.Options.Quiet) Log.Line($"{Id}/P{p + 1} → P{2 - p}  {Describe(relayed)}");
                    break;
                case Verdict.Consumed:
                    if (!server.Options.Quiet) Log.Line($"{Id}/P{p + 1} → 서버  {Describe(msg)}");
                    break;
                default:
                    Note($"P{p + 1} 입력 거절 ({Describe(msg)}): {why}");
                    break;
            }

            // what the rules sent because of this action (hit results, the winner...) goes out after the relay
            var queued = deferred.ToArray();
            deferred.Clear();
            foreach (var m in queued) ForEach(s => server.Send(s, m));
        }

        string Describe(Msg msg) => $"{Catalog.Name(gameIndex)} {msg}";

        // ---------- round flow ----------

        void StartRound(int r)
        {
            round = r;
            if (server.Options.ForcedGame >= 0) gameIndex = server.Options.ForcedGame;
            else
            {
                if (deck.Count == 0)
                {
                    deck.AddRange(Catalog.GamesOf(Groups));
                    for (int i = deck.Count - 1; i > 0; i--)
                    {
                        int j = Rng.Next(i + 1);
                        (deck[i], deck[j]) = (deck[j], deck[i]);
                    }
                    if (deck.Count > 1 && deck[0] == gameIndex) (deck[0], deck[^1]) = (deck[^1], deck[0]);
                }
                gameIndex = deck[0];
                deck.RemoveAt(0);
            }
            seed = Rng.Next(1, int.MaxValue);
            rules = null;
            phase = Phase.Ready;
            ForEach(s => server.Send(s, new Msg { Type = WireMsg.StartRound, A = round, B = gameIndex, C = seed }));
            Log.Line($"방 {Id}: ROUND {round} — {Catalog.Name(gameIndex)} (HP {hp[0]} : {hp[1]})");
        }

        public void Tick(double dt)
        {
            if (phase == Phase.Countdown)
            {
                countdown -= dt;
                if (countdown <= 0)
                {
                    phase = Phase.Playing;
                    rules.Begin();
                }
            }
            else if (phase == Phase.Playing) rules.Tick(dt);
        }

        // ---------- IRoundHost (used by the rules) ----------

        public double Now => server.Now;

        public void Broadcast(int kind, int a, float f = 0f) => SendBoth(Msg.Action(kind, -1, a, round, f));

        public void SendAs(int player, int kind, int a, float f = 0f) => SendBoth(Msg.Action(kind, player, a, round, f));

        /// <summary>To both players; while a player's action is being handled, after that action is relayed.</summary>
        void SendBoth(Msg msg)
        {
            if (inPlayerAction) deferred.Add(msg);
            else ForEach(s => server.Send(s, msg));
        }

        public void Declare(int winner, string reason)
        {
            if (phase != Phase.Playing) return;
            phase = Phase.Result;
            int loser = 1 - winner;
            int damage = server.Options.FixedDamage > 0 ? server.Options.FixedDamage : Rng.Next(25, 36);
            hp[loser] = Math.Max(0, hp[loser] - damage);

            Broadcast(Board3.KindWinner, winner);
            SendBoth(new Msg { Type = WireMsg.RoundResult, A = winner, B = damage });
            Log.Line($"방 {Id}: ROUND {round} {Catalog.Name(gameIndex)} 승자 P{winner + 1} ({reason}) — P{loser + 1} -{damage} → HP {hp[0]} : {hp[1]}");

            if (hp[loser] <= 0)
            {
                phase = Phase.Over;
                closed = true;
                Log.Line($"방 {Id}: 경기 종료 — 최종 승자 P{winner + 1}");
            }
        }

        public void Note(string text) => Log.Line($"방 {Id}: {text}");

    }
}
