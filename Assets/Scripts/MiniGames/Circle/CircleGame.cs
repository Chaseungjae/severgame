using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Draw a circle around the dot with the mouse; the rounder it is, the higher the score. Two tries each,
    /// best try counts. On one PC players take turns (P1, P2, P1, P2); online both draw at the same time.
    /// </summary>
    public class CircleGame : MiniGameBase
    {
        public override string Title => "원을 그려봐요";
        public override string Rules => "가운데 점을 둘러싸도록 마우스로 최대한 완벽한 원을 그리세요!\n한 사람당 2번 — 더 높은 점수로 승부\n(한 PC에서는 P1 → P2 순서로 번갈아 그립니다)";
        public override string ControlsText => "마우스 왼쪽 버튼을 누른 채로 한 바퀴 그리기";
        public override string OnlineControlsText => ControlsText;

        const int Tries = 2;
        const float TryTime = 7f;
        const float CanvasSize = 640f;
        const float MinRadius = 60f;
        const int ActTry = 1; // player: A = try index, F = score (0..100)

        RectTransform canvas, strokeLayer;
        Text prompt, scoreText, timerText;
        readonly RectTransform[] panels = new RectTransform[2];
        readonly Text[][] tryTexts = { new Text[Tries], new Text[Tries] };
        readonly Text[] bestTexts = new Text[2];
        readonly float[,] scores = new float[2, Tries];
        readonly int[] triesDone = new int[2];

        protected override void Build()
        {
            Background();

            prompt = UIKit.Label("Prompt", Root, "", 44, Color.white);
            prompt.rectTransform.Place(new Vector2(0, 360), new Vector2(1000, 60));
            UIKit.Shadow(prompt, 4f);

            canvas = UIKit.Img("Canvas", Root, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
            canvas.Place(new Vector2(0, -40), new Vector2(CanvasSize, CanvasSize));
            canvas.gameObject.AddComponent<RectMask2D>();
            UIKit.Img("Dot", canvas, Color.white, UIKit.Circle).rectTransform.Place(Vector2.zero, new Vector2(18, 18));
            strokeLayer = UIKit.Rect("Strokes", canvas).Stretch();
            scoreText = UIKit.Label("Score", canvas, "", 96, Color.white);
            scoreText.rectTransform.Place(Vector2.zero, new Vector2(600, 140));
            UIKit.Shadow(scoreText, 6f, 0.6f);
            timerText = UIKit.Label("Timer", canvas, "", 34, Palette.Muted, TextAnchor.UpperRight);
            timerText.rectTransform.Place(new Vector2(-20, -14), new Vector2(160, 44), new Vector2(1, 1), new Vector2(1, 1));

            for (int p = 0; p < 2; p++)
            {
                panels[p] = PlayerPanel(p, 420, 500, -40);
                panels[p].anchoredPosition = new Vector2(p == 0 ? -700 : 700, -40);
                for (int i = 0; i < Tries; i++)
                {
                    UIKit.Label("TryLabel", panels[p], $"{i + 1}차", 28, Palette.Muted).rectTransform.Place(new Vector2(-110, 110 - i * 90), new Vector2(100, 40));
                    tryTexts[p][i] = UIKit.Label("Try", panels[p], "-", 52, Color.white);
                    tryTexts[p][i].rectTransform.Place(new Vector2(60, 110 - i * 90), new Vector2(220, 70));
                }
                UIKit.Label("BestLabel", panels[p], "최고 점수", 28, Palette.Gold).rectTransform.Place(new Vector2(0, -95), new Vector2(300, 40));
                bestTexts[p] = UIKit.Label("Best", panels[p], "-", 72, Palette.Gold);
                bestTexts[p].rectTransform.Place(new Vector2(0, -160), new Vector2(300, 90));
            }
        }

        protected override void OnBegin() => StartCoroutine(LocalTurns());

        /// <summary>Plays every try belonging to players on this PC.</summary>
        IEnumerator LocalTurns()
        {
            var order = new List<int>();
            for (int i = 0; i < Tries; i++)
                for (int p = 0; p < 2; p++)
                    if (IsLocal(p)) order.Add(p);

            var tryIndex = new int[2];
            foreach (int p in order)
            {
                yield return DoTry(p, tryIndex[p]);
                tryIndex[p]++;
            }
            prompt.text = Online ? "상대를 기다리는 중..." : "";
        }

        IEnumerator DoTry(int p, int tryIdx)
        {
            ClearStrokes();
            scoreText.text = "";
            prompt.text = Online ? $"{tryIdx + 1}번째 원을 그리세요!" : $"{Players[p].Tag} 차례 — {tryIdx + 1}번째 원";
            prompt.color = Players[p].Color;
            StartCoroutine(Tween.Pop(prompt.transform, 1.3f, 0.25f));
            yield return new WaitForSeconds(0.6f);

            var points = new List<Vector2>();
            if (IsCpuHere(p)) yield return CpuDraw(p, points);
            else yield return HumanDraw(p, points);

            float score = Evaluate(points, out string reason);
            scoreText.text = reason ?? $"{score:0.0}%";
            scoreText.color = reason != null ? Palette.Red : ScoreColor(score);
            StartCoroutine(Tween.Pop(scoreText.transform, 1.6f, 0.3f));
            Emit(ActTry, p, tryIdx, score);
            yield return new WaitForSeconds(1.4f);
        }

        IEnumerator HumanDraw(int p, List<Vector2> points)
        {
            float t = 0f;
            bool started = false;
            float runningMean = 0f;
            while (t < TryTime)
            {
                timerText.text = Mathf.CeilToInt(TryTime - t).ToString();
                var mouse = Mouse.current;
                bool down = mouse != null && mouse.leftButton.isPressed;
                var local = MouseIn(canvas);
                bool inside = Mathf.Abs(local.x) <= CanvasSize / 2 && Mathf.Abs(local.y) <= CanvasSize / 2;

                if (down && inside)
                {
                    started = true;
                    if (points.Count == 0 || (local - points[points.Count - 1]).sqrMagnitude > 16f)
                    {
                        points.Add(local);
                        runningMean += (local.magnitude - runningMean) / points.Count;
                        if (points.Count > 1) AddSegment(points[points.Count - 2], local, runningMean);
                        if (Sweep(points) >= 360f) break; // full lap: auto-finish
                    }
                }
                else if (started && !down) break; // released
                t += Time.deltaTime;
                yield return null;
            }
            timerText.text = "";
        }

        IEnumerator CpuDraw(int p, List<Vector2> points)
        {
            float radius = Random.Range(150f, 260f);
            float wobble = Random.Range(0.02f, 0.09f);
            float phase = Random.value * Mathf.PI * 2f, start = Random.value * Mathf.PI * 2f;
            float runningMean = 0f;
            const int steps = 90;
            for (int i = 0; i <= steps; i++)
            {
                float a = start + i / (float)steps * Mathf.PI * 2f;
                float r = radius * (1f + wobble * Mathf.Sin(a * 3f + phase) + Random.Range(-wobble, wobble) * 0.4f);
                var pt = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                points.Add(pt);
                runningMean += (pt.magnitude - runningMean) / points.Count;
                if (points.Count > 1) AddSegment(points[points.Count - 2], pt, runningMean);
                if (i % 3 == 0) yield return null;
            }
        }

        /// <summary>Total signed angle swept around the center, in degrees.</summary>
        static float Sweep(List<Vector2> pts)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.SignedAngle(pts[i - 1], pts[i]);
            return Mathf.Abs(total);
        }

        static float Evaluate(List<Vector2> pts, out string reason)
        {
            reason = null;
            if (pts.Count < 10 || Sweep(pts) < 300f)
            {
                reason = "원을 완성하세요!";
                return 0f;
            }
            float mean = 0f;
            foreach (var p in pts) mean += p.magnitude;
            mean /= pts.Count;
            if (mean < MinRadius)
            {
                reason = "너무 작아요!";
                return 0f;
            }
            float dev = 0f;
            foreach (var p in pts) dev += Mathf.Abs(p.magnitude - mean);
            dev /= pts.Count;
            return Mathf.Clamp(100f * (1f - dev / mean * 3f), 0f, 100f);
        }

        static Color ScoreColor(float score) =>
            score >= 90f ? Palette.Green : score >= 75f ? Palette.Gold : Palette.Red;

        void AddSegment(Vector2 a, Vector2 b, float mean)
        {
            float err = mean > 1f ? Mathf.Abs(b.magnitude - mean) / mean : 0f;
            var color = Color.Lerp(Palette.Green, Palette.Red, Mathf.Clamp01(err * 6f));
            var seg = UIKit.Img("Seg", strokeLayer, color, UIKit.Rounded);
            var d = b - a;
            seg.rectTransform.Place((a + b) / 2f, new Vector2(d.magnitude + 8f, 9f));
            seg.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        void ClearStrokes()
        {
            foreach (Transform c in strokeLayer) Destroy(c.gameObject);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Kind != ActTry || act.Player < 0 || act.Player > 1 || act.A < 0 || act.A >= Tries) return;
            int p = act.Player;
            scores[p, act.A] = act.F;
            triesDone[p] = Mathf.Max(triesDone[p], act.A + 1);
            tryTexts[p][act.A].text = $"{act.F:0.0}%";
            tryTexts[p][act.A].color = ScoreColor(act.F);
            float best = Best(p);
            bestTexts[p].text = $"{best:0.0}%";
            StartCoroutine(Tween.Pop(tryTexts[p][act.A].transform, 1.4f, 0.2f));

            if (IsAuthority && triesDone[0] >= Tries && triesDone[1] >= Tries)
            {
                float b0 = Best(0), b1 = Best(1);
                if (Mathf.Abs(b0 - b1) > 0.001f) DeclareWinner(b0 > b1 ? 0 : 1);
                else DeclareWinner(scores[0, 0] + scores[0, 1] >= scores[1, 0] + scores[1, 1] ? 0 : 1);
            }
        }

        float Best(int p)
        {
            float best = 0f;
            for (int i = 0; i < Tries; i++) best = Mathf.Max(best, scores[p, i]);
            return best;
        }

        protected override void OnFinish()
        {
            prompt.text = "";
            for (int p = 0; p < 2; p++) Stamp(panels[p], p == WinnerIndex, 0);
        }

        // ---------- Punishment: a red circle is traced around the loser, who then spins until dizzy ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var center = feet + new Vector2(0, loser.Size * 0.5f);
            float radius = loser.Size * 0.85f;
            var segs = new List<GameObject>();

            // trace a (slightly wobbly) circle around the loser
            Vector2 prev = center + Vector2.right * radius;
            for (int i = 1; i <= 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                var pt = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * (1f + 0.05f * Mathf.Sin(a * 5f));
                var seg = UIKit.Img("Lasso", ctx.Stage, Palette.Red, UIKit.Rounded);
                var d = pt - prev;
                seg.rectTransform.Place((prev + pt) / 2f, new Vector2(d.magnitude + 10f, 12f));
                seg.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                segs.Add(seg.gameObject);
                prev = pt;
                if (i % 2 == 0) yield return null;
            }
            ctx.Start(PunishFx.Popup(ctx.Stage, "빙글빙글~", center + new Vector2(0, radius + 90f), Palette.Gold, 60));

            // spin! (the lasso spins with them)
            loser.Idle = false;
            loser.SetFace(CharacterView.Face.Hurt);
            // spin around the body's center (pivot is normally at the feet)
            loser.Rt.pivot = new Vector2(0.5f, 0.5f);
            loser.Rt.anchoredPosition = feet + new Vector2(0, loser.Size * 0.5f);
            yield return Tween.Run(1.6f, t => loser.Rt.localRotation = Quaternion.Euler(0, 0, 1440f * t * t), Ease.Linear);
            loser.Rt.pivot = new Vector2(0.5f, 0f);
            loser.Rt.anchoredPosition = feet;
            loser.Rt.localRotation = Quaternion.identity;
            foreach (var s in segs) Destroy(s);

            // dizzy wobble
            ctx.Start(PunishFx.Popup(ctx.Stage, "어지러워@_@", feet + new Vector2(0, loser.Size + 90f), Palette.Red, 56));
            yield return Tween.Run(1.1f, t =>
            {
                loser.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 22f) * 18f * (1 - t));
                loser.Rt.anchoredPosition = feet + new Vector2(Mathf.Sin(t * 11f) * 30f * (1 - t), 0);
            });
            loser.Rt.localRotation = Quaternion.identity;
            loser.Rt.anchoredPosition = feet;
            loser.Idle = true;
        }
    }
}
