namespace Group1Server;

/// <summary>콘솔 로그 (시각 + 색). 여러 스레드에서 불러도 줄이 섞이지 않게 잠근다.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static bool Verbose;
    public static string Prefix = "";

    public static void Info(string text) => Write(text, ConsoleColor.Gray);
    public static void Good(string text) => Write(text, ConsoleColor.Green);
    public static void Game(string text) => Write(text, ConsoleColor.Cyan);
    public static void Warn(string text) => Write(text, ConsoleColor.Yellow);
    public static void Error(string text) => Write(text, ConsoleColor.Red);

    public static void Debug(string text)
    {
        if (Verbose) Write(text, ConsoleColor.DarkGray);
    }

    static void Write(string text, ConsoleColor color)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] {Prefix}");
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }
}
