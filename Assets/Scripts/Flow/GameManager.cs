using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Entry point. Lobby → [MiniGame Select → Play → Result &amp; Punish → Survival Check] loop → Final Game Over.
    /// Works for local play (one PC) and online 1v1, where a dedicated UDP server picks the round, validates input
    /// and decides the winner and damage (see <see cref="UdpNetSession"/>).
    /// Everything is built at runtime; just put this component on an empty GameObject in the scene.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("벌칙 데미지 (라운드당)")]
        [SerializeField] int minDamage = 25;
        [SerializeField] int maxDamage = 35;
        [Tooltip("테스트용: 켜면 모든 패배 데미지가 1")]
        [SerializeField] bool testDamageOne = true;

        [Header("테스트용: 로비에서 CPU로 시작")]
        [SerializeField] bool p1Cpu;
        [SerializeField] bool p2Cpu;

        RectTransform gameLayer, stage, hudLayer, overlay;
        BlurBackdrop blur;
        ScreenFader fader;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true; // online: keep simulating while the window is unfocused
            // group test scenes (Scenes/Groups/Group2_Brain.unity ...) only play that group's minigames
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            MiniGameRegistry.ActiveGroup = scene.StartsWith("Group") && scene.Length > 5 && char.IsDigit(scene[5]) ? scene[5] - '0' : 0;
            EnsureEventSystem();
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

            // draw order: minigame < blur < stage (characters) < hud < overlay (lobby / intro) < screen fx < fader
            // everything except the screen fx and the fader sits in Shake, which screen shake moves around
            var shake = UIKit.Rect("Shake", go.transform).Stretch();
            gameLayer = UIKit.Rect("GameLayer", shake).Stretch();
            blur = BlurBackdrop.Create(shake);
            stage = UIKit.Rect("Stage", shake).Stretch();
            hudLayer = UIKit.Rect("HudLayer", shake).Stretch();
            overlay = UIKit.Rect("Overlay", shake).Stretch();
            var screenFx = UIKit.Rect("ScreenFx", go.transform).Stretch();
            fader = ScreenFader.Create(go.transform);
            Juice.Init(shake, screenFx);
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

                net?.Leave();
                Destroy(hud.gameObject);
            }
        }

        IEnumerator PlayMatch(PlayerData[] players, HpHud hud, UdpNetSession net)
        {
            bool online = net != null;
            bool Lost() => online && net.Disconnected;
            // CPU vs CPU = watch/test mode (log lines); every minigame is played once before any repeats
            bool cpuTest = !online && players[0].IsCpu && players[1].IsCpu;
            var deck = new System.Collections.Generic.List<int>();

            for (int round = 1; ; round++)
            {
                hud.SetRound(round);

                // 2. MiniGame Select — online the server picks it and tells both PCs
                int gameIndex, seed;
                if (online)
                {
                    NetMsg start = default;
                    yield return net.WaitFor(MsgType.StartRound, m => start = m);
                    if (Lost()) { yield return Abort(); yield break; }
                    round = start.A;
                    gameIndex = start.B;
                    seed = start.C;
                    net.ServerJudges = start.D == 1;
                }
                else
                {
                    if (deck.Count == 0) deck = MiniGameRegistry.ShuffledDeck();
                    gameIndex = deck[0];
                    deck.RemoveAt(0);
                    seed = Random.Range(1, int.MaxValue);
                }

                var game = MiniGameRegistry.Create(gameIndex, gameLayer);
                game.Setup(players, seed, net);
                if (cpuTest) Debug.Log($"[CPU 테스트] ROUND {round}: {game.Title} (HP {players[0].Hp} : {players[1].Hp}, 덱 남은 {deck.Count})");
                if (online)
                {
                    net.CurrentRound = round;
                    net.ActiveGame = game;
                }

                var intro = RoundIntro.Create(overlay, round, game, online);
                yield return fader.FadeTo(0f, 0.4f);
                yield return intro.ShowCard();
                if (online)
                {
                    intro.SetWaiting();
                    yield return net.Barrier(Barriers.Tag(round, Barriers.Ready));
                    if (Lost()) { yield return Abort(); yield break; }
                }
                yield return intro.Countdown();

                // 3. MiniGame Play
                yield return game.Play();
                if (Lost()) { yield return Abort(); yield break; }
                yield return new WaitForSeconds(1.0f);

                // 4. Result & Punish (over a blurred snapshot) — online the server decides winner and damage
                int winner = game.WinnerIndex, damage;
                if (online)
                {
                    NetMsg result = default;
                    yield return net.WaitFor(MsgType.RoundResult, m => result = m);
                    if (Lost()) { yield return Abort(); yield break; }
                    winner = result.A;
                    damage = result.B;
                }
                else damage = cpuTest || testDamageOne ? 1 : Random.Range(minDamage, maxDamage + 1);

                yield return blur.CaptureAndShow(0.45f);
                game.gameObject.SetActive(false);
                if (online) net.ActiveGame = null;
                var podium = new PodiumSequence(stage, hud, players, this);
                yield return podium.Run(winner, game, damage);
                Destroy(game.gameObject);

                // 5. Survival Check
                bool over = podium.LoserDead;
                if (over) // 6. Final Game Over
                    yield return podium.GameOver(online ? players[net.LocalIndex].IsCpu : players[0].IsCpu && players[1].IsCpu);

                yield return fader.FadeTo(1f, 0.45f);
                podium.Cleanup();
                blur.Clear();
                if (over) yield break;

                if (online)
                {
                    yield return net.Barrier(Barriers.Tag(round, Barriers.Next));
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
            var bg = UIKit.Backdrop(overlay);
            bg.transform.SetAsFirstSibling();
            yield return fader.FadeTo(0f, 0.3f);
            yield return new WaitForSeconds(2.5f);
            yield return fader.FadeTo(1f, 0.3f);
            Destroy(msg.gameObject);
            Destroy(bg.gameObject);
        }
    }
}
