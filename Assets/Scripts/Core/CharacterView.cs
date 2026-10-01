using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MiniParty
{
    /// <summary>Procedurally drawn blob character. Pivot is bottom-center so anchoredPosition = feet position.</summary>
    public class CharacterView : MonoBehaviour
    {
        public enum Face { Normal, Happy, Hurt }

        public RectTransform Rt { get; private set; }
        public CharacterDef Def { get; private set; }
        public float Size { get; private set; }

        /// <summary>Scale applied to the body (squash / flatten). Idle breathing is layered on top.</summary>
        public Vector3 BaseScale = Vector3.one;
        public bool Idle = true;

        RectTransform visual;
        Image body, belly;
        readonly GameObject[] eyes = new GameObject[2];
        readonly Text[] eyeAlt = new Text[2];
        Image mouth;
        float idleT;
        Coroutine hitRoutine;
        bool grey;

        public static CharacterView Create(Transform parent, CharacterDef def, float size = 220f)
        {
            var rt = UIKit.Rect(def.Name, parent);
            rt.Place(Vector2.zero, new Vector2(size, size), pivot: new Vector2(0.5f, 0f));
            var v = rt.gameObject.AddComponent<CharacterView>();
            v.Build(def, size);
            return v;
        }

        RectTransform Part(Image img, float x, float y, float w, float h)
        {
            return img.rectTransform.Place(new Vector2(x, y), new Vector2(w, h), new Vector2(0.5f, 0f));
        }

        void Build(CharacterDef def, float s)
        {
            Rt = (RectTransform)transform;
            Def = def;
            Size = s;
            idleT = Random.value * 10f;

            var shadow = UIKit.Img("Shadow", Rt, new Color(0, 0, 0, 0.25f), UIKit.Circle);
            Part(shadow, 0, 0, s * 0.8f, s * 0.16f);

            visual = UIKit.Rect("Visual", Rt);
            visual.Place(Vector2.zero, new Vector2(s, s), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            switch (def.Feature)
            {
                case 1: // ears
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Part(UIKit.Img("Ear", visual, def.Body, UIKit.Circle), i * s * 0.3f, s * 0.86f, s * 0.34f, s * 0.34f);
                        Part(UIKit.Img("EarIn", visual, def.Belly, UIKit.Circle), i * s * 0.3f, s * 0.86f, s * 0.18f, s * 0.18f);
                    }
                    break;
                case 2: // antenna
                    Part(UIKit.Img("Stem", visual, def.Body, UIKit.Rounded), 0, s * 1.02f, s * 0.05f, s * 0.26f);
                    Part(UIKit.Img("Ball", visual, def.Belly, UIKit.Circle), 0, s * 1.14f, s * 0.15f, s * 0.15f);
                    break;
                case 3: // leaf
                    var leaf = Part(UIKit.Img("Leaf", visual, Palette.Hex("#3FB950"), UIKit.Circle), s * 0.06f, s * 1.02f, s * 0.18f, s * 0.34f);
                    leaf.localRotation = Quaternion.Euler(0, 0, -30);
                    break;
                case 4: // horns
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var horn = Part(UIKit.Img("Horn", visual, def.Belly, UIKit.Circle), i * s * 0.24f, s * 0.9f, s * 0.12f, s * 0.26f);
                        horn.localRotation = Quaternion.Euler(0, 0, -i * 20);
                    }
                    break;
            }

            body = UIKit.Img("Body", visual, def.Body, UIKit.Circle);
            Part(body, 0, s * 0.5f, s, s);
            body.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            body.rectTransform.anchoredPosition = new Vector2(0, s * 0.5f);

            belly = UIKit.Img("Belly", visual, def.Belly, UIKit.Circle);
            Part(belly, 0, s * 0.25f, s * 0.6f, s * 0.4f);

            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1 : 1) * s * 0.17f;
                var eye = UIKit.Img("Eye", visual, Color.white, UIKit.Circle);
                Part(eye, x, s * 0.55f, s * 0.2f, s * 0.2f);
                var pupil = UIKit.Img("Pupil", eye.transform, Palette.Ink, UIKit.Circle);
                pupil.rectTransform.Place(new Vector2(0, -s * 0.01f), new Vector2(s * 0.11f, s * 0.11f));
                eyes[i] = eye.gameObject;

                var alt = UIKit.Label("EyeAlt", visual, "", Mathf.RoundToInt(s * 0.2f), Palette.Ink);
                alt.rectTransform.Place(new Vector2(x, s * 0.56f), new Vector2(s * 0.3f, s * 0.3f), new Vector2(0.5f, 0f));
                alt.gameObject.SetActive(false);
                eyeAlt[i] = alt;

                var cheek = UIKit.Img("Cheek", visual, Palette.Hex("#FF6B8A").WithAlpha(0.45f), UIKit.Circle);
                Part(cheek, (i == 0 ? -1 : 1) * s * 0.31f, s * 0.44f, s * 0.13f, s * 0.08f);
            }

            mouth = UIKit.Img("Mouth", visual, Palette.Ink, UIKit.Circle);
            Part(mouth, 0, s * 0.42f, s * 0.1f, s * 0.05f);
        }

        public void SetFace(Face f)
        {
            for (int i = 0; i < 2; i++)
            {
                eyes[i].SetActive(f == Face.Normal);
                eyeAlt[i].gameObject.SetActive(f != Face.Normal);
                eyeAlt[i].text = f == Face.Happy ? "^" : (i == 0 ? ">" : "<");
            }
            var m = mouth.rectTransform;
            switch (f)
            {
                case Face.Normal:
                    m.sizeDelta = new Vector2(Size * 0.1f, Size * 0.05f);
                    mouth.color = Palette.Ink;
                    break;
                case Face.Happy:
                    m.sizeDelta = new Vector2(Size * 0.18f, Size * 0.12f);
                    mouth.color = Palette.Hex("#D93A56");
                    break;
                case Face.Hurt:
                    m.sizeDelta = new Vector2(Size * 0.12f, Size * 0.09f);
                    mouth.color = Palette.Ink;
                    break;
            }
        }

        public void SetGrey(bool on)
        {
            grey = on;
            body.color = on ? Palette.Hex("#7A7F99") : Def.Body;
            belly.color = on ? Palette.Hex("#A9ADC2") : Def.Belly;
        }

        /// <summary>Temporarily recolor the body (null restores the character's own colors).</summary>
        public void Tint(Color? color)
        {
            if (color == null)
            {
                SetGrey(grey);
                return;
            }
            body.color = color.Value;
            belly.color = Color.Lerp(color.Value, Color.white, 0.55f);
        }

        void Update()
        {
            if (Idle)
            {
                idleT += Time.deltaTime * 3.2f;
                float b = Mathf.Sin(idleT) * 0.03f;
                visual.localScale = new Vector3(BaseScale.x * (1f - b), BaseScale.y * (1f + b), 1f);
            }
            else
            {
                visual.localScale = BaseScale;
            }
        }

        // ---------- Animations ----------

        public IEnumerator HopTo(Vector2 target, float duration, int hops = 3, float height = 40f)
        {
            var start = Rt.anchoredPosition;
            return Tween.Run(duration, t =>
            {
                var p = Vector2.Lerp(start, target, t);
                p.y += Mathf.Abs(Mathf.Sin(t * Mathf.PI * hops)) * height;
                Rt.anchoredPosition = p;
            });
        }

        public IEnumerator Jump(float height, float duration)
        {
            var start = Rt.anchoredPosition;
            return Tween.Run(duration, t => Rt.anchoredPosition = start + Vector2.up * (4f * height * t * (1f - t)));
        }

        public IEnumerator ScaleTo(Vector3 target, float duration, System.Func<float, float> ease = null)
        {
            var from = BaseScale;
            return Tween.Run(duration, t => BaseScale = Vector3.LerpUnclamped(from, target, t), ease ?? Ease.OutQuad);
        }

        /// <summary>Small impact reaction: white flash + squash.</summary>
        public void Hit()
        {
            if (hitRoutine != null) StopCoroutine(hitRoutine);
            hitRoutine = StartCoroutine(HitRoutine());
        }

        /// <summary>Quick squash-and-recover without the flash (e.g. pulling a rope).</summary>
        public void Pulse(Vector3 scale, float duration = 0.15f)
        {
            if (hitRoutine != null) StopCoroutine(hitRoutine);
            SetGrey(grey);
            hitRoutine = StartCoroutine(PulseRoutine(scale, duration));
        }

        IEnumerator PulseRoutine(Vector3 scale, float duration)
        {
            BaseScale = scale;
            yield return ScaleTo(Vector3.one, duration);
        }

        IEnumerator HitRoutine()
        {
            body.color = Color.white;
            BaseScale = new Vector3(1.15f, 0.85f, 1f);
            yield return new WaitForSeconds(0.06f);
            SetGrey(grey);
            yield return ScaleTo(Vector3.one, 0.18f, Ease.OutBack);
        }
    }
}
