using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Instant memory (arealme brain-memory-game): digits flash one by one; enter them in REVERSE order.
    /// Each clear adds one more digit. A wrong answer costs a life (2 lives). Whoever gets further wins.
    /// Each player gets their own digits of the same length (so peeking at the other board doesn't help).
    /// </summary>
    public class MemoryGame : MiniGameBase
    {
        public override string Title => "순간 기억력";
        public override string Rules => "숫자가 하나씩 나타났다 사라집니다. 끝나면 거꾸로 입력하세요!\n예) 1 6 8 4 → 4 8 6 1 · 맞히면 한 자리씩 늘어남\n틀리면 목숨 -1 (2개) · 더 멀리 간 사람 승리";
        public override string ControlsText => "P1   숫자키 1~9 (키보드 위쪽 줄)\nP2   숫자패드 1~9\n마우스로 버튼 클릭도 가능";
        public override string OnlineControlsText => "숫자키 1~9 또는 마우스 클릭";

        const int Lives = 2;
        const int StartLength = 3;
        const float InputTime = 12f;
        const int ActClear = 1, ActFail = 2;

        class Lane
        {
            public RectTransform Panel;
            public Text Level, Display, Entry, Info;
            public Text[] Hearts = new Text[Lives];
            public Image[] Keys = new Image[9];
            public int Length = StartLength, Attempt, LivesLeft = Lives, Cleared;
            public int[] Sequence;
            public readonly StringBuilder Typed = new StringBuilder();
            public bool Accepting, Out;
            public float InputLeft;
        }

        readonly Lane[] lanes = new Lane[2];
        readonly SurvivalJudge judge = new SurvivalJudge();

        protected override void Build()
        {
            Background();
            for (int p = 0; p < 2; p++)
            {
                var l = new Lane { Panel = PlayerPanel(p, 700, 820, -70) };
                lanes[p] = l;
                l.Level = UIKit.Label("Level", l.Panel, "", 40, Color.white, TextAnchor.MiddleLeft);
                l.Level.rectTransform.Place(new Vector2(-320, 300), new Vector2(300, 56), pivot: new Vector2(0, 0.5f));
                for (int h = 0; h < Lives; h++)
                {
                    var heart = UIKit.Label("Heart", l.Panel, "♥", 44, Palette.Red);
                    heart.rectTransform.Place(new Vector2(250 + h * 50, 300), new Vector2(50, 50));
                    l.Hearts[h] = heart;
                }

                var screen = UIKit.Img("Screen", l.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                screen.Place(new Vector2(0, 170), new Vector2(620, 170));
                l.Display = UIKit.Label("Display", screen, "", 120, Palette.Gold);
                l.Display.rectTransform.Stretch();
                l.Entry = UIKit.Label("Entry", l.Panel, "", 46, Color.white);
                l.Entry.rectTransform.Place(new Vector2(0, 50), new Vector2(620, 60));
                l.Info = UIKit.Label("Info", l.Panel, "", 30, Palette.Muted);
                l.Info.rectTransform.Place(new Vector2(0, -2), new Vector2(620, 44));

                // keypad 7 8 9 / 4 5 6 / 1 2 3 (numpad layout)
                for (int d = 1; d <= 9; d++)
                {
                    int col = (d - 1) % 3, row = 2 - (d - 1) / 3;
                    var key = UIKit.Img($"K{d}", l.Panel, Palette.PanelLight, UIKit.Rounded);
                    key.rectTransform.Place(new Vector2((col - 1) * 130, -110 - row * 105), new Vector2(118, 94));
                    key.raycastTarget = true;
                    UIKit.Label("D", key.transform, d.ToString(), 48, Color.white).rectTransform.Stretch();
                    int digit = d, player = p;
                    key.gameObject.AddComponent<KeyClick>().OnClick = () => { if (IsHumanHere(player)) Press(player, digit); };
                    l.Keys[d - 1] = key;
                }
                RefreshLevel(l);
            }
        }

        void RefreshLevel(Lane l) => l.Level.text = $"{l.Length}자리";

        /// <summary>Digits for a player's attempt: same length for both players, different digits per player.</summary>
        int[] MakeSequence(int p, int length, int attempt)
        {
            var rng = new System.Random(Seed + p * 100003 + length * 131 + attempt * 7);
            var seq = new int[length];
            for (int i = 0; i < length; i++)
            {
                int d;
                do d = rng.Next(1, 10); while (i > 0 && d == seq[i - 1]);
                seq[i] = d;
            }
            return seq;
        }

        protected override void OnBegin()
        {
            for (int p = 0; p < 2; p++)
                if (IsLocal(p)) StartCoroutine(Show(p));
        }

        /// <summary>Flash the digits (local players only; the opponent's digits are never shown).</summary>
        IEnumerator Show(int p)
        {
            var l = lanes[p];
            l.Accepting = false;
            l.Typed.Clear();
            l.Entry.text = "";
            l.Info.text = "잘 보세요!";
            l.Info.color = Palette.Muted;
            l.Sequence = MakeSequence(p, l.Length, l.Attempt);
            yield return new WaitForSeconds(0.7f);

            float on = Mathf.Max(0.35f, 0.7f - l.Length * 0.03f);
            foreach (int d in l.Sequence)
            {
                l.Display.text = d.ToString();
                StartCoroutine(Tween.Pop(l.Display.transform, 1.3f, 0.15f));
                yield return new WaitForSeconds(on);
                l.Display.text = "";
                yield return new WaitForSeconds(0.18f);
            }

            l.Display.text = "?";
            l.Info.text = "거꾸로 입력!";
            l.Info.color = Palette.Gold;
            l.InputLeft = InputTime;
            l.Accepting = true;
            if (IsCpuHere(p)) StartCoroutine(CpuAnswer(p));
        }

        IEnumerator CpuAnswer(int p)
        {
            var l = lanes[p];
            bool right = Random.value < Mathf.Clamp01(1.02f - (l.Length - StartLength) * 0.09f);
            int wrongAt = right ? -1 : Random.Range(0, l.Length);
            for (int i = 0; i < l.Length && l.Accepting; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.25f, 0.45f));
                int d = l.Sequence[l.Length - 1 - i];
                if (i == wrongAt) d = d % 9 + 1;
                Press(p, d);
            }
        }

        void Update()
        {
            if (!Running) return;
            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (!IsLocal(p) || !l.Accepting) continue;
                l.InputLeft -= Time.deltaTime;
                l.Info.text = $"거꾸로 입력! ({Mathf.CeilToInt(l.InputLeft)})";
                if (l.InputLeft <= 0f)
                {
                    Resolve(p, false);
                    continue;
                }
                if (IsHumanHere(p))
                {
                    int d = Controls.Digit(Online ? -1 : p);
                    if (d > 0) Press(p, d);
                }
            }
        }

        void Press(int p, int digit)
        {
            var l = lanes[p];
            if (!Running || !l.Accepting || !IsLocal(p)) return;
            StartCoroutine(Tween.Pop(l.Keys[digit - 1].transform, 0.85f, 0.12f));
            int pos = l.Typed.Length;
            l.Typed.Append(digit);
            l.Entry.text = string.Join(" ", l.Typed.ToString().ToCharArray());
            int expected = l.Sequence[l.Length - 1 - pos];
            if (digit != expected) Resolve(p, false);
            else if (l.Typed.Length == l.Length) Resolve(p, true);
        }

        void Resolve(int p, bool success)
        {
            var l = lanes[p];
            l.Accepting = false;
            if (success) Emit(ActClear, p, l.Length);
            else Emit(ActFail, p, l.LivesLeft - 1);
        }

        protected override void OnAction(GameAction act)
        {
            if (act.Player < 0 || act.Player > 1) return;
            int p = act.Player;
            var l = lanes[p];
            if (l.Out) return;

            if (act.Kind == ActClear)
            {
                l.Cleared = act.A - StartLength + 1;
                l.Length = act.A + 1;
                l.Attempt = 0;
                judge.Score[p] = l.Cleared;
                l.Display.text = "O";
                l.Display.color = Palette.Green;
                l.Info.text = "정답!";
                l.Info.color = Palette.Green;
            }
            else if (act.Kind == ActFail)
            {
                l.LivesLeft = Mathf.Max(0, act.A);
                l.Attempt++;
                for (int h = 0; h < Lives; h++) l.Hearts[h].color = h < l.LivesLeft ? Palette.Red : Color.white.WithAlpha(0.15f);
                l.Display.text = "X";
                l.Display.color = Palette.Red;
                l.Info.text = l.LivesLeft > 0 ? "틀렸어요!" : "탈락!";
                l.Info.color = Palette.Red;
                if (l.LivesLeft == 0)
                {
                    l.Out = true;
                    judge.Out[p] = true;
                }
            }
            StartCoroutine(Tween.Pop(l.Display.transform, 1.4f, 0.2f));
            RefreshLevel(l);

            if (IsAuthority)
            {
                int w = judge.Check();
                if (w >= 0) DeclareWinner(w);
            }
            if (IsLocal(p) && !l.Out && WinnerIndex < 0) StartCoroutine(Next(p));
        }

        IEnumerator Next(int p)
        {
            yield return new WaitForSeconds(0.9f);
            lanes[p].Display.color = Palette.Gold;
            if (Running) StartCoroutine(Show(p));
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++)
            {
                lanes[p].Accepting = false;
                Stamp(lanes[p].Panel, p == WinnerIndex, -120);
            }
        }

        class KeyClick : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnClick;
            public void OnPointerDown(PointerEventData e) => OnClick?.Invoke();
        }

        // ---------- Punishment: numbers swirl around the loser's head, then a giant "?" lands on it ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var head = feet + new Vector2(0, loser.Size * 0.8f);
            var nums = new Text[8];
            for (int i = 0; i < nums.Length; i++)
            {
                nums[i] = UIKit.Label("Num", ctx.Stage, Random.Range(1, 10).ToString(), 56, Color.HSVToRGB(i / 8f, 0.6f, 1f));
                nums[i].rectTransform.sizeDelta = new Vector2(80, 80);
            }
            loser.SetFace(CharacterView.Face.Hurt);
            ctx.Start(PunishFx.Popup(ctx.Stage, "뭐였더라...?", head + new Vector2(0, 230), Palette.Gold, 60));
            yield return Tween.Run(1.6f, t =>
            {
                for (int i = 0; i < nums.Length; i++)
                {
                    float a = (i / (float)nums.Length + t * 1.5f) * Mathf.PI * 2f;
                    nums[i].rectTransform.anchoredPosition = head + new Vector2(Mathf.Cos(a) * 170f, Mathf.Sin(a) * 50f + 60f);
                    nums[i].color = nums[i].color.WithAlpha(1f - Mathf.Clamp01((t - 0.8f) * 5f));
                }
                loser.Rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 18f) * 8f);
            });
            loser.Rt.localRotation = Quaternion.identity;
            foreach (var n in nums) Destroy(n.gameObject);

            var q = UIKit.Img("GiantQ", ctx.Stage, Palette.Hex("#9B6BFF"), UIKit.Rounded);
            UIKit.Label("Q", q.transform, "?", 200, Color.white).rectTransform.Stretch();
            ctx.Start(PunishFx.Popup(ctx.Stage, "까먹었다!", feet + new Vector2(0, 430), Palette.Red, 76));
            yield return PunishFx.Crush(ctx, q.rectTransform, new Vector2(230, 260));
        }
    }
}
