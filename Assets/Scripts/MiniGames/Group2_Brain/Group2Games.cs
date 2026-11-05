using System;

namespace MiniParty
{
    /// <summary>
    /// 2조 (두뇌·관찰) minigame list. Owned by group 2: add / remove your games here only, so the four groups never
    /// edit the same file. Test them alone in Assets/Scenes/Groups/Group2_Brain.unity.
    /// </summary>
    public static class Group2Games
    {
        public const string Name = "2조 · 두뇌·관찰";

        public static readonly (string Title, Type Type)[] Games =
        {
            ("1 to 50", typeof(OneToFiftyGame)),
            ("공 개수 세기", typeof(BallCountGame)),
            ("색깔 찾기", typeof(ColorGame)),
            ("순간 기억력", typeof(MemoryGame)),
            ("10초 맞추기", typeof(TenSecondsGame)),
        };
    }
}
