using System;

// ============================================================================================================
//  1조 전용 서버 프로토콜 (Unity 클라이언트와 외부 서버가 "같은 파일"을 컴파일해서 쓴다)
//
//  - 이 폴더(Net/Shared)의 파일은 UnityEngine 을 쓰면 안 된다. 서버 프로젝트(UDPServer/Group1Server)가
//    이 파일을 그대로 링크해서 빌드하기 때문. 순수 C# (C# 9) 만 사용.
//  - 외부 라이브러리 없음: System.Net.Sockets.Socket 위에 직접 만든 바이너리 패킷 + 신뢰성 계층.
//
//  패킷 (리틀 엔디안)
//    [0] Magic 'G'   [1] Version   [2] Channel   [3..6] Seq(int32)   [7..] Payload
//    Channel  Unreliable : 잃어버려도 되는 것 (Join 재시도, Ping/Pong)            Seq = 0
//             Reliable   : 받은 쪽이 Ack 를 보낼 때까지 재전송, 순서대로 한 번만 처리   Seq = 1, 2, 3 ...
//             Ack        : Reliable 수신 확인                                       Seq = 확인할 번호, Payload 없음
//    Payload  [0] Op  [1..] 내용 (Op 별 아래 표)
// ============================================================================================================
namespace MiniParty.Group1Net
{
    public static class G1Protocol
    {
        public const byte Magic = (byte)'G';
        public const byte Version = 1;
        public const int HeaderSize = 7;
        public const int MaxPacketSize = 512;

        /// <summary>서버·클라이언트 공통 기본 포트. (기존 IP 직접 연결 / UDP 로비가 쓰는 7777 과 겹치지 않게)</summary>
        public const int DefaultPort = 33333;

        public const double ResendInterval = 0.2;   // Ack 못 받은 Reliable 재전송 간격 (초)
        public const double PingInterval = 1.0;     // 클라 → 서버 생존 신호
        public const double Timeout = 6.0;          // 이 시간 동안 아무 패킷도 없으면 끊긴 것으로 본다
        public const double JoinRetryInterval = 0.5;
        public const double JoinTimeout = 6.0;

        /// <summary>
        /// 라운드 시작 동기화(Barrier Go) 뒤 클라이언트가 3-2-1-START 카운트다운을 보여주는 시간.
        /// 서버는 Go 를 보낸 뒤 이만큼 기다렸다가 게임 시계를 시작한다 (RoundIntro.Countdown 과 맞춤).
        /// </summary>
        public const double CountdownSeconds = 2.6;
    }

    public enum Channel : byte { Unreliable = 0, Reliable = 1, Ack = 2 }

    /// <summary>Payload 첫 바이트.</summary>
    public enum Op : byte
    {
        Join = 1,         // C→S (U)  int nonce                       접속 요청. JoinOk 받을 때까지 0.5초마다 재전송
        JoinOk = 2,       // S→C (U)  byte 내번호(0=P1,1=P2), int 방번호, int nonce
        Reject = 3,       // S→C (U)  byte RejectReason
        Matched = 4,      // S→C (R)                                  같은 방에 상대가 들어옴
        OpponentLeft = 5, // S→C (R)                                  상대가 나감 / 시간 초과
        Ping = 6,         // C→S (U)  int id
        Pong = 7,         // S→C (U)  int id
        Leave = 8,        // C→S (U)                                  나가기
        Net = 9,          // 양방향 (R) NetMsg: byte type, int A, int B, int C, int D, float F
    }

    public enum RejectReason : byte { BadVersion = 1, ServerFull = 2 }

    /// <summary>
    /// 게임 흐름 메시지. Unity 쪽 NetSession 의 NetMsg / MsgType 과 같은 모양이라 GameManager 를 그대로 쓴다.
    /// 전용 서버는 두 클라이언트 모두에게 "방장(host)" 역할을 하고, 두 클라이언트는 모두 게스트로 동작한다.
    /// </summary>
    public struct WireMsg
    {
        public byte Type;
        public int A, B, C, D;
        public float F;

