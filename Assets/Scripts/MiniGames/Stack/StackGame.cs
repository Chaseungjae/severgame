using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Stack tower: a block slides in from the left/right; press to drop it. Whatever hangs over the block below is
    /// sliced off, so the tower gets narrower. Miss completely and you're out. Higher tower wins.
    /// Each PC simulates its own player; placements are broadcast so both towers are visible.
    /// </summary>
    public class StackGame : MiniGameBase
    {
        public override string Title => "블럭 쌓기";
        public override string Rules => "좌우에서 오는 블럭을 타이밍 맞게 눌러 쌓으세요!\n삐져나온 부분은 잘려나가서 블럭이 점점 좁아집니다.\n완전히 빗나가면 끝 — 더 높이 쌓은 사람이 승리!";
        public override string ControlsText => "Space 또는 마우스 클릭";

        const float BaseWidth = 300f, BlockH = 34f, Travel = 330f;
        const float PerfectTolerance = 5f;
        const float AutoDropAfter = 6f;
        const int MaxLevel = 80;
        const int ActPlace = 1, ActFail = 2;

        static readonly Color[] Tints =
        {
            Palette.Hex("#FF8A3D"), Palette.Hex("#FFC83D"), Palette.Hex("#3FB950"),
            Palette.Hex("#2EC4A6"), Palette.Hex("#4C8DFF"), Palette.Hex("#9B6BFF"), Palette.Hex("#FF7EB3"),
        };

        class Tower
        {
            public RectTransform Panel, Content, Moving;
            public Text Height, Info;
            public float Left = -BaseWidth / 2, Width = BaseWidth; // top block
            public int Level;           // blocks placed
            public bool Done;
            // moving block (local players only)
            public float MovX, Dir, Age, CpuTarget;
            public float PrevDelta;
        }

        readonly Tower[] towers = new Tower[2];
        readonly SurvivalJudge judge = new SurvivalJudge();

        protected override void Build()
        {
            Background();
            for (int p = 0; p < 2; p++)
            {
                var t = new Tower { Panel = PlayerPanel(p, 660, 820, -70) };
                towers[p] = t;
                t.Height = UIKit.Label("Height", t.Panel, "0층", 64, Color.white);
                t.Height.rectTransform.Place(new Vector2(0, 300), new Vector2(400, 80));
                t.Info = UIKit.Label("Info", t.Panel, "", 30, Palette.Gold);
                t.Info.rectTransform.Place(new Vector2(0, 245), new Vector2(500, 44));

                var view = UIKit.Img("View", t.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                view.Place(new Vector2(0, -90), new Vector2(620, 620));
                view.gameObject.AddComponent<RectMask2D>();
                t.Content = UIKit.Rect("Content", view);
                t.Content.Place(new Vector2(0, 40), new Vector2(10, 10), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

                var baseBlock = UIKit.Img("Base", t.Content, Palette.PanelLight, UIKit.Rounded);
                baseBlock.rectTransform.Place(new Vector2(0, 0), new Vector2(BaseWidth, 400), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f));

                t.Moving = UIKit.Img("Moving", t.Content, Tints[1], UIKit.Rounded).rectTransform;
                t.Moving.gameObject.SetActive(false);
            }
        }

        static float LevelY(int level) => level * BlockH; // bottom of block [level] (1-based blocks sit above the base)
        static float Speed(int level) => Mathf.Min(260f + level * 16f, 760f);
        static Color Tint(int level) => Tints[level % Tints.Length];

        protected override void OnBegin()
        {
            for (int p = 0; p < 2; p++)
                if (IsLocal(p)) SpawnMoving(towers[p]);
        }

        void SpawnMoving(Tower t)
        {
            int next = t.Level + 1;
            t.Dir = next % 2 == 1 ? 1f : -1f; // alternate: from the left, then from the right
            t.MovX = -t.Dir * Travel - t.Width / 2f;
            t.Age = 0f;
            t.CpuTarget = t.Left + Random.Range(-1f, 1f) * (4f + next * 1.4f);
            t.PrevDelta = float.NaN;
            t.Moving.gameObject.SetActive(true);
            t.Moving.GetComponent<Image>().color = Tint(next);
            t.Moving.Place(new Vector2(t.MovX + t.Width / 2f, LevelY(t.Level)), new Vector2(t.Width, BlockH - 2), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            t.Moving.SetAsLastSibling();
        }

        void Update()
        {
            for (int p = 0; p < 2; p++)
            {
                var t = towers[p];
                // camera keeps the top of the tower in view
                float targetY = 40f - Mathf.Max(0f, LevelY(t.Level) - 330f);
                var cp = t.Content.anchoredPosition;
                t.Content.anchoredPosition = new Vector2(cp.x, Mathf.Lerp(cp.y, targetY, 1f - Mathf.Exp(-8f * Time.deltaTime)));

                if (!Running || !IsLocal(p) || t.Done) continue;

                // ping-pong the moving block
                t.Age += Time.deltaTime;
                t.MovX += t.Dir * Speed(t.Level + 1) * Time.deltaTime;
                float min = -Travel - t.Width / 2f, max = Travel - t.Width / 2f;
                if (t.MovX > max) { t.MovX = max; t.Dir = -1f; }
                if (t.MovX < min) { t.MovX = min; t.Dir = 1f; }
                t.Moving.anchoredPosition = new Vector2(t.MovX + t.Width / 2f, LevelY(t.Level));

                bool drop;
                if (IsCpuHere(p))
                {
                    float delta = t.MovX - t.CpuTarget;
                    drop = !float.IsNaN(t.PrevDelta) && Mathf.Sign(delta) != Mathf.Sign(t.PrevDelta);
                    t.PrevDelta = delta;
                }
                else drop = InConfirm(p) || InClick(p);

                if (drop || t.Age > AutoDropAfter) Drop(p);
            }
        }

        /// <summary>Local player drops the moving block: compute the slice and broadcast.</summary>
        void Drop(int p)
        {
            var t = towers[p];
            t.Moving.gameObject.SetActive(false);
            float l = t.MovX, r = t.MovX + t.Width;
            if (Mathf.Abs(l - t.Left) <= PerfectTolerance) { l = t.Left; r = t.Left + t.Width; } // perfect: snap

            float nl = Mathf.Max(l, t.Left), nr = Mathf.Min(r, t.Left + t.Width);
            if (nr - nl <= 1f)
            {
                StartCoroutine(Falling(t, l, t.Width, Tint(t.Level + 1), t.Dir));
                Emit(ActFail, p, t.Level);
                return;
            }

            // sliced overhang falls away (local visual)
            if (l < nl) StartCoroutine(Falling(t, l, nl - l, Tint(t.Level + 1), -1f));
            if (r > nr) StartCoroutine(Falling(t, nr, r - nr, Tint(t.Level + 1), 1f));

            int level = t.Level + 1;
            Emit(ActPlace, p, level * 100000 + Mathf.RoundToInt(nl + 50000f), nr - nl);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var t = towers[p];

            if (act.Kind == ActPlace)
            {
                int level = act.A / 100000;
                if (level != t.Level + 1 || t.Done) return;
                float left = act.A % 100000 - 50000f;
                bool perfect = Mathf.Abs(act.F - t.Width) < 0.5f;

                t.Level = level;
                t.Left = left;
                t.Width = act.F;
                var block = UIKit.Img($"B{level}", t.Content, Tint(level), UIKit.Rounded);
                block.rectTransform.Place(new Vector2(left + act.F / 2f, LevelY(level - 1)), new Vector2(act.F, BlockH - 2), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
                StartCoroutine(Tween.Pop(block.transform, 1.12f, 0.12f));
                t.Height.text = $"{level}층";
                t.Info.text = perfect ? "PERFECT!" : "";
                if (perfect)
                {
                    StartCoroutine(Tween.Pop(t.Info.transform, 1.5f, 0.2f));
                    Juice.Ring(t.Content, new Vector2(left + act.F / 2f, LevelY(level - 1) + BlockH / 2f), Color.white, act.F * 1.4f, 0.3f);
                }
                Juice.Burst(t.Content, new Vector2(left + act.F / 2f, LevelY(level - 1)), Juice.Style.Dust, perfect ? 4 : 6, null, 0.6f);

                judge.Score[p] = level;
                judge.TieBreak[p] = -act.F; // wider final block wins a tie
                if (IsLocal(p) && level < MaxLevel) SpawnMoving(t);
                if (level >= MaxLevel) { t.Done = true; judge.Out[p] = true; }
            }
            else if (act.Kind == ActFail)
            {
                if (t.Done) return;
                t.Done = true;
                t.Moving.gameObject.SetActive(false);
                t.Info.text = "실패!";
                t.Info.color = Palette.Red;
                judge.Out[p] = true;
            }

            if (IsAuthority)
            {
                int w = judge.Check();
                if (w >= 0) DeclareWinner(w);
            }
        }

        IEnumerator Falling(Tower t, float left, float width, Color color, float dir)
        {
            var piece = UIKit.Img("Cut", t.Content, color, UIKit.Rounded);
            var rt = piece.rectTransform;
            var start = new Vector2(left + width / 2f, LevelY(t.Level));
            rt.Place(start, new Vector2(width, BlockH - 2), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            var vel = new Vector2(dir * 120f, 60f);
            float life = 1.2f, rot = 0f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1600f * dt;
                rt.anchoredPosition += vel * dt;
                rot += dir * -160f * dt;
                rt.localRotation = Quaternion.Euler(0, 0, rot);
                life -= dt;
                piece.color = color.WithAlpha(Mathf.Clamp01(life / 0.4f));
                yield return null;
            }
            Destroy(piece.gameObject);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                towers[p].Moving.gameObject.SetActive(false);
                Stamp(towers[p].Panel, p == WinnerIndex, -60);
            }
        }

        // ---------- Punishment: blocks stack up on the loser's head, then the tower topples ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var slabs = new List<RectTransform>();
            float squash = 1f;
            for (int i = 0; i < 9; i++)
            {
                float w = 260f - i * 12f;
                var slab = UIKit.Img("Slab", ctx.Stage, Tint(i + 1), UIKit.Rounded).rectTransform;
                float from = i % 2 == 0 ? -1200f : 1200f;
                squash = Mathf.Max(0.45f, squash - 0.06f);
                float top = feet.y + loser.Size * squash + i * 40f;
                slab.Place(new Vector2(feet.x + from, top), new Vector2(w, 38), pivot: new Vector2(0.5f, 0f));
                slabs.Add(slab);
                float s0 = squash;
                yield return Tween.Run(0.14f, t => slab.anchoredPosition = new Vector2(Mathf.Lerp(feet.x + from, feet.x + Random.Range(-6f, 6f), t), top), Ease.OutQuad);
                loser.SetFace(CharacterView.Face.Hurt);
                loser.BaseScale = new Vector3(2f - s0, s0, 1f);
                // re-seat the slabs on the squashed head
                for (int k = 0; k < slabs.Count; k++)
                    slabs[k].anchoredPosition = new Vector2(slabs[k].anchoredPosition.x, feet.y + loser.Size * s0 + k * 40f);
                ctx.Start(ctx.Shake(5f, 0.08f));
            }
            ctx.Start(PunishFx.Popup(ctx.Stage, "무거워...", feet + new Vector2(0, 520), Palette.Gold, 56));
            yield return new WaitForSeconds(0.6f);

            // topple
            ctx.Start(PunishFx.Popup(ctx.Stage, "와르르!", feet + new Vector2(0, 300), Palette.Red, 80));
            ctx.Start(ctx.Shake(18f, 0.4f));
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            for (int k = 0; k < slabs.Count; k++) ctx.Start(Topple(slabs[k], k));
            yield return new WaitForSeconds(1.0f);
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }

        static IEnumerator Topple(RectTransform rt, int k)
        {
            var img = rt.GetComponent<Image>();
            var vel = new Vector2((k % 2 == 0 ? 1 : -1) * Random.Range(200f, 500f), Random.Range(100f, 400f));
            float spin = Random.Range(-400f, 400f), life = 1.0f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1800f * dt;
                rt.anchoredPosition += vel * dt;
                rt.Rotate(0, 0, spin * dt);
                life -= dt;
                img.color = img.color.WithAlpha(Mathf.Clamp01(life / 0.4f));
                yield return null;
            }
            Destroy(rt.gameObject);
        }
    }
}
