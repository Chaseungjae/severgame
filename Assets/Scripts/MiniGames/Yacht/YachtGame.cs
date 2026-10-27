using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Yacht dice: roll 5 dice up to 3 times, holding the ones you like between rolls. Both players roll at the
    /// same time; the bigger hand wins (야추 > 포카드 > 라지 스트레이트 > 풀하우스 > 스몰 스트레이트 > 트리플 > 투 페어 > 원 페어),
    /// equal hands compare the dice, then the total. A complete tie re-rolls. Each PC rolls its own player's dice
    /// and sends the result, so both screens show the same dice.
    /// </summary>
    public class YachtGame : MiniGameBase
    {
        public override string Title => "야추 다이스";
        public override string Rules => "주사위 5개를 최대 3번 굴려 가장 센 패를 만드세요!\n굴린 뒤 마음에 드는 주사위는 고정(HOLD)하고 나머지만 다시 굴릴 수 있어요.\n더 큰 패를 가진 사람 승리 · 운빨 100%";
        public override string ControlsText => "Space 굴리기 · 숫자키 1~5 또는 주사위 클릭으로 고정 · Enter 확정";

        const int DiceCount = 5, MaxRolls = 3;
        const float TimeLimit = 30f;
        const float RollAnim = 0.5f;
        const float DieSize = 110f;

        const int ActRoundStart = 1; // system: A = round
        const int ActRoll = 2;       // player: A = round << 20 | held << 15 | dice
        const int ActHold = 3;       // player: A = round << 20 | held
        const int ActDone = 4;       // player: A = round << 20 | dice
        const int ActResult = 5;     // system: A = winner, -1 = tie (re-roll)

        static readonly string Legend = "야추 > 포카드 > 라지 스트레이트 > 풀하우스 > 스몰 스트레이트 > 트리플 > 투 페어 > 원 페어";

        class Die
        {
            public RectTransform Rt;
            public Image Body;
            public Image[] Pips;
            public Text HoldLabel;
        }

        class Lane
        {
            public RectTransform Panel;
            public Die[] Dice = new Die[DiceCount];
            public Text Hand, Status, Rolls, Timer;
            public GameObject Buttons;
            public int[] Values = new int[DiceCount];
            public bool[] Held = new bool[DiceCount];
            public int RollsUsed;
            public bool Done, Animating;
            public float Elapsed, CpuNext;
            public Coroutine Anim;
        }

        readonly Lane[] lanes = new Lane[2];
        Text banner;
        int round;
        bool resolved;
        readonly bool[] done = new bool[2];
        readonly int[] finals = new int[2];

        // ---------- build ----------

        protected override void Build()
        {
            Background();
            banner = UIKit.Label("Banner", Root, "", 40, Color.white);
            banner.rectTransform.Place(new Vector2(0, 385), new Vector2(1400, 56));
            UIKit.Shadow(banner, 4f);
            UIKit.Label("Legend", Root, Legend, 24, Palette.Muted, style: FontStyle.Normal)
                .rectTransform.Place(new Vector2(0, -478), new Vector2(1600, 36));

            for (int p = 0; p < 2; p++)
            {
                int pi = p;
                var l = new Lane { Panel = PlayerPanel(p, 800, 720, -60) };
                l.Panel.anchoredPosition = new Vector2(p == 0 ? -470 : 470, -60);
                lanes[p] = l;

                l.Rolls = UIKit.Label("Rolls", l.Panel, "", 30, Palette.Muted, TextAnchor.MiddleLeft);
                l.Rolls.rectTransform.Place(new Vector2(-370, 270), new Vector2(360, 44), pivot: new Vector2(0, 0.5f));
                l.Timer = UIKit.Label("Timer", l.Panel, "", 30, Palette.Muted, TextAnchor.MiddleRight, FontStyle.Normal);
                l.Timer.rectTransform.Place(new Vector2(370, 270), new Vector2(200, 44), pivot: new Vector2(1, 0.5f));

                var tray = UIKit.Img("Tray", l.Panel, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
                tray.Place(new Vector2(0, 110), new Vector2(740, 230));
                for (int i = 0; i < DiceCount; i++)
                {
                    int di = i;
                    l.Dice[i] = MakeDie(tray, new Vector2((i - 2) * 140, 10), DieSize);
                    l.Dice[i].HoldLabel = UIKit.Label("Hold", tray, "", 24, Palette.Gold);
                    l.Dice[i].HoldLabel.rectTransform.Place(new Vector2((i - 2) * 140, -88), new Vector2(120, 32));
                    if (IsHumanHere(p))
                        l.Dice[i].Body.gameObject.AddComponent<DieClick>().OnClick = () => ToggleHold(pi, di);
                    SetFace(l.Dice[i], 0);
                }

                l.Hand = UIKit.Label("Hand", l.Panel, "", 56, Palette.Gold);
                l.Hand.rectTransform.Place(new Vector2(0, -60), new Vector2(760, 80));
                UIKit.Shadow(l.Hand, 4f);
                l.Status = UIKit.Label("Status", l.Panel, "", 30, Palette.Muted);
                l.Status.rectTransform.Place(new Vector2(0, -130), new Vector2(760, 44));

                if (IsHumanHere(p))
                {
                    var buttons = UIKit.Rect("Buttons", l.Panel);
                    buttons.Place(new Vector2(0, -240), new Vector2(700, 90));
                    UIKit.Button("Roll", buttons, "굴리기 (Space)", new Vector2(-170, 0), new Vector2(300, 80), 30, Palette.Hex("#3FB950"), () => Roll(pi));
                    UIKit.Button("Stop", buttons, "확정 (Enter)", new Vector2(170, 0), new Vector2(300, 80), 30, Palette.Hex("#4C8DFF"), () => Finish(pi));
                    l.Buttons = buttons.gameObject;
                }
                else
                {
                    UIKit.Label("Who", l.Panel, IsCpuHere(p) ? "CPU" : "상대", 26, Palette.Muted, style: FontStyle.Normal)
                        .rectTransform.Place(new Vector2(0, -240), new Vector2(300, 40));
                }
            }
        }

        static readonly Vector2[] PipSpots =
        {
            new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(1, 0),
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(0, 0),
        };

        // which of the 7 spots (TL, TR, ML, MR, BL, BR, C) are lit for each face
        static readonly int[][] FacePips =
        {
            new int[0],
            new[] { 6 },
            new[] { 0, 5 },
            new[] { 0, 6, 5 },
            new[] { 0, 1, 4, 5 },
            new[] { 0, 1, 4, 5, 6 },
            new[] { 0, 1, 2, 3, 4, 5 },
        };

        static Die MakeDie(Transform parent, Vector2 pos, float size)
        {
            var body = UIKit.Img("Die", parent, Color.white, UIKit.Rounded);
            body.rectTransform.Place(pos, new Vector2(size, size));
            body.raycastTarget = true;
            var d = new Die { Rt = body.rectTransform, Body = body, Pips = new Image[PipSpots.Length] };
            float off = size * 0.27f, pip = size * 0.19f;
            for (int k = 0; k < PipSpots.Length; k++)
            {
                d.Pips[k] = UIKit.Img("Pip", body.transform, Palette.Hex("#22253A"), UIKit.Circle);
                d.Pips[k].rectTransform.Place(PipSpots[k] * off, new Vector2(pip, pip));
                d.Pips[k].raycastTarget = false;
            }
            return d;
        }

        static void SetFace(Die d, int face)
        {
            foreach (var pip in d.Pips) pip.gameObject.SetActive(false);
            if (face <= 0) return;
            foreach (int k in FacePips[face])
            {
                d.Pips[k].gameObject.SetActive(true);
                d.Pips[k].color = face == 1 ? Palette.Red : Palette.Hex("#22253A");
            }
            if (face == 1) d.Pips[6].rectTransform.sizeDelta = Vector2.one * d.Rt.sizeDelta.x * 0.28f;
            else d.Pips[6].rectTransform.sizeDelta = Vector2.one * d.Rt.sizeDelta.x * 0.19f;
        }

        void ShowHolds(Lane l)
        {
            for (int i = 0; i < DiceCount; i++)
            {
                var d = l.Dice[i];
                bool held = l.Held[i] && !l.Done;
                d.Body.color = held ? Palette.Hex("#FFE7A3") : Color.white;
                d.Rt.anchoredPosition = new Vector2((i - 2) * 140, held ? 26 : 10);
                d.HoldLabel.text = held ? "HOLD" : (l.RollsUsed > 0 && !l.Done && l.Buttons != null ? (i + 1).ToString() : "");
                d.HoldLabel.color = held ? Palette.Gold : Palette.Muted;
            }
        }

        void ShowRolls(Lane l)
        {
            int left = MaxRolls - l.RollsUsed;
            l.Rolls.text = $"남은 기회  {new string('●', left)}{new string('○', MaxRolls - left)}";
        }

        // ---------- hands ----------

        static int Pack(int[] v)
        {
            int a = 0;
            for (int i = 0; i < DiceCount; i++) a |= (v[i] & 7) << (i * 3);
            return a;
        }

        static void Unpack(int a, int[] v)
        {
            for (int i = 0; i < DiceCount; i++) v[i] = (a >> (i * 3)) & 7;
        }

        /// <summary>Comparable hand strength: rank, then the dice that make the hand, then the total.</summary>
        public static int Score(int[] v, out string name)
        {
            var count = new int[7];
            int sum = 0;
            foreach (int x in v) { count[x]++; sum += x; }

            int four = 0, three = 0, highPair = 0, lowPair = 0, five = 0;
            for (int f = 6; f >= 1; f--)
            {
                if (count[f] == 5) five = f;
                else if (count[f] == 4) four = f;
                else if (count[f] == 3) three = f;
                else if (count[f] == 2) { if (highPair == 0) highPair = f; else lowPair = f; }
            }
            int run = 0, bestRun = 0, runHigh = 0;
            for (int f = 1; f <= 6; f++)
            {
                run = count[f] > 0 ? run + 1 : 0;
                if (run >= bestRun) { bestRun = run; runHigh = f; }
            }

            int rank, key;
            if (five > 0) { rank = 8; key = five; name = "야추!"; }
            else if (four > 0) { rank = 7; key = four; name = "포카드"; }
            else if (bestRun >= 5) { rank = 6; key = runHigh; name = "라지 스트레이트"; }
            else if (three > 0 && highPair > 0) { rank = 5; key = three * 10 + highPair; name = "풀하우스"; }
            else if (bestRun >= 4) { rank = 4; key = runHigh; name = "스몰 스트레이트"; }
            else if (three > 0) { rank = 3; key = three; name = "트리플"; }
            else if (lowPair > 0) { rank = 2; key = highPair * 10 + lowPair; name = "투 페어"; }
            else if (highPair > 0) { rank = 1; key = highPair; name = "원 페어"; }
            else { rank = 0; key = 0; name = "노 페어"; }
            return rank * 100000 + key * 100 + sum;
        }

        static int Rank(int score) => score / 100000;

        // ---------- authority ----------

        protected override void OnBegin()
        {
            if (IsAuthority) Emit(ActRoundStart, -1, 1);
        }

        IEnumerator AfterResult(int winner)
        {
            yield return new WaitForSeconds(winner < 0 ? 2.2f : 2.6f);
            if (winner < 0) Emit(ActRoundStart, -1, round + 1);
            else DeclareWinner(winner);
        }

        // ---------- everyone ----------

        protected override void OnAction(GameAction act)
        {
            int p = act.Player;
            int r = act.A >> 20, payload = act.A & 0xFFFFF;
            switch (act.Kind)
            {
                case ActRoundStart:
                    round = act.A;
                    resolved = false;
                    banner.text = round == 1 ? "주사위를 굴려 가장 센 패를 만드세요!" : "동점! 다시 굴립니다";
                    banner.color = Color.white;
                    StartCoroutine(Tween.Pop(banner.transform, 1.3f, 0.25f));
                    for (int q = 0; q < 2; q++)
                    {
                        var l = lanes[q];
                        done[q] = false;
                        l.Done = l.Animating = false;
                        l.RollsUsed = 0;
                        l.Elapsed = 0f;
                        l.CpuNext = Random.Range(0.8f, 1.4f);
                        for (int i = 0; i < DiceCount; i++) { l.Values[i] = 0; l.Held[i] = false; SetFace(l.Dice[i], 0); }
                        l.Hand.text = "";
                        l.Hand.color = Palette.Gold;
                        l.Status.text = IsHumanHere(q) ? "Space로 주사위를 굴리세요!" : "굴리는 중...";
                        l.Status.color = Palette.Muted;
                        if (l.Buttons != null) l.Buttons.SetActive(true);
                        ShowRolls(l);
                        ShowHolds(l);
                    }
                    break;

                case ActRoll:
                {
                    if (p < 0 || p > 1 || r != round || done[p]) return;
                    var l = lanes[p];
                    int held = payload >> 15;
                    for (int i = 0; i < DiceCount; i++) l.Held[i] = (held >> i & 1) == 1;
                    Unpack(payload & 0x7FFF, l.Values);
                    l.RollsUsed = Mathf.Min(MaxRolls, l.RollsUsed + 1);
                    ShowRolls(l);
                    ShowHolds(l);
                    if (l.Anim != null) StopCoroutine(l.Anim);
                    l.Anim = StartCoroutine(RollAnimation(p));
                    break;
                }

                case ActHold:
                {
                    if (p < 0 || p > 1 || r != round || done[p] || IsLocal(p)) return;
                    var l = lanes[p];
                    for (int i = 0; i < DiceCount; i++) l.Held[i] = (payload >> i & 1) == 1;
                    ShowHolds(l);
                    break;
                }

                case ActDone:
                {
                    if (p < 0 || p > 1 || r != round || done[p] || resolved) return;
                    done[p] = true;
                    finals[p] = payload;
                    var l = lanes[p];
                    l.Done = true;
                    Unpack(payload, l.Values);
                    if (!l.Animating) for (int i = 0; i < DiceCount; i++) SetFace(l.Dice[i], l.Values[i]);
                    ShowHolds(l);
                    if (l.Buttons != null) l.Buttons.SetActive(false);
                    l.Status.text = "확정!";
                    l.Status.color = Palette.Green;
                    StartCoroutine(Tween.Pop(l.Status.transform, 1.4f, 0.2f));
                    if (IsAuthority && done[0] && done[1])
                    {
                        var a = new int[DiceCount];
                        var b = new int[DiceCount];
                        Unpack(finals[0], a);
                        Unpack(finals[1], b);
                        int s0 = Score(a, out _), s1 = Score(b, out _);
                        Emit(ActResult, -1, s0 == s1 ? -1 : s0 > s1 ? 0 : 1);
                    }
                    break;
                }

                case ActResult:
                    if (resolved) return;
                    resolved = true;
                    StartCoroutine(Reveal(act.A));
                    if (IsAuthority) StartCoroutine(AfterResult(act.A));
                    break;
            }
        }

        IEnumerator RollAnimation(int p)
        {
            var l = lanes[p];
            l.Animating = true;
            l.Hand.text = "";
            float t = 0f, tick = 0f;
            while (t < RollAnim)
            {
                t += Time.deltaTime;
                tick -= Time.deltaTime;
                if (tick <= 0f)
                {
                    tick = 0.05f;
                    for (int i = 0; i < DiceCount; i++)
                    {
                        if (l.Held[i]) continue;
                        SetFace(l.Dice[i], Random.Range(1, 7));
                        l.Dice[i].Rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-25f, 25f) * (1f - t / RollAnim));
                    }
                }
                yield return null;
            }
            for (int i = 0; i < DiceCount; i++)
            {
                l.Dice[i].Rt.localRotation = Quaternion.identity;
                SetFace(l.Dice[i], l.Values[i]);
                if (!l.Held[i]) StartCoroutine(Tween.Pop(l.Dice[i].Rt, 1.2f, 0.15f));
            }
            int score = Score(l.Values, out var name);
            l.Hand.text = name;
            StartCoroutine(Tween.Pop(l.Hand.transform, 1.3f, 0.2f));
            if (Rank(score) >= 5)
            {
                Juice.Sparkle(l.Panel, new Vector2(0, -60), Palette.Gold, Rank(score) >= 7 ? 1.4f : 0.9f);
                if (Rank(score) == 8) Juice.Shake(0.4f);
            }
            if (IsHumanHere(p) && !l.Done)
                l.Status.text = l.RollsUsed < MaxRolls ? "고정할 주사위를 고르고 다시 굴리거나, Enter로 확정" : "";
            l.Animating = false;
            if (IsLocal(p) && l.RollsUsed >= MaxRolls) Finish(p); // out of rolls
        }

        IEnumerator Reveal(int winner)
        {
            while (lanes[0].Animating || lanes[1].Animating) yield return null;
            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                Unpack(finals[p], l.Values);
                for (int i = 0; i < DiceCount; i++) SetFace(l.Dice[i], l.Values[i]);
                Score(l.Values, out var name);
                l.Hand.text = name;
                l.Hand.color = winner < 0 ? Color.white : p == winner ? Palette.Green : Palette.Red;
                l.Status.text = winner < 0 ? "동점" : p == winner ? "더 센 패!" : "";
                l.Status.color = Players[p].Color;
                StartCoroutine(Tween.Pop(l.Hand.transform, 1.5f, 0.3f));
            }
            if (winner >= 0)
            {
                banner.text = $"{Players[winner].DisplayName} 승리!";
                banner.color = Players[winner].Color;
                Juice.Sparkle(lanes[winner].Panel, new Vector2(0, 110), Palette.Gold, 1.2f);
            }
            else banner.text = "완전히 같은 패! 다시 굴립니다";
        }

        // ---------- local players ----------

        void Roll(int p)
        {
            var l = lanes[p];
            if (!Running || !IsLocal(p) || l.Done || l.Animating || l.RollsUsed >= MaxRolls || resolved) return;
            int held = 0;
            var next = new int[DiceCount];
            for (int i = 0; i < DiceCount; i++)
            {
                bool keep = l.RollsUsed > 0 && l.Held[i];
                if (keep) held |= 1 << i;
                next[i] = keep ? l.Values[i] : Random.Range(1, 7);
            }
            Emit(ActRoll, p, round << 20 | held << 15 | Pack(next));
        }

        void ToggleHold(int p, int i)
        {
            var l = lanes[p];
            if (!Running || !IsLocal(p) || l.Done || l.Animating || l.RollsUsed == 0 || l.RollsUsed >= MaxRolls) return;
            l.Held[i] = !l.Held[i];
            ShowHolds(l);
            int mask = 0;
            for (int k = 0; k < DiceCount; k++) if (l.Held[k]) mask |= 1 << k;
            Emit(ActHold, p, round << 20 | mask);
        }

        void Finish(int p)
        {
            var l = lanes[p];
            if (!Running || !IsLocal(p) || l.Done || l.Animating || l.RollsUsed == 0 || resolved) return;
            Emit(ActDone, p, round << 20 | Pack(l.Values));
        }

        void Update()
        {
            if (!Running || resolved) return;
            var kb = Keyboard.current;
            for (int p = 0; p < 2; p++)
            {
                var l = lanes[p];
                if (!IsLocal(p) || l.Done) continue;

                l.Elapsed += Time.deltaTime;
                l.Timer.text = $"{Mathf.Max(0f, TimeLimit - l.Elapsed):0}s";
                if (l.Elapsed >= TimeLimit)
                {
                    // time's up: roll once if never rolled, then lock in
                    if (l.RollsUsed == 0) Roll(p);
                    else Finish(p);
                    continue;
                }

                if (IsCpuHere(p))
                {
                    UpdateCpu(p);
                    continue;
                }

                if (kb == null) continue;
                if (kb.spaceKey.wasPressedThisFrame) Roll(p);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Finish(p);
                int d = Controls.AnyDigit();
                if (d >= 1 && d <= DiceCount) ToggleHold(p, d - 1);
            }
        }

        void UpdateCpu(int p)
        {
            var l = lanes[p];
            if (l.Animating) return;
            l.CpuNext -= Time.deltaTime;
            if (l.CpuNext > 0f) return;
            l.CpuNext = Random.Range(0.9f, 1.5f);

            if (l.RollsUsed == 0) { Roll(p); return; }
            int score = Score(l.Values, out _);
            if (Rank(score) >= 5 || l.RollsUsed >= MaxRolls) { Finish(p); return; }

            // keep the best-looking dice, re-roll the rest
            var keep = CpuKeep(l.Values, Rank(score));
            for (int i = 0; i < DiceCount; i++)
                if (l.Held[i] != keep[i]) ToggleHold(p, i);
            Roll(p);
        }

        static bool[] CpuKeep(int[] v, int rank)
        {
            var keep = new bool[DiceCount];
            var count = new int[7];
            foreach (int x in v) count[x]++;

            if (rank == 4) // small straight: keep one of each die in the run, chase the large straight
            {
                for (int start = 3; start >= 1; start--)
                {
                    bool ok = true;
                    for (int f = start; f < start + 4; f++) ok &= count[f] > 0;
                    if (!ok) continue;
                    var used = new bool[7];
                    for (int i = 0; i < DiceCount; i++)
                        if (v[i] >= start && v[i] < start + 4 && !used[v[i]]) { used[v[i]] = true; keep[i] = true; }
                    return keep;
                }
            }
            if (rank == 2) // two pair: keep both pairs, hope for a full house
            {
                for (int i = 0; i < DiceCount; i++) keep[i] = count[v[i]] == 2;
                return keep;
            }
            int best = 0;
            for (int f = 6; f >= 1; f--) if (count[f] > count[best]) best = f;
            if (count[best] >= 2) for (int i = 0; i < DiceCount; i++) keep[i] = v[i] == best;
            return keep;
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(lanes[p].Panel, p == WinnerIndex, 110);
        }

        class DieClick : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnClick;
            public void OnPointerDown(PointerEventData e) => OnClick?.Invoke();
        }

        // ---------- Punishment: a giant die showing 1 (꽝) flattens the loser ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            ctx.Start(PunishFx.Popup(ctx.Stage, "꽝!", ctx.LoserFeet + new Vector2(0, 360), Palette.Red, 80));
            var die = MakeDie(ctx.Stage, Vector2.zero, 300f);
            SetFace(die, 1);
            yield return PunishFx.Crush(ctx, die.Rt, new Vector2(300, 300));
        }
    }
}
