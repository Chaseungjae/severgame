using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Timing survival: a marker sweeps back and forth across a bar; press while it's inside the green zone.
    /// Every hit makes the zone smaller and the marker faster. Misses cost a life (3 lives). Whoever lasts longer
    /// (more hits) wins. Both players get the same zones and speeds.
    /// </summary>
    public class TimingGame : MiniGameBase
    {
        public override string Title => "타이밍 서바이벌";
        public override string Rules => "움직이는 막대가 초록 구역 안에 있을 때 누르세요!\n성공할 때마다 구역은 좁아지고 속도는 빨라집니다.\n목숨 3개 — 더 오래 버틴 사람이 승리!";
        public override string ControlsText => "P1   Space\nP2   Enter";
        public override string OnlineControlsText => "Space 또는 마우스 클릭";

        const int Lives = 3;
        const float BarW = 600f, BarH = 70f;
        const float MaxSweeps = 3f; // not pressing within 3 passes counts as a miss
        const int MaxLevel = 200;
        const int ActHit = 1, ActMiss = 2, ActOut = 3;

        float[] zoneCenter, zoneWidth, speed;

        class Lane
        {
            public RectTransform Panel, Bar, Zone, Marker;
            public Text Score, Info;
            public Text[] Hearts = new Text[Lives];
            public int Level, LivesLeft = Lives;
            public float X, Dir = 1f, Travelled, CpuTarget, PrevDelta = float.NaN;
            public bool Out;
        }

        readonly Lane[] lanes = new Lane[2];
        readonly SurvivalJudge judge = new SurvivalJudge();

        protected override void Build()
        {
            Background();
            var rng = new System.Random(Seed);
            zoneCenter = new float[MaxLevel];
            zoneWidth = new float[MaxLevel];
            speed = new float[MaxLevel];
            for (int k = 0; k < MaxLevel; k++)
            {
                zoneWidth[k] = Mathf.Max(16f, 150f * Mathf.Pow(0.9f, k));
                float half = BarW / 2f - zoneWidth[k] / 2f - 10f;
                zoneCenter[k] = ((float)rng.NextDouble() * 2f - 1f) * half;
                speed[k] = Mathf.Min(380f + 38f * k, 1500f);
            }

            for (int p = 0; p < 2; p++)
            {
                var lane = new Lane { Panel = PlayerPanel(p, 760, 600, -60) };
                lanes[p] = lane;
                lane.Score = UIKit.Label("Score", lane.Panel, "0", 110, Color.white);
                lane.Score.rectTransform.Place(new Vector2(0, 150), new Vector2(400, 130));
                lane.Info = UIKit.Label("Info", lane.Panel, "", 36, Palette.Gold);
                lane.Info.rectTransform.Place(new Vector2(0, 65), new Vector2(500, 50));

                lane.Bar = UIKit.Img("Bar", lane.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                lane.Bar.Place(new Vector2(0, -40), new Vector2(BarW, BarH));
                lane.Zone = UIKit.Img("Zone", lane.Bar, Palette.Green, UIKit.Rounded).rectTransform;
                lane.Marker = UIKit.Img("Marker", lane.Bar, Color.white, UIKit.Rounded).rectTransform;
                lane.Marker.sizeDelta = new Vector2(10, BarH + 24);

                for (int h = 0; h < Lives; h++)
                {
                    var heart = UIKit.Label("Heart", lane.Panel, "♥", 48, Palette.Red);
                    heart.rectTransform.Place(new Vector2((h - 1) * 60, -150), new Vector2(60, 60));
                    lane.Hearts[h] = heart;
                }
                UIKit.Label("Hint", lane.Panel, Online ? "Space / 클릭" : p == 0 ? "Space" : "Enter", 26, Palette.Muted, style: FontStyle.Normal)
                    .rectTransform.Place(new Vector2(0, -220), new Vector2(300, 40));
                lane.X = -BarW / 2f;
                ShowLevel(lane);
            }
        }

        void ShowLevel(Lane lane)
        {
            int k = Mathf.Min(lane.Level, MaxLevel - 1);
            lane.Zone.Place(new Vector2(zoneCenter[k], 0), new Vector2(zoneWidth[k], BarH));
            lane.Marker.anchoredPosition = new Vector2(lane.X, 0);
            lane.Score.text = lane.Level.ToString();
        }

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var lane = lanes[p];
                if (!IsLocal(p) || lane.Out) continue;

                int k = Mathf.Min(lane.Level, MaxLevel - 1);
                float move = speed[k] * Time.deltaTime;
                lane.X += lane.Dir * move;
                lane.Travelled += move;
                float lim = BarW / 2f - 6f;
                if (lane.X > lim) { lane.X = lim; lane.Dir = -1f; }
                if (lane.X < -lim) { lane.X = -lim; lane.Dir = 1f; }
                lane.Marker.anchoredPosition = new Vector2(lane.X, 0);

                bool press;
                if (IsCpuHere(p))
                {
                    float delta = lane.X - lane.CpuTarget;
                    press = !float.IsNaN(lane.PrevDelta) && Mathf.Sign(delta) != Mathf.Sign(lane.PrevDelta);
                    lane.PrevDelta = delta;
                }
                else press = InConfirm(p) || InClick(p);

                if (press) Judge(p);
                else if (lane.Travelled > BarW * MaxSweeps) Judge(p, forceMiss: true);
            }
        }

        void PlanCpu(Lane lane)
        {
            int k = Mathf.Min(lane.Level, MaxLevel - 1);
            float err = Random.Range(-1f, 1f) * (zoneWidth[k] * 0.3f + 5f + k * 1.2f);
            lane.CpuTarget = zoneCenter[k] + err;
            lane.PrevDelta = float.NaN;
        }

        void Judge(int p, bool forceMiss = false)
        {
            var lane = lanes[p];
            int k = Mathf.Min(lane.Level, MaxLevel - 1);
            float offset = Mathf.Abs(lane.X - zoneCenter[k]);
            bool hit = !forceMiss && offset <= zoneWidth[k] / 2f + 5f; // marker half-width counts
            lane.Travelled = 0f;
            if (hit) Emit(ActHit, p, lane.Level + 1, offset);
            else if (lane.LivesLeft > 1) Emit(ActMiss, p, lane.LivesLeft - 1);
            else Emit(ActOut, p, lane.Level);
        }

        protected override void OnBegin()
        {
            for (int p = 0; p < 2; p++)
                if (IsCpuHere(p)) PlanCpu(lanes[p]);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var lane = lanes[p];
            if (lane.Out) return;

            switch (act.Kind)
            {
                case ActHit:
                    lane.Level = act.A;
                    judge.Score[p] = lane.Level;
                    judge.TieBreak[p] += act.F; // total distance from zone centers (lower = more accurate)
                    lane.Info.text = act.F < 4f ? "PERFECT!" : "GOOD";
                    lane.Info.color = act.F < 4f ? Palette.Gold : Palette.Green;
                    StartCoroutine(Tween.Pop(lane.Info.transform, 1.4f, 0.15f));
                    StartCoroutine(Tween.Pop(lane.Score.transform, 1.25f, 0.12f));
                    ShowLevel(lane);
                    StartCoroutine(Tween.Pop(lane.Zone, 1.3f, 0.15f));
                    if (IsCpuHere(p)) PlanCpu(lane);
                    if (lane.Level >= MaxLevel) { lane.Out = true; judge.Out[p] = true; }
                    break;

                case ActMiss:
                    lane.LivesLeft = act.A;
                    LoseHeart(lane);
                    if (IsCpuHere(p)) PlanCpu(lane);
                    break;

                case ActOut:
                    lane.LivesLeft = 0;
                    LoseHeart(lane);
                    lane.Out = true;
                    lane.Info.text = "탈락!";
                    lane.Info.color = Palette.Red;
                    lane.Marker.GetComponent<Image>().color = Palette.Red;
                    judge.Out[p] = true;
                    break;
            }

            if (IsAuthority)
            {
                int w = judge.Check();
                if (w >= 0) DeclareWinner(w);
            }
        }

        void LoseHeart(Lane lane)
        {
            for (int h = 0; h < Lives; h++)
            {
                lane.Hearts[h].color = h < lane.LivesLeft ? Palette.Red : Color.white.WithAlpha(0.15f);
            }
            lane.Info.text = "MISS";
            lane.Info.color = Palette.Red;
            StartCoroutine(Shake(lane.Bar));
        }

        IEnumerator Shake(RectTransform rt)
        {
            var home = rt.anchoredPosition;
            yield return Tween.Run(0.3f, t => rt.anchoredPosition = home + Vector2.right * Mathf.Sin(t * 40f) * 10f * (1 - t));
            rt.anchoredPosition = home;
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, 0);
        }

        // ---------- Punishment: a giant pendulum hammer swings through the loser, perfectly on time ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            const float length = 520f;
            var pivot = UIKit.Rect("Pendulum", ctx.Stage);
            pivot.Place(feet + new Vector2(0, loser.Size * 0.55f + length), new Vector2(10, 10));
            UIKit.Img("Rod", pivot, Palette.Hex("#8C93B8"), UIKit.Rounded).rectTransform
                .Place(new Vector2(0, -length / 2f), new Vector2(18, length));
            var head = UIKit.Img("Head", pivot, Palette.Hex("#50557A"), UIKit.Rounded).rectTransform;
            head.Place(new Vector2(0, -length), new Vector2(200, 110));
            UIKit.Label("Tick", head, "딱!", 48, Color.white).rectTransform.Stretch();
            UIKit.Img("Hinge", pivot, Palette.Gold, UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(34, 34));

            // metronome swings: 70° → -70° → ..., hitting the loser at the bottom each time
            float from = 75f;
            pivot.localRotation = Quaternion.Euler(0, 0, from);
            yield return new WaitForSeconds(0.3f);
            for (int swing = 0; swing < 4; swing++)
            {
                float to = -from;
                bool hit = false;
                float dur = swing == 3 ? 0.28f : 0.42f;
                yield return Tween.Run(dur, t =>
                {
                    float angle = Mathf.Lerp(from, to, Ease.InOutQuad(t));
                    pivot.localRotation = Quaternion.Euler(0, 0, angle);
                    if (!hit && Mathf.Sign(angle) != Mathf.Sign(from))
                    {
                        hit = true;
                        loser.SetFace(CharacterView.Face.Hurt);
                        loser.Hit();
                        ctx.Start(ctx.Shake(swing == 3 ? 26f : 10f, 0.2f));
                        ctx.Start(PunishFx.Popup(ctx.Stage, swing == 3 ? "딱!!!" : "딱!", feet + new Vector2(-Mathf.Sign(from) * 160f, 240f), Palette.Gold, swing == 3 ? 84 : 52));
                    }
                });
                from = to;
            }

            // last swing launches the loser sideways
            float dir = -Mathf.Sign(from);
            loser.Idle = false;
            yield return Tween.Run(0.45f, t =>
            {
                loser.Rt.anchoredPosition = feet + new Vector2(dir * 260f * t, Mathf.Sin(t * Mathf.PI) * 180f);
                loser.Rt.localRotation = Quaternion.Euler(0, 0, -dir * 360f * t);
            });
            loser.Rt.localRotation = Quaternion.identity;
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            ctx.Start(ctx.Shake(20f, 0.3f));
            Destroy(pivot.gameObject);
            yield return new WaitForSeconds(0.6f);
            yield return Tween.Run(0.35f, t => loser.Rt.anchoredPosition = feet + new Vector2(dir * 260f * (1 - t), 0));
            loser.Idle = true;
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }
    }
}
