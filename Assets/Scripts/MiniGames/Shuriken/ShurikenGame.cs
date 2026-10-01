using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// 표창 막기 (block the shuriken, dojo): shuriken fly at you from four directions. Press the matching direction while
    /// one is inside the ring to deflect it. Getting hit costs a life (3 lives); pressing at nothing stuns you briefly.
    /// It keeps speeding up. Both players face the same volley (shared seed); whoever lasts longer wins.
    /// </summary>
    public class ShurikenGame : MiniGameBase
    {
        public override string Title => "표창 막기";
        public override string Rules => "사방에서 날아오는 표창을 막아내세요!\n표창이 원 안에 들어왔을 때 그 방향키를 누르면 쳐냄 · 맞으면 목숨 -1 (3개)\n헛손질하면 잠깐 굳음 · 점점 빨라짐 · 더 오래 버틴 사람 승리";
        public override string ControlsText => "P1   W A S D\nP2   ↑ ← ↓ →";
        public override string OnlineControlsText => "W A S D 또는 방향키";

        const int Count = 250;
        const float Arena = 300f;       // spawn distance from the center
        const float ZoneOuter = 140f, ZoneInner = 50f;
        const float HitRadius = 40f;
        const float WhiffStun = 0.35f;
        const int Lives = 3;
        const int ActDeflect = 1, ActHit = 2, ActOut = 3;

        static readonly Vector2[] DirVec = { Vector2.up, Vector2.right, Vector2.down, Vector2.left }; // 0 ↑ 1 → 2 ↓ 3 ←

        static readonly Color FloorColor = Palette.Hex("#4E321D");

        float[] spawnAt, speed;
        int[] dirOf;

        class Lane
        {
            public RectTransform Panel, Field;
            public CharacterView View;
            public Text Score, Info;
            public Text[] Hearts = new Text[Lives];
            public RectTransform[] Stars;
            public bool[] Resolved;
            public float[] CpuPress;
            public int Points, LivesLeft = Lives;
            public float Elapsed, StunUntil;
            public bool Out;
        }

        readonly Lane[] lanes = new Lane[2];
        readonly SurvivalJudge judge = new SurvivalJudge();

        protected override void Build()
        {
            Background();
            var rng = new System.Random(Seed);
            spawnAt = new float[Count];
            speed = new float[Count];
            dirOf = new int[Count];
            float t = 1.0f;
            for (int i = 0; i < Count; i++)
            {
                spawnAt[i] = t;
                speed[i] = Mathf.Min(210f + i * 6f, 620f);
                dirOf[i] = rng.Next(4);
                t += Mathf.Max(0.32f, 0.95f - i * 0.018f) * (0.8f + (float)rng.NextDouble() * 0.4f);
            }

            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 760, 820, -70) };
                lanes[p] = l;
                l.Score = UIKit.Label("Score", l.Panel, "0", 64, Color.white, TextAnchor.MiddleLeft);
                l.Score.rectTransform.Place(new Vector2(-340, 300), new Vector2(300, 80), pivot: new Vector2(0, 0.5f));
                for (int h = 0; h < Lives; h++)
                {
                    var heart = UIKit.Label("Heart", l.Panel, "♥", 44, Palette.Red);
                    heart.rectTransform.Place(new Vector2(250 + h * 46, 300), new Vector2(50, 50));
                    l.Hearts[h] = heart;
                }
                l.Info = UIKit.Label("Info", l.Panel, "", 40, Palette.Gold);
                l.Info.rectTransform.Place(new Vector2(0, 245), new Vector2(500, 56));

                l.Field = UIKit.Img("Field", l.Panel, FloorColor, UIKit.Rounded).rectTransform;
                l.Field.Place(new Vector2(0, -80), new Vector2(660, 620));
                l.Field.gameObject.AddComponent<RectMask2D>();
                // dojo: wooden floorboards + a red target mat in the middle
                for (int k = 0; k < 8; k++)
                {
                    var board = UIKit.Img("Plank", l.Field, k % 2 == 0 ? Palette.Hex("#5A3A22") : Palette.Hex("#4E321D"));
                    board.rectTransform.Place(new Vector2(0, -280 + k * 80), new Vector2(660, 78));
                }
                UIKit.Img("Mat", l.Field, Palette.Hex("#8E2B2B").WithAlpha(0.55f), UIKit.Rounded).rectTransform.Place(Vector2.zero, new Vector2(330, 330));
                // lanes + deflect ring
                UIKit.Img("LaneH", l.Field, Color.white.WithAlpha(0.06f)).rectTransform.Place(Vector2.zero, new Vector2(Arena * 2, 60));
                UIKit.Img("LaneV", l.Field, Color.white.WithAlpha(0.06f)).rectTransform.Place(Vector2.zero, new Vector2(60, Arena * 2));
                UIKit.Img("Ring", l.Field, Palette.Gold.WithAlpha(0.25f), UIKit.Circle).rectTransform.Place(Vector2.zero, Vector2.one * ZoneOuter * 2);
                UIKit.Img("RingIn", l.Field, Palette.Hex("#6B2424"), UIKit.Circle).rectTransform.Place(Vector2.zero, Vector2.one * ZoneInner * 2);

                l.View = CharacterView.Create(l.Field, Players[p].Character, 90);
                l.View.Rt.anchoredPosition = new Vector2(0, -45);

                l.Stars = new RectTransform[Count];
                l.Resolved = new bool[Count];
                l.CpuPress = new float[Count];
                var cpuRng = new System.Random(Seed + 31 * (p + 1));
                for (int i = 0; i < Count; i++)
                {
                    var s = Shuriken(l.Field, 46f);
                    s.gameObject.SetActive(false);
                    l.Stars[i] = s;
                    // CPU: presses at a random depth inside the ring, sometimes too late as things speed up
                    float miss = Mathf.Lerp(0.03f, 0.35f, i / 120f);
                    float depth = Mathf.Lerp(ZoneOuter - 10f, ZoneInner - 30f, (float)cpuRng.NextDouble() * (cpuRng.NextDouble() < miss ? 1.6f : 0.85f));
                    l.CpuPress[i] = spawnAt[i] + (Arena - depth) / speed[i];
                }
            }
        }

        /// <summary>Four-pointed throwing star made of two overlapping diamonds.</summary>
        static RectTransform Shuriken(Transform parent, float size)
        {
            var root = UIKit.Rect("Shuriken", parent);
            root.sizeDelta = new Vector2(size, size);
            root.gameObject.AddComponent<CanvasGroup>();
            var steel = Palette.Hex("#D8DCE8");
            var a = UIKit.Img("A", root, steel).rectTransform;
            a.Place(Vector2.zero, new Vector2(size * 0.26f, size));
            var b = UIKit.Img("B", root, steel).rectTransform;
            b.Place(Vector2.zero, new Vector2(size, size * 0.26f));
            UIKit.Img("Hole", root, Palette.Hex("#4A5068"), UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(size * 0.22f, size * 0.22f));
            root.localRotation = Quaternion.Euler(0, 0, 45f);
            return root;
        }

        float Distance(int i, float t) => Arena - (t - spawnAt[i]) * speed[i];

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (l.Out) continue;
                l.Elapsed += Time.deltaTime;

                // draw live shuriken
                for (int i = 0; i < Count; i++)
                {
                    if (spawnAt[i] > l.Elapsed + 0.01f) break;
                    var s = l.Stars[i];
                    float d = Distance(i, l.Elapsed);
                    bool live = !l.Resolved[i] && d > -20f;
                    if (s.gameObject.activeSelf != live) s.gameObject.SetActive(live);
                    if (!live) continue;
                    s.anchoredPosition = DirVec[dirOf[i]] * d;
                    s.localRotation = Quaternion.Euler(0, 0, l.Elapsed * 720f);

                    // only the owner's PC decides hits for its own player
                    if (IsLocal(p) && d <= HitRadius) Emit(l.LivesLeft > 1 ? ActHit : ActOut, p, i, l.LivesLeft - 1);
                }

                if (!IsLocal(p) || l.Out || Time.time < l.StunUntil) continue;

                if (IsCpuHere(p))
                {
                    for (int i = 0; i < Count; i++)
                    {
                        if (spawnAt[i] > l.Elapsed) break;
                        if (l.Resolved[i] || l.Elapsed < l.CpuPress[i]) continue;
                        float d = Distance(i, l.Elapsed);
                        if (d <= ZoneOuter && d >= ZoneInner - 30f) Emit(ActDeflect, p, i);
                    }
                    continue;
                }

                int dir = -1;
                if (InUp(p)) dir = 0;
                else if (InRight(p)) dir = 1;
                else if (InDown(p)) dir = 2;
                else if (InLeft(p)) dir = 3;
                if (dir < 0) continue;

                // deflect the closest shuriken on that side that's inside the ring
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < Count; i++)
                {
                    if (spawnAt[i] > l.Elapsed) break;
                    if (l.Resolved[i] || dirOf[i] != dir) continue;
                    float d = Distance(i, l.Elapsed);
                    if (d <= ZoneOuter && d > HitRadius && d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) Emit(ActDeflect, p, best);
                else
                {
                    l.StunUntil = Time.time + WhiffStun;
                    l.Info.text = "헛손질!";
                    l.Info.color = Palette.Muted;
                }
            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1 || act.A < 0 || act.A >= Count) return;
            int p = act.Player;
            var l = lanes[p];
            if (l.Out || l.Resolved[act.A]) return;
            l.Resolved[act.A] = true;
            l.Stars[act.A].gameObject.SetActive(false);

            switch (act.Kind)
            {
                case ActDeflect:
                    l.Points++;
                    judge.Score[p] = l.Points;
                    l.Score.text = l.Points.ToString();
                    l.Info.text = "챙!";
                    l.Info.color = Palette.Gold;
                    StartCoroutine(Tween.Pop(l.Info.transform, 1.4f, 0.12f));
                    StartCoroutine(Slash(l, dirOf[act.A], Players[p].Color));
                    l.View.Pulse(new Vector3(1.12f, 0.9f, 1f), 0.12f);
                    break;
                case ActHit:
                case ActOut:
                    l.LivesLeft = act.Kind == ActOut ? 0 : act.F > 0 ? Mathf.RoundToInt(act.F) : l.LivesLeft - 1;
                    for (int h = 0; h < Lives; h++) l.Hearts[h].color = h < l.LivesLeft ? Palette.Red : Color.white.WithAlpha(0.15f);
                    l.View.Hit();
                    l.View.SetFace(CharacterView.Face.Hurt);
                    l.Info.text = "윽!";
                    l.Info.color = Palette.Red;
                    StartCoroutine(Tween.Pop(l.Info.transform, 1.4f, 0.12f));
                    if (act.Kind == ActOut)
                    {
                        l.Out = true;
                        judge.Out[p] = true;
                        l.Info.text = "탈락!";
                        foreach (var s in l.Stars) if (s.gameObject.activeSelf) s.gameObject.SetActive(false);
                    }
                    break;
            }

            if (IsAuthority)
            {
                int w = judge.Check();
                if (w >= 0) DeclareWinner(w);
            }
        }

        IEnumerator Slash(Lane l, int dir, Color color)
        {
            var slash = UIKit.Img("Slash", l.Field, color, UIKit.Rounded).rectTransform;
            var pos = DirVec[dir] * 95f;
            slash.Place(pos, new Vector2(dir % 2 == 0 ? 160 : 14, dir % 2 == 0 ? 14 : 160));
            slash.localRotation = Quaternion.Euler(0, 0, 25f);
            var img = slash.GetComponent<Image>();
            yield return Tween.Run(0.18f, t => img.color = color.WithAlpha(1 - t));
            Destroy(slash.gameObject);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, -80);
        }

        // ---------- Punishment: a volley of shuriken, then the master's single strike ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var stuck = new List<GameObject>();
            for (int i = 0; i < 10; i++)
            {
                var rt = Shuriken(ctx.Stage, 60f);
                var from = new Vector2(i % 2 == 0 ? -1100f : 1100f, Random.Range(-100f, 400f));
                var to = feet + new Vector2(Random.Range(-70f, 70f), Random.Range(40f, 190f));
                rt.anchoredPosition = from;
                stuck.Add(rt.gameObject);
                ctx.Start(Tween.Run(0.2f, t =>
                {
                    rt.anchoredPosition = Vector2.Lerp(from, to, t);
                    rt.localRotation = Quaternion.Euler(0, 0, 1080f * t);
                }));
                yield return new WaitForSeconds(0.2f);
                loser.SetFace(CharacterView.Face.Hurt);
                loser.Hit();
                if (i % 3 == 0) ctx.Start(PunishFx.Popup(ctx.Stage, "퍽!", to + new Vector2(0, 70), Palette.Gold, 44));
            }
            yield return new WaitForSeconds(0.3f);

            // the master's strike: a flash slash across the loser
            ctx.Start(PunishFx.Popup(ctx.Stage, "일격필살!", feet + new Vector2(0, 420), Palette.Red, 84));
            var slash = UIKit.Img("Strike", ctx.Stage, Color.white, UIKit.Rounded).rectTransform;
            slash.Place(feet + new Vector2(0, 110), new Vector2(10, 16));
            slash.localRotation = Quaternion.Euler(0, 0, -30f);
            var img = slash.GetComponent<Image>();
            yield return Tween.Run(0.12f, t => slash.sizeDelta = new Vector2(Mathf.Lerp(10, 620, t), 16));
            ctx.Start(ctx.Shake(24f, 0.35f));
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            foreach (var go in stuck) ctx.Start(Fall(go.GetComponent<RectTransform>()));
            yield return Tween.Run(0.4f, t => img.color = Color.white.WithAlpha(1 - t));
            Destroy(slash.gameObject);
            yield return new WaitForSeconds(0.6f);
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }

        static IEnumerator Fall(RectTransform rt)
        {
            var group = rt.GetComponent<CanvasGroup>();
            var vel = new Vector2(Random.Range(-200f, 200f), Random.Range(100f, 300f));
            float life = 0.8f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1800f * dt;
                rt.anchoredPosition += vel * dt;
                life -= dt;
                group.alpha = Mathf.Clamp01(life / 0.4f);
                yield return null;
            }
            Destroy(rt.gameObject);
        }
    }
}
