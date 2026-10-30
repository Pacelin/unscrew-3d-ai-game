using System.Collections;
using TMPro;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Dark message pill (Shop.dc.html bottom pill) that fades out ("Not enough coins", "Unlocks at level 5").
    /// Lives in a UiFrame, anchored to its bottom center; the caller gives the bottom offset of the current screen.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class Toast : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private Coroutine _routine;

        /// <param name="bottom">Distance of the pill's bottom edge from the content frame bottom (canvas units).</param>
        public void Show(string message, float bottom, float seconds = UiTokens.Toast.Seconds)
        {
            gameObject.SetActive(true);
            var layer = transform.parent;
            if (layer != null && layer.parent != null) layer.parent.SetAsLastSibling();
            var rt = (RectTransform)transform;
            rt.anchoredPosition = new Vector2(0f, bottom);
            label.text = message;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Routine(seconds));
        }

        private IEnumerator Routine(float seconds)
        {
            var group = GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            yield return UiTween.PopIn(transform, group, 0.2f);
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            yield return UiTween.Fade(group, 1f, 0f, 0.3f);
            gameObject.SetActive(false);
            _routine = null;
        }
    }
}
