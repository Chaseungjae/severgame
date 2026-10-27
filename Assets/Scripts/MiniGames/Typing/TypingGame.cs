using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Typing race: both players type the same 10 words (Korean with a few English words) at the same time.
    /// First to finish wins; each PC times its own player and the host compares the times.
    /// One keyboard can't tell two typists apart, so if BOTH players are humans on the same PC the race falls back
    /// to taking turns (P1, then P2). Against a CPU or online it is always simultaneous.
    /// </summary>
    public class TypingGame : MiniGameBase
    {
        public override string Title => "타자 대결";
        public override string Rules => "같은 단어 10개를 동시에 입력하는 타자 레이스! (영어도 섞여 나와요)\n두 사람에게 똑같은 단어가 같은 순서로 나옵니다.\n먼저 다 치는 사람 승리 · 틀리면 지우고 다시";
        public override string ControlsText => "단어 입력 후 Enter 또는 Space\n한/영 전환은 자동 (한글 단어면 한글, 영어 단어면 영어로 입력돼요)";

        const int WordCount = 10;
        const float TimeLimit = 60f;
        const float Grace = 0.6f; // after the first finish, wait briefly for a faster time from the other PC
        const int ActProgress = 1, ActFinal = 2;

        static readonly string[] Korean =
        {
            "사과", "바나나", "고양이", "강아지", "자전거", "컴퓨터", "키보드", "햄버거", "떡볶이", "비행기",
            "우주선", "무지개", "코끼리", "냉장고", "선풍기", "도서관", "놀이터", "초콜릿", "아이스크림", "미니게임",
            "게임기", "눈사람", "피아노", "기차역", "해바라기", "호랑이", "거북이", "토마토", "운동장", "편의점",
            "텔레비전", "스마트폰", "축구공", "라면", "김치찌개", "불고기", "바다", "하늘", "구름", "번개",
        };

        static readonly string[] English =
        {
            "apple", "game", "speed", "winner", "banana", "keyboard", "party", "rocket",
            "pizza", "music", "happy", "tiger", "power", "click", "jump", "star",
        };

        class Lane
        {
            public RectTransform Panel;
            public Text Word, Next, Input, Progress, Time;
            public Image Box;
            public readonly StringBuilder Typed = new StringBuilder(); // CPU / remote text
            public readonly List<char> Keys = new List<char>();       // human keys (jamo or letters)
            public int Index;
            public float Elapsed;
            public bool Active, Done;
            public float CpuNext;
            public int CpuChar;
        }

        string[] words;
        readonly Lane[] lanes = new Lane[2];
        Text prompt;

        // final results (authority)
        readonly bool[] hasFinal = new bool[2];
        readonly int[] finalDone = new int[2];
        readonly float[] finalTime = new float[2];
        float graceUntil = -1f;

        // keyboard capture
        int typist = -1;
        bool hooked;

        bool TurnMode => IsHumanHere(0) && IsHumanHere(1);

        protected override void Build()
        {
            Background();
            var rng = new System.Random(Seed);
            var ko = new List<string>(Korean);
            var en = new List<string>(English);
            words = new string[WordCount];
            for (int i = 0; i < WordCount; i++)
            {
                bool english = i == 3 || i == 6 || i == 9;
                var pool = english ? en : ko;
                int k = rng.Next(pool.Count);
                words[i] = pool[k];
                pool.RemoveAt(k);
            }

            prompt = UIKit.Label("Prompt", Root, "", 40, Color.white);
            prompt.rectTransform.Place(new Vector2(0, 385), new Vector2(1200, 56));
            UIKit.Shadow(prompt, 4f);

            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 900, 740, -80) };
                l.Panel.anchoredPosition = new Vector2(p == 0 ? -470 : 470, -80);
                lanes[p] = l;

                l.Progress = UIKit.Label("Progress", l.Panel, $"0 / {WordCount}", 44, Color.white, TextAnchor.MiddleLeft);
                l.Progress.rectTransform.Place(new Vector2(-420, 280), new Vector2(300, 56), pivot: new Vector2(0, 0.5f));
                l.Time = UIKit.Label("Time", l.Panel, "0.0s", 36, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
                l.Time.rectTransform.Place(new Vector2(420, 280), new Vector2(300, 56), pivot: new Vector2(1, 0.5f));

                var card = UIKit.Img("Card", l.Panel, Palette.PanelLight, UIKit.Rounded).rectTransform;
                card.Place(new Vector2(0, 100), new Vector2(820, 250));
                l.Word = UIKit.Label("Word", card, words[0], 96, Color.white);
                l.Word.rectTransform.Place(new Vector2(0, 20), new Vector2(800, 130));
                l.Next = UIKit.Label("Next", card, "", 28, Palette.Muted, style: FontStyle.Normal);
                l.Next.rectTransform.Place(new Vector2(0, -85), new Vector2(800, 44));

                l.Box = UIKit.Img("Input", l.Panel, Palette.Hex("#15182E"), UIKit.Rounded);
                l.Box.rectTransform.Place(new Vector2(0, -130), new Vector2(760, 110));
                l.Input = UIKit.Label("Typed", l.Box.transform, "", 56, Color.white);
                l.Input.rectTransform.Stretch();
                l.Input.supportRichText = true;

                string who = IsCpuHere(p) ? "CPU" : IsHumanHere(p) ? "내 입력칸" : "상대";
                UIKit.Label("Who", l.Panel, who, 26, Palette.Muted, style: FontStyle.Normal).rectTransform.Place(new Vector2(0, -225), new Vector2(400, 40));
                ShowWord(l);
                RefreshInput(l);
            }
        }

        void ShowWord(Lane l)
        {
            l.Word.text = l.Index < WordCount ? words[l.Index] : "완료!";
            var sb = new StringBuilder();
            for (int k = l.Index + 1; k < Mathf.Min(WordCount, l.Index + 4); k++) sb.Append(words[k]).Append("   ");
            l.Next.text = sb.ToString();
        }

        void RefreshInput(Lane l)
        {
            bool mine = typist >= 0 && lanes[typist] == l;
            string caret = l.Active && !l.Done ? "<color=#9AA3C7>|</color>" : "";
            if (mine)
            {
                Hangul.Compose(l.Keys, out var done, out var composing);
                string comp = composing.Length > 0 ? $"<color=#FFC83D>{composing}</color>" : "";
                l.Input.text = done + comp + caret;
            }
            else l.Input.text = l.Typed + caret;
        }

        string HumanText(Lane l)
        {
            Hangul.Compose(l.Keys, out var done, out var composing);
            return done + composing;
        }

        // ---------- keyboard capture ----------
        // The Windows IME is switched off and Korean is composed in-game (see Hangul), picked automatically
        // from the current word, so nobody has to press 한/영.

        void Hook(bool on)
        {
            var kb = Keyboard.current;
            if (kb == null || on == hooked) return;
            hooked = on;
            if (on) kb.onTextInput += OnChar;
            else kb.onTextInput -= OnChar;
            kb.SetIMEEnabled(false);
        }

        void OnChar(char c)
        {
            if (typist < 0 || char.IsControl(c) || char.IsWhiteSpace(c)) return;
            var l = lanes[typist];
            if (!l.Active || l.Done || l.Index >= WordCount) return;
            bool korean = Hangul.IsKorean(words[l.Index]);
            bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            if (letter)
            {
                var kb = Keyboard.current;
                bool shift = kb != null && kb.shiftKey.isPressed;
                c = korean ? Hangul.ToJamo(c, shift) : char.ToLowerInvariant(c);
            }
            if (l.Keys.Count >= 30) return;
            l.Keys.Add(c);
            RefreshInput(l);
        }

        void OnDestroy() => Hook(false);

        // ---------- flow ----------

        protected override void OnBegin() => StartCoroutine(Flow());

        IEnumerator Flow()
        {
            if (TurnMode)
            {
                for (int p = 0; p < 2; p++)
                {
                    prompt.text = $"{Players[p].Tag} 차례 — 준비...";
                    prompt.color = Players[p].Color;
                    yield return new WaitForSeconds(1.0f);
                    StartLane(p);
                    while (!lanes[p].Done) yield return null;
                    yield return new WaitForSeconds(0.8f);
                }
            }
            else
            {
                prompt.text = "동시에 시작!";
                for (int p = 0; p < 2; p++) if (IsLocal(p)) StartLane(p);
            }
        }

        void StartLane(int p)
        {
            var l = lanes[p];
            l.Active = true;
            l.CpuNext = Random.Range(0.3f, 0.6f);
            if (IsHumanHere(p))
            {
                typist = p;
                l.Keys.Clear();
                Hook(true);
            }
            RefreshInput(l);
            StartCoroutine(Tween.Pop(l.Word.transform, 1.3f, 0.2f));
        }

        void Update()
        {
            if (!Running) return;

            if (IsAuthority && graceUntil > 0f && Time.time >= graceUntil) Decide(true);

            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (!IsLocal(p) || !l.Active || l.Done) continue;

                l.Elapsed += Time.deltaTime;
                l.Time.text = $"{l.Elapsed:0.0}s";
                if (l.Elapsed >= TimeLimit)
                {
                    Finish(p);
                    continue;
                }

                bool submit = p == typist ? HumanSubmit(l) : IsCpuHere(p) && CpuStep(l);
                if (!submit) continue;

                string entry = (p == typist ? HumanText(l) : l.Typed.ToString()).Trim();
                l.Typed.Clear();
                l.Keys.Clear();
                if (entry == words[l.Index])
                {
                    l.Index++;
                    Emit(ActProgress, p, l.Index);
                    if (l.Index >= WordCount) Finish(p);
                }
                else if (entry.Length > 0) StartCoroutine(WrongFlash(l));
                RefreshInput(l);
            }
        }

        bool HumanSubmit(Lane l)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            if (kb.backspaceKey.wasPressedThisFrame && l.Keys.Count > 0)
            {
                l.Keys.RemoveAt(l.Keys.Count - 1);
                RefreshInput(l);
            }
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
        }

        bool CpuStep(Lane l)
        {
            l.CpuNext -= Time.deltaTime;
            if (l.CpuNext > 0f) return false;
            string w = words[l.Index];
            if (l.CpuChar < w.Length)
            {
                l.Typed.Append(w[l.CpuChar++]);
                RefreshInput(l);
                l.CpuNext = Random.Range(0.14f, 0.26f);
                return false;
            }
            l.CpuChar = 0;
            l.CpuNext = Random.Range(0.15f, 0.3f);
            return true;
        }

        void Finish(int p)
        {
            var l = lanes[p];
            l.Done = true;
            if (p == typist)
            {
                Hook(false);
                typist = -1;
            }
            RefreshInput(l);
            Emit(ActFinal, p, l.Index, l.Elapsed);
        }

        IEnumerator WrongFlash(Lane l)
        {
            var home = l.Box.rectTransform.anchoredPosition;
            l.Box.color = Palette.Red.WithAlpha(0.6f);
            yield return Tween.Run(0.3f, t => l.Box.rectTransform.anchoredPosition = home + Vector2.right * Mathf.Sin(t * 40f) * 10f * (1 - t));
            l.Box.rectTransform.anchoredPosition = home;
            l.Box.color = Palette.Hex("#15182E");
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var l = lanes[p];
            if (act.Kind == ActProgress)
            {
                l.Index = act.A;
                l.Progress.text = $"{act.A} / {WordCount}";
                ShowWord(l);
                StartCoroutine(Tween.Pop(l.Progress.transform, 1.25f, 0.12f));
                StartCoroutine(Tween.Pop(l.Word.transform, 1.2f, 0.12f));
            }
            else if (act.Kind == ActFinal)
            {
                hasFinal[p] = true;
                finalDone[p] = act.A;
                finalTime[p] = act.F;
                l.Time.text = act.A >= WordCount ? $"{act.F:0.00}초" : "시간 초과";
                l.Time.color = Palette.Gold;
                if (!IsAuthority) return;
                if (hasFinal[0] && hasFinal[1]) Decide(false);
                else if (act.A >= WordCount && graceUntil < 0f && !TurnMode) graceUntil = Time.time + Grace;
            }
        }

        /// <summary>Authority: more words wins, then the faster time. After the grace period a lone finisher wins.</summary>
        void Decide(bool graceExpired)
        {
            graceUntil = -1f;
            if (hasFinal[0] && hasFinal[1])
            {
                if (finalDone[0] != finalDone[1]) DeclareWinner(finalDone[0] > finalDone[1] ? 0 : 1);
                else DeclareWinner(finalTime[0] <= finalTime[1] ? 0 : 1);
            }
            else if (graceExpired)
            {
                DeclareWinner(hasFinal[0] ? 0 : 1);
            }
        }

        protected override void OnFinish()
        {
            Hook(false);
            typist = -1;
            for (int p = 0; p < 2; p++)
            {
                lanes[p].Active = false;
                RefreshInput(lanes[p]);
                Stamp(lanes[p].Panel, p == WinnerIndex, 0);
            }
        }

        // ---------- Punishment: keycaps pelt the loser, then a giant ENTER key slams down ----------

        static readonly string[] Jamo = { "ㅋ", "ㅎ", "ㅠ", "ㄱ", "ㅁ", "A", "Z", "ㅋ" };

        public override IEnumerator Punish(PunishContext ctx)
        {
            for (int i = 0; i < 12; i++)
            {
                ctx.Start(ThrowKey(ctx, Jamo[i % Jamo.Length], i % 2 == 0 ? -1f : 1f));
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(0.5f);

            var key = UIKit.Img("GiantEnter", ctx.Stage, Palette.Hex("#8C93B8"), UIKit.Rounded);
            var top = UIKit.Img("Top", key.transform, Palette.Hex("#E9ECF5"), UIKit.Rounded).rectTransform;
            top.anchorMin = Vector2.zero;
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(10, 22);
            top.offsetMax = new Vector2(-10, -8);
            UIKit.Label("Label", top, "ENTER", 60, Palette.Ink).rectTransform.Stretch();
            ctx.Start(PunishFx.Popup(ctx.Stage, "엔터!!", ctx.LoserFeet + new Vector2(0, 420), Palette.Red, 72));
            yield return PunishFx.Crush(ctx, key.rectTransform, new Vector2(340, 200), 2);
        }

        static IEnumerator ThrowKey(PunishContext ctx, string letter, float side)
        {
            var cap = UIKit.Img("Key", ctx.Stage, Palette.Hex("#E9ECF5"), UIKit.Rounded);
            var label = UIKit.Label("L", cap.transform, letter, 40, Palette.Ink);
            label.rectTransform.Stretch();
            var rt = cap.rectTransform;
            var target = ctx.LoserFeet + new Vector2(Random.Range(-40f, 40f), Random.Range(90f, 180f));
            var start = new Vector2(side * 1100f, Random.Range(100f, 400f));
            rt.Place(start, new Vector2(70, 70));
            yield return Tween.Run(0.28f, t =>
            {
                rt.anchoredPosition = Vector2.Lerp(start, target, t) + Vector2.up * (4f * 120f * t * (1 - t));
                rt.localRotation = Quaternion.Euler(0, 0, 540f * t * side);
            });
            ctx.Loser.SetFace(CharacterView.Face.Hurt);
            ctx.Loser.Hit();
            var vel = new Vector2(-side * Random.Range(200f, 400f), Random.Range(300f, 600f));
            float life = 0.7f;
            while (life > 0f)
            {
                float dt = Time.deltaTime;
                vel.y -= 2000f * dt;
                rt.anchoredPosition += vel * dt;
                rt.Rotate(0, 0, side * 400f * dt);
                life -= dt;
                float a = Mathf.Clamp01(life / 0.35f);
                cap.color = Palette.Hex("#E9ECF5").WithAlpha(a);
                label.color = Palette.Ink.WithAlpha(a);
                yield return null;
            }
            Destroy(cap.gameObject);
        }
    }
}
