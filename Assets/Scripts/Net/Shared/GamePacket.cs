using System.Globalization;

namespace MiniParty
{
    public enum MsgType : byte
    {
        Hello = 1,      // 클라 → 서버 : A = 프로토콜 버전, B = 조 번호(0 = 전체), C = 캐릭터
        Welcome,        // 서버 → 클라 : A = 내 슬롯 (0 = P1, 1 = P2)
        Match,          // 서버 → 클라 : A = P1 캐릭터, B = P2 캐릭터 — 두 명이 모였다
        Reject,         // 서버 → 클라 : A = RejectReason
        StartRound,     // 서버 → 클라 : A = 라운드, B = 미니게임 번호, C = 시드
        Barrier,        // 클라 → 서버 : A = 태그 — 여기까지 준비됐다
        Go,             // 서버 → 클라 : A = 태그 — 둘 다 준비됐으니 진행
        Action,         // 양방향      : A = kind, B = player, C = value, D = 라운드, F = float 값
        Claim,          // 클라 → 서버 : A = 승자 주장, D = 라운드 (서버 판정이 아직 없는 미니게임용)
        RoundResult,    // 서버 → 클라 : A = 승자, B = 데미지
        PeerLeft,       // 서버 → 클라 : 상대가 나갔다
        Ping,           // 클라 → 서버 : 생존 신호
        Pong,           // 서버 → 클라
        Leave,          // 클라 → 서버 : 나간다 (타임아웃을 기다리지 않고 바로 자리를 비운다)
        Input,          // 클라 → 서버 : 입력 보고 (Action 과 같은 칸 배치). 서버 터미널에만 기록, 중계 안 함
    }

    public enum RejectReason
    {
        None = 0,
        Full = 1,          // 이미 두 명이 들어와 있음
        BadVersion = 2,    // 프로토콜 버전 불일치
        InGame = 3,        // 경기 중인 방
        GroupMismatch = 4, // 상대와 다른 조(씬)로 접속함
    }

    /// <summary>게임 메시지 한 개. 모든 인자가 숫자라 텍스트로 주고받아도 디버깅하기 쉽다.</summary>
    public struct NetMsg
    {
        public MsgType Type;
        public int A, B, C, D;
        public float F;

        public GameAction ToAction() => new GameAction { Kind = A, Player = B, A = C, F = F };

        public static NetMsg FromAction(GameAction act, int round, MsgType type = MsgType.Action) =>
            new NetMsg { Type = type, A = act.Kind, B = act.Player, C = act.A, D = round, F = act.F };
    }

    /// <summary>게임 안에서 일어난 사건 하나. Player = -1 이면 판정자가 결정한 시스템 이벤트.</summary>
    public struct GameAction
    {
        public int Kind, Player, A;
        public float F;
    }

    /// <summary>두 클라이언트의 진행을 맞추는 지점. 태그 = 라운드 * 10 + 종류.</summary>
    public static class Barriers
    {
        public const int Ready = 1;   // 소개 카드까지 봤다 → 카운트다운 시작
        public const int Next = 2;    // 시상식까지 끝났다 → 다음 라운드

        public static int Tag(int round, int kind) => round * 10 + kind;

        public static int KindOf(int tag) => tag % 10;

        public static int RoundOf(int tag) => tag / 10;
    }

    /// <summary>NetMsg ↔ 패킷 본문("MSG|타입|A|B|C|D|F") 변환.</summary>
    public static class GamePacket
    {
        const string Prefix = "MSG";

        public static string Encode(NetMsg msg)
        {
            return Prefix + "|" + (int)msg.Type + "|" + msg.A + "|" + msg.B + "|" + msg.C + "|" + msg.D + "|" +
                   msg.F.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Encode(MsgType type, int a = 0, int b = 0, int c = 0, int d = 0, float f = 0f) =>
            Encode(new NetMsg { Type = type, A = a, B = b, C = c, D = d, F = f });

        public static bool TryDecode(string body, out NetMsg msg)
        {
            msg = default;
            if (body == null) return false;

            string[] parts = body.Split('|');
            if (parts.Length < 7 || parts[0] != Prefix) return false;
            if (!int.TryParse(parts[1], out int type)) return false;
            if (!int.TryParse(parts[2], out int a) || !int.TryParse(parts[3], out int b)) return false;
            if (!int.TryParse(parts[4], out int c) || !int.TryParse(parts[5], out int d)) return false;
            if (!float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;

            msg = new NetMsg { Type = (MsgType)type, A = a, B = b, C = c, D = d, F = f };
            return true;
        }
    }
}
