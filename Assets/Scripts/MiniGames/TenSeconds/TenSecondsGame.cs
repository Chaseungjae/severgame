using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Stop at 10 seconds: a stopwatch runs, but the digits vanish after 5 seconds. Stop it as close to 10.00 as you
    /// can. Closest wins the round; best of 3. Each PC times its own player against real time.
    /// </summary>
    public class TenSecondsGame : MiniGameBase
    {
        public override string Title => "10초 맞추기";
        public override string Rules => "스톱워치가 흘러갑니다. 5초가 지나면 숫자가 사라져요!\n속으로 세다가 정확히 10초라고 생각될 때 누르세요.\n10.00초에 더 가까운 사람이 라운드 승리 · 3판 2선승";
        public override string ControlsText => "P1   Space\nP2   Enter";
        public override string OnlineControlsText => "Space 또는 마우스 클릭";

        const int WinsNeeded = 2;
        const float Target = 10f, HideAfter = 5f, GiveUp = 15f;
        const int ActRoundStart = 1; // system: A = round
        const int ActStop = 2;       // player: A = round, F = stopped time
        const int ActRoundResult = 3; // system: A = winner

        class Lane
        {
            public RectTransform Panel;
            public Text Clock, Result, Status;
            public Image[] Pips = new Image[WinsNeeded];
            public bool Running, Reported;
            public float Start, Stopped, CpuStopAt;
        }

        readonly Lane[] lanes = new Lane[2];
        readonly int[] wins = new int[2];
        int round;
        bool resolved;
        readonly bool[] reported = new bool[2];
        readonly float[] times = new float[2];
        Text roundText;

        protected override void Build()
        {
            Background();
            roundText = UIKit.Label("Round", Root, "", 40, Palette.Muted);
            roundText.rectTransform.Place(new Vector2(0, 380), new Vector2(600, 56));

            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 760, 700, -80) };
                lanes[p] = l;
                var face = UIKit.Img("Face", l.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                face.Place(new Vector2(0, 110), new Vector2(620, 240));
                l.Clock = UIKit.Label("Clock", face, "0.00", 150, Color.white);
                l.Clock.rectTransform.Stretch();
                l.Result = UIKit.Label("Result", l.Panel, "", 48, Palette.Gold);
                l.Result.rectTransform.Place(new Vector2(0, -60), new Vector2(620, 70));
                l.Status = UIKit.Label("Status", l.Panel, "", 32, Palette.Muted);
                l.Status.rectTransform.Place(new Vector2(0, -140), new Vector2(620, 50));
                for (int w = 0; w < WinsNeeded; w++)
                {
                    var pip = UIKit.Img("Pip", l.Panel, Color.white.WithAlpha(0.15f), UIKit.Circle);
                    pip.rectTransform.Place(new Vector2((w - 0.5f) * 60, -230), new Vector2(40, 40));
                    l.Pips[w] = pip;
                }
                string key = Online ? "Space / 클릭" : p == 0 ? "Space" : "Enter";
                UIKit.Label("Key", l.Panel, $"{key}로 멈추기", 26, Palette.Muted, style: FontStyle.Normal)
                    .rectTransform.Place(new Vector2(0, -295), new Vector2(400, 40));
            }
        }

        // ---------- authority: rounds ----------

        protected override void OnBegin()
        {
            if (IsAuthority) StartCoroutine(RunRounds());
        }

        IEnumerator RunRounds()
        {
            int n = 0;
            while (wins[0] < WinsNeeded && wins[1] < WinsNeeded)
            {
                n++;
                Emit(ActRoundStart, -1, n);
                while (!resolved)
                {
                    if (Online && Net.Disconnected) yield break;
                    yield return null;
                }
                yield return new WaitForSeconds(2.2f);
            }
            DeclareWinner(wins[0] >= WinsNeeded ? 0 : 1);
        }

        // ---------- everyone ----------

        protected override void OnAction(GameAction act)
        {
            switch (act.Kind)
            {
                case ActRoundStart:
                    round = act.A;
                    resolved = false;
                    roundText.text = $"ROUND {round}";
                    for (int p = 0; p < 2; p++)
                    {
                        reported[p] = false;
                        var l = lanes[p];
                        l.Result.text = "";
                        l.Status.text = IsLocal(p) ? "" : "측정 중...";
                        l.Clock.text = "0.00";
                        l.Clock.color = Color.white;
                        if (!IsLocal(p)) continue;
                        l.Running = true;
                        l.Reported = false;
                        l.Start = Time.unscaledTime;
                        l.CpuStopAt = Target + Mathf.Clamp(Gaussian() * 0.35f, -1.2f, 1.2f);
                    }
                    break;

                case ActStop:
                {
                    int p = act.Player;
                    if (p < 0 || p > 1 || act.A != round || reported[p] || resolved) return;
                    reported[p] = true;
                    times[p] = act.F;
                    lanes[p].Status.text = "멈춤!";
                    lanes[p].Status.color = Palette.Green;
                    if (IsAuthority && reported[0] && reported[1])
                    {
                        float d0 = Mathf.Abs(times[0] - Target), d1 = Mathf.Abs(times[1] - Target);
                        int winner = Mathf.Abs(d0 - d1) < 0.0005f ? Seed % 2 : d0 < d1 ? 0 : 1;
                        Emit(ActRoundResult, -1, winner);
                    }
                    break;
                }

                case ActRoundResult:
                    if (resolved) return;
                    resolved = true;
                    for (int p = 0; p < 2; p++)
                    {
                        var l = lanes[p];
                        float diff = times[p] - Target;
                        l.Clock.text = times[p].ToString("0.00");
                        l.Clock.color = p == act.A ? Palette.Green : Palette.Red;
                        l.Result.text = $"{(diff >= 0 ? "+" : "")}{diff:0.00}초";
                        l.Status.text = p == act.A ? "라운드 승리!" : "";
                        l.Status.color = Players[p].Color;
                        StartCoroutine(Tween.Pop(l.Clock.transform, 1.3f, 0.25f));
                    }
                    int w = act.A;
                    wins[w] = Mathf.Min(WinsNeeded, wins[w] + 1);
                    var pip = lanes[w].Pips[wins[w] - 1];
                    pip.color = Players[w].Color;
                    StartCoroutine(Tween.Pop(pip.transform, 2f, 0.3f));
                    break;
            }
        }

        static float Gaussian()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        }

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (!IsLocal(p) || !l.Running) continue;
                float t = Time.unscaledTime - l.Start;
                l.Clock.text = t < HideAfter ? t.ToString("0.00") : "?.??";
                l.Clock.color = t < HideAfter ? Color.white : Palette.Muted;

                bool stop = IsCpuHere(p) ? t >= l.CpuStopAt : InConfirm(p) || InClick(p);
                if (t >= GiveUp) stop = true;
                if (!stop) continue;

                l.Running = false;
                l.Clock.text = "멈춤";
                Emit(ActStop, p, round, Mathf.Min(t, GiveUp));
            }
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, 110);
        }

        // ---------- Punishment: a giant alarm clock rings in the loser's ears, then drops on them ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var clock = UIKit.Rect("AlarmClock", ctx.Stage);
            clock.Place(feet + new Vector2(0, loser.Size + 170f), new Vector2(220, 220));
            var red = Palette.Red;
            foreach (var x in new[] { -70f, 70f })
                UIKit.Img("Bell", clock, red, UIKit.Circle).rectTransform.Place(new Vector2(x, 95), new Vector2(80, 80));
            UIKit.Img("Body", clock, red, UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(220, 220));
            UIKit.Img("Face", clock, Color.white, UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(180, 180));
            var hour = UIKit.Img("Hour", clock, Palette.Ink, UIKit.Rounded).rectTransform;
            hour.Place(Vector2.zero, new Vector2(10, 55), pivot: new Vector2(0.5f, 0f));
            var minute = UIKit.Img("Minute", clock, Palette.Ink, UIKit.Rounded).rectTransform;
            minute.Place(Vector2.zero, new Vector2(7, 78), pivot: new Vector2(0.5f, 0f));
            UIKit.Label("Ten", clock, "10", 40, Palette.Ink).rectTransform.Place(new Vector2(-45, 50), new Vector2(60, 50));

            ctx.Start(Tween.Pop(clock, 0.2f, 0.3f));
            loser.SetFace(CharacterView.Face.Hurt);
            // ring ring ring
            float popT = 0f;
            yield return Tween.Run(1.8f, t =>
            {
                clock.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 90f) * 12f);
                minute.localRotation = Quaternion.Euler(0, 0, -t * 1440f);
                hour.localRotation = Quaternion.Euler(0, 0, -t * 120f);
                loser.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 70f) * 6f);
                if (t >= popT)
                {
                    popT += 0.3f;
                    ctx.Start(PunishFx.Popup(ctx.Stage, "따르릉!", feet + new Vector2(Random.Range(-220f, 220f), loser.Size + Random.Range(40f, 260f)), Palette.Gold, 48));
                }
            });
            loser.Rt.localRotation = Quaternion.identity;
            clock.localRotation = Quaternion.identity;

            ctx.Start(PunishFx.Popup(ctx.Stage, "땡!!", feet + new Vector2(0, 460), Palette.Red, 84));
            yield return PunishFx.Crush(ctx, clock, new Vector2(220, 220), 1, clock.anchoredPosition.y);
        }
    }
}
