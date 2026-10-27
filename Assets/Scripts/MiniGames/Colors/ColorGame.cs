using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Color expert (arealme "colors"): find the one tile whose shade is slightly different. The grid grows and the
    /// difference shrinks each level. Both players get the same puzzles; most solved in 30 seconds wins.
    /// </summary>
    public class ColorGame : MiniGameBase
    {
        public override string Title => "색깔 찾기";
        public override string Rules => "타일 중 색이 살짝 다른 하나를 찾아 누르세요!\n맞힐수록 칸은 많아지고 색 차이는 줄어듭니다. 틀리면 잠깐 멈춤\n30초 동안 더 많이 맞힌 사람 승리 (두 사람 같은 문제)";
        public override string ControlsText => "다른 색 칸을 마우스로 클릭";

        const float Duration = 30f;
        const float GridPx = 560f;
        const float WrongLock = 0.6f;
        const int ActCorrect = 1, ActWrong = 2, ActFinal = 3;

        class Board
        {
            public RectTransform Panel, Grid, CursorFrame;
            public Text Score, Time;
            public Image[] Tiles;
            public int Level, N, Odd, Cursor, Mistakes;
            public float Elapsed, LockUntil, CpuAt;
            public bool FinalSent, HasFinal;
        }

        readonly Board[] boards = new Board[2];

        protected override void Build()
        {
            Background();
            for (int p = 0; p < 2; p++)
            {
                var b = new Board { Panel = PlayerPanel(p, 660, 820, -70) };
                boards[p] = b;
                b.Score = UIKit.Label("Score", b.Panel, "0", 64, Color.white, TextAnchor.MiddleLeft);
                b.Score.rectTransform.Place(new Vector2(-300, 300), new Vector2(300, 80), pivot: new Vector2(0, 0.5f));
                b.Time = UIKit.Label("Time", b.Panel, $"{Duration:0.0}s", 40, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
                b.Time.rectTransform.Place(new Vector2(300, 300), new Vector2(300, 80), pivot: new Vector2(1, 0.5f));
                b.Grid = UIKit.Rect("Grid", b.Panel);
                b.Grid.Place(new Vector2(0, -70), new Vector2(GridPx, GridPx));
                b.CursorFrame = UIKit.Img("Cursor", b.Grid, Color.white, UIKit.Rounded).rectTransform;
                b.CursorFrame.gameObject.SetActive(false); // no selection frame: it hid the color difference
                BuildLevel(p);
            }
        }

        static int GridSize(int level) => Mathf.Min(2 + (level + 1) / 2, 8);

        /// <summary>Same puzzle for both players at the same level (shared seed).</summary>
        void Puzzle(int level, out int n, out int odd, out Color baseColor, out Color oddColor)
        {
            var rng = new System.Random(Seed * 97 + level * 7919);
            n = GridSize(level);
            odd = rng.Next(n * n);
            float h = (float)rng.NextDouble();
            float s = 0.45f + (float)rng.NextDouble() * 0.35f;
            float v = 0.55f + (float)rng.NextDouble() * 0.3f;
            float diff = Mathf.Max(0.035f, 0.24f * Mathf.Pow(0.87f, level));
            baseColor = Color.HSVToRGB(h, s, v);
            oddColor = Color.HSVToRGB(h, Mathf.Clamp01(s - diff * 0.6f), Mathf.Clamp01(v + diff));
        }

        void BuildLevel(int p)
        {
            var b = boards[p];
            if (b.Tiles != null) foreach (var t in b.Tiles) Destroy(t.gameObject);
            Puzzle(b.Level, out b.N, out b.Odd, out var baseColor, out var oddColor);
            float gap = b.N >= 6 ? 6f : 10f;
            float cell = (GridPx - gap * (b.N - 1)) / b.N;
            b.Tiles = new Image[b.N * b.N];
            for (int i = 0; i < b.Tiles.Length; i++)
            {
                var tile = UIKit.Img("Tile", b.Grid, i == b.Odd ? oddColor : baseColor, UIKit.Rounded);
                tile.rectTransform.Place(TilePos(b, i, cell, gap), new Vector2(cell, cell));
                tile.raycastTarget = true;
                int index = i;
                tile.gameObject.AddComponent<TileClick>().OnClick = () => OnTileClicked(p, index);
                b.Tiles[i] = tile;
            }
            b.Cursor = Mathf.Min(b.Cursor, b.Tiles.Length - 1);
            b.CursorFrame.sizeDelta = new Vector2(cell + 12, cell + 12);
            b.CursorFrame.SetAsFirstSibling();
            UpdateCursor(b);
            b.CpuAt = b.Elapsed + Random.Range(0.8f, 1.5f) + b.Level * 0.06f;
            b.Score.text = b.Level.ToString();
        }

        static Vector2 TilePos(Board b, int i, float cell, float gap)
        {
            int r = i / b.N, c = i % b.N;
            float step = cell + gap, origin = -(b.N - 1) * step / 2f;
            return new Vector2(origin + c * step, -origin - r * step);
        }

        void UpdateCursor(Board b)
        {
            if (b.Tiles == null || b.Cursor >= b.Tiles.Length) return;
            b.CursorFrame.anchoredPosition = b.Tiles[b.Cursor].rectTransform.anchoredPosition;
        }

        void OnTileClicked(int p, int index)
        {
            if (!Running || !IsHumanHere(p)) return;
            boards[p].Cursor = index;
            UpdateCursor(boards[p]);
            TryPick(p, index);
        }

        void TryPick(int p, int index)
        {
            var b = boards[p];
            if (b.FinalSent || Time.time < b.LockUntil) return;
            if (index == b.Odd) Emit(ActCorrect, p, b.Level + 1);
            else Emit(ActWrong, p, b.Level);
        }

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var b = boards[p];
                if (!IsLocal(p) || b.FinalSent) continue;
                b.Elapsed += Time.deltaTime;
                b.Time.text = $"{Mathf.Max(0, Duration - b.Elapsed):0.0}s";
                if (b.Elapsed >= Duration)
                {
                    b.FinalSent = true;
                    Emit(ActFinal, p, b.Level, b.Mistakes);
                    continue;
                }

                if (IsCpuHere(p))
                {
                    if (b.Elapsed >= b.CpuAt)
                    {
                        bool right = Random.value < Mathf.Lerp(0.95f, 0.7f, b.Level / 25f);
                        int pick = right ? b.Odd : (b.Odd + 1 + Random.Range(0, b.Tiles.Length - 1)) % b.Tiles.Length;
                        b.Cursor = pick;
                        UpdateCursor(b);
                        TryPick(p, pick);
                        b.CpuAt = b.Elapsed + Random.Range(0.8f, 1.5f) + b.Level * 0.06f;
                    }
                    continue;
                }

            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var b = boards[p];
            switch (act.Kind)
            {
                case ActCorrect:
                    if (act.A != b.Level + 1 || b.HasFinal) return;
                    b.Level = act.A;
                    BuildLevel(p);
                    StartCoroutine(Tween.Pop(b.Score.transform, 1.4f, 0.15f));
                    StartCoroutine(Tween.Pop(b.Grid, 1.05f, 0.12f));
                    Juice.Sparkle(b.Panel, new Vector2(0, -70), null, 0.6f);
                    break;
                case ActWrong:
                    b.Mistakes++;
                    b.LockUntil = Time.time + WrongLock;
                    StartCoroutine(Wrong(b));
                    break;
                case ActFinal:
                    b.HasFinal = true;
                    b.Level = act.A;
                    b.Mistakes = Mathf.RoundToInt(act.F);
                    b.Score.text = b.Level.ToString();
                    b.Time.text = "종료";
                    if (IsAuthority && boards[0].HasFinal && boards[1].HasFinal)
                    {
                        var x = boards[0];
                        var y = boards[1];
                        if (x.Level != y.Level) DeclareWinner(x.Level > y.Level ? 0 : 1);
                        else if (x.Mistakes != y.Mistakes) DeclareWinner(x.Mistakes < y.Mistakes ? 0 : 1);
                        else DeclareWinner(Seed % 2);
                    }
                    break;
            }
        }

        IEnumerator Wrong(Board b)
        {
            var home = b.Grid.anchoredPosition;
            var flash = UIKit.Label("X", b.Panel, "×", 160, Palette.Red.WithAlpha(0.8f));
            flash.rectTransform.Place(home, new Vector2(300, 300));
            yield return Tween.Run(WrongLock, t =>
            {
                b.Grid.anchoredPosition = home + Vector2.right * Mathf.Sin(t * 40f) * 10f * (1 - t);
                flash.color = Palette.Red.WithAlpha(0.8f * (1 - t));
            });
            b.Grid.anchoredPosition = home;
            Destroy(flash.gameObject);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                boards[p].CursorFrame.gameObject.SetActive(false);
                if (boards[p].Tiles != null && boards[p].Odd < boards[p].Tiles.Length)
                    StartCoroutine(Tween.Pop(boards[p].Tiles[boards[p].Odd].transform, 1.5f, 0.4f)); // reveal the answer
                Stamp(boards[p].Panel, p == WinnerIndex, -70);
            }
        }

        class TileClick : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnClick;
            public void OnPointerDown(PointerEventData e) => OnClick?.Invoke();
        }

        // ---------- Punishment: paint splashes turn the loser every color — "which one is different?" ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            for (int i = 0; i < 7; i++)
            {
                var color = Color.HSVToRGB(i / 7f, 0.75f, 0.95f);
                var from = new Vector2(i % 2 == 0 ? -1100f : 1100f, Random.Range(0f, 350f));
                var blob = UIKit.Img("Paint", ctx.Stage, color, UIKit.Circle).rectTransform;
                blob.Place(from, new Vector2(70, 70));
                var target = feet + new Vector2(Random.Range(-40f, 40f), Random.Range(80f, 170f));
                yield return Tween.Run(0.22f, t => blob.anchoredPosition = Vector2.Lerp(from, target, t) + Vector2.up * (4f * 140f * t * (1 - t)));
                loser.Pulse(new Vector3(1.15f, 0.85f, 1f), 0.15f); // (Pulse resets colors, so tint after it)
                loser.Tint(color);
                loser.SetFace(CharacterView.Face.Hurt);
                ctx.Start(Splat(ctx, blob, color));
                ctx.Start(PunishFx.Popup(ctx.Stage, "철퍽!", target + new Vector2(0, 90), color, 44));
            }

            ctx.Start(PunishFx.Popup(ctx.Stage, "무슨 색이게?", feet + new Vector2(0, 400), Palette.Gold, 64));
            yield return Tween.Run(1.2f, t => loser.Tint(Color.HSVToRGB(Mathf.Repeat(t * 3f, 1f), 0.75f, 0.95f)));
            loser.Tint(null);
        }

        static IEnumerator Splat(PunishContext ctx, RectTransform blob, Color color)
        {
            var img = blob.GetComponent<Image>();
            yield return Tween.Run(0.25f, t =>
            {
                blob.sizeDelta = new Vector2(Mathf.Lerp(70, 200, t), Mathf.Lerp(70, 40, t));
                img.color = color.WithAlpha(1 - t);
            });
            Destroy(blob.gameObject);
        }
    }
}
