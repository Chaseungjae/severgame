using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Arrow sequence race: enter the shown arrow combos in order. A wrong key resets the current combo and
    /// stuns briefly. Combos get longer. First to clear them all wins. Both players get the same combos.
    /// </summary>
    public class ArrowGame : MiniGameBase
    {
        public override string Title => "방향키 입력 대결";
        public override string Rules => "화면에 나온 방향키를 순서대로 입력하세요!\n틀리면 그 줄은 처음부터 다시 (잠깐 멈춤)\n7줄을 먼저 끝내는 사람이 승리 · 뒤로 갈수록 길어집니다";
        public override string ControlsText => "P1   W A S D\nP2   ↑ ← ↓ →";
        public override string OnlineControlsText => "W A S D 또는 방향키";

        static readonly int[] Lengths = { 4, 5, 5, 6, 7, 8, 9 };
        static readonly string[] Glyph = { "↑", "→", "↓", "←" };
        const float Stun = 0.4f;
        const float Cell = 64f, Gap = 8f;
        const int ActKey = 1; // player: A = combo * 100 + position (position 0 after a mistake), F = 1 if mistake

        int[][] combos;

        class Lane
        {
            public RectTransform Panel, Row;
            public Image[] Cells;
            public Text[] Labels;
            public Text Progress;
            public int Combo, Pos;
            public float StunUntil, CpuTimer;
            public bool Done;
            public int Player;
        }

        readonly Lane[] lanes = new Lane[2];

        protected override void Build()
        {
            Background();
            var rng = new System.Random(Seed);
            combos = new int[Lengths.Length][];
            for (int c = 0; c < Lengths.Length; c++)
            {
                combos[c] = new int[Lengths[c]];
                for (int i = 0; i < Lengths[c]; i++) combos[c][i] = rng.Next(4);
            }

            for (int p = 0; p < 2; p++)
            {
                var lane = new Lane { Panel = PlayerPanel(p, 860, 560, -40), Player = p };
                lanes[p] = lane;
                lane.Progress = UIKit.Label("Progress", lane.Panel, "", 52, Color.white);
                lane.Progress.rectTransform.Place(new Vector2(0, 170), new Vector2(500, 70));
                lane.Row = UIKit.Rect("Row", lane.Panel);
                lane.Row.Place(new Vector2(0, 20), new Vector2(800, 90));
                UIKit.Label("Hint", lane.Panel, Online ? "W A S D / 방향키" : p == 0 ? "W A S D" : "방향키", 28, Palette.Muted, style: FontStyle.Normal)
                    .rectTransform.Place(new Vector2(0, -130), new Vector2(400, 40));
                ShowCombo(lane);
            }
        }

        void ShowCombo(Lane lane)
        {
            foreach (Transform c in lane.Row) Destroy(c.gameObject);
            if (lane.Combo >= combos.Length)
            {
                lane.Progress.text = "완료!";
                return;
            }
            var combo = combos[lane.Combo];
            lane.Cells = new Image[combo.Length];
            lane.Labels = new Text[combo.Length];
            float width = combo.Length * Cell + (combo.Length - 1) * Gap;
            for (int i = 0; i < combo.Length; i++)
            {
                var cell = UIKit.Img("Cell", lane.Row, Palette.PanelLight, UIKit.Rounded);
                cell.rectTransform.Place(new Vector2(-width / 2 + Cell / 2 + i * (Cell + Gap), 0), new Vector2(Cell, Cell));
                var label = UIKit.Label("Arrow", cell.transform, Glyph[combo[i]], 40, Color.white);
                label.rectTransform.Stretch();
                lane.Cells[i] = cell;
                lane.Labels[i] = label;
            }
            lane.Progress.text = $"{lane.Combo + 1} / {combos.Length} 줄";
            Paint(lane);
        }

        /// <summary>Entered arrows light up in the player's color.</summary>
        void Paint(Lane lane)
        {
            if (lane.Cells == null) return;
            for (int i = 0; i < lane.Cells.Length; i++)
                if (lane.Cells[i] != null)
                    lane.Cells[i].color = i < lane.Pos ? Players[lane.Player].Color : Palette.PanelLight;
        }

        int ReadKey(int p)
        {
            if (InUp(p)) return 0;
            if (InRight(p)) return 1;
            if (InDown(p)) return 2;
            if (InLeft(p)) return 3;
            return -1;
        }

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var lane = lanes[p];
                if (!IsLocal(p) || lane.Done || Time.time < lane.StunUntil) continue;

                int key;
                if (IsCpuHere(p))
                {
                    lane.CpuTimer -= Time.deltaTime;
                    if (lane.CpuTimer > 0f) continue;
                    lane.CpuTimer = Random.Range(0.17f, 0.3f);
                    key = combos[lane.Combo][lane.Pos];
                    if (Random.value < 0.04f) key = (key + 1 + Random.Range(0, 3)) % 4;
                }
                else key = ReadKey(p);
                if (key < 0) continue;

                bool right = combos[lane.Combo][lane.Pos] == key;
                int combo = lane.Combo, pos = right ? lane.Pos + 1 : 0;
                if (right && pos >= combos[combo].Length) { combo++; pos = 0; }
                Emit(ActKey, p, combo * 100 + pos, right ? 0f : 1f);
            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Kind != ActKey || act.Player < 0 || act.Player > 1) return;
            var lane = lanes[act.Player];
            if (lane.Done) return;
            int combo = act.A / 100, pos = act.A % 100;
            bool mistake = act.F > 0.5f;

            if (combo != lane.Combo)
            {
                lane.Combo = combo;
                lane.Pos = 0;
                if (combo >= combos.Length)
                {
                    lane.Done = true;
                    ShowCombo(lane);
                    StartCoroutine(Tween.Pop(lane.Progress.transform, 1.5f, 0.25f));
                    DeclareWinner(act.Player);
                    return;
                }
                ShowCombo(lane);
                StartCoroutine(Tween.Pop(lane.Row, 1.1f, 0.15f));
                return;
            }

            lane.Pos = pos;
            Paint(lane);
            if (mistake)
            {
                lane.StunUntil = Time.time + Stun;
                StartCoroutine(Mistake(lane));
            }
            else if (pos > 0) StartCoroutine(Tween.Pop(lane.Cells[pos - 1].transform, 1.3f, 0.12f));
        }

        IEnumerator Mistake(Lane lane)
        {
            foreach (var c in lane.Cells) c.color = Palette.Red;
            var home = lane.Row.anchoredPosition;
            yield return Tween.Run(Stun, t => lane.Row.anchoredPosition = home + Vector2.right * Mathf.Sin(t * 40f) * 12f * (1 - t));
            lane.Row.anchoredPosition = home;
            Paint(lane);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, 0);
        }

        // ---------- Punishment: arrow keys fly in from every side, then a giant ↓ slams down ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var feet = ctx.LoserFeet;
            for (int i = 0; i < 12; i++)
            {
                int dir = i % 4; // 0 ↑ from below, 1 → from left, 2 ↓ from above, 3 ← from right
                ctx.Start(FlyArrow(ctx, dir));
                yield return new WaitForSeconds(0.09f);
            }
            yield return new WaitForSeconds(0.5f);

            var key = UIKit.Img("GiantDown", ctx.Stage, Palette.Hex("#E9ECF5"), UIKit.Rounded);
            UIKit.Label("Arrow", key.transform, "↓", 200, Palette.Red).rectTransform.Stretch();
            yield return PunishFx.Crush(ctx, key.rectTransform, new Vector2(260, 260));
        }

        static IEnumerator FlyArrow(PunishContext ctx, int dir)
        {
            var target = ctx.LoserFeet + new Vector2(Random.Range(-40f, 40f), Random.Range(70f, 170f));
            Vector2 from = dir switch
            {
                0 => new Vector2(target.x, -700f),
                1 => new Vector2(-1100f, target.y),
                2 => new Vector2(target.x, 700f),
                _ => new Vector2(1100f, target.y),
            };
            var cap = UIKit.Img("Arrow", ctx.Stage, Palette.Hex("#E9ECF5"), UIKit.Rounded);
            var label = UIKit.Label("A", cap.transform, Glyph[dir], 44, Palette.Ink);
            label.rectTransform.Stretch();
            var rt = cap.rectTransform;
            rt.Place(from, new Vector2(72, 72));
            yield return Tween.Run(0.22f, t => rt.anchoredPosition = Vector2.Lerp(from, target, t), Ease.InQuad);
            ctx.Loser.SetFace(CharacterView.Face.Hurt);
            ctx.Loser.Hit();
            ctx.Start(PunishFx.Popup(ctx.Stage, "콕!", target + new Vector2(0, 80), Palette.Gold, 44));
            var back = (from - target).normalized;
            var vel = back * 500f + Vector2.up * 400f;
            float life = 0.6f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 2000f * dt;
                rt.anchoredPosition += vel * dt;
                rt.Rotate(0, 0, 500f * dt);
                life -= dt;
                float a = Mathf.Clamp01(life / 0.3f);
                cap.color = Palette.Hex("#E9ECF5").WithAlpha(a);
                label.color = Palette.Ink.WithAlpha(a);
                yield return null;
            }
            Destroy(cap.gameObject);
        }
    }
}
