using System.Collections.Generic;

namespace MiniParty
{
    /// <summary>
    /// 미니게임 번호의 기준표. 서버가 라운드를 고르고 판정 규칙을 찾을 때 쓴다.
    /// 순서는 MiniGameRegistry.Games (= Group1~4Games.Games 를 이어붙인 것) 와 반드시 같아야 하며,
    /// Id 는 Unity 쪽 클래스 이름이다. 조별 목록에 게임을 추가하면 여기도 같이 고친다.
    /// </summary>
    public static class MiniGameCatalog
    {
        public struct Entry
        {
            public string Id;
            public string Title;
            public int Group;

            public Entry(string id, string title, int group)
            {
                Id = id;
                Title = title;
                Group = group;
            }
        }

        public static readonly string[] GroupNames = { "전체", "1조 · 반응속도", "2조 · 두뇌·관찰", "3조 · 보드게임", "4조 · 액션" };

        public static readonly Entry[] All =
        {
            new Entry("SpaceTugGame",   "스페이스바 줄다리기", 1),
            new Entry("CpsGame",        "CPS 클릭 대결",      1),
            new Entry("F1ReactionGame", "F1 반응 속도",       1),
            new Entry("AimGame",        "에임 대결",          1),
            new Entry("StackGame",      "블럭 쌓기",          1),
            new Entry("TimingGame",     "타이밍 서바이벌",     1),

            new Entry("OneToFiftyGame", "1 to 50",           2),
            new Entry("BallCountGame",  "공 개수 세기",        2),
            new Entry("ColorGame",      "색깔 찾기",          2),
            new Entry("MemoryGame",     "순간 기억력",         2),
            new Entry("TenSecondsGame", "10초 맞추기",        2),

            new Entry("ConnectFourGame", "사목",              3),
            new Entry("OthelloGame",     "오델로",            3),
            new Entry("CircleGame",      "원을 그려봐요",      3),
            new Entry("TypingGame",      "타자 대결",         3),
            new Entry("YachtGame",       "야추 다이스",        3),

            new Entry("StairsGame",     "100층 도전",         4),
            new Entry("ArrowGame",      "방향키 입력 대결",    4),
            new Entry("ShurikenGame",   "표창 막기",          4),
            new Entry("BambooGame",     "죽림고수",           4),
            new Entry("SnakeGame",      "스네이크 대결",       4),
            new Entry("RhythmGame",     "리듬 대결",          4),
        };

        public static int Count => All.Length;

        public static bool IsValid(int index) => index >= 0 && index < All.Length;

        public static string IdOf(int index) => IsValid(index) ? All[index].Id : "";

        public static string TitleOf(int index) => IsValid(index) ? All[index].Title : "알 수 없는 게임";

        public static int GroupOf(int index) => IsValid(index) ? All[index].Group : 0;

        /// <summary>그 조의 게임 번호들 (0 = 전체).</summary>
        public static List<int> PoolFor(int group)
        {
            var pool = new List<int>();
            for (int i = 0; i < All.Length; i++)
                if (group == 0 || All[i].Group == group) pool.Add(i);
            if (pool.Count == 0)
                for (int i = 0; i < All.Length; i++) pool.Add(i);
            return pool;
        }

        /// <summary>Unity 쪽 목록(MiniGameRegistry)과 번호가 어긋났는지 확인한다.</summary>
        public static bool Matches(string[] typeNames, out string problem)
        {
            if (typeNames == null || typeNames.Length != All.Length)
            {
                problem = $"게임 개수가 다름: 카탈로그 {All.Length}개 ↔ 레지스트리 {(typeNames == null ? 0 : typeNames.Length)}개";
                return false;
            }
            for (int i = 0; i < All.Length; i++)
            {
                if (typeNames[i] == All[i].Id) continue;
                problem = $"{i}번이 다름: 카탈로그 {All[i].Id} ↔ 레지스트리 {typeNames[i]}";
                return false;
            }
            problem = "";
            return true;
        }
    }
}
