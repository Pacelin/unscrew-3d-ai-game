using System.Collections;
using TMPro;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Level intro / loading transition: a full-screen gradient with "Level N" (+HARD) and a spinning bolt.
    /// Covers while GameFlow generates the layout on a worker thread and rebuilds the view, then fades out.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TransitionCurtain : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private GameObject hardBadge;
        [SerializeField] private RectTransform content;
        [SerializeField] private float fadeIn = 0.18f;
        [SerializeField] private float fadeOut = 0.32f;
        [Tooltip("The title stays readable at least this long before the fade-out.")]
        [SerializeField] private float minShow = 0.45f;

        private CanvasGroup _group;
        private Coroutine _routine;
        private float _coveredAt;
        private bool _revealPending;

        public float FadeInTime => fadeIn;
        public bool IsCovering => gameObject.activeSelf;

        private CanvasGroup Group => _group != null ? _group : (_group = GetComponent<CanvasGroup>());

        /// <param name="text">Empty: plain transition without a title (menu).</param>
        public void Cover(string text, bool hard, bool instant)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Group.blocksRaycasts = true;
            title.text = text;
            title.gameObject.SetActive(!string.IsNullOrEmpty(text));
            hardBadge.SetActive(hard && !string.IsNullOrEmpty(text));
            _revealPending = false;
            if (_routine != null) StopCoroutine(_routine);
            if (instant || Group.alpha >= 0.99f)
            {
                Group.alpha = 1f;
                _coveredAt = Time.unscaledTime;
                _routine = null;
                if (!string.IsNullOrEmpty(text)) _routine = StartCoroutine(PopTitle());
                return;
            }
            _routine = StartCoroutine(CoverRoutine(!string.IsNullOrEmpty(text)));
        }

        private IEnumerator CoverRoutine(bool popTitle)
        {
            float from = Group.alpha;
            if (popTitle) content.localScale = Vector3.one * 0.7f;
            yield return UiTween.Run(fadeIn, k => Group.alpha = Mathf.Lerp(from, 1f, k));
            _coveredAt = Time.unscaledTime;
            if (popTitle) yield return PopTitle();
            _routine = null;
            if (_revealPending) Reveal();
        }

        private IEnumerator PopTitle()
        {
            yield return UiTween.Run(0.3f, k => content.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, Anim.OutBack(k)));
        }

        /// <summary>Fades out once covered and shown for <see cref="minShow"/>.</summary>
        public void Reveal()
        {
            if (!gameObject.activeSelf) return;
            if (_routine != null && Group.alpha < 0.99f) { _revealPending = true; return; }
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(RevealRoutine());
        }

        private IEnumerator RevealRoutine()
        {
            bool titled = title.gameObject.activeSelf;
            while (titled && Time.unscaledTime - _coveredAt < minShow) yield return null;
            content.localScale = Vector3.one;
            Group.blocksRaycasts = false;
            yield return UiTween.Run(fadeOut, k =>
            {
                Group.alpha = 1f - k;
                content.localScale = Vector3.one * (1f + 0.15f * k);
            });
            _routine = null;
            gameObject.SetActive(false);
        }
    }
}
