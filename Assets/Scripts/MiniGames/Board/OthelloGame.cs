using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Othello (reversi) on 8x8. Place a disc so it sandwiches opponent discs in a straight line; they flip.
    /// No legal move = pass. When nobody can move, more discs wins. Legal moves are shown as dots.
    /// </summary>
    public class OthelloGame : BoardGameBase
    {
        public override string Title => "오델로";
        public override string Rules => "상대 돌을 내 돌 사이에 끼우면 뒤집혀 내 돌이 됩니다! (흑백은 무작위, 흑 선공)\n둘 곳이 없으면 자동 패스 · 둘 다 못 두면 종료\n돌이 더 많은 사람 승리 · 한 수 12초 (시간 초과 시 자동 착수)";
        public override string ControlsText => "마우스 클릭으로 돌 두기 (점 표시된 곳만 가능)";

        protected override int BoardSize => 8;
        protected override float CellSize => 76f;
        protected override float TurnTime => 12f;
        protected override Color BoardColor => Palette.Hex("#23845A");

        static readonly (int dx, int dy)[] Dirs8 = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

        static readonly int[] Weights =
        {
            120, -20, 20,  5,  5, 20, -20, 120,
            -20, -40, -5, -5, -5, -5, -40, -20,
             20,  -5, 15,  3,  3, 15,  -5,  20,
              5,  -5,  3,  3,  3,  3,  -5,   5,
              5,  -5,  3,  3,  3,  3,  -5,   5,
             20,  -5, 15,  3,  3, 15,  -5,  20,
            -20, -40, -5, -5, -5, -5, -40, -20,
            120, -20, 20,  5,  5, 20, -20, 120,
        };

        RectTransform hintLayer;

        protected override void DrawBoard()
        {
            var line = Palette.Hex("#15573A");
            float len = Size * CellPx;
            for (int i = 0; i <= Size; i++)
            {
                float o = (i - Size / 2f) * CellPx;
                UIKit.Img("H", Board, line).rectTransform.Place(new Vector2(0, o), new Vector2(len, 3));
                UIKit.Img("V", Board, line).rectTransform.Place(new Vector2(o, 0), new Vector2(3, len));
            }
            hintLayer = UIKit.Rect("Hints", Board).Stretch();
        }

        protected override void Build()
        {
            base.Build();
            foreach (var (x, y, c) in new[] { (3, 3, White), (4, 4, White), (3, 4, Black), (4, 3, Black) })
            {
                Cells[Idx(x, y)] = c;
                PlaceStone(Idx(x, y), c);
            }
            RefreshCounts();
        }

        List<int> Flips(int idx, int color)
        {
            var flips = new List<int>();
            if (Cells[idx] != Empty) return flips;
            int x = idx % Size, y = idx / Size, opp = Other(color);
            var line = new List<int>();
            foreach (var (dx, dy) in Dirs8)
            {
                line.Clear();
                int cx = x + dx, cy = y + dy;
                while (In(cx, cy) && Cells[Idx(cx, cy)] == opp)
                {
                    line.Add(Idx(cx, cy));
                    cx += dx;
                    cy += dy;
                }
                if (line.Count > 0 && In(cx, cy) && Cells[Idx(cx, cy)] == color) flips.AddRange(line);
            }
            return flips;
        }

        protected override bool IsLegal(int idx, int color) => Cells[idx] == Empty && Flips(idx, color).Count > 0;

        bool HasMoves(int color)
        {
            for (int i = 0; i < Cells.Length; i++)
                if (IsLegal(i, color)) return true;
            return false;
        }

        int Count(int color)
        {
            int n = 0;
            foreach (int c in Cells) if (c == color) n++;
            return n;
        }

        void RefreshCounts()
        {
            for (int p = 0; p < 2; p++) CountTexts[p].text = $"{Count(ColorOf(p))}개";
        }

        protected override int ApplyMove(int idx, int color)
        {
            var flips = Flips(idx, color);
            Cells[idx] = color;
            var stone = PlaceStone(idx, color);
            StartCoroutine(Tween.Pop(stone.transform, 1.3f, 0.15f));
            for (int i = 0; i < flips.Count; i++)
            {
                Cells[flips[i]] = color;
                StartCoroutine(FlipDisc(Stones[flips[i]], color, i * 0.04f));
            }
            RefreshCounts();

            int opp = Other(color);
            if (HasMoves(opp))
            {
                Turn = opp;
                return -1;
            }
            if (HasMoves(color))
            {
                StartCoroutine(PopupOnBoard($"{(opp == Black ? "흑" : "백")} 패스!", Palette.Gold));
                Turn = color;
                return -1;
            }

            // nobody can move: count
            int b = Count(Black), w = Count(White);
            StartCoroutine(PopupOnBoard($"흑 {b} : {w} 백", Color.white));
            if (b != w) return PlayerOf(b > w ? Black : White);
            return Seed % 3 == 0 ? PlayerOf(Black) : PlayerOf(White); // exact tie: deterministic coin
        }

        IEnumerator FlipDisc(Image disc, int color, float delay)
        {
            yield return new WaitForSeconds(delay);
            var target = color == Black ? StoneBlack : StoneWhite;
            yield return Tween.Run(0.12f, t => disc.transform.localScale = new Vector3(1f - t, 1f, 1f));
            disc.color = target;
            var shine = disc.transform.GetChild(0).GetComponent<Image>();
            shine.color = Color.white.WithAlpha(color == Black ? 0.18f : 0.6f);
            yield return Tween.Run(0.12f, t => disc.transform.localScale = new Vector3(t, 1f, 1f));
        }

        protected override void OnTurnStart()
        {
            foreach (Transform c in hintLayer) Destroy(c.gameObject);
            var color = (Turn == Black ? StoneBlack : StoneWhite).WithAlpha(0.35f);
            for (int i = 0; i < Cells.Length; i++)
            {
                if (!IsLegal(i, Turn)) continue;
                UIKit.Img("Hint", hintLayer, color, UIKit.Circle).rectTransform.Place(CellCenter(i % Size, i / Size), new Vector2(20, 20));
            }
        }

        protected override int ChooseAuto(int color)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (!IsLegal(i, color)) continue;
                float s = Weights[i] + Flips(i, color).Count * 2f + Random.value * 6f;
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }

        protected override void OnFinish()
        {
            foreach (Transform c in hintLayer) Destroy(c.gameObject);
            base.OnFinish();
        }

        // ---------- Punishment: a giant disc slams down, then the loser gets flipped over and over ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            bool winnerBlack = ColorOf(WinnerIndex) == Black;

            var disc = UIKit.Img("GiantDisc", ctx.Stage, winnerBlack ? StoneBlack : StoneWhite, UIKit.Circle);
            ctx.Start(DiscSpin(disc, winnerBlack));
            yield return PunishFx.Crush(ctx, disc.rectTransform, new Vector2(260, 260));

            // flip, flip, flip!
            loser.Idle = false;
            loser.Rt.pivot = new Vector2(0.5f, 0.5f);
            var center = feet + new Vector2(0, loser.Size * 0.5f);
            loser.Rt.anchoredPosition = center;
            for (int i = 0; i < 3; i++)
            {
                ctx.Start(PunishFx.Popup(ctx.Stage, "뒤집기!", center + new Vector2(0, 260f), Palette.Gold, 60));
                float from = i * 180f;
                yield return Tween.Run(0.4f, t =>
                {
                    loser.Rt.anchoredPosition = center + Vector2.up * (4f * 160f * t * (1 - t));
                    loser.Rt.localRotation = Quaternion.Euler(0, 0, from + 180f * t);
                });
                loser.Hit();
                ctx.Start(ctx.Shake(10f, 0.15f));
                yield return new WaitForSeconds(0.12f);
            }
            // one more flip to land right side up
            yield return Tween.Run(0.35f, t =>
            {
                loser.Rt.anchoredPosition = center + Vector2.up * (4f * 120f * t * (1 - t));
                loser.Rt.localRotation = Quaternion.Euler(0, 0, 540f + 180f * t);
            });
            loser.Rt.pivot = new Vector2(0.5f, 0f);
            loser.Rt.anchoredPosition = feet;
            loser.Rt.localRotation = Quaternion.identity;
            loser.Idle = true;
        }

        static IEnumerator DiscSpin(Image disc, bool black)
        {
            // flips back and forth between black and white while falling
            for (int i = 0; i < 6 && disc != null; i++)
            {
                yield return Tween.Run(0.07f, t => { if (disc) disc.transform.localScale = new Vector3(1f - t, 1f, 1f); });
                if (!disc) yield break;
                bool showBlack = (i % 2 == 0) != black;
                disc.color = showBlack ? StoneBlack : StoneWhite;
                yield return Tween.Run(0.07f, t => { if (disc) disc.transform.localScale = new Vector3(t, 1f, 1f); });
            }
            if (disc) disc.color = black ? StoneBlack : StoneWhite;
        }
    }
}
