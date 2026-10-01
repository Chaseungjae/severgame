namespace MiniParty
{
    /// <summary>
    /// Judge for "who lasts longer" games (stacking, timing survival). Each player plays independently until they
    /// are out. The result is decided as soon as it is certain: once one player is out, the other wins the moment
    /// they beat that score. If both are out with equal scores, the lower tie-break value wins.
    /// </summary>
    public class SurvivalJudge
    {
        public readonly int[] Score = new int[2];
        public readonly bool[] Out = new bool[2];
        public readonly float[] TieBreak = new float[2];

        /// <summary>Winner index, or -1 while undecided.</summary>
        public int Check()
        {
            for (int p = 0; p < 2; p++)
                if (Out[1 - p] && Score[p] > Score[1 - p]) return p;

            if (Out[0] && Out[1])
            {
                if (Score[0] != Score[1]) return Score[0] > Score[1] ? 0 : 1;
                return TieBreak[0] <= TieBreak[1] ? 0 : 1;
            }
            return -1;
        }
    }
}
