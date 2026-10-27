using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>CPS click battle: most clicks in 5 seconds wins. Ties go to whoever reached the count first.</summary>
    public class CpsGame : MiniGameBase
    {
        public override string Title => "CPS 클릭 대결";
        public override string Rules => "5초 동안 최대한 많이 클릭하세요!\nCPS(초당 클릭 수)가 더 높은 사람이 승리!";
        public override string ControlsText => "마우스 왼쪽 클릭 연타!";

        const float Duration = 5f;
        const int ActClick = 1, ActFinal = 2;

        float elapsed;
        readonly int[] clicks = new int[2];
        readonly float[] lastClickAt = new float[2];
        readonly float[] cpuTimer = new float[2];
        readonly bool[] finalSent = new bool[2];
        readonly bool[] hasFinal = new bool[2];
        readonly float[] finalLast = new float[2];

        readonly RectTransform[] panels = new RectTransform[2];
        readonly RectTransform[] targets = new RectTransform[2];
        readonly Text[] countTexts = new Text[2], cpsTexts = new Text[2];
        RectTransform timeFill;
        Text timeText;

        protected override void Build()
        {
            Background();

            var barBg = UIKit.Img("TimeBg", Root, new Color(0, 0, 0, 0.4f), UIKit.Rounded).rectTransform;
            barBg.Place(new Vector2(0, 360), new Vector2(1500, 24));
            timeFill = UIKit.Img("TimeFill", barBg, Palette.Gold, UIKit.Rounded).rectTransform;
            timeText = UIKit.Label("Time", Root, Duration.ToString("0.00"), 44, Color.white);
            timeText.rectTransform.Place(new Vector2(0, 310), new Vector2(300, 60));
            SetTime(1f);

            for (int p = 0; p < 2; p++)
            {
                var panel = PlayerPanel(p, 660, 760, -100);
                panels[p] = panel;

                countTexts[p] = UIKit.Label("Count", panel, "0", 150, Color.white);
                countTexts[p].rectTransform.Place(new Vector2(0, 210), new Vector2(500, 180));
                cpsTexts[p] = UIKit.Label("Cps", panel, "0.0 CPS", 40, Palette.Muted);
                cpsTexts[p].rectTransform.Place(new Vector2(0, 100), new Vector2(500, 60));

                var target = UIKit.Img("Target", panel, Players[p].Color, UIKit.Circle).rectTransform;
                target.Place(new Vector2(0, -130), new Vector2(320, 320));
                UIKit.Img("Inner", target, Color.white.WithAlpha(0.25f), UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(220, 220));
                UIKit.Label("Label", target, "CLICK!", 56, Color.white).rectTransform.Stretch();
                targets[p] = target;

                UIKit.Label("Hint", panel, IsHumanHere(p) ? "왼쪽 클릭" : IsCpuHere(p) ? "CPU" : "상대", 26, Palette.Muted, style: FontStyle.Normal)
                    .rectTransform.Place(new Vector2(0, -340), new Vector2(400, 40));
            }
        }

        void SetTime(float ratio)
        {
            timeFill.anchorMin = new Vector2(0.5f - ratio / 2f, 0);
            timeFill.anchorMax = new Vector2(0.5f + ratio / 2f, 1);
            timeFill.offsetMin = timeFill.offsetMax = Vector2.zero;
            timeFill.gameObject.SetActive(ratio > 0.001f);
        }

        void Update()
        {
            if (!Running) return;

            elapsed += Time.deltaTime;
            float left = Mathf.Max(0, Duration - elapsed);
            SetTime(left / Duration);
            timeText.text = left.ToString("0.00");

            for (int p = 0; p < 2; p++)
            {
                cpsTexts[p].text = $"{clicks[p] / Mathf.Clamp(elapsed, 0.2f, Duration):0.0} CPS";
                if (!IsLocal(p) || finalSent[p]) continue;

                // each PC times its own player's 5 seconds, so latency never eats clicks
                if (elapsed >= Duration)
                {
                    finalSent[p] = true;
                    Emit(ActFinal, p, clicks[p], lastClickAt[p]);
                    continue;
                }

                bool click = false;
                if (IsCpuHere(p))
                {
                    cpuTimer[p] -= Time.deltaTime;
                    click = cpuTimer[p] <= 0f;
                    if (click) cpuTimer[p] = 1f / Random.Range(6.5f, 9.5f);
                }
                else click = InClick(p);

                if (click)
                {
                    lastClickAt[p] = elapsed;
                    Emit(ActClick, p);
                }
            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;

            if (act.Kind == ActClick)
            {
                if (hasFinal[p]) return;
                clicks[p]++;
                countTexts[p].text = clicks[p].ToString();
                StartCoroutine(Tween.Pop(targets[p], 0.88f, 0.1f));
                StartCoroutine(Ripple(p));
            }
            else if (act.Kind == ActFinal)
            {
                hasFinal[p] = true;
                clicks[p] = act.A;
                finalLast[p] = act.F;
                countTexts[p].text = clicks[p].ToString();

                if (IsAuthority && hasFinal[0] && hasFinal[1])
                {
                    if (clicks[0] != clicks[1]) DeclareWinner(clicks[0] > clicks[1] ? 0 : 1);
                    else DeclareWinner(finalLast[0] <= finalLast[1] ? 0 : 1);
                }
            }
        }

        IEnumerator Ripple(int p)
        {
            var ring = UIKit.Img("Ripple", panels[p], Players[p].Color.WithAlpha(0.5f), UIKit.Circle);
            ring.rectTransform.Place(targets[p].anchoredPosition, new Vector2(320, 320));
            ring.transform.SetSiblingIndex(targets[p].GetSiblingIndex());
            yield return Tween.Run(0.35f, t =>
            {
                ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(320, 460, t);
                ring.color = Players[p].Color.WithAlpha(0.5f * (1 - t));
            }, Ease.OutQuad);
            Destroy(ring.gameObject);
        }

        protected override void OnFinish()
        {
            timeText.text = "TIME UP!";
            for (int p = 0; p < 2; p++)
            {
                cpsTexts[p].text = $"{clicks[p] / Duration:0.0} CPS";
                cpsTexts[p].color = Color.white;
                Stamp(panels[p], p == WinnerIndex, -130);
            }
        }

        // ---------- Punishment: a giant mouse clicks the loser over and over, then double-clicks them flat ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var size = new Vector2(220, 320);
            var mouse = UIKit.Img("GiantMouse", ctx.Stage, Palette.Hex("#E9ECF5"), UIKit.Rounded);
            var rt = mouse.rectTransform;
            UIKit.Img("Split", rt, Palette.Hex("#8C93B8")).rectTransform.Place(new Vector2(0, 90), new Vector2(6, 140));
            UIKit.Img("Seam", rt, Palette.Hex("#8C93B8")).rectTransform.Place(new Vector2(0, 20), new Vector2(220, 6));
            UIKit.Img("Wheel", rt, Palette.Ink, UIKit.Rounded).rectTransform.Place(new Vector2(0, 100), new Vector2(26, 56));

            float x = ctx.LoserFeet.x;
            float head = ctx.LoserHeadY;
            rt.Place(new Vector2(x, 800), size, pivot: new Vector2(0.5f, 0f));
            yield return Tween.Run(0.35f, t => rt.anchoredPosition = new Vector2(x, Mathf.Lerp(800, head + 90, t)), Ease.OutQuad);

            for (int i = 0; i < 8; i++)
            {
                yield return Tween.Run(0.05f, t => rt.anchoredPosition = new Vector2(x, Mathf.Lerp(head + 90, head - 20, t)));
                ctx.Loser.SetFace(CharacterView.Face.Hurt);
                ctx.Loser.Hit();
                ctx.Start(PunishFx.Popup(ctx.Stage, "CLICK!", new Vector2(x + Random.Range(-220f, 220f), head + Random.Range(-40f, 120f)), Palette.Gold, 48));
                yield return Tween.Run(0.07f, t => rt.anchoredPosition = new Vector2(x, Mathf.Lerp(head - 20, head + 90, t)));
            }

            ctx.Start(PunishFx.Popup(ctx.Stage, "더블클릭!", new Vector2(x, head + 260), Palette.Red, 72));
            yield return PunishFx.Crush(ctx, rt, size, 2, head + 90);
        }
    }
}
