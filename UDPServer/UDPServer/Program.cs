using System.Text;
using MiniParty;
using MiniParty.Server;

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        if (ReadArg(args, "--log") is string logPath) Log.ToFile(logPath);
        int port = int.TryParse(ReadArg(args, "--port"), out int p) ? p : UdpProtocol.DefaultPort;
        new UdpGameServer(port).Run(cancellation.Token);
    }

    private static string? ReadArg(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
