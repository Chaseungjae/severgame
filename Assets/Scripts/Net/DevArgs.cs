using System;
using System.Collections.Generic;

namespace MiniParty
{
    /// <summary>
    /// Command-line switches for testing online play with two copies of the game on one PC:
    ///   -autohost            open a direct-IP room immediately
    ///   -autojoin 127.0.0.1  join a direct-IP room immediately
    ///   -autoserver 127.0.0.1  connect to the 1조 dedicated UDP server immediately
    ///   -autoplay            the local player is played by the CPU (online too)
    /// Test panels in the editor can also set them at runtime with <see cref="Set"/>.
    /// </summary>
    public static class DevArgs
    {
        static readonly string[] Args = Environment.GetCommandLineArgs();
        static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();

        public static bool Has(string name) => Overrides.ContainsKey(name) || Array.IndexOf(Args, name) >= 0;

        public static string Get(string name)
        {
            if (Overrides.TryGetValue(name, out var value)) return value;
            int i = Array.IndexOf(Args, name);
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
        }

        /// <summary>Runtime switch (e.g. from a test panel). value = null for flags like -autoplay.</summary>
        public static void Set(string name, string value = null) => Overrides[name] = value;

        public static void Clear(string name) => Overrides.Remove(name);
    }
}
