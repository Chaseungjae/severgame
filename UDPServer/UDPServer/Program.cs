using System.Net;
using System.Net.Sockets;
using System.Text;

internal static class Program
{
    private const int Port = 7777;

    private static async Task Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var server = new UdpTestServer(Port);
        await server.RunAsync(cancellation.Token);
    }
}

internal sealed class UdpTestServer
{
    private const int MaxClients = 2;

    private readonly int port;
    private readonly Dictionary<IPEndPoint, string> clients = new();
    private UdpClient? socket;

    public UdpTestServer(int port)
    {
        this.port = port;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        socket = udp;

        Console.WriteLine($"UDP 테스트 서버 시작: 0.0.0.0:{port}");
        Console.WriteLine("클라이언트가 JOIN 메시지를 보내면 접속됩니다. 종료하려면 Ctrl+C를 누르세요.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await udp.ReceiveAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await HandleMessageAsync(received);
            }
        }
        finally
        {
            socket = null;
            Console.WriteLine("UDP 테스트 서버를 종료합니다.");
        }
    }

    private async Task HandleMessageAsync(UdpReceiveResult received)
    {
        var endpoint = received.RemoteEndPoint;
        var message = Encoding.UTF8.GetString(received.Buffer).Trim();
        Console.WriteLine($"수신 {endpoint}: {message}");

        if (message.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
        {
            if (clients.TryGetValue(endpoint, out var existingPlayer))
            {
                await SendAsync($"WELCOME|{existingPlayer}", endpoint);
                return;
            }

            if (clients.Count >= MaxClients)
            {
                await SendAsync("FULL", endpoint);
                Console.WriteLine($"접속 거절 (정원 초과): {endpoint}");
                return;
            }

            var playerId = $"P{clients.Count + 1}";
            clients.Add(endpoint, playerId);
            await SendAsync($"WELCOME|{playerId}", endpoint);
            Console.WriteLine($"접속 등록: {playerId} ({endpoint}), 현재 {clients.Count}/{MaxClients}명");

            if (clients.Count == MaxClients)
            {
                await BroadcastAsync("READY");
                Console.WriteLine("두 클라이언트 접속 완료: READY 전송");
            }

            return;
        }

        if (!clients.TryGetValue(endpoint, out var senderId))
        {
            await SendAsync("ERROR|JOIN_REQUIRED", endpoint);
            Console.WriteLine($"JOIN 전 메시지 거절: {endpoint}");
            return;
        }

        if (message.Equals("PING", StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync("PONG", endpoint);
            return;
        }

        if (message.StartsWith("MSG|", StringComparison.Ordinal))
        {
            var text = message[4..];
            await BroadcastAsync($"MESSAGE|{senderId}|{text}");
            Console.WriteLine($"중계 {senderId}: {text}");
            return;
        }

        await SendAsync("ERROR|UNKNOWN_MESSAGE", endpoint);
        Console.WriteLine($"알 수 없는 메시지 종류: {endpoint}");
    }

    private async Task SendAsync(string message, IPEndPoint endpoint)
    {
        var data = Encoding.UTF8.GetBytes(message);
        var udp = socket ?? throw new InvalidOperationException("UDP 서버 소켓이 실행 중이 아닙니다.");
        await udp.SendAsync(data, data.Length, endpoint);
        Console.WriteLine($"송신 {endpoint}: {message}");
    }

    private async Task BroadcastAsync(string message)
    {
        foreach (var endpoint in clients.Keys.ToArray())
            await SendAsync(message, endpoint);
    }
}
