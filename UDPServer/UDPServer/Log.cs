using System.Text;

namespace MiniParty.Server;

/// <summary>터미널 출력. 서버 상태를 눈으로 따라갈 수 있게 한 줄씩 찍는다 (--log 를 주면 파일에도 남긴다).</summary>
internal static class Log
{
    static readonly object Gate = new();
    static StreamWriter? file;

    public static void ToFile(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Info(string message) => Write(message, ConsoleColor.Gray);

    public static void Event(string message) => Write(message, ConsoleColor.Cyan);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    /// <summary>게임 중 들어온 입력 한 줄.</summary>
    public static void Input(string message) => Write(message, ConsoleColor.White);

    static void Write(string message, ConsoleColor color)
    {
        lock (Gate)
        {
            string time = $"[{DateTime.Now:HH:mm:ss}] ";
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(time);
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
            file?.WriteLine(time + message);
        }
    }
}
