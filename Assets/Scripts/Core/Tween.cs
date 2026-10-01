using System;
using System.Collections;
using UnityEngine;

namespace MiniParty
{
    public static class Tween
    {
        public static IEnumerator Run(float duration, Action<float> step, Func<float, float> ease = null)
        {
            ease ??= Ease.Linear;
            float t = 0f;
            while (t < duration)
            {
                step(ease(t / duration));
                yield return null;
                t += Time.deltaTime;
            }
            step(ease(1f));
        }

        public static IEnumerator Pop(Transform tr, float from = 1.35f, float duration = 0.3f)
        {
            return Run(duration, t => { if (tr) tr.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, t); }, Ease.OutBack);
        }

        public static IEnumerator FadeGraphic(UnityEngine.UI.Graphic g, float to, float duration)
        {
            float from = g.color.a;
            return Run(duration, t => { var c = g.color; c.a = Mathf.Lerp(from, to, t); g.color = c; });
        }
    }

    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float InQuad(float t) => t * t;
        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
