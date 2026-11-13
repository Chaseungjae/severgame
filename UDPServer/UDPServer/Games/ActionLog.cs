namespace MiniParty.Server.Games;

/// <summary>
/// 클라이언트가 보낸 입력 신호를 사람이 읽을 수 있는 문장으로 바꾼다 (서버 터미널 기록용).
/// 번호는 서버와 Unity 가 같이 컴파일하는 BrainProtocol 을 그대로 쓴다.
/// 아직 문장을 붙이지 않은 게임은 원래 숫자를 그대로 보여준다.
/// </summary>
internal static class ActionLog
{
    public static string Who(int player) => player == -1 ? "진행(P1)" : $"P{player + 1}";

    public static string Describe(int gameIndex, NetMsg msg)
    {
        var a = msg.ToAction();
        string id = MiniGameCatalog.IdOf(gameIndex);
        return msg.Type == MsgType.Input ? DescribeInput(id, a) : DescribeAction(id, a);
    }

    static string DescribeAction(string id, GameAction a) => id switch
    {
        "OneToFiftyGame" => a.Kind switch
        {
            BrainProtocol.OneToFifty.Press => $"{a.A + 1}번 칸의 숫자 {(int)a.F} 누름",
            BrainProtocol.OneToFifty.Wrong => $"{a.A + 1}번 칸의 숫자 {(int)a.F} 누름 (순서 틀림 → 잠깐 멈춤)",
            _ => Raw(a),
        },
        "ColorGame" => a.Kind switch
        {
            BrainProtocol.Colors.Correct => $"다른 색 칸 맞힘 → {a.A}단계",
            BrainProtocol.Colors.Wrong => $"틀린 칸 누름 ({a.A}단계에서)",
            BrainProtocol.Colors.Final => $"30초 끝 — 최종 {a.A}단계, 틀린 횟수 {(int)a.F}",
            _ => Raw(a),
        },
        "BallCountGame" => a.Kind switch
        {
            BrainProtocol.BallCount.RoundStart => $"{a.A}판 시작 (공이 움직이기 시작)",
            BrainProtocol.BallCount.Answer => $"{a.A / 1000}판 답 확정: {a.A % 1000}개 (입력 시작 후 {a.F:0.00}초)",
            BrainProtocol.BallCount.RoundResult => $"이번 판 승자: P{a.A + 1}",
            _ => Raw(a),
        },
        "MemoryGame" => a.Kind switch
        {
            BrainProtocol.Memory.Clear => $"{a.A}자리 숫자를 거꾸로 모두 맞힘",
            BrainProtocol.Memory.Fail => $"틀림 — 남은 목숨 {a.A}",
            _ => Raw(a),
        },
        "TenSecondsGame" => a.Kind switch
        {
            BrainProtocol.TenSeconds.RoundStart => $"{a.A}판 시작 (스톱워치 출발)",
            BrainProtocol.TenSeconds.Stop => $"{a.A}판 스톱워치 멈춤: {a.F:0.00}초 (10초와 차이 {a.F - 10f:+0.00;-0.00})",
            BrainProtocol.TenSeconds.RoundResult => $"이번 판 승자: P{a.A + 1}",
            _ => Raw(a),
        },
        _ => Raw(a),
    };

    static string DescribeInput(string id, GameAction a) => id switch
    {
        "BallCountGame" => a.Kind switch
        {
            BrainProtocol.BallCount.InputDigit => $"숫자 입력 중: {a.A}",
            BrainProtocol.BallCount.InputErase => a.A < 0 ? "입력 지움 (비어 있음)" : $"입력 지움 → {a.A}",
            _ => Raw(a),
        },
        "MemoryGame" => a.Kind switch
        {
            BrainProtocol.Memory.InputKey => $"키패드 {a.A} 누름 ({(int)a.F}번째 자리)",
            _ => Raw(a),
        },
        _ => Raw(a),
    };

    static string Raw(GameAction a) => a.F == 0f ? $"신호 {a.Kind}, 값 {a.A}" : $"신호 {a.Kind}, 값 {a.A}, {a.F:0.###}";
}
