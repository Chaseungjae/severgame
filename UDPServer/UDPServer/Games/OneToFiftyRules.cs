namespace MiniParty.Server.Games;

/// <summary>
/// 1 to 50 서버 판정. 1부터 50까지 순서대로 눌렀는지 서버가 직접 확인하고,
/// 50을 먼저 누른 쪽을 승자로 정한다. 순서를 건너뛴 입력은 중계하지 않는다.
/// </summary>
internal sealed class OneToFiftyRules : IGameRules
{
    readonly int[] next = { 1, 1 };

    public bool TrustsClaims => false;

    public int Winner { get; private set; } = -1;

    public void Begin(int seed) { }

    public bool Apply(GameAction action)
    {
        if (action.Player < 0 || action.Player > 1) return false;
        if (action.Kind == BrainProtocol.OneToFifty.Wrong) return true;
        if (action.Kind != BrainProtocol.OneToFifty.Press) return false;

        int value = (int)action.F;
        if (value != next[action.Player]) return false;

        next[action.Player]++;
        if (next[action.Player] > BrainProtocol.OneToFifty.Goal && Winner < 0) Winner = action.Player;
        return true;
    }
}