        public override string ToString() => $"{NetType.Name(Type)}(A={A}, B={B}, C={C}, D={D}, F={F:0.###})";
    }

    /// <summary>NetSession.MsgType 과 숫자가 같아야 한다 (Group1ServerLink 가 시작할 때 검사).</summary>
    public static class NetType
    {
        public const byte Hello = 1;        // C→S  A = 내 캐릭터
        public const byte Welcome = 2;      // S→C  A = P1 캐릭터, B = P2 캐릭터
        public const byte StartRound = 3;   // S→C  A = 라운드, B = 1조 게임 번호(G1Game), C = 시드
        public const byte Barrier = 4;      // C→S  A = 태그 (라운드*10 + 1:준비 / 2:다음)
        public const byte Go = 5;           // S→C  A = 태그
        public const byte Action = 6;       // 양방향 A = 종류, B = 플레이어(-1 = 서버/시스템), C = 값, D = 라운드, F = 실수 값
        public const byte RoundResult = 7;  // S→C  A = 승자, B = 데미지

        public static string Name(byte t)
        {
            switch (t)
            {
                case Hello: return "Hello";
                case Welcome: return "Welcome";
                case StartRound: return "StartRound";
                case Barrier: return "Barrier";
                case Go: return "Go";
                case Action: return "Action";
                case RoundResult: return "RoundResult";
                default: return "Type" + t;
            }
        }
    }

    /// <summary>
    /// 서버가 다루는 1조 게임 번호와 각 게임의 액션 종류.
    /// 액션 번호는 각 게임 스크립트(SpaceTugGame.cs 등)의 const 와 반드시 같아야 한다.
    /// </summary>
    public static class G1Game
    {
        public const int SpaceTug = 0, Cps = 1, F1Reaction = 2, Aim = 3, Stack = 4, Timing = 5;
        public const int Count = 6;

        /// <summary>MiniGameBase 의 승자 결정 액션 (KindWinner). A = 승자.</summary>
        public const int KindWinner = -100;

        public static string Name(int id)
        {
            switch (id)
            {
                case SpaceTug: return "스페이스바 줄다리기";
                case Cps: return "CPS 클릭 대결";
                case F1Reaction: return "F1 반응 속도";
                case Aim: return "에임 대결";
                case Stack: return "블럭 쌓기";
                case Timing: return "타이밍 서바이벌";
                default: return "알 수 없는 게임 " + id;
            }
        }

        // SpaceTugGame
        public const int TugPull = 1, TugOffset = 2;            // Offset: 서버 → 전원, F = 깃발 위치
        // CpsGame
        public const int CpsClick = 1, CpsFinal = 2;            // Final: A = 클릭 수, F = 마지막 클릭 시각
        // F1ReactionGame
        public const int F1HeatStart = 1, F1Report = 2, F1HeatResult = 3;
        public const int F1CodeReact = 0, F1CodeJump = 1, F1CodeNone = 2;
        // AimGame
        public const int AimHit = 1, AimFinal = 2;              // Hit: A = 표적 번호, F = 반응 시간
        // StackGame
        public const int StackPlace = 1, StackFail = 2;         // Place: A = 층*100000 + (왼쪽+50000), F = 폭
        // TimingGame
        public const int TimingHit = 1, TimingMiss = 2, TimingOut = 3;
    }

    // ------------------------------------------------------------------------------------------------ 직렬화

    /// <summary>바이트 배열에 리틀 엔디안으로 직접 쓴다 (BinaryWriter 없이).</summary>
    public sealed class PacketWriter
    {
        readonly byte[] buffer = new byte[G1Protocol.MaxPacketSize];
        int length;

        public PacketWriter Header(Channel channel, int seq)
        {
            length = 0;
            Byte(G1Protocol.Magic).Byte(G1Protocol.Version).Byte((byte)channel).Int(seq);
            return this;
        }

        public PacketWriter Byte(byte v)
        {
            buffer[length++] = v;
            return this;
        }

