using System;
using System.Net;
using MiniParty.Server.Games;
using MiniParty.Udp;

namespace MiniParty.Server
{
    /// <summary>
    /// MiniParty UDP game server for 1조 (반응·연타) and 3조 (보드·턴제) minigames. The server is the "host" of every
    /// match: it picks the minigame, checks each input against the rules (Games/**/*Rules.cs), decides the winner,
    /// damage and HP. Each room plays the groups of the scene its first player is in (1조 / 3조 / 1·3조 통합 씬).
    ///
    ///   UdpGameServer.exe [port] [--bind IP] [--groups 1,3] [--damage N] [--game N] [--loss N] [--quiet] [--from-game]
    ///     port         UDP port (default 7780)
    ///     --bind IP    only this address of the PC (default: every address). The game's [내가 방 열기] passes it.
    ///     --groups G   groups this server may run (default 1,3)
    ///     --damage N   damage per lost round (default: random 25~35)
    ///     --game N     only this minigame (registry index: 0~5 = 1조, 11~15 = 3조) — for testing one game
    ///     --loss N     drop N% of incoming packets on purpose (tests the reliable channel)
    ///     --quiet      no per-message logs
    ///     --from-game  started by the game: on an error keep the console window open so the message can be read
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var options = new ServerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (a)
                {
                    case "--quiet": options.Quiet = true; break;
                    case "--from-game": options.FromGame = true; break;
                    case "--loss": int.TryParse(Next(), out options.LossPercent); break;
                    case "--damage": int.TryParse(Next(), out options.FixedDamage); break;
                    case "--game": int.TryParse(Next(), out options.ForcedGame); break;
                    case "--groups": options.Groups = Catalog.ParseGroups(Next()); break;
                    case "--bind":
                        string ip = Next();
                        if (!IPAddress.TryParse(ip, out options.Bind) || options.Bind.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                            return Exit(options, $"--bind 주소가 이상해요: {ip}");
                        break;
                    default:
                        if (int.TryParse(a, out int port)) options.Port = port;
                        else Console.WriteLine($"알 수 없는 옵션 무시: {a}");
                        break;
                }
            }
            options.LossPercent = Math.Clamp(options.LossPercent, 0, 90);
            if (options.Port <= 0 || options.Port > 65535) return Exit(options, $"포트 번호가 이상해요: {options.Port}");
            if (options.ForcedGame >= 0 && !Catalog.IsServerGame(options.ForcedGame))
                return Exit(options, $"--game 은 1조 {string.Join(", ", Reaction1.Games)} / 3조 {string.Join(", ", Board3.Games)} 중 하나여야 해요");

            var server = new GameServer(options);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                server.Stop();
            };
            int code = server.Run();
            return code == 0 ? 0 : Exit(options, null, code);
        }

        /// <summary>Prints the error; when the game started this window, waits so the player can read it.</summary>
        private static int Exit(ServerOptions options, string message, int code = 2)
        {
            if (message != null) Log.Line(message);
            if (options.FromGame)
            {
                Console.WriteLine("이 창은 15초 뒤 닫힙니다 (Enter: 바로 닫기)");
                var wait = System.Threading.Tasks.Task.Run(() => Console.ReadLine());
                wait.Wait(TimeSpan.FromSeconds(15));
            }
            return code;
        }
    }

    internal sealed class ServerOptions
    {
        public int Port = UdpWire.DefaultPort;
        public IPAddress Bind = IPAddress.Any;
        public int[] Groups = ServerGames.Groups;
        public bool Quiet;
        public bool FromGame;
        public int LossPercent;
        public int FixedDamage;      // 0 = random 25~35
        public int ForcedGame = -1;  // -1 = the room's groups in turn
    }

    internal static class Log
    {
        public static void Line(string text) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
    }
}
