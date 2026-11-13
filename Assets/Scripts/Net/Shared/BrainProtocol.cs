namespace MiniParty
{
    /// <summary>
    /// 2조(두뇌·관찰) 미니게임의 액션 규격. 클라이언트가 보내는 값과 서버(판정 · 터미널 기록)가 읽는 값이
    /// 같아야 하므로 서버와 Unity 가 이 파일을 같이 컴파일한다.
    ///
    /// 액션(MsgType.Action)은 상대에게 중계되어 두 화면을 맞춘다.
    /// 입력 보고(MsgType.Input)는 서버 터미널에만 남고 상대에게는 가지 않는다 (답이 새지 않게).
    /// </summary>
    public static class BrainProtocol
    {
        /// <summary>1 to 50 — A = 누른 칸 번호, F = 그 칸에 적혀 있던 수.</summary>
        public static class OneToFifty
        {
            public const int Press = 1;   // 순서에 맞는 입력
            public const int Wrong = 2;   // 틀린 입력 (잠깐 멈춤)
            public const int Goal = 50;
        }

        /// <summary>색깔 찾기.</summary>
        public static class Colors
        {
            public const int Correct = 1; // A = 올라간 단계
            public const int Wrong = 2;   // A = 현재 단계
            public const int Final = 3;   // A = 최종 단계, F = 틀린 횟수
        }

        /// <summary>공 개수 세기. Player = -1 인 것은 P1 이 진행하는 시스템 이벤트.</summary>
        public static class BallCount
        {
            public const int RoundStart = 1;  // 시스템: A = 판 번호
            public const int Answer = 2;      // A = 판 번호 * 1000 + 답, F = 확정까지 걸린 시간
            public const int RoundResult = 3; // 시스템: A = 그 판 승자

            public const int InputDigit = 1;  // 입력 보고: A = 지금까지 친 수
            public const int InputErase = 2;  // 입력 보고: A = 지운 뒤 남은 수 (-1 = 비어 있음)
        }

        /// <summary>순간 기억력.</summary>
        public static class Memory
        {
            public const int Clear = 1;       // A = 맞힌 자릿수
            public const int Fail = 2;        // A = 남은 목숨

            public const int InputKey = 1;    // 입력 보고: A = 누른 숫자, F = 몇 번째 자리 (1부터)
        }

        /// <summary>10초 맞추기. Player = -1 인 것은 P1 이 진행하는 시스템 이벤트.</summary>
        public static class TenSeconds
        {
            public const int RoundStart = 1;  // 시스템: A = 판 번호
            public const int Stop = 2;        // A = 판 번호, F = 멈춘 시간(초)
            public const int RoundResult = 3; // 시스템: A = 그 판 승자
        }
    }
}
