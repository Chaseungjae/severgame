using System.Text;

namespace MiniParty
{
    public enum PacketKind
    {
        None,
        Reliable,     // R|seq|body  — 받으면 A|seq 로 응답, 같은 seq 는 한 번만 처리
        Ack,          // A|seq
        Unreliable,   // U|body      — 잃어버려도 되는 것 (PING/PONG)
    }

    /// <summary>
    /// 서버(.NET 콘솔)와 클라이언트(Unity)가 같이 컴파일하는 패킷 규격.
    /// UnityEngine 에 의존하지 않는다 — UDPServer.csproj 가 이 폴더를 그대로 링크해서 쓴다.
    /// </summary>
    public static class UdpProtocol
    {
        public const int Version = 1;
        public const int DefaultPort = 7777;
        public const int MaxPacketSize = 1024;

        public const float ResendInterval = 0.25f;   // ACK 안 온 신뢰 패킷 재전송 간격
        public const float HeartbeatInterval = 1f;   // 클라이언트 PING 간격
        public const float Timeout = 10f;            // 이 시간 동안 아무것도 못 받으면 끊긴 것으로 판정

        public static byte[] ToBytes(string text) => Encoding.UTF8.GetBytes(text);

        public static string FromBytes(byte[] buffer, int length) => Encoding.UTF8.GetString(buffer, 0, length);

        public static string Reliable(int seq, string body) => "R|" + seq + "|" + body;

        public static string Ack(int seq) => "A|" + seq;

        public static string Unreliable(string body) => "U|" + body;

        /// <summary>깨진 패킷은 false. Ack 는 body 가 빈 문자열이다.</summary>
        public static bool TryParse(string text, out PacketKind kind, out int seq, out string body)
        {
            kind = PacketKind.None;
            seq = 0;
            body = "";
            if (text == null || text.Length < 2 || text[1] != '|') return false;

            switch (text[0])
            {
                case 'U':
                    kind = PacketKind.Unreliable;
                    body = text.Substring(2);
                    return true;

                case 'A':
                    kind = PacketKind.Ack;
                    return int.TryParse(text.Substring(2), out seq);

                case 'R':
                    int bar = text.IndexOf('|', 2);
                    if (bar < 0 || !int.TryParse(text.Substring(2, bar - 2), out seq)) return false;
                    kind = PacketKind.Reliable;
                    body = text.Substring(bar + 1);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>사용자 입력에서 구분자로 쓰는 문자를 제거한다.</summary>
        public static string Clean(string text, int maxLength = 16)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
                if (c != '|' && c != ';' && c != ':' && !char.IsControl(c)) sb.Append(c);
            string result = sb.ToString().Trim();
            return result.Length > maxLength ? result.Substring(0, maxLength) : result;
        }
    }
}
