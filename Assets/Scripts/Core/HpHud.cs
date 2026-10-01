using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>Top-of-screen HP bars for both players (fighting-game style, mirrored).</summary>
    public class HpHud : MonoBehaviour
    {
        class Bar
        {
            public PlayerData Player;
            public RectTransform Panel, Fill, Chip;
            public Text HpText;
        }

        readonly Bar[] bars = new Bar[2];
        Text roundText;

        public static HpHud Create(Transform parent, PlayerData[] players)
        {
            var rt = UIKit.Rect("HpHud", parent).Stretch();
            var hud = rt.gameObject.AddComponent<HpHud>();
            for (int p = 0; p < 2; p++) hud.bars[p] = hud.BuildBar(players[p]);
            hud.roundText = UIKit.Label("Round", rt, "", 34, Palette.Gold);
            hud.roundText.rectTransform.Place(new Vector2(0, -62), new Vector2(300, 50), new Vector2(0.5f, 1f));
            return hud;
        }

        Bar BuildBar(PlayerData player)
        {
            int p = player.Index;
            float a = p == 0 ? 0f : 1f; // anchor side
            float s = p == 0 ? 1f : -1f; // direction inward

            var panel = UIKit.Img($"P{p + 1}Panel", transform, Palette.Panel.WithAlpha(0.92f), UIKit.Rounded).rectTransform;
            panel.Place(new Vector2(s * 30, -20), new Vector2(760, 96), new Vector2(a, 1), new Vector2(a, 1));

            var portrait = CharacterView.Create(panel, player.Character, 70);
            portrait.Rt.Place(new Vector2(s * 58, 12), new Vector2(70, 70), new Vector2(a, 0), new Vector2(0.5f, 0));

            var name = UIKit.Label("Name", panel, player.DisplayName, 28, player.Color,
                p == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight);
            name.rectTransform.Place(new Vector2(s * 110, -10), new Vector2(420, 40), new Vector2(a, 1), new Vector2(a, 1));

            var hpText = UIKit.Label("Hp", panel, "", 26, Color.white, p == 0 ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
            hpText.rectTransform.Place(new Vector2(-s * 24, -12), new Vector2(200, 40), new Vector2(1 - a, 1), new Vector2(1 - a, 1));

            var bg = UIKit.Img("BarBg", panel, new Color(0, 0, 0, 0.45f), UIKit.Rounded).rectTransform;
            bg.Place(new Vector2(s * 110, 16), new Vector2(626, 28), new Vector2(a, 0), new Vector2(a, 0));

            var chip = UIKit.Img("Chip", bg, new Color(1f, 1f, 1f, 0.85f), UIKit.Rounded).rectTransform;
            var fill = UIKit.Img("Fill", bg, player.Color, UIKit.Rounded).rectTransform;

            var bar = new Bar { Player = player, Panel = panel, Fill = fill, Chip = chip, HpText = hpText };
            SetRatio(bar, fill, 1f);
            SetRatio(bar, chip, 1f);
            hpText.text = $"{player.Hp} / {player.MaxHp}";
            return bar;
        }

        static void SetRatio(Bar bar, RectTransform r, float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            r.gameObject.SetActive(ratio > 0.001f);
            if (bar.Player.Index == 0)
            {
                r.anchorMin = Vector2.zero;
                r.anchorMax = new Vector2(ratio, 1);
            }
            else
            {
                r.anchorMin = new Vector2(1 - ratio, 0);
                r.anchorMax = Vector2.one;
            }
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        public void SetRound(int round) => roundText.text = $"ROUND {round}";

        public IEnumerator AnimateHp(int index, int from, int to)
        {
            var bar = bars[index];
            float max = bar.Player.MaxHp;
            SetRatio(bar, bar.Fill, to / max);
            var home = bar.Panel.anchoredPosition;

            // shake panel + count down number, then let the white chip catch up
            yield return Tween.Run(0.35f, t =>
            {
                bar.Panel.anchoredPosition = home + Random.insideUnitCircle * 10f * (1 - t);
                bar.HpText.text = $"{Mathf.RoundToInt(Mathf.Lerp(from, to, t))} / {bar.Player.MaxHp}";
            });
            bar.Panel.anchoredPosition = home;
            yield return new WaitForSeconds(0.15f);
            yield return Tween.Run(0.5f, t => SetRatio(bar, bar.Chip, Mathf.Lerp(from, to, t) / max), Ease.OutQuad);
        }
    }
}
