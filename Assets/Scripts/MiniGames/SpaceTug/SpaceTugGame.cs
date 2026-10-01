using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Spacebar tug-of-war: every press pulls the rope toward you. Drag the flag past your goal line to win,
    /// or be ahead when the timer runs out.
    /// </summary>
    public class SpaceTugGame : MiniGameBase
    {
        public override string Title => "스페이스바 줄다리기";
        public override string Rules => "버튼을 미친 듯이 연타해서 줄을 내 쪽으로 끌어오세요!\n깃발이 내 쪽 결승선을 넘으면 승리.\n15초가 지나면 깃발이 더 가까운 쪽이 승리!";
        public override string ControlsText => "P1   Space 연타\nP2   Enter 연타";
        public override string OnlineControlsText => "Space (또는 Enter) 연타!";

        const float Goal = 300f;
        const float Step = 7f;
        const float TimeLimit = 15f;
        const float GroundY = -300f;
        const float RopeY = GroundY + 95f;
        const float HandX = 380f;

        float offset; // negative = toward P1 (left)
        float timeLeft = TimeLimit;
        readonly int[] presses = new int[2];
        readonly float[] cpuTimer = new float[2];

        RectTransform rig, flag;
        readonly CharacterView[] views = new CharacterView[2];
        readonly RectTransform[] infoPanels = new RectTransform[2];
        readonly Text[] countTexts = new Text[2];
        readonly RectTransform[] keyCaps = new RectTransform[2];
        Text timerText;

        protected override void Build()
        {
            Background();

            timerText = UIKit.Label("Timer", Root, TimeLimit.ToString("0.0"), 72, Color.white);
            timerText.rectTransform.Place(new Vector2(0, 300), new Vector2(300, 100));

            // ground + lines
            UIKit.Img("Ground", Root, Palette.Panel).rectTransform.Place(new Vector2(0, GroundY - 110), new Vector2(1920, 220));
            UIKit.Img("Center", Root, Color.white.WithAlpha(0.5f)).rectTransform.Place(new Vector2(0, RopeY), new Vector2(6, 260));
            for (int p = 0; p < 2; p++)
            {
                var line = UIKit.Img("GoalLine", Root, Players[p].Color, UIKit.Rounded);
                line.rectTransform.Place(new Vector2((p == 0 ? -1 : 1) * Goal, RopeY), new Vector2(12, 260));
            }

            // everything that moves with the rope
            rig = UIKit.Rect("Rig", Root);
            rig.Place(Vector2.zero, new Vector2(10, 10));
            UIKit.Img("Rope", rig, Palette.Hex("#C8964B"), UIKit.Rounded).rectTransform.Place(new Vector2(0, RopeY), new Vector2(HandX * 2 + 40, 18));
            flag = UIKit.Img("Flag", rig, Palette.Red, UIKit.Rounded).rectTransform;
            flag.Place(new Vector2(0, RopeY - 40), new Vector2(26, 80));

            for (int p = 0; p < 2; p++)
            {
                float side = p == 0 ? -1 : 1;
                views[p] = CharacterView.Create(rig, Players[p].Character, 200);
                views[p].Rt.anchoredPosition = new Vector2(side * (HandX + 90), GroundY);
                views[p].Rt.localRotation = Quaternion.Euler(0, 0, side * -8); // leaning back

                infoPanels[p] = PlayerPanel(p, 560, 250, 170);
                infoPanels[p].anchoredPosition = new Vector2(side * 560, 170);
                countTexts[p] = UIKit.Label("Count", infoPanels[p], "0", 90, Color.white);
                countTexts[p].rectTransform.Place(new Vector2(0, -10), new Vector2(400, 110));

                var cap = UIKit.Img("Key", infoPanels[p], Palette.PanelLight, UIKit.Rounded).rectTransform;
                cap.Place(new Vector2(0, -90), new Vector2(p == 0 ? 300 : 200, 56));
                UIKit.Label("Label", cap, p == 0 || Online ? "SPACE 연타!" : "ENTER 연타!", 26, Color.white).rectTransform.Stretch();
                keyCaps[p] = cap;
            }
            Apply();
        }

        void Apply()
        {
            rig.anchoredPosition = new Vector2(offset, 0);
        }

        void Update()
        {
            if (!Running) return;

            timeLeft -= Time.deltaTime;
            timerText.text = Mathf.Max(0, timeLeft).ToString("0.0");
            timerText.color = timeLeft < 3f ? Palette.Red : Color.white;

            for (int p = 0; p < 2; p++)
            {
                bool press = false;
                if (IsCpuHere(p))
                {
                    cpuTimer[p] -= Time.deltaTime;
                    press = cpuTimer[p] <= 0f;
                    if (press) cpuTimer[p] = 1f / Random.Range(6.5f, 9f);
                }
                else if (IsHumanHere(p)) press = InConfirm(p);

                if (press) Emit(ActPull, p);
            }
            Apply();

            if (!IsAuthority) return;

            // host streams the authoritative rope position to the client
            if (Online && (syncTimer -= Time.deltaTime) <= 0f)
            {
                syncTimer = 0.05f;
                Emit(ActOffset, -1, 0, offset);
            }

            if (offset <= -Goal) DeclareWinner(0);
            else if (offset >= Goal) DeclareWinner(1);
            else if (timeLeft <= 0f)
            {
                if (Mathf.Abs(offset) > 0.01f) DeclareWinner(offset < 0 ? 0 : 1);
                else if (presses[0] != presses[1]) DeclareWinner(presses[0] > presses[1] ? 0 : 1);
                else DeclareWinner(Random.Range(0, 2));
            }
        }

        const int ActPull = 1, ActOffset = 2;
        float syncTimer;

        protected override void OnAction(GameAction act)
        {
            if (act.Kind == ActOffset)
            {
                if (!IsAuthority) offset = act.F;
                return;
            }
            if (act.Kind != ActPull || act.Player < 0 || act.Player > 1 || WinnerIndex >= 0) return;

            int p = act.Player;
            presses[p]++;
            if (IsAuthority) offset += p == 0 ? -Step : Step;
            countTexts[p].text = presses[p].ToString();
            views[p].Pulse(new Vector3(1.12f, 0.88f, 1f), 0.12f);
            StartCoroutine(Tween.Pop(keyCaps[p], 0.85f, 0.12f));
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                bool won = p == WinnerIndex;
                views[p].SetFace(won ? CharacterView.Face.Happy : CharacterView.Face.Hurt);
                Stamp(infoPanels[p], won, -10);
            }
            // winner yanks the rope all the way
            float from = offset;
            float to = WinnerIndex == 0 ? -Goal - 60 : Goal + 60;
            StartCoroutine(Tween.Run(0.35f, t => { offset = Mathf.Lerp(from, to, t); Apply(); }, Ease.OutQuad));
        }

        // ---------- Punishment: the winner rushes in and mashes the loser (연타!), then an uppercut ----------

        static readonly string[] HitWords = { "퍽!", "팍!", "퍽퍽!", "빡!", "연타!" };

        public override IEnumerator Punish(PunishContext ctx)
        {
            var winner = ctx.Winner;
            var loser = ctx.Loser;
            var home = winner.Rt.anchoredPosition;
            var feet = ctx.LoserFeet;
            float dir = Mathf.Sign(feet.x - home.x); // winner → loser
            var spot = new Vector2(feet.x - dir * 190f, feet.y);

            winner.SetFace(CharacterView.Face.Happy);
            yield return winner.HopTo(spot, 0.35f, 2, 30f);

            // two fists in the winner's color, jabbing alternately
            var fists = new RectTransform[2];
            for (int i = 0; i < 2; i++)
            {
                var f = UIKit.Img("Fist", ctx.Stage, Color.Lerp(winner.Def.Body, Color.black, 0.15f), UIKit.Circle);
                fists[i] = f.rectTransform;
                fists[i].Place(FistRest(spot, dir, i), new Vector2(70, 70));
            }

            float jab = 0.09f;
            for (int n = 0; n < 16; n++)
            {
                int i = n % 2;
                var rest = FistRest(spot, dir, i);
                var target = new Vector2(feet.x - dir * 40f, feet.y + Random.Range(80f, 170f));
                yield return Tween.Run(jab * 0.5f, t => fists[i].anchoredPosition = Vector2.Lerp(rest, target, t));
                loser.SetFace(CharacterView.Face.Hurt);
                loser.Hit();
                winner.Pulse(new Vector3(1.08f, 0.94f, 1f), 0.08f);
                if (n % 2 == 0)
                    ctx.Start(PunishFx.Popup(ctx.Stage, HitWords[Random.Range(0, HitWords.Length)],
                        new Vector2(feet.x + Random.Range(-120f, 120f), feet.y + Random.Range(160f, 300f)), Palette.Gold, 52));
                if (n % 4 == 3) ctx.Start(ctx.Shake(8f, 0.12f));
                yield return Tween.Run(jab * 0.5f, t => fists[i].anchoredPosition = Vector2.Lerp(target, rest, t));
                jab = Mathf.Max(0.05f, jab * 0.93f); // speeds up
            }

            // wind-up + uppercut: loser flies up spinning and crash-lands
            var up = fists[0];
            var low = new Vector2(feet.x - dir * 60f, feet.y + 20f);
            yield return Tween.Run(0.25f, t => up.anchoredPosition = Vector2.Lerp(FistRest(spot, dir, 0), low, t), Ease.OutQuad);
            up.sizeDelta = new Vector2(110, 110);
            ctx.Start(Tween.Run(0.15f, t => up.anchoredPosition = Vector2.Lerp(low, new Vector2(feet.x, feet.y + 320f), t)));
            ctx.Start(PunishFx.Popup(ctx.Stage, "어퍼컷!!", new Vector2(feet.x, feet.y + 360f), Palette.Red, 84));
            ctx.Start(ctx.Shake(20f, 0.3f));
            loser.Idle = false;
            yield return Tween.Run(0.5f, t =>
            {
                loser.Rt.anchoredPosition = new Vector2(feet.x, feet.y + Mathf.Sin(t * Mathf.PI * 0.5f) * 700f);
                loser.Rt.localRotation = Quaternion.Euler(0, 0, -dir * 720f * t);
            }, Ease.OutQuad);

            foreach (var f in fists) Destroy(f.gameObject);
            ctx.Start(winner.HopTo(home, 0.4f, 2, 30f));
            yield return new WaitForSeconds(0.2f);

            yield return Tween.Run(0.35f, t =>
            {
                loser.Rt.anchoredPosition = new Vector2(feet.x, feet.y + (1 - t * t) * 700f);
                loser.Rt.localRotation = Quaternion.Euler(0, 0, -dir * 720f * (1 + t));
            });
            loser.Rt.anchoredPosition = feet;
            loser.Rt.localRotation = Quaternion.identity;
            loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
            ctx.Start(ctx.Shake(26f, 0.4f));
            ctx.Start(PunishFx.Popup(ctx.Stage, "쿵!", new Vector2(feet.x, feet.y + 140f), Palette.Red, 80));
            yield return new WaitForSeconds(0.8f);
            loser.Idle = true;
            yield return loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack);
        }

        static Vector2 FistRest(Vector2 spot, float dir, int i) => spot + new Vector2(dir * 95f, 110f + i * 40f);
    }
}
