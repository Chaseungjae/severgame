namespace MiniParty.Server.Games;

/// <summary>
/// 미니게임 한 종류의 서버 판정. 라운드마다 새로 만들어진다.
/// 클라이언트가 보낸 입력(GameAction)을 검증하고, 승자가 정해지는 순간을 서버가 직접 판단한다.
/// </summary>
internal interface IGameRules
{
    /// <summary>아직 서버 판정을 옮기지 못한 게임은 true — 클라이언트의 승리 주장(Claim)을 받아준다.</summary>
    bool TrustsClaims { get; }

    /// <summary>승자가 정해졌으면 0 또는 1, 아직이면 -1.</summary>
    int Winner { get; }

    void Begin(int seed);

    /// <summary>입력 하나를 적용한다. 규칙에 어긋나면 false (서버가 상대에게 중계하지 않는다).</summary>
    bool Apply(GameAction action);
}

/// <summary>
/// 아직 서버 규칙이 없는 미니게임용. 입력은 그대로 중계하고 승패는 클라이언트의 주장을 따른다.
/// 조별로 Rules 를 옮기면서 하나씩 사라질 예정.
/// </summary>
internal sealed class DefaultRules : IGameRules
{
    public bool TrustsClaims => true;

    public int Winner => -1;

    public void Begin(int seed) { }

    public bool Apply(GameAction action) => action.Player is >= 0 and <= 1 || action.Player == -1;
}
