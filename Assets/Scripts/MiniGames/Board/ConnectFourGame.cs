using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Connect Four (사목) on a 7x6 upright board: drop a disc into a column, it falls to the lowest empty slot.
    /// Four in a row (any direction) wins. A move is the column index.
    /// </summary>
    public class ConnectFourGame : BoardGameBase
    {
        public override string Title => "사목";
        public override string Rules => "원하는 줄을 클릭하면 돌이 맨 아래로 떨어집니다.\n가로·세로·대각선으로 4개를 먼저 이으면 승리! (선공은 무작위)\n한 수 8초 (시간 초과 시 자동 착수)";
        public override string ControlsText => "마우스로 줄(세로 칸) 클릭 (번갈아 가며)";
        public override string OnlineControlsText => ControlsText;

        protected override int BoardSize => 7;
        protected override int BoardRows => 6;
        protected override float CellSize => 92f;
        protected override float TurnTime => 8f;
        protected override Color BoardColor => Palette.Hex("#2F5BD3");

        static readonly Color Red = Palette.Hex("#FF4D5A");
        static readonly Color Yellow = Palette.Hex("#FFD23F");
        static readonly (int dx, int dy)[] Dirs = { (1, 0), (0, 1), (1, 1), (1, -1) };

        protected override Color StoneColor(int color) => color == Black ? Red : Yellow;
        protected override string ColorName(int color) => color == Black ? "빨강" : "노랑";

        protected override void DrawBoard()
        {
            var hole = Palette.Hex("#1A2B6B");
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Size; x++)
                    UIKit.Img("Hole", Board, hole, UIKit.Circle).rectTransform.Place(CellCenter(x, y), Vector2.one * CellPx * 0.82f);
        }

        /// <summary>Lowest empty row in a column, or -1 when full.</summary>
        int Landing(int col)
        {
            for (int y = Rows - 1; y >= 0; y--)
                if (Cells[Idx(col, y)] == Empty) return y;
            return -1;
        }

        protected override int PickMove(int x, int y) => x;
        protected override int MoveCell(int move) => move >= 0 && move < Size && Landing(move) >= 0 ? Idx(move, Landing(move)) : 0;
        protected override bool IsLegal(int col, int color) => col >= 0 && col < Size && Landing(col) >= 0;

        int LineLength(int x, int y, int dx, int dy, int color)
        {
            int n = 1;
            for (int k = 1; In(x + dx * k, y + dy * k) && Cells[Idx(x + dx * k, y + dy * k)] == color; k++) n++;
            for (int k = 1; In(x - dx * k, y - dy * k) && Cells[Idx(x - dx * k, y - dy * k)] == color; k++) n++;
            return n;
        }

        bool Wins(int x, int y, int color)
        {
            foreach (var (dx, dy) in Dirs)
                if (LineLength(x, y, dx, dy, color) >= 4) return true;
            return false;
        }

        protected override int ApplyMove(int col, int color)
        {
            int y = Landing(col);
            int idx = Idx(col, y);
            Cells[idx] = color;
            var disc = PlaceStone(idx, color);
            StartCoroutine(Drop(disc.rectTransform, CellCenter(col, y)));

            if (Wins(col, y, color))
            {
                StartCoroutine(PopupOnBoard("사목!", Players[PlayerOf(color)].Color));
                return PlayerOf(color);
            }
            if (MoveCount + 1 >= Cells.Length) return Seed % 3 == 0 ? PlayerOf(Black) : PlayerOf(White); // full board

            Turn = Other(color);
            return -1;
        }

        IEnumerator Drop(RectTransform disc, Vector2 to)
        {
            var from = new Vector2(to.x, CellCenter(0, 0).y + CellPx);
            yield return Tween.Run(0.25f, t => disc.anchoredPosition = Vector2.Lerp(from, to, t), Ease.InQuad);
            yield return Tween.Run(0.1f, t => disc.anchoredPosition = to + Vector2.up * (4f * 10f * t * (1 - t)));
        }

        // ---------- CPU / timeout move ----------

        bool WinsIfPlayed(int col, int color)
        {
            int y = Landing(col);
            if (y < 0) return false;
            int idx = Idx(col, y);
            Cells[idx] = color;
            bool win = Wins(col, y, color);
            Cells[idx] = Empty;
            return win;
        }

        int WindowScore(int color)
        {
            int score = 0, opp = Other(color);
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Size; x++)
                foreach (var (dx, dy) in Dirs)
                {
                    int ex = x + dx * 3, ey = y + dy * 3;
                    if (!In(ex, ey)) continue;
                    int mine = 0, theirs = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int c = Cells[Idx(x + dx * k, y + dy * k)];
                        if (c == color) mine++;
                        else if (c == opp) theirs++;
                    }
                    if (theirs == 0) score += mine == 3 ? 12 : mine == 2 ? 4 : mine == 1 ? 1 : 0;
                    if (mine == 0) score -= theirs == 3 ? 14 : theirs == 2 ? 4 : 0;
                }
            return score;
        }

        protected override int ChooseAuto(int color)
        {
            int opp = Other(color);
            var legal = new List<int>();
            for (int c = 0; c < Size; c++) if (IsLegal(c, color)) legal.Add(c);
            if (legal.Count == 0) return -1;

            foreach (int c in legal) if (WinsIfPlayed(c, color)) return c;   // win now
            foreach (int c in legal) if (WinsIfPlayed(c, opp)) return c;     // block

            int best = legal[0];
            float bestScore = float.MinValue;
            foreach (int c in legal)
            {
                int y = Landing(c);
                int idx = Idx(c, y);
                Cells[idx] = color;
                float s = WindowScore(color) - Mathf.Abs(c - Size / 2) * 2f + Random.value * 3f;
                // don't hand the opponent a win on top of this disc
                if (y > 0 && WinsIfPlayed(c, opp)) s -= 500f;
                Cells[idx] = Empty;
                if (s > bestScore) { bestScore = s; best = c; }
            }
            return best;
        }

        // ---------- Punishment: four giant discs drop onto the loser one by one — 사목! ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var color = StoneColor(ColorOf(WinnerIndex));
            var discs = new List<RectTransform>();
            const float d = 120f;

            for (int i = 0; i < 4; i++)
            {
                float squash = 1f - 0.14f * (i + 1);
                float top = feet.y + loser.Size * squash + i * (d - 6f);
                var disc = UIKit.Img("Disc", ctx.Stage, color, UIKit.Circle).rectTransform;
                disc.Place(new Vector2(feet.x, 760), new Vector2(d, d), pivot: new Vector2(0.5f, 0f));
                UIKit.Img("Ring", disc, Color.black.WithAlpha(0.15f), UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(d * 0.6f, d * 0.6f));
                discs.Add(disc);
                yield return Tween.Run(0.22f, t => disc.anchoredPosition = new Vector2(feet.x, Mathf.Lerp(760, top, t)), Ease.InQuad);
                loser.SetFace(CharacterView.Face.Hurt);
                loser.BaseScale = new Vector3(2f - squash, squash, 1f);
                for (int k = 0; k < discs.Count; k++)
                    discs[k].anchoredPosition = new Vector2(feet.x, feet.y + loser.Size * squash + k * (d - 6f));
                ctx.Start(ctx.Shake(8f + i * 4f, 0.15f));
                ctx.Start(PunishFx.Popup(ctx.Stage, $"{i + 1}!", feet + new Vector2(170f, 200f + i * 60f), Palette.Gold, 56));
                yield return new WaitForSeconds(0.1f);
            }

            ctx.Start(PunishFx.Popup(ctx.Stage, "사목!!", feet + new Vector2(0, 640), Palette.Red, 90));
            yield return new WaitForSeconds(0.7f);
            foreach (var disc in discs) ctx.Start(Roll(disc));
            yield return new WaitForSeconds(0.5f);
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }

        static IEnumerator Roll(RectTransform disc)
        {
            float dir = Random.value < 0.5f ? -1f : 1f;
            var img = disc.GetComponent<Image>();
            var vel = new Vector2(dir * Random.Range(300f, 600f), Random.Range(100f, 300f));
            float life = 0.9f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1800f * dt;
                disc.anchoredPosition += vel * dt;
                disc.Rotate(0, 0, -dir * 600f * dt);
                life -= dt;
                img.color = img.color.WithAlpha(Mathf.Clamp01(life / 0.4f));
                yield return null;
            }
            Destroy(disc.gameObject);
        }
    }
}
