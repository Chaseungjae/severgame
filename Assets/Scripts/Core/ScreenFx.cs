using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>Full-screen black fade used between rounds.</summary>
    public class ScreenFader : MonoBehaviour
    {
        CanvasGroup group;

        public static ScreenFader Create(Transform parent)
        {
            var img = UIKit.Img("Fader", parent, Color.black);
            img.raycastTarget = true;
            img.rectTransform.Stretch();
            var f = img.gameObject.AddComponent<ScreenFader>();
            f.group = img.gameObject.AddComponent<CanvasGroup>();
            f.group.alpha = 1f;
            return f;
        }

        public IEnumerator FadeTo(float alpha, float duration)
        {
            float from = group.alpha;
            group.blocksRaycasts = true;
            yield return Tween.Run(duration, t => group.alpha = Mathf.Lerp(from, alpha, t), Ease.InOutQuad);
            group.blocksRaycasts = alpha > 0.01f;
        }
    }

    /// <summary>
    /// Freezes the current frame into a blurred backdrop (progressive downsample → bilinear upscale)
    /// so the finished minigame stays visible, softly blurred, behind the podium scene.
    /// </summary>
    public class BlurBackdrop : MonoBehaviour
    {
        CanvasGroup group;
        RawImage raw;
        RenderTexture result;

        public static BlurBackdrop Create(Transform parent)
        {
            var rt = UIKit.Rect("BlurBackdrop", parent).Stretch();
            var b = rt.gameObject.AddComponent<BlurBackdrop>();
            b.group = rt.gameObject.AddComponent<CanvasGroup>();
            b.group.alpha = 0f;
            b.group.blocksRaycasts = false;
            b.raw = UIKit.Rect("Snapshot", rt).Stretch().gameObject.AddComponent<RawImage>();
            b.raw.raycastTarget = false;
            UIKit.Img("Dim", rt, new Color(0.05f, 0.06f, 0.12f, 0.35f)).rectTransform.Stretch();
            return b;
        }

        public IEnumerator CaptureAndShow(float duration, int downsamplePasses = 3)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            Release();

            int w = shot.width, h = shot.height;
            Texture current = shot;
            RenderTexture previous = null;
            for (int i = 0; i < downsamplePasses; i++)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
                var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                tmp.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, tmp);
                if (previous != null) RenderTexture.ReleaseTemporary(previous);
                previous = tmp;
                current = tmp;
            }
            result = new RenderTexture(w * 2, h * 2, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            Graphics.Blit(current, result);
            if (previous != null) RenderTexture.ReleaseTemporary(previous);
            Destroy(shot);

            raw.texture = result;
            yield return Tween.Run(duration, t => group.alpha = t, Ease.OutQuad);
        }

        public void Clear()
        {
            group.alpha = 0f;
            raw.texture = null;
            Release();
        }

        void Release()
        {
            if (result == null) return;
            result.Release();
            Destroy(result);
            result = null;
        }

        void OnDestroy() => Release();
    }

    public class Confetti : MonoBehaviour
    {
        static readonly Color[] Colors =
        {
            Palette.Gold, Palette.Red, Palette.Green, Palette.Hex("#4C8DFF"), Palette.Hex("#FF7EB3"), Color.white
        };

        RectTransform rt;
        Image img;
        Vector2 velocity;
        float spin, life;

        public static void Burst(RectTransform parent, Vector2 origin, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var img = UIKit.Img("Confetti", parent, Colors[Random.Range(0, Colors.Length)]);
                img.rectTransform.Place(origin, new Vector2(Random.Range(10f, 18f), Random.Range(16f, 28f)));
                var c = img.gameObject.AddComponent<Confetti>();
                c.rt = img.rectTransform;
                c.img = img;
                c.velocity = new Vector2(Random.Range(-600f, 600f), Random.Range(500f, 1200f));
                c.spin = Random.Range(-720f, 720f);
                c.life = Random.Range(1.6f, 2.4f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            velocity.y -= 1800f * dt;
            velocity.x *= 1f - 1.2f * dt;
            rt.anchoredPosition += velocity * dt;
            rt.Rotate(0, 0, spin * dt);
            life -= dt;
            if (life < 0.5f) img.color = img.color.WithAlpha(Mathf.Max(0, life / 0.5f));
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
