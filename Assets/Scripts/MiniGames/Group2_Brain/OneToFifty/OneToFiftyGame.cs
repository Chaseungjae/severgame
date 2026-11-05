using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// 1 to 50 (arealme.com/1-to-50): 5x5 grid of 1~25. Pressing the next number replaces it with one of 26~50.
    /// First player to press 50 wins. Both players get the same layout for fairness.
    /// </summary>
    public class OneToFiftyGame : MiniGameBase
    {
        public override string Title => "1 to 50";
        public override string Rules => "1부터 50까지 순서대로 최대한 빨리 누르세요!\n1~25를 누르면 그 자리에 26~50이 나타납니다.\n먼저 50을 누르는 사람이 승리!  (틀리면 잠깐 멈춤)";

        const int Grid = 5;
        const int Count = Grid * Grid;
        const float Cell = 112f, Gap = 10f;
        const float WrongLock = 0.35f;

        static readonly Color CellLow = Palette.Hex("#F2F4FA");
        static readonly Color CellEmpty = new Color(1, 1, 1, 0.05f);

        class Board
        {
            public int Player;
            public RectTransform Panel;
            public readonly int[] Values = new int[Count];
            public readonly Image[] Cells = new Image[Count];
            public readonly Text[] Labels = new Text[Count];
            public Queue<int> Upper;
            public int Next = 1;
            public int Cursor = 12;
            public RectTransform CursorFrame;
            public Text NextText, TimeText;
            public RectTransform Progress;
            public float LockUntil, CpuTimer, Elapsed;
            public bool Done;
        }

        readonly Board[] boards = new Board[2];

        protected override void Build()
        {
            UIKit.Img("Bg", Root, Palette.Bg).rectTransform.Stretch();

            // Same shuffled layout for both players (and for both PCs online, via the shared seed)
            for (int p = 0; p < 2; p++) boards[p] = BuildBoard(p, new System.Random(Seed));
        }

        Board BuildBoard(int p, System.Random rng)
        {
            var player = Players[p];
            var b = new Board { Player = p };

            b.Panel = UIKit.Img($"Board{p + 1}", Root, Palette.Panel, UIKit.Rounded).rectTransform;
            b.Panel.Place(new Vector2(p == 0 ? -450 : 450, -70), new Vector2(660, 820));

            var header = UIKit.Label("Header", b.Panel, player.DisplayName, 34, player.Color);
            header.rectTransform.Place(new Vector2(0, 360), new Vector2(600, 50));

            b.NextText = UIKit.Label("Next", b.Panel, "", 40, Color.white, TextAnchor.MiddleLeft);
            b.NextText.rectTransform.Place(new Vector2(-300, 298), new Vector2(300, 56), pivot: new Vector2(0, 0.5f));

            b.TimeText = UIKit.Label("Time", b.Panel, "0.00s", 34, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
            b.TimeText.rectTransform.Place(new Vector2(300, 298), new Vector2(300, 56), pivot: new Vector2(1, 0.5f));

            var progBg = UIKit.Img("ProgressBg", b.Panel, new Color(0, 0, 0, 0.35f), UIKit.Rounded).rectTransform;
            progBg.Place(new Vector2(0, 250), new Vector2(600, 16));
            b.Progress = UIKit.Img("Progress", progBg, player.Color, UIKit.Rounded).rectTransform;

            var grid = UIKit.Rect("Grid", b.Panel);
            grid.Place(new Vector2(0, -60), new Vector2(600, 600));

            b.CursorFrame = UIKit.Img("Cursor", grid, player.Color, UIKit.Rounded).rectTransform;
            b.CursorFrame.sizeDelta = new Vector2(Cell + 16, Cell + 16);

            // Fisher-Yates with the shared rng
            var lower = new List<int>();
            for (int i = 1; i <= 25; i++) lower.Add(i);
            var upper = new List<int>();
            for (int i = 26; i <= 50; i++) upper.Add(i);
            Shuffle(lower, rng);
            Shuffle(upper, rng);
            b.Upper = new Queue<int>(upper);

            for (int i = 0; i < Count; i++)
            {
                b.Values[i] = lower[i];
                var cell = UIKit.Img($"Cell{i}", grid, CellLow, UIKit.Rounded);
                cell.rectTransform.anchoredPosition = CellPos(i);
                cell.rectTransform.sizeDelta = new Vector2(Cell, Cell);
                cell.raycastTarget = true;
                int index = i;
                cell.gameObject.AddComponent<CellClick>().OnClick = () => OnCellClicked(b, index);
                b.Cells[i] = cell;
                b.Labels[i] = UIKit.Label("Num", cell.transform, "", 48, Palette.Ink);
                b.Labels[i].rectTransform.Stretch();
                RefreshCell(b, i);
            }

            UpdateCursor(b);
            UpdateHeader(b);
            return b;
        }

        static void Shuffle(List<int> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static Vector2 CellPos(int i)
        {
            int r = i / Grid, c = i % Grid;
            float step = Cell + Gap;
            float origin = -(Grid - 1) * step / 2f;
            return new Vector2(origin + c * step, -origin - r * step);
        }

        void RefreshCell(Board b, int i)
        {
            int v = b.Values[i];
            var color = Players[b.Player].Color;
            if (v == 0)
            {
                b.Cells[i].color = CellEmpty;
                b.Labels[i].text = "";
            }
            else if (v <= 25)
            {
                b.Cells[i].color = CellLow;
                b.Labels[i].color = Palette.Ink;
                b.Labels[i].text = v.ToString();
            }
            else
            {
                b.Cells[i].color = Color.Lerp(color, Color.white, 0.12f);
                b.Labels[i].color = Color.white;
                b.Labels[i].text = v.ToString();
            }
        }

        void UpdateCursor(Board b) => b.CursorFrame.anchoredPosition = CellPos(b.Cursor);

        void UpdateHeader(Board b)
        {
            b.NextText.text = b.Done ? "완료!" : $"NEXT  {b.Next}";
            float ratio = (b.Next - 1) / 50f;
            b.Progress.anchorMin = Vector2.zero;
            b.Progress.anchorMax = new Vector2(ratio, 1);
            b.Progress.offsetMin = b.Progress.offsetMax = Vector2.zero;
            b.Progress.gameObject.SetActive(ratio > 0.001f);
        }

        void Update()
        {
            if (!Running) return;
            foreach (var b in boards)
            {
                if (b.Done) continue;
                b.Elapsed += Time.deltaTime;
                b.TimeText.text = $"{b.Elapsed:0.00}s";

                if (IsCpuHere(b.Player)) UpdateCpu(b);
                else if (IsHumanHere(b.Player)) UpdateKeys(b);
            }
        }

        void UpdateKeys(Board b)
        {
            int p = b.Player;
            int r = b.Cursor / Grid, c = b.Cursor % Grid;
            if (InLeft(p)) c = (c + Grid - 1) % Grid;
            if (InRight(p)) c = (c + 1) % Grid;
            if (InUp(p)) r = (r + Grid - 1) % Grid;
            if (InDown(p)) r = (r + 1) % Grid;
            int next = r * Grid + c;
            if (next != b.Cursor)
            {
                b.Cursor = next;
                UpdateCursor(b);
            }
            if (InConfirm(p)) TryPress(b, b.Cursor);
        }

        void UpdateCpu(Board b)
        {
            b.CpuTimer -= Time.deltaTime;
            if (b.CpuTimer > 0) return;
            b.CpuTimer = Random.Range(0.5f, 0.9f);
            int target = System.Array.IndexOf(b.Values, b.Next);
            if (target < 0) return;
            b.Cursor = target;
            UpdateCursor(b);
            TryPress(b, target);
        }

        void OnCellClicked(Board b, int index)
        {
            if (!Running || !IsHumanHere(b.Player)) return;
            b.Cursor = index;
            UpdateCursor(b);
            TryPress(b, index);
        }

        const int ActPress = 1, ActWrong = 2;

        /// <summary>Local player's press: validated here, then broadcast as an action.</summary>
        void TryPress(Board b, int i)
        {
            if (!Running || b.Done || Time.time < b.LockUntil) return;
            int v = b.Values[i];
            if (v == 0) return;
            Emit(v == b.Next ? ActPress : ActWrong, b.Player, i);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1 || act.A < 0 || act.A >= Count) return;
            var b = boards[act.Player];
            int i = act.A;

            if (act.Kind == ActWrong)
            {
                b.LockUntil = Time.time + WrongLock;
                StartCoroutine(WrongFlash(b, i));
                return;
            }
            if (act.Kind != ActPress || b.Done || b.Values[i] != b.Next) return;

            b.Cursor = i;
            UpdateCursor(b);
            b.Values[i] = b.Values[i] <= 25 ? b.Upper.Dequeue() : 0;
            b.Next++;
            RefreshCell(b, i);
            StartCoroutine(Tween.Pop(b.Cells[i].transform, 0.8f, 0.18f));

            if (b.Next > 50)
            {
                b.Done = true;
                DeclareWinner(b.Player); // authority only; first finisher it sees wins
            }
            UpdateHeader(b);
        }

        IEnumerator WrongFlash(Board b, int i)
        {
            var cell = b.Cells[i];
            var home = CellPos(i);
            cell.color = Palette.Red;
            yield return Tween.Run(WrongLock, t => cell.rectTransform.anchoredPosition = home + Vector2.right * Mathf.Sin(t * 40f) * 8f * (1 - t));
            cell.rectTransform.anchoredPosition = home;
            RefreshCell(b, i);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                var b = boards[p];
                b.CursorFrame.gameObject.SetActive(false);
                b.Done = true;
                Stamp(b.Panel, p == WinnerIndex, -60);
            }
        }

        // ---------- Punishment: number tiles rain on the loser, then a giant "50" crushes them ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            for (int n = 1; n <= 12; n++)
            {
                ctx.Start(DropTile(ctx, n));
                yield return new WaitForSeconds(0.11f);
            }
            yield return new WaitForSeconds(0.55f);

            var winnerColor = Players[WinnerIndex].Color;
            var giant = UIKit.Img("Giant50", ctx.Stage, winnerColor, UIKit.Rounded);
            var label = UIKit.Label("Num", giant.transform, "50", 150, Color.white);
            label.rectTransform.Stretch();
            UIKit.Shadow(label, 6f, 0.4f);
            yield return PunishFx.Crush(ctx, giant.rectTransform, new Vector2(300, 300));
        }

        IEnumerator DropTile(PunishContext ctx, int number)
        {
            var tile = UIKit.Img("Tile", ctx.Stage, CellLow, UIKit.Rounded);
            var label = UIKit.Label("Num", tile.transform, number.ToString(), 44, Palette.Ink);
            label.rectTransform.Stretch();
            var rt = tile.rectTransform;
            float x = ctx.LoserFeet.x + Random.Range(-70f, 70f);
            float headY = ctx.LoserHeadY - 30f;
            rt.Place(new Vector2(x, 640), new Vector2(92, 92));
            rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-25f, 25f));

            yield return Tween.Run(0.3f, t => rt.anchoredPosition = new Vector2(x, Mathf.Lerp(640, headY, t)), Ease.InQuad);

            ctx.Loser.SetFace(CharacterView.Face.Hurt);
            ctx.Loser.Hit();

            // bounce off to the side
            var vel = new Vector2((Random.value < 0.5f ? -1 : 1) * Random.Range(250f, 500f), Random.Range(500f, 800f));
            float spin = Random.Range(-600f, 600f);
            float life = 0.8f;
            while (life > 0)
            {
                float dt = Time.deltaTime;
                vel.y -= 2200f * dt;
                rt.anchoredPosition += vel * dt;
                rt.Rotate(0, 0, spin * dt);
                life -= dt;
                float a = Mathf.Clamp01(life / 0.4f);
                tile.color = CellLow.WithAlpha(a);
                label.color = Palette.Ink.WithAlpha(a);
                yield return null;
            }
            Destroy(tile.gameObject);
        }

        class CellClick : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnClick;
            public void OnPointerDown(PointerEventData e) => OnClick?.Invoke();
        }
    }
}
