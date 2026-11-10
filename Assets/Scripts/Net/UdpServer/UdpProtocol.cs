// Pure C# (no UnityEngine): the dedicated server project (UdpGameServer/) compiles this same file,
// so the client and the server always speak exactly the same protocol.
using System;
using System.Collections.Generic;
using System.Text;

namespace MiniParty.Udp
{
    /// <summary>
    /// Wire format of the MiniParty UDP game server. Every datagram is UTF-8 text:
    ///   R|seq|BODY   reliable  : the receiver answers "A|seq", handles each seq once and in order
    ///   A|seq        ack
    ///   U|BODY       unreliable: may be lost (heartbeats)
    ///
    /// BODY (fields separated by '|')
    ///   client → server  HELLO|version|room   join a room (empty room = quick match with whoever is waiting)
    ///                    MSG|...              game message, relayed as-is to the other player in the room
    ///                    PING                 heartbeat (unreliable, every second)
    ///                    BYE                  leaving (unreliable, best effort)
    ///   server → client  WELCOME|index|room   your slot: 0 = P1 (host / authority), 1 = P2
    ///                    MATCH                both players are in the room — the game can start
    ///                    MSG|...              the other player's game message
    ///                    PEER_LEFT            the other player left or timed out
    ///                    REJECT|reason        VERSION / FULL / BAD
    ///                    PONG                 heartbeat answer (unreliable)
    /// </summary>
    public static class UdpWire
    {
        public const string Version = "MP1";
        public const int DefaultPort = 7780;
        public const double Timeout = 8.0; // seconds of silence before a peer counts as gone
        public static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Windows only: stops ICMP "port unreachable" from breaking ReceiveFrom (WSAECONNRESET).</summary>
        public const int SIO_UDP_CONNRESET = -1744830452;
    }

    /// <summary>
    /// Reliable, ordered message stream to one remote endpoint (plus an unreliable side channel).
    /// Not thread-safe: feed it from one thread. Time is passed in seconds so it works with any clock.
    /// </summary>
    public sealed class ReliableChannel
    {
        const double ResendInterval = 0.2;
        const double GiveUpAfter = 10.0;

        sealed class Pending
        {
            public byte[] Data;
            public double FirstSent, LastSent;
        }

        readonly Action<byte[]> send;
        readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        readonly SortedDictionary<int, string> early = new SortedDictionary<int, string>();
        int nextSeq = 1, expected = 1;

        public double LastHeard;
        /// <summary>A reliable message went unacknowledged for too long: the other side is gone.</summary>
        public bool Failed { get; private set; }
        public int Unacked => pending.Count;

        public ReliableChannel(Action<byte[]> send, double now)
        {
            this.send = send;
            LastHeard = now;
        }

        public void SendReliable(string body, double now)
        {
            int seq = nextSeq++;
            var data = Encode("R|" + seq + "|" + body);
            pending[seq] = new Pending { Data = data, FirstSent = now, LastSent = now };
            send(data);
        }

        public void SendUnreliable(string body) => send(Encode("U|" + body));

        /// <summary>Handles one datagram; bodies that are ready (in order) are appended to <paramref name="output"/>.</summary>
        public void Receive(string packet, double now, List<string> output)
        {
            LastHeard = now;
            if (packet.StartsWith("A|", StringComparison.Ordinal))
            {
                if (int.TryParse(packet.Substring(2), out int acked)) pending.Remove(acked);
                return;
            }
            if (packet.StartsWith("U|", StringComparison.Ordinal))
            {
                output.Add(packet.Substring(2));
                return;
            }
            if (!packet.StartsWith("R|", StringComparison.Ordinal)) return;

            int bar = packet.IndexOf('|', 2);
            if (bar < 0 || !int.TryParse(packet.Substring(2, bar - 2), out int seq)) return;
            send(Encode("A|" + seq)); // always ack, even duplicates (our earlier ack may have been lost)
            if (seq < expected || early.ContainsKey(seq)) return;
            early[seq] = packet.Substring(bar + 1);
            while (early.TryGetValue(expected, out var body))
            {
                early.Remove(expected);
                output.Add(body);
                expected++;
            }
        }

        /// <summary>Resends unacknowledged messages. Call regularly (every frame / every few ms).</summary>
        public void Tick(double now)
        {
            foreach (var p in pending.Values)
            {
                if (now - p.FirstSent > GiveUpAfter)
                {
                    Failed = true;
                    continue;
                }
                if (now - p.LastSent >= ResendInterval)
                {
                    p.LastSent = now;
                    send(p.Data);
                }
            }
        }

        static byte[] Encode(string text) => UdpWire.Utf8.GetBytes(text);
    }
}
