using System;
using System.Collections;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Tiny coroutine tweens for UI (unscaled time, no tween package).</summary>
    public static class UiTween
    {
        public static IEnumerator Run(float duration, Action<float> step)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(t / duration));
                yield return null;
            }
            step(1f);
        }

        /// <summary>Scale 0.85 → 1 with overshoot and alpha 0 → 1.</summary>
        public static IEnumerator PopIn(Transform t, CanvasGroup group, float duration = 0.28f)
        {
            return Run(duration, k =>
            {
                if (t != null) t.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, Anim.OutBack(k));
                if (group != null) group.alpha = Mathf.Clamp01(k * 2f);
            });
        }

        public static IEnumerator Fade(CanvasGroup group, float from, float to, float duration = 0.18f)
        {
            return Run(duration, k => group.alpha = Mathf.Lerp(from, to, k));
        }

        /// <summary>Quick scale punch (counter tick, refused tap).</summary>
        public static IEnumerator Punch(Transform t, float amount = 0.18f, float duration = 0.25f)
        {
            return Run(duration, k => t.localScale = Vector3.one * (1f + amount * Mathf.Sin(k * Mathf.PI)));
        }

        /// <summary>Horizontal shake ("no").</summary>
        public static IEnumerator Shake(RectTransform t, float amplitude = 18f, float duration = 0.3f)
        {
            var basePos = t.anchoredPosition;
            return Run(duration, k =>
                t.anchoredPosition = basePos + new Vector2(Mathf.Sin(k * 40f) * amplitude * (1f - k), 0f));
        }
    }
}
