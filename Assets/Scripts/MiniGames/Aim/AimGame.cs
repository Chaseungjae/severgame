using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Aim duel (Valorant range style): bots pop up at random spots for a moment and vanish; some strafe.
    /// Both players get the exact same spawn schedule (shared seed). Most hits wins, ties go to the faster
    /// average reaction. Each PC times its own player, so latency doesn't matter online.
    /// </summary>
    public class AimGame : MiniGameBase
    {
        public override string Title => "에임 대결";
        public override string Rules => "순간 나타났다 사라지는 적을 최대한 빨리 맞히세요! (일부는 움직임)\n두 사람에게 똑같은 위치·순서로 나옵니다.\n더 많이 맞힌 사람 승리 · 동점이면 평균 반응속도 승부";
        public override string ControlsText => "마우스로 조준 + 왼쪽 클릭";

        const int TargetCount = 24;
        const float FieldW = 820f, FieldH = 600f;
        const int ActHit = 1, ActFinal = 2;

        struct Spawn
        {
            public float At, Life, Size;
            public Vector2 Pos, Vel;
        }

        class Field
        {
            public RectTransform Rt, Crosshair;
            public readonly RectTransform[] Targets = new RectTransform[TargetCount];
            public readonly bool[] Hit = new bool[TargetCount];
            public readonly float[] CpuShotAt = new float[TargetCount];
            public int Hits;
            public float ReactSum;
            public Text Score, Avg;
            public bool FinalSent, HasFinal;
        }

        Spawn[] schedule;
        readonly Field[] fields = new Field[2];
        readonly RectTransform[] panels = new RectTransform[2];
        float elapsed, endTime;
        Text remainText;

        protected override void Build()
        {
            Background();
            BuildSchedule();

            remainText = UIKit.Label("Remain", Root, "", 36, Palette.Muted);
            remainText.rectTransform.Place(new Vector2(0, 380), new Vector2(600, 50));

            for (int p = 0; p < 2; p++)
            {
                panels[p] = PlayerPanel(p, 880, 760, -100);
                var f = new Field();
                fields[p] = f;

                f.Score = UIKit.Label("Score", panels[p], "명중 0", 40, Color.white, TextAnchor.MiddleLeft);
                f.Score.rectTransform.Place(new Vector2(-410, 290), new Vector2(400, 50), pivot: new Vector2(0, 0.5f));
                f.Avg = UIKit.Label("Avg", panels[p], "평균 --- ms", 30, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
                f.Avg.rectTransform.Place(new Vector2(410, 290), new Vector2(400, 50), pivot: new Vector2(1, 0.5f));

                var field = UIKit.Img("Field", panels[p], Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                field.Place(new Vector2(0, -50), new Vector2(FieldW, FieldH));
                field.gameObject.AddComponent<RectMask2D>();
                f.Rt = field;
                // range decoration
                UIKit.Img("Floor", field, Color.white.WithAlpha(0.04f)).rectTransform.Place(new Vector2(0, -FieldH / 2 + 60), new Vector2(FieldW, 120));
                UIKit.Img("Line", field, Color.white.WithAlpha(0.08f)).rectTransform.Place(new Vector2(0, -FieldH / 2 + 120), new Vector2(FieldW, 3));

                for (int i = 0; i < TargetCount; i++)
                {
                    f.Targets[i] = BuildBot(field, schedule[i].Size);
                    f.Targets[i].gameObject.SetActive(false);
                    f.CpuShotAt[i] = -1f;
                }

                f.Crosshair = BuildCrosshair(field, Players[p].Color, 44);
                f.Crosshair.gameObject.SetActive(false);

                if (IsCpuHere(p))
                {
                    var rng = new System.Random(Seed + 101 * (p + 1));
                    for (int i = 0; i < TargetCount; i++)
                        if (rng.NextDouble() < 0.8)
                            f.CpuShotAt[i] = schedule[i].At + Mathf.Lerp(0.3f, 0.6f, (float)rng.NextDouble());
                }
            }
            UpdateRemain();
        }

        void BuildSchedule()
        {
            var rng = new System.Random(Seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            schedule = new Spawn[TargetCount];
            float t = 1.0f;
            for (int i = 0; i < TargetCount; i++)
            {
                float k = i / (float)(TargetCount - 1);
                var s = new Spawn
                {
                    At = t,
                    Life = Mathf.Lerp(1.15f, 0.7f, k),
                    Size = R(64f, 92f),
                };
                s.Pos = new Vector2(R(-FieldW / 2 + 70, FieldW / 2 - 70), R(-FieldH / 2 + 80, FieldH / 2 - 90));
                if (i % 3 == 2) // strafing bot
                {
                    float speed = R(180f, 280f) * (rng.NextDouble() < 0.5 ? -1 : 1);
                    s.Vel = new Vector2(speed, 0);
                }
                schedule[i] = s;
                t += R(0.55f, 0.95f);
            }
            var last = schedule[TargetCount - 1];
            endTime = last.At + last.Life + 0.4f;
        }

        static RectTransform BuildBot(RectTransform parent, float size)
        {
            var root = UIKit.Rect("Bot", parent);
            root.sizeDelta = new Vector2(size, size * 1.45f);
            var red = Palette.Hex("#FF4655");
            UIKit.Img("Body", root, Palette.Hex("#8A1E2A"), UIKit.Rounded).rectTransform.Place(new Vector2(0, -size * 0.3f), new Vector2(size * 0.9f, size * 0.85f));
            UIKit.Img("Head", root, red, UIKit.Circle).rectTransform.Place(new Vector2(0, size * 0.36f), new Vector2(size * 0.62f, size * 0.62f));
            UIKit.Img("Visor", root, Palette.Hex("#FFD166"), UIKit.Rounded).rectTransform.Place(new Vector2(0, size * 0.38f), new Vector2(size * 0.44f, size * 0.12f));
            return root;
        }

        static RectTransform BuildCrosshair(RectTransform parent, Color color, float size)
        {
            var root = UIKit.Rect("Crosshair", parent);
            root.sizeDelta = new Vector2(size, size);
            float gap = size * 0.18f, len = size * 0.32f, w = 4f;
            UIKit.Img("Dot", root, color, UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(6, 6));
            UIKit.Img("L", root, color).rectTransform.Place(new Vector2(-gap - len / 2, 0), new Vector2(len, w));
            UIKit.Img("R", root, color).rectTransform.Place(new Vector2(gap + len / 2, 0), new Vector2(len, w));
            UIKit.Img("U", root, color).rectTransform.Place(new Vector2(0, gap + len / 2), new Vector2(w, len));
            UIKit.Img("D", root, color).rectTransform.Place(new Vector2(0, -gap - len / 2), new Vector2(w, len));
            return root;
        }

        /// <summary>Where target i is at the current time (strafers bounce off the walls).</summary>
        Vector2 TargetPos(int i)
        {
            var s = schedule[i];
            float t = elapsed - s.At;
            float x = s.Pos.x + s.Vel.x * t;
            float half = FieldW / 2 - s.Size * 0.5f;
            float span = half * 2f;
            float u = Mathf.Repeat(x + half, span * 2f);
            x = (u < span ? u : span * 2f - u) - half;
            return new Vector2(x, s.Pos.y);
        }

        bool Visible(Field f, int i) => !f.Hit[i] && elapsed >= schedule[i].At && elapsed < schedule[i].At + schedule[i].Life;

        protected override void OnBegin() => Cursor.visible = false;

        void OnDestroy() => Cursor.visible = true;

        void Update()
        {
            if (!Running) return;
            elapsed += Time.deltaTime;

            for (int p = 0; p < 2; p++)
            {
                var f = fields[p];
                for (int i = 0; i < TargetCount; i++)
                {
                    var tr = f.Targets[i];
                    bool vis = Visible(f, i);
                    if (tr.gameObject.activeSelf != vis) tr.gameObject.SetActive(vis);
                    if (!vis) continue;
                    tr.anchoredPosition = TargetPos(i);
                    float age = elapsed - schedule[i].At;
                    float left = schedule[i].At + schedule[i].Life - elapsed;
                    tr.localScale = Vector3.one * Mathf.Min(1f, age / 0.07f, left / 0.07f);
                }

                if (!IsLocal(p) || f.FinalSent) continue;

                if (elapsed >= endTime)
                {
                    f.FinalSent = true;
                    Emit(ActFinal, p, f.Hits, f.ReactSum);
                    continue;
                }

                if (IsCpuHere(p)) UpdateCpu(p);
                else UpdateHuman(p);
            }
            UpdateRemain();
        }

        void UpdateHuman(int p)
        {
            var f = fields[p];
            var local = MouseIn(f.Rt);
            bool inside = Mathf.Abs(local.x) <= FieldW / 2 && Mathf.Abs(local.y) <= FieldH / 2;

            // offline: the field under the cursor owns the click; online: only your own field
            bool owns = Online || inside;
            f.Crosshair.gameObject.SetActive(inside);
            if (inside) f.Crosshair.anchoredPosition = local;
            f.Crosshair.SetAsLastSibling();

            if (!owns || !inside || !InClick(p)) return;

            int best = -1;
            for (int i = 0; i < TargetCount; i++)
            {
                if (!Visible(f, i)) continue;
                var s = schedule[i];
                var d = local - TargetPos(i);
                if (Mathf.Abs(d.x) <= s.Size * 0.48f && Mathf.Abs(d.y) <= s.Size * 0.72f) { best = i; break; }
            }
            if (best >= 0) Emit(ActHit, p, best, elapsed - schedule[best].At);
            else StartCoroutine(MissMark(f, local));
        }

        void UpdateCpu(int p)
        {
            var f = fields[p];
            for (int i = 0; i < TargetCount; i++)
            {
                if (f.CpuShotAt[i] < 0 || f.Hit[i] || elapsed < f.CpuShotAt[i]) continue;
                f.CpuShotAt[i] = -1f;
                if (Visible(f, i)) Emit(ActHit, p, i, elapsed - schedule[i].At);
            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            var f = fields[act.Player];

            if (act.Kind == ActHit)
            {
                int i = act.A;
                if (i < 0 || i >= TargetCount || f.Hit[i] || f.HasFinal) return;
                f.Hit[i] = true;
                f.Hits++;
                f.ReactSum += act.F;
                f.Targets[i].gameObject.SetActive(false);
                StartCoroutine(HitBurst(f, TargetPos(i), Players[act.Player].Color));
                Juice.Burst(f.Rt, TargetPos(i), Juice.Style.Debris, 8, Palette.Hex("#FF4655"), 0.7f);
                RefreshScore(f);
            }
            else if (act.Kind == ActFinal)
            {
                f.HasFinal = true;
                f.Hits = act.A;
                f.ReactSum = act.F;
                RefreshScore(f);
                if (IsAuthority && fields[0].HasFinal && fields[1].HasFinal) Decide();
            }
        }

        void Decide()
        {
            var a = fields[0];
            var b = fields[1];
            if (a.Hits != b.Hits)
            {
                DeclareWinner(a.Hits > b.Hits ? 0 : 1);
                return;
            }
            float avgA = a.Hits > 0 ? a.ReactSum / a.Hits : float.MaxValue;
            float avgB = b.Hits > 0 ? b.ReactSum / b.Hits : float.MaxValue;
            DeclareWinner(avgA <= avgB ? 0 : 1);
        }

        void RefreshScore(Field f)
        {
            f.Score.text = $"명중 {f.Hits}";
            f.Avg.text = f.Hits > 0 ? $"평균 {Mathf.RoundToInt(f.ReactSum / f.Hits * 1000)} ms" : "평균 --- ms";
            StartCoroutine(Tween.Pop(f.Score.transform, 1.25f, 0.15f));
        }

        void UpdateRemain()
        {
            int left = 0;
            for (int i = 0; i < TargetCount; i++)
                if (elapsed < schedule[i].At + schedule[i].Life) left++;
            remainText.text = left > 0 ? $"남은 적 {left} / {TargetCount}" : "사격 종료!";
        }

        IEnumerator HitBurst(Field f, Vector2 pos, Color color)
        {
            var ring = UIKit.Img("Hit", f.Rt, color, UIKit.Circle);
            ring.rectTransform.Place(pos, new Vector2(40, 40));
            var plus = UIKit.Label("Plus", f.Rt, "+1", 40, Palette.Gold);
            plus.rectTransform.Place(pos + new Vector2(0, 50), new Vector2(120, 60));
            yield return Tween.Run(0.3f, t =>
            {
                ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40, 150, t);
                ring.color = color.WithAlpha(0.8f * (1 - t));
                plus.rectTransform.anchoredPosition = pos + new Vector2(0, 50 + 40 * t);
                plus.color = Palette.Gold.WithAlpha(1 - t);
            }, Ease.OutQuad);
            Destroy(ring.gameObject);
            Destroy(plus.gameObject);
        }

        IEnumerator MissMark(Field f, Vector2 pos)
        {
            var mark = UIKit.Label("Miss", f.Rt, "×", 36, Color.white.WithAlpha(0.6f));
            mark.rectTransform.Place(pos, new Vector2(60, 60));
            yield return Tween.FadeGraphic(mark, 0f, 0.4f);
            Destroy(mark.gameObject);
        }

        protected override void OnFinish()
        {
            Cursor.visible = true;
            remainText.text = "사격 종료!";
            for (int p = 0; p < 2; p++)
            {
                fields[p].Crosshair.gameObject.SetActive(false);
                Stamp(panels[p], p == WinnerIndex, -50);
            }
        }

        // ---------- Punishment: the winner's crosshair lights the loser up, finishing with a headshot ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var color = Players[WinnerIndex].Color;
            var cross = BuildCrosshair(ctx.Stage, color, 120);
            var from = new Vector2(ctx.Winner.Rt.anchoredPosition.x, 300);
            cross.anchoredPosition = from;

            var holes = new System.Collections.Generic.List<GameObject>();
            for (int n = 0; n < 6; n++)
            {
                var aim = feet + new Vector2(Random.Range(-70f, 70f), Random.Range(40f, 170f));
                var start = cross.anchoredPosition;
                yield return Tween.Run(0.14f, t => cross.anchoredPosition = Vector2.Lerp(start, aim, t), Ease.OutQuad);
                var hole = UIKit.Img("Hole", ctx.Stage, Palette.Ink, UIKit.Circle);
                hole.rectTransform.Place(aim, new Vector2(18, 18));
                holes.Add(hole.gameObject);
                loser.SetFace(CharacterView.Face.Hurt);
                loser.Hit();
                ctx.Start(PunishFx.Popup(ctx.Stage, "탕!", aim + new Vector2(60, 60), Palette.Gold, 48));
                ctx.Start(ctx.Shake(7f, 0.1f));
                yield return new WaitForSeconds(0.08f);
            }

            // slow lock onto the head…
            var head = feet + new Vector2(0, loser.Size * 0.62f);
            var s0 = cross.anchoredPosition;
            yield return Tween.Run(0.5f, t =>
            {
                cross.anchoredPosition = Vector2.Lerp(s0, head, t);
                cross.localScale = Vector3.one * Mathf.Lerp(1.6f, 0.8f, t);
            }, Ease.InOutQuad);
            yield return new WaitForSeconds(0.25f);

            // …HEADSHOT
            var flash = UIKit.Img("Flash", ctx.Stage, Color.white.WithAlpha(0.6f));
            flash.rectTransform.Stretch();
            ctx.Start(PunishFx.Popup(ctx.Stage, "HEADSHOT!", head + new Vector2(0, 200), Palette.Red, 96));
            ctx.Start(ctx.Shake(26f, 0.4f));
            loser.Hit();
            float dir = Mathf.Sign(feet.x - ctx.Winner.Rt.anchoredPosition.x);
            loser.Idle = false;
            yield return Tween.Run(0.3f, t =>
            {
                flash.color = Color.white.WithAlpha(0.6f * (1 - t));
                loser.Rt.localRotation = Quaternion.Euler(0, 0, -dir * 90f * t);
                loser.Rt.anchoredPosition = feet + new Vector2(dir * 60f * t, 0);
            }, Ease.OutQuad);
            Destroy(flash.gameObject);
            Destroy(cross.gameObject);
            yield return new WaitForSeconds(0.9f);

            foreach (var h in holes) Destroy(h);
            yield return Tween.Run(0.3f, t =>
            {
                loser.Rt.localRotation = Quaternion.Euler(0, 0, -dir * 90f * (1 - t));
                loser.Rt.anchoredPosition = feet + new Vector2(dir * 60f * (1 - t), 0);
            }, Ease.OutBack);
            loser.Rt.localRotation = Quaternion.identity;
            loser.Rt.anchoredPosition = feet;
            loser.Idle = true;
        }
    }
}
