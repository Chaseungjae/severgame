using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Count the moving balls. Colored balls bounce around for a few seconds, then disappear and both players
    /// answer how many balls of the asked color (or in total) there were. Closest answer wins the round,
    /// ties go to whoever locked in first. Best of 3. Ball sets come from the shared seed, so online both
    /// players see the same balls.
    /// </summary>
    public class BallCountGame : MiniGameBase
    {
        public override string Title => "공 개수 세기";
        public override string Rules => "움직이는 공 중에서 제시된 색의 공이 몇 개인지 세세요!\n공이 사라지면 개수를 입력 — 정답에 더 가까운 사람이 승리\n(같으면 먼저 입력한 사람) · 3판 2선승";
        public override string ControlsText => "숫자키로 개수 입력 · Enter(또는 Space) 확정 · Backspace 지우기";

        const int WinsNeeded = 2;
        const float WatchTime = 6f;
        const float AnswerTime = 7f;
        const float ArenaW = 1400f, ArenaH = 470f;
        const float BallSize = 54f;

        const int ActRoundStart = 1; // system: A = round number
        const int ActAnswer = 2;     // player: A = round * 1000 + value, F = lock time
        const int ActRoundResult = 3; // system: A = winner

        static readonly (string Name, Color Color)[] BallColors =
        {
            ("빨간", Palette.Hex("#FF5A5F")),
            ("파란", Palette.Hex("#4C8DFF")),
            ("초록", Palette.Hex("#3FB950")),
            ("노란", Palette.Hex("#FFC83D")),
        };

        class Ball
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Pos, Vel;
            public int ColorIndex;
        }

        class Picker
        {
            public RectTransform Panel;
            public Text Value, Status, Diff;
            public int Current = -1; // -1 = nothing typed yet
            public bool Locked;
            public Text Hint;
            public readonly Image[] Pips = new Image[WinsNeeded];
        }

        RectTransform arena, ballLayer;
        Text questionText, roundText, timerText;
        Image cover;
        readonly List<Ball> balls = new List<Ball>();
        readonly Picker[] pickers = new Picker[2];
        readonly int[] wins = new int[2];

        // per-round state
        int round, targetColor, correct;
        bool askAll, roundResolved;
        float phaseTime;
        readonly bool[] answered = new bool[2];
        readonly int[] answers = new int[2];
        readonly float[] lockTimes = new float[2];
        readonly float[] cpuLockAt = new float[2];
        readonly int[] cpuGuess = new int[2];
        Coroutine localRound;

        protected override void Build()
        {
            Background();

            roundText = UIKit.Label("Round", Root, "", 32, Palette.Muted);
            roundText.rectTransform.Place(new Vector2(0, 390), new Vector2(600, 44));
            questionText = UIKit.Label("Question", Root, "", 52, Color.white);
            questionText.rectTransform.Place(new Vector2(0, 335), new Vector2(1600, 70));
            UIKit.Shadow(questionText, 4f);

            arena = UIKit.Img("Arena", Root, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
            arena.Place(new Vector2(0, 45), new Vector2(ArenaW, ArenaH));
            arena.gameObject.AddComponent<RectMask2D>();
            ballLayer = UIKit.Rect("Balls", arena).Stretch();
            cover = UIKit.Img("Cover", arena, Palette.Hex("#15182E"), UIKit.Rounded);
            cover.rectTransform.Stretch();
            UIKit.Label("Q", cover.transform, "?", 220, Color.white.WithAlpha(0.12f)).rectTransform.Stretch();
            timerText = UIKit.Label("Timer", arena, "", 40, Color.white, TextAnchor.UpperRight);
            timerText.rectTransform.Place(new Vector2(-24, -14), new Vector2(200, 50), new Vector2(1, 1), new Vector2(1, 1));

            for (int p = 0; p < 2; p++)
            {
                var pk = new Picker { Panel = PlayerPanel(p, 640, 280, -355) };
                pickers[p] = pk;
                pk.Value = UIKit.Label("Value", pk.Panel, "", 96, Color.white);
                pk.Value.rectTransform.Place(new Vector2(0, -20), new Vector2(300, 120));
                pk.Hint = UIKit.Label("Hint", pk.Panel, "", 24, Palette.Muted, style: FontStyle.Normal);
                pk.Hint.rectTransform.Place(new Vector2(-215, -20), new Vector2(180, 90));
                pk.Status = UIKit.Label("Status", pk.Panel, "", 30, Palette.Muted);
                pk.Status.rectTransform.Place(new Vector2(0, -100), new Vector2(500, 44));
                pk.Diff = UIKit.Label("Diff", pk.Panel, "", 34, Palette.Gold);
                pk.Diff.rectTransform.Place(new Vector2(220, -20), new Vector2(200, 50));
                for (int w = 0; w < WinsNeeded; w++)
                {
                    var pip = UIKit.Img("Pip", pk.Panel, Color.white.WithAlpha(0.15f), UIKit.Circle);
                    pip.rectTransform.Place(new Vector2(250 + (w - 0.5f) * 44, 100), new Vector2(30, 30));
                    pk.Pips[w] = pip;
                }
            }
            ResetPickers(false);
        }

        void ResetPickers(bool active)
        {
            for (int p = 0; p < 2; p++)
            {
                var pk = pickers[p];
                pk.Current = -1;
                pk.Locked = false;
                pk.Value.text = !active ? "-" : IsHumanHere(p) ? "_" : "?"; // only your own number is visible
                pk.Value.color = Color.white;
                pk.Hint.text = active && IsHumanHere(p) ? "숫자 입력\nEnter 확정" : "";
                pk.Status.text = active ? (IsHumanHere(p) ? "개수를 입력하세요" : "상대 입력 중...") : "공을 세는 중...";
                pk.Status.color = Palette.Muted;
                pk.Diff.text = "";
            }
        }

        // ---------- authority: runs the rounds ----------

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
                while (!roundResolved)
                {
                    if (Online && Net.Disconnected) yield break;
                    yield return null;
                }
                yield return new WaitForSeconds(3.2f);
            }
            DeclareWinner(wins[0] >= WinsNeeded ? 0 : 1);
        }

        void TryResolve()
        {
            if (roundResolved || !answered[0] || !answered[1]) return;
            int d0 = Mathf.Abs(answers[0] - correct), d1 = Mathf.Abs(answers[1] - correct);
            int winner = d0 != d1 ? (d0 < d1 ? 0 : 1) : (lockTimes[0] <= lockTimes[1] ? 0 : 1);
            Emit(ActRoundResult, -1, winner);
        }

        // ---------- everyone ----------

        protected override void OnAction(GameAction act)
        {
            switch (act.Kind)
            {
                case ActRoundStart:
                    round = act.A;
                    roundResolved = false;
                    answered[0] = answered[1] = false;
                    if (localRound != null) StopCoroutine(localRound);
                    localRound = StartCoroutine(LocalRound());
                    break;

                case ActAnswer:
                {
                    int p = act.Player;
                    if (p < 0 || p > 1 || act.A / 1000 != round || answered[p] || roundResolved) return;
                    answered[p] = true;
                    answers[p] = act.A % 1000;
                    lockTimes[p] = act.F;
                    var pk = pickers[p];
                    pk.Locked = true;
                    pk.Status.text = "확정!";
                    pk.Status.color = Palette.Green;
                    if (!IsHumanHere(p)) pk.Value.text = "?"; // keep the opponent's answer hidden until the reveal
                    StartCoroutine(Tween.Pop(pk.Status.transform, 1.4f, 0.2f));
                    if (IsAuthority) TryResolve();
                    break;
                }

                case ActRoundResult:
                    if (roundResolved) return;
                    roundResolved = true;
                    if (localRound != null) StopCoroutine(localRound);
                    StartCoroutine(Reveal(act.A));
                    break;
            }
        }

        void SpawnBalls()
        {
            foreach (var b in balls) Destroy(b.Rt.gameObject);
            balls.Clear();

            var rng = new System.Random(Seed * 31 + round);
            float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);

            askAll = round == 2; // one round asks for the total
            targetColor = rng.Next(BallColors.Length);
            int total, count;
            if (askAll)
            {
                total = rng.Next(12, 21);
                count = total;
            }
            else
            {
                total = rng.Next(16, 27);
                count = rng.Next(6, Mathf.Min(15, total - 5));
            }
            correct = count;

            for (int i = 0; i < total; i++)
            {
                int color = askAll ? rng.Next(BallColors.Length)
                    : i < count ? targetColor
                    : (targetColor + 1 + rng.Next(BallColors.Length - 1)) % BallColors.Length;
                var img = UIKit.Img("Ball", ballLayer, BallColors[color].Color, UIKit.Circle);
                var b = new Ball
                {
                    Rt = img.rectTransform,
                    Img = img,
                    ColorIndex = color,
                    Pos = new Vector2(R(-ArenaW / 2 + 40, ArenaW / 2 - 40), R(-ArenaH / 2 + 40, ArenaH / 2 - 40)),
                };
                float angle = R(0, Mathf.PI * 2), speed = R(170f, 380f);
                b.Vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                b.Rt.Place(b.Pos, new Vector2(BallSize, BallSize));
                UIKit.Img("Shine", b.Rt, Color.white.WithAlpha(0.35f), UIKit.Circle).rectTransform.Place(new Vector2(-10, 10), new Vector2(16, 16));
                balls.Add(b);
            }
        }

        void MoveBalls(float dt)
        {
            float hx = ArenaW / 2 - BallSize / 2, hy = ArenaH / 2 - BallSize / 2;
            foreach (var b in balls)
            {
                b.Pos += b.Vel * dt;
                if (b.Pos.x < -hx || b.Pos.x > hx) { b.Vel.x = -b.Vel.x; b.Pos.x = Mathf.Clamp(b.Pos.x, -hx, hx); }
                if (b.Pos.y < -hy || b.Pos.y > hy) { b.Vel.y = -b.Vel.y; b.Pos.y = Mathf.Clamp(b.Pos.y, -hy, hy); }
                b.Rt.anchoredPosition = b.Pos;
            }
        }

        IEnumerator LocalRound()
        {
            roundText.text = $"ROUND {round}";
            SpawnBalls();
            ResetPickers(false);

            var c = BallColors[targetColor];
            questionText.text = askAll ? "공이 모두 몇 개인지 세세요!" : $"<color=#{ColorUtility.ToHtmlStringRGB(c.Color)}>{c.Name}</color> 공이 몇 개인지 세세요!";
            questionText.supportRichText = true;
            StartCoroutine(Tween.Pop(questionText.transform, 1.3f, 0.3f));

            // plan CPU answers: usually close, sometimes off by a couple
            var rng = new System.Random(Seed + round * 7);
            for (int p = 0; p < 2; p++)
            {
                if (!IsCpuHere(p)) continue;
                int err = rng.NextDouble() < 0.45 ? 0 : rng.Next(1, 4) * (rng.NextDouble() < 0.5 ? -1 : 1);
                cpuGuess[p] = Mathf.Max(0, correct + err);
                cpuLockAt[p] = 1.2f + (float)rng.NextDouble() * 2.5f;
            }

            // watch
            cover.gameObject.SetActive(false);
            phaseTime = 0f;
            while (phaseTime < WatchTime)
            {
                MoveBalls(Time.deltaTime);
                timerText.text = Mathf.CeilToInt(WatchTime - phaseTime).ToString();
                phaseTime += Time.deltaTime;
                yield return null;
            }

            // answer
            cover.gameObject.SetActive(true);
            questionText.text = askAll ? "공은 모두 몇 개였을까요?" : $"<color=#{ColorUtility.ToHtmlStringRGB(c.Color)}>{c.Name}</color> 공은 몇 개였을까요?";
            ResetPickers(true);
            phaseTime = 0f;
            while (phaseTime < AnswerTime)
            {
                timerText.text = Mathf.CeilToInt(AnswerTime - phaseTime).ToString();
                for (int p = 0; p < 2; p++)
                {
                    if (!IsLocal(p) || pickers[p].Locked || answered[p]) continue;
                    if (IsCpuHere(p)) UpdateCpuPicker(p);
                    else UpdateHumanPicker(p);
                }
                phaseTime += Time.deltaTime;
                yield return null;
            }
            timerText.text = "0";
            for (int p = 0; p < 2; p++)
                if (IsLocal(p) && !pickers[p].Locked) Lock(p); // time's up: submit whatever is shown
        }

        void UpdateHumanPicker(int p)
        {
            var pk = pickers[p];
            int d = Controls.AnyDigit();
            if (d >= 0)
            {
                pk.Current = pk.Current < 0 || pk.Current >= 10 ? d : pk.Current * 10 + d; // two digits max
                pk.Value.text = pk.Current.ToString();
                StartCoroutine(Tween.Pop(pk.Value.transform, 1.15f, 0.1f));
            }
            else if (Controls.Backspace && pk.Current >= 0)
            {
                pk.Current = pk.Current >= 10 ? pk.Current / 10 : -1;
                pk.Value.text = pk.Current < 0 ? "_" : pk.Current.ToString();
            }
            if (InConfirm(p) && pk.Current >= 0) Lock(p);
        }

        void UpdateCpuPicker(int p)
        {
            if (phaseTime < cpuLockAt[p]) return;
            pickers[p].Current = cpuGuess[p];
            Lock(p);
        }

        void Lock(int p)
        {
            var pk = pickers[p];
            if (pk.Locked) return;
            pk.Locked = true;
            pk.Hint.text = "";
            Emit(ActAnswer, p, round * 1000 + Mathf.Max(0, pk.Current), phaseTime); // nothing typed = 0
        }

        IEnumerator Reveal(int winner)
        {
            questionText.text = $"정답: {correct}개";
            questionText.color = Palette.Gold;
            StartCoroutine(Tween.Pop(questionText.transform, 1.5f, 0.3f));
            cover.gameObject.SetActive(false);
            timerText.text = "";

            for (int p = 0; p < 2; p++)
            {
                var pk = pickers[p];
                pk.Value.text = answers[p].ToString();
                StartCoroutine(Tween.Pop(pk.Value.transform, 1.4f, 0.25f));
                int diff = answers[p] - correct;
                pk.Diff.text = diff == 0 ? "정답!" : diff > 0 ? $"+{diff}" : diff.ToString();
                pk.Diff.color = diff == 0 ? Palette.Green : Palette.Red;
                pk.Status.text = p == winner ? "라운드 승리!" : "";
                pk.Status.color = Players[p].Color;
            }

            // highlight the counted balls with running numbers
            int k = 0;
            foreach (var b in balls)
            {
                bool counted = askAll || b.ColorIndex == targetColor;
                b.Img.color = counted ? BallColors[b.ColorIndex].Color : BallColors[b.ColorIndex].Color.WithAlpha(0.15f);
                if (!counted) continue;
                k++;
                var n = UIKit.Label("N", b.Rt, k.ToString(), 26, Color.white);
                n.rectTransform.Stretch();
                StartCoroutine(Tween.Pop(b.Rt, 1.5f, 0.2f));
                yield return new WaitForSeconds(Mathf.Min(0.08f, 1.2f / Mathf.Max(1, correct)));
            }

            wins[winner] = Mathf.Min(WinsNeeded, wins[winner] + 1);
            var pip = pickers[winner].Pips[wins[winner] - 1];
            pip.color = Players[winner].Color;
            StartCoroutine(Tween.Pop(pip.transform, 2f, 0.3f));
            questionText.color = Color.white;
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(pickers[p].Panel, p == WinnerIndex, 0);
        }

        // ---------- Punishment: an avalanche of balls buries the loser ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var feet = ctx.LoserFeet;
            var pile = new List<RectTransform>();
            ctx.Start(PunishFx.Popup(ctx.Stage, "몇 개게~?", feet + new Vector2(0, 330), Palette.Gold, 64));

            for (int i = 0; i < 46; i++)
            {
                var img = UIKit.Img("PileBall", ctx.Stage, BallColors[Random.Range(0, BallColors.Length)].Color, UIKit.Circle);
                var rt = img.rectTransform;
                float size = Random.Range(56f, 80f);
                var land = feet + new Vector2(Random.Range(-170f, 170f), Random.Range(10f, 60f) + (i / 46f) * 200f);
                rt.Place(new Vector2(land.x, 700), new Vector2(size, size));
                pile.Add(rt);
                ctx.Start(DropBall(rt, land, i == 20 ? ctx.Loser : null));
                yield return new WaitForSeconds(0.035f);
            }
            yield return new WaitForSeconds(0.9f);

            // burst out of the pile
            ctx.Loser.SetFace(CharacterView.Face.Hurt);
            ctx.Start(ctx.Shake(16f, 0.3f));
            ctx.Start(ctx.Loser.Jump(160, 0.5f));
            foreach (var rt in pile) ctx.Start(Scatter(rt, feet));
            ctx.Start(PunishFx.Popup(ctx.Stage, "푸하!", feet + new Vector2(0, 300), Palette.Red, 72));
            yield return new WaitForSeconds(1.0f);
        }

        static IEnumerator DropBall(RectTransform rt, Vector2 land, CharacterView hitReact)
        {
            float y0 = rt.anchoredPosition.y;
            yield return Tween.Run(0.35f, t => rt.anchoredPosition = new Vector2(land.x, Mathf.Lerp(y0, land.y, t)), Ease.InQuad);
            yield return Tween.Run(0.15f, t => rt.anchoredPosition = land + Vector2.up * (4f * 30f * t * (1 - t)));
            if (hitReact != null)
            {
                hitReact.SetFace(CharacterView.Face.Hurt);
                hitReact.Hit();
            }
        }

        static IEnumerator Scatter(RectTransform rt, Vector2 center)
        {
            var img = rt.GetComponent<Image>();
            var dir = (rt.anchoredPosition - center + Vector2.up * 60f).normalized;
            var vel = dir * Random.Range(600f, 1100f) + Vector2.up * Random.Range(200f, 500f);
            float life = 0.9f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1800f * dt;
                rt.anchoredPosition += vel * dt;
                life -= dt;
                img.color = img.color.WithAlpha(Mathf.Clamp01(life / 0.4f));
                yield return null;
            }
            Destroy(rt.gameObject);
        }
    }
}
