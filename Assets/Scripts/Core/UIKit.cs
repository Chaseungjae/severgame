using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    public static class Palette
    {
        public static readonly Color Bg = Hex("#1E2240");
        public static readonly Color Panel = Hex("#2A2F55");
        public static readonly Color PanelLight = Hex("#383E6E");
        public static readonly Color Text = Color.white;
        public static readonly Color Muted = Hex("#9AA3C7");
        public static readonly Color Gold = Hex("#FFC83D");
        public static readonly Color Red = Hex("#FF5A5F");
        public static readonly Color Green = Hex("#4ADE80");
        public static readonly Color Ink = Hex("#2A2F55");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
    }

    /// <summary>Runtime UI construction helpers. All UI is built in code so no art assets are needed.</summary>
    public static class UIKit
    {
        static Font font;
        static Sprite circle, rounded;

        /// <summary>OS fallback font (Malgun Gothic), used when no theme font is set up.</summary>
        public static Font Font
        {
            get
            {
                if (font == null)
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 48);
                return font;
            }
        }

        /// <summary>UI font: the theme font (Jua) when set up, else the OS font.</summary>
        public static Font TitleFont
        {
            get
            {
                var theme = UiTheme.Current;
                return theme != null && theme.titleFont != null ? theme.titleFont : Font;
            }
        }

        public static Sprite Circle => circle != null ? circle : circle = MakeRounded(128, 64f, false);
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(64, 20f, true);

        static Sprite ring, glow, vignette;

        /// <summary>Thin circle outline (impact rings).</summary>
        public static Sprite Ring => ring != null ? ring : ring = MakeRadial(128, "Ring", d => Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.08f));
        /// <summary>Soft round glow that fades to the edge.</summary>
        public static Sprite Glow => glow != null ? glow : glow = MakeRadial(128, "Glow", d => Mathf.Pow(Mathf.Clamp01(1f - d), 2f));
        /// <summary>Transparent center, opaque edges (screen vignette).</summary>
        public static Sprite Vignette => vignette != null ? vignette : vignette = MakeRadial(128, "Vignette", d => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.55f) / 0.6f)));

        static Sprite gradient, dots;

        /// <summary>Vertical fade: opaque at the top, transparent at the bottom.</summary>
        public static Sprite Gradient
        {
            get
            {
                if (gradient != null) return gradient;
                var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Gradient" };
                for (int y = 0; y < 64; y++) tex.SetPixel(0, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, y / 63f)));
                tex.Apply();
                return gradient = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100);
            }
        }

        /// <summary>Tileable 40px cell with one soft dot (background pattern).</summary>
        public static Sprite Dots
        {
            get
            {
                if (dots != null) return dots;
                const int S = 40;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Dots" };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(2.6f - d) * 255));
                }
                tex.SetPixels32(px);
                tex.Apply();
                return dots = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            }
        }

        /// <summary>
        /// Full-screen game background: base color, a soft light from the top, a faint dot pattern and a vignette.
        /// Returns the base image; anything parented to it later draws on top of the decoration.
        /// </summary>
        public static Image Backdrop(Transform parent, string name = "Bg")
        {
            var bg = Img(name, parent, Palette.Bg);
            bg.rectTransform.Stretch();
            var light = Img("TopLight", bg.transform, Palette.Hex("#3A4290").WithAlpha(0.45f), Gradient);
            light.rectTransform.Stretch();
            light.rectTransform.anchorMin = new Vector2(0, 0.35f);
            var pattern = Img("Dots", bg.transform, Color.white.WithAlpha(0.035f), Dots);
            pattern.type = Image.Type.Tiled;
            pattern.rectTransform.Stretch();
            var vignette = Img("Vignette", bg.transform, new Color(0.02f, 0.02f, 0.08f, 0.55f), Vignette);
            vignette.rectTransform.Stretch();
            vignette.rectTransform.offsetMin = new Vector2(-260, -200);
            vignette.rectTransform.offsetMax = new Vector2(260, 200);
            return bg;
        }

        /// <summary>Soft drop shadow drawn just behind a (rounded) panel. Call right after creating the panel.</summary>
        public static Image DropShadow(RectTransform panel, float depth = 10f, float alpha = 0.32f)
        {
            var shadow = Img(panel.name + "Shadow", panel.parent, new Color(0.02f, 0.02f, 0.08f, alpha), Rounded);
            var rt = shadow.rectTransform;
            rt.anchorMin = panel.anchorMin;
            rt.anchorMax = panel.anchorMax;
            rt.pivot = panel.pivot;
            rt.sizeDelta = panel.sizeDelta + new Vector2(6, 6);
            rt.anchoredPosition = panel.anchoredPosition + new Vector2(0, -depth);
            rt.SetSiblingIndex(panel.GetSiblingIndex());
            return shadow;
        }

        /// <summary>Alpha as a function of the normalized distance from the center (0 center, 1 edge).</summary>
        static Sprite MakeRadial(int size, string name, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            var px = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(d)) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        static Sprite MakeRounded(int size, float radius, bool sliced)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = sliced ? "Rounded" : "Circle"
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, radius, size - radius);
                float cy = Mathf.Clamp(fy, radius, size - radius);
                float d = Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 pos, Vector2 size, Vector2? anchor = null, Vector2? pivot = null)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Img(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            var t = Rect(name, parent).gameObject.AddComponent<Text>();
            // every label uses the theme font (Jua); it is already heavy, so no synthetic bold on top
            bool themed = TitleFont != Font;
            t.font = TitleFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = themed ? FontStyle.Normal : style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(200, size * 1.4f);
            return t;
        }

        public static Button Button(string name, Transform parent, string text, Vector2 pos, Vector2 size, int fontSize,
            Color bg, System.Action onClick)
        {
            var img = Img(name, parent, bg, Rounded);
            img.raycastTarget = true;
            img.rectTransform.Place(pos, size);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Sfx.Click);
            btn.onClick.AddListener(() => onClick());
            var label = Label("Label", img.transform, text, fontSize, Color.white);
            label.rectTransform.Stretch();

            var theme = UiTheme.Current;
            if (theme != null && theme.button != null)
            {
                // Kenney button: light grey 9-slice with a bottom lip, tinted with the button color
                img.sprite = theme.button;
                img.type = Image.Type.Sliced;
                img.color = Color.Lerp(bg, Color.white, 0.12f);
                label.rectTransform.offsetMin = new Vector2(0, 6); // sit above the lip
                Shadow(label, 2f, 0.45f);
            }
            return btn;
        }

        public static void Shadow(Graphic g, float dist = 4f, float alpha = 0.35f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, alpha);
            s.effectDistance = new Vector2(0, -dist);
        }
    }
}

namespace MiniParty
{
    /// <summary>Keeps a drop shadow (a sibling image) glued to its panel when the panel is moved, resized or scaled.</summary>
    public class FollowShadow : MonoBehaviour
    {
        public RectTransform Shadow;
        public float Depth = 10f;

        void LateUpdate()
        {
            if (Shadow == null) return;
            var rt = (RectTransform)transform;
            Shadow.anchorMin = rt.anchorMin;
            Shadow.anchorMax = rt.anchorMax;
            Shadow.pivot = rt.pivot;
            Shadow.sizeDelta = rt.sizeDelta + new Vector2(6, 6);
            Shadow.anchoredPosition = rt.anchoredPosition + new Vector2(0, -Depth);
            Shadow.localScale = rt.localScale;
            Shadow.localRotation = rt.localRotation;
        }

        void OnEnable() { if (Shadow) Shadow.gameObject.SetActive(true); }
        void OnDisable() { if (Shadow) Shadow.gameObject.SetActive(false); }
        void OnDestroy() { if (Shadow) Destroy(Shadow.gameObject); }
    }
}