        public PacketWriter Int(int v)
        {
            buffer[length++] = (byte)v;
            buffer[length++] = (byte)(v >> 8);
            buffer[length++] = (byte)(v >> 16);
            buffer[length++] = (byte)(v >> 24);
            return this;
        }

        public PacketWriter Float(float v) => Int(BitConverter.SingleToInt32Bits(v));

        public PacketWriter Bytes(byte[] data)
        {
            Buffer.BlockCopy(data, 0, buffer, length, data.Length);
            length += data.Length;
            return this;
        }

        public byte[] ToArray()
        {
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }
    }

    /// <summary>범위를 검사하며 읽는다. 잘린 패킷이면 FormatException.</summary>
    public sealed class PacketReader
    {
        readonly byte[] data;
        readonly int end;
        int pos;

        public PacketReader(byte[] data, int offset, int length)
        {
            this.data = data;
            pos = offset;
            end = offset + length;
        }

        public int Remaining => end - pos;

        void Need(int n)
        {
            if (pos + n > end) throw new FormatException("패킷이 너무 짧습니다");
        }

        public byte Byte()
        {
            Need(1);
            return data[pos++];
        }

        public int Int()
        {
            Need(4);
            int v = data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16 | data[pos + 3] << 24;
            pos += 4;
            return v;
        }

        public float Float() => BitConverter.Int32BitsToSingle(Int());

        public byte[] Rest()
        {
            var r = new byte[end - pos];
            Buffer.BlockCopy(data, pos, r, 0, r.Length);
            pos = end;
            return r;
        }
    }

    /// <summary>수신한 패킷 하나를 헤더 / 본문으로 나눈 것.</summary>
    public struct Packet
    {
        public Channel Channel;
        public int Seq;
        public byte[] Payload; // Ack 는 빈 배열

        /// <summary>형식이 틀린 패킷(다른 프로그램이 보낸 것 등)은 false.</summary>
        public static bool TryParse(byte[] data, int length, out Packet packet, out bool badVersion)
        {
            packet = default;
            badVersion = false;
            if (length < G1Protocol.HeaderSize || data[0] != G1Protocol.Magic) return false;
            if (data[1] != G1Protocol.Version)
            {
                badVersion = true;
                return false;
            }
            var r = new PacketReader(data, 2, length - 2);
            byte channel = r.Byte();
            if (channel > (byte)Channel.Ack) return false;
            packet.Channel = (Channel)channel;
            packet.Seq = r.Int();
            packet.Payload = r.Rest();
            return packet.Channel == Channel.Ack || packet.Payload.Length > 0;
        }
    }

    /// <summary>Payload 만들기 / 읽기 도우미. (새 PacketWriter 는 비어 있으므로 Header 없이 바로 쓴다)</summary>
    public static class Payloads
    {
        public static byte[] Simple(Op op) => new[] { (byte)op };

        public static byte[] OneInt(Op op, int value) => new PacketWriter().Byte((byte)op).Int(value).ToArray();

        public static byte[] Net(WireMsg m) =>
            new PacketWriter().Byte((byte)Op.Net).Byte(m.Type).Int(m.A).Int(m.B).Int(m.C).Int(m.D).Float(m.F).ToArray();

        public static WireMsg ReadNet(PacketReader r) => new WireMsg
        {
            Type = r.Byte(), A = r.Int(), B = r.Int(), C = r.Int(), D = r.Int(), F = r.Float(),
        };

        public static byte[] JoinOk(int index, int roomId, int nonce) =>
            new PacketWriter().Byte((byte)Op.JoinOk).Byte((byte)index).Int(roomId).Int(nonce).ToArray();

        public static byte[] Reject(RejectReason reason) => new[] { (byte)Op.Reject, (byte)reason };

        /// <summary>로그용 한 줄 설명.</summary>
        public static string Describe(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return "(empty)";
            var op = (Op)payload[0];
            if (op == Op.Net)
            {
                try { return "Net " + ReadNet(new PacketReader(payload, 1, payload.Length - 1)); }
                catch (FormatException) { }
            }
            return op.ToString();
        }
    }
}
