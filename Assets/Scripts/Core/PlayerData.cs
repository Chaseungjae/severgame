using UnityEngine;

namespace MiniParty
{
    public class CharacterDef
    {
        public readonly string Name;
        public readonly Color Body;
        public readonly Color Belly;
        public readonly int Feature; // 0 none, 1 ears, 2 antenna, 3 leaf, 4 horns

        public CharacterDef(string name, string body, string belly, int feature)
        {
            Name = name;
            Body = Palette.Hex(body);
            Belly = Palette.Hex(belly);
            Feature = feature;
        }
    }

    public static class Roster
    {
        public static readonly CharacterDef[] All =
        {
            new CharacterDef("토리", "#FF8A3D", "#FFD9B8", 1),
            new CharacterDef("블루", "#4C8DFF", "#CFE0FF", 2),
            new CharacterDef("모찌", "#FF7EB3", "#FFD6E8", 1),
            new CharacterDef("민트", "#2EC4A6", "#C8F4EA", 3),
            new CharacterDef("퍼플", "#9B6BFF", "#E2D6FF", 4),
            new CharacterDef("레몬", "#F5C400", "#FFF1B0", 2),
        };
    }

    public class PlayerData
    {
        public readonly int Index;
        public CharacterDef Character;
        public int MaxHp = 100;
        public int Hp = 100;
        public bool IsCpu;
        public bool IsYou; // online: the player controlled on this PC

        public PlayerData(int index)
        {
            Index = index;
            Character = Roster.All[index];
        }

        public string Tag => $"P{Index + 1}";
        public Color Color => Character.Body;
        public string DisplayName => $"{Tag} · {Character.Name}{(IsCpu ? " (CPU)" : IsYou ? " (나)" : "")}";
    }
}
