using System.Diagnostics;
using System.Net;
using Group1Server;
using Group1Server.Bot;
using Group1Server.Server;
using MiniParty.Group1Net;

// ============================================================================================================
//  1조(반응·연타) 미니게임 전용 UDP 서버
//
//    Group1Server [--port 33333] [--damage N] [--game 0~5] [--seed N] [--verbose]
//        서버 실행. Unity 클라이언트: 온라인 1:1 → IP 직접 연결 → "1조 전용 서버" → 이 PC 의 IP 입력
//
//    Group1Server bot [주소[:포트]] [--count N] [--verbose]
//        테스트 봇 N명(기본 1)을 서버에 접속시킨다. Unity 1대 + 봇 1명으로 혼자 시험할 때.
//
//    Group1Server selftest [--damage N] [--verbose]
//        서버 + 봇 2명을 한 프로세스에서 돌려 경기 하나를 끝까지 진행해 본다 (성공 시 종료 코드 0).
// ============================================================================================================

Console.OutputEncoding = System.Text.Encoding.UTF8;
var argList = args.ToList();
Log.Verbose = Flag("--verbose");
string command = argList.Count > 0 && !argList[0].StartsWith("--") ? argList[0].ToLowerInvariant() : "server";

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    return command switch
    {
        "server" => RunServer(),
        "bot" => RunBots(),
        "selftest" => RunSelfTest(),
        _ => Usage(),
    };
}
catch (System.Net.Sockets.SocketException e) when (e.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
{
    Log.Error($"포트가 이미 사용 중입니다. 다른 서버가 켜져 있는지 확인하거나 --port 로 바꾸세요.");
    return 1;
}

int RunServer()
{
    var options = new ServerOptions
    {
        Port = IntOption("--port") ?? G1Protocol.DefaultPort,
        Damage = IntOption("--damage"),
        Seed = IntOption("--seed"),
        OnlyGame = IntOption("--game") is int g && g >= 0 && g < G1Game.Count ? g : null,
    };
    using var server = new G1Server(options);
    server.Run(cancel.Token);
    return 0;
}

int RunBots()
{
    string address = argList.Count > 1 && !argList[1].StartsWith("--") ? argList[1] : "127.0.0.1";
    var endpoint = ParseEndPoint(address);
    int count = Math.Max(1, IntOption("--count") ?? 1);
    Log.Info($"봇 {count}명 → {endpoint} 접속");

    var bots = Enumerable.Range(1, count).Select(i => new BotClient($"봇{i}", endpoint, Environment.TickCount + i * 7919)).ToList();
    var clock = Stopwatch.StartNew();
    while (!cancel.IsCancellationRequested && bots.Any(b => !b.Done))
    {
        foreach (var b in bots) b.Step(clock.Elapsed.TotalSeconds);
        Thread.Sleep(5);
    }
    foreach (var b in bots) b.Dispose();
    return bots.All(b => b.Finished) ? 0 : 1;
}

int RunSelfTest()
{
    var options = new ServerOptions { Port = 0, Damage = IntOption("--damage") ?? 17, Seed = IntOption("--seed") };
    using var server = new G1Server(options);
    var endpoint = new IPEndPoint(IPAddress.Loopback, server.Port);
    Log.Good($"자체 테스트: 서버 127.0.0.1:{server.Port} + 봇 2명 (라운드당 데미지 {options.Damage})");

    var bots = new[] { new BotClient("봇A", endpoint, 1234), new BotClient("봇B", endpoint, 5678) };
    var clock = Stopwatch.StartNew();
    const double Limit = 600;
    while (!cancel.IsCancellationRequested && bots.Any(b => !b.Done) && clock.Elapsed.TotalSeconds < Limit)
    {
        server.Step();
        foreach (var b in bots) b.Step(clock.Elapsed.TotalSeconds);
        Thread.Sleep(2);
    }
    // 마지막 Leave 패킷까지 서버가 처리하도록 잠깐 더
    for (int i = 0; i < 50; i++)
    {
        server.Step();
        Thread.Sleep(2);
    }

    bool ok = bots.All(b => b.Finished) && bots[0].MatchWinner == bots[1].MatchWinner && server.FinishedMatches == 1;
    var games = bots[0].GamesSeen.OrderBy(g => g).Select(G1Game.Name);
    Console.WriteLine();
    Log.Info($"라운드 {bots[0].Rounds}, 나온 게임 {bots[0].GamesSeen.Count}/{G1Game.Count}: {string.Join(", ", games)}");
    Log.Info($"최종 HP  봇A {bots[0].Hp[0]} : {bots[0].Hp[1]}  /  봇B 가 본 HP {bots[1].Hp[0]} : {bots[1].Hp[1]}");
    if (ok) Log.Good($"자체 테스트 성공 ({clock.Elapsed.TotalSeconds:0}초)");
    else Log.Error($"자체 테스트 실패: {string.Join(", ", bots.Where(b => !b.Finished).Select(b => b.FailReason ?? "시간 초과"))}");

    foreach (var b in bots) b.Dispose();
    return ok ? 0 : 1;
}

int Usage()
{
    Console.WriteLine("사용법:");
    Console.WriteLine("  Group1Server [--port 33333] [--damage N] [--game 0~5] [--seed N] [--verbose]");
    Console.WriteLine("    --game: 0 줄다리기, 1 CPS, 2 F1, 3 에임, 4 블럭 쌓기, 5 타이밍 (그 게임만 반복)");
    Console.WriteLine("  Group1Server bot [주소[:포트]] [--count N] [--verbose]");
    Console.WriteLine("  Group1Server selftest [--damage N] [--verbose]");
    return 2;
}

bool Flag(string name) => argList.Contains(name);

int? IntOption(string name)
{
    int i = argList.IndexOf(name);
    return i >= 0 && i + 1 < argList.Count && int.TryParse(argList[i + 1], out int v) ? v : null;
}

static IPEndPoint ParseEndPoint(string text)
{
    string host = text;
    int port = G1Protocol.DefaultPort;
    int colon = text.LastIndexOf(':');
    if (colon > 0 && int.TryParse(text[(colon + 1)..], out int p))
    {
        host = text[..colon];
        port = p;
    }
    var address = IPAddress.TryParse(host, out var ip)
        ? ip
        : Dns.GetHostAddresses(host).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    return new IPEndPoint(address, port);
}
