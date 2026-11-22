using MiniParty.Udp;

namespace MiniParty.Server.Games;

/// <summary>
/// 2조 미니게임의 액션·입력을 서버 터미널에 읽기 쉬운 한국어로 적는다 (교수님 확인용: 게임 중 모든 입력이 보이게).
/// 액션 번호는 BrainProtocol.cs (Unity 와 같은 파일).
/// </summary>
internal static class BrainLog
{
    public static bool IsBrainGame(int game) => System.Array.IndexOf(BrainProtocol.Games, game) >= 0;

    /// <summary>중계되는 액션 (Action). null = 이 게임 형식이 아님.</summary>
    public static string Action(int game, int kind, int a, float f) => game switch
    {
        BrainProtocol.OneToFiftyIndex => kind switch
        {
            BrainProtocol.OneToFifty.Press => $"{a + 1}번 칸의 숫자 {f:0} 누름",
            BrainProtocol.OneToFifty.Wrong => $"{a + 1}번 칸의 숫자 {f:0} 누름 (순서가 아니라 잠깐 멈춤)",
            _ => null,
        },
        BrainProtocol.ColorsIndex => kind switch
        {
            BrainProtocol.Colors.Correct => $"다른 색 칸 찾음 → {a}단계",
            BrainProtocol.Colors.Wrong => $"틀린 칸 누름 ({a}단계)",
            BrainProtocol.Colors.Final => $"시간 종료: 최종 {a}단계, 실수 {f:0}번",
            _ => null,
        },
        BrainProtocol.BallCountIndex => kind switch
        {
            BrainProtocol.BallCount.Answer => $"{a / 1000}판 답 {a % 1000}개 확정 ({f:0.00}초)",
            _ => null,
        },
        BrainProtocol.MemoryIndex => kind switch
        {
            BrainProtocol.Memory.Clear => $"{a}자리 거꾸로 입력 성공",
            BrainProtocol.Memory.Fail => $"입력 실패 → 남은 목숨 {a}",
            _ => null,
        },
        BrainProtocol.TenSecondsIndex => kind switch
        {
            BrainProtocol.TenSeconds.Stop => $"{a}판 {f:0.00}초에 멈춤 (10초와 차이 {System.Math.Abs(f - BrainProtocol.TenSeconds.Target):0.00})",
            _ => null,
        },
        _ => null,
    };

    /// <summary>서버에만 오는 입력 보고 (Input).</summary>
    public static string Input(int game, int kind, int a, float f) => game switch
    {
        BrainProtocol.BallCountIndex => kind switch
        {
            BrainProtocol.BallCount.InputDigit => $"숫자 입력 → 화면 값 {a}",
            BrainProtocol.BallCount.InputErase => a < 0 ? "지우기 → 빈칸" : $"지우기 → 화면 값 {a}",
            _ => $"입력 {kind} ({a})",
        },
        BrainProtocol.MemoryIndex => kind switch
        {
            BrainProtocol.Memory.InputKey => $"{f:0}번째 자리에 {a} 입력",
            _ => $"입력 {kind} ({a})",
        },
        _ => $"입력 {kind} ({a}, {f:0.##})",
    };
}
