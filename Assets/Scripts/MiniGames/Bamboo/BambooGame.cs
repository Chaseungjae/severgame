using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// 죽림고수: spears fly in from all four edges of the bamboo grove, aimed at where you are standing.
    /// Move with the arrow keys to dodge. Getting hit costs a life (3 lives, short invincibility after a hit).
    /// The spears get faster and denser. Both players face the same spawn schedule; whoever survives longer wins.
    /// Each PC simulates its own player; online the opponent's position and spears are mirrored for display.
    /// </summary>
    public class BambooGame : MiniGameBase
    {
        public override string Title => "죽림고수";
        public override string Rules => "사방에서 내 쪽으로 날아오는 창을 방향키로 움직여 피하세요!\n맞으면 목숨 -1 (3개) · 맞은 직후 잠깐 무적\n점점 빨라지고 많아짐 · 더 오래 버틴 사람 승리";
        public override string ControlsText => "P1   W A S D 이동\nP2   방향키 이동";
        public override string OnlineControlsText => "방향키 또는 W A S D 이동";

        const float FieldW = 640f, FieldH = 600f;
        const float MoveSpeed = 340f;
        const float BodyRadius = 22f;
        const float SpearLength = 96f;
        const int Lives = 3;
        const float Invincible = 1.2f;
        const int Count = 320;
        const float Grace = 0.5f;
        const int ActHit = 1, ActOut = 2, ActSpawn = 3, ActPos = 4;

        // shared schedule
        float[] spawnAt, speed, along, spread;
        int[] side;

        class Spear
        {
            public RectTransform Rt;
            public Vector2 Pos, Dir;
            public float Speed;
            public bool Live;
        }

        class Lane
        {
            public RectTransform Panel, Field;
            public CharacterView View;
            public CanvasGroup ViewAlpha;
            public Text Time, Info;
            public Text[] Hearts = new Text[Lives];
            public Spear[] Spears = new Spear[Count];
            public Vector2 Pos, RemoteTarget;
            public int LivesLeft = Lives, NextSpawn;
            public float Elapsed, InvUntil, PosTimer, CpuThink;
            public Vector2 CpuMove;
            public bool Out;
            public int OutTenths;
        }

        readonly Lane[] lanes = new Lane[2];
        float graceUntil = -1f;

        protected override void Build()
        {
            Background();
            var rng = new System.Random(Seed);
            spawnAt = new float[Count];
            speed = new float[Count];
            along = new float[Count];
            spread = new float[Count];
            side = new int[Count];
            float t = 1.2f;
            for (int i = 0; i < Count; i++)
            {
                spawnAt[i] = t;
                speed[i] = Mathf.Min(250f + i * 4.5f, 700f);
                side[i] = rng.Next(4);
                along[i] = 0.08f + (float)rng.NextDouble() * 0.84f;
                spread[i] = ((float)rng.NextDouble() * 2f - 1f) * 10f; // degrees off the aim line
                // every so often two spears arrive together
                bool pair = i > 20 && i % 6 == 0;
                t += pair ? 0.05f : Mathf.Max(0.2f, 0.8f - i * 0.011f) * (0.75f + (float)rng.NextDouble() * 0.5f);
            }

            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 760, 820, -70) };
                lanes[p] = l;
                l.Time = UIKit.Label("Time", l.Panel, "0.0초", 52, Color.white, TextAnchor.MiddleLeft);
                l.Time.rectTransform.Place(new Vector2(-340, 300), new Vector2(320, 70), pivot: new Vector2(0, 0.5f));
                for (int h = 0; h < Lives; h++)
                {
                    var heart = UIKit.Label("Heart", l.Panel, "♥", 44, Palette.Red);
                    heart.rectTransform.Place(new Vector2(250 + h * 46, 300), new Vector2(50, 50));
                    l.Hearts[h] = heart;
                }
                l.Info = UIKit.Label("Info", l.Panel, "", 36, Palette.Gold);
                l.Info.rectTransform.Place(new Vector2(0, 250), new Vector2(500, 50));

                l.Field = UIKit.Img("Grove", l.Panel, Palette.Hex("#163A26"), UIKit.Rounded).rectTransform;
                l.Field.Place(new Vector2(0, -70), new Vector2(FieldW, FieldH));
                l.Field.gameObject.AddComponent<RectMask2D>();
                DrawGrove(l.Field, new System.Random(Seed + 5));

                l.View = CharacterView.Create(l.Field, Players[p].Character, 58);
                l.ViewAlpha = l.View.gameObject.AddComponent<CanvasGroup>();
                PlaceView(l);

                for (int i = 0; i < Count; i++)
                {
                    var rt = MakeSpear(l.Field, SpearLength);
                    rt.gameObject.SetActive(false);
                    l.Spears[i] = new Spear { Rt = rt };
                }
            }
        }

        static void DrawGrove(RectTransform field, System.Random rng)
        {
            for (int k = 0; k < 14; k++)
            {
                float x = -FieldW / 2 + 20 + (float)rng.NextDouble() * (FieldW - 40);
                float w = 10 + (float)rng.NextDouble() * 14;
                var stalk = UIKit.Img("Stalk", field, Palette.Hex("#2F7A4C").WithAlpha(0.35f + (float)rng.NextDouble() * 0.3f), UIKit.Rounded);
                stalk.rectTransform.Place(new Vector2(x, 0), new Vector2(w, FieldH));
                for (int n = 0; n < 5; n++) // bamboo joints
                    UIKit.Img("Node", stalk.transform, Palette.Hex("#1E5233"), UIKit.Rounded).rectTransform
                        .Place(new Vector2(0, -FieldH / 2 + 60 + n * 120 + (float)rng.NextDouble() * 30), new Vector2(w + 4, 5));
            }
        }

        static RectTransform MakeSpear(Transform parent, float length)
        {
            var root = UIKit.Rect("Spear", parent);
            root.sizeDelta = new Vector2(length, 16);
            UIKit.Img("Shaft", root, Palette.Hex("#C8A06A"), UIKit.Rounded).rectTransform.Place(new Vector2(-8, 0), new Vector2(length - 16, 6));
            var tip = UIKit.Img("Tip", root, Palette.Hex("#E3E7F0")).rectTransform;
            tip.Place(new Vector2(length / 2 - 12, 0), new Vector2(15, 15));
            tip.localRotation = Quaternion.Euler(0, 0, 45f);
            UIKit.Img("Tassel", root, Palette.Red, UIKit.Circle).rectTransform.Place(new Vector2(length / 2 - 28, 0), new Vector2(12, 12));
            return root;
        }

        void PlaceView(Lane l) => l.View.Rt.anchoredPosition = l.Pos + new Vector2(0, -29f);

        /// <summary>Spawn point on the field edge for spear i.</summary>
        static Vector2 EdgePoint(int s, float a)
        {
            float hx = FieldW / 2 + 60, hy = FieldH / 2 + 60;
            switch (s)
            {
                case 0: return new Vector2(Mathf.Lerp(-hx, hx, a), hy);
                case 1: return new Vector2(hx, Mathf.Lerp(-hy, hy, a));
                case 2: return new Vector2(Mathf.Lerp(-hx, hx, a), -hy);
                default: return new Vector2(-hx, Mathf.Lerp(-hy, hy, a));
            }
        }

        void Launch(Lane l, int i, float angleDeg)
        {
            var sp = l.Spears[i];
            sp.Pos = EdgePoint(side[i], along[i]);
            sp.Dir = new Vector2(Mathf.Cos(angleDeg * Mathf.Deg2Rad), Mathf.Sin(angleDeg * Mathf.Deg2Rad));
            sp.Speed = speed[i];
            sp.Live = true;
            sp.Rt.gameObject.SetActive(true);
            sp.Rt.localRotation = Quaternion.Euler(0, 0, angleDeg);
            sp.Rt.anchoredPosition = sp.Pos;
        }

        void Update()
        {
            if (!Running) return;
            if (IsAuthority && graceUntil > 0f && Time.time >= graceUntil) Decide();

            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (l.Out) continue;
                float dt = Time.deltaTime;
                l.Elapsed += dt;

                if (IsLocal(p))
                {
                    l.Time.text = $"{l.Elapsed:0.0}초";
                    MovePlayer(p, l, dt);

                    // spawns, aimed at where the player is right now
                    while (l.NextSpawn < Count && spawnAt[l.NextSpawn] <= l.Elapsed)
                    {
                        int i = l.NextSpawn++;
                        var from = EdgePoint(side[i], along[i]);
                        var aim = l.Pos - from;
                        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg + spread[i];
                        Launch(l, i, angle);
                        if (Online) Emit(ActSpawn, p, i, angle);
                    }

                    if (Online && (l.PosTimer -= dt) <= 0f)
                    {
                        l.PosTimer = 0.066f;
                        Emit(ActPos, p, PackPos(l.Pos));
                    }
                }
                else
                {
                    l.Pos = Vector2.Lerp(l.Pos, l.RemoteTarget, 1f - Mathf.Exp(-15f * dt));
                    PlaceView(l);
                }

                // move spears; only the owner's PC decides hits
                bool blink = Time.time < l.InvUntil;
                l.ViewAlpha.alpha = !blink || Mathf.Repeat(Time.time * 12f, 1f) > 0.4f ? 1f : 0.2f;
                foreach (var sp in l.Spears)
                {
                    if (!sp.Live) continue;
                    sp.Pos += sp.Dir * sp.Speed * dt;
                    sp.Rt.anchoredPosition = sp.Pos;
                    if (Mathf.Abs(sp.Pos.x) > FieldW / 2 + 160 || Mathf.Abs(sp.Pos.y) > FieldH / 2 + 160)
                    {
                        sp.Live = false;
                        sp.Rt.gameObject.SetActive(false);
                        continue;
                    }
                    if (!IsLocal(p) || blink) continue;
                    var tipA = sp.Pos + sp.Dir * (SpearLength / 2f);
                    var tipB = sp.Pos - sp.Dir * (SpearLength / 2f - 20f);
                    if (DistanceToSegment(l.Pos, tipA, tipB) <= BodyRadius)
                    {
                        sp.Live = false;
                        sp.Rt.gameObject.SetActive(false);
                        if (l.LivesLeft > 1) Emit(ActHit, p, l.LivesLeft - 1);
                        else Emit(ActOut, p, Mathf.RoundToInt(l.Elapsed * 10f));
                        break;
                    }
                }
            }
        }

        static int PackPos(Vector2 v) => (Mathf.RoundToInt(v.x) + 2000) * 10000 + (Mathf.RoundToInt(v.y) + 2000);
        static Vector2 UnpackPos(int a) => new Vector2(a / 10000 - 2000, a % 10000 - 2000);

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        void MovePlayer(int p, Lane l, float dt)
        {
            Vector2 move;
            if (IsCpuHere(p)) move = CpuMove(l, dt);
            else
            {
                move = Vector2.zero;
                if (InLeftHeld(p)) move.x -= 1;
                if (InRightHeld(p)) move.x += 1;
                if (InUpHeld(p)) move.y += 1;
                if (InDownHeld(p)) move.y -= 1;
                if (move.sqrMagnitude > 1f) move.Normalize();
            }
            l.Pos += move * MoveSpeed * dt;
            l.Pos.x = Mathf.Clamp(l.Pos.x, -FieldW / 2 + 26, FieldW / 2 - 26);
            l.Pos.y = Mathf.Clamp(l.Pos.y, -FieldH / 2 + 30, FieldH / 2 - 30);
            PlaceView(l);
        }

        /// <summary>CPU: sidestep spears that will pass close, drift back to the middle otherwise. Reacts in steps.</summary>
        Vector2 CpuMove(Lane l, float dt)
        {
            l.CpuThink -= dt;
            if (l.CpuThink > 0f) return l.CpuMove;
            l.CpuThink = Random.Range(0.08f, 0.16f);

            var push = -l.Pos * 0.004f; // gentle pull to the center
            foreach (var sp in l.Spears)
            {
                if (!sp.Live) continue;
                var rel = l.Pos - sp.Pos;
                float ahead = Vector2.Dot(rel, sp.Dir);
                if (ahead < -30f) continue;                     // already past
                float time = ahead / sp.Speed;
                if (time > 0.9f) continue;                      // far away, ignore for now
                float lateral = rel.x * -sp.Dir.y + rel.y * sp.Dir.x;
                if (Mathf.Abs(lateral) > 75f) continue;         // will miss anyway
                var perp = new Vector2(-sp.Dir.y, sp.Dir.x) * (lateral >= 0 ? 1f : -1f);
                push += perp * (1.2f / Mathf.Max(0.12f, time));
            }
            l.CpuMove = push.sqrMagnitude > 0.04f ? push.normalized : Vector2.zero;
            return l.CpuMove;
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var l = lanes[p];
            switch (act.Kind)
            {
                case ActPos:
                    if (!IsLocal(p)) l.RemoteTarget = UnpackPos(act.A);
                    break;

                case ActSpawn:
                    if (!IsLocal(p) && act.A >= 0 && act.A < Count) Launch(l, act.A, act.F);
                    break;

                case ActHit:
                    l.LivesLeft = Mathf.Max(1, act.A);
                    l.InvUntil = Time.time + Invincible;
                    RefreshHearts(l);
                    l.View.SetFace(CharacterView.Face.Hurt);
                    l.View.Hit();
                    l.Info.text = "윽!";
                    l.Info.color = Palette.Red;
                    StartCoroutine(Tween.Pop(l.Info.transform, 1.4f, 0.15f));
                    StartCoroutine(Recover(l));
                    break;

                case ActOut:
                    if (l.Out) return;
                    l.Out = true;
                    l.OutTenths = act.A;
                    l.LivesLeft = 0;
                    RefreshHearts(l);
                    l.ViewAlpha.alpha = 1f;
                    l.View.SetFace(CharacterView.Face.Hurt);
                    l.View.SetGrey(true);
                    l.Time.text = $"{act.A / 10f:0.0}초";
                    l.Info.text = "탈락!";
                    l.Info.color = Palette.Red;
                    if (!IsAuthority) return;
                    if (lanes[0].Out && lanes[1].Out) Decide();
                    else if (graceUntil < 0f) graceUntil = Time.time + Grace;
                    break;
            }
        }

        IEnumerator Recover(Lane l)
        {
            yield return new WaitForSeconds(0.6f);
            if (!l.Out) l.View.SetFace(CharacterView.Face.Normal);
        }

        void RefreshHearts(Lane l)
        {
            for (int h = 0; h < Lives; h++) l.Hearts[h].color = h < l.LivesLeft ? Palette.Red : Color.white.WithAlpha(0.15f);
        }

        /// <summary>Authority: the survivor wins; if both are out, the longer survival time wins.</summary>
        void Decide()
        {
            graceUntil = -1f;
            var a = lanes[0];
            var b = lanes[1];
            if (a.Out && b.Out) DeclareWinner(a.OutTenths != b.OutTenths ? (a.OutTenths > b.OutTenths ? 0 : 1) : Seed % 2);
            else DeclareWinner(a.Out ? 1 : 0);
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                foreach (var sp in lanes[p].Spears) sp.Live = false;
                Stamp(lanes[p].Panel, p == WinnerIndex, -70);
            }
        }

        // ---------- Punishment: the loser dodges a few spears… then gets caged and skewered from above ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var stuck = new List<GameObject>();

            // three spears from the side: the loser hops out of the way each time
            for (int i = 0; i < 3; i++)
            {
                float dir = i % 2 == 0 ? 1f : -1f;
                var spear = MakeSpear(ctx.Stage, 180f);
                float y = feet.y + loser.Size * 0.5f;
                var from = new Vector2(-dir * 1200f, y);
                spear.localRotation = Quaternion.Euler(0, 0, dir > 0 ? 0f : 180f);
                spear.anchoredPosition = from;
                ctx.Start(loser.Jump(170f, 0.45f));
                ctx.Start(PunishFx.Popup(ctx.Stage, "휙!", feet + new Vector2(0, loser.Size + 200f), Palette.Gold, 48));
                yield return Tween.Run(0.45f, t => spear.anchoredPosition = Vector2.Lerp(from, new Vector2(dir * 1200f, y), t));
                Destroy(spear.gameObject);
            }

            // a cage of spears thuds into the ground around the loser
            ctx.Start(PunishFx.Popup(ctx.Stage, "꼼짝마!", feet + new Vector2(0, loser.Size + 220f), Palette.Red, 72));
            foreach (var x in new[] { -150f, 150f, -90f, 90f })
            {
                var spear = MakeSpear(ctx.Stage, 220f);
                spear.localRotation = Quaternion.Euler(0, 0, -90f);
                var to = new Vector2(feet.x + x, feet.y + 80f);
                var from = new Vector2(to.x, 800f);
                spear.anchoredPosition = from;
                stuck.Add(spear.gameObject);
                yield return Tween.Run(0.12f, t => spear.anchoredPosition = Vector2.Lerp(from, to, t), Ease.InQuad);
                ctx.Start(ctx.Shake(8f, 0.1f));
                loser.SetFace(CharacterView.Face.Hurt);
            }
            yield return new WaitForSeconds(0.35f);

            // the last one comes straight down
            var final = MakeSpear(ctx.Stage, 260f);
            final.localRotation = Quaternion.Euler(0, 0, -90f);
            var top = new Vector2(feet.x, 900f);
            var hit = new Vector2(feet.x, feet.y + loser.Size * 0.3f + 130f);
            final.anchoredPosition = top;
            stuck.Add(final.gameObject);
            yield return Tween.Run(0.18f, t => final.anchoredPosition = Vector2.Lerp(top, hit, t), Ease.InQuad);
            loser.Hit();
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            ctx.Start(ctx.Shake(24f, 0.35f));
            ctx.Start(PunishFx.Popup(ctx.Stage, "푹!", feet + new Vector2(0, 160f), Palette.Red, 84));
            yield return new WaitForSeconds(0.9f);
            foreach (var s in stuck) Destroy(s);
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }
    }
}
