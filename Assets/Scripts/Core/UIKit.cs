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

        public static Font Font
        {
            get
            {
                if (font == null)
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 48);
                return font;
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
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
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
            btn.onClick.AddListener(() => onClick());
            var label = Label("Label", img.transform, text, fontSize, Color.white);
            label.rectTransform.Stretch();
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
