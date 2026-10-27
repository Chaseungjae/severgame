using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// Step 4: over the blurred minigame, both characters walk to the center. The winner gets a "WIN!" label,
    /// the loser receives the minigame's themed punishment and loses HP, then both walk back out.
    /// </summary>
    public class PodiumSequence
    {
        public const float GroundY = -330f;
        const float OffscreenX = 1150f;

        public readonly CharacterView[] Views = new CharacterView[2];
        public int Winner { get; private set; }
        public int Loser => 1 - Winner;
        public bool LoserDead { get; private set; }

        readonly RectTransform stage;
        readonly HpHud hud;
        readonly PlayerData[] players;
        readonly MonoBehaviour runner;
        readonly List<GameObject> spawned = new List<GameObject>();

        Text banner, winLabel;

        public PodiumSequence(RectTransform stage, HpHud hud, PlayerData[] players, MonoBehaviour runner)
        {
            this.stage = stage;
            this.hud = hud;
            this.players = players;
            this.runner = runner;
        }

        static Vector2 SlotPos(int p) => new Vector2(p == 0 ? -280f : 280f, GroundY);

        public T Track<T>(T c) where T : Component
        {
            spawned.Add(c.gameObject);
            return c;
        }

        public IEnumerator Run(int winner, MiniGameBase game, int damage)
        {
            Winner = winner;

            // both characters hop in from their own side
            for (int p = 0; p < 2; p++)
            {
                Views[p] = Track(CharacterView.Create(stage, players[p].Character, 230));
                Views[p].Rt.anchoredPosition = new Vector2(p == 0 ? -OffscreenX : OffscreenX, GroundY);
            }
            var a = runner.StartCoroutine(Views[0].HopTo(SlotPos(0), 0.9f));
            var b = runner.StartCoroutine(Views[1].HopTo(SlotPos(1), 0.9f));
            yield return a;
            yield return b;

            // winner: just a WIN! label
            var wv = Views[winner];
            wv.SetFace(CharacterView.Face.Happy);
            winLabel = UIKit.Label("Win", wv.Rt, "WIN!", 96, Palette.Gold);
            winLabel.rectTransform.Place(new Vector2(0, 290), new Vector2(400, 130), new Vector2(0.5f, 0f));
            winLabel.rectTransform.localRotation = Quaternion.Euler(0, 0, 6);
            UIKit.Shadow(winLabel, 6f, 0.6f);
            Juice.Sparkle(stage, wv.Rt.anchoredPosition + new Vector2(0, wv.Size + 110f), Palette.Gold, 1.2f);
            yield return Tween.Pop(winLabel.transform, 2.2f, 0.35f);
            yield return wv.Jump(70, 0.35f);
            yield return new WaitForSeconds(0.25f);

            // punishment
            banner = Track(UIKit.Label("Banner", stage, $"{players[Loser].Tag} 벌칙 타임!", 88, Palette.Red));
            banner.rectTransform.Place(new Vector2(0, 300), new Vector2(1200, 120));
            UIKit.Shadow(banner, 6f, 0.5f);
            yield return Tween.Pop(banner.transform, 1.8f, 0.3f);
            var ctx = new PunishContext(stage, Views[Loser], wv, runner);
            yield return game.Punish(ctx);

            // damage
            var loser = players[Loser];
            int from = loser.Hp;
            loser.Hp = Mathf.Max(0, loser.Hp - damage);
            runner.StartCoroutine(DamagePopup(Views[Loser], damage));
            Juice.Vignette(Palette.Red.WithAlpha(0.55f), 0.7f);
            Juice.Shake(0.35f);
            yield return hud.AnimateHp(Loser, from, loser.Hp);
            yield return new WaitForSeconds(0.8f);

            LoserDead = loser.Hp <= 0;
            if (LoserDead) yield break;

            // both walk back out
            wv.SetFace(CharacterView.Face.Normal);
            runner.StartCoroutine(Tween.FadeGraphic(banner, 0f, 0.4f));
            runner.StartCoroutine(Tween.FadeGraphic(winLabel, 0f, 0.3f));
            a = runner.StartCoroutine(Views[0].HopTo(new Vector2(-OffscreenX, GroundY), 0.8f));
            b = runner.StartCoroutine(Views[1].HopTo(new Vector2(OffscreenX, GroundY), 0.8f));
            yield return a;
            yield return b;
        }

        IEnumerator DamagePopup(CharacterView target, int dmg)
        {
            var label = Track(UIKit.Label("Damage", stage, $"-{dmg}", 90, Palette.Red));
            var start = target.Rt.anchoredPosition + new Vector2(0, target.Size + 40);
            label.rectTransform.Place(start, new Vector2(300, 120));
            UIKit.Shadow(label, 5f, 0.6f);
            runner.StartCoroutine(Tween.Pop(label.transform, 2f, 0.3f));
            yield return Tween.Run(1.2f, t =>
            {
                label.rectTransform.anchoredPosition = start + Vector2.up * 80f * t;
                label.color = Palette.Red.WithAlpha(t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f);
            });
        }

        /// <summary>Final showdown when the loser's HP hits 0 (step 6).</summary>
        public IEnumerator GameOver(bool autoContinue)
        {
            var wv = Views[Winner];
            var lv = Views[Loser];

            banner.text = "최종 우승!";
            banner.color = Palette.Gold;
            runner.StartCoroutine(Tween.Pop(banner.transform, 2f, 0.4f));

            var sub = Track(UIKit.Label("Winner", stage, $"{players[Winner].Tag} · {players[Winner].Character.Name}", 52, players[Winner].Color));
            sub.rectTransform.Place(new Vector2(0, 205), new Vector2(900, 70));

            lv.SetFace(CharacterView.Face.Hurt);
            lv.SetGrey(true);
            lv.Idle = false;
            runner.StartCoroutine(lv.ScaleTo(new Vector3(1.4f, 0.35f, 1f), 0.3f));
            var ko = Track(UIKit.Label("KO", stage, "K.O.", 72, Palette.Red));
            ko.rectTransform.Place(lv.Rt.anchoredPosition + new Vector2(0, 160), new Vector2(300, 100));
            ko.rectTransform.localRotation = Quaternion.Euler(0, 0, -10);
            runner.StartCoroutine(Tween.Pop(ko.transform, 2.5f, 0.3f));

            var prompt = Track(UIKit.Label("Prompt", stage, "Space / Enter 로 로비로", 32, Color.white, style: FontStyle.Normal));
            prompt.rectTransform.Place(new Vector2(0, -470), new Vector2(800, 50));

            float t = 0f;
            float nextParty = 0f;
            while (true)
            {
                t += Time.deltaTime;
                if (t >= nextParty)
                {
                    Confetti.Burst(stage, wv.Rt.anchoredPosition + new Vector2(0, 260), 50);
                    Juice.Sparkle(stage, wv.Rt.anchoredPosition + new Vector2(0, 200), Palette.Gold, 1.4f);
                    runner.StartCoroutine(wv.Jump(90, 0.4f));
                    nextParty = t + 1.6f;
                }
                prompt.color = Color.white.WithAlpha(0.5f + 0.5f * Mathf.Sin(t * 4f));
                if (t > 1f && (Controls.AnyConfirm || (autoContinue && t > 6f))) break;
                yield return null;
            }
        }

        public void Cleanup()
        {
            foreach (var go in spawned)
                if (go != null) Object.Destroy(go);
            spawned.Clear();
        }
    }
}
