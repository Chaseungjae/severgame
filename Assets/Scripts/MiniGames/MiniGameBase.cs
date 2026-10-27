using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>A gameplay event. Player = -1 for system events decided by the authority.</summary>
    public struct GameAction
    {
        public int Kind, Player, A;
        public float F;
    }

    /// <summary>Network hook for minigames. Null when playing locally on one PC.</summary>
    public interface INetLink
    {
        bool IsHost { get; }
        int LocalIndex { get; }
        bool Disconnected { get; }
        void SendAction(GameAction action);
    }

    /// <summary>
    /// Base for every 1v1 minigame. A minigame builds its own UI under its RectTransform,
    /// runs until a winner is declared, then provides its own themed punishment for the podium scene.
    ///
    /// Networking model: players controlled on this machine (local humans / CPU) produce actions via
    /// <see cref="Emit"/>. An action is applied locally in <see cref="OnAction"/> and, online, sent to the
    /// opponent who applies it the same way. Only the authority (offline: this PC, online: host) decides results.
    /// </summary>
    public abstract class MiniGameBase : MonoBehaviour
    {
        const int KindWinner = -100;

        public abstract string Title { get; }
        public abstract string Rules { get; }
        public virtual string ControlsText => "W A S D / 방향키 이동 · Space 선택\n마우스 클릭도 가능";

        protected PlayerData[] Players;
        protected RectTransform Root;
        protected INetLink Net;
        protected int Seed;

        public int WinnerIndex { get; private set; } = -1;
        protected bool Running { get; private set; }
        bool winnerSent;

        public bool Online => Net != null;
        protected bool IsAuthority => Net == null || Net.IsHost;
        protected bool IsLocal(int p) => Net == null || Net.LocalIndex == p;
        protected bool IsCpuHere(int p) => IsLocal(p) && Players[p].IsCpu;
        protected bool IsHumanHere(int p) => IsLocal(p) && !Players[p].IsCpu;

        public void Setup(PlayerData[] players, int seed, INetLink net = null)
        {
            Players = players;
            Seed = seed;
            Net = net;
            Root = (RectTransform)transform;
            Build();
        }

        protected abstract void Build();
        protected virtual void OnBegin() { }
        protected virtual void OnFinish() { }
        protected abstract void OnAction(GameAction act);

        public IEnumerator Play()
        {
            Running = true;
            OnBegin();
            while (WinnerIndex < 0)
            {
                if (Net != null && Net.Disconnected) yield break;
                yield return null;
            }
            Running = false;
            OnFinish();
        }

        protected void Emit(int kind, int player, int a = 0, float f = 0f)
        {
            var act = new GameAction { Kind = kind, Player = player, A = a, F = f };
            Net?.SendAction(act);
            Handle(act);
        }

        /// <summary>Called by the network layer with the opponent's actions.</summary>
        public void ReceiveAction(GameAction act) => Handle(act);

        void Handle(GameAction act)
        {
            if (act.Kind == KindWinner)
            {
                if (WinnerIndex < 0) WinnerIndex = act.A;
                return;
            }
            OnAction(act);
        }

        /// <summary>Authority only: ends the game for everyone.</summary>
        protected void DeclareWinner(int player)
        {
            if (!IsAuthority || winnerSent) return;
            winnerSent = true;
            Emit(KindWinner, -1, player);
        }

        // ---------- input for the player controlled on this machine ----------
        // One PC = one human player, so every key set works for them: WASD or arrows, Space or Enter, left click.

        protected bool InConfirm(int p) => Controls.AnyConfirm;
        protected bool InClick(int p) => Controls.Click(0);
        protected bool InLeft(int p) => Controls.Left(0) || Controls.Left(1);
        protected bool InRight(int p) => Controls.Right(0) || Controls.Right(1);
        protected bool InUp(int p) => Controls.Up(0) || Controls.Up(1);
        protected bool InDown(int p) => Controls.Down(0) || Controls.Down(1);
        protected bool InUpHeld(int p) => Controls.UpHeld(0) || Controls.UpHeld(1);
        protected bool InDownHeld(int p) => Controls.DownHeld(0) || Controls.DownHeld(1);
        protected bool InLeftHeld(int p) => Controls.LeftHeld(0) || Controls.LeftHeld(1);
        protected bool InRightHeld(int p) => Controls.RightHeld(0) || Controls.RightHeld(1);

        /// <summary>Mouse position in the local space of a (pivot-centered) RectTransform on the overlay canvas.</summary>
        protected static Vector2 MouseIn(RectTransform rt)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Controls.MousePosition, null, out var local);
            return local;
        }

        /// <summary>Themed punishment played on the loser in the podium scene. Override per minigame.</summary>
        public virtual IEnumerator Punish(PunishContext ctx)
        {
            var block = UIKit.Img("PunishBlock", ctx.Stage, Palette.Hex("#50557A"), UIKit.Rounded);
            UIKit.Label("Text", block.transform, "벌칙", 80, Color.white).rectTransform.Stretch();
            yield return PunishFx.Crush(ctx, block.rectTransform, new Vector2(300, 300));
        }

        // ---------- shared layout helpers ----------

        protected void Background() => UIKit.Img("Bg", Root, Palette.Bg).rectTransform.Stretch();

        /// <summary>Standard left/right player panel with a colored name header.</summary>
        protected RectTransform PlayerPanel(int p, float width = 660f, float height = 820f, float y = -70f)
        {
            var panel = UIKit.Img($"Panel{p + 1}", Root, Palette.Panel, UIKit.Rounded).rectTransform;
            panel.Place(new Vector2(p == 0 ? -450 : 450, y), new Vector2(width, height));
            var header = UIKit.Label("Header", panel, Players[p].DisplayName, 34, Players[p].Color);
            header.rectTransform.Place(new Vector2(0, -40), new Vector2(width - 40, 50), new Vector2(0.5f, 1f));
            return panel;
        }

        /// <summary>Big result stamp (WIN! / LOSE) over a player's panel.</summary>
        protected void Stamp(RectTransform panel, bool won, float y = 0f)
        {
            if (!won) UIKit.Img("Dim", panel, new Color(0, 0, 0, 0.45f), UIKit.Rounded).rectTransform.Stretch();
            var stamp = UIKit.Label("Stamp", panel, won ? "WIN!" : "LOSE", won ? 150 : 110, won ? Palette.Gold : Palette.Muted);
            stamp.rectTransform.Place(new Vector2(0, y), new Vector2(600, 200));
            stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, won ? 8 : -6);
            UIKit.Shadow(stamp, 6f, 0.6f);
            StartCoroutine(Tween.Pop(stamp.transform, 2.2f, 0.35f));
            if (won)
            {
                Juice.Sparkle(panel, new Vector2(0, y), Palette.Gold, 1.3f);
                Juice.Shake(0.25f);
            }
            else Juice.Burst(panel, new Vector2(0, y), Juice.Style.Dust, 10, Palette.Muted);
        }
    }

    public class PunishContext
    {
        public readonly RectTransform Stage;
        public readonly CharacterView Loser;
        public readonly CharacterView Winner;
        readonly MonoBehaviour runner;

        public PunishContext(RectTransform stage, CharacterView loser, CharacterView winner, MonoBehaviour runner)
        {
            Stage = stage;
            Loser = loser;
            Winner = winner;
            this.runner = runner;
        }

        public Vector2 LoserFeet => Loser.Rt.anchoredPosition;
        public float LoserHeadY => Loser.Rt.anchoredPosition.y + Loser.Size;

        public Coroutine Start(IEnumerator routine) => runner.StartCoroutine(routine);

        public Vector2 LoserCenter => Loser.Rt.anchoredPosition + Vector2.up * Loser.Size * 0.5f * Loser.BaseScale.y;

        /// <summary>
        /// Screen shake for a punishment beat (intensity ≈ old pixel amount). Big ones (18+) also play a full impact
        /// on the loser: freeze frame, ring, sparks, speed lines, dust.
        /// </summary>
        public IEnumerator Shake(float intensity, float duration)
        {
            if (intensity >= 18f) Juice.Impact(Stage, LoserCenter, Mathf.InverseLerp(14f, 30f, intensity), ground: true);
            else Juice.Shake(intensity / 40f);
            yield return new WaitForSeconds(duration);
        }
    }

    public static class PunishFx
    {
        /// <summary>
        /// Drops a heavy block on the loser (optionally several times), flattens them, then lifts it away.
        /// Destroys the block.
        /// </summary>
        public static IEnumerator Crush(PunishContext ctx, RectTransform block, Vector2 size, int hits = 1, float startY = 760f)
        {
            var feet = ctx.LoserFeet;
            float flatH = ctx.Loser.Size * 0.3f;
            block.Place(new Vector2(feet.x, startY), size, pivot: new Vector2(0.5f, 0f));

            // warning shadow on the ground
            var warn = UIKit.Img("Warn", ctx.Stage, new Color(0, 0, 0, 0f), UIKit.Circle);
            warn.rectTransform.Place(feet, new Vector2(size.x * 1.1f, 60f));
            warn.transform.SetSiblingIndex(0);
            yield return Tween.Run(0.45f, t => warn.color = new Color(0, 0, 0, 0.35f * t));

            for (int h = 0; h < hits; h++)
            {
                float fromY = block.anchoredPosition.y;
                yield return Tween.Run(h == 0 ? 0.3f : 0.12f, t => block.anchoredPosition = new Vector2(feet.x, Mathf.Lerp(fromY, feet.y + flatH, t)), Ease.InQuad);

                ctx.Loser.SetFace(CharacterView.Face.Hurt);
                ctx.Loser.BaseScale = new Vector3(1.5f, 0.3f, 1f);
                bool last = h == hits - 1;
                var contact = new Vector2(feet.x, feet.y + flatH);
                Juice.Impact(ctx.Stage, contact, last ? 1f : 0.55f, ground: true);
                Juice.Burst(ctx.Stage, new Vector2(feet.x, feet.y + 10f), Juice.Style.Debris, last ? 14 : 6);
                if (last) break;

                // bounce up a bit for the next slam
                yield return new WaitForSeconds(0.08f);
                float lowY = block.anchoredPosition.y;
                ctx.Start(ctx.Loser.ScaleTo(new Vector3(1.1f, 0.8f, 1f), 0.15f));
                yield return Tween.Run(0.15f, t => block.anchoredPosition = new Vector2(feet.x, Mathf.Lerp(lowY, lowY + 260, t)), Ease.OutQuad);
            }
            yield return new WaitForSeconds(0.9f);

            float y0 = block.anchoredPosition.y;
            ctx.Start(ctx.Loser.ScaleTo(Vector3.one, 0.35f, Ease.OutBack));
            yield return Tween.Run(0.4f, t =>
            {
                block.anchoredPosition = new Vector2(feet.x, Mathf.Lerp(y0, 900, t));
                warn.color = new Color(0, 0, 0, 0.35f * (1 - t));
            }, Ease.InQuad);
            UnityEngine.Object.Destroy(block.gameObject);
            UnityEngine.Object.Destroy(warn.gameObject);
        }

        /// <summary>Comic-style text burst ("CLICK!", "부릉!") that pops, floats up and fades.</summary>
        public static IEnumerator Popup(RectTransform parent, string text, Vector2 pos, Color color, int size = 56)
        {
            var label = UIKit.Label("Popup", parent, text, size, color);
            label.rectTransform.Place(pos, new Vector2(400, size * 1.5f));
            label.rectTransform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-15f, 15f));
            UIKit.Shadow(label, 4f, 0.6f);
            yield return Tween.Pop(label.transform, 1.8f, 0.2f);
            yield return Tween.Run(0.45f, t =>
            {
                label.rectTransform.anchoredPosition = pos + Vector2.up * 50f * t;
                label.color = color.WithAlpha(1 - t);
            });
            UnityEngine.Object.Destroy(label.gameObject);
        }
    }

    public static class MiniGameRegistry
    {
        /// <summary>Add new minigames here. The round picker draws randomly from this list.</summary>
        public static readonly (string Title, Type Type)[] Games =
        {
            ("1 to 50", typeof(OneToFiftyGame)),
            ("스페이스바 줄다리기", typeof(SpaceTugGame)),
            ("CPS 클릭 대결", typeof(CpsGame)),
            ("F1 반응 속도", typeof(F1ReactionGame)),
            ("에임 대결", typeof(AimGame)),
            ("공 개수 세기", typeof(BallCountGame)),
            ("100층 도전", typeof(StairsGame)),
            ("블럭 쌓기", typeof(StackGame)),
            ("원을 그려봐요", typeof(CircleGame)),
            ("타자 대결", typeof(TypingGame)),
            ("방향키 입력 대결", typeof(ArrowGame)),
            ("타이밍 서바이벌", typeof(TimingGame)),
            ("사목", typeof(ConnectFourGame)),
            ("오델로", typeof(OthelloGame)),
            ("색깔 찾기", typeof(ColorGame)),
            ("표창 막기", typeof(ShurikenGame)),
            ("죽림고수", typeof(BambooGame)),
            ("리듬 대결", typeof(RhythmGame)),
            ("스네이크 대결", typeof(SnakeGame)),
            ("순간 기억력", typeof(MemoryGame)),
            ("10초 맞추기", typeof(TenSecondsGame)),
            ("야추 다이스", typeof(YachtGame)),
        };

        static int last = -1;

        /// <summary>Random minigame index, never the same as the previous round.</summary>
        public static int PickIndex()
        {
            int i = UnityEngine.Random.Range(0, Games.Length);
            if (Games.Length > 1 && i == last) i = (i + 1 + UnityEngine.Random.Range(0, Games.Length - 1)) % Games.Length;
            last = i;
            return i;
        }

        /// <summary>All minigame indices in random order (no repeats until every game was played).</summary>
        public static System.Collections.Generic.List<int> ShuffledDeck()
        {
            var deck = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Games.Length; i++) deck.Add(i);
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            if (deck.Count > 1 && deck[0] == last) (deck[0], deck[deck.Count - 1]) = (deck[deck.Count - 1], deck[0]);
            last = deck[deck.Count - 1];
            return deck;
        }

        public static MiniGameBase Create(int index, Transform parent)
        {
            var type = Games[Mathf.Clamp(index, 0, Games.Length - 1)].Type;
            var rt = UIKit.Rect(type.Name, parent).Stretch();
            return (MiniGameBase)rt.gameObject.AddComponent(type);
        }
    }
}
