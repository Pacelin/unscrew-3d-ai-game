using System.Collections;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// A screen or popup: root with a CanvasGroup (fade) and an optional "Window" child that pops in.
    /// Close fades out (and shrinks the window) before deactivating; <see cref="IsOpen"/> turns false immediately.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPanel : MonoBehaviour
    {
        [SerializeField] protected RectTransform window;
        [SerializeField] private float closeTime = 0.14f;

        private CanvasGroup _group;
        private Coroutine _anim;
        private bool _open;

        public bool IsOpen => _open && gameObject.activeSelf;

        protected CanvasGroup Group => _group != null ? _group : (_group = GetComponent<CanvasGroup>());

        public virtual void Open()
        {
            _open = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Group.interactable = true;
            Group.blocksRaycasts = true;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(window != null ? UiTween.PopIn(window, Group) : UiTween.Fade(Group, Group.alpha < 1f && gameObject.activeSelf ? Group.alpha : 0f, 1f));
        }

        public virtual void Close()
        {
            if (!_open && !gameObject.activeSelf) return;
            bool wasOpen = _open;
            _open = false;
            if (_anim != null) StopCoroutine(_anim);
            _anim = null;
            if (!wasOpen || !gameObject.activeInHierarchy || closeTime <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }
            Group.interactable = false;
            Group.blocksRaycasts = false;
            _anim = StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            float from = Group.alpha;
            yield return UiTween.Run(closeTime, k =>
            {
                Group.alpha = Mathf.Lerp(from, 0f, k);
                if (window != null) window.localScale = Vector3.one * Mathf.Lerp(1f, 0.92f, k);
            });
            _anim = null;
            gameObject.SetActive(false);
        }
    }
}
