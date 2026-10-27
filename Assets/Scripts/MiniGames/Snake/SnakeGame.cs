using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Snake battle: both snakes share one arena. Eat apples to grow; crashing into a wall, yourself or the other
    /// snake loses. After 60 seconds the longer snake wins.
    /// Online: the host runs the clock and broadcasts each tick with both snakes' directions; the simulation is
    /// deterministic (grid + shared seed for apples), so both PCs stay identical.
    /// </summary>
    public class SnakeGame : MiniGameBase
    {
        public override string Title => "스네이크 대결";
        public override string Rules => "한 경기장에서 두 뱀이 대결! 사과를 먹으면 길어집니다.\n벽·자기 몸·상대 뱀에 부딪히면 패배 (머리끼리 부딪히면 더 긴 쪽 승리)\n60초가 지나면 더 긴 뱀이 승리";
        public override string ControlsText => "W A S D 또는 방향키";

        const int W = 28, H = 17;
        const float Cell = 32f;
        const float TickTime = 0.11f;
        const float Duration = 60f;
        const int Apples = 3;
        const int ActTick = 1, ActDir = 2;

        static readonly Vector2Int[] Step = { Vector2Int.down, Vector2Int.right, Vector2Int.up, Vector2Int.left }; // grid y grows downward: 0 = up

        readonly List<Vector2Int>[] bodies = { new List<Vector2Int>(), new List<Vector2Int>() };
        readonly int[] dirs = { 1, 3 };
        readonly bool[] dead = new bool[2];
        readonly List<Vector2Int> apples = new List<Vector2Int>();
        readonly Queue<int>[] pending = { new Queue<int>(), new Queue<int>() };
        System.Random rng;
        int tick;
        float tickTimer;

        RectTransform arena;
        readonly List<Image> pool = new List<Image>();
        readonly RectTransform[] panels = new RectTransform[2];
        readonly Text[] lengthTexts = new Text[2];
        Text timerText;

        protected override void Build()
        {
            Background();
            rng = new System.Random(Seed);

            timerText = UIKit.Label("Timer", Root, $"{Duration:0}", 48, Color.white);
            timerText.rectTransform.Place(new Vector2(0, 380), new Vector2(300, 60));

            arena = UIKit.Img("Arena", Root, Palette.Hex("#15182E"), UIKit.Rounded).rectTransform;
            arena.Place(new Vector2(0, -60), new Vector2(W * Cell + 16, H * Cell + 16));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if ((x + y) % 2 == 0)
                        UIKit.Img("Check", arena, Color.white.WithAlpha(0.025f)).rectTransform.Place(CellPos(new Vector2Int(x, y)), new Vector2(Cell, Cell));

            for (int p = 0; p < 2; p++)
            {
                panels[p] = PlayerPanel(p, 360, 300, -60);
                panels[p].anchoredPosition = new Vector2(p == 0 ? -770 : 770, -60);
                UIKit.Label("L", panels[p], "길이", 28, Palette.Muted).rectTransform.Place(new Vector2(0, 40), new Vector2(200, 40));
                lengthTexts[p] = UIKit.Label("Len", panels[p], "3", 80, Color.white);
                lengthTexts[p].rectTransform.Place(new Vector2(0, -30), new Vector2(200, 100));
                string keys = "WASD / 방향키";
                UIKit.Label("Keys", panels[p], keys, 24, Palette.Muted, style: FontStyle.Normal).rectTransform.Place(new Vector2(0, -105), new Vector2(300, 36));
            }

            for (int i = 0; i < 3; i++)
            {
                bodies[0].Add(new Vector2Int(5 - i, 5));
                bodies[1].Add(new Vector2Int(W - 6 + i, H - 6));
            }
            for (int i = 0; i < Apples; i++) SpawnApple();
            Render();
        }

        Vector2 CellPos(Vector2Int c) => new Vector2((c.x - (W - 1) / 2f) * Cell, ((H - 1) / 2f - c.y) * Cell);

        bool Occupied(Vector2Int c)
        {
            foreach (var b in bodies) if (b.Contains(c)) return true;
            return apples.Contains(c);
        }

        void SpawnApple()
        {
            for (int tries = 0; tries < 500; tries++)
            {
                var c = new Vector2Int(rng.Next(W), rng.Next(H));
                if (Occupied(c)) continue;
                apples.Add(c);
                return;
            }
        }

        static bool Inside(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H;

        // ---------- input ----------

        void Update()
        {
            if (!Running) return;

            for (int p = 0; p < 2; p++)
            {
                if (!IsHumanHere(p) || dead[p]) continue;
                int d = InUp(p) ? 0 : InRight(p) ? 1 : InDown(p) ? 2 : InLeft(p) ? 3 : -1;
                if (d < 0) continue;
                if (IsAuthority) Queue(p, d);
                else Emit(ActDir, p, d);
            }

            if (!IsAuthority) return;
            tickTimer += Time.deltaTime;
            if (tickTimer < TickTime) return;
            tickTimer -= TickTime;

            var next = new int[2];
            for (int p = 0; p < 2; p++)
            {
                if (IsCpuHere(p)) next[p] = CpuDir(p);
                else next[p] = pending[p].Count > 0 ? pending[p].Dequeue() : dirs[p];
            }
            Emit(ActTick, -1, (tick + 1) * 16 + next[0] * 4 + next[1]);
        }

        void Queue(int p, int d)
        {
            if (pending[p].Count >= 2) return;
            int last = dirs[p];
            foreach (int q in pending[p]) last = q;
            if (d == last || d == (last + 2) % 4) return; // same or reverse
            pending[p].Enqueue(d);
        }

        // ---------- simulation (identical on both PCs) ----------

        protected override void OnAction(GameAction act)
        {
            if (act.Kind == ActDir)
            {
                if (IsAuthority && act.Player >= 0 && act.Player < 2 && act.A >= 0 && act.A < 4) Queue(act.Player, act.A);
                return;
            }
            if (act.Kind != ActTick || WinnerIndex >= 0) return;
            int t = act.A / 16;
            if (t != tick + 1) return;
            tick = t;
            for (int p = 0; p < 2; p++)
            {
                int d = p == 0 ? (act.A / 4) % 4 : act.A % 4;
                if (d != (dirs[p] + 2) % 4) dirs[p] = d;
            }
            Advance();
        }

        void Advance()
        {
            var heads = new Vector2Int[2];
            var eats = new bool[2];
            for (int p = 0; p < 2; p++)
            {
                if (dead[p]) continue;
                heads[p] = bodies[p][0] + Step[dirs[p]];
                eats[p] = apples.Contains(heads[p]);
            }
            // tails move first (unless growing)
            for (int p = 0; p < 2; p++)
                if (!dead[p] && !eats[p]) bodies[p].RemoveAt(bodies[p].Count - 1);

            var crash = new bool[2];
            for (int p = 0; p < 2; p++)
            {
                if (dead[p]) continue;
                var h = heads[p];
                if (!Inside(h) || bodies[0].Contains(h) || bodies[1].Contains(h)) crash[p] = true;
            }
            if (!dead[0] && !dead[1])
            {
                bool headOn = heads[0] == heads[1];
                bool swap = heads[0] == bodies[1][0] && heads[1] == bodies[0][0]; // passing through each other
                if (headOn || swap) crash[0] = crash[1] = true;
            }

            for (int p = 0; p < 2; p++)
            {
                if (dead[p]) continue;
                if (crash[p])
                {
                    dead[p] = true;
                    continue;
                }
                bodies[p].Insert(0, heads[p]);
                if (eats[p])
                {
                    apples.Remove(heads[p]);
                    SpawnApple();
                }
            }

            Render();
            for (int p = 0; p < 2; p++)
            {
                lengthTexts[p].text = bodies[p].Count.ToString();
                if (eats[p] && !dead[p]) StartCoroutine(Tween.Pop(lengthTexts[p].transform, 1.4f, 0.15f));
            }
            timerText.text = Mathf.CeilToInt(Mathf.Max(0f, Duration - tick * TickTime)).ToString();

            if (!IsAuthority) return;
            if (dead[0] || dead[1])
            {
                if (dead[0] && dead[1])
                    DeclareWinner(bodies[0].Count != bodies[1].Count ? (bodies[0].Count > bodies[1].Count ? 0 : 1) : Seed % 2);
                else DeclareWinner(dead[0] ? 1 : 0);
            }
            else if (tick * TickTime >= Duration)
            {
                DeclareWinner(bodies[0].Count != bodies[1].Count ? (bodies[0].Count > bodies[1].Count ? 0 : 1) : Seed % 2);
            }
        }

        void Render()
        {
            int used = 0;
            Image Next()
            {
                if (used == pool.Count) pool.Add(UIKit.Img("Seg", arena, Color.white, UIKit.Rounded));
                var img = pool[used++];
                img.gameObject.SetActive(true);
                return img;
            }

            foreach (var a in apples)
            {
                var img = Next();
                img.sprite = UIKit.Circle;
                img.type = Image.Type.Simple;
                img.color = Palette.Red;
                img.rectTransform.Place(CellPos(a), new Vector2(Cell - 6, Cell - 6));
            }
            for (int p = 0; p < 2; p++)
            {
                var color = Players[p].Color;
                for (int i = 0; i < bodies[p].Count; i++)
                {
                    var img = Next();
                    img.sprite = UIKit.Rounded;
                    img.type = Image.Type.Sliced;
                    float size = i == 0 ? Cell : Cell - 4;
                    img.color = dead[p] ? Palette.Muted : i == 0 ? Color.Lerp(color, Color.white, 0.25f) : Color.Lerp(color, Color.black, 0.12f * (i % 2));
                    img.rectTransform.Place(CellPos(bodies[p][i]), new Vector2(size, size));
                }
            }
            for (int i = used; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }

        // ---------- CPU ----------

        int CpuDir(int p)
        {
            int best = dirs[p];
            float bestScore = float.MinValue;
            var head = bodies[p][0];
            var otherHead = bodies[1 - p].Count > 0 ? bodies[1 - p][0] : new Vector2Int(-9, -9);
            for (int d = 0; d < 4; d++)
            {
                if (d == (dirs[p] + 2) % 4) continue;
                var c = head + Step[d];
                if (!Inside(c) || bodies[0].Contains(c) || bodies[1].Contains(c)) continue;
                float score = FloodSize(c, 80) * 1.5f;
                int nearest = int.MaxValue;
                foreach (var a in apples) nearest = Mathf.Min(nearest, Mathf.Abs(a.x - c.x) + Mathf.Abs(a.y - c.y));
                score -= nearest * 2f;
                if (Mathf.Abs(c.x - otherHead.x) + Mathf.Abs(c.y - otherHead.y) <= 1) score -= 40f; // avoid head-ons
                score += Random.value;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        int FloodSize(Vector2Int start, int cap)
        {
            var seen = new HashSet<Vector2Int> { start };
            var q = new Queue<Vector2Int>();
            q.Enqueue(start);
            while (q.Count > 0 && seen.Count < cap)
            {
                var c = q.Dequeue();
                foreach (var s in Step)
                {
                    var n = c + s;
                    if (!Inside(n) || seen.Contains(n) || bodies[0].Contains(n) || bodies[1].Contains(n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return seen.Count;
        }

        protected override void OnFinish()
        {
            for (int p = 0; p < 2; p++) Stamp(panels[p], p == WinnerIndex, 0);
        }

        // ---------- Punishment: a giant snake coils around the loser and squeezes ----------

        public override IEnumerator Punish(PunishContext ctx)
        {
            var loser = ctx.Loser;
            var feet = ctx.LoserFeet;
            var center = feet + new Vector2(0, loser.Size * 0.5f);
            var color = Players[WinnerIndex].Color;
            const int n = 18;
            var segs = new RectTransform[n];
            for (int i = 0; i < n; i++)
            {
                var img = UIKit.Img("Coil", ctx.Stage, i == 0 ? Color.Lerp(color, Color.white, 0.25f) : Color.Lerp(color, Color.black, 0.12f * (i % 2)), UIKit.Circle);
                segs[i] = img.rectTransform;
                segs[i].sizeDelta = Vector2.one * (i == 0 ? 90f : 70f);
                segs[i].anchoredPosition = new Vector2(-1300f, center.y);
            }
            var eyes = UIKit.Label("Eyes", segs[0], "• •", 30, Palette.Ink);
            eyes.rectTransform.Stretch();

            // slither in and wrap
            float radius = 190f;
            yield return Tween.Run(0.9f, t =>
            {
                for (int i = 0; i < n; i++)
                {
                    float a = (t * 2.2f - i * 0.06f) * Mathf.PI * 2f;
                    float into = Mathf.Clamp01(t * 1.6f - i * 0.03f);
                    var ring = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * radius;
                    segs[i].anchoredPosition = Vector2.Lerp(new Vector2(-1300f + i * 60f, center.y), ring, into);
                }
            });

            // squeeze!
            ctx.Start(PunishFx.Popup(ctx.Stage, "꽈악!", center + new Vector2(0, 280), Palette.Red, 84));
            loser.SetFace(CharacterView.Face.Hurt);
            yield return Tween.Run(0.8f, t =>
            {
                float r = Mathf.Lerp(radius, 105f, Ease.OutQuad(t));
                for (int i = 0; i < n; i++)
                {
                    float a = (2.2f - i * 0.06f) * Mathf.PI * 2f + t * 2f;
                    segs[i].anchoredPosition = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * r;
                }
                loser.BaseScale = new Vector3(Mathf.Lerp(1f, 0.6f, t), Mathf.Lerp(1f, 1.25f, t), 1f);
            });
            ctx.Start(ctx.Shake(10f, 0.3f));
            yield return new WaitForSeconds(0.5f);

            // slither away
            ctx.Start(loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack));
            var start = new Vector2[n];
            for (int i = 0; i < n; i++) start[i] = segs[i].anchoredPosition;
            yield return Tween.Run(0.6f, t =>
            {
                for (int i = 0; i < n; i++)
                    segs[i].anchoredPosition = Vector2.Lerp(start[i], new Vector2(1400f + i * 60f, center.y), Mathf.Clamp01(t * 1.3f - i * 0.015f));
            }, Ease.InQuad);
            foreach (var s in segs) Destroy(s.gameObject);
        }
    }

}
