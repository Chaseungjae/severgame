namespace MiniParty.Server.Games;

/// <summary>
/// 미니게임 번호 → 서버 판정 규칙. 아직 옮기지 않은 게임은 DefaultRules 로 떨어져
/// 지금처럼 클라이언트가 알려주는 승자를 쓴다 (조별로 하나씩 교체해 나가면 된다).
/// </summary>
internal static class RulesCatalog
{
    public static IGameRules For(int gameIndex)
    {
        return MiniGameCatalog.IdOf(gameIndex) switch
        {
            "OneToFiftyGame" => new OneToFiftyRules(),
            _ => new DefaultRules(),
        };
    }
}
