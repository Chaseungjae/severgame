using System;

namespace MiniParty
{
    /// <summary>
    /// 4조 (액션·이동) minigame list. Owned by group 4: add / remove your games here only, so the four groups never
    /// edit the same file. Test them alone in Assets/Scenes/Groups/Group4_Action.unity.
    /// </summary>
    public static class Group4Games
    {
        public const string Name = "4조 · 액션·이동";

        public static readonly (string Title, Type Type)[] Games =
        {
            ("100층 도전", typeof(StairsGame)),
            ("방향키 입력 대결", typeof(ArrowGame)),
            ("표창 막기", typeof(ShurikenGame)),
            ("죽림고수", typeof(BambooGame)),
            ("스네이크 대결", typeof(SnakeGame)),
            ("리듬 대결", typeof(RhythmGame)),
        };
    }
}
