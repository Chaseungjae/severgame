using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// 100-floor challenge (infinite-stairs style). The staircase zig-zags left/right. "Climb" steps forward in the
    /// direction you're facing, "Turn" flips your direction and steps. Wrong button = misstep (short stun).
    /// First to floor 100 wins. Both players get the same staircase (shared seed).
    /// </summary>
    public class StairsGame : MiniGameBase
    {
        public override string Title => "100층 도전";
        public override string Rules => "계단을 올라 100층에 먼저 도착하세요!\n오르기: 보고 있는 방향으로 한 칸 · 방향전환: 방향을 바꾸며 한 칸\n계단 방향과 다르게 누르면 헛디딤 (잠깐 멈춤)";
        public override string ControlsText => "D 또는 → 오르기 · A 또는 ← 방향전환";

        const int Floors = 100;
        const float StepW = 84f, StepH = 56f;
        const float Stun = 0.5f;
        const int ActStep = 1, ActMiss = 2;

        class Tower
        {
            public RectTransform Panel, Content;
            public CharacterView View;
            public Text Floor, Arrow;
            public RectTransform Progress;
            public int Step;
            public int Face = 1;
            public float StunUntil, CpuTimer;
            public Coroutine Hop;
        }

        int[] dirs;   // dirs[i] = direction (+1/-1) from step i-1 to step i
        int[] xs;     // xs[i] = column of step i
        readonly Tower[] towers = new Tower[2];

        protected override void Build()
        {
            Background();

            var rng = new System.Random(Seed);
            dirs = new int[Floors + 1];
            xs = new int[Floors + 1];
            dirs[1] = 1;
            for (int i = 1; i <= Floors; i++)
            {
                if (i > 1) dirs[i] = rng.NextDouble() < 0.35 ? -dirs[i - 1] : dirs[i - 1];
                xs[i] = xs[i - 1] + dirs[i];
            }

            for (int p = 0; p < 2; p++) towers[p] = BuildTower(p);
        }

        static Vector2 StepPos(int x, int i) => new Vector2(x * StepW, i * StepH);

        Tower BuildTower(int p)
        {
            var t = new Tower { Panel = PlayerPanel(p, 660, 820, -70) };

            t.Floor = UIKit.Label("Floor", t.Panel, "0층", 64, Color.white);
            t.Floor.rectTransform.Place(new Vector2(0, 300), new Vector2(400, 80));
            var bg = UIKit.Img("ProgressBg", t.Panel, new Color(0, 0, 0, 0.35f), UIKit.Rounded).rectTransform;
            bg.Place(new Vector2(0, 245), new Vector2(600, 16));
            t.Progress = UIKit.Img("Progress", bg, Players[p].Color, UIKit.Rounded).rectTransform;

            var view = UIKit.Img("View", t.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
            view.Place(new Vector2(0, -90), new Vector2(600, 600));
            view.gameObject.AddComponent<RectMask2D>();
            t.Content = UIKit.Rect("Content", view);
            t.Content.Place(Vector2.zero, new Vector2(10, 10));

            var stepColor = Color.Lerp(Players[p].Color, Color.white, 0.55f);
            UIKit.Img("Ground", t.Content, Palette.PanelLight, UIKit.Rounded).rectTransform
                .Place(new Vector2(0, 0), new Vector2(400, 30), pivot: new Vector2(0.5f, 1f));
            for (int i = 1; i <= Floors; i++)
            {
                bool goal = i == Floors;
                var block = UIKit.Img($"Step{i}", t.Content, goal ? Palette.Gold : stepColor, UIKit.Rounded);
                block.rectTransform.Place(StepPos(xs[i], i), new Vector2(StepW - 6, 22), pivot: new Vector2(0.5f, 1f));
                if (i % 10 == 0)
                {
                    var tag = UIKit.Label("Tag", t.Content, goal ? "GOAL" : $"{i}F", 20, goal ? Palette.Gold : Palette.Muted);
                    tag.rectTransform.Place(StepPos(xs[i], i) + new Vector2(0, -32), new Vector2(100, 30));
                }
            }

            t.View = CharacterView.Create(t.Content, Players[p].Character, 72);
            t.Arrow = UIKit.Label("Arrow", t.View.Rt, "▶", 30, Palette.Gold);
            t.Arrow.rectTransform.Place(new Vector2(0, 92), new Vector2(60, 40), new Vector2(0.5f, 0f));
            Apply(t, true);
            return t;
        }

        void Apply(Tower t, bool snap)
        {
            var pos = StepPos(xs[t.Step], t.Step);
            if (snap) t.View.Rt.anchoredPosition = pos;
            t.Arrow.text = t.Face > 0 ? "▶" : "◀";
            t.Floor.text = t.Step >= Floors ? "100층 도착!" : $"{t.Step}층";
            float ratio = t.Step / (float)Floors;
            t.Progress.anchorMin = Vector2.zero;
            t.Progress.anchorMax = new Vector2(ratio, 1);
            t.Progress.offsetMin = t.Progress.offsetMax = Vector2.zero;
            t.Progress.gameObject.SetActive(ratio > 0.001f);
        }

        void Update()
        {
            for (int p = 0; p < 2; p++)
            {
                var t = towers[p];
                // camera follows the climber (character sits in the lower part of the view)
                var target = -t.View.Rt.anchoredPosition + new Vector2(0, -170);
                t.Content.anchoredPosition = Vector2.Lerp(t.Content.anchoredPosition, target, 1f - Mathf.Exp(-12f * Time.deltaTime));

                if (!Running || !IsLocal(p) || t.Step >= Floors || Time.time < t.StunUntil) continue;

                if (IsCpuHere(p))
                {
                    t.CpuTimer -= Time.deltaTime;
                    if (t.CpuTimer > 0f) continue;
                    t.CpuTimer = 1f / Random.Range(4.5f, 6f);
                    bool needTurn = dirs[t.Step + 1] != t.Face;
                    if (Random.value < 0.05f) needTurn = !needTurn; // occasional slip
                    Move(p, needTurn);
                }
                else
                {
                    bool climb = InRight(p);
                    bool turn = InLeft(p);
                    if (climb) Move(p, false);
                    else if (turn) Move(p, true);
                }
            }
        }

        /// <summary>Local player's input, validated against the staircase and broadcast.</summary>
        void Move(int p, bool turn)
        {
            var t = towers[p];
            int face = turn ? -t.Face : t.Face;
            int faceBit = face > 0 ? 1 : 0;
            if (dirs[t.Step + 1] == face) Emit(ActStep, p, (t.Step + 1) * 2 + faceBit);
            else Emit(ActMiss, p, faceBit);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            var t = towers[act.Player];

            if (act.Kind == ActStep)
            {
                int step = act.A / 2;
                if (step <= t.Step || step > Floors) return;
                t.Step = step;
                t.Face = act.A % 2 == 1 ? 1 : -1;
                Apply(t, false);
                if (t.Hop != null) StopCoroutine(t.Hop);
                t.Hop = StartCoroutine(HopTo(t, StepPos(xs[step], step)));
                if (t.Step >= Floors)
                {
                    t.View.SetFace(CharacterView.Face.Happy);
                    DeclareWinner(act.Player);
                }
            }
            else if (act.Kind == ActMiss)
            {
                // a misstep keeps the old facing; flipping here made the keys feel reversed after a wrong turn
                t.StunUntil = Time.time + Stun;
                Apply(t, false);
                t.View.SetFace(CharacterView.Face.Hurt);
                t.View.Hit();
                StartCoroutine(Misstep(t));
            }
        }

        IEnumerator HopTo(Tower t, Vector2 to)
        {
            var from = t.View.Rt.anchoredPosition;
            yield return Tween.Run(0.08f, k => t.View.Rt.anchoredPosition = Vector2.Lerp(from, to, k) + Vector2.up * (4f * 18f * k * (1 - k)));
        }

        IEnumerator Misstep(Tower t)
        {
            var label = UIKit.Label("Miss", t.Panel, "헛디딤!", 44, Palette.Red);
            label.rectTransform.Place(new Vector2(0, 120), new Vector2(300, 60));
            UIKit.Shadow(label, 4f);
            yield return Tween.Run(Stun, k =>
            {
                t.View.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(k * 30f) * 15f * (1 - k));
                label.color = Palette.Red.WithAlpha(1 - k);
            });
            t.View.Rt.localRotation = Quaternion.identity;
            t.View.SetFace(CharacterView.Face.Normal);
            Destroy(label.gameObject);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                towers[p].View.SetFace(p == WinnerIndex ? CharacterView.Face.Happy : CharacterView.Face.Hurt);
                Stamp(towers[p].Panel, p == WinnerIndex, -60);
            }
        }

        // ---------- Punishment: the loser is marched up a staircase and tumbles all the way down ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            float dir = feet.x >= 0 ? 1f : -1f; // build the stairs outward, away from the winner
            const int n = 6;
            const float w = 78f, h = 52f;

            var blocks = new List<Image>();
            for (int k = 1; k <= n; k++)
            {
                var b = UIKit.Img("PunishStep", ctx.Stage, Palette.Hex("#8C93B8"), UIKit.Rounded);
                b.rectTransform.Place(feet + new Vector2(dir * k * w, k * h), new Vector2(w - 4, h * k), pivot: new Vector2(0.5f, 1f));
                b.transform.SetSiblingIndex(0);
                blocks.Add(b);
                ctx.Start(Tween.Pop(b.transform, 0.3f, 0.2f));
                yield return new WaitForSeconds(0.05f);
            }

            // climb up quickly…
            for (int k = 1; k <= n; k++)
                yield return loser.HopTo(feet + new Vector2(dir * k * w, k * h), 0.09f, 1, 20f);
            ctx.Start(PunishFx.Popup(ctx.Stage, "어어...?", loser.Rt.anchoredPosition + new Vector2(0, 300), Palette.Gold, 56));
            yield return new WaitForSeconds(0.4f);

            // …and tumble all the way down
            loser.SetFace(CharacterView.Face.Hurt);
            loser.Idle = false;
            ctx.Start(PunishFx.Popup(ctx.Stage, "데굴데굴~", feet + new Vector2(dir * 200f, 420f), Palette.Red, 64));
            float angle = 0f;
            for (int k = n - 1; k >= 0; k--)
            {
                var from = loser.Rt.anchoredPosition;
                var to = feet + new Vector2(dir * k * w, k * h);
                float a0 = angle;
                angle -= dir * 120f;
                float a1 = angle;
                yield return Tween.Run(0.12f, t =>
                {
                    loser.Rt.anchoredPosition = Vector2.Lerp(from, to, t) + Vector2.up * (4f * 30f * t * (1 - t));
                    loser.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(a0, a1, t));
                });
                loser.Hit();
                ctx.Start(ctx.Shake(6f, 0.08f));
            }

            loser.Rt.localRotation = Quaternion.identity;
            loser.Rt.anchoredPosition = feet;
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            ctx.Start(ctx.Shake(22f, 0.35f));
            ctx.Start(PunishFx.Popup(ctx.Stage, "쿵!", feet + new Vector2(0, 140), Palette.Red, 80));
            foreach (var b in blocks) ctx.Start(FadeOut(b));
            yield return new WaitForSeconds(0.8f);
            loser.Idle = true;
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }

        static IEnumerator FadeOut(Image img)
        {
            yield return Tween.FadeGraphic(img, 0f, 0.4f);
            Destroy(img.gameObject);
        }
    }
}
