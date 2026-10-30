using System.Collections;
using TMPro;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Short message pill that fades out ("Not enough coins", "Unlocks at level 5").</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class Toast : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private Coroutine _routine;

        public void Show(string message, float seconds = 1.6f)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
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
