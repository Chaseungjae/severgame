using System;

namespace MiniParty
{
    /// <summary>
    /// Command-line switches for testing online play with two copies of the game on one PC:
    ///   -autohost            open a direct-IP room immediately
    ///   -autojoin 127.0.0.1  join a direct-IP room immediately
    ///   -autoplay            the local player is played by the CPU (online too)
    /// </summary>
    public static class DevArgs
    {
        static readonly string[] Args = Environment.GetCommandLineArgs();

        public static bool Has(string name) => Array.IndexOf(Args, name) >= 0;

        public static string Get(string name)
        {
            int i = Array.IndexOf(Args, name);
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
        }
    }
}
