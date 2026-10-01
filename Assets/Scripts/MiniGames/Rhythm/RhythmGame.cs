using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Rhythm battle (osu!mania 4K style): notes fall down four lanes; hit the lane's key as a note crosses the line.
    /// Both players play the same generated song/chart at the same time; each PC judges its own player against its
    /// own audio clock, so latency doesn't matter. Higher score wins (tie: higher max combo).
    /// </summary>
    public class RhythmGame : MiniGameBase
    {
        public override string Title => "리듬 대결";
        public override string Rules => "떨어지는 노트가 판정선에 닿을 때 그 줄의 키를 누르세요! (4키 리듬게임)\nPERFECT 300 · GREAT 200 · GOOD 100 · MISS 0 — 같은 곡, 같은 채보\n점수가 더 높은 사람 승리 (동점이면 최대 콤보)";
        public override string ControlsText => "P1   D F K L\nP2   ← ↓ ↑ →";
        public override string OnlineControlsText => "D F K L (또는 방향키 ← ↓ ↑ →)";

        const float LaneW = 96f, HighwayH = 640f, JudgeY = -250f, ScrollSpeed = 820f;
        const float Perfect = 0.045f, Great = 0.09f, Good = 0.14f;
        const int ActJudge = 1, ActFinal = 2; // judge: A = note * 4 + grade (0 perfect .. 3 miss)

        static readonly Key[][] LocalKeys =
        {
            new[] { Key.D, Key.F, Key.K, Key.L },
            new[] { Key.LeftArrow, Key.DownArrow, Key.UpArrow, Key.RightArrow },
        };
        static readonly Key[] OnlineKeys = { Key.D, Key.F, Key.K, Key.L };
        static readonly string[][] KeyLabels =
        {
            new[] { "D", "F", "K", "L" },
            new[] { "←", "↓", "↑", "→" },
        };
        static readonly Color[] LaneColors =
        {
            Palette.Hex("#E9ECF5"), Palette.Hex("#4C8DFF"), Palette.Hex("#4C8DFF"), Palette.Hex("#E9ECF5"),
        };
        static readonly string[] GradeText = { "PERFECT", "GREAT", "GOOD", "MISS" };
        static readonly Color[] GradeColor = { Palette.Hex("#7CF7FF"), Palette.Green, Palette.Gold, Palette.Red };
        static readonly int[] GradeScore = { 300, 200, 100, 0 };

        List<RhythmSynth.Note> chart;
        AudioSource music, sfx;
        AudioClip click;
        double dspStart, lastDsp;
        float songTime;
        bool started;
        float endTime;

        class Lane
        {
            public RectTransform Panel, Highway;
            public Image[] Notes;
            public bool[] Judged;
            public Image[] Flash = new Image[4];
            public Text Score, Combo, Grade, Acc;
            public int Points, ComboCount, MaxCombo, Hits, Total;
            public float[] CpuOffset;
            public bool[] CpuMiss;
            public int NextCpu;
            public bool FinalSent, HasFinal;
        }

        readonly Lane[] lanes = new Lane[2];

        protected override void Build()
        {
            Background();
            chart = RhythmSynth.MakeChart(Seed);
            endTime = chart[chart.Count - 1].Time + 1.2f;

            music = gameObject.AddComponent<AudioSource>();
            music.clip = RhythmSynth.MakeSong(Seed, chart);
            music.playOnAwake = false;
            music.volume = 0.7f;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            click = RhythmSynth.MakeClick();

            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 760, 820, -70) };
                lanes[p] = l;
                l.Score = UIKit.Label("Score", l.Panel, "0", 52, Color.white, TextAnchor.MiddleLeft);
                l.Score.rectTransform.Place(new Vector2(-340, 300), new Vector2(340, 70), pivot: new Vector2(0, 0.5f));
                l.Acc = UIKit.Label("Acc", l.Panel, "100.00%", 32, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
                l.Acc.rectTransform.Place(new Vector2(340, 300), new Vector2(260, 60), pivot: new Vector2(1, 0.5f));

                l.Highway = UIKit.Img("Highway", l.Panel, Palette.Hex("#0E1022"), UIKit.Rounded).rectTransform;
                l.Highway.Place(new Vector2(0, -80), new Vector2(LaneW * 4 + 16, HighwayH));
                l.Highway.gameObject.AddComponent<RectMask2D>();
                for (int k = 0; k < 4; k++)
                {
                    float x = LaneX(k);
                    UIKit.Img("Lane", l.Highway, Color.white.WithAlpha(k % 2 == 0 ? 0.03f : 0.05f)).rectTransform.Place(new Vector2(x, 0), new Vector2(LaneW - 4, HighwayH));
                    l.Flash[k] = UIKit.Img("Flash", l.Highway, LaneColors[k].WithAlpha(0f));
                    l.Flash[k].rectTransform.Place(new Vector2(x, JudgeY + 90), new Vector2(LaneW - 4, 180));
                    var keyBox = UIKit.Img("Key", l.Highway, Palette.PanelLight, UIKit.Rounded);
                    keyBox.rectTransform.Place(new Vector2(x, JudgeY - 50), new Vector2(LaneW - 12, 56));
                    string label = Online ? KeyLabels[0][k] : KeyLabels[p][k];
                    UIKit.Label("K", keyBox.transform, IsCpuHere(p) ? "" : label, 30, Color.white).rectTransform.Stretch();
                }
                UIKit.Img("JudgeLine", l.Highway, Color.white.WithAlpha(0.85f)).rectTransform.Place(new Vector2(0, JudgeY), new Vector2(LaneW * 4, 6));

                l.Notes = new Image[chart.Count];
                l.Judged = new bool[chart.Count];
                for (int i = 0; i < chart.Count; i++)
                {
                    var n = UIKit.Img("Note", l.Highway, LaneColors[chart[i].Lane], UIKit.Rounded);
                    n.rectTransform.sizeDelta = new Vector2(LaneW - 10, 26);
                    n.gameObject.SetActive(false);
                    l.Notes[i] = n;
                }

                l.Combo = UIKit.Label("Combo", l.Highway, "", 64, Color.white.WithAlpha(0.85f));
                l.Combo.rectTransform.Place(new Vector2(0, 120), new Vector2(380, 90));
                l.Grade = UIKit.Label("Grade", l.Highway, "", 42, Color.white);
                l.Grade.rectTransform.Place(new Vector2(0, 40), new Vector2(380, 60));
                UIKit.Shadow(l.Grade, 4f);

                // CPU: hits with a little timing noise; misses become more likely as it gets dense
                var rng = new System.Random(Seed + 77 * (p + 1));
                l.CpuOffset = new float[chart.Count];
                l.CpuMiss = new bool[chart.Count];
                for (int i = 0; i < chart.Count; i++)
                {
                    float u1 = 1f - (float)rng.NextDouble(), u2 = (float)rng.NextDouble();
                    float gauss = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
                    l.CpuOffset[i] = Mathf.Clamp(gauss * 0.04f, -0.12f, 0.12f);
                    l.CpuMiss[i] = rng.NextDouble() < Mathf.Lerp(0.02f, 0.08f, i / (float)chart.Count);
                }
            }
        }

        static float LaneX(int k) => (k - 1.5f) * LaneW;

        protected override void OnBegin()
        {
            dspStart = AudioSettings.dspTime + 0.4;
            lastDsp = 0;
            songTime = -0.4f;
            music.PlayScheduled(dspStart);
            started = true;
        }

        /// <summary>Song position from the audio clock, smoothed between DSP buffer updates.</summary>
        void TickClock()
        {
            double dsp = AudioSettings.dspTime;
            if (dsp != lastDsp)
            {
                lastDsp = dsp;
                songTime = (float)(dsp - dspStart);
            }
            else songTime += Time.unscaledDeltaTime;
        }

        bool LaneDown(int p, int k)
        {
            if (!Online) return Controls.Pressed(LocalKeys[p][k]);
            return Controls.Pressed(OnlineKeys[k]) || Controls.Pressed(LocalKeys[1][k]);
        }

        void Update()
        {
            if (!started) return;
            TickClock();
            float t = songTime;

            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];

                // scroll notes
                for (int i = 0; i < chart.Count; i++)
                {
                    var n = l.Notes[i];
                    float y = JudgeY + (chart[i].Time - t) * ScrollSpeed;
                    bool show = !l.Judged[i] && y < HighwayH / 2 + 30 && y > -HighwayH / 2 - 30;
                    if (n.gameObject.activeSelf != show) n.gameObject.SetActive(show);
                    if (show) n.rectTransform.anchoredPosition = new Vector2(LaneX(chart[i].Lane), y);
                }
                for (int k = 0; k < 4; k++)
                {
                    var c = l.Flash[k].color;
                    if (c.a > 0f) l.Flash[k].color = c.WithAlpha(Mathf.Max(0f, c.a - Time.deltaTime * 3f));
                }

                if (!Running || !IsLocal(p) || l.FinalSent) continue;

                // misses: notes that slid past the window
                for (int i = 0; i < chart.Count; i++)
                {
                    if (chart[i].Time > t - Good) break;
                    if (!l.Judged[i]) Emit(ActJudge, p, i * 4 + 3, 0f);
                }

                if (IsCpuHere(p))
                {
                    while (l.NextCpu < chart.Count && chart[l.NextCpu].Time + l.CpuOffset[l.NextCpu] <= t)
                    {
                        int i = l.NextCpu++;
                        if (l.CpuMiss[i] || l.Judged[i]) continue;
                        Press(p, chart[i].Lane, t);
                    }
                }
                else
                {
                    for (int k = 0; k < 4; k++)
                        if (LaneDown(p, k)) Press(p, k, t);
                }

                if (t >= endTime)
                {
                    l.FinalSent = true;
                    Emit(ActFinal, p, l.Points, l.MaxCombo);
                }
            }
        }

        void Press(int p, int lane, float t)
        {
            var l = lanes[p];
            l.Flash[lane].color = LaneColors[lane].WithAlpha(0.35f);
            if (IsHumanHere(p)) sfx.PlayOneShot(click, 0.6f);

            // earliest unjudged note in this lane inside the window
            for (int i = 0; i < chart.Count; i++)
            {
                if (chart[i].Time > t + Good) break;
                if (l.Judged[i] || chart[i].Lane != lane) continue;
                float off = Mathf.Abs(chart[i].Time - t);
                if (off > Good) continue;
                int grade = off <= Perfect ? 0 : off <= Great ? 1 : 2;
                Emit(ActJudge, p, i * 4 + grade, chart[i].Time - t);
                return;
            }
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var l = lanes[p];

            if (act.Kind == ActJudge)
            {
                int i = act.A / 4, grade = act.A % 4;
                if (i < 0 || i >= chart.Count || l.Judged[i]) return;
                l.Judged[i] = true;
                l.Notes[i].gameObject.SetActive(false);
                l.Total++;
                l.Points += GradeScore[grade];
                if (grade < 3)
                {
                    l.Hits += 3 - grade;
                    l.ComboCount++;
                    l.MaxCombo = Mathf.Max(l.MaxCombo, l.ComboCount);
                    l.Flash[chart[i].Lane].color = GradeColor[grade].WithAlpha(0.5f);
                }
                else l.ComboCount = 0;

                l.Score.text = l.Points.ToString("N0");
                l.Acc.text = $"{(l.Total > 0 ? l.Hits / (3f * l.Total) * 100f : 100f):0.00}%";
                l.Combo.text = l.ComboCount >= 2 ? l.ComboCount.ToString() : "";
                l.Grade.text = GradeText[grade];
                l.Grade.color = GradeColor[grade];
                StartCoroutine(Tween.Pop(l.Grade.transform, 1.3f, 0.1f));
                if (l.ComboCount >= 2) StartCoroutine(Tween.Pop(l.Combo.transform, 1.15f, 0.08f));
            }
            else if (act.Kind == ActFinal)
            {
                l.HasFinal = true;
                l.Points = act.A;
                l.MaxCombo = Mathf.RoundToInt(act.F);
                l.Score.text = l.Points.ToString("N0");
                l.Combo.text = $"최대 {l.MaxCombo} 콤보";
                l.Combo.fontSize = 36;
                if (IsAuthority && lanes[0].HasFinal && lanes[1].HasFinal)
                {
                    var a = lanes[0];
                    var b = lanes[1];
                    if (a.Points != b.Points) DeclareWinner(a.Points > b.Points ? 0 : 1);
                    else DeclareWinner(a.MaxCombo != b.MaxCombo ? (a.MaxCombo > b.MaxCombo ? 0 : 1) : Seed % 2);
                }
            }
        }

        protected override void OnFinish()
        {
            StartCoroutine(FadeMusic());
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, -40);
        }

        void OnDestroy()
        {
            if (music != null && music.clip != null) Destroy(music.clip);
            if (click != null) Destroy(click);
        }

        IEnumerator FadeMusic()
        {
            float from = music.volume;
            yield return Tween.Run(0.8f, t => music.volume = Mathf.Lerp(from, 0f, t));
            music.Stop();
        }

        // ---------- Punishment: notes rain down on the loser to the beat — MISS! MISS! — then a giant MISS ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            float beat = 60f / RhythmSynth.Bpm / 2f;
            for (int i = 0; i < 10; i++)
            {
                int lane = i % 4;
                var note = UIKit.Img("Note", ctx.Stage, LaneColors[lane], UIKit.Rounded).rectTransform;
                float x = feet.x + (lane - 1.5f) * 70f;
                float head = feet.y + loser.Size * 0.9f;
                note.Place(new Vector2(x, 700), new Vector2(64, 24));
                ctx.Start(DropNote(ctx, note, x, head));
                yield return new WaitForSeconds(beat);
            }
            yield return new WaitForSeconds(0.4f);

            var block = UIKit.Img("Miss", ctx.Stage, Palette.Red, UIKit.Rounded);
            UIKit.Label("Text", block.transform, "MISS", 96, Color.white).rectTransform.Stretch();
            yield return PunishFx.Crush(ctx, block.rectTransform, new Vector2(360, 160));
        }

        static IEnumerator DropNote(PunishContext ctx, RectTransform note, float x, float head)
        {
            yield return Tween.Run(0.22f, t => note.anchoredPosition = new Vector2(x, Mathf.Lerp(700, head, t)), Ease.InQuad);
            ctx.Loser.SetFace(CharacterView.Face.Hurt);
            ctx.Loser.Hit();
            ctx.Start(PunishFx.Popup(ctx.Stage, "MISS", new Vector2(x, head + 70), Palette.Red, 40));
            var img = note.GetComponent<Image>();
            var vel = new Vector2(Random.Range(-300f, 300f), Random.Range(200f, 400f));
            float life = 0.6f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 1800f * dt;
                note.anchoredPosition += vel * dt;
                life -= dt;
                img.color = img.color.WithAlpha(Mathf.Clamp01(life / 0.3f));
                yield return null;
            }
            Destroy(note.gameObject);
        }
    }
}
