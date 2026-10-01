using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Entry point. Lobby → [MiniGame Select → Play → Result &amp; Punish → Survival Check] loop → Final Game Over.
    /// Works for local play (one PC) and online 1v1 (host = authority, see <see cref="NetSession"/>).
    /// Everything is built at runtime; just put this component on an empty GameObject in the scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("벌칙 데미지 (라운드당)")]
        [SerializeField] int minDamage = 25;
        [SerializeField] int maxDamage = 35;

        [Header("테스트용: 로비에서 CPU로 시작")]
        [SerializeField] bool p1Cpu;
        [SerializeField] bool p2Cpu;

        // barrier tags (combined with the round number)
        const int BarrierReady = 1, BarrierNext = 2;

        RectTransform gameLayer, stage, hudLayer, overlay;
        BlurBackdrop blur;
        ScreenFader fader;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true; // online: keep simulating while the window is unfocused
            EnsureEventSystem();
            NetSession.EnsureNetworkManager();
            BuildCanvas();
        }

        void Start() => StartCoroutine(MainLoop());

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        void BuildCanvas()
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = 5 };
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // draw order: minigame < blur < stage (characters) < hud < overlay (lobby / intro) < fader
            gameLayer = UIKit.Rect("GameLayer", go.transform).Stretch();
            blur = BlurBackdrop.Create(go.transform);
            stage = UIKit.Rect("Stage", go.transform).Stretch();
            hudLayer = UIKit.Rect("HudLayer", go.transform).Stretch();
            overlay = UIKit.Rect("Overlay", go.transform).Stretch();
            fader = ScreenFader.Create(go.transform);
        }

        IEnumerator MainLoop()
        {
            while (true)
            {
                // 1. Lobby / Match
                var players = new[] { new PlayerData(0), new PlayerData(1) };
                var lobby = LobbyScreen.Create(overlay);
                lobby.Cpu[0] = p1Cpu;
                lobby.Cpu[1] = p2Cpu;
                var lobbyRun = StartCoroutine(lobby.Run(players));
                yield return fader.FadeTo(0f, 0.4f);
                yield return lobbyRun;
                var net = lobby.Session;
                yield return fader.FadeTo(1f, 0.4f);
                Destroy(lobby.gameObject);

                var hud = HpHud.Create(hudLayer, players);
                yield return PlayMatch(players, hud, net);

                if (net != null)
                {
                    var leave = net.LeaveAsync();
                    while (!leave.IsCompleted) yield return null;
                }
                Destroy(hud.gameObject);
            }
        }

        IEnumerator PlayMatch(PlayerData[] players, HpHud hud, NetSession net)
        {
            bool Lost() => net != null && net.Disconnected;
            bool authority = net == null || net.IsHost;
            // CPU vs CPU = watch/test mode: every minigame once before any repeats, and only 1 damage per loss
            bool cpuTest = net == null && players[0].IsCpu && players[1].IsCpu;
            var deck = new System.Collections.Generic.List<int>();

            for (int round = 1; ; round++)
            {
                hud.SetRound(round);

                // 2. MiniGame Select (host picks, client follows)
                int gameIndex = 0, seed = 0;
                if (authority)
                {
                    if (cpuTest)
                    {
                        if (deck.Count == 0) deck = MiniGameRegistry.ShuffledDeck();
                        gameIndex = deck[0];
                        deck.RemoveAt(0);
                    }
                    else gameIndex = MiniGameRegistry.PickIndex();
                    seed = Random.Range(1, int.MaxValue);
                    net?.Send(new NetMsg { Type = MsgType.StartRound, A = round, B = gameIndex, C = seed });
                }
                else
                {
                    NetMsg start = default;
                    yield return net.WaitFor(MsgType.StartRound, m => start = m);
                    if (Lost()) { yield return Abort(); yield break; }
                    gameIndex = start.B;
                    seed = start.C;
                }

                var game = MiniGameRegistry.Create(gameIndex, gameLayer);
                game.Setup(players, seed, net);
                if (cpuTest) Debug.Log($"[CPU 테스트] ROUND {round}: {game.Title} (HP {players[0].Hp} : {players[1].Hp}, 덱 남은 {deck.Count})");
                if (net != null)
                {
                    net.CurrentRound = round;
                    net.ActiveGame = game;
                }

                var intro = RoundIntro.Create(overlay, round, game, net != null);
                yield return fader.FadeTo(0f, 0.4f);
                yield return intro.ShowCard();
                if (net != null)
                {
                    intro.SetWaiting();
                    yield return net.Barrier(round * 10 + BarrierReady);
                    if (Lost()) { yield return Abort(); yield break; }
                }
                yield return intro.Countdown();

                // 3. MiniGame Play
                yield return game.Play();
                if (Lost()) { yield return Abort(); yield break; }
                yield return new WaitForSeconds(1.0f);

                // 4. Result & Punish (over a blurred snapshot of the finished minigame)
                int winner = game.WinnerIndex, damage;
                if (authority)
                {
                    damage = cpuTest ? 1 : Random.Range(minDamage, maxDamage + 1);
                    net?.Send(new NetMsg { Type = MsgType.RoundResult, A = winner, B = damage });
                }
                else
                {
                    NetMsg result = default;
                    yield return net.WaitFor(MsgType.RoundResult, m => result = m);
                    if (Lost()) { yield return Abort(); yield break; }
                    winner = result.A;
                    damage = result.B;
                }

                yield return blur.CaptureAndShow(0.45f);
                game.gameObject.SetActive(false);
                if (net != null) net.ActiveGame = null;
                var podium = new PodiumSequence(stage, hud, players, this);
                yield return podium.Run(winner, game, damage);
                Destroy(game.gameObject);

                // 5. Survival Check
                bool over = podium.LoserDead;
                if (over) // 6. Final Game Over
                    yield return podium.GameOver(net == null ? players[0].IsCpu && players[1].IsCpu : players[net.LocalIndex].IsCpu);

                yield return fader.FadeTo(1f, 0.45f);
                podium.Cleanup();
                blur.Clear();
                if (over) yield break;

                if (net != null)
                {
                    yield return net.Barrier(round * 10 + BarrierNext);
                    if (Lost()) { yield return Abort(); yield break; }
                }
                yield return new WaitForSeconds(0.2f);
            }
        }

        /// <summary>Opponent disconnected: clear the screen, tell the player, and fall back to the lobby.</summary>
        IEnumerator Abort()
        {
            yield return fader.FadeTo(1f, 0.3f);
            foreach (var layer in new[] { gameLayer, stage, overlay })
                foreach (Transform child in layer) Destroy(child.gameObject);
            blur.Clear();

            var msg = UIKit.Label("Disconnected", overlay, "상대와 연결이 끊겼습니다\n로비로 돌아갑니다", 56, Color.white);
            msg.rectTransform.Place(Vector2.zero, new Vector2(1200, 200));
            var bg = UIKit.Img("Bg", overlay, Palette.Bg);
            bg.rectTransform.Stretch();
            bg.transform.SetAsFirstSibling();
            yield return fader.FadeTo(0f, 0.3f);
            yield return new WaitForSeconds(2.5f);
            yield return fader.FadeTo(1f, 0.3f);
            Destroy(msg.gameObject);
            Destroy(bg.gameObject);
        }
    }
}
