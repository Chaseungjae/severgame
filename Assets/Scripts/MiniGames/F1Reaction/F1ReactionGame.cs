using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// F1 start-light reaction duel. Five red lights come on one by one, then all go out after a random hold.
    /// Fastest press after lights-out wins the heat; pressing early is a jump start and gives the heat away.
    /// Best of 3.
    ///
    /// Online: the host starts each heat (with the random hold time). Each PC runs the lights itself and measures
    /// its own player's reaction against its own lights-out moment, so latency does not affect the result.
    /// The host compares the reports and announces the heat winner.
    /// </summary>
    public class F1ReactionGame : MiniGameBase
    {
        public override string Title => "F1 반응 속도";
        public override string Rules => "빨간 불 5개가 하나씩 켜진 뒤, 모두 꺼지는 순간 누르세요!\n불이 꺼지기 전에 누르면 부정출발 → 상대 득점.\n3판 2선승!";
        public override string ControlsText => "Space 또는 마우스 왼쪽 클릭";

        const int WinsNeeded = 2;
        const float ResponseWindow = 1.2f;

        const int ActHeatStart = 1;   // system: A = heat number, F = hold time
        const int ActReport = 2;      // player: A = heat * 10 + code, F = reaction seconds
        const int ActHeatResult = 3;  // system: A = winner (-1 = replay), F = 1 if decided by a jump start
        const int CodeReact = 0, CodeJump = 1, CodeNone = 2;

        static readonly Color LightOff = Palette.Hex("#3A1418");
        static readonly Color LightOn = Palette.Hex("#FF2A2A");

        readonly Image[,] lights = new Image[5, 2];
        readonly RectTransform[] panels = new RectTransform[2];
        readonly Text[] reactionTexts = new Text[2];
        readonly Image[,] pips = new Image[2, WinsNeeded];
        readonly int[] wins = new int[2];
        Text statusText, heatText;

        // per-heat state
        int heat;
        bool heatResolved;
        readonly int[] reportCode = new int[2];
        readonly float[] reportTime = new float[2];
        readonly bool[] reported = new bool[2];
        readonly bool[] localReported = new bool[2];
        readonly float[] cpuJumpAt = new float[2];
        Coroutine localHeat;

        protected override void Build()
        {
            Background();

            heatText = UIKit.Label("Heat", Root, "", 34, Palette.Muted);
            heatText.rectTransform.Place(new Vector2(0, 370), new Vector2(600, 50));

            var gantry = UIKit.Img("Gantry", Root, Palette.Hex("#111325"), UIKit.Rounded).rectTransform;
            gantry.Place(new Vector2(0, 190), new Vector2(820, 290));
            for (int i = 0; i < 5; i++)
            {
                var unit = UIKit.Img($"Unit{i}", gantry, Palette.Hex("#23263F"), UIKit.Rounded).rectTransform;
                unit.Place(new Vector2(-320 + i * 160, 0), new Vector2(130, 250));
                for (int r = 0; r < 2; r++)
                {
                    var l = UIKit.Img("Light", unit, LightOff, UIKit.Circle);
                    l.rectTransform.Place(new Vector2(0, 55 - r * 110), new Vector2(92, 92));
                    lights[i, r] = l;
                }
            }

            statusText = UIKit.Label("Status", Root, "", 64, Color.white);
            statusText.rectTransform.Place(new Vector2(0, -25), new Vector2(1400, 90));
            UIKit.Shadow(statusText, 5f);

            for (int p = 0; p < 2; p++)
            {
                var panel = PlayerPanel(p, 660, 330, -330);
                panels[p] = panel;
                reactionTexts[p] = UIKit.Label("Reaction", panel, "--- ms", 76, Color.white);
                reactionTexts[p].rectTransform.Place(new Vector2(0, -5), new Vector2(560, 100));
                for (int w = 0; w < WinsNeeded; w++)
                {
                    var pip = UIKit.Img("Pip", panel, Color.white.WithAlpha(0.15f), UIKit.Circle);
                    pip.rectTransform.Place(new Vector2((w - (WinsNeeded - 1) / 2f) * 60, -110), new Vector2(40, 40));
                    pips[p, w] = pip;
                }
            }
        }

        void SetLights(int count)
        {
            for (int i = 0; i < 5; i++)
                for (int r = 0; r < 2; r++)
                    lights[i, r].color = i < count ? LightOn : LightOff;
        }

        bool HumanPressed(int p) => IsHumanHere(p) && (InConfirm(p) || InClick(p));

        // ---------- authority: runs the heats ----------

        protected override void OnBegin()
        {
            if (IsAuthority) StartCoroutine(RunHeats());
        }

        IEnumerator RunHeats()
        {
            int n = 0;
            while (wins[0] < WinsNeeded && wins[1] < WinsNeeded)
            {
                n++;
                Emit(ActHeatStart, -1, n, Random.Range(0.4f, 2.6f));
                while (!heatResolved)
                {
                    if (Online && Net.Disconnected) yield break;
                    yield return null;
                }
                yield return new WaitForSeconds(1.8f);
            }
            DeclareWinner(wins[0] >= WinsNeeded ? 0 : 1);
        }

        void TryResolve()
        {
            if (heatResolved) return;
            for (int p = 0; p < 2; p++)
                if (reported[p] && reportCode[p] == CodeJump)
                {
                    Emit(ActHeatResult, -1, 1 - p, 1f);
                    return;
                }
            if (!reported[0] || !reported[1]) return;

            bool r0 = reportCode[0] == CodeReact, r1 = reportCode[1] == CodeReact;
            int winner;
            if (!r0 && !r1) winner = -1;
            else if (!r0) winner = 1;
            else if (!r1) winner = 0;
            else winner = reportTime[0] <= reportTime[1] ? 0 : 1;
            Emit(ActHeatResult, -1, winner, 0f);
        }

        // ---------- everyone: applies actions ----------

        protected override void OnAction(GameAction act)
        {
            switch (act.Kind)
            {
                case ActHeatStart:
                    heat = act.A;
                    heatResolved = false;
                    for (int p = 0; p < 2; p++)
                    {
                        reported[p] = localReported[p] = false;
                        reactionTexts[p].text = "--- ms";
                        reactionTexts[p].color = Color.white;
                    }
                    heatText.text = $"HEAT {heat}";
                    if (localHeat != null) StopCoroutine(localHeat);
                    localHeat = StartCoroutine(LocalHeat(act.F));
                    break;

                case ActReport:
                {
                    int p = act.Player;
                    if (p < 0 || p > 1 || act.A / 10 != heat || reported[p] || heatResolved) return;
                    reported[p] = true;
                    reportCode[p] = act.A % 10;
                    reportTime[p] = act.F;
                    ShowReport(p);
                    if (IsAuthority) TryResolve();
                    break;
                }

                case ActHeatResult:
                    if (heatResolved) return;
                    heatResolved = true;
                    if (localHeat != null) StopCoroutine(localHeat);
                    SetLights(0);
                    ShowHeatResult(act.A, act.F > 0.5f);
                    break;
            }
        }

        void ShowReport(int p)
        {
            var t = reactionTexts[p];
            switch (reportCode[p])
            {
                case CodeReact:
                    t.text = $"{Mathf.RoundToInt(reportTime[p] * 1000)} ms";
                    break;
                case CodeJump:
                    t.text = "JUMP START";
                    t.color = Palette.Red;
                    break;
                default:
                    t.text = "너무 느림!";
                    t.color = Palette.Muted;
                    break;
            }
            StartCoroutine(Tween.Pop(t.transform, 1.3f, 0.2f));
        }

        void ShowHeatResult(int winner, bool byJump)
        {
            if (winner < 0)
            {
                statusText.text = "둘 다 반응 없음 — 다시!";
                statusText.color = Palette.Muted;
                return;
            }
            if (byJump)
            {
                statusText.text = $"{Players[1 - winner].Tag} 부정출발!";
                statusText.color = Palette.Red;
            }
            else
            {
                statusText.text = $"{Players[winner].Tag} 승리!";
                statusText.color = Players[winner].Color;
            }
            StartCoroutine(Tween.Pop(statusText.transform, 1.6f, 0.3f));

            wins[winner] = Mathf.Min(WinsNeeded, wins[winner] + 1);
            var pip = pips[winner, wins[winner] - 1];
            pip.color = Players[winner].Color;
            StartCoroutine(Tween.Pop(pip.transform, 2f, 0.3f));
        }

        // ---------- each PC: lights + measuring its own players ----------

        void Report(int p, int code, float seconds)
        {
            if (localReported[p]) return;
            localReported[p] = true;
            Emit(ActReport, p, heat * 10 + code, seconds);
        }

        IEnumerator LocalHeat(float hold)
        {
            SetLights(0);
            statusText.text = "준비...";
            statusText.color = Palette.Muted;
            for (int p = 0; p < 2; p++)
                cpuJumpAt[p] = IsCpuHere(p) && Random.value < 0.07f ? Random.Range(1f, 5f) : -1f;

            // light sequence: any press now is a jump start
            float clock = 0f;
            float[] marks = { 0.8f, 1.7f, 2.6f, 3.5f, 4.4f, 5.3f + hold };
            for (int step = 0; step < marks.Length; step++)
            {
                while (clock < marks[step])
                {
                    yield return null;
                    clock += Time.deltaTime;
                    for (int p = 0; p < 2; p++)
                    {
                        if (!IsLocal(p) || localReported[p]) continue;
                        bool early = IsCpuHere(p) ? cpuJumpAt[p] > 0 && clock >= cpuJumpAt[p] : HumanPressed(p);
                        if (early) Report(p, CodeJump, 0f);
                    }
                }
                if (step < 5) SetLights(step + 1);
            }

            // lights out!
            SetLights(0);
            statusText.text = "GO!";
            statusText.color = Palette.Green;
            StartCoroutine(Tween.Pop(statusText.transform, 1.8f, 0.25f));

            float go = Time.unscaledTime;
            var cpuReact = new[] { Random.Range(0.18f, 0.32f), Random.Range(0.18f, 0.32f) };
            while (Time.unscaledTime - go < ResponseWindow)
            {
                float since = Time.unscaledTime - go;
                for (int p = 0; p < 2; p++)
                {
                    if (!IsLocal(p) || localReported[p]) continue;
                    bool press = IsCpuHere(p) ? since >= cpuReact[p] : HumanPressed(p);
                    if (press) Report(p, CodeReact, since);
                }
                yield return null;
            }
            for (int p = 0; p < 2; p++)
                if (IsLocal(p)) Report(p, CodeNone, 0f);
        }

        protected override void OnFinish()
        {
            statusText.text = $"{Players[WinnerIndex].Tag} 최종 승리!";
            statusText.color = Palette.Gold;
            for (int p = 0; p < 2; p++) Stamp(panels[p], p == WinnerIndex, 0);
        }

        // ---------- Punishment: an F1 car runs the loser over (there and back) ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var car = BuildCar(ctx.Stage, Players[WinnerIndex].Color);
            float y = ctx.LoserFeet.y;
            float lx = ctx.LoserFeet.x;
            ctx.Start(PunishFx.Popup(ctx.Stage, "부릉부릉~", new Vector2(lx, ctx.LoserHeadY + 120), Palette.Gold, 60));
            yield return new WaitForSeconds(0.5f);

            for (int pass = 0; pass < 2; pass++)
            {
                float dir = pass == 0 ? 1f : -1f;
                car.localScale = new Vector3(dir, 1, 1);
                float from = -1400f * dir, to = 1400f * dir;
                bool hit = false, winnerJumped = false;
                float wx = ctx.Winner.Rt.anchoredPosition.x;
                yield return Tween.Run(0.75f, t =>
                {
                    float x = Mathf.Lerp(from, to, t);
                    car.anchoredPosition = new Vector2(x, y);
                    if (!winnerJumped && Mathf.Abs(x - wx) < 420f)
                    {
                        winnerJumped = true;
                        ctx.Start(ctx.Winner.Jump(220, 0.45f)); // winner hops over the car
                    }
                    if (!hit && (x - lx) * dir >= 0f)
                    {
                        hit = true;
                        ctx.Loser.SetFace(CharacterView.Face.Hurt);
                        ctx.Loser.BaseScale = new Vector3(1.6f, 0.2f, 1f);
                        ctx.Start(ctx.Shake(24f, 0.35f));
                        ctx.Start(PunishFx.Popup(ctx.Stage, "쾅!", new Vector2(lx, y + 170), Palette.Red, 80));
                        SkidMark(ctx, lx, y);
                    }
                });
                yield return new WaitForSeconds(0.35f);
            }

            yield return ctx.Loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
            Destroy(car.gameObject);
        }

        static void SkidMark(PunishContext ctx, float x, float y)
        {
            for (int i = 0; i < 2; i++)
            {
                var m = UIKit.Img("Skid", ctx.Stage, new Color(0, 0, 0, 0.45f), UIKit.Rounded);
                m.rectTransform.Place(new Vector2(x, y + 6 + i * 14), new Vector2(420, 10));
                m.transform.SetSiblingIndex(0);
                ctx.Start(FadeOutAndDestroy(m, 2.5f));
            }
        }

        static IEnumerator FadeOutAndDestroy(Graphic g, float duration)
        {
            yield return Tween.FadeGraphic(g, 0f, duration);
            Destroy(g.gameObject);
        }

        static RectTransform BuildCar(RectTransform parent, Color color)
        {
            var root = UIKit.Rect("F1Car", parent);
            root.Place(new Vector2(-1400, 0), new Vector2(460, 150), pivot: new Vector2(0.5f, 0f));
            var dark = Palette.Hex("#1A1C2C");
            var bottom = new Vector2(0.5f, 0f);

            void Part(string name, Color c, Sprite s, float x, float y, float w, float h) =>
                UIKit.Img(name, root, c, s).rectTransform.Place(new Vector2(x, y), new Vector2(w, h), bottom);

            Part("RearWing", dark, UIKit.Rounded, -205, 95, 34, 100);
            Part("Body", color, UIKit.Rounded, 0, 60, 430, 64);
            Part("Nose", color, UIKit.Rounded, 190, 48, 120, 34);
            Part("FrontWing", dark, UIKit.Rounded, 238, 30, 40, 56);
            Part("Cockpit", dark, UIKit.Circle, -10, 94, 110, 50);
            Part("Helmet", Palette.Gold, UIKit.Circle, -10, 108, 46, 46);
            Part("Number", Color.white, UIKit.Circle, 80, 60, 40, 40);
            foreach (var wx in new[] { -130f, 150f })
            {
                Part("Wheel", dark, UIKit.Circle, wx, 42, 84, 84);
                Part("Hub", Palette.Muted, UIKit.Circle, wx, 42, 30, 30);
            }
            return root;
        }
    }
}
