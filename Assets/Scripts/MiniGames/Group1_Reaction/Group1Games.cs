using System;

namespace MiniParty
{
    /// <summary>
    /// 1조 (반응·연타) minigame list. Owned by group 1: add / remove your games here only, so the four groups never
    /// edit the same file. Test them alone in Assets/Scenes/Groups/Group1_Reaction.unity.
    /// </summary>
    public static class Group1Games
    {
        public const string Name = "1조 · 반응·연타";

        public static readonly (string Title, Type Type)[] Games =
        {
            ("스페이스바 줄다리기", typeof(SpaceTugGame)),
            ("CPS 클릭 대결", typeof(CpsGame)),
            ("F1 반응 속도", typeof(F1ReactionGame)),
            ("에임 대결", typeof(AimGame)),
            ("블럭 쌓기", typeof(StackGame)),
            ("타이밍 서바이벌", typeof(TimingGame)),
        };
    }
}
