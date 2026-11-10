using System.Globalization;
using MiniParty.Udp;

namespace MiniParty.Server
{
    /// <summary>A game message as carried in "MSG|type|a|b|c|d|f" (same fields as NetSession's NetMsg).</summary>
    internal struct Msg
    {
        public WireMsg Type;
        public int A, B, C, D;
        public float F;

        public static Msg Action(int kind, int player, int value, int round, float f = 0f) =>
            new Msg { Type = WireMsg.Action, A = kind, B = player, C = value, D = round, F = f };

        public string Format() => string.Format(CultureInfo.InvariantCulture, "MSG|{0}|{1}|{2}|{3}|{4}|{5:R}", (byte)Type, A, B, C, D, F);

        public static bool TryParse(string body, out Msg msg)
        {
            msg = default;
            var f = body.Split('|');
            if (f.Length != 7 || f[0] != "MSG") return false;
            if (!byte.TryParse(f[1], out byte type) || type < 1 || type > 7) return false;
            if (!int.TryParse(f[2], out msg.A) || !int.TryParse(f[3], out msg.B) || !int.TryParse(f[4], out msg.C)
                || !int.TryParse(f[5], out msg.D)) return false;
            if (!float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out msg.F) || float.IsNaN(msg.F) || float.IsInfinity(msg.F)) return false;
            msg.Type = (WireMsg)type;
            return true;
        }

        public override string ToString() => Type == WireMsg.Action
            ? $"Action 종류 {A}, P{(B >= 0 ? (B + 1).ToString() : "-")}, 값 {C}{(F != 0 ? $", {F:0.###}" : "")}"
            : $"{Type} {A} {B} {C}";
    }
}
