using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>Step 2: announce the drawn minigame + rules, then 3-2-1 countdown over the game.</summary>
    public class RoundIntro : MonoBehaviour
    {
        CanvasGroup card;
        Text titleText;
        CanvasGroup details;
        MiniGameBase game;

        Text skipText;
        bool online;

        public static RoundIntro Create(Transform parent, int round, MiniGameBase game, bool online = false)
        {
            var rt = UIKit.Rect("RoundIntro", parent).Stretch();
            var intro = rt.gameObject.AddComponent<RoundIntro>();
            intro.game = game;
            intro.online = online;
            intro.Build(round);
            return intro;
        }

        void Build(int round)
        {
            var bg = UIKit.Img("Card", transform, Palette.Bg);
            bg.rectTransform.Stretch();
            card = bg.gameObject.AddComponent<CanvasGroup>();

            UIKit.Label("Round", bg.transform, $"ROUND {round}", 48, Palette.Gold).rectTransform.Place(new Vector2(0, 340), new Vector2(600, 70));
            UIKit.Label("Caption", bg.transform, "이번 미니게임은...", 30, Palette.Muted, style: FontStyle.Normal)
                .rectTransform.Place(new Vector2(0, 275), new Vector2(600, 44));

            var box = UIKit.Img("TitleBox", bg.transform, Palette.PanelLight, UIKit.Rounded).rectTransform;
            box.Place(new Vector2(0, 165), new Vector2(900, 150));
            titleText = UIKit.Label("Title", box, "? ? ?", 84, Color.white);
            titleText.rectTransform.Stretch();
            UIKit.Shadow(titleText, 5f);

            var d = UIKit.Rect("Details", bg.transform).Stretch();
            details = d.gameObject.AddComponent<CanvasGroup>();
            details.alpha = 0;

            UIKit.Label("Rules", d, game.Rules, 34, Color.white, style: FontStyle.Normal).rectTransform.Place(new Vector2(0, -20), new Vector2(1400, 180));

            var ctrl = UIKit.Img("ControlsBox", d, Palette.Panel, UIKit.Rounded).rectTransform;
            ctrl.Place(new Vector2(0, -225), new Vector2(900, 150));
            UIKit.Label("Controls", ctrl, online ? game.OnlineControlsText : game.ControlsText, 28, Palette.Muted, style: FontStyle.Normal).rectTransform.Stretch();

            skipText = UIKit.Label("Skip", d, "Space / Enter 로 바로 시작", 24, Palette.Muted, style: FontStyle.Normal);
            skipText.rectTransform.Place(new Vector2(0, -380), new Vector2(600, 36));
        }

        public IEnumerator Run()
        {
            yield return ShowCard();
            yield return Countdown();
        }

        /// <summary>Online: shown on the card while waiting for the opponent to finish reading.</summary>
        public void SetWaiting()
        {
            skipText.text = "상대를 기다리는 중...";
        }

        /// <summary>Title roulette + rules card. Waits a few seconds (skippable with Space / Enter).</summary>
        public IEnumerator ShowCard()
        {
            // roulette over registered minigames
            var games = MiniGameRegistry.Games;
            float delay = 0.05f;
            for (int k = 0; k < 14; k++)
            {
                titleText.text = games.Length > 1 ? games[k % games.Length].Title : (k % 2 == 0 ? "? ? ?" : "! ! !");
                yield return new WaitForSeconds(delay);
                delay *= 1.18f;
            }
            titleText.text = game.Title;
            yield return Tween.Pop(titleText.transform, 1.8f, 0.4f);
            yield return Tween.Run(0.3f, t => details.alpha = t);

            float wait = 0f;
            while (wait < 3.2f && !(wait > 0.4f && Controls.AnyConfirm))
            {
                wait += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Fades the card and plays 3-2-1-START over the game. Destroys the intro afterwards.</summary>
        public IEnumerator Countdown()
        {
            yield return Tween.Run(0.3f, t => card.alpha = 1 - t);

            foreach (var s in new[] { "3", "2", "1", "START!" })
            {
                var label = UIKit.Label("Count", transform, s, s.Length > 1 ? 170 : 240, s.Length > 1 ? Palette.Gold : Color.white);
                label.rectTransform.Place(Vector2.zero, new Vector2(900, 300));
                UIKit.Shadow(label, 8f, 0.6f);
                var self = (RectTransform)transform;
                if (s.Length > 1)
                {
                    Juice.Ring(self, Vector2.zero, Palette.Gold, 700f, 0.4f);
                    Juice.Burst(self, Vector2.zero, Juice.Style.Stars, 24, null, 1.4f);
                    Juice.Shake(0.3f);
                }
                else
                {
                    Juice.Ring(self, Vector2.zero, Color.white.WithAlpha(0.7f), 420f, 0.3f);
                    Juice.Shake(0.12f);
                }
                yield return Tween.Pop(label.transform, 1.8f, 0.3f);
                yield return new WaitForSeconds(s.Length > 1 ? 0.15f : 0.3f);
                if (s.Length > 1)
                {
                    // START! fades while the game already runs
                    StartCoroutine(FadeAndDestroy(label));
                    break;
                }
                Destroy(label.gameObject);
            }
        }

        IEnumerator FadeAndDestroy(Text label)
        {
            yield return Tween.FadeGraphic(label, 0f, 0.4f);
            Destroy(gameObject);
        }
    }
}
