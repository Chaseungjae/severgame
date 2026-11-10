using System;
using MiniParty.Udp;

namespace MiniParty.Server
{
    /// <summary>
    /// MiniParty 3조 (보드·턴제) UDP game server. The server is the "host" of every match: it picks the minigame,
    /// checks each move against the rules (Games/*Rules.cs), decides the winner, damage and HP.
    ///
    ///   UdpGameServer.exe [port] [--damage N] [--game N] [--loss N] [--quiet]
    ///     port        UDP port (default 7780)
    ///     --damage N  damage per lost round (default: random 25~35)
    ///     --game N    only this minigame (11 사목, 12 오델로, 13 원, 14 타자, 15 야추) — for testing one game
    ///     --loss N    drop N% of incoming packets on purpose (tests the reliable channel)
    ///     --quiet     no per-message logs
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = new ServerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (a)
                {
                    case "--quiet": options.Quiet = true; break;
                    case "--loss": int.TryParse(Next(), out options.LossPercent); break;
                    case "--damage": int.TryParse(Next(), out options.FixedDamage); break;
                    case "--game": int.TryParse(Next(), out options.ForcedGame); break;
                    default:
                        if (int.TryParse(a, out int port)) options.Port = port;
                        else Console.WriteLine($"알 수 없는 옵션 무시: {a}");
                        break;
                }
            }
            options.LossPercent = Math.Clamp(options.LossPercent, 0, 90);
            if (options.ForcedGame >= 0 && Array.IndexOf(Board3.Games, options.ForcedGame) < 0)
            {
                Console.WriteLine($"--game 은 {string.Join(", ", Board3.Games)} 중 하나여야 해요");
                return 2;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var server = new GameServer(options);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                server.Stop();
            };
            return server.Run();
        }
    }

    internal sealed class ServerOptions
    {
        public int Port = UdpWire.DefaultPort;
        public bool Quiet;
        public int LossPercent;
        public int FixedDamage;      // 0 = random 25~35
        public int ForcedGame = -1;  // -1 = all 3조 games in turn
    }

    internal static class Log
    {
        public static void Line(string text) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
    }
}
