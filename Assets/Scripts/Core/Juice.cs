using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>
    /// "Game feel" toolkit (in the spirit of Feel / MMFeedbacks), built for the code-generated UI:
    ///  - trauma-based screen shake, hit-stop (freeze frame), full-screen flash, damage vignette
    ///  - impact rings, glow pops, anime speed lines
    ///  - pooled UI particles: sparks, dust, stars, debris
    ///  - elastic punch scale
    /// <see cref="Impact"/> combines them into one hit. <see cref="Intensity"/> scales everything (0 = off).
    /// </summary>
    public static class Juice
    {
        public static float Intensity = 1f;

        static JuiceRunner runner;
        static RectTransform shakeRoot, fxLayer;

        /// <summary>Called once by the GameManager: what to shake, and where full-screen effects go.</summary>
        public static void Init(RectTransform shakeTarget, RectTransform screenFxLayer)
        {
            shakeRoot = shakeTarget;
            fxLayer = screenFxLayer;
            if (runner == null)
            {
                var go = new GameObject("Juice");
                Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<JuiceRunner>();
            }
            runner.ShakeRoot = shakeRoot;
        }

        static bool Ready => runner != null && Intensity > 0f;

        // ---------- camera-ish ----------

        /// <summary>Adds trauma (0..1). Shake strength is trauma², so small hits stay subtle and big hits feel big.</summary>
        public static void Shake(float trauma)
        {
            if (!Ready) return;
            runner.Trauma = Mathf.Clamp01(runner.Trauma + trauma * Intensity);
        }

        /// <summary>Freeze frame: game time stops for a moment so the hit "lands". Uses real time.</summary>
        public static void HitStop(float seconds)
        {
            if (!Ready || seconds <= 0f) return;
            runner.HitStop(seconds * Mathf.Min(1f, Intensity));
        }

        public static void Flash(Color color, float duration = 0.18f)
        {
            if (!Ready || fxLayer == null) return;
            var img = UIKit.Img("Flash", fxLayer, color);
            img.rectTransform.Stretch();
            runner.StartCoroutine(FadeOut(img, color.a * Mathf.Min(1f, Intensity), duration));
        }

        /// <summary>Colored screen edges (e.g. red when taking damage).</summary>
        public static void Vignette(Color color, float duration = 0.6f)
        {
            if (!Ready || fxLayer == null) return;
            var img = UIKit.Img("Vignette", fxLayer, color, UIKit.Vignette);
            img.rectTransform.Stretch();
            img.rectTransform.offsetMin = new Vector2(-200, -200);
            img.rectTransform.offsetMax = new Vector2(200, 200);
            runner.StartCoroutine(FadeOut(img, color.a * Mathf.Min(1f, Intensity), duration));
        }

        static IEnumerator FadeOut(Graphic g, float from, float duration)
        {
            float t = 0f;
            while (t < duration && g != null)
            {
                g.color = g.color.WithAlpha(from * (1f - t / duration));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (g != null) Object.Destroy(g.gameObject);
        }

        // ---------- shapes ----------

        public static void Ring(RectTransform parent, Vector2 pos, Color color, float maxSize = 300f, float duration = 0.35f)
        {
            if (!Ready || parent == null) return;
            var img = UIKit.Img("Ring", parent, color, UIKit.Ring);
            img.rectTransform.Place(pos, Vector2.one * 20f);
            runner.StartCoroutine(Expand(img, 20f, maxSize, duration));
        }

        public static void Glow(RectTransform parent, Vector2 pos, Color color, float size = 260f, float duration = 0.3f)
        {
            if (!Ready || parent == null) return;
            var img = UIKit.Img("Glow", parent, color, UIKit.Glow);
            img.rectTransform.Place(pos, Vector2.one * size * 0.5f);
            runner.StartCoroutine(Expand(img, size * 0.5f, size, duration));
        }

        static IEnumerator Expand(Image img, float from, float to, float duration)
        {
            var c = img.color;
            float t = 0f;
            while (t < duration && img != null)
            {
                float k = t / duration;
                img.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(from, to, Ease.OutQuad(k));
                img.color = c.WithAlpha(c.a * (1f - k));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (img != null) Object.Destroy(img.gameObject);
        }

        /// <summary>Anime impact lines bursting out from a point.</summary>
        public static void SpeedLines(RectTransform parent, Vector2 pos, Color color, int count = 18, float radius = 120f)
        {
            if (!Ready || parent == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                runner.Emit(parent, new JuiceRunner.Particle
                {
                    Pos = pos + dir * radius,
                    Vel = dir * Random.Range(900f, 1500f),
                    Drag = 6f,
                    Life = Random.Range(0.18f, 0.28f),
                    Size = new Vector2(Random.Range(70f, 140f), Random.Range(5f, 9f)),
                    EndScale = 0.2f,
                    Color = color,
                    Sprite = UIKit.Rounded,
                    AlignToVelocity = true,
                });
            }
        }

        // ---------- particles ----------

        public enum Style { Sparks, Dust, Stars, Debris, Hearts }

        public static void Burst(RectTransform parent, Vector2 pos, Style style, int count = 16, Color? tint = null, float power = 1f)
        {
            if (!Ready || parent == null) return;
            count = Mathf.RoundToInt(count * Mathf.Clamp(Intensity, 0.3f, 1.5f));
            for (int i = 0; i < count; i++)
            {
                var p = new JuiceRunner.Particle { Pos = pos };
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                switch (style)
                {
                    case Style.Sparks:
                        p.Vel = dir * Random.Range(500f, 1100f) * power;
                        p.Drag = 5f;
                        p.Gravity = 900f;
                        p.Life = Random.Range(0.25f, 0.45f);
                        p.Size = new Vector2(Random.Range(26f, 46f), Random.Range(5f, 8f));
                        p.EndScale = 0.1f;
                        p.Color = tint ?? (Random.value < 0.5f ? Palette.Gold : Color.white);
                        p.Sprite = UIKit.Rounded;
                        p.AlignToVelocity = true;
                        break;
                    case Style.Dust:
                        dir = new Vector2(Mathf.Cos(a), Mathf.Abs(Mathf.Sin(a)) * 0.35f); // mostly sideways along the ground
                        p.Vel = dir.normalized * Random.Range(150f, 420f) * power;
                        p.Drag = 4f;
                        p.Gravity = -60f;
                        p.Life = Random.Range(0.45f, 0.8f);
                        float s = Random.Range(30f, 64f);
                        p.Size = new Vector2(s, s);
                        p.StartScale = 0.5f;
                        p.EndScale = 1.4f;
                        p.Color = (tint ?? new Color(0.85f, 0.85f, 0.9f)).WithAlpha(0.7f);
                        p.Sprite = UIKit.Circle;
                        break;
                    case Style.Stars:
                        p.Vel = dir * Random.Range(250f, 650f) * power;
                        p.Drag = 3f;
                        p.Gravity = 500f;
                        p.Life = Random.Range(0.5f, 0.9f);
                        float st = Random.Range(18f, 34f);
                        p.Size = new Vector2(st, st);
                        p.Spin = Random.Range(-540f, 540f);
                        p.EndScale = 0.2f;
                        p.Color = tint ?? Palette.Gold;
                        p.Sprite = null; // square rotated = diamond star
                        p.Rotation = 45f;
                        break;
                    case Style.Debris:
                        p.Vel = new Vector2(dir.x, Mathf.Abs(dir.y) + 0.4f).normalized * Random.Range(400f, 900f) * power;
                        p.Gravity = 2200f;
                        p.Life = Random.Range(0.5f, 0.9f);
                        float d = Random.Range(10f, 22f);
                        p.Size = new Vector2(d, d * Random.Range(0.6f, 1.2f));
                        p.Spin = Random.Range(-900f, 900f);
                        p.EndScale = 0.8f;
                        p.Color = tint ?? Palette.Muted;
                        p.Sprite = null;
                        break;
                    case Style.Hearts:
                        p.Vel = new Vector2(dir.x * 200f, Random.Range(200f, 420f)) * power;
                        p.Drag = 1.5f;
                        p.Gravity = -100f;
                        p.Life = Random.Range(0.7f, 1.1f);
                        float h = Random.Range(22f, 36f);
                        p.Size = new Vector2(h, h);
                        p.EndScale = 0.4f;
                        p.Color = tint ?? Palette.Red;
                        p.Sprite = UIKit.Circle;
                        break;
                }
                runner.Emit(parent, p);
            }
        }

        // ---------- transforms ----------

        /// <summary>Elastic "punch": overshoots and wobbles back to the original scale (damped spring).</summary>
        public static void Punch(Transform target, float amount = 0.25f, float duration = 0.35f)
        {
            if (!Ready || target == null) return;
            runner.StartCoroutine(PunchRoutine(target, amount * Intensity, duration));
        }

        static IEnumerator PunchRoutine(Transform target, float amount, float duration)
        {
            var baseScale = target.localScale;
            float t = 0f;
            while (t < duration && target != null)
            {
                float k = t / duration;
                float wobble = Mathf.Sin(k * Mathf.PI * 5f) * (1f - k) * amount;
                target.localScale = baseScale * (1f + wobble);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (target != null) target.localScale = baseScale;
        }

        // ---------- presets ----------

        /// <summary>One big hit: freeze frame, shake, flash, ring, sparks, speed lines (and dust if on the ground).</summary>
        public static void Impact(RectTransform parent, Vector2 pos, float strength = 1f, bool ground = false)
        {
            if (!Ready || parent == null) return;
            strength = Mathf.Clamp01(strength);
            HitStop(0.03f + 0.07f * strength);
            Shake(0.25f + 0.5f * strength);
            if (strength > 0.6f) Flash(Color.white.WithAlpha(0.22f * strength), 0.15f);
            Ring(parent, pos, Color.white.WithAlpha(0.9f), 200f + 260f * strength, 0.35f);
            Glow(parent, pos, Palette.Gold.WithAlpha(0.6f), 180f + 200f * strength, 0.25f);
            Burst(parent, pos, Style.Sparks, Mathf.RoundToInt(10 + 18 * strength), null, 0.7f + 0.5f * strength);
            if (strength > 0.5f) SpeedLines(parent, pos, Color.white.WithAlpha(0.85f), 14 + Mathf.RoundToInt(10 * strength));
            if (ground) Burst(parent, pos, Style.Dust, Mathf.RoundToInt(8 + 10 * strength), null, 0.8f + 0.6f * strength);
        }

        /// <summary>Small hit (a jab, a tile bonk).</summary>
        public static void Hit(RectTransform parent, Vector2 pos, Color? tint = null)
        {
            if (!Ready || parent == null) return;
            Shake(0.12f);
            Ring(parent, pos, (tint ?? Color.white).WithAlpha(0.8f), 140f, 0.22f);
            Burst(parent, pos, Style.Sparks, 7, tint, 0.6f);
        }

        /// <summary>Plays every effect in turn on <paramref name="parent"/> (menu: MiniParty > Effects > 데모).</summary>
        public static void Demo(RectTransform parent)
        {
            if (runner != null && parent != null) runner.StartCoroutine(DemoRoutine(parent));
        }

        static IEnumerator DemoRoutine(RectTransform parent)
        {
            var label = UIKit.Label("DemoLabel", parent, "", 56, Color.white);
            label.rectTransform.Place(new Vector2(0, 360), new Vector2(1200, 80));
            UIKit.Shadow(label, 4f);
            void Title(string t) => label.text = t;
            var wait = new WaitForSecondsRealtime(1.1f);

            Title("Impact — 히트스톱 + 흔들림 + 플래시 + 링 + 스파크 + 집중선 + 먼지");
            Impact(parent, new Vector2(0, -60), 1f, ground: true);
            yield return wait;
            Title("Hit — 작은 타격");
            Hit(parent, new Vector2(-300, 0));
            Hit(parent, new Vector2(300, 0), Palette.Red);
            yield return wait;
            Title("Sparkle — 승리 반짝임");
            Sparkle(parent, Vector2.zero, null, 1.5f);
            yield return wait;
            Title("Burst — Sparks / Dust / Stars / Debris / Hearts");
            Burst(parent, new Vector2(-560, 0), Style.Sparks, 20);
            Burst(parent, new Vector2(-280, -100), Style.Dust, 14);
            Burst(parent, new Vector2(0, 0), Style.Stars, 18);
            Burst(parent, new Vector2(280, -60), Style.Debris, 16);
            Burst(parent, new Vector2(560, 0), Style.Hearts, 12);
            yield return wait;
            Title("SpeedLines — 집중선");
            SpeedLines(parent, Vector2.zero, Color.white, 28);
            yield return wait;
            Title("Vignette — 피격 붉은 테두리");
            Vignette(Palette.Red.WithAlpha(0.7f), 0.9f);
            Shake(0.4f);
            yield return wait;
            Title("Punch — 탄성 펀치 스케일");
            Punch(label.transform, 0.35f, 0.5f);
            yield return wait;
            Object.Destroy(label.gameObject);
        }

        /// <summary>Celebration sparkle (a win, a perfect).</summary>
        public static void Sparkle(RectTransform parent, Vector2 pos, Color? tint = null, float size = 1f)
        {
            if (!Ready || parent == null) return;
            Glow(parent, pos, (tint ?? Palette.Gold).WithAlpha(0.7f), 220f * size, 0.35f);
            Ring(parent, pos, (tint ?? Palette.Gold).WithAlpha(0.9f), 260f * size, 0.4f);
            Burst(parent, pos, Style.Stars, Mathf.RoundToInt(14 * size), tint, size);
        }
    }

    /// <summary>Hidden host for Juice: runs shake, hit-stop and a pooled UI particle system.</summary>
    public class JuiceRunner : MonoBehaviour
    {
        public struct Particle
        {
            public Vector2 Pos, Vel, Size;
            public float Drag, Gravity, Life, Spin, Rotation, StartScale, EndScale;
            public Color Color;
            public Sprite Sprite;
            public bool AlignToVelocity;
        }

        class Live
        {
            public Particle P;
            public float Age;
            public Image Img;
        }

        public RectTransform ShakeRoot;
        public float Trauma;

        readonly List<Live> live = new List<Live>();
        readonly Stack<Image> pool = new Stack<Image>();
        float hitStopUntil;
        float savedTimeScale = 1f;
        bool stopping;
        Vector2 shakeHome;
        bool homeSet;

        public void Emit(RectTransform parent, Particle p)
        {
            if (p.StartScale <= 0f) p.StartScale = 1f;
            var img = pool.Count > 0 ? pool.Pop() : null;
            if (img == null) img = UIKit.Img("Fx", parent, Color.white);
            else
            {
                img.transform.SetParent(parent, false);
                img.gameObject.SetActive(true);
            }
            img.sprite = p.Sprite;
            img.type = p.Sprite != null && p.Sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = p.Color;
            img.rectTransform.Place(p.Pos, p.Size);
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, p.Rotation);
            img.rectTransform.localScale = Vector3.one * p.StartScale;
            img.transform.SetAsLastSibling();
            live.Add(new Live { P = p, Img = img });
        }

        public void HitStop(float seconds)
        {
            if (!stopping)
            {
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                stopping = true;
            }
            Time.timeScale = 0f;
            hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (stopping && Time.unscaledTime >= hitStopUntil)
            {
                stopping = false;
                Time.timeScale = savedTimeScale;
            }

            // trauma shake (perlin noise so it rumbles instead of jittering)
            if (ShakeRoot != null)
            {
                if (!homeSet)
                {
                    shakeHome = ShakeRoot.anchoredPosition;
                    homeSet = true;
                }
                float shake = Trauma * Trauma;
                float t = Time.unscaledTime * 28f;
                var offset = new Vector2(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * 34f * shake;
                ShakeRoot.anchoredPosition = shakeHome + offset;
                ShakeRoot.localRotation = Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 3.3f) - 0.5f) * 2f * 2.2f * shake);
                Trauma = Mathf.Max(0f, Trauma - dt * 1.6f);
            }

            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                if (l.Img == null)
                {
                    live.RemoveAt(i);
                    continue;
                }
                l.Age += dt;
                float k = l.Age / l.P.Life;
                if (k >= 1f)
                {
                    l.Img.gameObject.SetActive(false);
                    l.Img.transform.SetParent(transform, false);
                    pool.Push(l.Img);
                    live.RemoveAt(i);
                    continue;
                }
                ref var p = ref l.P;
                p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);
                p.Vel.y -= p.Gravity * dt;
                p.Pos += p.Vel * dt;
                p.Rotation += p.Spin * dt;
                var rt = l.Img.rectTransform;
                rt.anchoredPosition = p.Pos;
                rt.localRotation = Quaternion.Euler(0, 0, p.AlignToVelocity && p.Vel.sqrMagnitude > 1f ? Mathf.Atan2(p.Vel.y, p.Vel.x) * Mathf.Rad2Deg : p.Rotation);
                rt.localScale = Vector3.one * Mathf.Lerp(p.StartScale, p.EndScale, k);
                l.Img.color = p.Color.WithAlpha(p.Color.a * (k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f));
            }
        }

        void OnDisable()
        {
            if (stopping) Time.timeScale = savedTimeScale;
        }
    }
}
