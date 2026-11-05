using System;

namespace MiniParty
{
    /// <summary>
    /// 3조 (보드·턴제) minigame list. Owned by group 3: add / remove your games here only, so the four groups never
    /// edit the same file. Test them alone in Assets/Scenes/Groups/Group3_Board.unity.
    /// </summary>
    public static class Group3Games
    {
        public const string Name = "3조 · 보드·턴제";

        public static readonly (string Title, Type Type)[] Games =
        {
            ("사목", typeof(ConnectFourGame)),
            ("오델로", typeof(OthelloGame)),
            ("원을 그려봐요", typeof(CircleGame)),
            ("타자 대결", typeof(TypingGame)),
            ("야추 다이스", typeof(YachtGame)),
        };
    }
}
